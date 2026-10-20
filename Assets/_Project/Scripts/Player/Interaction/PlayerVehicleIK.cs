using System;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.Serialization;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// IK corrections for the vehicle enter/exit clips, driven by <see cref="PlayerVehicleInteraction"/>.
    /// The clips are generic, so on a low car:
    /// - Duck: the body is lowered (driver seat space) while the head passes under the roof, following a per-clip curve.
    /// - Feet: a foot inside the cabin is lifted straight up onto the cabin floor (legs never show under the vehicle)
    ///   and a foot outside never sinks below the ground. Feet are pinned while ducking, so only the knees bend.
    /// Corrections are smoothed and the IK weight never flips, so the legs keep their animated motion without popping.
    /// - Hand: one hand can be pulled onto a moving target (e.g. grabbing a driver's collar during a carjack), faded in and out.
    /// - Seated lean: the torso is reclined onto the seat back (the clips sit bolt upright); faded in while settling onto the
    ///   seat and out again at the start of the exit, so the swap with the vehicle's reclined driver visual is seamless.
    /// Must sit on the same GameObject as the Animator (OnAnimatorIK); the Base Layer needs "IK Pass" enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleIK : MonoBehaviour
    {
        private const int BaseLayer = 0;

        public enum Clip
        {
            Enter,
            Exit
        }

        [Serializable]
        private class ClipSettings
        {
            [Tooltip("Keep the feet on the cabin floor / ground during this clip. When off, the feet play as animated " +
                     "(if the clip starts at full weight, the correction fades out over Fade Duration instead of popping).")]
            public bool footIK = true;
            [Tooltip("Also lift feet that are inside the cabin up onto the cabin floor. When off, only the ground limit applies.")]
            public bool cabinFloor = true;
            [Tooltip("Body offset in driver seat space (x = toward the passenger side, y = up, z = forward) at Curve value 1.")]
            public Vector3 offset = new Vector3(0f, -0.2f, 0f);
            [Tooltip("Head duck weight over the clip's normalized time. Keep it 0 at both ends.")]
            public AnimationCurve curve = AnimationCurve.Constant(0f, 1f, 0f);
            [Tooltip("Raises the whole pose (body and feet together) by this many metres, weighted by Lift Curve. " +
                     "Use when the clip's feet sit below the ground compared with the locomotion animations.")]
            public float liftHeight;
            [Tooltip("Lift weight over the clip's normalized time. It fades out with the IK when on-foot control returns.")]
            public AnimationCurve liftCurve = AnimationCurve.Constant(0f, 1f, 0f);
        }

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Clips")]
        [SerializeField, FormerlySerializedAs("enterDuck")] private ClipSettings enterSettings = new ClipSettings();
        [SerializeField, FormerlySerializedAs("exitDuck")] private ClipSettings exitSettings = new ClipSettings();

        [Header("Feet")]
        [Tooltip("Distance (m) past the cabin floor's door-sill edge over which the floor correction blends in.")]
        [SerializeField, Min(0.01f)] private float sillBlendWidth = 0.2f;
        [Tooltip("Smoothing time (s) of each foot's correction, so a foot crossing the sill or touching the floor eases instead of snapping.")]
        [SerializeField, Min(0f)] private float correctionSmoothTime = 0.1f;
        [Tooltip("Seconds to fade the IK in/out. The weight stays continuous in between so the legs never pop between animated and corrected.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;
        [Tooltip("The cabin floor lift never brings a foot closer than this (m) to its hip, so the knee can't jam up while getting out.")]
        [SerializeField, Min(0f)] private float minLegReach = 0.4f;
        [Tooltip("Minimum height (m) of the toes above the ground. The ankle is raised further when the animated foot points its toes down.")]
        [SerializeField, Min(0f)] private float toeClearance = 0.015f;

        [Header("Hand")]
        [Tooltip("Seconds to fade the hand IK in/out.")]
        [SerializeField, Min(0.01f)] private float handFadeDuration = 0.2f;
        [Tooltip("Distance (m) the hand stays in front of the target bone, on the side facing the character.")]
        [SerializeField, Min(0f)] private float handSurfaceOffset = 0.12f;

        [Header("Seated Lean")]
        [Tooltip("Degrees the torso is rotated back about the hips when seated (the seated clip sits ~8 degrees forward; 23 gives a " +
                 "15 degree recline onto the seat back). Must match the vehicles' seated driver visuals (VehicleFactory).")]
        [SerializeField, Range(0f, 40f)] private float seatedLeanDegrees = 23f;
        [Tooltip("Share of the lean the head turns back forward, so the driver keeps looking at the road.")]
        [SerializeField, Range(0f, 1f)] private float seatedLeanHeadLevel = 0.55f;

        public bool IsActive => vehicle != null;

        private Transform handTarget;
        private float handTargetOffset;
        private AvatarIKGoal handGoal = AvatarIKGoal.LeftHand;
        private float handWeight;
        private float handTargetWeight;

        private float leanWeight;
        private float leanTargetWeight;
        private float leanFadeDuration = 0.3f;

        private IVehicleController vehicle;
        private ClipSettings settings;
        private float footWeight;
        private float footTargetWeight;
        private float groundHeight;
        private float clipTime;
        private float weight;
        private float targetWeight;
        private Vector3 leftOffset;
        private Vector3 rightOffset;
        private Vector3 leftOffsetVelocity;
        private Vector3 rightOffsetVelocity;
        private bool snapOffsets;
        private Transform leftUpperLeg;
        private Transform rightUpperLeg;
        private Transform leftFootBone;
        private Transform rightFootBone;
        private Transform leftToes;
        private Transform rightToes;
        private Transform spine;
        private Transform chest;
        private Transform upperChest;
        private Transform head;

        private void Awake()
        {
            if (animator == null)
            {
                Debug.LogError($"[{nameof(PlayerVehicleIK)}] Animator is not assigned on '{name}'. Vehicle IK is disabled.", this);
                enabled = false;
                return;
            }

            // Optional bones (null-safe below): leg reach and toe clearance are skipped when a bone is missing.
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private void OnDisable()
        {
            End();
            leanWeight = 0f;
            leanTargetWeight = 0f;
        }

        /// <summary>
        /// Starts the corrections for a clip. ground = world height of the ground beside the vehicle.
        /// instant = full weight on the first frame (e.g. exit starting from the seated pose).
        /// </summary>
        public void Begin(IVehicleController targetVehicle, Clip clip, float ground, bool instant)
        {
            vehicle = targetVehicle;
            settings = clip == Clip.Enter ? enterSettings : exitSettings;
            groundHeight = ground;
            clipTime = 0f;
            targetWeight = 1f;
            footTargetWeight = settings.footIK ? 1f : 0f;
            if (instant)
            {
                weight = 1f;
                footWeight = 1f;
            }
            snapOffsets = true;
        }

        /// <summary>Normalized time of the current enter/exit clip (1 once it has finished).</summary>
        public void SetClipTime(float normalizedTime)
        {
            clipTime = Mathf.Clamp01(normalizedTime);
        }

        /// <summary>Fades the corrections out (use when the character is back on the ground).</summary>
        public void FadeOut()
        {
            targetWeight = 0f;
            footTargetWeight = 0f;
        }

        /// <summary>Pulls a hand onto a target (fades in). Pass null to fade the hand back to its animation.</summary>
        public void SetHandTarget(AvatarIKGoal goal, Transform target) => SetHandTarget(goal, target, handSurfaceOffset);

        /// <summary>
        /// Same, with the distance (m) the hand stays in front of the target toward the character (0 = on the target, e.g. a
        /// door handle).
        /// </summary>
        public void SetHandTarget(AvatarIKGoal goal, Transform target, float surfaceOffset)
        {
            if (target != null) handTargetOffset = surfaceOffset;
            if (target != null)
            {
                if (goal != handGoal) handWeight = 0f;
                handGoal = goal;
                handTarget = target;
            }
            handTargetWeight = target != null ? 1f : 0f;
        }

        /// <summary>
        /// Reclines the torso onto the seat back (target 1) or sits it back up (0), over fadeSeconds (instant = jump there now).
        /// Independent of the clip corrections: it stays on while seated, so the exit can start from the reclined pose.
        /// </summary>
        public void SetSeatedLean(float target, float fadeSeconds, bool instant)
        {
            leanTargetWeight = Mathf.Clamp01(target);
            leanFadeDuration = Mathf.Max(0.01f, fadeSeconds);
            if (instant) leanWeight = leanTargetWeight;
        }

        /// <summary>Stops the clip corrections immediately (the seated lean keeps its own state).</summary>
        public void End()
        {
            vehicle = null;
            weight = 0f;
            targetWeight = 0f;
            footWeight = 0f;
            footTargetWeight = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != BaseLayer) return;
            UpdateHand();
            UpdateSeatedLean();
            if (vehicle == null) return;
            if ((vehicle as UnityEngine.Object) == null)
            {
                End();
                return;
            }

            weight = Mathf.MoveTowards(weight, targetWeight, Time.deltaTime / fadeDuration);
            footWeight = Mathf.MoveTowards(footWeight, footTargetWeight, Time.deltaTime / fadeDuration);
            if (weight <= 0f && targetWeight <= 0f)
            {
                End();
                return;
            }

            // Animated foot goals are read before the body moves, so ducking bends the knees instead of sinking the feet.
            Vector3 leftAnimated = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightAnimated = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion leftRotation = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            Quaternion rightRotation = animator.GetIKRotation(AvatarIKGoal.RightFoot);

            Transform seat = vehicle.GetDriverSeat();
            float duckWeight = settings != null ? settings.curve.Evaluate(clipTime) * weight : 0f;
            if (seat != null && Mathf.Abs(duckWeight) > 0.0001f) animator.bodyPosition += seat.rotation * (settings.offset * duckWeight);

            // Lift moves body and feet together, so the pose keeps its shape and only its height changes.
            float lift = settings != null ? settings.liftHeight * settings.liftCurve.Evaluate(clipTime) * weight : 0f;
            if (Mathf.Abs(lift) > 0.0001f)
            {
                Vector3 up = seat != null ? seat.up : Vector3.up;
                animator.bodyPosition += up * lift;
                leftAnimated += up * lift;
                rightAnimated += up * lift;
            }

            Transform cabinFloor = settings != null && settings.cabinFloor ? vehicle.GetCabinFloor() : null;
            SetFootGoal(AvatarIKGoal.LeftFoot, leftAnimated, leftRotation, animator.leftFeetBottomHeight, cabinFloor, leftUpperLeg, leftFootBone, leftToes, ref leftOffset, ref leftOffsetVelocity);
            SetFootGoal(AvatarIKGoal.RightFoot, rightAnimated, rightRotation, animator.rightFeetBottomHeight, cabinFloor, rightUpperLeg, rightFootBone, rightToes, ref rightOffset, ref rightOffsetVelocity);
            snapOffsets = false;
        }

        // Torso rotated back about the character's right axis, split over the spine chain; the head turns part of it back so the
        // gaze stays level. Bone rotations are written through the humanoid rig (SetBoneLocalRotation), on top of the pose.
        private void UpdateSeatedLean()
        {
            leanWeight = Mathf.MoveTowards(leanWeight, leanTargetWeight, Time.deltaTime / leanFadeDuration);
            float degrees = seatedLeanDegrees * leanWeight;
            if (degrees <= 0.01f) return;

            Vector3 axis = transform.right;
            int links = (spine != null ? 1 : 0) + (chest != null ? 1 : 0) + (upperChest != null ? 1 : 0);
            if (links == 0) return;
            Quaternion back = Quaternion.AngleAxis(-degrees / links, axis);
            LeanBone(HumanBodyBones.Spine, spine, back);
            LeanBone(HumanBodyBones.Chest, chest, back);
            LeanBone(HumanBodyBones.UpperChest, upperChest, back);
            if (head != null) LeanBone(HumanBodyBones.Head, head, Quaternion.AngleAxis(degrees * seatedLeanHeadLevel, axis));
        }

        // Each link is rotated by the same world-space delta; since the deltas share one axis, a child's local rotation only
        // needs its own delta applied against its parent's unrotated frame.
        private void LeanBone(HumanBodyBones bone, Transform t, Quaternion worldDelta)
        {
            if (t == null || t.parent == null) return;
            Quaternion local = Quaternion.Inverse(t.parent.rotation) * (worldDelta * t.rotation);
            animator.SetBoneLocalRotation(bone, local);
        }

        private void UpdateHand()
        {
            if (handTarget == null)
            {
                handWeight = 0f;
                return;
            }
            handWeight = Mathf.MoveTowards(handWeight, handTargetWeight, Time.deltaTime / handFadeDuration);
            if (handWeight <= 0f && handTargetWeight <= 0f)
            {
                handTarget = null;
                return;
            }
            // The hand rests on the surface facing the character (collar/shoulder), not inside the target bone.
            Vector3 toCharacter = animator.bodyPosition - handTarget.position;
            toCharacter.y = 0f;
            Vector3 position = handTarget.position + (toCharacter.sqrMagnitude > 0.0001f ? toCharacter.normalized * handTargetOffset : Vector3.zero);
            animator.SetIKPositionWeight(handGoal, handWeight);
            animator.SetIKPosition(handGoal, position);
        }

        private void SetFootGoal(AvatarIKGoal goal, Vector3 animated, Quaternion animatedRotation, float soleOffset, Transform cabinFloor,
            Transform upperLeg, Transform footBone, Transform toes, ref Vector3 offset, ref Vector3 offsetVelocity)
        {
            Vector3 position = animated;

            if (cabinFloor != null)
            {
                // Floor space: +x points from the door sill into the cabin, +y is up.
                Vector3 local = cabinFloor.InverseTransformPoint(position);
                float inside = Mathf.Clamp01(local.x / sillBlendWidth);
                float raise = Mathf.Max(0f, soleOffset - local.y) * inside;
                if (raise > 0f)
                {
                    local.y += raise;
                    position = cabinFloor.TransformPoint(local);
                }

                // Leg reach: a lifted foot that would end up too close to its hip is lowered back (never below its animated height).
                // The hip comes from the last evaluated pose, which is close enough for this limit.
                if (raise > 0f && upperLeg != null && minLegReach > 0f)
                {
                    Vector3 hip = upperLeg.position;
                    Vector3 toFoot = position - hip;
                    float horizontalSqr = toFoot.x * toFoot.x + toFoot.z * toFoot.z;
                    if (toFoot.sqrMagnitude < minLegReach * minLegReach && horizontalSqr < minLegReach * minLegReach)
                    {
                        float reachY = hip.y - Mathf.Sqrt(minLegReach * minLegReach - horizontalSqr);
                        position.y = Mathf.Max(animated.y, Mathf.Min(position.y, reachY));
                    }
                }
            }

            // The floor correction (not the animation) is smoothed, so the foot keeps its animated motion but never snaps.
            Vector3 targetOffset = position - animated;
            if (snapOffsets || correctionSmoothTime <= 0f)
            {
                offset = targetOffset;
                offsetVelocity = Vector3.zero;
            }
            else
            {
                offset = Vector3.SmoothDamp(offset, targetOffset, ref offsetVelocity, correctionSmoothTime);
            }

            Vector3 target = animated + offset * footWeight;

            // Ground limit is applied last and never smoothed, so a foot stepping down can't lag through the ground.
            // Toe clearance uses how far the toes hang below the ankle in the last evaluated pose (foot rotation is left animated).
            float toeDrop = footBone != null && toes != null ? footBone.position.y - toes.position.y : 0f;
            float minHeight = groundHeight + Mathf.Max(soleOffset, toeDrop + toeClearance);
            if (target.y < minHeight) target.y = minHeight;

            // Same weight every frame (an uncorrected foot just targets its animated position), so the knee solution never flips.
            // Feet stay pinned at full weight, so ducking never sinks them. The animated foot rotation is pinned too:
            // without it the foot follows the re-bent shin and tips its toes into the ground.
            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKPosition(goal, target);
            animator.SetIKRotationWeight(goal, weight);
            animator.SetIKRotation(goal, animatedRotation);
        }
    }
}
