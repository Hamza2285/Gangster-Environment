using System.Collections.Generic;
using Invector.vCamera;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Connects the Invector player to the map, taxi and route line (<see cref="IGamePlayer"/>). Lives on the player root.
    /// Those systems move the object they get as <see cref="CameraPivot"/> and reparent it, which would break the Invector camera,
    /// so they get a stand-in transform and this script copies it to the Main Camera while riding in a taxi.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // After TaxiService.LateUpdate has moved the pivot.
    public class InvectorGamePlayer : MonoBehaviour, IGamePlayer
    {
        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CapsuleCollider capsule;
        [Tooltip("Hidden while the player rides in a taxi.")]
        [SerializeField] private GameObject characterModel;

        [Header("Camera")]
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;
        [SerializeField] private Camera playerCamera;
        [Tooltip("Empty child of the player that the taxi moves during a trip.")]
        [SerializeField] private Transform rideCameraPivot;

        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private bool controlEnabled = true;
        private bool riding;

        public Transform CameraPivot => rideCameraPivot;
        public bool ControlEnabled => controlEnabled;
        public float FacingYaw => transform.eulerAngles.y;

        private void Awake()
        {
            if (playerManager == null || vehicleInteraction == null || body == null || capsule == null || characterModel == null
                || thirdPersonCamera == null || playerCamera == null || rideCameraPivot == null)
            {
                Debug.LogError($"[{nameof(InvectorGamePlayer)}] Missing references on '{name}'; the map and taxi will not work with this player.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            GamePlayer.Register(this);
            vehicleInteraction.OnVehicleEnterStarted += HandleVehicleEnterStarted;
            vehicleInteraction.OnVehicleExited += HandleVehicleExited;
        }

        private void OnDisable()
        {
            GamePlayer.Unregister(this);
            GamePlayer.InVehicle = false;
            vehicleInteraction.OnVehicleEnterStarted -= HandleVehicleEnterStarted;
            vehicleInteraction.OnVehicleExited -= HandleVehicleExited;
        }

        private void LateUpdate()
        {
            if (riding) playerCamera.transform.SetPositionAndRotation(rideCameraPivot.position, rideCameraPivot.rotation);
        }

        public void SetControl(bool enabledNow)
        {
            if (controlEnabled == enabledNow) return;
            controlEnabled = enabledNow;
            // The full map can open while driving; the vehicle system owns the player then.
            if (!playerManager.IsInVehicle) playerManager.SetInputLocked(!enabledNow);
            if (enabledNow) EndRide();
        }

        public void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void ResetLook()
        {
            thirdPersonCamera.mouseX = transform.eulerAngles.y;
        }

        public void SetRidingPose(Vector3 position, float yaw)
        {
            if (!riding) BeginRide();
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        public void Teleport(Vector3 feetPosition, float yaw)
        {
            EndRide();
            transform.SetPositionAndRotation(feetPosition, Quaternion.Euler(0f, yaw, 0f));
            body.position = feetPosition;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private void BeginRide()
        {
            riding = true;
            body.linearVelocity = Vector3.zero;
            body.isKinematic = true;
            capsule.enabled = false;
            thirdPersonCamera.enabled = false;
            HideModel();
        }

        private void EndRide()
        {
            if (!riding) return;
            riding = false;
            body.isKinematic = false;
            capsule.enabled = true;
            thirdPersonCamera.enabled = true;
            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
            ShowModel();
        }

        // Looked up per ride: equipped weapons are added to the model at runtime.
        private void HideModel()
        {
            foreach (Renderer part in characterModel.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled) continue;
                part.enabled = false;
                hiddenRenderers.Add(part);
            }
        }

        private void ShowModel()
        {
            foreach (Renderer part in hiddenRenderers)
            {
                if (part != null) part.enabled = true;
            }
            hiddenRenderers.Clear();
        }

        private void HandleVehicleEnterStarted(OpenworldCrime.Vehicles.IVehicleController vehicle) => GamePlayer.InVehicle = true;

        private void HandleVehicleExited(OpenworldCrime.Vehicles.IVehicleController vehicle) => GamePlayer.InVehicle = false;
    }
}
