using System.Collections;
using System.Collections.Generic;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Combat
{
    /// <summary>
    /// Makes an <see cref="EnemyDummy"/> react to vehicle hits, scaled by impact speed. Uses the existing Invector systems:
    /// damage goes through vCharacter.TakeDamage and the ragdoll is Invector's vRagdoll, which also handles settling,
    /// aligning the root to the hips and the get-up animation (StandUp@FromBack / StandUp@FromBelly) for survivors.
    /// - Below Minimum Impact Speed: ignored.
    /// - Below Ragdoll Impact Speed: light damage, hit reaction and an eased stumble out of the car's path (sideways).
    /// - Above: damage and a temporary ragdoll thrown along the car's direction slightly faster than the car, so the car does not
    ///   catch the body. Legs and hips get the full throw (the legs are swept), the upper body less, plus lift and a tumble that
    ///   rolls the upper body back onto the hood; faster hits launch the body.
    /// - Towards High Impact Speed: damage, lift and time on the ground ramp up smoothly (no hard step).
    /// A dead dummy only gets pushed; it never recovers.
    /// Other systems can knock the character down without damage through <see cref="Launch"/> (e.g. a driver pulled out of a car).
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyVehicleImpact : MonoBehaviour, IVehicleImpactReceiver
    {
        [Header("References")]
        [SerializeField] private EnemyDummy dummy;
        [SerializeField] private vCharacter character;
        [SerializeField] private vRagdoll ragdoll;

        [Header("Impact Speeds (m/s)")]
        [Tooltip("Slower hits are ignored.")]
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 0.6f;
        [Tooltip("From this speed the character is knocked down (temporary ragdoll).")]
        [SerializeField, Min(0f)] private float ragdollImpactSpeed = 4f;
        [Tooltip("Speed at which damage, lift and time on the ground reach their high-impact values (ramped from Ragdoll Impact Speed).")]
        [SerializeField, Min(0f)] private float highImpactSpeed = 12f;

        [Header("Damage")]
        [Tooltip("Damage per m/s of impact speed for light hits (no ragdoll).")]
        [SerializeField, Min(0f)] private float lightDamagePerSpeed = 1f;
        [Tooltip("Damage per m/s of impact speed for knock-downs.")]
        [SerializeField, Min(0f)] private float damageMultiplier = 4f;
        [Tooltip("Damage multiplier reached at High Impact Speed.")]
        [SerializeField, Min(1f)] private float highImpactDamageMultiplier = 1.5f;

        [Header("Force")]
        [Tooltip("Velocity given to the hips and legs as a fraction of the car's impact velocity at High Impact Speed (launched ahead of the car).")]
        [SerializeField, Min(0f)] private float ragdollForceMultiplier = 0.8f;
        [Tooltip("Same fraction at Ragdoll Impact Speed. Below 1 the car catches up and the lifted body rolls onto the hood (medium hits).")]
        [SerializeField, Min(0f)] private float lowSpeedForceMultiplier = 0.65f;
        [Tooltip("Upper limit (m/s) of the horizontal throw velocity.")]
        [SerializeField, Min(0f)] private float maxThrowSpeed = 9f;
        [Tooltip("Upward velocity given to the ragdoll per m/s of impact speed.")]
        [SerializeField, Min(0f)] private float liftPerSpeed = 0.16f;
        [Tooltip("Upward velocity (m/s) every knock-down gets at least, so the body rolls onto the hood instead of sliding under the car.")]
        [SerializeField, Min(0f)] private float minimumLift = 1.2f;
        [Tooltip("Lift multiplier reached at High Impact Speed.")]
        [SerializeField, Min(1f)] private float highImpactLiftMultiplier = 1.3f;
        [Tooltip("Share of the throw velocity kept by the body parts above the hips at Upper Body Falloff Distance (head, arms).")]
        [SerializeField, Range(0f, 1f)] private float upperBodyVelocityFactor = 0.7f;
        [Tooltip("Share of the throw velocity given to the legs (the bumper sweeps them).")]
        [SerializeField, Range(0f, 2f)] private float legVelocityFactor = 1.1f;
        [Tooltip("Distance (m) above the hips at which a body part only keeps Upper Body Velocity Factor.")]
        [SerializeField, Min(0.1f)] private float upperBodyFalloffDistance = 0.8f;
        [Tooltip("Tumble (rad/s) per m/s of impact speed: the legs are swept along and the upper body falls back toward the car.")]
        [SerializeField, Min(0f)] private float tumblePerSpeed = 0.25f;
        [SerializeField, Min(0f)] private float maxTumble = 4f;
        [Tooltip("Random spin (rad/s) around the vertical axis, so repeated hits do not look identical.")]
        [SerializeField, Min(0f)] private float randomYawSpin = 1.5f;

        [Header("Flight Damping")]
        [Tooltip("Linear damping on the ragdoll bodies right after a knock-down (air and ground friction feel; keeps fast hits from sending the body across the map).")]
        [SerializeField, Min(0f)] private float knockDownLinearDamping = 0.8f;
        [SerializeField, Min(0f)] private float knockDownAngularDamping = 1.5f;
        [Tooltip("Seconds the extra damping lasts before the ragdoll's own values are restored.")]
        [SerializeField, Min(0f)] private float knockDownDampingDuration = 2.5f;

        [Header("Light Push")]
        [Tooltip("Distance (m) a light hit pushes the character, mostly sideways out of the car's path.")]
        [SerializeField, Min(0f)] private float lightPushDistance = 0.8f;
        [Tooltip("Share of the push along the car's direction (the rest is sideways, away from the car's centre line).")]
        [SerializeField, Range(0f, 1f)] private float lightPushForwardShare = 0.3f;
        [Tooltip("Duration of the push; it starts fast and eases out.")]
        [SerializeField, Min(0.01f)] private float lightPushDuration = 0.3f;

        [Header("Recovery")]
        [Tooltip("Minimum seconds a knocked-down survivor stays on the ground (vRagdoll also waits for the body to settle).")]
        [SerializeField, Min(0f)] private float recoveryDelay = 2.5f;
        [Tooltip("Extra seconds on the ground reached at High Impact Speed.")]
        [SerializeField, Min(0f)] private float highImpactExtraDelay = 1.5f;
        [Tooltip("Seconds during which new hits from vehicles are ignored (one reaction per bump).")]
        [SerializeField, Min(0f)] private float hitCooldown = 0.35f;
        [Tooltip("Safety limit (s) for getting back up; after it the character is forced back to its animated state.")]
        [SerializeField, Min(1f)] private float recoveryTimeout = 20f;

        private readonly List<Rigidbody> ragdollBodies = new List<Rigidbody>();
        private readonly List<Collider> ragdollColliders = new List<Collider>();
        private Rigidbody hipsBody;
        private Collider aliveCollider;
        private float lastHitTime = -100f;
        private Coroutine pushRoutine;
        private Coroutine restoreRoutine;
        private Coroutine dampingRoutine;
        private float[] baseLinearDamping = new float[0];
        private float[] baseAngularDamping = new float[0];

        /// <summary>True while knocked down or getting back up.</summary>
        public bool IsKnockedDown => restoreRoutine != null || (ragdoll != null && ragdoll.isActive);

        private void Awake()
        {
            if (dummy == null || character == null || ragdoll == null)
            {
                Debug.LogError($"[{nameof(EnemyVehicleImpact)}] Missing EnemyDummy, vCharacter or vRagdoll reference on '{name}'. Vehicle impacts are disabled.", this);
                enabled = false;
                return;
            }

            aliveCollider = dummy.AliveCollider;
            // Cached now: vRagdoll re-parents the hips while active, but the bodies stay the same.
            Rigidbody rootBody = GetComponent<Rigidbody>();
            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == rootBody) continue;
                ragdollBodies.Add(body);
                if (body.TryGetComponent(out Collider limb)) ragdollColliders.Add(limb);
            }

            baseLinearDamping = new float[ragdollBodies.Count];
            baseAngularDamping = new float[ragdollBodies.Count];
            for (int i = 0; i < ragdollBodies.Count; i++)
            {
                baseLinearDamping[i] = ragdollBodies[i].linearDamping;
                baseAngularDamping[i] = ragdollBodies[i].angularDamping;
            }

            Animator animator = GetComponent<Animator>();
            Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            hipsBody = hips != null ? hips.GetComponent<Rigidbody>() : null;
        }

        public void ReceiveVehicleImpact(VehicleImpact impact)
        {
            if (!enabled || Time.time - lastHitTime < hitCooldown) return;

            Vector3 flatVelocity = impact.Velocity;
            flatVelocity.y = 0f;
            float speed = flatVelocity.magnitude;
            if (speed < minimumImpactSpeed) return;
            lastHitTime = Time.time;

            if (dummy.IsDead)
            {
                if (ragdoll.isActive) ThrowRagdoll(GetThrowVelocity(flatVelocity, speed, 0f), GetTumble(flatVelocity, speed));
                return;
            }

            if (speed < ragdollImpactSpeed)
            {
                LightHit(impact, flatVelocity, speed);
                return;
            }

            KnockDown(impact, flatVelocity, speed);
        }

        /// <summary>
        /// Knocks the character down without damage and throws it with the given velocity (m/s) and tumble (rad/s, world axis).
        /// Survivors get back up after at least <paramref name="groundTime"/> seconds. Used e.g. for a driver pulled out of a car.
        /// </summary>
        public void Launch(Vector3 velocity, Vector3 angularVelocity, float groundTime, Transform source)
        {
            if (!enabled) return;
            lastHitTime = Time.time;

            var damage = new vDamage
            {
                damageValue = 0f,
                sender = source,
                hitPosition = hipsBody != null ? hipsBody.position : transform.position,
                force = velocity,
                damageType = "Thrown",
                ignoreDefense = true,
                activeRagdoll = true,
                hitReaction = false,
                senselessTime = groundTime
            };
            StartKnockDown(damage, velocity, angularVelocity);
        }

        private void LightHit(VehicleImpact impact, Vector3 flatVelocity, float speed)
        {
            // Already on the ground: a slow car just nudges the body, no extra damage.
            if (ragdoll.isActive)
            {
                ThrowRagdoll(flatVelocity * (ragdollForceMultiplier * 0.5f), Vector3.zero);
                return;
            }

            vDamage damage = CreateDamage(impact, flatVelocity, speed * lightDamagePerSpeed);
            damage.activeRagdoll = false;
            damage.hitReaction = true;
            character.TakeDamage(damage);

            if (dummy.IsDead) return;
            if (pushRoutine != null) StopCoroutine(pushRoutine);
            float strength = Mathf.InverseLerp(minimumImpactSpeed, ragdollImpactSpeed, speed);
            pushRoutine = StartCoroutine(PushRoutine(GetPushDirection(impact, flatVelocity) * (lightPushDistance * Mathf.Lerp(0.5f, 1f, strength))));
        }

        private void KnockDown(VehicleImpact impact, Vector3 flatVelocity, float speed)
        {
            float high = Mathf.InverseLerp(ragdollImpactSpeed, highImpactSpeed, speed);
            float damageValue = speed * damageMultiplier * Mathf.Lerp(1f, highImpactDamageMultiplier, high);

            vDamage damage = CreateDamage(impact, flatVelocity, damageValue);
            damage.activeRagdoll = true;
            damage.hitReaction = false;
            damage.senselessTime = recoveryDelay + highImpactExtraDelay * high;

            StartKnockDown(damage, GetThrowVelocity(flatVelocity, speed, high), GetTumble(flatVelocity, speed));
            // The limbs start inside the bumper: the car ignores them for a moment (no depenetration launch, no stopping dead).
            if (impact.Source != null && ragdoll.isActive) impact.Source.IgnoreCollidersTemporarily(ragdollColliders);
        }

        private void StartKnockDown(vDamage damage, Vector3 velocity, Vector3 angularVelocity)
        {
            // The capsule would fight the limbs while ragdolled; it is restored once the character is back up.
            if (aliveCollider != null) aliveCollider.enabled = false;
            if (pushRoutine != null)
            {
                StopCoroutine(pushRoutine);
                pushRoutine = null;
            }

            character.TakeDamage(damage);
            ThrowRagdoll(velocity, angularVelocity);
            if (ragdoll.isActive && knockDownDampingDuration > 0f)
            {
                if (dampingRoutine != null) StopCoroutine(dampingRoutine);
                dampingRoutine = StartCoroutine(DampingRoutine());
            }

            if (!dummy.IsDead && ragdoll.isActive)
            {
                // vRagdoll only blends back to the animated state while the character reports itself as ragdolled.
                // vThirdPersonMotor sets this in EnableRagdoll(); the plain vCharacter used by the dummy does not.
                character.ragdolled = true;
                if (restoreRoutine != null) StopCoroutine(restoreRoutine);
                restoreRoutine = StartCoroutine(RestoreAfterGetUp());
            }
            else if (!dummy.IsDead && aliveCollider != null)
            {
                // The ragdoll could not start (e.g. still blending back from a previous knock-down): keep the body solid.
                aliveCollider.enabled = true;
            }
        }

        private vDamage CreateDamage(VehicleImpact impact, Vector3 flatVelocity, float value)
        {
            return new vDamage
            {
                damageValue = Mathf.Round(value),
                sender = impact.Vehicle,
                hitPosition = impact.Point,
                force = flatVelocity * ragdollForceMultiplier,
                damageType = "Vehicle",
                ignoreDefense = true
            };
        }

        private Vector3 GetThrowVelocity(Vector3 flatVelocity, float speed, float high)
        {
            Vector3 horizontal = Vector3.ClampMagnitude(flatVelocity * Mathf.Lerp(lowSpeedForceMultiplier, ragdollForceMultiplier, high), maxThrowSpeed);
            float lift = Mathf.Max(minimumLift, speed * liftPerSpeed) * Mathf.Lerp(1f, highImpactLiftMultiplier, high);
            return horizontal + Vector3.up * lift;
        }

        // Mostly sideways, away from the car's centre line, so a slow car nudges the character out of its way.
        private Vector3 GetPushDirection(VehicleImpact impact, Vector3 flatVelocity)
        {
            Vector3 carDirection = flatVelocity.normalized;
            Vector3 fromCar = impact.Vehicle != null ? transform.position - impact.Vehicle.position : transform.position - impact.Point;
            Vector3 sideways = Vector3.ProjectOnPlane(fromCar, Vector3.up);
            sideways = Vector3.ProjectOnPlane(sideways, carDirection);
            if (sideways.sqrMagnitude < 0.0025f) sideways = Vector3.Cross(Vector3.up, carDirection);
            return (sideways.normalized * (1f - lightPushForwardShare) + carDirection * lightPushForwardShare).normalized;
        }

        // Legs are swept along the car's direction and the upper body falls back toward the car, plus a little random yaw.
        private Vector3 GetTumble(Vector3 flatVelocity, float speed)
        {
            if (speed < 0.01f) return Vector3.zero;
            Vector3 axis = Vector3.Cross(flatVelocity / speed, Vector3.up);
            return axis * Mathf.Min(speed * tumblePerSpeed, maxTumble) + Vector3.up * Random.Range(-randomYawSpin, randomYawSpin);
        }

        // Velocity change per body part: legs get legVelocityFactor of the horizontal throw, the upper body fades to
        // upperBodyVelocityFactor with height above the hips (lift is the same for every part), plus the rigid-body rotation
        // (angular x offset from the hips) so the whole body tumbles instead of spinning one bone.
        private void ThrowRagdoll(Vector3 velocity, Vector3 angularVelocity)
        {
            if (!ragdoll.isActive) return;
            Vector3 core = hipsBody != null ? hipsBody.worldCenterOfMass : transform.position;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 lift = new Vector3(0f, velocity.y, 0f);
            for (int i = 0; i < ragdollBodies.Count; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null || body.isKinematic) continue;
                Vector3 offset = body.worldCenterOfMass - core;
                float weight = offset.y < -0.05f
                    ? legVelocityFactor
                    : Mathf.Lerp(1f, upperBodyVelocityFactor, Mathf.Clamp01(offset.y / upperBodyFalloffDistance));
                body.AddForce(horizontal * weight + lift + Vector3.Cross(angularVelocity, offset), ForceMode.VelocityChange);
            }
        }

        private void OnDisable()
        {
            if (dampingRoutine == null) return;
            StopCoroutine(dampingRoutine);
            dampingRoutine = null;
            SetDamping(false);
        }

        private IEnumerator DampingRoutine()
        {
            SetDamping(true);
            yield return new WaitForSeconds(knockDownDampingDuration);
            SetDamping(false);
            dampingRoutine = null;
        }

        private void SetDamping(bool knockedDown)
        {
            for (int i = 0; i < ragdollBodies.Count; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null) continue;
                body.linearDamping = knockedDown ? Mathf.Max(knockDownLinearDamping, baseLinearDamping[i]) : baseLinearDamping[i];
                body.angularDamping = knockedDown ? Mathf.Max(knockDownAngularDamping, baseAngularDamping[i]) : baseAngularDamping[i];
            }
        }

        // Starts fast and eases out, so a light bump reads as a stumble instead of a slide.
        private IEnumerator PushRoutine(Vector3 offset)
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < lightPushDuration; t += Time.deltaTime)
            {
                if (ragdoll.isActive || dummy.IsDead) break;
                float k = 1f - Mathf.Pow(1f - t / lightPushDuration, 3f);
                transform.position = start + offset * k;
                yield return null;
            }
            pushRoutine = null;
        }

        // Once vRagdoll has settled the body, played the get-up and blended back to the animated state, the alive capsule
        // and the ragdolled flag are restored (never for a dead dummy, which stays ragdolled).
        private IEnumerator RestoreAfterGetUp()
        {
            float timer = 0f;
            yield return null;
            while (!dummy.IsDead && (ragdoll.isActive || ragdoll.state != vRagdoll.RagdollState.animated))
            {
                timer += Time.deltaTime;
                if (timer > recoveryTimeout && !ragdoll.isActive)
                {
                    Debug.LogWarning($"[{nameof(EnemyVehicleImpact)}] '{name}' did not finish getting up in {recoveryTimeout}s; forcing the animated state.", this);
                    ragdoll.state = vRagdoll.RagdollState.animated;
                    break;
                }
                yield return null;
            }

            if (!dummy.IsDead)
            {
                character.ragdolled = false;
                if (aliveCollider != null) aliveCollider.enabled = true;
            }
            restoreRoutine = null;
        }
    }
}
