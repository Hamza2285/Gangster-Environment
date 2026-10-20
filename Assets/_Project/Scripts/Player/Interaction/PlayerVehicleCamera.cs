using Invector.vCamera;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Camera framing for getting in and out of a vehicle, driven by <see cref="PlayerVehicleInteraction"/> events.
    /// Both sequences are filmed from one view behind the vehicle on the driver side (back-left for a left-hand drive car):
    /// - Entering: the player's Main Camera blends from the third-person camera to that view (the vehicle camera takes over once seated).
    /// - Exiting: it starts where the vehicle camera was, moves to that view while the character climbs out, and blends
    ///   back to the third-person camera once on-foot control returns.
    /// The Invector camera keeps running underneath; only its Main Camera child is overridden.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleCamera : MonoBehaviour
    {
        private const int MaxCastHits = 8;

        private enum Mode
        {
            None,
            Enter,
            Exit,
            Return
        }

        [Header("References")]
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [Tooltip("Invector vThirdPersonCamera (parent of the Main Camera).")]
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;
        [Tooltip("Main Camera under the vThirdPersonCamera object (sits at local identity).")]
        [SerializeField] private Camera playerCamera;

        [Header("View (driver seat space: x = passenger side, y = up, z = forward)")]
        [Tooltip("Camera position relative to the driver seat. Negative z = behind the vehicle, negative x = driver side.")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(-2.2f, 2f, -4f);
        [Tooltip("Point the camera looks at, relative to the driver seat.")]
        [SerializeField] private Vector3 lookOffset = new Vector3(-0.6f, 1f, 0.1f);

        [Header("Blending (seconds)")]
        [Tooltip("Third-person camera -> view when entering starts.")]
        [SerializeField, Min(0.01f)] private float enterBlendDuration = 0.6f;
        [Tooltip("Vehicle camera -> view when exiting starts.")]
        [SerializeField, Min(0.01f)] private float exitBlendDuration = 0.8f;
        [Tooltip("View -> third-person camera once the character is back on foot.")]
        [SerializeField, Min(0.01f)] private float returnBlendDuration = 0.9f;

        [Header("Obstacles")]
        [Tooltip("Layers that push the view closer when between the vehicle and the camera (the vehicle and player are ignored).")]
        [SerializeField] private LayerMask obstacleLayers = 1;
        [SerializeField, Min(0.01f)] private float obstacleRadius = 0.2f;

        private readonly RaycastHit[] castHits = new RaycastHit[MaxCastHits];
        private Transform cameraTransform;
        private IVehicleController vehicle;
        private Mode mode = Mode.None;
        private float progress;
        private bool hasExitStart;
        private Vector3 exitStartPosition;
        private Quaternion exitStartRotation;
        private float exitStartFieldOfView;

        private void Awake()
        {
            if (vehicleInteraction == null || playerCamera == null)
            {
                Debug.LogError($"[{nameof(PlayerVehicleCamera)}] Missing references on '{name}' (PlayerVehicleInteraction, player Main Camera). Vehicle camera framing is disabled.", this);
                enabled = false;
                return;
            }
            cameraTransform = playerCamera.transform;
        }

        private void OnEnable()
        {
            if (vehicleInteraction == null) return;
            vehicleInteraction.OnVehicleEnterStarted += HandleEnterStarted;
            vehicleInteraction.OnVehicleEntered += HandleEntered;
            vehicleInteraction.OnVehicleExitStarted += HandleExitStarted;
            vehicleInteraction.OnVehicleExited += HandleExited;
        }

        private void OnDisable()
        {
            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnVehicleEnterStarted -= HandleEnterStarted;
                vehicleInteraction.OnVehicleEntered -= HandleEntered;
                vehicleInteraction.OnVehicleExitStarted -= HandleExitStarted;
                vehicleInteraction.OnVehicleExited -= HandleExited;
            }
            StopOverride();
        }

        private void HandleEnterStarted(IVehicleController target)
        {
            vehicle = target;
            mode = Mode.Enter;
            progress = 0f;
        }

        // The player camera is switched off once seated (the vehicle camera takes over).
        private void HandleEntered(IVehicleController target)
        {
            StopOverride();
        }

        // Raised before the vehicle camera is switched off, so its final view can be captured.
        private void HandleExitStarted(IVehicleController target)
        {
            vehicle = target;
            mode = Mode.Exit;
            progress = 0f;

            Camera vehicleCamera = target != null ? target.GetActiveCamera() : null;
            hasExitStart = vehicleCamera != null;
            if (hasExitStart)
            {
                exitStartPosition = vehicleCamera.transform.position;
                exitStartRotation = vehicleCamera.transform.rotation;
                exitStartFieldOfView = vehicleCamera.fieldOfView;
            }

            // The third-person camera was off while driving; face it along the seat so it ends behind the character.
            if (thirdPersonCamera != null && thirdPersonCamera.currentTarget != null) thirdPersonCamera.ResetAngleWithoutSmooth();
            LateUpdate();
        }

        private void HandleExited(IVehicleController target)
        {
            if (mode != Mode.Exit) return;
            mode = Mode.Return;
            progress = 0f;
        }

        private void LateUpdate()
        {
            if (mode == Mode.None) return;
            if (vehicle == null || (vehicle as Object) == null || vehicle.GetDriverSeat() == null)
            {
                StopOverride();
                return;
            }

            GetView(out Vector3 viewPosition, out Quaternion viewRotation);
            float thirdPersonFov = GetThirdPersonFieldOfView();

            switch (mode)
            {
                case Mode.Enter:
                {
                    progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / enterBlendDuration);
                    float k = Mathf.SmoothStep(0f, 1f, progress);
                    GetThirdPersonPose(out Vector3 basePosition, out Quaternion baseRotation);
                    Apply(Vector3.Lerp(basePosition, viewPosition, k), Quaternion.Slerp(baseRotation, viewRotation, k), thirdPersonFov);
                    break;
                }
                case Mode.Exit:
                {
                    progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / exitBlendDuration);
                    if (!hasExitStart)
                    {
                        Apply(viewPosition, viewRotation, thirdPersonFov);
                        break;
                    }
                    float k = Mathf.SmoothStep(0f, 1f, progress);
                    Apply(Vector3.Lerp(exitStartPosition, viewPosition, k), Quaternion.Slerp(exitStartRotation, viewRotation, k),
                        Mathf.Lerp(exitStartFieldOfView, thirdPersonFov, k));
                    break;
                }
                case Mode.Return:
                {
                    progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / returnBlendDuration);
                    if (progress >= 1f)
                    {
                        StopOverride();
                        return;
                    }
                    float k = Mathf.SmoothStep(0f, 1f, progress);
                    GetThirdPersonPose(out Vector3 basePosition, out Quaternion baseRotation);
                    Apply(Vector3.Lerp(viewPosition, basePosition, k), Quaternion.Slerp(viewRotation, baseRotation, k), thirdPersonFov);
                    break;
                }
            }
        }

        private void GetView(out Vector3 position, out Quaternion rotation)
        {
            Transform seat = vehicle.GetDriverSeat();
            Vector3 lookPoint = seat.TransformPoint(lookOffset);
            position = GetUnobstructedPosition(lookPoint, seat.TransformPoint(cameraOffset));
            rotation = Quaternion.LookRotation(lookPoint - position, Vector3.up);
        }

        // Where the Invector camera puts the Main Camera (its parent's pose).
        private void GetThirdPersonPose(out Vector3 position, out Quaternion rotation)
        {
            Transform parent = cameraTransform.parent;
            position = parent != null ? parent.position : cameraTransform.position;
            rotation = parent != null ? parent.rotation : cameraTransform.rotation;
        }

        private void Apply(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            cameraTransform.SetPositionAndRotation(position, rotation);
            playerCamera.fieldOfView = fieldOfView;
        }

        private float GetThirdPersonFieldOfView()
        {
            return thirdPersonCamera != null && thirdPersonCamera.currentState != null ? thirdPersonCamera.currentState.fov : playerCamera.fieldOfView;
        }

        private Vector3 GetUnobstructedPosition(Vector3 from, Vector3 desired)
        {
            Vector3 direction = desired - from;
            float distance = direction.magnitude;
            if (distance < 0.001f) return desired;
            direction /= distance;

            float closest = distance;
            int count = Physics.SphereCastNonAlloc(from, obstacleRadius, direction, castHits, distance, obstacleLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = castHits[i].transform;
                if (hit.IsChildOf(vehicle.transform) || hit.IsChildOf(transform)) continue;
                if (castHits[i].distance > 0f && castHits[i].distance < closest) closest = castHits[i].distance;
            }
            return from + direction * closest;
        }

        private void StopOverride()
        {
            mode = Mode.None;
            vehicle = null;
            progress = 0f;
            if (cameraTransform == null) return;
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
            playerCamera.fieldOfView = GetThirdPersonFieldOfView();
        }
    }
}
