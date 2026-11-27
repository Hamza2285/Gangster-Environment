using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using OpenworldCrime.Environment;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// A flyable helicopter (one per helicopter, not a Singleton). Plugs into the player's normal enter / exit flow as an
    /// <see cref="IVehicleController"/> (PlayerVehicleInteraction owns the get-in / get-out; quick mount through the cabin door
    /// with a camera cut, see <see cref="HelicopterBoarding"/>), so nothing in RCC or the player side is specific to it.
    /// Flight model (arcade, mobile friendly): the rotor spools up when a pilot takes control and down when he leaves; at speed
    /// the rotor carries the weight, so with no input it holds a steady hover and levels itself. Collective climbs / descends,
    /// the stick pitches forward / back (flies forward / back), yaw turns, strafe slides sideways; the body tilts with the
    /// motion. On the ground it rests on its skids. Crashes are handled by <see cref="VehicleHealth"/> on the same object
    /// (crash damage, smoke, fire, explosion); a wreck loses its rotor and falls. Getting out only when landed or hovering
    /// low; in the air the pilot jumps out instead (<see cref="IVehicleExitRule.AllowsAirJump"/>: parachute pose or ragdoll fall).
    /// Input: keyboard W/S, A/D, Q/E, Space (up), Left Ctrl (down); touch: the helicopter stick and UP / DOWN buttons
    /// (HelicopterHudView), read through Invector's GenericInput like every other control.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class HelicopterController : MonoBehaviour, IVehicleController, IVehicleExitRule, IFlyingVehicle
    {
        [Header("Parts")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform mainRotor;
        [Tooltip("Main rotor spin axis, in the rotor's local space.")]
        [SerializeField] private Vector3 mainRotorAxis = Vector3.forward;
        [SerializeField] private Transform tailRotor;
        [SerializeField] private Vector3 tailRotorAxis = Vector3.right;
        [Tooltip("Optional: health / crash damage / wreck (shared VehicleDamageSettings).")]
        [SerializeField] private VehicleHealth health;

        [Header("Seat & Points")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform enterPoint;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private Transform alternateExitPoint;
        [SerializeField] private Transform interactionPoint;
        [Tooltip("Static seated pilot shown while flying. Inactive by default.")]
        [SerializeField] private GameObject driverDummy;

        [Header("Camera")]
        [SerializeField] private HelicopterCamera chaseCamera;

        [Header("Input")]
        [SerializeField] private GenericInput pitchInput = new GenericInput("Vertical", "LeftAnalogVertical", "HeliStickY");
        [SerializeField] private GenericInput yawInput = new GenericInput("Horizontal", "LeftAnalogHorizontal", "HeliStickX");
        [SerializeField] private GenericInput upInput = new GenericInput("Space", "A", "M_HeliUp");
        [SerializeField] private GenericInput downInput = new GenericInput("LeftControl", "B", "M_HeliDown");
        [SerializeField] private GenericInput strafeLeftInput = new GenericInput("Q", "LB", "");
        [SerializeField] private GenericInput strafeRightInput = new GenericInput("E", "RB", "");

        [Header("Rotor")]
        [Tooltip("Seconds from standstill to full rotor speed (lift-off possible from about 85%).")]
        [SerializeField, Min(0.1f)] private float spoolUpTime = 3.5f;
        [SerializeField, Min(0.1f)] private float spoolDownTime = 6f;
        [Tooltip("Main rotor speed (deg/s) at full spool.")]
        [SerializeField] private float mainRotorSpeed = 2600f;
        [SerializeField] private float tailRotorSpeed = 4200f;
        [Tooltip("Largest turn per rendered frame (deg). Kept under half the angle between two blades, so at any frame rate the " +
                 "blades read as turning forward fast instead of strobing still / backwards (wagon-wheel effect).")]
        [SerializeField, Range(5f, 170f)] private float maxMainRotorStep = 38f;
        [SerializeField, Range(5f, 170f)] private float maxTailRotorStep = 40f;

        [Header("Flight")]
        [SerializeField, Min(0.5f)] private float climbSpeed = 9f;
        [SerializeField, Min(0.5f)] private float descendSpeed = 7f;
        [Tooltip("How fast the vertical speed follows the collective (1/s).")]
        [SerializeField, Min(0.1f)] private float verticalResponse = 2.2f;
        [SerializeField, Min(1f)] private float maxForwardSpeed = 42f;
        [SerializeField, Min(1f)] private float maxBackwardSpeed = 10f;
        [SerializeField, Min(1f)] private float maxStrafeSpeed = 14f;
        [Tooltip("How fast the horizontal speed follows the stick (1/s).")]
        [SerializeField, Min(0.05f)] private float horizontalResponse = 0.9f;
        [Tooltip("Turn rate (deg/s) at full yaw input.")]
        [SerializeField, Min(1f)] private float yawRate = 75f;
        [SerializeField, Min(0.1f)] private float yawResponse = 3f;
        [Tooltip("Nose-down tilt (deg) at full forward speed, roll (deg) at full strafe / turn.")]
        [SerializeField, Range(0f, 40f)] private float maxPitch = 16f;
        [SerializeField, Range(0f, 40f)] private float maxRoll = 18f;
        [Tooltip("How fast the body tilts / levels (1/s).")]
        [SerializeField, Min(0.1f)] private float tiltResponse = 2.5f;
        [Tooltip("Highest flight (m, world).")]
        [SerializeField] private float ceiling = 360f;
        [Tooltip("Optional: the map's limit. Beyond it the helicopter is pushed back towards the map (and down above its ceiling).")]
        [SerializeField] private WorldBoundary boundary;
        [Tooltip("Speed (m/s) the helicopter is pushed back with at full turn-back strength.")]
        [SerializeField, Min(0f)] private float boundaryPushSpeed = 25f;

        [Header("Ground")]
        [Tooltip("Ground counts as under the skids within this distance (m) of the body's lowest point.")]
        [SerializeField, Min(0.05f)] private float groundedDistance = 0.4f;
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Getting In / Out")]
        [SerializeField, Min(0f)] private float maxEnterSpeed = 1.5f;
        [Tooltip("The pilot may get out on the ground below this speed (m/s) ...")]
        [SerializeField, Min(0f)] private float maxExitSpeed = 3f;
        [Tooltip("... or hovering at most this high (m) above the ground (drops out).")]
        [SerializeField, Min(0f)] private float maxExitHeight = 3.5f;

        [Header("Audio")]
        [SerializeField] private AudioSource rotorAudio;
        [SerializeField, Range(0f, 1f)] private float maxVolume = 0.9f;
        [Tooltip("Rotor loop pitch at the start of the spool-up and at full rotor speed.")]
        [SerializeField] private float idlePitch = 0.6f;
        [SerializeField] private float fullPitch = 1f;
        [Tooltip("Extra pitch / volume share under load (climbing, descending hard, flying fast).")]
        [SerializeField, Range(0f, 0.3f)] private float loadPitch = 0.08f;
        [SerializeField, Range(0f, 0.5f)] private float loadVolume = 0.12f;

        /// <summary>The player took (true) or gave up (false) the controls of a helicopter.</summary>
        public static event Action<HelicopterController, bool> OnPlayerPilotingChanged;

        public bool IsOccupied { get; private set; }
        public bool IsControlled { get; private set; }
        public float CurrentSpeed => body != null ? body.linearVelocity.magnitude : 0f;
        public bool CanExit => IsOccupied && ((IsGrounded && CurrentSpeed <= maxExitSpeed) || (HeightAboveGround <= maxExitHeight && CurrentSpeed <= maxExitSpeed + 1f));
        public bool UsesQuickMount => true;
        public bool AllowsBailOut => false;
        public bool AllowsAirJump => true;
        public bool CanEnter => isValid && isActiveAndEnabled && !IsOccupied && !IsWrecked && CurrentSpeed <= maxEnterSpeed;
        public bool IsWrecked => health != null && health.IsDestroyed;
        public bool IsReadyForEntry => CurrentSpeed <= maxEnterSpeed;
        public VehicleEntryPrompt EntryPrompt => VehicleEntryPrompt.Enter;
        public bool IsGrounded { get; private set; }
        public float HeightAboveGround { get; private set; }
        /// <summary>0 = rotor stopped, 1 = full speed.</summary>
        public float RotorSpeed01 { get; private set; }

        private bool isValid;
        private bool externalInput;
        private float externalStick, externalTurn, externalCollective, externalStrafe;
        private Collider[] colliders = new Collider[0];
        private Transform groundExit;
        private float yaw, pitch, roll;
        private float yawSpeed;
        private float lowestPointOffset;
        private float load, smoothedLoad;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private void Reset() => body = GetComponent<Rigidbody>();

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            isValid = body != null && driverSeat != null && enterPoint != null && exitPoint != null;
            if (!isValid)
            {
                Debug.LogError($"[{nameof(HelicopterController)}] Missing references on '{name}' (Rigidbody, DriverSeat, EnterPoint, ExitPoint).", this);
                return;
            }
            colliders = GetComponentsInChildren<Collider>(true);
            if (driverDummy != null) driverDummy.SetActive(false);
            if (chaseCamera != null) chaseCamera.gameObject.SetActive(false);
            groundExit = new GameObject("Ground Exit").transform;
            groundExit.SetParent(transform, false);
            yaw = transform.eulerAngles.y;
            // Lowest collider point under the pivot (the skids), for the height above ground.
            float lowest = 0f;
            foreach (Collider c in colliders)
            {
                if (c.isTrigger) continue;
                lowest = Mathf.Min(lowest, c.bounds.min.y - transform.position.y);
            }
            lowestPointOffset = lowest;
            if (rotorAudio != null)
            {
                rotorAudio.loop = true;
                rotorAudio.volume = 0f;
            }
            enabled = true;
        }

        private void OnEnable()
        {
            if (health != null) health.OnDestroyedWreck += HandleWrecked;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDestroyedWreck -= HandleWrecked;
        }

        private void HandleWrecked()
        {
            // Rotor stops driving; the body falls and tumbles freely (the player side ejects the pilot).
            body.useGravity = true;
        }

        // ------------------------------------------------------------------ IVehicleController

        public Transform GetDriverSeat() => driverSeat;
        public Transform GetEnterPoint() => enterPoint;
        public Transform GetInteractionPoint() => interactionPoint != null ? interactionPoint : transform;
        public Transform GetStealPoint() => null;
        public IReadOnlyList<Collider> GetColliders() => colliders;
        public VehicleDoor GetDriverDoor() => null;
        public Transform GetCabinFloor() => null;
        public TrafficVehicleDriver GetNpcDriver() => null;
        public Transform GetAlternateExitPoint() => alternateExitPoint;

        /// <summary>The exit point dropped onto the ground under it (hovering low the pilot drops out onto the ground).</summary>
        public Transform GetExitPoint()
        {
            if (exitPoint == null || groundExit == null) return exitPoint;
            Vector3 p = exitPoint.position;
            groundExit.rotation = exitPoint.rotation;
            groundExit.position = Physics.Raycast(p + Vector3.up, Vector3.down, out RaycastHit hit, maxExitHeight + 6f, groundLayers, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * 0.05f : p;
            return groundExit;
        }

        public Camera GetActiveCamera() => IsControlled && chaseCamera != null && chaseCamera.isActiveAndEnabled ? chaseCamera.Camera : null;

        public void ReserveForEntry() => SetOccupied(true);
        public void CancelEntryReservation() => SetOccupied(false);
        public IVehicleController CompleteEntry() => this;
        public void SetOccupied(bool occupied) => IsOccupied = occupied;

        public void EnableControl()
        {
            if (!isValid) return;
            IsControlled = true;
            yaw = transform.eulerAngles.y;
            body.isKinematic = false;
            body.WakeUp();
            if (chaseCamera != null)
            {
                // Already on (boarding side view): swing round behind from there; otherwise start right behind.
                if (chaseCamera.gameObject.activeSelf) chaseCamera.BlendBehind(transform);
                else
                {
                    chaseCamera.gameObject.SetActive(true);
                    chaseCamera.SnapBehind(transform);
                }
            }
            OnPlayerPilotingChanged?.Invoke(this, true);
        }

        public void DisableControl()
        {
            if (!isValid) return;
            bool was = IsControlled;
            IsControlled = false;
            if (chaseCamera != null) chaseCamera.gameObject.SetActive(false);
            if (was) OnPlayerPilotingChanged?.Invoke(this, false);
        }

        public void ReleaseControlRolling() => DisableControl();

        public void SetDriverVisualActive(bool active)
        {
            if (driverDummy != null) driverDummy.SetActive(active);
        }

        /// <summary>
        /// Flies on given inputs instead of the player's controls (scripted flights: missions, cutscenes, tests), like RCC's
        /// external controller. Values -1..1: stick (forward +), turn (right +), collective (up +), strafe (right +).
        /// </summary>
        public void SetExternalInput(bool active, float stick = 0f, float turn = 0f, float collective = 0f, float strafe = 0f)
        {
            externalInput = active;
            externalStick = Mathf.Clamp(stick, -1f, 1f);
            externalTurn = Mathf.Clamp(turn, -1f, 1f);
            externalCollective = Mathf.Clamp(collective, -1f, 1f);
            externalStrafe = Mathf.Clamp(strafe, -1f, 1f);
        }

        // ------------------------------------------------------------------ flight

        private void Update()
        {
            float target = IsControlled && !IsWrecked ? 1f : 0f;
            RotorSpeed01 = Mathf.MoveTowards(RotorSpeed01, target, Time.deltaTime / (target > RotorSpeed01 ? spoolUpTime : spoolDownTime));
            // Spin eases in with the spool (squared: slow first turns, then fast), capped per frame against strobing.
            float spin = RotorSpeed01 * RotorSpeed01 * Time.deltaTime;
            if (mainRotor != null && spin > 0f) mainRotor.Rotate(mainRotorAxis, Mathf.Min(mainRotorSpeed * spin, maxMainRotorStep), Space.Self);
            if (tailRotor != null && spin > 0f) tailRotor.Rotate(tailRotorAxis, Mathf.Min(tailRotorSpeed * spin, maxTailRotorStep), Space.Self);
            if (rotorAudio != null)
            {
                if (RotorSpeed01 > 0.01f && !rotorAudio.isPlaying) rotorAudio.Play();
                else if (RotorSpeed01 <= 0.01f && rotorAudio.isPlaying) rotorAudio.Stop();
                smoothedLoad = Mathf.MoveTowards(smoothedLoad, load, Time.deltaTime * 1.5f);
                float spool = Mathf.SmoothStep(0f, 1f, RotorSpeed01);
                rotorAudio.volume = maxVolume * spool * (1f - loadVolume + loadVolume * smoothedLoad);
                rotorAudio.pitch = Mathf.Lerp(idlePitch, fullPitch, RotorSpeed01) + loadPitch * smoothedLoad * spool;
            }
        }

        private void FixedUpdate()
        {
            if (!isValid || body.isKinematic) return;
            MeasureGround();
            if (IsWrecked || RotorSpeed01 <= 0.01f)
            {
                body.useGravity = true;
                return;
            }

            float dt = Time.fixedDeltaTime;
            bool menu = GamePlayer.MenuOpen;
            float collective = 0f, stick = 0f, turn = 0f, strafe = 0f;
            if (IsControlled && externalInput)
            {
                stick = externalStick;
                turn = externalTurn;
                collective = externalCollective;
                strafe = externalStrafe;
            }
            else if (IsControlled && !menu)
            {
                stick = Mathf.Clamp(pitchInput.GetAxis(), -1f, 1f);
                turn = Mathf.Clamp(yawInput.GetAxis(), -1f, 1f);
                collective = (upInput.GetButton() ? 1f : 0f) - (downInput.GetButton() ? 1f : 0f);
                strafe = (strafeRightInput.GetButton() ? 1f : 0f) - (strafeLeftInput.GetButton() ? 1f : 0f);
            }
            else if (!IsControlled) collective = -0.5f; // left with the rotor still turning: it settles down
            // Rotor load for the sound: collective (climbing loads it most) and forward speed.
            load = Mathf.Clamp01(Mathf.Max(collective, -collective * 0.5f) + Mathf.Abs(stick) * 0.35f
                + new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude / maxForwardSpeed * 0.5f);

            // Lift: the rotor carries the weight from ~85% spool; below that the helicopter stays down.
            float lift = Mathf.InverseLerp(0.6f, 0.9f, RotorSpeed01);
            body.useGravity = false;
            Vector3 velocity = body.linearVelocity;

            // Vertical: a speed set by the collective (0 = hover), gravity where the rotor is not up to speed yet.
            float targetVy = collective >= 0f ? collective * climbSpeed : collective * descendSpeed;
            if (transform.position.y > ceiling) targetVy = Mathf.Min(targetVy, 0f);
            float vy = Mathf.Lerp(velocity.y + Physics.gravity.y * dt, Mathf.Lerp(velocity.y, targetVy, verticalResponse * dt), lift);
            if (IsGrounded && collective <= 0f && vy < 0f) vy = Mathf.Max(vy, Physics.gravity.y * dt * 4f); // sits down on the skids

            // Horizontal (only off the ground): forward / back and strafe relative to the heading.
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            bool airborne = !IsGrounded || collective > 0f;
            Vector3 targetFlat = Vector3.zero;
            if (airborne && lift > 0.99f)
            {
                float forward = stick >= 0f ? stick * maxForwardSpeed : stick * maxBackwardSpeed;
                targetFlat = heading * new Vector3(strafe * maxStrafeSpeed, 0f, forward);
            }
            // Map limit: the outward part of the wanted motion fades out and a push back towards the map fades in.
            if (airborne && boundary != null && boundary.Evaluate(body.position, out Vector3 inward, out float outside01, out float above01))
            {
                float push = Mathf.SmoothStep(0f, 1f, outside01);
                float outward = -Vector3.Dot(targetFlat, inward);
                if (outward > 0f) targetFlat += inward * (outward * push);
                targetFlat += inward * (boundaryPushSpeed * push);
                if (above01 > 0f) vy = Mathf.Min(vy, Mathf.Lerp(vy, -descendSpeed, Mathf.SmoothStep(0f, 1f, above01)));
            }
            float response = airborne ? horizontalResponse : 4f; // on the ground it simply stops
            flat = Vector3.Lerp(flat, targetFlat, 1f - Mathf.Exp(-response * dt));
            body.linearVelocity = new Vector3(flat.x, vy, flat.z);

            // Rotation: yaw by input (also on the ground with the rotor up, like a real one), tilt with the motion; levels itself.
            float targetYawSpeed = (airborne || lift > 0.99f) ? turn * yawRate : 0f;
            yawSpeed = Mathf.Lerp(yawSpeed, targetYawSpeed, 1f - Mathf.Exp(-yawResponse * dt));
            yaw += yawSpeed * dt;
            Vector3 localFlat = Quaternion.Inverse(heading) * flat;
            float targetPitch = airborne ? Mathf.Clamp(localFlat.z / maxForwardSpeed, -0.5f, 1f) * maxPitch + stick * maxPitch * 0.35f : 0f;
            float targetRoll = airborne ? -(Mathf.Clamp(localFlat.x / maxStrafeSpeed, -1f, 1f) * 0.7f + yawSpeed / yawRate * 0.5f) * maxRoll : 0f;
            float k = 1f - Mathf.Exp(-tiltResponse * dt);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            roll = Mathf.Lerp(roll, targetRoll, k);
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(Quaternion.Euler(pitch, yaw, roll));
        }

        // One ray from the pivot: height of the skids above the ground.
        private void MeasureGround()
        {
            Vector3 origin = body.position + Vector3.up * 0.5f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 200f, groundLayers, QueryTriggerInteraction.Ignore);
            float nearest = 200f;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.IsChildOf(transform)) continue;
                nearest = Mathf.Min(nearest, Hits[i].distance - 0.5f);
            }
            HeightAboveGround = Mathf.Max(0f, nearest + lowestPointOffset);
            IsGrounded = HeightAboveGround <= groundedDistance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnPlayerPilotingChanged = null;

#if UNITY_EDITOR
        public void EditorSetBoundary(WorldBoundary worldBoundary) => boundary = worldBoundary;

        public void EditorSetup(Rigidbody rigidbody, Transform rotor, Vector3 rotorAxis, Transform tail, Vector3 tailAxis, VehicleHealth vehicleHealth,
            Transform seat, Transform enter, Transform exit, Transform alternateExit, Transform interaction, GameObject dummy,
            HelicopterCamera cameraRig, AudioSource audio)
        {
            body = rigidbody;
            mainRotor = rotor;
            mainRotorAxis = rotorAxis;
            tailRotor = tail;
            tailRotorAxis = tailAxis;
            health = vehicleHealth;
            driverSeat = seat;
            enterPoint = enter;
            exitPoint = exit;
            alternateExitPoint = alternateExit;
            interactionPoint = interaction;
            driverDummy = dummy;
            chaseCamera = cameraRig;
            rotorAudio = audio;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            if (driverSeat != null) Gizmos.DrawWireSphere(driverSeat.position, 0.2f);
            Gizmos.color = Color.cyan;
            if (enterPoint != null) Gizmos.DrawWireSphere(enterPoint.position, 0.2f);
            Gizmos.color = Color.yellow;
            if (exitPoint != null) Gizmos.DrawWireSphere(exitPoint.position, 0.2f);
        }
#endif
    }
}
