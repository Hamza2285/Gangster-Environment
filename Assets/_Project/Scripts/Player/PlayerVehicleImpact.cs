using System;
using System.Collections.Generic;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Makes the player react to being hit by a vehicle (<see cref="VehicleCharacterImpact"/> reports every car's contact here): slow
    /// bumps do nothing, light hits hurt, faster ones knock him down through Invector's own ragdoll, thrown along the car's direction,
    /// and Invector gets him back up. Damage goes through Invector's health, so the HUD, death and PlayerHealth events all follow.
    /// Nothing runs per frame. Tuned on this component; ignored while he is in a vehicle.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleImpact : MonoBehaviour, IVehicleImpactReceiver
    {
        [Header("References")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vRagdoll ragdoll;
        [SerializeField] private PlayerManager playerManager;

        [Header("Impact Speeds (m/s)")]
        [Tooltip("Slower contacts are ignored (walking into a slow car).")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 2f;
        [Tooltip("From this speed he is knocked down (ragdoll).")]
        [SerializeField, Min(0f)] private float knockDownSpeed = 5f;
        [Tooltip("Speed at which damage and time on the ground reach their high values.")]
        [SerializeField, Min(0f)] private float highImpactSpeed = 16f;

        [Header("Damage")]
        [Tooltip("Damage at the minimum impact speed.")]
        [SerializeField, Min(0f)] private float minimumDamage = 3f;
        [Tooltip("Damage at High Impact Speed and above.")]
        [SerializeField, Min(0f)] private float highImpactDamage = 55f;

        [Header("Throw")]
        [Tooltip("Share of the car's velocity given to the body.")]
        [SerializeField, Min(0f)] private float throwSpeedShare = 0.7f;
        [SerializeField, Min(0f)] private float maxThrowSpeed = 9f;
        [SerializeField, Min(0f)] private float liftPerSpeed = 0.18f;
        [SerializeField, Min(0f)] private float maxLift = 3.5f;

        [Header("Recovery")]
        [SerializeField, Min(0f)] private float minimumGroundTime = 1.5f;
        [SerializeField, Min(0f)] private float highImpactGroundTime = 3f;
        [Tooltip("Seconds in which further vehicle hits are ignored (one reaction per bump).")]
        [SerializeField, Min(0f)] private float hitCooldown = 1f;

        /// <summary>Raised when a vehicle really hit the player (after the cooldown and speed checks). Arg: the impact.</summary>
        public static event Action<VehicleImpact> OnPlayerHit;

        private readonly List<Rigidbody> bones = new List<Rigidbody>();
        private readonly List<Collider> boneColliders = new List<Collider>();
        private float lastHitTime = -100f;
        private bool valid;

        private void Awake()
        {
            valid = controller != null && ragdoll != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(PlayerVehicleImpact)}] Missing vThirdPersonController or vRagdoll on '{name}'. Cars will not hurt the player.", this);
                return;
            }
            Rigidbody root = GetComponent<Rigidbody>();
            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == root || body.GetComponent<vDamageReceiver>() == null) continue;
                bones.Add(body);
                if (body.TryGetComponent(out Collider c)) boneColliders.Add(c);
            }
        }

        public void ReceiveVehicleImpact(VehicleImpact impact)
        {
            if (!valid || !isActiveAndEnabled || controller.isDead || Time.time - lastHitTime < hitCooldown) return;
            if (playerManager != null && playerManager.IsInVehicle) return;

            // The car's own speed: he is a light dynamic body the solver has already carried along, and him running into a
            // parked car must not count.
            Vector3 flat = impact.VehicleVelocity;
            flat.y = 0f;
            float speed = flat.magnitude;
            if (speed < minimumImpactSpeed) return;
            lastHitTime = Time.time;

            float high = Mathf.InverseLerp(minimumImpactSpeed, highImpactSpeed, speed);
            bool knockDown = speed >= knockDownSpeed;
            controller.TakeDamage(new vDamage
            {
                damageValue = Mathf.Round(Mathf.Lerp(minimumDamage, highImpactDamage, high)),
                sender = impact.Vehicle,
                hitPosition = impact.Point,
                force = flat * throwSpeedShare,
                damageType = "Vehicle",
                ignoreDefense = true,
                activeRagdoll = knockDown,
                hitReaction = !knockDown,
                senselessTime = knockDown ? Mathf.Lerp(minimumGroundTime, highImpactGroundTime, high) : 0f
            });

            if (knockDown && ragdoll.isActive)
            {
                // The limbs start inside the bumper: the car ignores them for a moment (no depenetration launch).
                if (impact.Source != null) impact.Source.IgnoreCollidersTemporarily(boneColliders);
                Vector3 velocity = Vector3.ClampMagnitude(flat * throwSpeedShare, maxThrowSpeed) + Vector3.up * Mathf.Min(speed * liftPerSpeed, maxLift);
                for (int i = 0; i < bones.Count; i++)
                    if (bones[i] != null) bones[i].AddForce(velocity, ForceMode.VelocityChange);
            }
            OnPlayerHit?.Invoke(impact);
        }

#if UNITY_EDITOR
        public void EditorSetup(vThirdPersonController thirdPerson, vRagdoll bodyRagdoll, PlayerManager manager)
        {
            controller = thirdPerson;
            ragdoll = bodyRagdoll;
            playerManager = manager;
        }
#endif
    }
}
