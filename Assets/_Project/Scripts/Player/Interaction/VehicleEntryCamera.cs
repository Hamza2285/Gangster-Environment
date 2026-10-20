using Invector.vCamera;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Camera while getting into a vehicle, driven by <see cref="PlayerVehicleInteraction"/> events (no per-frame work outside
    /// the short blend):
    /// - Walking to the door (e.g. around the car from the passenger side): the Invector third-person camera swings smoothly
    ///   behind the character so he stays in view.
    /// - Getting in: the third-person camera holds the view it had at the door while the character takes the handle and opens
    ///   the door; when he starts climbing in it swings (yaw) to a view from the far side of the car, so the car body hides the
    ///   legs going in (the enter framing of <see cref="PlayerVehicleCamera"/> is paused for the enter; its exit framing is unchanged).
    /// - Seated: the vehicle camera takes over with a short blend from where the player camera was, instead of a cut.
    /// - Bailing out of a moving vehicle: the player camera starts exactly where the vehicle camera was and blends onto the
    ///   third-person camera, which follows the diving / tumbling character; while he is down it tilts toward his body, so a
    ///   character lying on the road is framed instead of sitting at the bottom edge.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after the vehicle camera has placed itself this frame
    public class VehicleEntryCamera : MonoBehaviour
    {
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [Tooltip("Invector vThirdPersonCamera that follows the player.")]
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;
        [Tooltip("Player Main Camera (its last view is where the vehicle camera blend starts).")]
        [SerializeField] private Camera playerCamera;
        [Tooltip("Enter/exit framing; paused while getting in so the normal follow camera stays.")]
        [SerializeField] private PlayerVehicleCamera enterExitFraming;
        [Tooltip("How quickly the camera swings behind the walking character (Invector 'behind target' smoothing; lower = softer).")]
        [SerializeField, Min(0.1f)] private float followSmoothing = 2.5f;
        [Header("Get-in View")]
        [Tooltip("When the character climbs in, the camera swings around (yaw) to a view from behind the vehicle on the far side of the " +
                 "driver door, looking over the car at the door, so the car body hides the legs going in. Degrees from straight " +
                 "behind the vehicle toward that far side.")]
        [SerializeField, Range(0f, 90f)] private float enterViewYaw = 25f;
        [Tooltip("Horizontal distance (m) of that view from the driver seat.")]
        [SerializeField, Min(1f)] private float enterViewDistance = 3.6f;
        [Tooltip("Height (m) of that view above the driver seat.")]
        [SerializeField] private float enterViewHeight = 2.2f;
        [Tooltip("Height (m) above the driver seat the view looks at.")]
        [SerializeField] private float enterViewLookHeight = 0.9f;
        [Tooltip("Seconds of the swing into that view (it starts as the character climbs in, so keep it short enough to turn " +
                 "away before the legs pass the door sill).")]
        [SerializeField, Min(0.05f)] private float enterPullDuration = 0.6f;
        [Tooltip("Layers that keep the view in front of walls.")]
        [SerializeField] private LayerMask pullBackObstacles = 1;
        [Tooltip("Seconds of the blend from the player camera into the vehicle camera once seated.")]
        [SerializeField, Min(0f)] private float vehicleBlendDuration = 1f;
        [Tooltip("Seconds of the blend from the vehicle camera onto the third-person camera when bailing out of a moving vehicle.")]
        [SerializeField, Min(0f)] private float bailBlendDuration = 0.9f;
        [Tooltip("How much the camera turns toward the tumbling body while bailing out (0 = plain third-person camera).")]
        [SerializeField, Range(0f, 1f)] private float bailLookAtBody = 0.6f;
        [Tooltip("Seconds to ease the look-at-body tilt in and out.")]
        [SerializeField, Min(0.05f)] private float bailLookFade = 0.5f;

        private bool following;
        private bool held;
        private bool savedLockCamera;
        private bool savedAutoBehind;
        private float savedBehindSmooth;

        private Camera blendCamera;
        private Vector3 blendStartPosition;
        private Quaternion blendStartRotation;
        private float blendStartFieldOfView;
        private Vector3 blendLocalPosition;
        private Quaternion blendLocalRotation;
        private float blendTime = -1f;
        private float pullTime = -1f;
        private float bailTime = -1f;
        private Vector3 bailStartPosition;
        private Quaternion bailStartRotation;
        private float bailStartFieldOfView;
        private bool bailing;
        private float bailLookWeight;
        private Transform bodyHips;
        private bool hasLastView;
        private Vector3 lastViewPosition;
        private Quaternion lastViewRotation;
        private Vector3 viewPivot;
        private Vector3 viewStartPosition;
        private Quaternion viewStartRotation;
        private Vector3 viewTargetPosition;
        private Quaternion viewTargetRotation;

        private void Awake()
        {
            if (vehicleInteraction == null || thirdPersonCamera == null)
            {
                Debug.LogWarning($"[{nameof(VehicleEntryCamera)}] Missing PlayerVehicleInteraction or vThirdPersonCamera on '{name}'; the vehicle entry camera is disabled.", this);
                enabled = false;
                return;
            }
            Animator bodyAnimator = vehicleInteraction.GetComponent<Animator>();
            bodyHips = bodyAnimator != null && bodyAnimator.isHuman ? bodyAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
        }

        private void OnEnable()
        {
            if (vehicleInteraction == null) return;
            vehicleInteraction.OnStateChanged += HandleStateChanged;
            vehicleInteraction.OnVehicleEntered += HandleEntered;
            vehicleInteraction.OnVehicleClimbStarted += HandleClimbStarted;
            vehicleInteraction.OnVehicleBailStarted += HandleBailStarted;
        }

        private void OnDisable()
        {
            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnStateChanged -= HandleStateChanged;
                vehicleInteraction.OnVehicleEntered -= HandleEntered;
                vehicleInteraction.OnVehicleClimbStarted -= HandleClimbStarted;
                vehicleInteraction.OnVehicleBailStarted -= HandleBailStarted;
            }
            SetFollowing(false);
            SetHeld(false);
            EndBlend();
            EndBailBlend();
            if (enterExitFraming != null) enterExitFraming.enabled = true;
        }

        private void HandleStateChanged(VehicleInteractionState state)
        {
            SetFollowing(state == VehicleInteractionState.Approaching);
            SetHeld(state == VehicleInteractionState.Entering);
            bailing = state == VehicleInteractionState.Bailing;
            // The framing is only paused for the enter itself; any other state (in the car, exiting, back on foot) has it back.
            if (enterExitFraming != null) enterExitFraming.enabled = state != VehicleInteractionState.Entering;
        }

        // The grab and door opening are watched from behind the character; the swing starts as he climbs in.
        private void HandleClimbStarted(IVehicleController vehicle)
        {
            if (held && pullTime < 0f) StartPullBack();
        }

        // Raised once seated, after the player camera was switched off and the vehicle camera on.
        private void HandleEntered(IVehicleController vehicle)
        {
            EndBlend();
            Camera vehicleCamera = vehicle != null ? vehicle.GetActiveCamera() : null;
            if (vehicleCamera == null || playerCamera == null || vehicleBlendDuration <= 0f) return;
            blendCamera = vehicleCamera;
            blendStartPosition = hasLastView ? lastViewPosition : playerCamera.transform.position;
            blendStartRotation = hasLastView ? lastViewRotation : playerCamera.transform.rotation;
            hasLastView = false;
            blendStartFieldOfView = playerCamera.fieldOfView;
            blendLocalPosition = vehicleCamera.transform.localPosition;
            blendLocalRotation = vehicleCamera.transform.localRotation;
            blendTime = 0f;
        }

        // Raised while the vehicle camera is still rendering: the player camera takes over from its exact view.
        private void HandleBailStarted(IVehicleController vehicle)
        {
            EndBlend();
            Camera vehicleCamera = vehicle != null ? vehicle.GetActiveCamera() : null;
            if (playerCamera == null || vehicleCamera == null || bailBlendDuration <= 0f) return;
            bailStartPosition = vehicleCamera.transform.position;
            bailStartRotation = vehicleCamera.transform.rotation;
            bailStartFieldOfView = vehicleCamera.fieldOfView;
            // The third-person camera was off while driving; face it along the character (the seat) so it ends up behind him.
            if (thirdPersonCamera != null && thirdPersonCamera.currentTarget != null) thirdPersonCamera.ResetAngleWithoutSmooth();
            bailTime = 0f;
            ApplyBailBlend();
        }

        private void LateUpdate()
        {
            if (bailTime >= 0f)
            {
                bailTime += Time.deltaTime;
                ApplyBailBlend();
            }
            UpdateBailLook();
            UpdatePullBack();
            if (blendCamera == null || blendTime < 0f) return;
            blendTime += Time.deltaTime;
            float k = SmootherStep(blendTime / vehicleBlendDuration);
            Transform cameraTransform = blendCamera.transform;
            Transform parent = cameraTransform.parent;
            Vector3 targetPosition = parent != null ? parent.TransformPoint(blendLocalPosition) : blendLocalPosition;
            Quaternion targetRotation = parent != null ? parent.rotation * blendLocalRotation : blendLocalRotation;
            cameraTransform.SetPositionAndRotation(Vector3.Lerp(blendStartPosition, targetPosition, k), Quaternion.Slerp(blendStartRotation, targetRotation, k));
            blendCamera.fieldOfView = Mathf.Lerp(blendStartFieldOfView, blendCamera.fieldOfView, k);
            if (blendTime >= vehicleBlendDuration) EndBlend();
        }

        // Swings the held player camera to the get-in view.
        private void UpdatePullBack()
        {
            if (pullTime < 0f || playerCamera == null) return;
            pullTime += Time.deltaTime;
            // Gentler ease-in than the vehicle blend so the camera is already turning away when the legs reach the sill.
            float k = Mathf.SmoothStep(0f, 1f, pullTime / enterPullDuration);
            // Around the seat (yaw, distance and height blended separately), so the swing never cuts through the car.
            Vector3 fromStart = viewStartPosition - viewPivot;
            Vector3 fromTarget = viewTargetPosition - viewPivot;
            Vector3 flatStart = new Vector3(fromStart.x, 0f, fromStart.z);
            Vector3 flatTarget = new Vector3(fromTarget.x, 0f, fromTarget.z);
            Vector3 flat = Vector3.Slerp(flatStart.normalized, flatTarget.normalized, k) * Mathf.Lerp(flatStart.magnitude, flatTarget.magnitude, k);
            Vector3 position = viewPivot + flat + Vector3.up * Mathf.Lerp(fromStart.y, fromTarget.y, k);
            playerCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(viewStartRotation, viewTargetRotation, k));
            if (pullTime >= enterPullDuration) pullTime = enterPullDuration; // keep holding the view until the get-in ends
        }

        private void StartPullBack()
        {
            IVehicleController vehicle = vehicleInteraction.CurrentVehicle;
            if (playerCamera == null || vehicle == null || (vehicle as Object) == null || vehicle.GetDriverSeat() == null) return;
            Transform car = vehicle.transform;
            Transform seat = vehicle.GetDriverSeat();

            // Far side of the driver door: the side of the car's centre line the seat is not on.
            float seatSide = Vector3.Dot(seat.position - car.position, car.right) >= 0f ? 1f : -1f;
            Vector3 behind = -car.forward;
            behind.y = 0f;
            behind.Normalize();
            Vector3 direction = Quaternion.AngleAxis(seatSide * enterViewYaw, Vector3.up) * behind;
            Vector3 focus = seat.position + Vector3.up * enterViewLookHeight;
            Vector3 position = seat.position + direction * enterViewDistance + Vector3.up * enterViewHeight;

            // Keep the view in front of walls.
            Vector3 toView = position - focus;
            float length = toView.magnitude;
            if (length > 0.001f && Physics.SphereCast(focus, 0.2f, toView / length, out RaycastHit hit, length, pullBackObstacles, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(car))
                position = focus + toView / length * Mathf.Max(1f, hit.distance - 0.1f);

            viewPivot = seat.position;
            viewStartPosition = playerCamera.transform.position;
            viewStartRotation = playerCamera.transform.rotation;
            viewTargetPosition = position;
            viewTargetRotation = Quaternion.LookRotation(focus - position, Vector3.up);
            pullTime = 0f;
        }

        private void ApplyBailBlend()
        {
            if (playerCamera == null) return;
            if (bailTime >= bailBlendDuration)
            {
                EndBailBlend();
                return;
            }
            float k = SmootherStep(bailTime / bailBlendDuration);
            Transform cameraTransform = playerCamera.transform;
            Transform parent = cameraTransform.parent;
            Vector3 basePosition = parent != null ? parent.position : cameraTransform.position;
            Quaternion baseRotation = parent != null ? parent.rotation : cameraTransform.rotation;
            float baseFieldOfView = thirdPersonCamera != null && thirdPersonCamera.currentState != null ? thirdPersonCamera.currentState.fov : playerCamera.fieldOfView;
            cameraTransform.SetPositionAndRotation(Vector3.Lerp(bailStartPosition, basePosition, k), Quaternion.Slerp(bailStartRotation, baseRotation, k));
            playerCamera.fieldOfView = Mathf.Lerp(bailStartFieldOfView, baseFieldOfView, k);
        }

        // While bailing, the view turns part of the way toward the body (after the bail blend placed the camera this frame).
        private void UpdateBailLook()
        {
            float target = bailing ? bailLookAtBody : 0f;
            if (bailLookWeight <= 0f && target <= 0f) return;
            bailLookWeight = Mathf.MoveTowards(bailLookWeight, target, Time.deltaTime / bailLookFade);
            if (playerCamera == null || bodyHips == null) return;
            Transform cameraTransform = playerCamera.transform;
            if (bailLookWeight <= 0f)
            {
                if (bailTime < 0f) cameraTransform.localRotation = Quaternion.identity;
                return;
            }
            Quaternion baseRotation = bailTime >= 0f || cameraTransform.parent == null ? cameraTransform.rotation : cameraTransform.parent.rotation;
            Vector3 toBody = bodyHips.position - cameraTransform.position;
            if (toBody.sqrMagnitude < 0.01f) return;
            Quaternion lookRotation = Quaternion.LookRotation(toBody, Vector3.up);
            cameraTransform.rotation = Quaternion.Slerp(baseRotation, lookRotation, Mathf.SmoothStep(0f, 1f, bailLookWeight / Mathf.Max(0.01f, bailLookAtBody)) * bailLookAtBody);
        }

        private void EndBailBlend()
        {
            if (bailTime < 0f) return;
            bailTime = -1f;
            if (playerCamera == null) return;
            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
            if (thirdPersonCamera != null && thirdPersonCamera.currentState != null) playerCamera.fieldOfView = thirdPersonCamera.currentState.fov;
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void EndBlend()
        {
            if (blendCamera != null)
            {
                blendCamera.transform.localPosition = blendLocalPosition;
                blendCamera.transform.localRotation = blendLocalRotation;
            }
            blendCamera = null;
            blendTime = -1f;
        }

        // While getting in, the camera holds the view it had at the door (like a real player camera would not dive into the
        // car with the character's head) until the climb-in swing; the vehicle camera blend starts from where it ends up.
        private void SetHeld(bool hold)
        {
            if (hold == held || thirdPersonCamera == null) return;
            held = hold;
            if (hold)
            {
                thirdPersonCamera.FreezeCamera();
            }
            else
            {
                // Remember the view the character was seen from (the vehicle camera blend starts there), then reset.
                if (playerCamera != null)
                {
                    lastViewPosition = playerCamera.transform.position;
                    lastViewRotation = playerCamera.transform.rotation;
                    hasLastView = true;
                    playerCamera.transform.localPosition = Vector3.zero;
                }
                thirdPersonCamera.UnFreezeCamera();
                pullTime = -1f;
            }
        }

        private void SetFollowing(bool follow)
        {
            if (follow == following || thirdPersonCamera == null) return;
            following = follow;
            if (follow)
            {
                savedLockCamera = thirdPersonCamera.LockCamera;
                savedAutoBehind = thirdPersonCamera.autoBehindTarget;
                savedBehindSmooth = thirdPersonCamera.behindTargetSmoothRotation;
                thirdPersonCamera.autoBehindTarget = true;
                thirdPersonCamera.behindTargetSmoothRotation = followSmoothing;
                thirdPersonCamera.LockCamera = true;
            }
            else
            {
                thirdPersonCamera.LockCamera = savedLockCamera;
                thirdPersonCamera.autoBehindTarget = savedAutoBehind;
                thirdPersonCamera.behindTargetSmoothRotation = savedBehindSmooth;
            }
        }
    }
}
