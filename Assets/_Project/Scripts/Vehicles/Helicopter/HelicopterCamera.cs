using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Chase camera for flying (on its own camera object, switched on only while a helicopter is piloted, like the RCC camera
    /// for cars). Sits behind and above the helicopter, swings round with its heading (not its tilt), pulls back a little with
    /// speed and looks slightly ahead; never dips under the ground. Runs in LateUpdate after the physics move.
    /// Also gives the fixed side view used while the pilot gets in / out (<see cref="HelicopterBoarding"/>): a point and a look
    /// point in the helicopter's space; from there <see cref="BlendBehind"/> swings round behind instead of cutting.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class HelicopterCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(1f)] private float distance = 15f;
        [SerializeField] private float height = 5.5f;
        [Tooltip("Extra distance (m) at full speed.")]
        [SerializeField, Min(0f)] private float speedPullBack = 8f;
        [SerializeField, Min(1f)] private float fullSpeed = 40f;
        [Tooltip("Look point ahead of the helicopter (m).")]
        [SerializeField] private float lookAhead = 6f;
        [SerializeField] private float lookHeight = 1.5f;
        [SerializeField, Min(0.1f)] private float followSharpness = 4f;
        [SerializeField, Min(0.1f)] private float yawSharpness = 2.5f;
        [SerializeField] private LayerMask groundLayers = 1;
        [Tooltip("Share (0-1) of the target's nose up / down the camera follows (0 = stays level: helicopter; a plane follows " +
                 "part of its climb / dive so it stays behind the tail).")]
        [SerializeField, Range(0f, 1f)] private float pitchFollow = 0f;
        [Tooltip("Largest pitch (deg) the camera follows.")]
        [SerializeField, Range(0f, 80f)] private float maxFollowPitch = 35f;

        [Tooltip("How fast (1/s) the camera moves to / follows the boarding side view.")]
        [SerializeField, Min(0.1f)] private float boardingViewSharpness = 3f;

        private Camera cam;
        private Rigidbody targetBody;
        private float yaw;
        private float pitch;
        private bool fixedView;
        private Vector3 viewPosition, viewLookAt;
        private Vector3 offset;   // camera - target, smoothed
        private float speed01;

        public Camera Camera => cam != null ? cam : (cam = GetComponent<Camera>());

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (target != null) targetBody = target.GetComponent<Rigidbody>();
        }

        /// <summary>Puts the camera straight behind the target (on taking the controls).</summary>
        public void SnapBehind(Transform follow)
        {
            SetTarget(follow);
            fixedView = false;
            if (target == null) return;
            yaw = target.eulerAngles.y;
            pitch = TargetPitch();
            speed01 = 0f;
            transform.position = DesiredPosition(0f);
            transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
            offset = transform.position - target.position;
        }

        /// <summary>
        /// Leaves the boarding view: from where the camera is now it swings round the helicopter to the chase position behind
        /// (the heading starts at the camera's current bearing and eases to the helicopter's).
        /// </summary>
        public void BlendBehind(Transform follow)
        {
            SetTarget(follow);
            fixedView = false;
            if (target == null) return;
            Vector3 fromTarget = transform.position - target.position;
            fromTarget.y = 0f;
            yaw = fromTarget.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-fromTarget).eulerAngles.y : target.eulerAngles.y;
            offset = transform.position - target.position;
        }

        /// <summary>
        /// Fixed view in the helicopter's space (<paramref name="localPosition"/> looking at <paramref name="localLookAt"/>) that
        /// follows it. <paramref name="glide"/>: move there from the current pose, else cut straight to it.
        /// </summary>
        public void ShowFixedView(Transform follow, Vector3 localPosition, Vector3 localLookAt, bool glide)
        {
            SetTarget(follow);
            if (target == null) return;
            fixedView = true;
            viewPosition = localPosition;
            viewLookAt = localLookAt;
            if (glide) return;
            transform.position = target.TransformPoint(viewPosition);
            transform.rotation = Quaternion.LookRotation(target.TransformPoint(viewLookAt) - transform.position);
        }

        private void SetTarget(Transform follow)
        {
            target = follow;
            targetBody = follow != null ? follow.GetComponent<Rigidbody>() : null;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            if (fixedView)
            {
                float k = 1f - Mathf.Exp(-boardingViewSharpness * dt);
                transform.position = Vector3.Lerp(transform.position, target.TransformPoint(viewPosition), k);
                Quaternion view = Quaternion.LookRotation(target.TransformPoint(viewLookAt) - transform.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, view, k * 1.5f);
                offset = transform.position - target.position;
                return;
            }
            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, 1f - Mathf.Exp(-yawSharpness * dt));
            pitch = Mathf.Lerp(pitch, TargetPitch(), 1f - Mathf.Exp(-yawSharpness * dt));
            float speed = targetBody != null ? targetBody.linearVelocity.magnitude : 0f;
            speed01 = Mathf.Lerp(speed01, Mathf.Clamp01(speed / fullSpeed), 1f - Mathf.Exp(-2f * dt));
            Vector3 desired = DesiredPosition(speed01);
            // Never under the ground (one ray down from the desired point).
            if (Physics.Raycast(desired + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 60f, groundLayers, QueryTriggerInteraction.Ignore))
                desired.y = Mathf.Max(desired.y, hit.point.y + 1.5f);
            // The offset from the target is smoothed, not the world position: smoothing a fast target's world position lags
            // by speed x frame time, which shakes the view (most of all on a jet) whenever the frame time varies.
            Vector3 desiredOffset = desired - target.position;
            offset = Vector3.Lerp(offset, desiredOffset, 1f - Mathf.Exp(-followSharpness * dt));
            transform.position = target.position + offset;
            Quaternion look = Quaternion.LookRotation(LookPoint() - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-followSharpness * 1.5f * dt));
        }

        // Nose up = negative (Unity's X rotation), scaled by Pitch Follow.
        private float TargetPitch()
        {
            if (pitchFollow <= 0f || target == null) return 0f;
            Vector3 f = target.forward;
            float nose = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(nose * pitchFollow, -maxFollowPitch, maxFollowPitch);
        }

        private Vector3 DesiredPosition(float speed01)
        {
            Quaternion heading = Quaternion.Euler(pitch, yaw, 0f);
            return target.position + heading * new Vector3(0f, height, -(distance + speedPullBack * speed01));
        }

        private Vector3 LookPoint()
        {
            Quaternion heading = Quaternion.Euler(pitch, yaw, 0f);
            return target.position + Vector3.up * lookHeight + heading * Vector3.forward * lookAhead;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform follow) => target = follow;

        /// <summary>Chase framing (a plane sits farther back and follows part of the climb / dive).</summary>
        public void EditorSetupFraming(float chaseDistance, float chaseHeight, float pullBack, float pullBackSpeed, float ahead,
            float lookUp, float follow, float turnFollow, float pitchShare)
        {
            distance = chaseDistance; height = chaseHeight; speedPullBack = pullBack; fullSpeed = pullBackSpeed;
            lookAhead = ahead; lookHeight = lookUp; followSharpness = follow; yawSharpness = turnFollow; pitchFollow = pitchShare;
        }
#endif
    }
}
