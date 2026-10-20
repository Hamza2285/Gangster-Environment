using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// A light static prop (sign, pole, street light, barrier, traffic light post) that is knocked over by a vehicle.
    /// - Idle cost is zero: no Update, no Rigidbody. The prop is a plain static collider until a vehicle hits it.
    ///   The colliders must sit on this GameObject (static colliders only report collisions to their own GameObject).
    /// - Break: a vehicle on Vehicle Layers, at least Minimum Vehicle Mass, hitting at Break Speed or faster. A Rigidbody is
    ///   added to the existing mesh (no fracture, no debris spawning), launched with clamped velocity and spin, and the hitting
    ///   vehicle ignores the prop until they no longer overlap and gets back most of the speed the contact took from it
    ///   (it does not stop dead).
    /// - Reset: <see cref="ResetProp"/> removes the Rigidbody and puts the prop back at its rest pose. Called by
    ///   <see cref="BreakablePropManager"/> (distance / cap), which learns about broken props through <see cref="OnBroken"/>.
    /// Only primitive or convex colliders are allowed (a non-convex MeshCollider cannot be on a moving body).
    /// </summary>
    [DisallowMultipleComponent]
    public class BreakableProp : MonoBehaviour
    {
        [Header("Break Condition")]
        [Tooltip("Layers of vehicle colliders that can break this prop.")]
        [SerializeField] private LayerMask vehicleLayers = (1 << 19) | (1 << 13);
        [Tooltip("Impact speed (m/s) needed to break the prop. 5 m/s = 18 km/h.")]
        [SerializeField, Min(0.1f)] private float breakSpeed = 5f;
        [Tooltip("Lighter bodies (characters, ragdolls, other broken props) never break the prop.")]
        [SerializeField, Min(0f)] private float minimumVehicleMass = 200f;

        [Header("Broken Physics")]
        [SerializeField, Min(1f)] private float brokenMass = 60f;
        [Tooltip("Share of the impact speed given to the prop.")]
        [SerializeField, Range(0f, 1.5f)] private float launchSpeedFactor = 0.6f;
        [Tooltip("Upper limit (m/s) of the prop's launch speed, so fast hits never shoot props across the map.")]
        [SerializeField, Min(0f)] private float maxLaunchSpeed = 9f;
        [SerializeField, Min(0f)] private float upwardLaunchSpeed = 1.5f;
        [Tooltip("Tipping spin (rad/s) around the axis across the hit direction.")]
        [SerializeField, Min(0f)] private float tipAngularSpeed = 3f;

        [Header("Vehicle Feel")]
        [Tooltip("Share of the speed the contact took from the vehicle that is given back (1 = no slowdown).")]
        [SerializeField, Range(0f, 1f)] private float vehicleSpeedRetention = 0.85f;
        [Tooltip("Minimum seconds the hitting vehicle passes through the falling prop. Collisions come back only once the " +
                 "prop no longer overlaps the vehicle, so a car that catches up with the prop never gets wedged on it.")]
        [SerializeField, Min(0f)] private float vehicleIgnoreDuration = 0.5f;

        [Header("Setup")]
        [Tooltip("Colliders of this prop (filled by the editor tool; gathered from this GameObject and children when empty).")]
        [SerializeField] private Collider[] propColliders = new Collider[0];

        /// <summary>Raised once when a prop breaks.</summary>
        public static event Action<BreakableProp> OnBroken;
        /// <summary>Raised when a broken prop has been put back.</summary>
        public static event Action<BreakableProp> OnPropReset;

        public bool IsBroken { get; private set; }
        /// <summary>Time.time of the last break.</summary>
        public float BrokenTime { get; private set; }
        /// <summary>World position of the prop before it broke (used for the reset distance).</summary>
        public Vector3 RestPosition { get; private set; }

        private const float MaxVehicleIgnoreTime = 4f;
        private static readonly List<Collider> VehicleColliderBuffer = new List<Collider>(32);

        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private Rigidbody body;
        private Collider[] ignoredVehicleColliders;
        private Coroutine restoreRoutine;

        private void OnCollisionEnter(Collision collision)
        {
            if (IsBroken) return;
            Rigidbody vehicle = collision.rigidbody;
            if (vehicle == null || vehicle.isKinematic || vehicle.mass < minimumVehicleMass) return;
            if (((1 << collision.collider.gameObject.layer) & vehicleLayers) == 0) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < breakSpeed) return;

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Break(vehicle, collision.relativeVelocity, collision.impulse.magnitude, point);
        }

        /// <summary>Knocks the prop over. Direction is taken towards the prop, away from the vehicle.</summary>
        public void Break(Rigidbody vehicle, Vector3 relativeVelocity, float contactImpulse, Vector3 point)
        {
            if (IsBroken) return;
            if (!CacheColliders()) return;

            IsBroken = true;
            BrokenTime = Time.time;
            RestPosition = transform.position;
            restLocalPosition = transform.localPosition;
            restLocalRotation = transform.localRotation;

            Vector3 direction = Vector3.ProjectOnPlane(relativeVelocity, Vector3.up);
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.ProjectOnPlane(transform.position - point, Vector3.up);
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            direction.Normalize();
            if (vehicle != null && Vector3.Dot(direction, point - vehicle.worldCenterOfMass) < 0f) direction = -direction;

            if (vehicle != null)
            {
                RestoreVehicleSpeed(vehicle, direction, contactImpulse, relativeVelocity.magnitude);
                IgnoreVehicle(vehicle);
            }

            body = gameObject.AddComponent<Rigidbody>();
            body.mass = brokenMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            float launch = Mathf.Min(relativeVelocity.magnitude * launchSpeedFactor, maxLaunchSpeed);
            body.linearVelocity = direction * launch + Vector3.up * upwardLaunchSpeed;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * tipAngularSpeed;

            OnBroken?.Invoke(this);
        }

        /// <summary>Puts the prop back at its rest pose as a static collider again.</summary>
        public void ResetProp()
        {
            if (!IsBroken) return;
            if (restoreRoutine != null)
            {
                StopCoroutine(restoreRoutine);
                restoreRoutine = null;
            }
            RestoreVehicleCollisions();
            if (body != null)
            {
                body.isKinematic = true;
                Destroy(body);
                body = null;
            }
            transform.localPosition = restLocalPosition;
            transform.localRotation = restLocalRotation;
            IsBroken = false;
            OnPropReset?.Invoke(this);
        }

        private bool CacheColliders()
        {
            if (propColliders == null || propColliders.Length == 0) propColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < propColliders.Length; i++)
            {
                if (propColliders[i] is MeshCollider mesh && !mesh.convex)
                {
                    Debug.LogWarning($"[{nameof(BreakableProp)}] '{name}' has a non-convex MeshCollider and cannot become dynamic. Use primitive colliders.", this);
                    return false;
                }
            }
            return propColliders.Length > 0;
        }

        // The solver already took speed from the vehicle when this callback runs; give most of it back along the hit direction.
        private void RestoreVehicleSpeed(Rigidbody vehicle, Vector3 direction, float contactImpulse, float impactSpeed)
        {
            float lostSpeed = Mathf.Min(contactImpulse / vehicle.mass, impactSpeed);
            if (lostSpeed <= 0f) return;
            vehicle.linearVelocity += direction * (lostSpeed * vehicleSpeedRetention);
        }

        private void IgnoreVehicle(Rigidbody vehicle)
        {
            if (vehicleIgnoreDuration <= 0f) return;
            VehicleColliderBuffer.Clear();
            vehicle.GetComponentsInChildren(false, VehicleColliderBuffer);
            ignoredVehicleColliders = VehicleColliderBuffer.ToArray();
            SetVehicleIgnored(true);
            restoreRoutine = StartCoroutine(RestoreAfterDelay());
        }

        private IEnumerator RestoreAfterDelay()
        {
            yield return new WaitForSeconds(vehicleIgnoreDuration);
            float waited = vehicleIgnoreDuration;
            while (waited < MaxVehicleIgnoreTime && OverlapsIgnoredVehicle())
            {
                waited += Time.deltaTime;
                yield return null;
            }
            restoreRoutine = null;
            RestoreVehicleCollisions();
        }

        // Cheap bounds test; runs only for a just-broken prop during its ignore window.
        private bool OverlapsIgnoredVehicle()
        {
            for (int v = 0; v < ignoredVehicleColliders.Length; v++)
            {
                Collider vehicleCollider = ignoredVehicleColliders[v];
                if (vehicleCollider == null || vehicleCollider.isTrigger || !vehicleCollider.enabled || vehicleCollider is WheelCollider) continue;
                if (Physics.GetIgnoreLayerCollision(vehicleCollider.gameObject.layer, gameObject.layer)) continue;
                Bounds vehicleBounds = vehicleCollider.bounds;
                for (int p = 0; p < propColliders.Length; p++)
                {
                    if (propColliders[p] != null && propColliders[p].bounds.Intersects(vehicleBounds)) return true;
                }
            }
            return false;
        }

        private void RestoreVehicleCollisions()
        {
            if (ignoredVehicleColliders == null) return;
            SetVehicleIgnored(false);
            ignoredVehicleColliders = null;
        }

        private void SetVehicleIgnored(bool ignore)
        {
            for (int v = 0; v < ignoredVehicleColliders.Length; v++)
            {
                Collider vehicleCollider = ignoredVehicleColliders[v];
                if (vehicleCollider == null || vehicleCollider.isTrigger) continue;
                for (int p = 0; p < propColliders.Length; p++)
                {
                    if (propColliders[p] != null) Physics.IgnoreCollision(propColliders[p], vehicleCollider, ignore);
                }
            }
        }

        private void OnDisable()
        {
            if (restoreRoutine != null) restoreRoutine = null;
            RestoreVehicleCollisions();
        }

#if UNITY_EDITOR
        /// <summary>Editor setup: stores this prop's colliders so nothing is searched at runtime.</summary>
        public void EditorSetColliders(Collider[] colliders) => propColliders = colliders;
#endif
    }
}
