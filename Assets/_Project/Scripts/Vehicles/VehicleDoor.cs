using System;
using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Opens/closes one vehicle door by rotating it around its hinge (this transform's pivot must sit on the hinge).
    /// Vehicle-framework independent. If the door is a physics part (e.g. an RCC detachable door on a
    /// ConfigurableJoint), its Rigidbody is held kinematic and every joint axis freed while the door is open or moving, so
    /// physics neither fights the animation nor drags the car (a kinematic door still locked to a moving car would pull it
    /// back every physics step); both are restored once closed.
    /// Requests made while the door is moving are ignored (Open/Close return false).
    /// Clearance: while a character gets in or out, <see cref="SetClearanceBones"/> registers his hand/arm bones, so the door
    /// does not swing through them: bones on the inner side push the door open / keep it from closing onto them, bones on the
    /// outer side (a hand pulling the handle) stop it from swinging past them. Afterwards it eases back to its own swing.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleDoor : MonoBehaviour
    {
        [Header("Hinge")]
        [Tooltip("Hinge axis in this transform's local space.")]
        [SerializeField] private Vector3 hingeAxis = Vector3.up;
        [Tooltip("Open angle in degrees around the hinge axis. The sign sets the direction - it must swing outward.")]
        [SerializeField, Range(-120f, 120f)] private float doorOpenAngle = 75f;
        [SerializeField, Min(0.05f)] private float doorOpenDuration = 0.45f;
        [SerializeField, Min(0.05f)] private float doorCloseDuration = 0.35f;
        [Tooltip("Direction from the hinge to the door's free (rear) edge, in this transform's local space with the door closed. " +
                 "Used to measure how far a point such as a pushing hand has opened the door.")]
        [SerializeField] private Vector3 edgeDirection = Vector3.back;

        [Header("Handle")]
        [Tooltip("Outside door handle: where a character's hand takes the door to pull it open.")]
        [SerializeField] private Transform handle;

        [Header("Clearance")]
        [Tooltip("Gap (m) kept between a registered bone (hand, forearm) and the door panel.")]
        [SerializeField, Min(0f)] private float clearanceMargin = 0.08f;
        [Tooltip("Degrees per second the door eases back to its swing once a bone no longer blocks it.")]
        [SerializeField, Min(10f)] private float clearanceReleaseSpeed = 300f;
        [Tooltip("Band (m) along the door's free edge where a hand may wrap around the door (it is not blocked there).")]
        [SerializeField, Min(0f)] private float clearanceEdgeBand = 0.12f;

        [Header("Physics (optional)")]
        [Tooltip("Door Rigidbody, held kinematic while the door is open or moving.")]
        [SerializeField] private Rigidbody doorBody;
        [Tooltip("Joint holding the door to the vehicle. All its axes are freed while the door is open or moving (the door follows its parent).")]
        [SerializeField] private ConfigurableJoint doorJoint;

        public event Action<VehicleDoor> OnOpened;
        public event Action<VehicleDoor> OnClosed;

        public bool IsOpen { get; private set; }
        public bool IsMoving => moveRoutine != null;
        /// <summary>Fully open angle in degrees (always positive).</summary>
        public float MaxOpenAngle => Mathf.Abs(doorOpenAngle);
        /// <summary>Current opening in degrees: 0 = closed, <see cref="MaxOpenAngle"/> = fully open.</summary>
        public float OpenAngle => GetCurrentAngle() * Mathf.Sign(doorOpenAngle);
        /// <summary>False once the door has come off its hinge (e.g. detached by vehicle damage).</summary>
        public bool IsAvailable => isActiveAndEnabled && transform.parent == hingeParent;
        /// <summary>Outside door handle (moves with the door); null if none is set up.</summary>
        public Transform Handle => handle;

        private Transform hingeParent;
        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private Coroutine moveRoutine;
        private bool physicsHeld;
        private bool bodyWasKinematic;
        private readonly ConfigurableJointMotion[] savedJointMotions = new ConfigurableJointMotion[6];
        // Close to the hinge line the panel barely moves (and the angle is meaningless), so bones there are ignored.
        private const float ClearanceMinRadius = 0.3f;
        private const int IgnoredSide = 2;
        // Safety limit (s) for a swing waiting on a bone.
        private const float MaxClearanceHold = 3f;
        private Transform[] clearanceBones;
        private int[] clearanceSides;
        private float clearanceAngle;
        private float clearanceLowest;
        private int clearanceWarmup;
        private float clearanceHighest = float.MaxValue;
        private float doorReach = 1.2f;
        private float doorAxisMin = -0.6f;
        private float doorAxisMax = 0.6f;

        private void Awake()
        {
            hingeParent = transform.parent;
            closedLocalPosition = transform.localPosition;
            closedLocalRotation = transform.localRotation;
            if (hingeAxis.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"[{nameof(VehicleDoor)}] Hinge axis on '{name}' is zero; using local up.", this);
                hingeAxis = Vector3.up;
            }
            hingeAxis.Normalize();
            if (edgeDirection.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"[{nameof(VehicleDoor)}] Edge direction on '{name}' is zero; using local back.", this);
                edgeDirection = Vector3.back;
            }
            MeasureDoorExtent();
        }

        /// <summary>
        /// Bones (e.g. a character's hands and forearms) the door must not pass through while set; null clears them.
        /// Each bone keeps the side of the door it was on when it came within reach.
        /// </summary>
        public void SetClearanceBones(Transform[] bones)
        {
            clearanceBones = bones != null && bones.Length > 0 ? bones : null;
            clearanceSides = clearanceBones != null ? new int[clearanceBones.Length] : null;
            clearanceAngle = OpenAngle;
            clearanceWarmup = 2;
            clearanceLowest = 0f;
            clearanceHighest = float.MaxValue;
        }

        // While bones are registered the swing obeys last frame's clearance limits, so the door a character's IK sees this
        // frame is already the one that will be drawn.
        private float ConstrainSwing(float signedAngle)
        {
            if (clearanceBones == null) return signedAngle;
            float open = signedAngle * Mathf.Sign(doorOpenAngle);
            open = Mathf.MoveTowards(clearanceAngle, open, clearanceReleaseSpeed * Time.deltaTime);
            open = Mathf.Clamp(open, clearanceLowest, Mathf.Max(clearanceLowest, clearanceHighest));
            return Mathf.Clamp(open, 0f, MaxOpenAngle) * Mathf.Sign(doorOpenAngle);
        }

        /// <summary>
        /// Leaves one registered bone out of the clearance while it holds the door (e.g. the hand on the handle); its side is
        /// taken again when it lets go.
        /// </summary>
        public void SetClearanceIgnored(Transform bone, bool ignored)
        {
            if (clearanceBones == null || bone == null) return;
            int index = Array.IndexOf(clearanceBones, bone);
            if (index < 0) return;
            clearanceSides[index] = ignored ? IgnoredSide : 0;
        }

        // After animation and IK, so the bones are where they are drawn this frame.
        private void LateUpdate()
        {
            if (clearanceBones == null || !IsAvailable) return;

            // The character's pose may not be up to date on the first frames (e.g. just shown in the seat).
            if (clearanceWarmup > 0)
            {
                clearanceWarmup--;
                clearanceAngle = OpenAngle;
                return;
            }

            float current = OpenAngle;
            float lowest = 0f;
            float highest = MaxOpenAngle;
            Transform parent = transform.parent;
            Vector3 axis = closedLocalRotation * hingeAxis;
            for (int i = 0; i < clearanceBones.Length; i++)
            {
                Transform bone = clearanceBones[i];
                if (bone == null || clearanceSides[i] == IgnoredSide) continue;
                Vector3 local = (parent != null ? parent.InverseTransformPoint(bone.position) : bone.position) - closedLocalPosition;
                float along = Vector3.Dot(local, axis);
                float radial = (local - axis * along).magnitude;
                // A bone near the free edge counts as clear of the panel: that is where a hand wraps around the door.
                bool inReach = radial > ClearanceMinRadius && radial <= doorReach - clearanceEdgeBand && along >= doorAxisMin - clearanceMargin && along <= doorAxisMax + clearanceMargin;
                float boneAngle = GetOpenAngleTowards(bone.position);
                float margin = Mathf.Rad2Deg * clearanceMargin / Mathf.Max(radial, 0.2f);
                // Only the sector the panel can sweep through matters (not e.g. a hand ahead of the hinge).
                inReach &= boneAngle > -45f && boneAngle < MaxOpenAngle + 45f;

                // The side is taken when the bone is first seen and only changes while the bone is clear of the door
                // (e.g. reaching around its edge).
                float difference = boneAngle - clearanceAngle;
                if (clearanceSides[i] == 0) clearanceSides[i] = difference >= 0f ? 1 : -1;
                else if (!inReach && Mathf.Abs(difference) > margin) clearanceSides[i] = difference > 0f ? 1 : -1;
                // A bone that still ended up well on the other side (the clip moved it faster than the door can follow)
                // now belongs to that side, so e.g. a hand that reached into the cabin keeps the door from closing on it.
                else if (inReach && Mathf.Abs(difference) > 2f * margin && (difference > 0f) != (clearanceSides[i] > 0)) clearanceSides[i] = -clearanceSides[i];
                if (!inReach) continue;

                // Inner side: the bone pushes the door open / keeps it from closing onto it.
                // Outer side (e.g. a hand on the handle): the door does not swing past the bone (it follows the pulling hand),
                // but is never pulled closed by it either.
                if (clearanceSides[i] < 0) lowest = Mathf.Max(lowest, boneAngle + margin);
                else highest = Mathf.Min(highest, Mathf.Max(boneAngle - margin, clearanceAngle));
            }

            clearanceLowest = lowest;
            clearanceHighest = highest;
            float angle = Mathf.MoveTowards(clearanceAngle, current, clearanceReleaseSpeed * Time.deltaTime);
            if (lowest <= highest) angle = Mathf.Clamp(angle, lowest, highest);
            else angle = lowest;
            angle = Mathf.Clamp(angle, 0f, MaxOpenAngle);
            clearanceAngle = angle;
            if (Mathf.Abs(angle - current) < 0.01f) return;

            // Held open by a bone after a close finished: keep the physics part held until it really closes.
            if (angle > 0.5f && !IsOpen && !IsMoving)
            {
                SetPhysicsHeld(true);
                IsOpen = true;
            }
            ApplyAngle(angle * Mathf.Sign(doorOpenAngle));
        }

        // Reach (distance from the hinge line) and height span of the closed door, from its mesh.
        private void MeasureDoorExtent()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null) filter = GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || transform.parent == null) return;

            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 axis = closedLocalRotation * hingeAxis;
            float reach = 0f, min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.parent.InverseTransformPoint(filter.transform.TransformPoint(corner)) - closedLocalPosition;
                float along = Vector3.Dot(local, axis);
                reach = Mathf.Max(reach, (local - axis * along).magnitude);
                min = Mathf.Min(min, along);
                max = Mathf.Max(max, along);
            }
            doorReach = reach;
            doorAxisMin = min;
            doorAxisMax = max;
        }

        private void OnDisable()
        {
            if (moveRoutine == null) return;
            StopCoroutine(moveRoutine);
            moveRoutine = null;
            ApplyAngle(IsOpen ? doorOpenAngle : 0f);
            if (!IsOpen) SetPhysicsHeld(false);
        }

        /// <summary>Starts opening. Returns false if the door is moving or unavailable.</summary>
        public bool Open() => Open(doorOpenDuration);

        /// <summary>Starts opening over the given duration (seconds; values &lt;= 0 use the door's own duration).</summary>
        public bool Open(float duration)
        {
            if (IsMoving || !IsAvailable) return false;
            float remaining = MaxOpenAngle - OpenAngle;
            if (IsOpen && remaining < 0.5f) return true;
            SetPhysicsHeld(true);
            IsOpen = true;
            // A door already pushed partly open keeps its momentum (ease-out) instead of starting from rest.
            bool alreadyMoving = OpenAngle > 1f;
            moveRoutine = StartCoroutine(RotateRoutine(doorOpenAngle, duration > 0f ? duration : doorOpenDuration, alreadyMoving));
            return true;
        }

        /// <summary>
        /// Opening angle (degrees) the door needs to reach a world point, measured around the hinge.
        /// Negative = the point is on the inside of the closed door.
        /// </summary>
        public float GetOpenAngleTowards(Vector3 worldPoint)
        {
            Transform parent = transform.parent;
            Vector3 local = (parent != null ? parent.InverseTransformPoint(worldPoint) : worldPoint) - closedLocalPosition;
            Vector3 axis = closedLocalRotation * hingeAxis;
            Vector3 edge = Vector3.ProjectOnPlane(closedLocalRotation * edgeDirection, axis);
            float angle = Vector3.SignedAngle(edge, Vector3.ProjectOnPlane(local, axis), axis);
            return angle * Mathf.Sign(doorOpenAngle);
        }

        /// <summary>
        /// Puts the door directly at an opening angle (degrees, clamped to 0..MaxOpenAngle), e.g. following a pushing hand.
        /// Stops any swing in progress.
        /// </summary>
        public bool SetOpenAngle(float openAngle)
        {
            if (!IsAvailable) return false;
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            float clamped = Mathf.Clamp(openAngle, 0f, MaxOpenAngle);
            if (clamped > 0f)
            {
                SetPhysicsHeld(true);
                IsOpen = true;
            }
            ApplyAngle(clamped * Mathf.Sign(doorOpenAngle));
            return true;
        }

        /// <summary>Starts closing. Returns false if the door is moving or unavailable.</summary>
        public bool Close() => Close(doorCloseDuration);

        /// <summary>Starts closing over the given duration (seconds; values &lt;= 0 use the door's own duration).</summary>
        public bool Close(float duration)
        {
            if (IsMoving || !IsAvailable) return false;
            if (!IsOpen) return true;
            IsOpen = false;
            moveRoutine = StartCoroutine(RotateRoutine(0f, duration > 0f ? duration : doorCloseDuration, false));
            return true;
        }

        private IEnumerator RotateRoutine(float targetAngle, float duration, bool easeOut)
        {
            float startAngle = GetCurrentAngle();
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                k = easeOut ? 1f - (1f - k) * (1f - k) : Mathf.SmoothStep(0f, 1f, k);
                ApplyAngle(ConstrainSwing(Mathf.Lerp(startAngle, targetAngle, k)));
                yield return null;
            }
            // A close held by a registered bone (e.g. the hand that pulls the door shut) finishes as soon as it lets go.
            // An opening held by a hand simply stays where the hand left it.
            for (float held = 0f; !IsOpen && held < MaxClearanceHold && clearanceBones != null && Mathf.Abs(ConstrainSwing(targetAngle) - targetAngle) > 0.05f; held += Time.deltaTime)
            {
                ApplyAngle(ConstrainSwing(targetAngle));
                yield return null;
            }
            ApplyAngle(ConstrainSwing(targetAngle));
            moveRoutine = null;

            if (IsOpen)
            {
                OnOpened?.Invoke(this);
            }
            else
            {
                SetPhysicsHeld(false);
                OnClosed?.Invoke(this);
            }
        }

        private float GetCurrentAngle()
        {
            Quaternion offset = Quaternion.Inverse(closedLocalRotation) * transform.localRotation;
            offset.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return Vector3.Dot(axis, hingeAxis) < 0f ? -angle : angle;
        }

        private void ApplyAngle(float angle)
        {
            transform.localPosition = closedLocalPosition;
            transform.localRotation = closedLocalRotation * Quaternion.AngleAxis(angle, hingeAxis);
        }

        private void SetPhysicsHeld(bool held)
        {
            if (held == physicsHeld) return;
            physicsHeld = held;

            if (doorJoint != null)
            {
                if (held)
                {
                    savedJointMotions[0] = doorJoint.xMotion;
                    savedJointMotions[1] = doorJoint.yMotion;
                    savedJointMotions[2] = doorJoint.zMotion;
                    savedJointMotions[3] = doorJoint.angularXMotion;
                    savedJointMotions[4] = doorJoint.angularYMotion;
                    savedJointMotions[5] = doorJoint.angularZMotion;
                    doorJoint.xMotion = ConfigurableJointMotion.Free;
                    doorJoint.yMotion = ConfigurableJointMotion.Free;
                    doorJoint.zMotion = ConfigurableJointMotion.Free;
                    doorJoint.angularXMotion = ConfigurableJointMotion.Free;
                    doorJoint.angularYMotion = ConfigurableJointMotion.Free;
                    doorJoint.angularZMotion = ConfigurableJointMotion.Free;
                }
                else
                {
                    // Only axes still free are restored, in case something else (e.g. vehicle damage) changed the joint meanwhile.
                    if (doorJoint.xMotion == ConfigurableJointMotion.Free) doorJoint.xMotion = savedJointMotions[0];
                    if (doorJoint.yMotion == ConfigurableJointMotion.Free) doorJoint.yMotion = savedJointMotions[1];
                    if (doorJoint.zMotion == ConfigurableJointMotion.Free) doorJoint.zMotion = savedJointMotions[2];
                    if (doorJoint.angularXMotion == ConfigurableJointMotion.Free) doorJoint.angularXMotion = savedJointMotions[3];
                    if (doorJoint.angularYMotion == ConfigurableJointMotion.Free) doorJoint.angularYMotion = savedJointMotions[4];
                    if (doorJoint.angularZMotion == ConfigurableJointMotion.Free) doorJoint.angularZMotion = savedJointMotions[5];
                }
            }

            if (doorBody != null)
            {
                if (held)
                {
                    bodyWasKinematic = doorBody.isKinematic;
                    doorBody.isKinematic = true;
                }
                else
                {
                    doorBody.isKinematic = bodyWasKinematic;
                }
            }
        }
    }
}
