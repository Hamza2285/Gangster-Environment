using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Slims a character's shoulders, arms and hands by scaling the arm bones at runtime, without touching the FBX import.
    /// Each bone is scaled uniformly (no skew) and the bone below it is counter-scaled, so only that bone's own skinned part changes.
    /// Weapon holders, hit boxes and other attachments under the bones keep their world size and offset, and with
    /// "Keep Arm Length" the elbow and wrist stay where the animation and IK put them, so aiming, grips and seat IK are unaffected.
    /// Values come from a shared <see cref="BoneProportionsProfile"/>. Works on an animated humanoid (the player) and on a static
    /// posed copy without an Animator (the seated driver visual in a car). Runs before Invector's LateUpdate so its aim IK
    /// works on the final skeleton.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public class CharacterBoneProportions : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Shared proportions. Use the same asset on every copy of the same character.")]
        [SerializeField] private BoneProportionsProfile profile;
        [Tooltip("Humanoid Animator of the character. Leave empty on a static posed copy; bones are then found by name below.")]
        [SerializeField] private Animator animator;
        [Tooltip("Bone name prefix used to find the arm bones when there is no Animator (Mixamo rigs: \"mixamorig:\").")]
        [SerializeField] private string boneNamePrefix = "mixamorig:";

        [Header("Ragdoll")]
        [Tooltip("Re-anchors the character's ragdoll joints to the live, scaled skeleton once it is posed (and after every change), " +
                 "so the ragdoll keeps the limb lengths seen in game instead of pulling the limbs together.")]
        [SerializeField] private bool refitRagdollJoints = true;

        private enum BoneGroup { Shoulder, UpperArm, Forearm, Hand }

        private struct Attachment
        {
            public Transform Transform;
            public Vector3 BaseScale;
            public Vector3 BasePosition;
        }

        // One scaled bone, the next bone it counter-scales and the attachments (non-bone children) it compensates.
        private sealed class ScaledBone
        {
            public Transform Bone;
            public BoneGroup Group;
            public Vector3 RestScale;
            public ScaledBone Parent;            // scaled bone directly above (its scale is undone on this bone)
            public Transform NextBone;           // shoulder -> upper arm -> forearm -> hand (null for the hand)
            public Vector3 NextBoneRestPosition;
            public Rigidbody NextBoneBody;       // ragdoll body on the next bone, if any
            public float AppliedScale = 1f;
            public readonly List<Attachment> Attachments = new List<Attachment>();
        }

        private sealed class BoundsFix
        {
            public SkinnedMeshRenderer Renderer;
            public Bounds RestBounds;
            public ScaledBone RootBone;
        }

        private static readonly HumanBodyBones[,] ArmChains =
        {
            { HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            { HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }
        };

        // Mixamo names of the same bones, used without an Animator.
        private static readonly string[,] ArmChainNames =
        {
            { "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand" },
            { "RightShoulder", "RightArm", "RightForeArm", "RightHand" }
        };

        private static readonly BoneGroup[] ChainGroups = { BoneGroup.Shoulder, BoneGroup.UpperArm, BoneGroup.Forearm, BoneGroup.Hand };

        private readonly List<ScaledBone> scaledBones = new List<ScaledBone>();
        private readonly List<BoundsFix> boundsFixes = new List<BoundsFix>();
        private readonly HashSet<Transform> skeletonBones = new HashSet<Transform>();
        private CharacterJoint[] joints;
        private bool[] jointRestAutoConfigure;
        private readonly List<CharacterJoint> elbowJoints = new List<CharacterJoint>(); // watched for re-creation
        private bool jointsNeedRefit;
        private Transform poseProbe;           // a bone outside the arms, to tell when the Animator has posed the skeleton
        private Vector3 poseProbeRestPosition; // avatar position (what the Animator writes)
        private Vector3 poseProbeStartPosition; // prefab position before the first animation update
        private bool animated;
        private bool initialized;
        private bool applied;
        private bool appliedAfterStart;

        private void Awake()
        {
            if (profile == null)
            {
                Debug.LogError($"[{nameof(CharacterBoneProportions)}] No BoneProportionsProfile assigned on '{name}'. Bone proportions are disabled.", this);
                enabled = false;
                return;
            }

            animated = animator != null;
            if (animated && (!animator.isHuman || animator.avatar == null))
            {
                Debug.LogError($"[{nameof(CharacterBoneProportions)}] The Animator on '{name}' is not humanoid. Bone proportions are disabled.", this);
                enabled = false;
                return;
            }

            CacheBones();
        }

        private void OnEnable()
        {
            if (!initialized) return;
            profile.Changed += HandleProfileChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            profile.Changed -= HandleProfileChanged;
            Restore();
        }

        private void LateUpdate()
        {
            if (!applied) return;

            // Invector's vSnapToBody parents the weapon holders to the hands in Start; pick them up once all Starts have run.
            if (!appliedAfterStart)
            {
                appliedAfterStart = true;
                Apply();
            }

            // The humanoid Animator rewrites every bone's local position when it evaluates, so the length compensation is
            // re-applied each frame. Static copies keep what Apply set. While ragdolled the bodies belong to physics (their joints
            // hold the compensated length); bones without a body, like the hands, are still placed here.
            if (!animated) return;
            bool animatorDriven = animator.enabled;
            if (profile.KeepArmLength)
            {
                for (int i = 0; i < scaledBones.Count; i++)
                {
                    ScaledBone sb = scaledBones[i];
                    if (sb.NextBone == null || !IsArmSegment(sb.Group)) continue;
                    if (animatorDriven || sb.NextBoneBody == null || sb.NextBoneBody.isKinematic)
                        sb.NextBone.localPosition = sb.NextBoneRestPosition / sb.AppliedScale;
                }
            }

            // Only from an animated pose (the Animator has written the avatar skeleton; the prefab's own bone positions differ),
            // never while ragdolled, when the pose belongs to physics.
            if (joints.Length == 0 || !animatorDriven || !IsPosedByAnimator()) return;
            if (!jointsNeedRefit) jointsNeedRefit = ElbowJointsAreStale();
            if (jointsNeedRefit)
            {
                jointsNeedRefit = false;
                RefitJoints();
            }
        }

        private void HandleProfileChanged()
        {
            if (Application.isPlaying && applied) Apply();
        }

        private void CacheBones()
        {
            Dictionary<string, Vector3> avatarRest = null;
            if (animated)
            {
                avatarRest = new Dictionary<string, Vector3>();
                foreach (SkeletonBone bone in animator.avatar.humanDescription.skeleton) avatarRest[bone.name] = bone.position;
            }

            // Everything that is part of the skeleton (never treated as an attachment).
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in smr.bones)
                {
                    if (bone != null) skeletonBones.Add(bone);
                }
            }

            for (int side = 0; side < 2; side++)
            {
                ScaledBone parent = null;
                for (int i = 0; i < ChainGroups.Length; i++)
                {
                    Transform bone = FindArmBone(side, i);
                    if (bone == null)
                    {
                        Debug.LogWarning($"[{nameof(CharacterBoneProportions)}] '{name}' has no {ArmChains[side, i]} bone; that arm is skipped.", this);
                        break;
                    }

                    skeletonBones.Add(bone);
                    var sb = new ScaledBone
                    {
                        Bone = bone,
                        Group = ChainGroups[i],
                        RestScale = bone.localScale,
                        Parent = parent != null && bone.parent == parent.Bone ? parent : null
                    };
                    if (i < ChainGroups.Length - 1)
                    {
                        Transform next = FindArmBone(side, i + 1);
                        if (next != null && next.parent == bone)
                        {
                            sb.NextBone = next;
                            // Animated: the avatar position the Animator writes. Static copy: the posed position as authored.
                            sb.NextBoneRestPosition = animated && avatarRest.TryGetValue(next.name, out Vector3 rest) ? rest : next.localPosition;
                            sb.NextBoneBody = next.GetComponent<Rigidbody>();
                        }
                    }
                    scaledBones.Add(sb);
                    parent = sb;
                }
            }

            // Fingers and other bones under the hands scale with the hand, so they count as skeleton too.
            for (int i = 0; i < scaledBones.Count; i++)
            {
                if (scaledBones[i].Group != BoneGroup.Hand) continue;
                foreach (Transform t in scaledBones[i].Bone.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith(boneNamePrefix)) skeletonBones.Add(t);
                }
            }

            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                ScaledBone root = FindScaledBone(smr.rootBone);
                if (root != null) boundsFixes.Add(new BoundsFix { Renderer = smr, RestBounds = smr.localBounds, RootBone = root });
            }

            if (animated)
            {
                poseProbe = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                if (poseProbe != null && !avatarRest.TryGetValue(poseProbe.name, out poseProbeRestPosition)) poseProbe = null;
                if (poseProbe != null) poseProbeStartPosition = poseProbe.localPosition;
            }

            joints = refitRagdollJoints && animated ? GetComponentsInChildren<CharacterJoint>(true) : new CharacterJoint[0];
            jointRestAutoConfigure = new bool[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                jointRestAutoConfigure[i] = joints[i].autoConfigureConnectedAnchor;
                Rigidbody body = joints[i].connectedBody;
                for (int b = 0; b < scaledBones.Count; b++)
                {
                    ScaledBone sb = scaledBones[b];
                    if (IsArmSegment(sb.Group) && sb.NextBone == joints[i].transform && body != null && body.transform == sb.Bone)
                        elbowJoints.Add(joints[i]);
                }
            }

            initialized = true;
        }

        private bool IsPosedByAnimator()
        {
            if (poseProbe == null || (poseProbeStartPosition - poseProbeRestPosition).sqrMagnitude < 1e-6f) return true;
            Vector3 current = poseProbe.localPosition;
            return (current - poseProbeRestPosition).sqrMagnitude < (current - poseProbeStartPosition).sqrMagnitude;
        }

        // Anchors every joint where its bone really sits in the posed, scaled skeleton (connected body space).
        // Re-attaching the connected body makes Unity rebuild the joint from the current pose and bone scales (assigning
        // connectedAnchor alone keeps the scale the joint was built with). Same thing the character does when re-enabled.
        private void RefitJoints()
        {
            for (int i = 0; i < joints.Length; i++)
            {
                CharacterJoint joint = joints[i];
                if (joint == null || joint.connectedBody == null) continue;
                Rigidbody body = joint.connectedBody;
                joint.autoConfigureConnectedAnchor = true;
                joint.connectedBody = null;
                joint.connectedBody = body;
            }
        }

        // Joints rebuilt by Unity from a pose the Animator wrote before this component ran (e.g. the character model was
        // re-enabled after a car ride) anchor the elbow at the unscaled spot. Cheap check: the elbow joint lives in the
        // upper arm's space, so its correct anchor does not depend on the pose.
        private bool ElbowJointsAreStale()
        {
            for (int i = 0; i < elbowJoints.Count; i++)
            {
                CharacterJoint joint = elbowJoints[i];
                if (joint == null || joint.connectedBody == null) continue;
                Vector3 expected = joint.connectedBody.transform.InverseTransformPoint(joint.transform.TransformPoint(joint.anchor));
                if ((expected - joint.connectedAnchor).sqrMagnitude > 1e-6f) return true;
            }
            return false;
        }

        private Transform FindArmBone(int side, int index)
        {
            if (animated) return animator.GetBoneTransform(ArmChains[side, index]);
            return FindChildRecursive(transform, boneNamePrefix + ArmChainNames[side, index]);
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                Transform found = FindChildRecursive(child, childName);
                if (found != null) return found;
            }
            return null;
        }

        private ScaledBone FindScaledBone(Transform bone)
        {
            for (int i = 0; i < scaledBones.Count; i++)
            {
                if (scaledBones[i].Bone == bone) return scaledBones[i];
            }
            return null;
        }

        private static bool IsArmSegment(BoneGroup group) => group == BoneGroup.UpperArm || group == BoneGroup.Forearm;

        private float GetScale(BoneGroup group)
        {
            switch (group)
            {
                case BoneGroup.Shoulder: return profile.ShoulderScale;
                case BoneGroup.UpperArm: return profile.UpperArmScale;
                case BoneGroup.Forearm: return profile.ForearmScale;
                default: return profile.HandScale;
            }
        }

        /// <summary>Applies the profile. Absolute (from cached rest values), so it is safe to call repeatedly.</summary>
        public void Apply()
        {
            if (!initialized) return;

            for (int i = 0; i < scaledBones.Count; i++)
            {
                ScaledBone sb = scaledBones[i];
                float s = GetScale(sb.Group);
                float parentScale = sb.Parent != null ? GetScale(sb.Parent.Group) : 1f;

                RegisterNewAttachments(sb);
                sb.AppliedScale = s;
                sb.Bone.localScale = sb.RestScale * (s / parentScale);

                for (int a = 0; a < sb.Attachments.Count; a++)
                {
                    Attachment att = sb.Attachments[a];
                    if (att.Transform == null || att.Transform.parent != sb.Bone) continue;
                    att.Transform.localScale = att.BaseScale / s;
                    att.Transform.localPosition = att.BasePosition / s;
                }

                if (sb.NextBone != null)
                {
                    bool keep = profile.KeepArmLength && IsArmSegment(sb.Group);
                    sb.NextBone.localPosition = keep ? sb.NextBoneRestPosition / s : sb.NextBoneRestPosition;
                }
            }

            for (int i = 0; i < boundsFixes.Count; i++)
            {
                BoundsFix fix = boundsFixes[i];
                float s = fix.RootBone.AppliedScale;
                fix.Renderer.localBounds = new Bounds(fix.RestBounds.center / s, fix.RestBounds.size / s);
            }

            applied = true;
            jointsNeedRefit = joints.Length > 0;
        }

        // Non-bone children (weapon holders, hit boxes). Their unscaled base is recovered from the scale already applied to the
        // bone, which also covers holders that Invector re-parents onto the hand (keeping world scale) after this component started.
        private void RegisterNewAttachments(ScaledBone sb)
        {
            foreach (Transform child in sb.Bone)
            {
                if (skeletonBones.Contains(child) || HasAttachment(sb, child)) continue;
                sb.Attachments.Add(new Attachment
                {
                    Transform = child,
                    BaseScale = child.localScale * sb.AppliedScale,
                    BasePosition = child.localPosition * sb.AppliedScale
                });
            }
        }

        private static bool HasAttachment(ScaledBone sb, Transform t)
        {
            for (int i = 0; i < sb.Attachments.Count; i++)
            {
                if (sb.Attachments[i].Transform == t) return true;
            }
            return false;
        }

        private void Restore()
        {
            for (int i = 0; i < scaledBones.Count; i++)
            {
                ScaledBone sb = scaledBones[i];
                if (sb.Bone == null) continue;
                sb.Bone.localScale = sb.RestScale;
                for (int a = 0; a < sb.Attachments.Count; a++)
                {
                    Attachment att = sb.Attachments[a];
                    if (att.Transform == null || att.Transform.parent != sb.Bone) continue;
                    att.Transform.localScale = att.BaseScale;
                    att.Transform.localPosition = att.BasePosition;
                }
                if (sb.NextBone != null) sb.NextBone.localPosition = sb.NextBoneRestPosition;
                sb.AppliedScale = 1f;
            }

            for (int i = 0; i < boundsFixes.Count; i++)
            {
                if (boundsFixes[i].Renderer != null) boundsFixes[i].Renderer.localBounds = boundsFixes[i].RestBounds;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null || joints[i].connectedBody == null) continue;
                Rigidbody body = joints[i].connectedBody;
                joints[i].autoConfigureConnectedAnchor = jointRestAutoConfigure[i];
                joints[i].connectedBody = null;
                joints[i].connectedBody = body;
            }

            applied = false;
            appliedAfterStart = false;
            jointsNeedRefit = false;
        }
    }
}
