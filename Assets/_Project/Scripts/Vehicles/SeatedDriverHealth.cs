using System;
using System.Collections;
using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Lightweight health of a driver sitting at the wheel as a static seated mesh (traffic cars, stolen cars before the
    /// carjack). There is no character, Animator or ragdoll while seated: a few trigger colliders (<see cref="SeatedDriverHitZone"/>,
    /// children of this object, on the vehicle's kinematic bullet hitbox) take Invector damage and report it here. Invector's
    /// <see cref="vHitDamageParticle"/> spawns the blood. The owner (<see cref="TrafficVehicleAdapter"/> or
    /// <see cref="TrafficVehicleDriver"/>) turns a death into a real Invector ragdoll only when it is needed.
    /// Wounds make the seated mesh flinch (a short coroutine only while it plays; no per-frame cost otherwise).
    /// Deactivate this object while nobody sits at the wheel.
    /// </summary>
    [DisallowMultipleComponent]
    public class SeatedDriverHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Header("Feedback")]
        [Tooltip("Invector hit particle used for the blood (the same effects as the on-foot NPCs). Optional.")]
        [SerializeField] private vHitDamageParticle hitParticle;
        [Tooltip("Seated driver mesh that flinches when he is hit and slumps when he dies. Optional.")]
        [SerializeField] private Transform flinchTarget;
        [Tooltip("Degrees the seated mesh is knocked away from the shot.")]
        [SerializeField, Range(0f, 30f)] private float flinchAngle = 8f;
        [SerializeField, Min(0.05f)] private float flinchDuration = 0.3f;
        [Tooltip("Degrees the seated mesh slumps forward/sideways once dead (until the body is replaced by a ragdoll).")]
        [SerializeField, Range(0f, 45f)] private float slumpAngle = 22f;
        [SerializeField, Min(0.05f)] private float slumpDuration = 0.35f;

        /// <summary>Raised when a hit leaves the driver alive. Args: this, the damage (after the zone multiplier).</summary>
        public event Action<SeatedDriverHealth, vDamage> OnWounded;
        /// <summary>Raised once when the driver dies. Args: this, the killing damage (after the zone multiplier).</summary>
        public event Action<SeatedDriverHealth, vDamage> OnKilled;

        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }
        /// <summary>World direction of the last hit (from the shooter toward the driver, flat).</summary>
        public Vector3 LastHitDirection { get; private set; }

        // Seconds within which the same bullet (vDamage instance) counts as one hit.
        private const float SameShotWindow = 0.5f;
        private vDamage lastSource;
        private float lastSourceTime;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private bool hasRestPose;
        private Coroutine poseRoutine;

        private void Awake()
        {
            CurrentHealth = maxHealth;
            if (flinchTarget != null)
            {
                restLocalPosition = flinchTarget.localPosition;
                restLocalRotation = flinchTarget.localRotation;
                hasRestPose = true;
            }
        }

        private void OnDisable()
        {
            StopPose();
        }

        /// <summary>Full health, alive, seated mesh back in its rest pose. Call whenever a (new) driver takes the seat.</summary>
        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            LastHitDirection = Vector3.zero;
            StopPose();
        }

        /// <summary>
        /// Applies damage from a hit zone (already multiplied by the zone). <paramref name="source"/> identifies the bullet: a
        /// bullet that already hit this driver (e.g. traced on through the window glass, then arriving itself) is not counted twice.
        /// </summary>
        public void ReceiveDamage(vDamage damage, vDamage source = null)
        {
            if (!isActiveAndEnabled || damage == null) return;
            if (source != null)
            {
                if (ReferenceEquals(source, lastSource) && Time.time - lastSourceTime < SameShotWindow) return;
                lastSource = source;
                lastSourceTime = Time.time;
            }

            // A dead body still bleeds when hit (until it is replaced by the ragdoll), but nothing else happens.
            if (hitParticle != null && damage.damageValue > 0f) hitParticle.OnReceiveDamage(damage);
            if (IsDead) return;

            Vector3 direction = damage.sender != null ? damage.hitPosition - damage.sender.position : damage.force;
            direction.y = 0f;
            LastHitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;

            CurrentHealth -= damage.damageValue;
            if (CurrentHealth > 0f)
            {
                PlayPose(flinchAngle, flinchDuration, true);
                OnWounded?.Invoke(this, damage);
                return;
            }

            CurrentHealth = 0f;
            IsDead = true;
            PlayPose(slumpAngle, slumpDuration, false);
            OnKilled?.Invoke(this, damage);
        }

        // Tilts the seated mesh away from the shot around its seat: a flinch eases back, a slump stays.
        private void PlayPose(float angle, float duration, bool returnToRest)
        {
            if (!hasRestPose || angle <= 0f || !isActiveAndEnabled) return;
            StopPose();
            poseRoutine = StartCoroutine(PoseRoutine(angle, duration, returnToRest));
        }

        private IEnumerator PoseRoutine(float angle, float duration, bool returnToRest)
        {
            Transform parent = flinchTarget.parent;
            Vector3 push = LastHitDirection.sqrMagnitude > 0f ? LastHitDirection : (parent != null ? parent.forward : Vector3.forward);
            Vector3 localPush = parent != null ? parent.InverseTransformDirection(push) : push;
            localPush.y = 0f;
            if (localPush.sqrMagnitude < 0.0001f) localPush = Vector3.forward;
            // Tipping along the push: rotate around the horizontal axis perpendicular to it.
            Quaternion tilted = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, localPush.normalized)) * restLocalRotation;

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                // Flinch: fast out, ease back. Slump: ease into the slumped pose.
                float weight = returnToRest ? Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f) : Mathf.SmoothStep(0f, 1f, k);
                flinchTarget.localRotation = Quaternion.Slerp(restLocalRotation, tilted, weight);
                yield return null;
            }
            flinchTarget.localRotation = returnToRest ? restLocalRotation : tilted;
            poseRoutine = null;
        }

        private void StopPose()
        {
            if (poseRoutine != null)
            {
                StopCoroutine(poseRoutine);
                poseRoutine = null;
            }
            if (hasRestPose && flinchTarget != null)
            {
                flinchTarget.localPosition = restLocalPosition;
                flinchTarget.localRotation = restLocalRotation;
            }
        }
    }
}
