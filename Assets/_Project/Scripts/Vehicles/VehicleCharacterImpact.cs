using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Reports vehicle hits to <see cref="IVehicleImpactReceiver"/> objects (e.g. enemy characters) and gives the hit its feel.
    /// Lives on the vehicle root next to its Rigidbody (player car, stolen cars and traffic cars alike).
    /// - Impact velocity = the vehicle's point velocity from the physics step before the contact (by the time OnCollisionEnter
    ///   runs the solver has already slowed the car), minus the hit object's own velocity. A parked car being walked into reports nothing.
    /// - A living character's body is a kinematic capsule, which the solver treats as an immovable wall: the car would stop
    ///   dead and bounce back. From Pass-Through Minimum Speed that body is ignored by this vehicle for a moment and the speed
    ///   the solver took away is given back, so the character reacts (stumble / ragdoll) while the car barely slows.
    ///   Ragdoll limbs are real rigidbodies and stay fully physical (bumps when driving over a body), except right after a
    ///   knock-down: the receiver hands its fresh limbs to <see cref="IgnoreCollidersTemporarily"/>, so a ragdoll that starts
    ///   inside the bumper is not shot out by depenetration and does not stop the car (no physics explosions, no getting stuck).
    /// - Optional feedback: an impact sound on this vehicle and <see cref="OnCharacterHit"/> (e.g. a camera shake).
    /// How the character reacts is up to the receiver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleCharacterImpact : MonoBehaviour
    {
        [Header("Receivers")]
        [Tooltip("Layers of objects that can receive vehicle impacts (characters). Keeps ground/building contacts cheap.")]
        [SerializeField] private LayerMask receiverLayers = ~0;
        [Tooltip("Contacts slower than this (m/s) are ignored.")]
        [SerializeField, Min(0f)] private float minimumContactSpeed = 0.5f;

        [Header("Pass-Through (living characters)")]
        [Tooltip("From this speed (m/s) a character's solid (kinematic) body stops blocking the vehicle. Slower contacts stay fully physical.")]
        [SerializeField, Min(0f)] private float passThroughMinimumSpeed = 0.6f;
        [Tooltip("Seconds the hit body is ignored by this vehicle (longer while it still overlaps the vehicle).")]
        [SerializeField, Min(0.05f)] private float passThroughDuration = 0.75f;
        [Tooltip("Share of the speed the solid body took from the vehicle that is given back (1 = no slowdown).")]
        [SerializeField, Range(0f, 1f)] private float characterHitSpeedRetention = 1f;

        [Header("Fresh Ragdolls")]
        [Tooltip("Seconds a just knocked-down ragdoll ignores this vehicle (longer while its limbs still overlap the vehicle).")]
        [SerializeField, Min(0f)] private float ragdollIgnoreDuration = 0.6f;
        [Tooltip("Safety limit (s) for keeping a still-overlapping ragdoll ignored.")]
        [SerializeField, Min(0.1f)] private float ragdollIgnoreMaxDuration = 2.5f;

        [Header("Feedback (optional)")]
        [Tooltip("Plays the hit sound. Leave empty for no sound (e.g. traffic cars).")]
        [SerializeField] private AudioSource hitAudioSource;
        [SerializeField] private AudioClip[] hitClips = new AudioClip[0];
        [Tooltip("Hits from this speed (m/s) give feedback.")]
        [SerializeField, Min(0f)] private float feedbackMinimumSpeed = 2f;
        [Tooltip("Speed (m/s) at which the hit sound reaches full volume.")]
        [SerializeField, Min(0.1f)] private float fullVolumeSpeed = 10f;
        [SerializeField, Range(0f, 1f)] private float minimumVolume = 0.35f;
        [SerializeField, Min(0f)] private float feedbackCooldown = 0.3f;

        /// <summary>Raised for character hits at or above the feedback speed (any vehicle; listeners filter by <see cref="VehicleImpact.Vehicle"/>).</summary>
        public static event Action<VehicleImpact> OnCharacterHit;

        private const float MaxOverlapWait = 3f;

        private readonly List<Collider> vehicleColliders = new List<Collider>();
        private readonly HashSet<Collider> ignoredBodies = new HashSet<Collider>();
        private readonly HashSet<Collider> ignoredLimbs = new HashSet<Collider>();
        private Rigidbody body;
        private Vector3 previousVelocity;
        private Vector3 previousAngularVelocity;
        private Vector3 previousCenterOfMass;
        private float lastFeedbackTime = -100f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                if (!c.isTrigger) vehicleColliders.Add(c);
            }
        }

        private void OnDisable()
        {
            // Coroutines stop with the component; drop the bookkeeping (Unity resets ignore pairs of re-enabled colliders).
            ignoredBodies.Clear();
            ignoredLimbs.Clear();
        }

        // Runs before the physics step, so these are the velocities the car had going into a collision.
        private void FixedUpdate()
        {
            previousVelocity = body.linearVelocity;
            previousAngularVelocity = body.angularVelocity;
            previousCenterOfMass = body.worldCenterOfMass;
        }

        private void OnCollisionEnter(Collision collision) => HandleContact(collision, true);

        // Stay covers a car slowly pushing a character; receivers apply their own cooldown.
        private void OnCollisionStay(Collision collision) => HandleContact(collision, false);

        private void HandleContact(Collision collision, bool isFirstContact)
        {
            if (((1 << collision.gameObject.layer) & receiverLayers) == 0 || collision.contactCount == 0) return;

            Vector3 point = collision.GetContact(0).point;
            // Point velocity from the pre-contact state; fall back to the current one if the car is still pushing harder.
            Vector3 before = previousVelocity + Vector3.Cross(previousAngularVelocity, point - previousCenterOfMass);
            Vector3 now = body.GetPointVelocity(point);
            Vector3 velocity = before.sqrMagnitude >= now.sqrMagnitude ? before : now;

            Rigidbody other = collision.rigidbody;
            bool solidBody = other == null || other.isKinematic;
            if (!solidBody) velocity -= other.GetPointVelocity(point);
            float speed = velocity.magnitude;
            if (speed < minimumContactSpeed) return;

            IVehicleImpactReceiver receiver = collision.collider.GetComponentInParent<IVehicleImpactReceiver>();
            if (receiver == null) return;

            if (solidBody && speed >= passThroughMinimumSpeed && !body.isKinematic)
            {
                IgnoreBody(collision.collider);
                body.linearVelocity = Vector3.Lerp(body.linearVelocity, previousVelocity, characterHitSpeedRetention);
                body.angularVelocity = Vector3.Lerp(body.angularVelocity, previousAngularVelocity, characterHitSpeedRetention);
            }

            var impact = new VehicleImpact(velocity, point, body.mass, transform, this);
            receiver.ReceiveVehicleImpact(impact);

            if ((isFirstContact || solidBody) && speed >= feedbackMinimumSpeed && Time.time - lastFeedbackTime >= feedbackCooldown)
            {
                lastFeedbackTime = Time.time;
                PlayHitSound(speed);
                OnCharacterHit?.Invoke(impact);
            }
        }

        private void PlayHitSound(float speed)
        {
            if (hitAudioSource == null || hitClips.Length == 0) return;
            AudioClip clip = hitClips[UnityEngine.Random.Range(0, hitClips.Length)];
            if (clip == null) return;
            float volume = Mathf.Lerp(minimumVolume, 1f, Mathf.InverseLerp(feedbackMinimumSpeed, fullVolumeSpeed, speed));
            hitAudioSource.PlayOneShot(clip, volume);
        }

        private void IgnoreBody(Collider hitBody)
        {
            if (!ignoredBodies.Add(hitBody)) return;
            SetIgnored(hitBody, true);
            StartCoroutine(RestoreBodyCollision(hitBody));
        }

        // Collisions come back after the pass-through time, but never while the body still overlaps the vehicle (it would pop out).
        private IEnumerator RestoreBodyCollision(Collider hitBody)
        {
            yield return new WaitForSeconds(passThroughDuration);
            float waited = 0f;
            while (hitBody != null && hitBody.enabled && waited < MaxOverlapWait && OverlapsVehicle(hitBody))
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (hitBody != null) SetIgnored(hitBody, false);
            ignoredBodies.Remove(hitBody);
        }

        /// <summary>
        /// Makes this vehicle ignore the given colliders (e.g. a ragdoll that was just knocked down by it) for
        /// Ragdoll Ignore Duration, and for as long as any of them still overlaps the vehicle (up to the safety limit).
        /// Called by impact receivers; costs nothing per frame once the window has passed.
        /// </summary>
        public void IgnoreCollidersTemporarily(IReadOnlyList<Collider> colliders)
        {
            if (!isActiveAndEnabled || colliders == null || colliders.Count == 0 || ragdollIgnoreDuration <= 0f) return;
            var batch = new List<Collider>(colliders.Count);
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider c = colliders[i];
                if (c == null || !ignoredLimbs.Add(c)) continue;
                SetIgnored(c, true);
                batch.Add(c);
            }
            if (batch.Count > 0) StartCoroutine(RestoreLimbCollision(batch));
        }

        private IEnumerator RestoreLimbCollision(List<Collider> limbs)
        {
            yield return new WaitForSeconds(ragdollIgnoreDuration);
            float waited = ragdollIgnoreDuration;
            while (waited < ragdollIgnoreMaxDuration && AnyOverlapsVehicle(limbs))
            {
                waited += Time.deltaTime;
                yield return null;
            }
            for (int i = 0; i < limbs.Count; i++)
            {
                Collider c = limbs[i];
                if (c != null) SetIgnored(c, false);
                ignoredLimbs.Remove(c);
            }
        }

        private bool AnyOverlapsVehicle(List<Collider> limbs)
        {
            for (int i = 0; i < limbs.Count; i++)
            {
                Collider c = limbs[i];
                if (c != null && c.enabled && OverlapsVehicle(c)) return true;
            }
            return false;
        }

        private bool OverlapsVehicle(Collider hitBody)
        {
            Bounds bodyBounds = hitBody.bounds;
            for (int i = 0; i < vehicleColliders.Count; i++)
            {
                Collider c = vehicleColliders[i];
                if (c != null && c.enabled && !(c is WheelCollider) && c.bounds.Intersects(bodyBounds)) return true;
            }
            return false;
        }

        private void SetIgnored(Collider hitBody, bool ignore)
        {
            for (int i = 0; i < vehicleColliders.Count; i++)
            {
                if (vehicleColliders[i] != null) Physics.IgnoreCollision(hitBody, vehicleColliders[i], ignore);
            }
        }
    }
}
