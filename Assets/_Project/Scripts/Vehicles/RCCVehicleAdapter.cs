using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// <see cref="IVehicleController"/> for Realistic Car Controller V3 vehicles. Every RCC-specific call
    /// (control on/off, engine, RCC camera, player-vehicle registration) lives here, so the player side
    /// never touches RCC. Lives on the vehicle root next to RCC_CarControllerV3. Not a Singleton.
    /// </summary>
    [DisallowMultipleComponent]
    public class RCCVehicleAdapter : MonoBehaviour, IVehicleController
    {
        private const float KmhToMs = 1f / 3.6f;

        [Header("RCC")]
        [SerializeField] private RCC_CarControllerV3 carController;
        [Tooltip("Scene RCC camera. Keep its GameObject inactive in the scene; it is activated and targeted at this vehicle only while driven. " +
                 "Leave empty on vehicle prefabs spawned at runtime; the spawning system assigns it (SetVehicleCamera).")]
        [SerializeField] private RCC_Camera rccCamera;
        [Tooltip("Start the engine when the driver takes control.")]
        [SerializeField] private bool startEngineOnEnter = true;
        [Tooltip("Keep the engine off while nobody drives (at start and after the driver leaves).")]
        [SerializeField] private bool engineOffWhenParked = true;

        [Header("Seat & Door Points")]
        [Tooltip("Character root anchor while seated (root of the seated pose, not the hips).")]
        [SerializeField] private Transform driverSeat;
        [Tooltip("Character root when the enter animation starts (beside the driver door, facing the car).")]
        [SerializeField] private Transform enterPoint;
        [Tooltip("Character root when the exit animation ends (driver side, facing away from the car).")]
        [SerializeField] private Transform exitPoint;
        [Tooltip("Optional fallback exit (other side). Used without animation when the main exit is blocked.")]
        [SerializeField] private Transform alternateExitPoint;
        [Tooltip("Used to rank nearby vehicles. The interaction trigger itself can be any trigger collider under the vehicle.")]
        [SerializeField] private Transform interactionPoint;
        [Tooltip("Driver door, opened before the enter/exit animation and closed after it. Optional.")]
        [SerializeField] private VehicleDoor driverDoor;
        [Tooltip("Cabin floor at the driver door sill (up = floor normal, right = into the cabin). Keeps the driver's feet inside the car. Optional.")]
        [SerializeField] private Transform cabinFloor;

        [Header("Driver Visual")]
        [Tooltip("Static seated character (CarPlayer) shown while driving. Inactive by default.")]
        [SerializeField] private GameObject driverDummy;
        [Tooltip("Optional NPC driver sitting at the wheel (vehicles taken over from traffic). Pulled out when the player gets in.")]
        [SerializeField] private TrafficVehicleDriver npcDriver;

        [Header("Mounting")]
        [Tooltip("Mounted without a door (bikes): the player swings onto the seat and steps off instead of the car door clips.")]
        [SerializeField] private bool quickMount;

        [Header("Rules")]
        [Tooltip("Max speed (m/s) at which the vehicle can be entered.")]
        [SerializeField, Min(0f)] private float maxEnterSpeed = 1f;
        [Tooltip("Max speed (m/s) at which the driver can get out.")]
        [SerializeField, Min(0f)] private float maxExitSpeed = 1f;

        [Header("Rolling On (driver bailed out)")]
        [Tooltip("A vehicle the driver bailed out of rolls on (no throttle, brake or handbrake) until it is slower than this (m/s), then parks.")]
        [SerializeField, Min(0.05f)] private float rollParkSpeed = 0.5f;
        [Tooltip("Light brake (0-1) while rolling on, standing in for engine drag so the vehicle stops in a sensible distance. 0 = free rolling.")]
        [SerializeField, Range(0f, 0.5f)] private float rollBrakeInput = 0.1f;
        [Tooltip("Safety limit (s) for rolling on; the vehicle parks after it in any case.")]
        [SerializeField, Min(1f)] private float rollMaxDuration = 30f;

        private Collider[] vehicleColliders = new Collider[0];
        private RCC_WheelCollider[] wheelColliders = new RCC_WheelCollider[0];
        private AudioSource[] audioSources = new AudioSource[0];
        private Rigidbody body;
        private bool bodyWasKinematic;
        private bool isValid;
        private Coroutine rollRoutine;

        public bool IsOccupied { get; private set; }
        public bool IsControlled { get; private set; }
        public float CurrentSpeed => carController != null ? Mathf.Abs(carController.speed) * KmhToMs : 0f;
        public bool CanEnter => isValid && isActiveAndEnabled && !IsOccupied && CurrentSpeed <= maxEnterSpeed;
        public bool CanExit => IsOccupied && CurrentSpeed <= maxExitSpeed;
        public bool IsReadyForEntry => CurrentSpeed <= maxEnterSpeed;
        public bool UsesQuickMount => quickMount;
        /// <summary>True when all references the enter/exit sequence needs are set (also valid on the prefab asset).</summary>
        public bool HasEntrySetup => carController != null && driverSeat != null && enterPoint != null && exitPoint != null;
        /// <summary>True while parked with RCC simulation, wheels, audio and rigidbody switched off.</summary>
        public bool IsDormant { get; private set; }
        /// <summary>True while the vehicle rolls on without a driver after a bail-out (it parks once it has stopped).</summary>
        public bool IsRolling => rollRoutine != null;
        public VehicleEntryPrompt EntryPrompt => npcDriver != null && npcDriver.HasLivingDriver ? VehicleEntryPrompt.Steal : VehicleEntryPrompt.Enter;

        private bool missingCameraReported;

        private void Awake()
        {
            isValid = HasEntrySetup;
            if (!isValid)
            {
                Debug.LogError($"[{nameof(RCCVehicleAdapter)}] Missing references on '{name}' (RCC_CarControllerV3, DriverSeat, EnterPoint, ExitPoint). Vehicle cannot be entered.", this);
                return;
            }

            vehicleColliders = GetComponentsInChildren<Collider>(true);
            body = GetComponent<Rigidbody>();
            if (driverDummy != null) driverDummy.SetActive(false);
            if (rccCamera != null) rccCamera.gameObject.SetActive(false);
            // Parked until a driver takes control (RCC reads player input while canControl is set).
            carController.SetCanControl(false);
        }

        // After RCC's own Awake (which starts the engine when 'runEngineAtAwake' is set).
        private void Start()
        {
            if (!isValid) return;
            carController.SetCanControl(false);
            if (engineOffWhenParked) carController.KillEngine();
        }

        public Transform GetDriverSeat() => driverSeat;
        public Transform GetEnterPoint() => enterPoint;
        public Transform GetExitPoint() => exitPoint;
        public Transform GetAlternateExitPoint() => alternateExitPoint;
        public Transform GetInteractionPoint() => interactionPoint != null ? interactionPoint : transform;
        public IReadOnlyList<Collider> GetColliders() => vehicleColliders;
        public VehicleDoor GetDriverDoor() => driverDoor;
        public Transform GetCabinFloor() => cabinFloor;

        public TrafficVehicleDriver GetNpcDriver() => npcDriver;
        public Transform GetStealPoint() => npcDriver != null && npcDriver.HasLivingDriver ? npcDriver.CarjackStandPoint : null;

        public void ReserveForEntry() => SetOccupied(true);
        public void CancelEntryReservation() => SetOccupied(false);

        /// <summary>This vehicle is drivable as it is.</summary>
        public IVehicleController CompleteEntry() => this;

        /// <summary>
        /// Puts a parked, unoccupied vehicle to sleep (RCC controller, RCC wheels and its audio off, rigidbody kinematic) or
        /// wakes it up. Used for abandoned vehicles far from the player; a dormant vehicle costs almost nothing per frame.
        /// </summary>
        public void SetDormant(bool dormant)
        {
            // A car still rolling on after a bail-out is never frozen mid-roll.
            if (!isValid || dormant == IsDormant || (dormant && (IsOccupied || IsRolling))) return;
            IsDormant = dormant;

            if (dormant)
            {
                // Collected when first needed: RCC creates its wheel components' and audio children in Awake/Start.
                wheelColliders = GetComponentsInChildren<RCC_WheelCollider>(true);
                audioSources = GetComponentsInChildren<AudioSource>(true);
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] != null) audioSources[i].enabled = false;
                }
            }

            carController.enabled = !dormant;
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                if (wheelColliders[i] != null) wheelColliders[i].enabled = !dormant;
            }
            if (!dormant)
            {
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] != null) audioSources[i].enabled = true;
                }
            }

            if (body == null) return;
            if (dormant)
            {
                bodyWasKinematic = body.isKinematic;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            else
            {
                body.isKinematic = bodyWasKinematic;
                body.WakeUp();
            }
        }

        /// <summary>Assigns the scene RCC camera to a vehicle spawned at runtime (prefabs cannot reference scene objects).</summary>
        public void SetVehicleCamera(RCC_Camera vehicleCamera)
        {
            rccCamera = vehicleCamera;
        }

        public Camera GetActiveCamera()
        {
            if (!IsControlled || rccCamera == null || rccCamera.thisCam == null) return null;
            return rccCamera.thisCam.isActiveAndEnabled ? rccCamera.thisCam : null;
        }

        public void SetOccupied(bool occupied)
        {
            if (occupied) SetDormant(false);
            IsOccupied = occupied;
        }

        public void EnableControl()
        {
            if (!isValid) return;
            StopRolling();
            IsControlled = true;

            // A disabled RCC_CarControllerV3 reads no input and drives no wheels, so the car could never be driven.
            if (!carController.enabled)
            {
                Debug.LogWarning($"[{nameof(RCCVehicleAdapter)}] RCC_CarControllerV3 on '{name}' was disabled; enabling it so the vehicle can be driven.", this);
                carController.enabled = true;
            }

            // A vehicle placed or spawned without a camera reference (prefabs cannot point at scene objects) uses the scene's
            // RCC camera, which starts switched off; without this the screen goes black once seated.
            if (rccCamera == null) rccCamera = FindFirstObjectByType<RCC_Camera>(FindObjectsInactive.Include);

            // Enabling the RCC camera raises RCC_Camera.OnBCGCameraSpawned, so RCC_SceneManager picks it up
            // as its active camera and RegisterPlayerVehicle targets it at this car.
            if (rccCamera != null)
            {
                rccCamera.gameObject.SetActive(true);
                rccCamera.ToggleCamera(true);
            }
            else if (!missingCameraReported)
            {
                missingCameraReported = true;
                Debug.LogWarning($"[{nameof(RCCVehicleAdapter)}] No RCC_Camera assigned on '{name}'; the player camera will stay off while driving.", this);
            }

            RCC.RegisterPlayerVehicle(carController, true, startEngineOnEnter || carController.engineRunning);

            if (rccCamera != null && RCC_SceneManager.Instance.activePlayerCamera != rccCamera)
            {
                rccCamera.SetTarget(carController.gameObject);
            }
        }

        public void DisableControl()
        {
            if (!isValid) return;
            IsControlled = false;

            // With canControl off RCC zeroes throttle/steer and holds the handbrake, so the car stops and stays parked.
            if (RCC_SceneManager.Instance.activePlayerVehicle == carController) RCC.DeRegisterPlayerVehicle();
            else carController.SetCanControl(false);

            if (engineOffWhenParked) carController.KillEngine();

            if (rccCamera != null)
            {
                rccCamera.RemoveTarget();
                rccCamera.ToggleCamera(false);
                rccCamera.gameObject.SetActive(false);
            }
        }

        public void ReleaseControlRolling()
        {
            if (!isValid) return;
            DisableControl();
            StopRolling();
            // RCC holds the handbrake while nobody controls the car. Marked as externally controlled with no throttle, steering or
            // handbrake it coasts instead (RCC's own recorder uses the same switch), slowed only by a light brake standing in for
            // engine drag; the handbrake comes back once it has stopped.
            carController.externalController = true;
            carController.throttleInput = 0f;
            carController.brakeInput = rollBrakeInput;
            carController.steerInput = 0f;
            carController.handbrakeInput = 0f;
            carController.boostInput = 0f;
            rollRoutine = StartCoroutine(RollOnRoutine());
        }

        // Low-frequency check (not per frame) while the car rolls to a stop.
        private IEnumerator RollOnRoutine()
        {
            const float interval = 0.25f;
            var wait = new WaitForSeconds(interval);
            float timer = 0f;
            while (timer < rollMaxDuration && CurrentSpeed > rollParkSpeed)
            {
                yield return wait;
                timer += interval;
            }
            rollRoutine = null;
            carController.externalController = false;
        }

        private void StopRolling()
        {
            if (rollRoutine == null) return;
            StopCoroutine(rollRoutine);
            rollRoutine = null;
            if (carController != null) carController.externalController = false;
        }

        private void OnDisable()
        {
            StopRolling();
        }

        public void SetDriverVisualActive(bool active)
        {
            if (driverDummy != null) driverDummy.SetActive(active);
        }

        private void OnDrawGizmosSelected()
        {
            DrawPoint(driverSeat, Color.green);
            DrawPoint(enterPoint, Color.cyan);
            DrawPoint(exitPoint, Color.yellow);
            DrawPoint(alternateExitPoint, new Color(1f, 0.5f, 0f));
            DrawPoint(cabinFloor, Color.magenta);
        }

        private static void DrawPoint(Transform point, Color color)
        {
            if (point == null) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(point.position, 0.15f);
            Gizmos.DrawLine(point.position, point.position + point.forward * 0.6f);
        }
    }
}
