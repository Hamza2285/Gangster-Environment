using System.Collections.Generic;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Ragdoll for the pooled street pedestrians (generic Biped rigs, which Invector's vRagdoll cannot drive).
    /// Bodies, colliders and joints are authored on the pedestrian prefab variants by the Pedestrian Variant Builder, but a
    /// walking pedestrian must not pay for eleven animated rigidbodies, so:
    /// - Awake records every bone body's and joint's settings and removes the bone Rigidbodies and CharacterJoints. The bone
    ///   colliders stay as trigger hitboxes on the root's single kinematic body (no per-bone physics bodies while alive).
    /// - Hitbox detail (<see cref="SetHitboxesEnabled"/>, driven by the spawner's distance check): near the player the bone
    ///   hitboxes are on (head shots); farther away one trigger capsule on the BodyPart layer takes the bullets instead.
    /// - <see cref="Activate"/> (knock-down or death) creates the bodies and joints again from the recorded settings (joint
    ///   frames built in the authored bind pose, so the limits are exactly the builder's), then hands the body to physics.
    /// - <see cref="GetUp"/> / <see cref="ResetToAnimated"/> remove them again. A dead body that has settled far from the
    ///   player can be <see cref="Freeze"/>-d (bodies removed, limbs become triggers, the pose stays) and is rebuilt asleep by
    ///   <see cref="Unfreeze"/> when the player comes close again, so cars can still bump it.
    /// Cost while walking: no Update/LateUpdate (the component stays disabled); it is only enabled for the short blend back to
    /// animation after a survivor gets up.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianRagdoll : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [Tooltip("Pelvis bone body (authored; recorded and removed at Awake, recreated on knock-down).")]
        [SerializeField] private Rigidbody hipsBody;
        [Tooltip("Every ragdoll body (hips first). Each has one collider on the same GameObject. Recorded and removed at Awake.")]
        [SerializeField] private Rigidbody[] bodies = new Rigidbody[0];

        [Header("Get Up")]
        [Tooltip("Layers the body stands up on after a knock-down.")]
        [SerializeField] private LayerMask groundLayers = 1;
        [Tooltip("Seconds over which the ragdoll pose blends back into the animation.")]
        [SerializeField, Min(0.01f)] private float blendToAnimationTime = 0.35f;

        [Header("Getting Up")]
        [Tooltip("Humanoid version of this person's skeleton, used only while the (humanoid) get-up clip plays. Empty = no get-up clip.")]
        [SerializeField] private Avatar getUpAvatar;
        [Tooltip("Controller with the states GetUpFromBack and GetUpFromBelly.")]
        [SerializeField] private RuntimeAnimatorController getUpController;
        [Tooltip("Share of the get-up clip played before the person's own animation takes over (they are standing by then).")]
        [SerializeField, Range(0.5f, 1f)] private float getUpPlayedShare = 0.9f;

        [Header("Stability")]
        [Tooltip("Fastest speed (m/s) at which physics pushes overlapping limbs apart. Low values stop a body that starts inside " +
                 "the ground or a car from being shot away (the physics default is 10).")]
        [SerializeField, Min(0.1f)] private float maxDepenetrationVelocity = 2f;
        [Tooltip("Limbs slower than this (kinetic energy per mass) fall asleep; higher than the physics default so bodies on the " +
                 "ground stop costing simulation time sooner.")]
        [SerializeField, Min(0f)] private float sleepThreshold = 0.05f;

        [Header("Far Hitbox")]
        [Tooltip("Height (m) of the single trigger capsule that takes bullets while the bone hitboxes are off.")]
        [SerializeField, Min(0.5f)] private float farHitboxHeight = 1.8f;
        [SerializeField, Min(0.05f)] private float farHitboxRadius = 0.3f;

        private struct BodySettings
        {
            public float Mass, LinearDamping, AngularDamping;
            public bool UseGravity;
            public CollisionDetectionMode Detection;
            public RigidbodyInterpolation Interpolation;
        }

        private struct JointSettings
        {
            public bool Exists;
            public int ConnectedIndex;
            public Vector3 Anchor, Axis, SwingAxis;
            public SoftJointLimit LowTwist, HighTwist, Swing1, Swing2;
            public SoftJointLimitSpring TwistSpring, SwingSpring;
            public bool EnableProjection, EnablePreprocessing, EnableCollision;
            public float ProjectionDistance, ProjectionAngle, BreakForce, BreakTorque, MassScale, ConnectedMassScale;
        }

        private Rigidbody[] runtimeBodies = new Rigidbody[0];
        private Collider[] colliders = new Collider[0];
        private Transform[] boneTransforms = new Transform[0];
        private BodySettings[] bodySettings = new BodySettings[0];
        private JointSettings[] jointSettings = new JointSettings[0];
        private Quaternion[] bindRotations = new Quaternion[0];
        private Vector3[] bindPositions = new Vector3[0];
        private Quaternion[] storedRotations = new Quaternion[0];
        private Vector3[] storedPositions = new Vector3[0];
        private Collider farHitbox;
        private Transform hipsBone;
        private Vector3 storedHipsPosition;
        // Getting up with a clip (see GetUp).
        private Vector3 hipsFrontLocal;
        private Transform headBone, leftFootBone, rightFootBone;
        private Vector3 lyingHead, lyingFeet;
        private Avatar ownAvatar;
        private RuntimeAnimatorController ownController;
        private bool clipGetUp;
        private float getUpEndTime;
        private int alignFrames;
        private float blendStartTime;
        private bool initialized;
        private bool hitboxesEnabled;

        /// <summary>True while physics drives the body.</summary>
        public bool IsActive { get; private set; }
        /// <summary>True while a survivor plays the get-up clip (still on the ground or rising, not yet able to run).</summary>
        public bool IsGettingUp => clipGetUp;
        /// <summary>True while a settled dead body has no physics bodies (see <see cref="Freeze"/>).</summary>
        public bool IsFrozen { get; private set; }
        /// <summary>Hips body while the ragdoll is built (knocked down or dead), otherwise null.</summary>
        public Rigidbody HipsBody => runtimeBodies.Length > 0 ? runtimeBodies[0] : null;
        /// <summary>Ragdoll bodies while built (hips first); entries are null while the pedestrian walks.</summary>
        public Rigidbody[] Bodies => runtimeBodies;
        /// <summary>One collider per bone (same order as <see cref="Bodies"/>).</summary>
        public IReadOnlyList<Collider> Colliders => colliders;
        /// <summary>World position of the hips bone (also while no bodies exist).</summary>
        public Vector3 HipsPosition => hipsBone != null ? hipsBone.position : transform.position;

        private void Awake()
        {
            Initialize();
            enabled = false;
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (animator == null || hipsBody == null || bodies.Length == 0 || bodies[0] != hipsBody)
            {
                Debug.LogError($"[{nameof(PedestrianRagdoll)}] Missing animator, hips body or bodies (hips first) on '{name}'. Run the Pedestrian Variant Builder.", this);
                return;
            }

            int n = bodies.Length;
            runtimeBodies = new Rigidbody[n];
            colliders = new Collider[n];
            boneTransforms = new Transform[n];
            bodySettings = new BodySettings[n];
            jointSettings = new JointSettings[n];
            bindRotations = new Quaternion[n];
            bindPositions = new Vector3[n];
            storedRotations = new Quaternion[n];
            storedPositions = new Vector3[n];
            hipsBone = hipsBody.transform;
            // In the bind pose (Awake, before the Animator has evaluated) the person stands facing the root's forward.
            hipsFrontLocal = Quaternion.Inverse(hipsBone.rotation) * transform.forward;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string boneName = t.name.ToLowerInvariant();
                if (headBone == null && boneName.EndsWith("head")) headBone = t;
                else if (leftFootBone == null && (boneName.EndsWith("l foot") || boneName.EndsWith("leftfoot") || boneName.EndsWith("foot_l"))) leftFootBone = t;
                else if (rightFootBone == null && (boneName.EndsWith("r foot") || boneName.EndsWith("rightfoot") || boneName.EndsWith("foot_r"))) rightFootBone = t;
            }

            var indexOf = new Dictionary<Rigidbody, int>(n);
            for (int i = 0; i < n; i++) indexOf[bodies[i]] = i;

            // Record the authored setup in the prefab's bind pose (Awake runs before the Animator has evaluated).
            for (int i = 0; i < n; i++)
            {
                Rigidbody body = bodies[i];
                Transform bone = body.transform;
                boneTransforms[i] = bone;
                colliders[i] = bone.GetComponent<Collider>();
                bindRotations[i] = bone.localRotation;
                bindPositions[i] = bone.localPosition;
                bodySettings[i] = new BodySettings
                {
                    Mass = body.mass,
                    LinearDamping = body.linearDamping,
                    AngularDamping = body.angularDamping,
                    UseGravity = body.useGravity,
                    Detection = body.collisionDetectionMode,
                    Interpolation = body.interpolation
                };
                if (bone.TryGetComponent(out CharacterJoint joint) && joint.connectedBody != null && indexOf.TryGetValue(joint.connectedBody, out int parent))
                {
                    jointSettings[i] = new JointSettings
                    {
                        Exists = true,
                        ConnectedIndex = parent,
                        Anchor = joint.anchor,
                        Axis = joint.axis,
                        SwingAxis = joint.swingAxis,
                        LowTwist = joint.lowTwistLimit,
                        HighTwist = joint.highTwistLimit,
                        Swing1 = joint.swing1Limit,
                        Swing2 = joint.swing2Limit,
                        TwistSpring = joint.twistLimitSpring,
                        SwingSpring = joint.swingLimitSpring,
                        EnableProjection = joint.enableProjection,
                        EnablePreprocessing = joint.enablePreprocessing,
                        EnableCollision = joint.enableCollision,
                        ProjectionDistance = joint.projectionDistance,
                        ProjectionAngle = joint.projectionAngle,
                        BreakForce = joint.breakForce,
                        BreakTorque = joint.breakTorque,
                        MassScale = joint.massScale,
                        ConnectedMassScale = joint.connectedMassScale
                    };
                }
            }

            CreateFarHitbox();
            DestroyBodies();
            SetPhysical(false);
        }

        // One trigger capsule on the hitbox layer (child of the root, so it rides on the root's kinematic body for free).
        private void CreateFarHitbox()
        {
            var go = new GameObject("FarHitbox");
            go.layer = colliders[0] != null ? colliders[0].gameObject.layer : gameObject.layer;
            go.transform.SetParent(transform, false);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.height = farHitboxHeight;
            capsule.radius = farHitboxRadius;
            capsule.center = new Vector3(0f, farHitboxHeight * 0.5f, 0f);
            go.AddComponent<vDamageReceiver>();
            farHitbox = capsule;
        }

        /// <summary>
        /// Bone hitboxes near the player (true) or the single far capsule (false). The spawner calls this from its batched
        /// distance check; nothing changes while the body is ragdolled.
        /// </summary>
        public void SetHitboxesEnabled(bool value)
        {
            Initialize();
            if (value == hitboxesEnabled) return;
            hitboxesEnabled = value;
            if (IsActive || IsFrozen) return;
            ApplyHitboxes();
        }

        /// <summary>Authored height of the far hitbox (a standing person).</summary>
        public float FarHitboxHeight => farHitboxHeight;

        /// <summary>Resizes the far hitbox (a seated person's ends at the head).</summary>
        public void SetFarHitboxHeight(float height)
        {
            Initialize();
            if (!(farHitbox is CapsuleCollider capsule)) return;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Hands the body to physics. Bodies start with the given velocity.</summary>
        public void Activate(Vector3 velocity)
        {
            CancelClipGetUp();
            Initialize();
            if (IsActive || bodySettings.Length == 0) return;
            enabled = false;
            IsActive = true;
            IsFrozen = false;
            animator.enabled = false;
            BuildBodies(false);
            SetPhysical(true);
            for (int i = 0; i < runtimeBodies.Length; i++)
                if (runtimeBodies[i] != null) runtimeBodies[i].linearVelocity = velocity;
        }

        /// <summary>True when the active ragdoll has come to rest (asleep, or the hips barely move).</summary>
        public bool IsSettled
        {
            get
            {
                Rigidbody hips = HipsBody;
                return IsActive && hips != null && (hips.IsSleeping() || hips.linearVelocity.sqrMagnitude < 0.01f);
            }
        }

        /// <summary>
        /// Removes the physics bodies of a settled (dead) ragdoll; the pose stays and the limbs become trigger hitboxes on the
        /// root, so a far corpse costs no simulation. <see cref="Unfreeze"/> rebuilds it.
        /// </summary>
        public void Freeze()
        {
            if (!IsActive || IsFrozen) return;
            DestroyBodies();
            IsActive = false;
            IsFrozen = true;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;
                c.isTrigger = true;
                c.enabled = true;
            }
            if (farHitbox != null) farHitbox.enabled = false;
        }

        /// <summary>Rebuilds the bodies of a frozen corpse where it lies, asleep, so passing cars bump it again.</summary>
        public void Unfreeze()
        {
            if (!IsFrozen) return;
            IsFrozen = false;
            IsActive = true;
            BuildBodies(true);
            SetPhysical(true);
            for (int i = 0; i < runtimeBodies.Length; i++)
                if (runtimeBodies[i] != null) runtimeBodies[i].Sleep();
        }

        /// <summary>
        /// Stands a survivor back up where the body lies: the root moves under the hips, facing along the body, and the
        /// ragdoll pose blends into the animation over <see cref="blendToAnimationTime"/>.
        /// </summary>
        public void GetUp()
        {
            if (!IsActive) return;

            // Store the lying pose before the root moves (the bones are its children).
            for (int i = 0; i < boneTransforms.Length; i++) storedRotations[i] = boneTransforms[i].rotation;
            storedHipsPosition = hipsBone.position;

            Vector3 standPoint = hipsBone.position;
            if (Physics.Raycast(standPoint + Vector3.up, Vector3.down, out RaycastHit hit, 3f, groundLayers, QueryTriggerInteraction.Ignore))
                standPoint = hit.point;
            // Face the way the body was thrown (away from what hit it).
            Vector3 facing = Vector3.ProjectOnPlane(hipsBone.position - transform.position, Vector3.up);
            if (facing.sqrMagnitude < 0.01f) facing = transform.forward;

            bool faceUp = hipsBone.TransformDirection(hipsFrontLocal).y > 0f;
            if (headBone != null) lyingHead = headBone.position;
            lyingFeet = leftFootBone != null && rightFootBone != null ? (leftFootBone.position + rightFootBone.position) * 0.5f : transform.position;

            DestroyBodies();
            IsActive = false;
            SetPhysical(false);
            transform.SetPositionAndRotation(standPoint, Quaternion.LookRotation(facing.normalized, Vector3.up));
            animator.enabled = true;

            // A proper get-up: the humanoid clip for lying on the back or the front, lined up with where the body lies.
            clipGetUp = getUpAvatar != null && getUpController != null && headBone != null;
            if (clipGetUp)
            {
                ownAvatar = animator.avatar;
                ownController = animator.runtimeAnimatorController;
                animator.avatar = getUpAvatar;
                animator.runtimeAnimatorController = getUpController;
                animator.applyRootMotion = false;
                animator.Rebind();
                string state = faceUp ? "GetUpFromBack" : "GetUpFromBelly";
                animator.Play(state, 0, 0f);
                animator.Update(0f);
                float length = 1.8f;
                foreach (var clip in getUpController.animationClips)
                    if (clip != null && clip.name == state) length = clip.length;
                getUpEndTime = Time.time + length * getUpPlayedShare;
                alignFrames = 2;
            }
            blendStartTime = Time.time;
            enabled = true; // LateUpdate runs for the blend (and the get-up)
        }

        // The clip starts with the body lying somewhere relative to the root: move and turn the root so the clip's lying body
        // lies where the ragdoll does (hips over the ragdoll's hips, head-to-feet the same way).
        private void AlignRootToLyingBody()
        {
            Vector3 lying = lyingHead - lyingFeet;
            lying.y = 0f;
            Vector3 feet = leftFootBone != null && rightFootBone != null ? (leftFootBone.position + rightFootBone.position) * 0.5f : transform.position;
            Vector3 animated = headBone.position - feet;
            animated.y = 0f;
            if (lying.sqrMagnitude > 0.01f && animated.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.FromToRotation(animated.normalized, lying.normalized) * transform.rotation;

            Vector3 toRagdoll = storedHipsPosition - hipsBone.position;
            Vector3 root = transform.position + new Vector3(toRagdoll.x, 0f, toRagdoll.z);
            if (Physics.Raycast(root + Vector3.up, Vector3.down, out RaycastHit hit, 3f, groundLayers, QueryTriggerInteraction.Ignore))
                root.y = hit.point.y;
            transform.position = root;
        }

        // Knocked over again while getting up: the clip stops and the own animator setup comes back before the ragdoll.
        private void CancelClipGetUp()
        {
            if (!clipGetUp) return;
            EndClipGetUp();
        }

        // The person's own animation back after the get-up clip: bones to the bind pose, own avatar and controller rebound.
        private void EndClipGetUp()
        {
            clipGetUp = false;
            // Swapping the avatar back writes the humanoid's bind-time values (the lying pose) into the bones, so the rest pose
            // is restored after the swap, not before.
            animator.avatar = ownAvatar;
            animator.runtimeAnimatorController = ownController;
            animator.Rebind();
            for (int i = 0; i < boneTransforms.Length && i < bindPositions.Length; i++)
                if (boneTransforms[i] != null) boneTransforms[i].localPosition = bindPositions[i];
            animator.Update(0f);
        }

        /// <summary>Back to the plain animated state at once (pool recycling). Also restores the bind pose.</summary>
        public void ResetToAnimated()
        {
            Initialize();
            enabled = false;
            bool wasRagdoll = IsActive || IsFrozen || clipGetUp;
            if (clipGetUp && animator != null)
            {
                animator.avatar = ownAvatar;
                animator.runtimeAnimatorController = ownController;
                clipGetUp = false;
            }
            IsActive = false;
            IsFrozen = false;
            DestroyBodies();
            SetPhysical(false);
            if (animator != null)
            {
                animator.enabled = true;
                if (wasRagdoll) animator.Rebind();
            }
            // Rebind only resets what the clips animate; bone positions the physics moved go back to the bind pose here.
            if (wasRagdoll) RestoreBindPositions();
        }

        // Bone positions back to the prefab's bind pose (the clips only rotate these bones).
        private void RestoreBindPositions()
        {
            if (boneTransforms == null || bindPositions == null) return;
            for (int i = 0; i < boneTransforms.Length && i < bindPositions.Length; i++)
                if (boneTransforms[i] != null) boneTransforms[i].localPosition = bindPositions[i];
        }

        // Runs after the Animator: blends from the stored ragdoll pose into the animated pose.
        private void LateUpdate()
        {
            if (clipGetUp)
            {
                if (alignFrames > 0)
                {
                    AlignRootToLyingBody();
                    alignFrames--;
                }
                if (Time.time >= getUpEndTime)
                {
                    EndClipGetUp();
                    enabled = false;
                    return;
                }
                // Out of the ragdoll pose into the clip (the clip moves the hips itself).
                float tc = (Time.time - blendStartTime) / Mathf.Max(blendToAnimationTime, 0.45f);
                if (tc < 1f)
                {
                    float w = 1f - tc;
                    hipsBone.position = Vector3.Lerp(hipsBone.position, storedHipsPosition, w);
                    for (int i = 0; i < boneTransforms.Length; i++)
                        boneTransforms[i].rotation = Quaternion.Slerp(boneTransforms[i].rotation, storedRotations[i], w);
                }
                return;
            }

            float t = (Time.time - blendStartTime) / blendToAnimationTime;
            if (t >= 1f)
            {
                RestoreBindPositions();
                enabled = false;
                return;
            }
            float weight = 1f - t;
            // The walk/run clips animate the bone above the pelvis, not the pelvis's own position, so the pelvis has to be led
            // back to its place under it (where the animation expects it); holding it at the lying position left the body sunk
            // ~0.8 m into the ground for good once the root moved under it.
            Vector3 animatedHips = hipsBone.parent != null ? hipsBone.parent.TransformPoint(bindPositions[0]) : hipsBone.position;
            hipsBone.position = Vector3.Lerp(animatedHips, storedHipsPosition, weight);
            for (int i = 0; i < boneTransforms.Length; i++)
                boneTransforms[i].rotation = Quaternion.Slerp(boneTransforms[i].rotation, storedRotations[i], weight);
        }

        // Creates the bone bodies and joints from the recorded settings. The joints are created with the bones in their bind
        // pose (the pose the builder authored the limits in), then the current pose is restored.
        private void BuildBodies(bool asleep)
        {
            if (runtimeBodies.Length == 0 || runtimeBodies[0] != null) return;
            int n = boneTransforms.Length;
            for (int i = 0; i < n; i++)
            {
                storedPositions[i] = boneTransforms[i].localPosition;
                storedRotations[i] = boneTransforms[i].localRotation;
            }
            Vector3 hipsWorld = hipsBone.position;
            Quaternion hipsWorldRotation = hipsBone.rotation;
            for (int i = 0; i < n; i++) boneTransforms[i].SetLocalPositionAndRotation(bindPositions[i], bindRotations[i]);
            // Keep the hips where the body is, so the joints are built around the body and not around the root.
            hipsBone.SetPositionAndRotation(hipsWorld, hipsWorldRotation);

            for (int i = 0; i < n; i++)
            {
                Rigidbody body = boneTransforms[i].gameObject.AddComponent<Rigidbody>();
                BodySettings s = bodySettings[i];
                body.isKinematic = true;
                body.mass = s.Mass;
                body.linearDamping = s.LinearDamping;
                body.angularDamping = s.AngularDamping;
                body.useGravity = s.UseGravity;
                body.collisionDetectionMode = s.Detection;
                body.interpolation = s.Interpolation;
                body.maxDepenetrationVelocity = maxDepenetrationVelocity;
                body.sleepThreshold = sleepThreshold;
                runtimeBodies[i] = body;
            }
            for (int i = 0; i < n; i++)
            {
                JointSettings j = jointSettings[i];
                if (!j.Exists) continue;
                CharacterJoint joint = boneTransforms[i].gameObject.AddComponent<CharacterJoint>();
                joint.anchor = j.Anchor;
                joint.axis = j.Axis;
                joint.swingAxis = j.SwingAxis;
                joint.lowTwistLimit = j.LowTwist;
                joint.highTwistLimit = j.HighTwist;
                joint.swing1Limit = j.Swing1;
                joint.swing2Limit = j.Swing2;
                joint.twistLimitSpring = j.TwistSpring;
                joint.swingLimitSpring = j.SwingSpring;
                joint.enableProjection = j.EnableProjection;
                joint.projectionDistance = j.ProjectionDistance;
                joint.projectionAngle = j.ProjectionAngle;
                joint.enablePreprocessing = j.EnablePreprocessing;
                joint.enableCollision = j.EnableCollision;
                joint.breakForce = j.BreakForce;
                joint.breakTorque = j.BreakTorque;
                joint.massScale = j.MassScale;
                joint.connectedMassScale = j.ConnectedMassScale;
                joint.connectedBody = runtimeBodies[j.ConnectedIndex];
            }

            for (int i = 0; i < n; i++) boneTransforms[i].SetLocalPositionAndRotation(storedPositions[i], storedRotations[i]);
            if (asleep)
                for (int i = 0; i < n; i++) runtimeBodies[i].Sleep();
        }

        private void DestroyBodies()
        {
            // Joints first: a Rigidbody cannot be removed while a joint requires it. Immediate, so a knock-down in the same
            // frame can rebuild them.
            for (int i = 0; i < boneTransforms.Length; i++)
                if (boneTransforms[i] != null && boneTransforms[i].TryGetComponent(out CharacterJoint joint)) DestroyImmediate(joint);
            for (int i = 0; i < boneTransforms.Length; i++)
            {
                if (boneTransforms[i] != null && boneTransforms[i].TryGetComponent(out Rigidbody body)) DestroyImmediate(body);
                if (i < runtimeBodies.Length) runtimeBodies[i] = null;
            }
        }

        private void SetPhysical(bool physical)
        {
            for (int i = 0; i < runtimeBodies.Length; i++)
            {
                Rigidbody body = runtimeBodies[i];
                if (body != null)
                {
                    if (!physical && !body.isKinematic)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                    body.isKinematic = !physical;
                }
            }
            if (physical)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider c = colliders[i];
                    if (c == null) continue;
                    // Physical: solid limbs.
                    c.isTrigger = false;
                    c.enabled = true;
                }
                if (farHitbox != null) farHitbox.enabled = false;
                return;
            }
            ApplyHitboxes();
        }

        // Animated: trigger hitboxes for bullets (bones near the player, one capsule farther away).
        private void ApplyHitboxes()
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;
                c.isTrigger = true;
                c.enabled = hitboxesEnabled;
            }
            if (farHitbox != null) farHitbox.enabled = !hitboxesEnabled;
        }
    }
}
