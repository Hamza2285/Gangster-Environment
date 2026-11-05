using System;
using System.Collections;
using System.Collections.Generic;
using Invector;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// A light static prop (bench, bin, sign, pole, street light, bollard, hydrant, barrier) that is knocked over GTA-style.
    /// - Idle cost is zero: no Update, no Rigidbody. The prop is a plain static collider until something breaks it.
    ///   The colliders must sit on this GameObject (static colliders only report collisions to their own GameObject).
    /// - Vehicle break: a vehicle on Vehicle Layers, at least Minimum Vehicle Mass, hitting at Break Speed or faster. A Rigidbody
    ///   is added to the existing mesh (no fracture, no debris spawning), launched with clamped velocity and spin, and the hitting
    ///   vehicle ignores the prop until they no longer overlap and gets back most of the speed the contact took from it
    ///   (it does not stop dead).
    /// - Bullet break: Invector projectiles call <see cref="TakeDamage"/>; once Damage To Break is reached the prop is knocked
    ///   along the shot (0 = bullets only hit it). After that Invector's projectile pushes the new Rigidbody itself.
    /// - Cleanup: after Despawn After seconds the fallen prop is hidden (renderers and colliders off, Rigidbody removed) as soon
    ///   as no camera sees it. <see cref="ResetProp"/> removes the Rigidbody and puts the prop back at its rest pose; it is called
    ///   by <see cref="BreakablePropManager"/> (distance / cap), which learns about broken props through <see cref="OnBroken"/>.
    /// Only primitive or convex colliders are allowed (a non-convex MeshCollider cannot be on a moving body).
    /// Breakable props must not be Batching Static (they move); the editor setup clears that flag.
    /// </summary>
    [DisallowMultipleComponent]
    public class BreakableProp : MonoBehaviour, vIDamageReceiver
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

        [Header("Bullets")]
        [Tooltip("Total Invector damage that knocks the prop over (0 = bullets hit the prop but never break it).")]
        [SerializeField, Min(0f)] private float damageToBreak = 40f;
        [Tooltip("Launch speed (m/s) along the shot when bullets break the prop.")]
        [SerializeField, Min(0f)] private float damageLaunchSpeed = 2.5f;

        [Header("Cleanup")]
        [Tooltip("Seconds after breaking before the fallen prop is hidden once no camera sees it (0 = never; it stays until " +
                 "the BreakablePropManager puts it back).")]
        [SerializeField, Min(0f)] private float despawnAfter = 20f;

        [Header("Feedback (optional)")]
        [SerializeField] private AudioClip breakSound;
        [SerializeField, Range(0f, 1f)] private float breakSoundVolume = 0.8f;

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
        /// <summary>True once a broken prop has been hidden by the cleanup.</summary>
        public bool IsDespawned { get; private set; }

        // vIDamageReceiver: created on first use only, so thousands of props do not serialize empty events.
        private OnReceiveDamage startReceiveDamage;
        private OnReceiveDamage receiveDamage;
        public OnReceiveDamage onStartReceiveDamage => startReceiveDamage ??= new OnReceiveDamage();
        public OnReceiveDamage onReceiveDamage => receiveDamage ??= new OnReceiveDamage();

        private const float MaxVehicleIgnoreTime = 4f;
        private static readonly List<Collider> VehicleColliderBuffer = new List<Collider>(32);
        private static readonly WaitForSeconds DespawnCheckWait = new WaitForSeconds(1f);

        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private Rigidbody body;
        private Collider[] ignoredVehicleColliders;
        private Renderer[] propRenderers;
        private bool[] rendererWasEnabled;
        private Coroutine restoreRoutine;
        private Coroutine despawnRoutine;
        private float receivedDamage;

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

            Vector3 direction = FlatDirection(relativeVelocity, point);
            if (vehicle != null && Vector3.Dot(direction, point - vehicle.worldCenterOfMass) < 0f) direction = -direction;
            if (!BeginBreak()) return;

            if (vehicle != null)
            {
                RestoreVehicleSpeed(vehicle, direction, contactImpulse, relativeVelocity.magnitude);
                IgnoreVehicle(vehicle);
            }
            Launch(direction, Mathf.Min(relativeVelocity.magnitude * launchSpeedFactor, maxLaunchSpeed), point);
        }

        /// <summary>Invector damage (bullets, melee). Accumulates until Damage To Break, then knocks the prop along the hit.</summary>
        public void TakeDamage(vDamage damage)
        {
            if (IsBroken || damage == null || damageToBreak <= 0f) return;
            receivedDamage += damage.damageValue;
            if (receivedDamage < damageToBreak) return;

            Vector3 direction = FlatDirection(damage.force, damage.hitPosition);
            if (!BeginBreak()) return;
            Launch(direction, damageLaunchSpeed, damage.hitPosition);
        }

        /// <summary>Puts the prop back at its rest pose as a static collider again.</summary>
        public void ResetProp()
        {
            if (!IsBroken) return;
            StopRoutine(ref restoreRoutine);
            StopRoutine(ref despawnRoutine);
            RestoreVehicleCollisions();
            RemoveBody();
            if (IsDespawned) SetHidden(false);
            transform.localPosition = restLocalPosition;
            transform.localRotation = restLocalRotation;
            IsBroken = false;
            receivedDamage = 0f;
            OnPropReset?.Invoke(this);
        }

        private bool BeginBreak()
        {
            if (!CacheColliders()) return false;
            IsBroken = true;
            IsDespawned = false;
            BrokenTime = Time.time;
            RestPosition = transform.position;
            restLocalPosition = transform.localPosition;
            restLocalRotation = transform.localRotation;
            return true;
        }

        private void Launch(Vector3 direction, float speed, Vector3 point)
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = brokenMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = direction * speed + Vector3.up * upwardLaunchSpeed;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * tipAngularSpeed;

            if (breakSound != null) AudioSource.PlayClipAtPoint(breakSound, point, breakSoundVolume);
            if (despawnAfter > 0f) despawnRoutine = StartCoroutine(DespawnWhenUnseen());
            OnBroken?.Invoke(this);
        }

        private Vector3 FlatDirection(Vector3 push, Vector3 point)
        {
            Vector3 direction = Vector3.ProjectOnPlane(push, Vector3.up);
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.ProjectOnPlane(transform.position - point, Vector3.up);
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            return direction.normalized;
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

        // Runs only for a broken prop: waits, then hides it the first time no camera renders it.
        private IEnumerator DespawnWhenUnseen()
        {
            yield return new WaitForSeconds(despawnAfter);
            if (propRenderers == null) propRenderers = GetComponentsInChildren<Renderer>(true);
            while (AnyRendererVisible()) yield return DespawnCheckWait;
            despawnRoutine = null;
            StopRoutine(ref restoreRoutine);
            RestoreVehicleCollisions();
            RemoveBody();
            SetHidden(true);
        }

        private bool AnyRendererVisible()
        {
            for (int i = 0; i < propRenderers.Length; i++)
                if (propRenderers[i] != null && propRenderers[i].isVisible) return true;
            return false;
        }

        private void SetHidden(bool hidden)
        {
            IsDespawned = hidden;
            if (propRenderers == null) propRenderers = GetComponentsInChildren<Renderer>(true);
            if (hidden)
            {
                rendererWasEnabled = new bool[propRenderers.Length];
                for (int i = 0; i < propRenderers.Length; i++)
                {
                    if (propRenderers[i] == null) continue;
                    rendererWasEnabled[i] = propRenderers[i].enabled;
                    propRenderers[i].enabled = false;
                }
            }
            else if (rendererWasEnabled != null)
            {
                for (int i = 0; i < propRenderers.Length; i++)
                    if (propRenderers[i] != null) propRenderers[i].enabled = rendererWasEnabled[i];
            }
            for (int i = 0; i < propColliders.Length; i++)
                if (propColliders[i] != null) propColliders[i].enabled = !hidden;
        }

        private void RemoveBody()
        {
            if (body == null) return;
            body.isKinematic = true;
            Destroy(body);
            body = null;
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

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null) return;
            StopCoroutine(routine);
            routine = null;
        }

        private void OnDisable()
        {
            // Unity stops this object's coroutines on disable.
            restoreRoutine = null;
            despawnRoutine = null;
            RestoreVehicleCollisions();
        }

#if UNITY_EDITOR
        /// <summary>Editor setup: stores this prop's colliders so nothing is searched at runtime.</summary>
        public void EditorSetColliders(Collider[] colliders) => propColliders = colliders;
#endif
    }
}
