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
    /// Any bullet that reaches him kills him (health 1). He slumps about his hips (a short coroutine; no per-frame cost otherwise).
    /// Deactivate this object while nobody sits at the wheel.
    /// </summary>
    [DisallowMultipleComponent]
    public class SeatedDriverHealth : MonoBehaviour
    {
        [Header("Health")]
        [Tooltip("1 = any bullet that reaches the driver kills him, whatever the weapon.")]
        [SerializeField, Min(1f)] private float maxHealth = 1f;

        [Header("Hit Zones")]
        [Tooltip("Sphere around the head; placed on the seated driver each time one takes the seat.")]
        [SerializeField] private Transform headZone;
        [Tooltip("Capsule from the hips to the neck; placed on the seated driver each time one takes the seat.")]
        [SerializeField] private Transform torsoZone;

        [Header("Feedback")]
        [Tooltip("Invector hit particle used for the blood (the same effects as the on-foot NPCs). Optional.")]
        [SerializeField] private vHitDamageParticle hitParticle;
        [Tooltip("Seated driver mesh that flinches when he is hit and slumps when he dies. Optional.")]
        [SerializeField] private Transform flinchTarget;
        [Tooltip("Degrees the seated mesh is knocked away from the shot.")]
        [SerializeField, Range(0f, 30f)] private float flinchAngle = 8f;
        [SerializeField, Min(0.05f)] private float flinchDuration = 0.3f;
        [Tooltip("Degrees the seated mesh slumps forward/sideways once dead (until the body is replaced by a ragdoll).")]
        [SerializeField, Range(0f, 45f)] private float slumpAngle = 18f;
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
        private Vector3 poseHips;
        private bool hasRestPose;
        private Coroutine poseRoutine;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        /// <summary>
        /// A driver took the seat: remembers his seated pose as the one to return to and puts the hit zones on him. Call after
        /// the mesh was posed; each driver sits differently, so a pose kept from an earlier driver would lift this one through the roof.
        /// </summary>
        public void FitToDriver(Vector3 hipsInMesh, Vector3 headInMesh)
        {
            StopPose();
            if (flinchTarget == null) return;
            restLocalPosition = flinchTarget.localPosition;
            restLocalRotation = flinchTarget.localRotation;
            poseHips = hipsInMesh;
            hasRestPose = true;

            Vector3 head = flinchTarget.TransformPoint(headInMesh);
            Vector3 hips = flinchTarget.TransformPoint(hipsInMesh);
            if (headZone != null) headZone.position = head;
            if (torsoZone == null) return;
            Vector3 up = head - hips;
            torsoZone.SetPositionAndRotation(hips + up * 0.45f, Quaternion.FromToRotation(Vector3.up, up));
            if (torsoZone.TryGetComponent(out CapsuleCollider capsule)) capsule.height = (up.magnitude + 0.1f) / Mathf.Max(1e-4f, torsoZone.lossyScale.y);
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
            // Tipping along the push: rotate around the horizontal axis perpendicular to it, pivoting on the hips (which stay on the seat).
            Quaternion tilted = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, localPush.normalized)) * restLocalRotation;
            Vector3 hips = restLocalPosition + restLocalRotation * poseHips;

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                // Flinch: fast out, ease back. Slump: ease into the slumped pose.
                float weight = returnToRest ? Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f) : Mathf.SmoothStep(0f, 1f, k);
                Quaternion rotation = Quaternion.Slerp(restLocalRotation, tilted, weight);
                flinchTarget.SetLocalPositionAndRotation(hips - rotation * poseHips, rotation);
                yield return null;
            }
            Quaternion end = returnToRest ? restLocalRotation : tilted;
            flinchTarget.SetLocalPositionAndRotation(hips - end * poseHips, end);
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
