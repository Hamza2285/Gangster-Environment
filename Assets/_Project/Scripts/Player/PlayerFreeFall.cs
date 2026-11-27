using System;
using System.Collections;
using Invector;
using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Falling from a height (one per playable character, not a Singleton), in two ways:
    /// - With the parachute bag on the back and high enough to open it (<see cref="PlayerParachute.CanDeploy"/>): a stable
    ///   skydiving pose - body belly-down, arms and legs spread (generated humanoid clip played over the Animator, the skeleton
    ///   pitched at the hips like the swim pose) - until the parachute opens or the ground comes.
    /// - Without a parachute (jumping out of a helicopter in the air, letting go of the canopy): <see cref="StartRagdollFall"/>
    ///   throws Invector's ragdoll with the given velocity; the landing hurts by the fall speed (a long fall kills).
    /// - Screams: while falling helplessly (that ragdoll fall, or a long ordinary fall without an open parachute or the stable
    ///   pose) a new scream starts every Scream Interval seconds (random clip, start point and pitch), the previous one fading
    ///   under it; they fade out on landing or death.
    /// - A hard landing (ragdoll or ordinary fall above Safe Impact Speed) raises <see cref="OnHardLanding"/> (blood).
    /// Called by <see cref="PlayerVehicleInteraction"/> and <see cref="PlayerParachute"/>; drives Invector from outside.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1003)] // after PlayerParachute (1002)
    public class PlayerFreeFall : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerParachute parachute;
        [Tooltip("Invector ragdoll of the character. Without it there is no ragdoll fall.")]
        [SerializeField] private vRagdoll ragdoll;

        [Header("Stable Fall (with the parachute bag)")]
        [Tooltip("Spread-eagle skydiving pose (generated humanoid clip, upright; the body is pitched by code).")]
        [SerializeField] private AnimationClip skydiveClip;
        [Tooltip("Forward pitch (deg) of the body: 90 = flat belly-down.")]
        [SerializeField, Range(0f, 90f)] private float skydivePitch = 78f;
        [Tooltip("Seconds to blend into / out of the pose.")]
        [SerializeField, Min(0.05f)] private float skydiveBlendTime = 0.35f;
        [Tooltip("Fall speed (m/s) the spread body does not go beyond (air drag), so there is time to open the parachute. " +
                 "Invector's extra air gravity alone reaches ~40 m/s within two seconds.")]
        [SerializeField, Min(5f)] private float skydiveTerminalSpeed = 24f;

        [Header("Screams")]
        [Tooltip("Screams while falling. One is picked at random each time (not the same twice in a row when there are several).")]
        [SerializeField] private AudioClip[] screams = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] private float screamVolume = 1f;
        [Tooltip("Pitch of the scream (e.g. higher for a female character using a male recording).")]
        [SerializeField, Range(0.5f, 2f)] private float screamPitch = 1f;
        [Tooltip("Random pitch variation (+/-) of each scream.")]
        [SerializeField, Range(0f, 0.3f)] private float screamPitchJitter = 0.08f;
        [Tooltip("Seconds between the starts of two screams (random between x and y).")]
        [SerializeField] private Vector2 screamInterval = new Vector2(1.3f, 1.9f);
        [Tooltip("Seconds the previous scream fades out under the next one, and on landing.")]
        [SerializeField, Min(0.02f)] private float screamFadeTime = 0.3f;
        [Tooltip("Latest start point (s) into the clip, random, so repeats do not sound identical.")]
        [SerializeField, Min(0f)] private float screamMaxStartOffset = 0.3f;
        [Tooltip("An ordinary fall (no ragdoll) screams from this fall speed (m/s, about a 10 m drop).")]
        [SerializeField, Min(1f)] private float screamFallSpeed = 14f;
        [Tooltip("Optional: no screams while seated in a vehicle.")]
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;

        [Header("Ragdoll Fall (no parachute)")]
        [Tooltip("Landing speed (m/s) up to which the fall does not hurt ...")]
        [SerializeField, Min(0f)] private float safeImpactSpeed = 9f;
        [Tooltip("... and from which it kills (in between the damage grows with the speed).")]
        [SerializeField, Min(1f)] private float lethalImpactSpeed = 24f;
        [Tooltip("Safety limit (s) of a ragdoll fall.")]
        [SerializeField, Min(2f)] private float maxFallTime = 40f;

        /// <summary>True while the character tumbles down as a ragdoll from <see cref="StartRagdollFall"/>.</summary>
        public bool IsRagdollFalling { get; private set; }
        /// <summary>
        /// A hard landing (above Safe Impact Speed): the character, the point (hips) and how hard (0 = just hurts, 1 = lethal
        /// speed or more). Blood effects listen.
        /// </summary>
        public static event Action<PlayerFreeFall, Vector3, float> OnHardLanding;

        /// <summary>True when a parachute bag is on the back (the fall is then the stable pose).</summary>
        public bool HasParachuteBag => parachute != null && parachute.State == ParachuteState.Equipped;

        private Transform hips;
        private Rigidbody hipsBody;
        private Rigidbody body;
        private Rigidbody[] limbs = new Rigidbody[0];
        private AudioSource[] screamSources = new AudioSource[0];
        private int screamIndex;
        private int lastScreamClip = -1;
        private float nextScreamTime;
        private bool screaming;
        private bool ragdollImpactDone;
        private float airPeakFall;
        private bool wasAirborne;
        private int[] screamTokens = new int[2];
        private Coroutine fallRoutine;
        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private float weight;
        private float time;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private void Reset()
        {
            controller = GetComponent<vThirdPersonController>();
            animator = GetComponent<Animator>();
            parachute = GetComponent<PlayerParachute>();
            ragdoll = GetComponent<vRagdoll>();
            vehicleInteraction = GetComponent<PlayerVehicleInteraction>();
        }

        private void Awake()
        {
            if (controller == null || animator == null) Reset();
            if (controller == null || animator == null)
            {
                Debug.LogError($"[{nameof(PlayerFreeFall)}] Missing Invector references on '{name}'.", this);
                enabled = false;
                return;
            }
            if (animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) hipsBody = hips.GetComponent<Rigidbody>();
            Rigidbody root = GetComponent<Rigidbody>();
            body = root;
            var list = new System.Collections.Generic.List<Rigidbody>();
            foreach (Rigidbody limb in GetComponentsInChildren<Rigidbody>(true))
                if (limb != root) list.Add(limb);
            limbs = list.ToArray();
        }

        private void OnDisable()
        {
            DestroyGraph();
            IsRagdollFalling = false;
            StopScreams(true);
            airPeakFall = 0f;
            wasAirborne = false;
        }

        private void OnDestroy() => DestroyGraph();

        // ------------------------------------------------------------------ stable fall

        private void Update()
        {
            UpdateFall();
            UpdatePose();
        }

        private void UpdatePose()
        {
            bool stable = parachute != null && parachute.State == ParachuteState.Equipped && parachute.CanDeploy
                          && !controller.ragdolled && !controller.isDead && hips != null;
            float target = stable ? 1f : 0f;
            if (target > 0f && !graph.IsValid() && !BuildGraph()) return;
            if (!graph.IsValid()) return;
            weight = Mathf.MoveTowards(weight, target, Time.deltaTime / skydiveBlendTime);
            output.SetWeight(weight);
            if (weight <= 0f && target <= 0f) DestroyGraph();
        }

        // Air drag of the spread body: the fall speed is held at the terminal speed (after Invector's extra gravity this step).
        private void FixedUpdate()
        {
            if (weight < 0.5f || body == null || body.isKinematic) return;
            Vector3 v = body.linearVelocity;
            if (v.y < -skydiveTerminalSpeed) body.linearVelocity = new Vector3(v.x, Mathf.MoveTowards(v.y, -skydiveTerminalSpeed, 30f * Time.fixedDeltaTime), v.z);
        }

        // After the Animator (and the parachute's hang pose): the spread pose, then the whole body pitched belly-down at the hips.
        private void LateUpdate()
        {
            if (!graph.IsValid() || weight <= 0f) return;
            time += Time.deltaTime;
            graph.Evaluate(Time.deltaTime);
            float angle = skydivePitch * Mathf.SmoothStep(0f, 1f, weight);
            hips.rotation = Quaternion.AngleAxis(angle, transform.right) * hips.rotation;
        }

        private bool BuildGraph()
        {
            if (skydiveClip == null) return false;
            graph = PlayableGraph.Create(name + " Free Fall");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); // the Animator runs in Animate Physics: evaluated in LateUpdate
            var playable = AnimationClipPlayable.Create(graph, skydiveClip);
            playable.SetApplyFootIK(false);
            output = AnimationPlayableOutput.Create(graph, "Free Fall", animator);
            output.SetSourcePlayable(playable);
            weight = 0f;
            output.SetWeight(0f);
            graph.Play();
            return true;
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            weight = 0f;
        }

        // ------------------------------------------------------------------ screams and hard landings

        private bool OnFoot => vehicleInteraction == null || vehicleInteraction.State == VehicleInteractionState.OnFoot;
        private bool HoldsParachute => parachute != null
            && (parachute.State == ParachuteState.Gliding || (parachute.State == ParachuteState.Equipped && parachute.CanDeploy));

        private void UpdateFall()
        {
            bool dead = controller.isDead;
            // Ordinary (non-ragdoll) fall: the peak fall speed while off the ground, judged when it ends.
            bool ordinaryAir = !IsRagdollFalling && OnFoot && !controller.isGrounded && !controller.ragdolled && body != null && !body.isKinematic
                               && (parachute == null || parachute.State != ParachuteState.Gliding);
            if (ordinaryAir)
            {
                airPeakFall = Mathf.Max(airPeakFall, -body.linearVelocity.y);
                wasAirborne = true;
            }
            else if (wasAirborne)
            {
                wasAirborne = false;
                // Not when the canopy opened (it caught the fall) or the fall became a ragdoll fall (judged there).
                bool caught = parachute != null && parachute.State == ParachuteState.Gliding;
                bool landed = !caught && !IsRagdollFalling && OnFoot && (controller.isGrounded || controller.ragdolled || dead);
                if (landed && airPeakFall > safeImpactSpeed) RaiseHardLanding(airPeakFall);
                airPeakFall = 0f;
            }

            bool fallingHelplessly = !dead && ((IsRagdollFalling && !ragdollImpactDone)
                || (ordinaryAir && !HoldsParachute && -body.linearVelocity.y >= screamFallSpeed));
            if (fallingHelplessly)
            {
                if (!screaming || Time.time >= nextScreamTime) PlayScream();
            }
            else if (screaming) StopScreams(false);
        }

        private void RaiseHardLanding(float speed)
        {
            OnHardLanding?.Invoke(this, hips != null ? hips.position : transform.position, Mathf.InverseLerp(safeImpactSpeed, lethalImpactSpeed, speed));
        }

        // ------------------------------------------------------------------ ragdoll fall

        /// <summary>
        /// Throws the character as Invector's ragdoll with <paramref name="velocity"/> (and a tumble), screaming; the landing
        /// hurts by the fall speed. False when there is no ragdoll or the character is dead.
        /// </summary>
        public bool StartRagdollFall(Vector3 velocity, Vector3 angularVelocity, Transform sender)
        {
            if (!isActiveAndEnabled || ragdoll == null || controller.isDead) return false;
            var damage = new vDamage(0, true)
            {
                activeRagdoll = true,
                senselessTime = 1.5f,
                ignoreDefense = true,
                sender = sender,
                hitPosition = hips != null ? hips.position : transform.position,
                force = velocity,
                damageType = "FreeFall"
            };
            controller.TakeDamage(damage);
            if (!ragdoll.isActive) ragdoll.ActivateRagdoll(damage);
            Vector3 core = hips != null ? hips.position : transform.position;
            for (int i = 0; i < limbs.Length; i++)
            {
                Rigidbody limb = limbs[i];
                if (limb == null || limb.isKinematic) continue;
                limb.linearVelocity = velocity + Vector3.Cross(angularVelocity, limb.worldCenterOfMass - core);
                limb.angularVelocity = angularVelocity;
            }
            ragdollImpactDone = false;
            airPeakFall = 0f;
            wasAirborne = false;
            if (fallRoutine != null) StopCoroutine(fallRoutine);
            PlayScream();
            fallRoutine = StartCoroutine(RagdollFallRoutine());
            return true;
        }

        private IEnumerator RagdollFallRoutine()
        {
            IsRagdollFalling = true;
            float peakFall = 0f;
            float timer = 0f;
            while (timer < maxFallTime && !controller.isDead)
            {
                timer += Time.deltaTime;
                Vector3 v = hipsBody != null ? hipsBody.linearVelocity : Vector3.zero;
                peakFall = Mathf.Max(peakFall, -v.y);
                // Down: the ground right under the hips, or the fall has stopped after picking up speed.
                bool nearGround = hips != null && GroundBelow(hips.position, 0.7f);
                if (timer > 0.3f && (nearGround || (peakFall > safeImpactSpeed && -v.y < peakFall * 0.25f)))
                {
                    ragdollImpactDone = true;
                    StopScreams(false);
                    ApplyImpact(peakFall);
                    break;
                }
                yield return null;
            }
            ragdollImpactDone = true;
            // Invector's ragdoll gets up by itself once the body has settled.
            while (timer < maxFallTime && !controller.isDead && (ragdoll.isActive || controller.ragdolled))
            {
                timer += Time.deltaTime;
                yield return null;
            }
            if (timer >= maxFallTime && ragdoll.isActive && !controller.isDead) ragdoll.RestoreRagdoll();
            IsRagdollFalling = false;
        }

        private void ApplyImpact(float speed)
        {
            if (speed <= safeImpactSpeed) return;
            RaiseHardLanding(speed);
            float share = Mathf.InverseLerp(safeImpactSpeed, lethalImpactSpeed, speed);
            int hurt = speed >= lethalImpactSpeed ? Mathf.CeilToInt(controller.currentHealth) + 1 : Mathf.RoundToInt(share * controller.MaxHealth);
            if (hurt <= 0) return;
            controller.TakeDamage(new vDamage(hurt, true)
            {
                ignoreDefense = true,
                activeRagdoll = true,
                hitPosition = hips != null ? hips.position : transform.position,
                damageType = "FallImpact"
            });
        }

        private bool GroundBelow(Vector3 point, float distance)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.1f, Vector3.down, Hits, distance + 0.1f, controller.groundLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!Hits[i].collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        // A new scream on one of two sources; the one still sounding fades out under it.
        private void PlayScream()
        {
            if (screams == null || screams.Length == 0) return;
            int pick = UnityEngine.Random.Range(0, screams.Length);
            if (screams.Length > 1 && pick == lastScreamClip) pick = (pick + 1) % screams.Length;
            AudioClip clip = screams[pick];
            if (clip == null) return;
            lastScreamClip = pick;
            EnsureScreamSources();
            AudioSource previous = screamSources[screamIndex];
            screamIndex = (screamIndex + 1) % screamSources.Length;
            AudioSource source = screamSources[screamIndex];
            if (previous.isPlaying) StartCoroutine(FadeOut(screamIndex == 0 ? 1 : 0));
            screamTokens[screamIndex]++;
            source.Stop();
            source.clip = clip;
            source.pitch = screamPitch * (1f + UnityEngine.Random.Range(-screamPitchJitter, screamPitchJitter));
            source.volume = screamVolume * UnityEngine.Random.Range(0.85f, 1f);
            source.time = Mathf.Min(UnityEngine.Random.Range(0f, screamMaxStartOffset), Mathf.Max(0f, clip.length - 0.5f));
            source.Play();
            screaming = true;
            float interval = UnityEngine.Random.Range(Mathf.Min(screamInterval.x, screamInterval.y), Mathf.Max(screamInterval.x, screamInterval.y));
            // Never later than the clip's end (so there is no silence between screams).
            float remaining = (clip.length - source.time) / Mathf.Max(0.1f, source.pitch);
            nextScreamTime = Time.time + Mathf.Min(interval, Mathf.Max(0.5f, remaining - 0.1f));
        }

        private void StopScreams(bool immediate)
        {
            screaming = false;
            for (int i = 0; i < screamSources.Length; i++)
            {
                if (screamSources[i] == null || !screamSources[i].isPlaying) continue;
                if (immediate || !isActiveAndEnabled) screamSources[i].Stop();
                else StartCoroutine(FadeOut(i));
            }
        }

        private IEnumerator FadeOut(int index)
        {
            AudioSource source = screamSources[index];
            int token = screamTokens[index];
            float start = source.volume;
            for (float t = 0f; t < screamFadeTime; t += Time.deltaTime)
            {
                // A new scream took the source over meanwhile (it set its own volume): leave it.
                if (screamTokens[index] != token || !source.isPlaying) yield break;
                source.volume = start * (1f - t / screamFadeTime);
                yield return null;
            }
            if (screamTokens[index] == token) source.Stop();
        }

        private void EnsureScreamSources()
        {
            if (screamSources.Length == 2 && screamSources[0] != null && screamSources[1] != null) return;
            screamSources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0.3f;
                source.minDistance = 5f;
                source.maxDistance = 60f;
                screamSources[i] = source;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnHardLanding = null;

#if UNITY_EDITOR
        public void EditorSetup(vRagdoll characterRagdoll, AnimationClip skydive, AudioClip[] fallScreams)
        {
            Reset();
            ragdoll = characterRagdoll;
            skydiveClip = skydive;
            screams = fallScreams;
        }
#endif
    }
}
