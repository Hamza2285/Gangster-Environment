using System;
using System.Collections;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Gives a pooled street pedestrian the same hit system as the enemy dummies:
    /// - Health and damage are Invector's (vCharacter on the root, vDamageReceiver on every ragdoll bone, vHitDamageParticle),
    ///   so the player's guns damage pedestrians exactly like enemies (headshot multiplier on the head bone).
    /// - Vehicle hits arrive through <see cref="IVehicleImpactReceiver"/> (sent by <see cref="VehicleCharacterImpact"/> on every
    ///   car) and are scaled by impact speed like EnemyVehicleImpact: slow bumps hurt a little, faster hits knock the pedestrian
    ///   down as a ragdoll thrown ahead of the car (legs swept, upper body tumbling back), survivors get back up.
    /// - Death switches to the ragdoll and pushes the hit limb with the killing damage, like EnemyDummy.
    /// - Smooth car hits: the vehicle that knocks the body down ignores the fresh limbs for a moment (no depenetration launch, the
    ///   car keeps its momentum), the throw and lift are clamped, the flight is damped like the enemies' (EnemyVehicleImpact), and a
    ///   car touching a body already on the ground only carries it up to a share of its own speed (never adds up per contact).
    /// The walker reacts to <see cref="OnKnockedDown"/> / <see cref="OnRecovered"/> / <see cref="OnDied"/> (leaves its lane or the
    /// panic NavMesh) and calls <see cref="ResetForReuse"/> when the spawner recycles it.
    /// No Update: everything is event driven; coroutines only run while knocked down.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianDamage : MonoBehaviour, IVehicleImpactReceiver
    {
        [Header("References")]
        [SerializeField] private vCharacter character;
        [SerializeField] private PedestrianRagdoll ragdoll;
        [Tooltip("Root capsule used while alive (blocks the player and cars). Disabled while ragdolled.")]
        [SerializeField] private Collider aliveCollider;

        [Header("Vehicle Impact Speeds (m/s)")]
        [Tooltip("Slower hits are ignored.")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 0.6f;
        [Tooltip("From this speed the pedestrian is knocked down (ragdoll). Slower hits only hurt and scare.")]
        [SerializeField, Min(0f)] private float ragdollImpactSpeed = 2.5f;
        [Tooltip("Speed at which damage, lift and time on the ground reach their high-impact values.")]
        [SerializeField, Min(0f)] private float highImpactSpeed = 12f;

        [Header("Vehicle Damage")]
        [Tooltip("Damage per m/s of impact speed for light bumps.")]
        [SerializeField, Min(0f)] private float lightDamagePerSpeed = 1f;
        [Tooltip("Damage per m/s of impact speed for knock-downs (ramped up to x High Impact Damage Multiplier).")]
        [SerializeField, Min(0f)] private float damageMultiplier = 5f;
        [SerializeField, Min(1f)] private float highImpactDamageMultiplier = 1.5f;

        [Header("Vehicle Throw")]
        [Tooltip("Throw velocity as a fraction of the car's impact velocity at High Impact Speed.")]
        [SerializeField, Min(0f)] private float ragdollForceMultiplier = 0.8f;
        [Tooltip("Same fraction at Ragdoll Impact Speed (below 1 the lifted body rolls onto the hood).")]
        [SerializeField, Min(0f)] private float lowSpeedForceMultiplier = 0.65f;
        [SerializeField, Min(0f)] private float maxThrowSpeed = 9f;
        [Tooltip("Upward velocity per m/s of impact speed.")]
        [SerializeField, Min(0f)] private float liftPerSpeed = 0.16f;
        [SerializeField, Min(0f)] private float minimumLift = 1.2f;
        [Tooltip("Upper limit (m/s) of the upward throw velocity, so fast hits roll the body over the hood instead of launching it.")]
        [SerializeField, Min(0f)] private float maxLift = 3.5f;
        [Tooltip("Share of the throw kept by body parts high above the hips (head, arms).")]
        [SerializeField, Range(0f, 1f)] private float upperBodyVelocityFactor = 0.7f;
        [Tooltip("Share of the throw given to the legs (the bumper sweeps them).")]
        [SerializeField, Range(0f, 2f)] private float legVelocityFactor = 1.1f;
        [SerializeField, Min(0.1f)] private float upperBodyFalloffDistance = 0.8f;
        [Tooltip("Tumble (rad/s) per m/s of impact speed.")]
        [SerializeField, Min(0f)] private float tumblePerSpeed = 0.25f;
        [SerializeField, Min(0f)] private float maxTumble = 4f;

        [Header("Flight Damping")]
        [Tooltip("Linear damping on the ragdoll right after a vehicle knock-down (air and ground friction feel; stops fast hits sending the body far).")]
        [SerializeField, Min(0f)] private float knockDownLinearDamping = 0.8f;
        [SerializeField, Min(0f)] private float knockDownAngularDamping = 1.5f;
        [Tooltip("Seconds the extra damping lasts before the ragdoll's own values are restored.")]
        [SerializeField, Min(0f)] private float knockDownDampingDuration = 2.5f;

        [Header("Body On The Ground")]
        [Tooltip("A car touching a body that is already down carries it along at most at this share of the car's speed.")]
        [SerializeField, Range(0f, 1f)] private float groundShoveSpeedShare = 0.4f;

        [Header("Death Impulse")]
        [Tooltip("Impulse on the hit limb on death, as a multiple of the killing vDamage.force (Invector's bullet also pushes it once).")]
        [SerializeField, Min(0f)] private float deathForceMultiplier = 1f;
        [Tooltip("Impulse used when the killing damage carries no force. Applied along sender -> pedestrian.")]
        [SerializeField, Min(0f)] private float fallbackDeathImpulse = 20f;

        [Header("Recovery")]
        [Tooltip("Minimum seconds a knocked-down survivor stays on the ground.")]
        [SerializeField, Min(0f)] private float minimumGroundTime = 2.5f;
        [Tooltip("Extra ground time reached at High Impact Speed.")]
        [SerializeField, Min(0f)] private float highImpactExtraGroundTime = 1.5f;
        [Tooltip("The body has to be slower than this (m/s) before getting up.")]
        [SerializeField, Min(0f)] private float settledSpeed = 0.25f;
        [Tooltip("Safety limit: get up after this many seconds even if the body keeps moving.")]
        [SerializeField, Min(1f)] private float maxGroundTime = 10f;
        [Tooltip("Seconds during which further vehicle hits are ignored (one reaction per bump).")]
        [SerializeField, Min(0f)] private float hitCooldown = 0.35f;

        /// <summary>Raised when the pedestrian is knocked to the ground (alive). Arg: what hit it.</summary>
        public event Action<Vector3> OnKnockedDown;
        /// <summary>Raised when a knocked-down survivor is back on its feet. Arg: the threat to run from.</summary>
        public event Action<Vector3> OnRecovered;
        /// <summary>Raised once when the pedestrian dies. Arg: where the killing damage came from.</summary>
        public event Action<Vector3> OnDied;
        /// <summary>Raised when the pedestrian takes damage and survives (e.g. shot or bumped). Arg: damage source position.</summary>
        public event Action<Vector3> OnHurt;

        public bool IsDead => character == null || character.isDead;
        public bool IsDown => ragdoll != null && (ragdoll.IsActive || ragdoll.IsFrozen);
        /// <summary>World position of the body (ragdoll hips; they can be thrown away from the root).</summary>
        public Vector3 BodyPosition => ragdoll != null ? ragdoll.HipsPosition : transform.position;

        private vDamage lastDamage;
        private float lastVehicleHitTime = -100f;
        private Coroutine recoverRoutine;
        private Vector3 lastThreat;
        private bool dirty;
        private bool valid;
        private Coroutine dampingRoutine;
        private float[] baseLinearDamping = new float[0];
        private float[] baseAngularDamping = new float[0];

        private void Awake()
        {
            valid = character != null && ragdoll != null && aliveCollider != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(PedestrianDamage)}] Missing vCharacter, PedestrianRagdoll or alive collider on '{name}'. Damage is disabled.", this);
                enabled = false;
                return;
            }
            // vCharacter does not initialise itself (vThirdPersonMotor / AI normally do); caches its animator parameters.
            character.Init();
            // Subscribed for the whole lifetime (the character is a sibling component and raises nothing while pooled):
            // re-subscribing on every pool spawn / despawn allocated in UnityEvent each time.
            character.onStartReceiveDamage.AddListener(HandleStartDamage);
            character.onReceiveDamage.AddListener(HandleDamage);
            character.onDead.AddListener(HandleDead);
        }

        private void OnDestroy()
        {
            if (!valid || character == null) return;
            character.onStartReceiveDamage.RemoveListener(HandleStartDamage);
            character.onReceiveDamage.RemoveListener(HandleDamage);
            character.onDead.RemoveListener(HandleDead);
        }

        private void OnEnable()
        {
            if (!valid) return;
            IgnoreOwnBones();
        }

        private void OnDisable()
        {
            if (!valid) return;
            recoverRoutine = null; // coroutines stop with the GameObject
            if (dampingRoutine != null)
            {
                dampingRoutine = null;
                SetFlightDamping(false);
            }
        }

        /// <summary>Full reset for the pool: health, ragdoll, colliders. Cheap when nothing happened to this pedestrian.</summary>
        public void ResetForReuse()
        {
            if (!valid || !dirty) return;
            dirty = false;
            if (recoverRoutine != null) StopCoroutine(recoverRoutine);
            recoverRoutine = null;
            lastDamage = null;
            lastVehicleHitTime = -100f;
            if (dampingRoutine != null) StopCoroutine(dampingRoutine);
            dampingRoutine = null;
            SetFlightDamping(false);
            ragdoll.ResetToAnimated();
            character.ResetHealth();
            aliveCollider.enabled = true;
        }

        /// <summary>
        /// Detail level from the spawner's distance check: near the player the bone hitboxes are on (head shots) and a dead body
        /// keeps its physics bodies (cars bump it); farther away one capsule takes the bullets and a settled corpse is frozen
        /// (no physics bodies, see <see cref="PedestrianRagdoll.Freeze"/>).
        /// </summary>
        public void SetHitboxesEnabled(bool near)
        {
            if (!valid) return;
            ragdoll.SetHitboxesEnabled(near);
            if (!IsDead) return;
            if (near) ragdoll.Unfreeze();
            else if (ragdoll.IsSettled) ragdoll.Freeze();
        }

        public void ReceiveVehicleImpact(VehicleImpact impact)
        {
            if (!valid || !isActiveAndEnabled || Time.time - lastVehicleHitTime < hitCooldown) return;

            Vector3 flatVelocity = impact.Velocity;
            flatVelocity.y = 0f;
            float speed = flatVelocity.magnitude;
            if (speed < minimumImpactSpeed) return;
            lastVehicleHitTime = Time.time;
            dirty = true;
            lastThreat = impact.Vehicle != null ? impact.Vehicle.position : impact.Point;

            if (IsDead || IsDown)
            {
                // Already on the ground: the car carries the body along (capped), it does not kick it again on every contact.
                ShoveRagdoll(flatVelocity * groundShoveSpeedShare);
                return;
            }

            if (speed < ragdollImpactSpeed)
            {
                character.TakeDamage(CreateDamage(impact, flatVelocity, speed * lightDamagePerSpeed, false));
                return;
            }

            float high = Mathf.InverseLerp(ragdollImpactSpeed, highImpactSpeed, speed);
            float damageValue = speed * damageMultiplier * Mathf.Lerp(1f, highImpactDamageMultiplier, high);
            KnockDown(GetThrowVelocity(flatVelocity, speed, high), GetTumble(flatVelocity, speed),
                      minimumGroundTime + highImpactExtraGroundTime * high);
            // The limbs start where the capsule was, often inside the bumper: the car ignores them for a moment.
            if (impact.Source != null) impact.Source.IgnoreCollidersTemporarily(ragdoll.Colliders);
            // Damage after the knock-down so a lethal hit finds the ragdoll already active (HandleDead keeps it).
            character.TakeDamage(CreateDamage(impact, flatVelocity, damageValue, true));
        }

        /// <summary>
        /// Knocks the pedestrian down from a scripted push (a carjacked driver shoved away from the car): ragdoll thrown with
        /// the given velocity / tumble, back up after groundTime and then running from the threat.
        /// </summary>
        public void KnockDownBy(Vector3 velocity, Vector3 tumble, float groundTime, Vector3 threat)
        {
            if (!valid || IsDead) return;
            dirty = true;
            lastThreat = threat;
            KnockDown(velocity, tumble, groundTime);
        }

        /// <summary>Kills the pedestrian at once (e.g. a traffic driver shot at the wheel falling out of the car).</summary>
        public void Kill(Transform sender, Vector3 hitPosition)
        {
            if (!valid || IsDead) return;
            dirty = true;
            character.TakeDamage(new vDamage(Mathf.CeilToInt(character.MaxHealth * 10f), true)
            {
                ignoreDefense = true,
                sender = sender,
                hitPosition = hitPosition,
                damageType = "Shot"
            });
        }

        private void KnockDown(Vector3 velocity, Vector3 tumble, float groundTime)
        {
            aliveCollider.enabled = false;
            ragdoll.Activate(Vector3.zero);
            ThrowRagdoll(velocity, tumble);
            if (knockDownDampingDuration > 0f)
            {
                if (dampingRoutine != null) StopCoroutine(dampingRoutine);
                dampingRoutine = StartCoroutine(FlightDampingRoutine());
            }
            OnKnockedDown?.Invoke(lastThreat);
            if (recoverRoutine != null) StopCoroutine(recoverRoutine);
            recoverRoutine = StartCoroutine(RecoverRoutine(groundTime));
        }

        private IEnumerator RecoverRoutine(float groundTime)
        {
            float start = Time.time;
            yield return new WaitForSeconds(groundTime);
            while (!IsDead && Time.time - start < maxGroundTime && ragdoll.HipsBody != null && ragdoll.HipsBody.linearVelocity.sqrMagnitude > settledSpeed * settledSpeed)
                yield return null;
            recoverRoutine = null;
            if (IsDead || !ragdoll.IsActive) yield break;
            ragdoll.GetUp();
            aliveCollider.enabled = true;
            OnRecovered?.Invoke(lastThreat);
        }

        private vDamage CreateDamage(VehicleImpact impact, Vector3 flatVelocity, float value, bool knockDown)
        {
            return new vDamage
            {
                damageValue = Mathf.Round(value),
                sender = impact.Vehicle,
                hitPosition = impact.Point,
                force = flatVelocity * ragdollForceMultiplier,
                damageType = "Vehicle",
                ignoreDefense = true,
                activeRagdoll = false, // the ragdoll is driven here, not through Invector's onActiveRagdoll
                hitReaction = !knockDown
            };
        }

        private void HandleStartDamage(vDamage damage)
        {
            dirty = true;
            lastDamage = damage;
            if (damage.sender != null) lastThreat = damage.sender.position;
        }

        private void HandleDamage(vDamage damage)
        {
            if (!IsDead) OnHurt?.Invoke(lastThreat);
        }

        private void HandleDead(GameObject deadObject)
        {
            if (recoverRoutine != null) StopCoroutine(recoverRoutine);
            recoverRoutine = null;
            aliveCollider.enabled = false;
            bool wasDown = ragdoll.IsActive;
            ragdoll.Activate(Vector3.zero);
            if (!wasDown) ApplyDeathImpulse();
            OnDied?.Invoke(lastThreat);
        }

        private void ApplyDeathImpulse()
        {
            if (lastDamage == null) return;
            Rigidbody limb = lastDamage.receiver != null ? lastDamage.receiver.GetComponentInParent<Rigidbody>() : null;
            if (limb == null || limb.isKinematic) limb = ragdoll.HipsBody;
            if (limb == null || limb.isKinematic) return;

            Vector3 impulse = lastDamage.force * deathForceMultiplier;
            if (lastDamage.force.sqrMagnitude < 0.0001f && lastDamage.sender != null)
            {
                Vector3 direction = transform.position - lastDamage.sender.position;
                direction.y = 0f;
                impulse = direction.normalized * fallbackDeathImpulse;
            }
            if (impulse.sqrMagnitude > 0f) limb.AddForceAtPosition(impulse, lastDamage.hitPosition, ForceMode.Impulse);
        }

        private Vector3 GetThrowVelocity(Vector3 flatVelocity, float speed, float high)
        {
            Vector3 horizontal = Vector3.ClampMagnitude(flatVelocity * Mathf.Lerp(lowSpeedForceMultiplier, ragdollForceMultiplier, high), maxThrowSpeed);
            float lift = Mathf.Min(Mathf.Max(minimumLift, speed * liftPerSpeed) * Mathf.Lerp(1f, 1.3f, high), Mathf.Max(minimumLift, maxLift));
            return horizontal + Vector3.up * lift;
        }

        private Vector3 GetTumble(Vector3 flatVelocity, float speed)
        {
            if (speed < 0.01f) return Vector3.zero;
            Vector3 axis = Vector3.Cross(flatVelocity / speed, Vector3.up);
            return axis * Mathf.Min(speed * tumblePerSpeed, maxTumble) + Vector3.up * UnityEngine.Random.Range(-1.5f, 1.5f);
        }

        // Legs get legVelocityFactor of the throw, the upper body fades to upperBodyVelocityFactor with height above the hips,
        // plus the rigid-body rotation so the whole body tumbles (same model as EnemyVehicleImpact).
        private void ThrowRagdoll(Vector3 velocity, Vector3 angularVelocity)
        {
            if (!ragdoll.IsActive) return;
            Rigidbody hips = ragdoll.HipsBody;
            Vector3 core = hips.worldCenterOfMass;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 lift = new Vector3(0f, velocity.y, 0f);
            Rigidbody[] bodies = ragdoll.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null || body.isKinematic) continue;
                Vector3 offset = body.worldCenterOfMass - core;
                float weight = offset.y < -0.05f
                    ? legVelocityFactor
                    : Mathf.Lerp(1f, upperBodyVelocityFactor, Mathf.Clamp01(offset.y / upperBodyFalloffDistance));
                body.AddForce(horizontal * weight + lift + Vector3.Cross(angularVelocity, offset), ForceMode.VelocityChange);
            }
        }

        // Raises each limb's velocity along the car's direction up to the wanted speed (never adds on top of what it already has).
        private void ShoveRagdoll(Vector3 wantedVelocity)
        {
            if (!ragdoll.IsActive) return;
            float wantedSpeed = wantedVelocity.magnitude;
            if (wantedSpeed < 0.01f) return;
            Vector3 direction = wantedVelocity / wantedSpeed;
            Rigidbody[] bodies = ragdoll.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null || body.isKinematic) continue;
                float missing = wantedSpeed - Vector3.Dot(body.linearVelocity, direction);
                if (missing > 0f) body.AddForce(direction * missing, ForceMode.VelocityChange);
            }
        }

        private IEnumerator FlightDampingRoutine()
        {
            SetFlightDamping(true);
            yield return new WaitForSeconds(knockDownDampingDuration);
            SetFlightDamping(false);
            dampingRoutine = null;
        }

        private void SetFlightDamping(bool on)
        {
            Rigidbody[] bodies = ragdoll.Bodies;
            if (baseLinearDamping.Length != bodies.Length)
            {
                if (!on) return; // never changed
                baseLinearDamping = new float[bodies.Length];
                baseAngularDamping = new float[bodies.Length];
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] == null) continue;
                    baseLinearDamping[i] = bodies[i].linearDamping;
                    baseAngularDamping[i] = bodies[i].angularDamping;
                }
            }
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null) continue;
                body.linearDamping = on ? Mathf.Max(knockDownLinearDamping, baseLinearDamping[i]) : baseLinearDamping[i];
                body.angularDamping = on ? Mathf.Max(knockDownAngularDamping, baseAngularDamping[i]) : baseAngularDamping[i];
            }
        }

        // The root capsule must never touch its own limbs (trigger hitboxes while alive, solid limbs while ragdolled).
        // Ignore pairs are reset when colliders are deactivated, so this runs every time the pooled pedestrian is activated.
        private void IgnoreOwnBones()
        {
            var bones = ragdoll.Colliders;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] != null) Physics.IgnoreCollision(aliveCollider, bones[i], true);
            }
        }
    }
}
