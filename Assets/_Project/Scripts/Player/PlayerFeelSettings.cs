using System;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// On-foot feel for every playable character (Jason and Mia), applied at startup to the listed Invector components
    /// (the character objects themselves are not edited):
    /// - Free locomotion: Invector's stock walk-run behaviour. No walk-by-default, so the input magnitude drives the single
    ///   Free Locomotion blend tree (idle 0, walk 0.5, run 1) smoothly; the sprint input still sprints (1.5).
    ///   An analog stick / the touch joystick shifts from walk to run with how far it is pushed.
    /// - Head: no head / body look while moving freely or standing (the head stays with the animation, it does not follow
    ///   the camera when looking up or down). Aiming and strafing keep their own head-track weights.
    /// - Animator controllers: a character can get a different (our own) override controller, e.g. Mia's with a short idle
    ///   loop: her 13 s idle is time-synced with the 1.2 s walk in the locomotion blend tree, which slowed the walk
    ///   several times over whenever she started or stopped moving.
    /// - Stride match: each character can have its own speeds and the natural ground speeds of its walk / run / sprint clips
    ///   (as played in the blend tree). While moving freely on foot the locomotion playback is scaled by
    ///   actual speed / natural speed at the current blend point, so the planted feet do not slide.
    /// Lives under ///Manager/// (not a Singleton). Both characters are listed, so the inactive one is set up too.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class PlayerFeelSettings : MonoBehaviour
    {
        [Header("Characters")]
        [SerializeField] private vThirdPersonController[] controllers = new vThirdPersonController[0];
        [SerializeField] private vHeadTrack[] headTracks = new vHeadTrack[0];

        [Serializable]
        public class ControllerOverride
        {
            public Animator animator;
            public RuntimeAnimatorController controller;
        }

        [Tooltip("Animator controllers assigned at startup (our own override controllers).")]
        [SerializeField] private ControllerOverride[] animatorControllers = new ControllerOverride[0];

        [Header("Free locomotion (Invector stock values)")]
        [SerializeField] private bool walkByDefault = false;
        [SerializeField, Min(0f)] private float walkSpeed = 2f;
        [SerializeField, Min(0f)] private float runningSpeed = 4f;
        [SerializeField, Min(0f)] private float sprintSpeed = 6f;
        [Tooltip("How quickly the speed follows the input.")]
        [SerializeField, Min(0f)] private float movementSmooth = 6f;
        [Tooltip("How smoothly the walk / run blend follows the speed.")]
        [SerializeField, Min(0f)] private float animationSmooth = 0.05f;

        [Serializable]
        public class CharacterProfile
        {
            [Tooltip("PlayableCharacter id of the listed controller this profile is for.")]
            public string characterId;
            [Tooltip("Movement speeds for this character (0 = the shared values above).")]
            [Min(0f)] public float walkSpeed, runningSpeed, sprintSpeed;
            [Tooltip("Ground speed (m/s) of the walk / run / sprint animation as played in the locomotion blend tree " +
                     "(the speed at which the planted foot stays put, measured in play).")]
            [Min(0.1f)] public float naturalWalk = 2f, naturalRun = 4f, naturalSprint = 6f;
            [NonSerialized] public vThirdPersonController controller;

            public CharacterProfile() { }
            public CharacterProfile(string id, float walk, float run, float sprint, float natWalk, float natRun, float natSprint)
            {
                characterId = id; walkSpeed = walk; runningSpeed = run; sprintSpeed = sprint;
                naturalWalk = natWalk; naturalRun = natRun; naturalSprint = natSprint;
            }
        }

        [Header("Stride match (per character)")]
        [Tooltip("Natural speeds measured in play (planted-foot drift = 0): Jason walk 1.79, run 2.6, sprint 6.7 m/s; " +
                 "Mia walk 1.01, run 2.92, sprint 5.3 m/s. Without walk-by-default Invector moves at running speed x stick input, " +
                 "so the running speed also sets the half-stick walk speed.")]
        [SerializeField] private CharacterProfile[] profiles =
        {
            new CharacterProfile("jason", 2f, 3.4f, 7f, 1.79f, 2.6f, 6.7f),
            new CharacterProfile("girl", 1.4f, 3.4f, 6f, 1.01f, 2.92f, 5.3f),
        };
        [SerializeField] private bool matchStride = true;
        [Tooltip("Limits of the locomotion playback scale.")]
        [SerializeField] private Vector2 playbackRange = new Vector2(0.6f, 1.7f);
        [Tooltip("How quickly the playback scale follows (per second).")]
        [SerializeField, Min(0.1f)] private float playbackSmooth = 10f;

        [Header("Head track outside aiming")]
        [SerializeField, Range(0f, 1f)] private float freeHeadWeight = 0f;
        [SerializeField, Range(0f, 1f)] private float freeBodyWeight = 0f;

        private void Awake() => Apply();

        /// <summary>Applies the settings to every listed character (also callable after changing values).</summary>
        public void Apply()
        {
            for (int i = 0; i < animatorControllers.Length; i++)
            {
                ControllerOverride o = animatorControllers[i];
                if (o != null && o.animator != null && o.controller != null && o.animator.runtimeAnimatorController != o.controller)
                    o.animator.runtimeAnimatorController = o.controller;
            }
            for (int i = 0; i < controllers.Length; i++)
            {
                vThirdPersonController c = controllers[i];
                if (c == null || c.freeSpeed == null) continue;
                c.freeSpeed.walkByDefault = walkByDefault;
                c.freeSpeed.walkSpeed = walkSpeed;
                c.freeSpeed.runningSpeed = runningSpeed;
                c.freeSpeed.sprintSpeed = sprintSpeed;
                c.freeSpeed.movementSmooth = movementSmooth;
                c.freeSpeed.animationSmooth = animationSmooth;
            }
            for (int i = 0; i < profiles.Length; i++)
            {
                CharacterProfile p = profiles[i];
                if (p == null) continue;
                p.controller = FindController(p.characterId);
                if (p.controller == null || p.controller.freeSpeed == null) continue;
                if (p.walkSpeed > 0f) p.controller.freeSpeed.walkSpeed = p.walkSpeed;
                if (p.runningSpeed > 0f) p.controller.freeSpeed.runningSpeed = p.runningSpeed;
                if (p.sprintSpeed > 0f) p.controller.freeSpeed.sprintSpeed = p.sprintSpeed;
                p.controller.freeSpeed.Init(); // these are now the controller's defaults (ResetToDefault keeps them)
            }
            for (int i = 0; i < headTracks.Length; i++)
            {
                if (headTracks[i] == null) continue;
                headTracks[i].freeHeadWeight = freeHeadWeight;
                headTracks[i].freeBodyWeight = freeBodyWeight;
            }
        }

        private vThirdPersonController FindController(string id)
        {
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] == null) continue;
                var pc = controllers[i].GetComponent<PlayableCharacter>();
                if (pc != null && pc.id == id) return controllers[i];
            }
            return null;
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }

        private static readonly int FreeMovementState = Animator.StringToHash("Free Movement");
        private static readonly int InputMagnitudeParam = Animator.StringToHash("InputMagnitude");
        private CharacterProfile scaledProfile;
        private float playback = 1f;

        // Per frame for the one character being played: scales the locomotion playback to its ground speed.
        // It only owns animator.speed while the character is in free movement; outside it (car enter / exit, ladders,
        // attacks, aiming) the speed is handed back at 1, and never over a value another system has set.
        private void Update()
        {
            CharacterProfile active = null;
            if (matchStride)
                for (int i = 0; i < profiles.Length; i++)
                    if (profiles[i] != null && profiles[i].controller != null && profiles[i].controller.isActiveAndEnabled) { active = profiles[i]; break; }
            if (active != scaledProfile) { ReleasePlayback(); scaledProfile = active; }
            if (active == null) return;

            vThirdPersonController c = active.controller;
            Animator animator = c.animator;
            if (animator == null || !IsFreeMoving(c, animator)) { ReleasePlayback(); return; }

            float target = 1f;
            float blend = animator.GetFloat(InputMagnitudeParam);
            if (blend > 0.15f)
            {
                Vector3 v = c._rigidbody != null ? c._rigidbody.linearVelocity : Vector3.zero;
                v.y = 0f;
                float natural = NaturalSpeed(active, blend);
                if (natural > 0.05f) target = Mathf.Clamp(v.magnitude / natural, playbackRange.x, playbackRange.y);
            }
            playback = Mathf.MoveTowards(playback, target, playbackSmooth * Time.deltaTime);
            animator.speed = playback;
            owning = true;
        }

        private static bool IsFreeMoving(vThirdPersonController c, Animator animator)
        {
            if (animator.runtimeAnimatorController == null || c.isStrafing || c.customAction || !c.isGrounded || c.IsAnimatorTag("Attack"))
                return false;
            if (animator.IsInTransition(0)) return animator.GetNextAnimatorStateInfo(0).shortNameHash == FreeMovementState;
            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == FreeMovementState;
        }

        // Natural ground speed at a blend value: idle 0, walk 0.5, run 1, sprint 1.5 (the blend tree's thresholds).
        private static float NaturalSpeed(CharacterProfile p, float blend)
        {
            if (blend <= 0.5f) return Mathf.Lerp(0f, p.naturalWalk, blend / 0.5f);
            if (blend <= 1f) return Mathf.Lerp(p.naturalWalk, p.naturalRun, (blend - 0.5f) / 0.5f);
            return Mathf.Lerp(p.naturalRun, p.naturalSprint, Mathf.Clamp01((blend - 1f) / 0.5f));
        }

        private bool owning;

        // Back to normal playback, unless another system has set its own animator speed since our last write.
        private void ReleasePlayback()
        {
            if (owning && scaledProfile != null && scaledProfile.controller != null && scaledProfile.controller.animator != null)
            {
                Animator animator = scaledProfile.controller.animator;
                if (Mathf.Abs(animator.speed - playback) < 0.0001f) animator.speed = 1f;
            }
            owning = false;
            playback = 1f;
        }

        private void OnDisable() => ReleasePlayback();
    }
}
