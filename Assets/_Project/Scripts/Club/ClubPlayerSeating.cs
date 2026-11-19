using System;
using System.Collections;
using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenworldCrime.Club
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Sits the player (Jason or Mia) on a club seat and stands him up again. While seated the Invector controller and
    /// input are off, the body is frozen (<see cref="InvectorGamePlayer.SetPosed"/>), a Humanoid sit loop plays through a
    /// small PlayableGraph on the player's own Animator (no controller changes) and the camera holds the seat's framing.
    /// A masked upper-body layer plays the cash-throw gesture on request.
    /// One per club scene (referenced by the seats, the cash thrower, the private dance and the club HUD).
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubPlayerSeating : MonoBehaviour
    {
        [Header("Poses (Humanoid)")]
        [SerializeField] private AnimationClip maleSitClip;
        [SerializeField] private AnimationClip femaleSitClip;
        [Tooltip("PlayableCharacter ids that use the female sit pose.")]
        [SerializeField] private string[] femaleCharacterIds = { "girl" };
        [Tooltip("Arm gesture played over the sit pose when cash is thrown.")]
        [SerializeField] private AnimationClip throwGestureClip;
        [SerializeField, Range(0f, 1f)] private float gestureWeight = 0.85f;

        [Header("Camera")]
        [SerializeField, Min(0.01f)] private float cameraBlendSeconds = 0.6f;

        /// <summary>Raised when the player sits down (true) or has stood up (false).</summary>
        public event Action<bool> OnSeatedChanged;

        public bool IsSeated { get; private set; }
        public PlayerManager SeatedPlayer { get; private set; }
        /// <summary>The seated player's right hand (cash spawns here); null while standing.</summary>
        public Transform RightHand { get; private set; }

        private InvectorGamePlayer gamePlayer;
        private Animator animator;
        private Behaviour controllerBehaviour;
        private Behaviour inputBehaviour;
        private bool rootMotionBefore;
        private RuntimeAnimatorController savedController;
        private Transform seatPoint;
        private Transform cameraPoint;
        private PlayableGraph graph;
        private AnimationLayerMixerPlayable mixer;
        private AnimationClipPlayable gesture;
        private float gestureTimer;
        private Coroutine fitRoutine;

        private void Awake()
        {
            if (maleSitClip == null || femaleSitClip == null)
            {
                Debug.LogError($"[{nameof(ClubPlayerSeating)}] Sit clips are not assigned on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            if (IsSeated) StandUp(null);
        }

        /// <summary>Sits <paramref name="player"/> with his hips on <paramref name="seat"/> (facing its forward), camera at <paramref name="view"/>.</summary>
        public bool Sit(PlayerManager player, Transform seat, Transform view)
        {
            if (!enabled || IsSeated || player == null || seat == null) return false;
            gamePlayer = player.GetComponent<InvectorGamePlayer>();
            animator = player.GetComponent<Animator>();
            if (gamePlayer == null || animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"[{nameof(ClubPlayerSeating)}] '{player.name}' has no InvectorGamePlayer or Humanoid Animator.", this);
                return false;
            }

            SeatedPlayer = player;
            seatPoint = seat;
            cameraPoint = view;
            RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            player.SetInputLocked(true);
            controllerBehaviour = player.Controller;
            inputBehaviour = player.GetComponent<Invector.vCharacterController.vThirdPersonInput>();
            if (controllerBehaviour != null) controllerBehaviour.enabled = false;
            if (inputBehaviour != null) inputBehaviour.enabled = false;
            rootMotionBefore = animator.applyRootMotion;
            animator.applyRootMotion = false;
            // The Invector controller's own output would override the seat graph (its layers animate every bone), so it is
            // taken off while seated and given back on standing up (the state machine then restarts in locomotion).
            savedController = animator.runtimeAnimatorController;
            animator.runtimeAnimatorController = null;
            gamePlayer.SetPosed(true);
            player.transform.SetPositionAndRotation(seat.position - Vector3.up * 0.9f, Quaternion.Euler(0f, seat.eulerAngles.y, 0f));

            BuildGraph(IsFemale(player) ? femaleSitClip : maleSitClip);
            IsSeated = true;
            fitRoutine = StartCoroutine(FitAndFrame());
            OnSeatedChanged?.Invoke(true);
            return true;
        }

        /// <summary>Stands the player up at <paramref name="standPoint"/> (or in front of the seat when null).</summary>
        public void StandUp(Transform standPoint)
        {
            if (!IsSeated) return;
            IsSeated = false;
            if (fitRoutine != null) StopCoroutine(fitRoutine);
            fitRoutine = null;
            if (graph.IsValid()) graph.Destroy();
            gestureTimer = 0f;

            Vector3 position;
            float yaw;
            if (standPoint != null)
            {
                position = standPoint.position;
                yaw = standPoint.eulerAngles.y;
            }
            else
            {
                yaw = seatPoint.eulerAngles.y;
                position = seatPoint.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.75f;
                if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) position = hit.point;
            }

            Transform playerRoot = SeatedPlayer.transform;
            playerRoot.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (animator != null)
            {
                animator.runtimeAnimatorController = savedController;
                animator.applyRootMotion = rootMotionBefore;
            }
            savedController = null;
            if (controllerBehaviour != null) controllerBehaviour.enabled = true;
            if (inputBehaviour != null) inputBehaviour.enabled = true;
            gamePlayer.SetPosed(false, yaw);
            SeatedPlayer.SetInputLocked(false);

            SeatedPlayer = null;
            RightHand = null;
            seatPoint = cameraPoint = null;
            OnSeatedChanged?.Invoke(false);
        }

        /// <summary>Plays the arm gesture over the sit pose (cash throw). Restarts if already playing.</summary>
        public void PlayGesture()
        {
            if (!IsSeated || !gesture.IsValid()) return;
            gesture.SetTime(0.0);
            gestureTimer = (float)gesture.GetAnimationClip().length;
        }

        // Per-frame only while a gesture plays (weight envelope of the arm layer).
        private void Update()
        {
            if (!IsSeated || gestureTimer <= 0f || !mixer.IsValid()) return;
            gestureTimer -= Time.deltaTime;
            float length = (float)gesture.GetAnimationClip().length;
            float t = 1f - Mathf.Clamp01(gestureTimer / length);
            float envelope = Mathf.Clamp01(Mathf.Min(t / 0.15f, (1f - t) / 0.25f));
            mixer.SetInputWeight(1, envelope * gestureWeight);
        }

        private void BuildGraph(AnimationClip sitClip)
        {
            graph = PlayableGraph.Create(name + " Seat");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Seat", animator);
            mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            var sit = AnimationClipPlayable.Create(graph, sitClip);
            sit.SetApplyFootIK(false); // the generated pose has no IK goals; foot IK would pull the feet to the root
            sit.SetTime(UnityEngine.Random.Range(0f, sitClip.length));
            graph.Connect(sit, 0, mixer, 0);
            mixer.SetInputWeight(0, 1f);
            if (throwGestureClip != null)
            {
                gesture = AnimationClipPlayable.Create(graph, throwGestureClip);
                gesture.SetApplyFootIK(false);
                graph.Connect(gesture, 0, mixer, 1);
                mixer.SetLayerMaskFromAvatarMask(1, ArmsMask());
                mixer.SetInputWeight(1, 0f);
            }
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        // Puts the hips on the seat once the pose is applied, then eases the camera to the seat's framing.
        private IEnumerator FitAndFrame()
        {
            yield return null;
            yield return null;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) SeatedPlayer.transform.position += seatPoint.position - hips.position;

            if (cameraPoint == null) yield break;
            Transform cam = gamePlayer.PlayerCamera.transform;
            Vector3 fromPosition = cam.position;
            Quaternion fromRotation = cam.rotation;
            for (float t = 0f; t < cameraBlendSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / cameraBlendSeconds);
                cam.SetPositionAndRotation(Vector3.Lerp(fromPosition, cameraPoint.position, k), Quaternion.Slerp(fromRotation, cameraPoint.rotation, k));
                yield return null;
            }
            cam.SetPositionAndRotation(cameraPoint.position, cameraPoint.rotation);
            fitRoutine = null;
        }

        /// <summary>Snaps the camera to the seat framing (used behind a fade).</summary>
        public void SnapCamera()
        {
            if (IsSeated && cameraPoint != null) gamePlayer.PlayerCamera.transform.SetPositionAndRotation(cameraPoint.position, cameraPoint.rotation);
        }

        private bool IsFemale(PlayerManager player)
        {
            var character = player.GetComponent<PlayableCharacter>();
            if (character == null) return false;
            for (int i = 0; i < femaleCharacterIds.Length; i++)
                if (femaleCharacterIds[i] == character.id) return true;
            return false;
        }

        private static AvatarMask armsMask;

        private static AvatarMask ArmsMask()
        {
            if (armsMask != null) return armsMask;
            armsMask = new AvatarMask { name = "Club Throw Arms" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) armsMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            armsMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            armsMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            return armsMask;
        }
    }
}
