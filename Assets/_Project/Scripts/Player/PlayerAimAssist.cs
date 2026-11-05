using System.Collections;
using Invector;
using Invector.vCamera;
using OpenworldCrime.Controls;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// GTA-style touch aim assist: when aiming starts on a touch device the camera eases onto the pedestrian or enemy nearest to the
    /// crosshair. It searches once per aim press, never per frame, and only turns the camera; shots still go where the crosshair is.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAimAssist : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;
        [SerializeField] private Camera playerCamera;
        [Tooltip("The assist only runs in its mobile mode.")]
        [SerializeField] private ControlsModeManager controlsMode;

        [Header("Assist")]
        [SerializeField, Min(1f)] private float range = 35f;
        [Tooltip("Largest angle (degrees) between the crosshair and a target that still pulls the camera.")]
        [SerializeField, Range(1f, 30f)] private float maxAngle = 12f;
        [Tooltip("Long enough to outlast the camera's move to the Aiming state, which shifts the view while the assist turns it.")]
        [SerializeField, Min(0.01f)] private float snapTime = 0.35f;
        [Tooltip("Layers of the target body parts (pedestrians, enemies). Far pedestrians only have a trigger hitbox, so triggers count.")]
        [SerializeField] private LayerMask targetLayers;
        [Tooltip("Layers that block the line of sight to a target (not the target layers).")]
        [SerializeField] private LayerMask lineOfSightLayers = 1;

        private readonly Collider[] overlapResults = new Collider[64];
        private Coroutine snapRoutine;

        private void Awake()
        {
            if (combat == null || thirdPersonCamera == null || playerCamera == null || controlsMode == null)
            {
                Debug.LogError($"[{nameof(PlayerAimAssist)}] Missing references on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (combat != null) combat.OnAimChanged += HandleAimChanged;
        }

        private void OnDisable()
        {
            if (combat != null) combat.OnAimChanged -= HandleAimChanged;
            StopSnap();
        }

        private void HandleAimChanged(bool aiming)
        {
            StopSnap();
            if (!aiming || !combat.IsAiming || !controlsMode.IsMobile) return;
            Collider target = FindTarget();
            if (target != null) snapRoutine = StartCoroutine(SnapTo(target));
        }

        private Collider FindTarget()
        {
            Transform view = playerCamera.transform;
            int count = Physics.OverlapSphereNonAlloc(view.position + view.forward * range * 0.5f, range * 0.5f, overlapResults, targetLayers, QueryTriggerInteraction.Collide);
            Collider best = null;
            float bestAngle = maxAngle;
            for (int i = 0; i < count; i++)
            {
                Collider part = overlapResults[i];
                if (part.transform.IsChildOf(transform) || part is SphereCollider) continue; // own body; head spheres would hand out headshots
                Vector3 point = part.bounds.center;
                float angle = Vector3.Angle(view.forward, point - view.position);
                if (angle >= bestAngle) continue;
                vIHealthController health = part.GetComponentInParent<vIHealthController>();
                if (health == null || health.isDead) continue;
                // The target's own solid capsule on the way to its hitbox does not count as cover.
                if (Physics.Linecast(view.position, point, out RaycastHit blocker, lineOfSightLayers, QueryTriggerInteraction.Ignore)
                    && blocker.collider.GetComponentInParent<vIHealthController>() != health) continue;
                best = part;
                bestAngle = angle;
            }
            return best;
        }

        private IEnumerator SnapTo(Collider target)
        {
            for (float elapsed = 0f; elapsed < snapTime && target != null; elapsed += Time.unscaledDeltaTime)
            {
                Vector3 toTarget = target.bounds.center - playerCamera.transform.position;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Vector3 euler = Quaternion.LookRotation(toTarget).eulerAngles;
                    float weight = Mathf.Clamp01(Time.unscaledDeltaTime / Mathf.Max(0.01f, snapTime - elapsed));
                    thirdPersonCamera.mouseX = Mathf.LerpAngle(thirdPersonCamera.mouseX, euler.y, weight);
                    thirdPersonCamera.mouseY = Mathf.LerpAngle(thirdPersonCamera.mouseY, euler.x, weight);
                }
                yield return null;
            }
            snapRoutine = null;
        }

        private void StopSnap()
        {
            if (snapRoutine != null) StopCoroutine(snapRoutine);
            snapRoutine = null;
        }
    }
}
