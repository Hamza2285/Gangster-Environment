using System.Collections.Generic;
using Invector.vCharacterController;
using Invector.vMelee;
using Invector.vShooter;
using OpenworldCrime.Pedestrians;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// GTA-style punch on the run (Jason and Mia; per character). Pressing punch while running / sprinting unarmed throws a
    /// punch with the upper body ("RunPunch" layer of our player controller, see PlayerCombatAnimBuilder) while the legs keep
    /// running, instead of Invector stopping into the standing combo. The nearest person in a short cone in front is picked;
    /// until the fist lands the player turns toward him and slides a little sideways onto his line (soft aim, so moving
    /// people are easy to hit), and the hit is resolved by distance / angle at the impact time (not by the fist's hitbox).
    /// A landed running punch knocks the person down (PedestrianDamage knock-down + get-up). Then the layer blends back out
    /// into the run. Standing punches are Invector's as before.
    /// Runs before Invector's input (execution order) so the same button press is not also taken as a standing attack.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class PlayerRunningPunch : MonoBehaviour
    {
        private struct Punch
        {
            public int State;
            public float Seconds;    // played length (clip / state speed)
            public float Impact;     // seconds to the moment the fist is out
            public float HoldUntil;  // seconds after which the layer blends back to the run
            public float SpineYaw;   // degrees: the clip's stance turns the hips, the run's hips face forward; this puts the fist back on line
        }

        [Header("References (filled automatically)")]
        [SerializeField] private Animator animator;
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vShooterMeleeInput input;
        [SerializeField] private vMeleeManager meleeManager;
        [SerializeField] private vShooterManager shooterManager;
        [SerializeField] private Rigidbody body;
        [SerializeField] private PlayerVehicleInteraction vehicle;
        [SerializeField] private PlayerHealth health;

        [Header("When")]
        [Tooltip("Horizontal speed (m/s) from which a punch press becomes a running punch.")]
        [SerializeField, Min(0f)] private float minimumSpeed = 2.6f;
        [Tooltip("Seconds after a running punch before the next one.")]
        [SerializeField, Min(0f)] private float cooldown = 0.15f;

        [Header("Targeting")]
        [Tooltip("Reach (m, body to body) of the punch, plus the ground covered while it winds up.")]
        [SerializeField, Min(0.5f)] private float range = 2.5f;
        [Tooltip("Half angle (degrees) of the cone in front in which people are picked.")]
        [SerializeField, Range(10f, 90f)] private float coneAngle = 55f;
        [Tooltip("Turn rate (degrees/s) toward the target until the fist lands.")]
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [Tooltip("Sideways slide (m/s) onto the target's line until the fist lands, and its total limit (m).")]
        [SerializeField, Min(0f)] private float magnetSpeed = 3f;
        [SerializeField, Min(0f)] private float magnetLimit = 0.7f;

        [Header("Hit")]
        [Tooltip("Damage of a landed running punch (at or above PedestrianDamage's knock-down melee damage it knocks down).")]
        [SerializeField, Min(0f)] private float damage = 26f;
        [SerializeField] private AudioClip[] hitSounds = new AudioClip[0];
        [SerializeField] private AudioClip[] swingSounds = new AudioClip[0];

        [Header("Blending")]
        [SerializeField, Min(0.01f)] private float blendIn = 0.06f;
        [SerializeField, Min(0.01f)] private float blendOut = 0.2f;

        private static readonly Punch[] Punches =
        {
            // WeakAttack_UnarmedA (0.54 s, right fist out at 0.27 s) at speed 1.15.
            // Fist 34 degrees right of the hips at impact (stance hips turned left): spine turned back by that much.
            new Punch { State = Animator.StringToHash("RunPunch_R"), Seconds = 0.47f, Impact = 0.235f, HoldUntil = 0.36f, SpineYaw = -34f },
            // Moto AS_Punch_Front (1.0 s, left fist out at 0.35 s) at speed 1.3.
            // Fist 15 degrees left of the hips at impact.
            new Punch { State = Animator.StringToHash("RunPunch_L"), Seconds = 0.77f, Impact = 0.27f, HoldUntil = 0.45f, SpineYaw = 15f },
        };

        /// <summary>A running punch was thrown (true = it landed on someone).</summary>
        public event System.Action<bool> OnRunningPunch;

        public bool IsPunching => punching;

        private int layer = -1;
        private Transform spine;
        private bool punching;
        private int current;
        private int next;
        private float started;
        private float weight;
        private bool landed;
        private float magnetUsed;
        private float readyAt;
        private bool restoreInput;
        private PedestrianDamage target;
        private AudioSource audioSource;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            controller = GetComponent<vThirdPersonController>();
            input = GetComponent<vShooterMeleeInput>();
            meleeManager = GetComponent<vMeleeManager>();
            shooterManager = GetComponent<vShooterManager>();
            body = GetComponent<Rigidbody>();
            vehicle = GetComponent<PlayerVehicleInteraction>();
            health = GetComponent<PlayerHealth>();
        }

        private void Awake()
        {
            if (animator == null || controller == null || input == null) Reset();
            if (animator == null || controller == null || input == null || body == null)
            {
                Debug.LogError($"[{nameof(PlayerRunningPunch)}] Missing Animator / Invector controller / input / Rigidbody on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            layer = animator != null ? animator.GetLayerIndex("RunPunch") : -1;
            spine = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (layer < 0 && animator != null) Debug.LogWarning($"[{nameof(PlayerRunningPunch)}] '{name}' controller has no RunPunch layer (run Tools > OpenworldCrime > Player > Build Fist Fight Animations).", this);
        }

        private void OnDisable()
        {
            EndPunch();
            weight = 0f;
            if (layer >= 0 && animator != null) animator.SetLayerWeight(layer, 0f);
            if (restoreInput && input != null) input.weakAttackInput.useInput = true;
            restoreInput = false;
        }

        private void Update()
        {
            if (layer < 0) return;
            if (!punching && Time.time >= readyAt && CanRunPunch() && input.weakAttackInput.useInput && input.weakAttackInput.GetButtonDown())
            {
                // Taken here: Invector's input (later this frame) must not also start a standing attack.
                input.weakAttackInput.useInput = false;
                restoreInput = true;
                BeginPunch();
            }
            if (punching) TickPunch();
            UpdateWeight();
        }

        private void LateUpdate()
        {
            if (restoreInput)
            {
                restoreInput = false;
                input.weakAttackInput.useInput = true;
            }
            // After the Animator: turn the upper body so the punch goes where the player runs (at the target).
            if (weight > 0.001f && spine != null) spine.rotation = Quaternion.AngleAxis(Punches[current].SpineYaw * weight, Vector3.up) * spine.rotation;
        }

        private bool CanRunPunch()
        {
            if (!controller.isGrounded || controller.isDead || controller.ragdolled || controller.customAction || controller.isRolling) return false;
            if (input.lockMeleeInput || input.IsAiming || input.isAttacking) return false;
            if (shooterManager != null && shooterManager.CurrentActiveWeapon != null) return false;
            if (meleeManager != null && meleeManager.CurrentActiveAttackWeapon != null) return false; // bats / knives: Invector's attacks
            if (vehicle != null && vehicle.State != VehicleInteractionState.OnFoot) return false;
            if (health != null && health.IsDead) return false;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude >= minimumSpeed * minimumSpeed;
        }

        private void BeginPunch()
        {
            punching = true;
            landed = false;
            magnetUsed = 0f;
            started = Time.time;
            current = next;
            next = (next + 1) % Punches.Length; // left / right in turn
            target = FindTarget(range + Speed() * Punches[current].Impact);
            animator.CrossFadeInFixedTime(Punches[current].State, 0.04f, layer, 0f);
            if (target != null) controller.lockRotation = true;
            PlaySound(swingSounds, 0.6f);
        }

        private void TickPunch()
        {
            Punch punch = Punches[current];
            float t = Time.time - started;
            if (!landed)
            {
                if (target != null && (target.IsDead || target.IsDown || !target.isActiveAndEnabled)) target = null;
                if (target != null) SteerTo(target.BodyPosition);
                if (t >= punch.Impact)
                {
                    landed = true;
                    controller.lockRotation = false;
                    ResolveHit();
                }
            }
            if (t >= punch.Seconds) EndPunch();
        }

        private void EndPunch()
        {
            if (!punching) return;
            punching = false;
            target = null;
            if (controller != null) controller.lockRotation = false;
            readyAt = Time.time + cooldown;
        }

        private void UpdateWeight()
        {
            float goal = punching && Time.time - started < Punches[current].HoldUntil ? 1f : 0f;
            float rate = goal > weight ? 1f / blendIn : 1f / blendOut;
            weight = Mathf.MoveTowards(weight, goal, rate * Time.deltaTime);
            animator.SetLayerWeight(layer, weight);
        }

        // Turns toward the target and slides onto its line (limited), so a moving person in the cone is reached.
        private void SteerTo(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(to.normalized);
            Quaternion rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            transform.rotation = rotation;
            body.rotation = rotation;

            Vector3 forward = rotation * Vector3.forward;
            Vector3 side = to - forward * Vector3.Dot(to, forward);
            float step = Mathf.Min(side.magnitude, magnetSpeed * Time.deltaTime, magnetLimit - magnetUsed);
            if (step <= 0f) return;
            Vector3 delta = side.normalized * step;
            magnetUsed += step;
            body.position += delta;
            transform.position += delta;
        }

        private void ResolveHit()
        {
            float reach = range + 0.3f;
            if (target == null || !InReach(target, reach, coneAngle + 20f)) target = FindTarget(reach);
            bool hit = target != null;
            if (hit)
            {
                Vector3 point = target.BodyPosition + Vector3.up * 0.35f;
                target.ReceiveMeleeStrike(transform, damage, point);
                PlaySound(hitSounds, 1f);
            }
            OnRunningPunch?.Invoke(hit);
        }

        private PedestrianDamage FindTarget(float reach)
        {
            IReadOnlyList<PedestrianDamage> people = PedestrianDamage.Active;
            PedestrianDamage best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < people.Count; i++)
            {
                PedestrianDamage person = people[i];
                if (person == null || person.IsDead || person.IsDown) continue;
                if (!InReach(person, reach, coneAngle)) continue;
                Vector3 to = person.BodyPosition - transform.position;
                to.y = 0f;
                float score = to.magnitude + Vector3.Angle(transform.forward, to) * 0.02f;
                if (score < bestScore) { bestScore = score; best = person; }
            }
            return best;
        }

        private bool InReach(PedestrianDamage person, float reach, float angle)
        {
            Vector3 to = person.BodyPosition - transform.position;
            if (Mathf.Abs(to.y) > 1.6f) return false;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > reach) return false;
            return distance < 0.6f || Vector3.Angle(transform.forward, to) <= angle;
        }

        private float Speed()
        {
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        private void PlaySound(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 2f;
                audioSource.maxDistance = 25f;
            }
            audioSource.PlayOneShot(clip, volume);
        }
    }
}
