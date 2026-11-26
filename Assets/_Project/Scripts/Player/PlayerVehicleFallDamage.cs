using Invector;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Coming off a vehicle always hurts when the player hits the ground (Jason and Mia; per character):
    /// - bailing out of a moving car / bike, being thrown off or out through the windscreen (PlayerVehicleInteraction's bail
    ///   event): the moment the body (ragdoll) reaches the ground, or the bail's roll on the feet starts;
    /// - falling / jumping off a car's roof or bonnet onto the ground (a drop of at least Min Roof Drop).
    /// Before, a bail below 40 km/h did no damage (the bail's own damage starts there and is dealt at launch), a slow bail
    /// rolled off unhurt, and Invector's fall damage only starts at 6 m, so a car roof never counted.
    /// The damage is scaled by the impact speed, with a minimum high enough to always show (and to bleed through
    /// BloodEffects, damage type "FallDamage"); damage the bail already dealt counts toward it, the landing always adds at
    /// least the minimum. Never lethal below Lethal Speed. Nothing runs per step except reading Invector's ground hit, unless
    /// a fall is being followed.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleFallDamage : MonoBehaviour
    {
        private enum Mode { None, Bail, Roof }

        [Header("References (filled automatically)")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [Tooltip("Landing in water is a splash, not a fall (optional).")]
        [SerializeField] private PlayerSwimController swimController;

        [Header("Layers")]
        [Tooltip("Vehicle colliders (standing on them arms the roof fall; landing back on one is no fall).")]
        [SerializeField] private LayerMask vehicleLayers = (1 << 19) | (1 << 20);
        [Tooltip("What a falling body lands on (the world and vehicles; not people, triggers or the player).")]
        [SerializeField] private LayerMask groundLayers = ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 15) | (1 << 21) | (1 << 30));

        [Header("Damage")]
        [Tooltip("Damage of every landing (at or above BloodEffects' minimum, so it always bleeds).")]
        [SerializeField, Min(1f)] private float minimumDamage = 10f;
        [Tooltip("Extra damage per m/s of impact speed above 3 m/s.")]
        [SerializeField, Min(0f)] private float damagePerSpeed = 1.4f;
        [SerializeField, Min(1f)] private float maximumDamage = 60f;
        [Tooltip("Below this impact speed (m/s) a landing leaves at least 1 health.")]
        [SerializeField, Min(1f)] private float lethalSpeed = 30f;
        [Tooltip("A roof / bonnet fall counts from this drop (m) between the vehicle surface and the ground.")]
        [SerializeField, Min(0f)] private float minRoofDrop = 0.9f;
        [Tooltip("Safety limit (s): a followed fall that never met the ground is resolved after this.")]
        [SerializeField, Min(1f)] private float timeout = 6f;

        /// <summary>A landing from a vehicle hurt the player. Args: damage, impact speed (m/s).</summary>
        public event System.Action<float, float> OnLanded;

        private Mode mode;
        private float armedAt;
        private float healthAtArm;
        private float armSpeed;
        private float lastSpeed;
        private float leaveHeight;
        private bool wasRagdolled;
        private bool onVehicle;
        private float vehicleSurfaceY;
        private Collider lastGround;
        private bool lastGroundIsVehicle;
        private Transform hips;
        private Rigidbody hipsBody;

        private void Reset()
        {
            controller = GetComponent<vThirdPersonController>();
            body = GetComponent<Rigidbody>();
            animator = GetComponent<Animator>();
            vehicleInteraction = GetComponent<PlayerVehicleInteraction>();
            swimController = GetComponent<PlayerSwimController>();
        }

        private void Awake()
        {
            if (controller == null || body == null || animator == null) Reset();
            if (controller == null || body == null || animator == null)
            {
                Debug.LogError($"[{nameof(PlayerVehicleFallDamage)}] Missing Invector controller / Rigidbody / Animator on '{name}'.", this);
                enabled = false;
                return;
            }
            hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : transform;
            hipsBody = hips != null ? hips.GetComponent<Rigidbody>() : null;
        }

        private void OnEnable()
        {
            if (vehicleInteraction != null) vehicleInteraction.OnVehicleBailStarted += HandleBailStarted;
        }

        private void OnDisable()
        {
            if (vehicleInteraction != null) vehicleInteraction.OnVehicleBailStarted -= HandleBailStarted;
            mode = Mode.None;
            onVehicle = false;
        }

        private void HandleBailStarted(OpenworldCrime.Vehicles.IVehicleController vehicle)
        {
            float speed = vehicle != null && (vehicle as Object) != null ? vehicle.CurrentSpeed : 0f;
            Arm(Mode.Bail, speed);
        }

        private void Arm(Mode newMode, float speed)
        {
            mode = newMode;
            armedAt = Time.time;
            healthAtArm = controller.currentHealth;
            armSpeed = speed;
            lastSpeed = speed;
            wasRagdolled = false;
        }

        private void FixedUpdate()
        {
            if (controller.isDead) { mode = Mode.None; onVehicle = false; return; }
            // Came down in the water: the swim controller splashes instead, no ground damage.
            if (swimController != null && swimController.IsSubmerged) { mode = Mode.None; onVehicle = false; return; }
            switch (mode)
            {
                case Mode.None: WatchRoof(); return;
                case Mode.Bail: FollowBail(); return;
                case Mode.Roof: FollowRoofFall(); return;
            }
        }

        // Standing on a vehicle (Invector's own ground hit; no physics query), then in the air: a roof / bonnet fall starts.
        private void WatchRoof()
        {
            if (vehicleInteraction != null && vehicleInteraction.State != VehicleInteractionState.OnFoot) { onVehicle = false; return; }
            if (controller.isGrounded && !controller.ragdolled)
            {
                onVehicle = IsVehicle(controller.groundHit.collider);
                if (onVehicle) vehicleSurfaceY = controller.groundHit.point.y;
                return;
            }
            if (!onVehicle) return;
            onVehicle = false;
            Vector3 v = body.linearVelocity;
            Arm(Mode.Roof, new Vector3(v.x, 0f, v.z).magnitude);
            leaveHeight = vehicleSurfaceY;
        }

        private void FollowRoofFall()
        {
            if (Time.time - armedAt > timeout) { mode = Mode.None; return; }
            if (controller.ragdolled)
            {
                // Knocked into a ragdoll on the way down: follow it like a bail.
                mode = Mode.Bail;
                return;
            }
            if (!controller.isGrounded)
            {
                lastSpeed = body.linearVelocity.magnitude;
                return;
            }
            Collider ground = controller.groundHit.collider;
            // Back on a vehicle (jumped from roof to roof / onto the bonnet) or a small step: no fall.
            if (IsVehicle(ground) || leaveHeight - controller.groundHit.point.y < minRoofDrop)
            {
                mode = Mode.None;
                onVehicle = IsVehicle(ground);
                return;
            }
            Land(Mathf.Max(lastSpeed, armSpeed));
        }

        private void FollowBail()
        {
            float t = Time.time - armedAt;
            if (controller.ragdolled)
            {
                wasRagdolled = true;
                if (hipsBody != null) lastSpeed = hipsBody.linearVelocity.magnitude;
                // Still diving out of the seat (the first moment) or high in the air: wait for the ground.
                if (t < 0.15f) return;
                if (Physics.Raycast(hips.position + Vector3.up * 0.1f, Vector3.down, 0.5f, groundLayers, QueryTriggerInteraction.Ignore)
                    && (hipsBody == null || hipsBody.linearVelocity.y < 0.5f))
                {
                    Land(Mathf.Max(lastSpeed, armSpeed * 0.6f));
                }
                else if (t > timeout) Land(armSpeed);
                return;
            }
            // The bail's roll on the feet: it starts on the ground.
            if (controller.isRolling && t > 0.1f) { Land(Mathf.Max(armSpeed, body.linearVelocity.magnitude)); return; }
            // Back on the feet without a landing being seen (ragdoll ended, bail finished): it still hurt.
            bool bailing = vehicleInteraction != null && vehicleInteraction.State == VehicleInteractionState.Bailing;
            if ((wasRagdolled && !bailing) || (!bailing && t > 0.5f && controller.isGrounded) || t > timeout) Land(armSpeed);
        }

        private void Land(float impactSpeed)
        {
            mode = Mode.None;
            onVehicle = false;
            if (controller.isDead) return;
            float wanted = Mathf.Clamp(minimumDamage + Mathf.Max(0f, impactSpeed - 3f) * damagePerSpeed, minimumDamage, maximumDamage);
            float alreadyTaken = Mathf.Max(0f, healthAtArm - controller.currentHealth);
            float damage = Mathf.Max(minimumDamage, wanted - alreadyTaken);
            if (impactSpeed < lethalSpeed) damage = Mathf.Min(damage, controller.currentHealth - 1f);
            damage = Mathf.Round(damage);
            if (damage < 1f) return;
            Vector3 point = hips != null ? hips.position : transform.position;
            // The bail's landing is a roll, and Invector ignores all damage while rolling (dodge roll): not for this hit.
            bool rollImmunity = controller.noDamageWhileRolling;
            controller.noDamageWhileRolling = false;
            controller.TakeDamage(new vDamage
            {
                damageValue = damage,
                sender = transform,
                receiver = transform,
                hitPosition = point,
                force = Vector3.up * 2f,
                damageType = "FallDamage",
                ignoreDefense = true,
                activeRagdoll = false,
                hitReaction = false
            });
            controller.noDamageWhileRolling = rollImmunity;
            OnLanded?.Invoke(damage, impactSpeed);
        }

        private bool IsVehicle(Collider c)
        {
            if (c == null) return false;
            if (c == lastGround) return lastGroundIsVehicle;
            lastGround = c;
            lastGroundIsVehicle = (vehicleLayers.value & (1 << c.gameObject.layer)) != 0 && c.attachedRigidbody != null;
            return lastGroundIsVehicle;
        }
    }
}
