using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using OpenworldCrime.Environment;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// A flyable jet (one per plane, not a Singleton). Plugs into the player's normal enter / exit flow as an
    /// <see cref="IVehicleController"/> like the helicopter: quick mount through the airstair door with a camera cut
    /// (<see cref="HelicopterBoarding"/>, the shared door + boarding-view component), its own chase camera
    /// (<see cref="HelicopterCamera"/> with pitch follow) and <see cref="VehicleHealth"/> for damage / explosion.
    /// Arcade flight model (mobile friendly; the velocity is set each physics step, no aerodynamic forces):
    /// - Ground: throttle accelerates down the runway, roll / rudder input steers the nose wheel (tight when slow), throttle down
    ///   at idle brakes. Above Rotate Speed pulling up lifts the nose; above Takeoff Speed with the nose up it flies.
    /// - Air: roll input banks (the bank turns the plane), pitch input climbs / dives, rudder yaws a little. Without input the
    ///   bank and pitch level themselves. Climbing costs speed, diving gains it. Below Stall Speed the wings lose lift: the
    ///   plane sinks and the nose drops until it has speed again.
    /// - Landing: the wheels touching down gently (sink rate, bank and pitch within limits) puts it back on the runway; anything
    ///   harder, a hit at speed, or the water destroys it (VehicleHealth explosion; the player side ejects the pilot).
    /// Getting out only when stopped on the ground; in the air the pilot jumps out (parachute with a bag, ragdoll without).
    /// Input (Invector GenericInput, like every other control): keyboard W / S throttle, A / D roll (nose wheel on the ground),
    /// Up / Down arrows pitch, Q / E rudder; touch: the plane stick (X roll, Y pitch) and THR + / THR - buttons. Invert Pitch
    /// (default on, flight-sim style): Up arrow / stick up = nose down, Down arrow / stick pulled down = nose up.
    /// Feel: every input is eased (SmoothDamp), the pitch rate follows its target with a response time and eases off near the
    /// pitch limits and the ceiling, the bank is SmoothDamped - nothing snaps when a key is pressed or released.
    /// Beyond the map's soft limit (<see cref="WorldBoundary"/>) the plane is banked and turned back, and pushed down above it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class PlaneController : MonoBehaviour, IVehicleController, IVehicleExitRule, IFlyingVehicle
    {
        [Header("Parts")]
        [SerializeField] private Rigidbody body;
        [Tooltip("Optional: health / crash damage / explosion (shared VehicleDamageSettings).")]
        [SerializeField] private VehicleHealth health;
        [Tooltip("Optional: sea surface (touching the water is a crash).")]
        [SerializeField] private WaterManager water;
        [Tooltip("Optional: the map's limit (turned back beyond it, pushed down above its ceiling).")]
        [SerializeField] private WorldBoundary boundary;

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
        [SerializeField] private GenericInput throttleUpInput = new GenericInput("W", "RB", "M_PlaneThrottleUp");
        [SerializeField] private GenericInput throttleDownInput = new GenericInput("S", "LB", "M_PlaneThrottleDown");
        [SerializeField] private GenericInput rollInput = new GenericInput("Horizontal", "LeftAnalogHorizontal", "PlaneStickX");
        [SerializeField] private GenericInput pitchInput = new GenericInput("", "LeftAnalogVertical", "PlaneStickY");
        [SerializeField] private GenericInput noseUpKey = new GenericInput("UpArrow", "", "");
        [SerializeField] private GenericInput noseDownKey = new GenericInput("DownArrow", "", "");
        [SerializeField] private GenericInput rudderLeftInput = new GenericInput("Q", "", "");
        [SerializeField] private GenericInput rudderRightInput = new GenericInput("E", "", "");
        [Tooltip("On (default, flight-sim style): Up arrow / stick up = nose down, Down arrow / stick pulled down = nose up. " +
                 "Off: up = nose up (arcade).")]
        [SerializeField] private bool invertPitch = true;

        [Header("Engine")]
        [Tooltip("Throttle change per second while a throttle key / button is held.")]
        [SerializeField, Min(0.05f)] private float throttleRate = 0.6f;
        [Tooltip("Acceleration (m/s²) at full throttle (before drag).")]
        [SerializeField, Min(0.5f)] private float thrust = 9f;
        [Tooltip("Top speed (m/s) in level flight at full throttle; sets the air drag.")]
        [SerializeField, Min(10f)] private float maxSpeed = 85f;
        [Tooltip("Seconds the engines take to wind up / down.")]
        [SerializeField, Min(0.1f)] private float spoolTime = 2.5f;

        [Header("Ground")]
        [SerializeField, Min(1f)] private float rotateSpeed = 36f;
        [SerializeField, Min(1f)] private float takeoffSpeed = 42f;
        [Tooltip("Largest nose-up (deg) on the main wheels before lifting off.")]
        [SerializeField, Range(2f, 20f)] private float maxRotatePitch = 10f;
        [Tooltip("Nose-wheel turn rate (deg/s) when slow, and at Rotate Speed.")]
        [SerializeField, Min(1f)] private float taxiTurnRate = 32f;
        [SerializeField, Min(0f)] private float fastTurnRate = 5f;
        [SerializeField, Min(0f)] private float rollingResistance = 0.4f;
        [SerializeField, Min(0f)] private float brakeDeceleration = 7f;
        [Tooltip("Ground, runway and obstacles the wheels stand on.")]
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Flight")]
        [SerializeField, Min(1f)] private float stallSpeed = 32f;
        [Tooltip("Bank (deg) at full roll input.")]
        [SerializeField, Range(10f, 80f)] private float maxBank = 60f;
        [Tooltip("Bank change (deg/s).")]
        [SerializeField, Min(5f)] private float rollRate = 90f;
        [Tooltip("Nose up / down rate (deg/s) at full pitch input.")]
        [SerializeField, Min(5f)] private float pitchRate = 32f;
        [Tooltip("Largest climb / dive angle (deg).")]
        [SerializeField, Range(10f, 80f)] private float maxPitch = 45f;
        [Tooltip("Rate (deg/s) the nose levels itself without pitch input.")]
        [SerializeField, Min(0f)] private float pitchLevelRate = 15f;
        [Tooltip("Turn rate multiplier on the bank (arcade: >1 turns tighter than real).")]
        [SerializeField, Min(0.1f)] private float turnBoost = 1.7f;
        [Tooltip("Rudder yaw (deg/s) at full input.")]
        [SerializeField, Min(0f)] private float rudderRate = 12f;
        [Tooltip("Highest flight (m, world). The nose is eased down over the last 60 m.")]
        [SerializeField] private float ceiling = 700f;

        [Header("Control Feel")]
        [Tooltip("Seconds the pitch / roll / rudder inputs take to ease in and out (keys are on / off; this ramps them).")]
        [SerializeField, Range(0.01f, 0.6f)] private float inputSmoothTime = 0.16f;
        [Tooltip("How quickly (1/s) the pitch rate follows its target (lower = softer, heavier nose).")]
        [SerializeField, Min(0.5f)] private float pitchResponse = 3.5f;
        [Tooltip("Within this many degrees of Max Pitch the pitch rate eases to zero (no hard stop at the limit).")]
        [SerializeField, Range(1f, 30f)] private float pitchSoftLimit = 12f;
        [Tooltip("Seconds the bank takes to follow the roll input (SmoothDamp, capped by Roll Rate).")]
        [SerializeField, Range(0.05f, 1.5f)] private float bankSmoothTime = 0.35f;

        [Header("Map Limit")]
        [Tooltip("Extra turn (deg/s) towards the map at full turn-back strength, on top of the forced bank.")]
        [SerializeField, Min(0f)] private float boundaryTurnRate = 35f;

        [Header("Landing & Crashing")]
        [Tooltip("Largest sink rate (m/s) for a safe touchdown.")]
        [SerializeField, Min(0.5f)] private float maxLandingSink = 7f;
        [SerializeField, Range(1f, 60f)] private float maxLandingBank = 20f;
        [Tooltip("Speed (m/s) into an obstacle (along the contact normal) that destroys the plane.")]
        [SerializeField, Min(1f)] private float crashImpactSpeed = 14f;

        [Header("Getting In / Out")]
        [SerializeField, Min(0f)] private float maxEnterSpeed = 1f;
        [SerializeField, Min(0f)] private float maxExitSpeed = 2f;

        [Header("Audio")]
        [Tooltip("Engine loop (planeloop).")]
        [SerializeField] private AudioSource engineAudio;
        [SerializeField, Range(0f, 1f)] private float idleVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] private float fullVolume = 0.95f;
        [SerializeField] private float idlePitch = 0.7f;
        [SerializeField] private float fullPitch = 1.15f;
        [Tooltip("Extra pitch at top speed.")]
        [SerializeField, Range(0f, 0.5f)] private float speedPitch = 0.15f;

        /// <summary>The player took (true) or gave up (false) the controls of a plane.</summary>
        public static event Action<PlaneController, bool> OnPlayerPilotingChanged;

        public bool IsOccupied { get; private set; }
        public bool IsControlled { get; private set; }
        public float CurrentSpeed => body != null ? body.linearVelocity.magnitude : 0f;
        public bool CanExit => IsOccupied && IsGrounded && CurrentSpeed <= maxExitSpeed;
        public bool UsesQuickMount => true;
        public bool AllowsBailOut => false;
        /// <summary>Only in the air: on the runway at speed the exit waits (no bail-out).</summary>
        public bool AllowsAirJump => !IsGrounded;
        public bool CanEnter => isValid && isActiveAndEnabled && !IsOccupied && !IsWrecked && IsGrounded && CurrentSpeed <= maxEnterSpeed;
        public bool IsWrecked => health != null && health.IsDestroyed;
        public bool IsReadyForEntry => CurrentSpeed <= maxEnterSpeed;
        public VehicleEntryPrompt EntryPrompt => VehicleEntryPrompt.Enter;
        public bool IsGrounded { get; private set; } = true;
        public float HeightAboveGround { get; private set; }
        /// <summary>Throttle 0..1 (HUD).</summary>
        public float Throttle01 { get; private set; }
        /// <summary>True while stalled (below stall speed in the air).</summary>
        public bool IsStalled { get; private set; }
        /// <summary>Current nose up / down rate (deg/s, nose up +). Diagnostics / tests.</summary>
        public float PitchRate => pitchVelocity;
        /// <summary>Current nose angle (deg, nose up +) and bank (deg, right wing down +).</summary>
        public float Pitch => pitch;
        public float Bank => bank;

        private bool isValid;
        private bool externalInput;
        private float externalThrottle, externalRoll, externalPitch, externalRudder;
        private Collider[] colliders = new Collider[0];
        private Transform groundExit;
        private float speed;          // along the nose
        private float sink;           // extra vertical speed lost to the stall
        private float yaw, pitch, bank; // pitch: nose up +, bank: right wing down +
        private float engine01;
        private float pitchCmd, rollCmd, rudderCmd;            // eased inputs
        private float pitchCmdVel, rollCmdVel, rudderCmdVel;   // SmoothDamp state
        private float pitchVelocity;                            // deg/s, nose up +
        private float bankVelocity;                             // SmoothDamp state of the bank
        private Vector3 lastVelocity;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool crashed;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private void Reset() => body = GetComponent<Rigidbody>();

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            isValid = body != null && driverSeat != null && enterPoint != null && exitPoint != null;
            if (!isValid)
            {
                Debug.LogError($"[{nameof(PlaneController)}] Missing references on '{name}' (Rigidbody, DriverSeat, EnterPoint, ExitPoint).", this);
                enabled = false;
                return;
            }
            colliders = GetComponentsInChildren<Collider>(true);
            if (driverDummy != null) driverDummy.SetActive(false);
            if (chaseCamera != null) chaseCamera.gameObject.SetActive(false);
            groundExit = new GameObject("Ground Exit").transform;
            groundExit.SetParent(transform, false);
            homePosition = transform.position;
            homeRotation = transform.rotation;
            if (engineAudio != null)
            {
                engineAudio.loop = true;
                engineAudio.volume = 0f;
            }
        }

        private void OnEnable()
        {
            if (!isValid) return;
            if (health != null)
            {
                health.OnDestroyedWreck += HandleWrecked;
                health.OnWreckCleanup += HandleWreckCleanup;
            }
            ResetFlightState();
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDestroyedWreck -= HandleWrecked;
                health.OnWreckCleanup -= HandleWreckCleanup;
            }
            if (engineAudio != null) engineAudio.Stop();
        }

        private void ResetFlightState()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = 0f;
            bank = 0f;
            speed = 0f;
            sink = 0f;
            ResetControlFeel();
            Throttle01 = 0f;
            engine01 = 0f;
            crashed = false;
            IsStalled = false;
            IsGrounded = true;
            body.useGravity = false;
            body.isKinematic = true; // parked: nothing to simulate until a pilot takes it
        }

        private void ResetControlFeel()
        {
            pitchCmd = rollCmd = rudderCmd = 0f;
            pitchCmdVel = rollCmdVel = rudderCmdVel = 0f;
            pitchVelocity = 0f;
            bankVelocity = 0f;
        }

        private void HandleWrecked()
        {
            crashed = true;
            body.isKinematic = false;
            body.useGravity = true;
            body.angularDamping = 0.5f;
        }

        // The burnt-out plane goes once out of sight; a fresh one is back on its parking spot.
        private void HandleWreckCleanup()
        {
            gameObject.SetActive(false);
            body.isKinematic = true;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            gameObject.SetActive(true); // VehicleHealth and this reset on enable
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

        /// <summary>The exit point dropped onto the ground under it.</summary>
        public Transform GetExitPoint()
        {
            if (exitPoint == null || groundExit == null) return exitPoint;
            Vector3 p = exitPoint.position;
            groundExit.rotation = Quaternion.Euler(0f, exitPoint.eulerAngles.y, 0f);
            groundExit.position = Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 8f, groundLayers, QueryTriggerInteraction.Ignore)
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
            body.isKinematic = false;
            body.useGravity = false;
            body.WakeUp();
            yaw = transform.eulerAngles.y;
            if (chaseCamera != null)
            {
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
            externalInput = false;
            if (chaseCamera != null) chaseCamera.gameObject.SetActive(false);
            if (was) OnPlayerPilotingChanged?.Invoke(this, false);
        }

        public void ReleaseControlRolling() => DisableControl();

        public void SetDriverVisualActive(bool active)
        {
            if (driverDummy != null) driverDummy.SetActive(active);
        }

        /// <summary>
        /// Puts the plane at a spot (scripted starts, missions, tests): on the ground there (<paramref name="airSpeed"/> 0) or
        /// flying level along the heading at that speed.
        /// </summary>
        public void PlaceAt(Vector3 position, float heading, float airSpeed = 0f)
        {
            if (!isValid || IsWrecked) return;
            bool kinematic = body.isKinematic;
            body.isKinematic = true;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            body.position = position;
            body.rotation = transform.rotation;
            body.isKinematic = kinematic && airSpeed <= 0f;
            yaw = heading;
            pitch = 0f;
            bank = 0f;
            sink = 0f;
            ResetControlFeel();
            speed = Mathf.Max(0f, airSpeed);
            IsGrounded = airSpeed <= 0f;
            body.linearVelocity = body.isKinematic ? Vector3.zero : transform.forward * speed;
            lastVelocity = body.linearVelocity;
        }

        /// <summary>
        /// Flies on given inputs instead of the player's controls (scripted flights, tests). Throttle 0..1 (absolute; 0 on the
        /// ground brakes), roll (right +), pitch (nose up +), rudder (right +) -1..1.
        /// </summary>
        public void SetExternalInput(bool active, float throttle = 0f, float roll = 0f, float pitchUp = 0f, float rudder = 0f)
        {
            externalInput = active;
            externalThrottle = Mathf.Clamp01(throttle);
            externalRoll = Mathf.Clamp(roll, -1f, 1f);
            externalPitch = Mathf.Clamp(pitchUp, -1f, 1f);
            externalRudder = Mathf.Clamp(rudder, -1f, 1f);
        }

        // ------------------------------------------------------------------ engine sound

        private void Update()
        {
            float target = IsControlled && !IsWrecked ? 1f : 0f;
            engine01 = Mathf.MoveTowards(engine01, target, Time.deltaTime / spoolTime);
            if (engineAudio == null) return;
            if (engine01 > 0.01f && !engineAudio.isPlaying) engineAudio.Play();
            else if (engine01 <= 0.01f && engineAudio.isPlaying) engineAudio.Stop();
            float spool = Mathf.SmoothStep(0f, 1f, engine01);
            float speed01 = Mathf.Clamp01(CurrentSpeed / maxSpeed);
            engineAudio.volume = spool * Mathf.Lerp(idleVolume, fullVolume, Mathf.Max(Throttle01, speed01 * 0.6f));
            engineAudio.pitch = Mathf.Lerp(idlePitch * 0.6f, Mathf.Lerp(idlePitch, fullPitch, Throttle01), spool) + speedPitch * speed01;
        }

        // ------------------------------------------------------------------ flight

        private void FixedUpdate()
        {
            if (!isValid || body.isKinematic || crashed || IsWrecked) return;
            float dt = Time.fixedDeltaTime;
            MeasureGround();

            // --- input
            float roll = 0f, pitchIn = 0f, rudder = 0f, throttleDelta = 0f;
            bool brake = false;
            if (IsControlled && externalInput)
            {
                Throttle01 = externalThrottle;
                brake = IsGrounded && externalThrottle <= 0f;
                roll = externalRoll;
                pitchIn = externalPitch;
                rudder = externalRudder;
            }
            else if (IsControlled && !GamePlayer.MenuOpen)
            {
                bool up = throttleUpInput.GetButton(), down = throttleDownInput.GetButton();
                throttleDelta = (up ? 1f : 0f) - (down ? 1f : 0f);
                brake = down && Throttle01 <= 0.001f;
                roll = Mathf.Clamp(rollInput.GetAxis(), -1f, 1f);
                float stickPitch = Mathf.Clamp(pitchInput.GetAxis(), -1f, 1f);
                float keyPitch = (noseUpKey.GetButton() ? 1f : 0f) - (noseDownKey.GetButton() ? 1f : 0f);
                pitchIn = Mathf.Clamp(stickPitch + keyPitch, -1f, 1f) * (invertPitch ? -1f : 1f);
                rudder = (rudderRightInput.GetButton() ? 1f : 0f) - (rudderLeftInput.GetButton() ? 1f : 0f);
            }
            else
            {
                // Nobody at the controls: the engines wind down; on the ground it brakes to a stop.
                throttleDelta = -1f;
                brake = IsGrounded;
            }
            // Eased inputs: a key press / release (or a jerk of the stick) ramps in over Input Smooth Time.
            pitchCmd = Mathf.SmoothDamp(pitchCmd, pitchIn, ref pitchCmdVel, inputSmoothTime, Mathf.Infinity, dt);
            rollCmd = Mathf.SmoothDamp(rollCmd, roll, ref rollCmdVel, inputSmoothTime, Mathf.Infinity, dt);
            rudderCmd = Mathf.SmoothDamp(rudderCmd, rudder, ref rudderCmdVel, inputSmoothTime, Mathf.Infinity, dt);
            pitchIn = pitchCmd;
            roll = rollCmd;
            rudder = rudderCmd;

            Throttle01 = Mathf.Clamp01(Throttle01 + throttleDelta * throttleRate * dt);
            float power = Throttle01 * Mathf.SmoothStep(0f, 1f, engine01);

            // --- speed along the nose: thrust - drag, gravity along the climb / dive
            float drag = thrust / (maxSpeed * maxSpeed);
            float accel = power * thrust - drag * speed * Mathf.Abs(speed);
            if (IsGrounded)
            {
                accel -= Mathf.Sign(speed) * rollingResistance;
                if (brake) accel -= Mathf.Sign(speed) * brakeDeceleration;
                if (Mathf.Abs(speed) < 0.2f && power <= 0.001f) { speed = 0f; accel = 0f; }
            }
            else accel -= Physics.gravity.magnitude * Mathf.Sin(pitch * Mathf.Deg2Rad);
            speed = Mathf.Max(0f, speed + accel * dt);

            if (IsGrounded) GroundStep(dt, roll, rudder, pitchIn);
            else AirStep(dt, roll, rudder, pitchIn);

            if (IsGrounded && !IsControlled && speed <= 0.05f && sink <= 0.01f)
            {
                body.linearVelocity = Vector3.zero;
                body.isKinematic = true; // parked again
            }
        }

        private void GroundStep(float dt, float roll, float rudder, float pitchIn)
        {
            IsStalled = false;
            sink = 0f;
            // Nose wheel steering: tight when slow, gentle near rotate speed.
            float steer = Mathf.Clamp(roll + rudder, -1f, 1f);
            float turnRate = Mathf.Lerp(taxiTurnRate, fastTurnRate, Mathf.InverseLerp(4f, rotateSpeed, speed));
            yaw += steer * turnRate * Mathf.Clamp01(speed / 2f) * dt;
            bank = Mathf.SmoothDamp(bank, 0f, ref bankVelocity, bankSmoothTime, rollRate, dt);
            // Rotation: the nose comes up on the main wheels above rotate speed (eased, no snap when the key is released).
            float targetPitch = speed >= rotateSpeed && pitchIn > 0.1f ? maxRotatePitch * pitchIn : 0f;
            float rotateRate = Mathf.Clamp((targetPitch - pitch) * 2.5f, -8f, 8f);
            pitchVelocity = Mathf.Lerp(pitchVelocity, rotateRate, 1f - Mathf.Exp(-pitchResponse * dt));
            pitch += pitchVelocity * dt;

            Quaternion rotation = Quaternion.Euler(-pitch, yaw, -bank);
            Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // Held on the ground: the wheels follow the surface height.
            float vy = Mathf.Clamp(-HeightAboveGround / dt, -6f, 6f);
            Vector3 velocity = heading * speed + Vector3.up * vy;

            if (speed >= takeoffSpeed && pitch >= maxRotatePitch * 0.4f)
            {
                IsGrounded = false; // lift-off
                velocity = rotation * Vector3.forward * speed;
            }
            else if (HeightAboveGround > 1.5f)
            {
                IsGrounded = false; // rolled off an edge
            }
            Apply(velocity, rotation);
        }

        private void AirStep(float dt, float roll, float rudder, float pitchIn)
        {
            // Lift: full above stall speed, gone well below it.
            float lift = Mathf.InverseLerp(stallSpeed * 0.75f, stallSpeed, speed);
            IsStalled = lift < 0.99f;

            // Map limit: beyond the soft edge the plane is banked round towards the map (and turned a little extra), above the
            // ceiling the nose is pushed down. Both blend in with the depth, so the player feels a firm hand, not a wall.
            if (boundary != null && boundary.Evaluate(body.position, out Vector3 inward, out float outside01, out float above01))
            {
                if (outside01 > 0f)
                {
                    float inwardYaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
                    float delta = Mathf.DeltaAngle(yaw, inwardYaw);
                    float push = Mathf.SmoothStep(0f, 1f, outside01);
                    roll = Mathf.Lerp(roll, Mathf.Clamp(delta / 40f, -1f, 1f), push);
                    yaw = Mathf.MoveTowardsAngle(yaw, inwardYaw, boundaryTurnRate * push * Mathf.Clamp01(Mathf.Abs(delta) / 30f) * dt);
                }
                if (above01 > 0f) pitchIn = Mathf.Lerp(pitchIn, -0.5f, Mathf.SmoothStep(0f, 1f, above01));
            }

            // Bank follows the roll input and levels itself (SmoothDamp: eases in and out, capped by Roll Rate).
            bank = Mathf.SmoothDamp(bank, roll * maxBank, ref bankVelocity, bankSmoothTime, rollRate, dt);

            // Pitch: a target rate (input x authority, or a gentle self-levelling without input), followed with Pitch Response.
            float authority = Mathf.Lerp(0.3f, 1f, lift) * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(stallSpeed, stallSpeed * 1.8f, speed));
            float hands = Mathf.Clamp01(Mathf.Abs(pitchIn) * 2f);
            float levelling = Mathf.Clamp(-pitch * 1.2f, -pitchLevelRate, pitchLevelRate);
            float targetRate = pitchIn * pitchRate * authority + levelling * (1f - hands);
            // Stall: the nose drops until there is speed again.
            if (lift < 1f) targetRate = Mathf.Lerp(targetRate, Mathf.Clamp((-25f - pitch) * 1.5f, -25f, 25f), 1f - lift);
            // Ceiling: the climb rate eases to a gentle nose-down over the last 60 m.
            float ceiling01 = Mathf.InverseLerp(ceiling - 60f, ceiling, transform.position.y);
            if (ceiling01 > 0f) targetRate = Mathf.Min(targetRate, Mathf.Lerp(pitchRate, -6f, ceiling01));
            // Limits: the rate eases to zero within Pitch Soft Limit of Max Pitch instead of stopping dead.
            if (targetRate > 0f) targetRate *= Mathf.Clamp01((maxPitch - pitch) / pitchSoftLimit);
            else targetRate *= Mathf.Clamp01((pitch + maxPitch) / pitchSoftLimit);
            pitchVelocity = Mathf.Lerp(pitchVelocity, targetRate, 1f - Mathf.Exp(-pitchResponse * dt));
            pitch = Mathf.Clamp(pitch + pitchVelocity * dt, -maxPitch, maxPitch);

            // Turn: the bank turns the plane (coordinated turn, arcade boosted), plus rudder.
            float g = Physics.gravity.magnitude;
            float bankTurn = g * Mathf.Tan(bank * Mathf.Deg2Rad) / Mathf.Max(speed, 15f) * Mathf.Rad2Deg * turnBoost;
            yaw += (bankTurn * lift + rudder * rudderRate) * dt;

            // Lost lift: an extra sink that builds up (and goes away again once flying).
            sink = lift < 1f ? sink + g * (1f - lift) * dt : Mathf.MoveTowards(sink, 0f, g * dt);

            Quaternion rotation = Quaternion.Euler(-pitch, yaw, -bank);
            Vector3 velocity = rotation * Vector3.forward * speed + Vector3.down * sink;

            // Touchdown: the wheels at the ground while coming down.
            if (HeightAboveGround <= 0.2f && velocity.y <= 0.5f)
            {
                if (IsOverWater())
                {
                    Crash();
                    return;
                }
                bool gentle = -velocity.y <= maxLandingSink && Mathf.Abs(bank) <= maxLandingBank && pitch > -8f && pitch < maxPitch * 0.6f;
                if (!gentle)
                {
                    Crash();
                    return;
                }
                IsGrounded = true;
                sink = 0f;
                // Rolls on along the heading with the speed it had.
                speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
                velocity = new Vector3(velocity.x, 0f, velocity.z);
            }
            Apply(velocity, rotation);
        }

        private void Apply(Vector3 velocity, Quaternion rotation)
        {
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(rotation);
            lastVelocity = velocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (crashed || IsWrecked || body.isKinematic || collision.contactCount == 0) return;
            if (((1 << collision.gameObject.layer) & CrashLayers) == 0) return;
            ContactPoint contact = collision.GetContact(0);
            // Our own speed into the obstacle (a car running into the parked plane does not count).
            float into = Mathf.Abs(Vector3.Dot(lastVelocity, contact.normal));
            if (into >= crashImpactSpeed || (!IsGrounded && speed > stallSpeed * 0.5f && into >= crashImpactSpeed * 0.5f)) Crash();
        }

        // World and vehicles (not characters or triggers' layers): the shared crash layers if there is health, else the ground.
        private int CrashLayers => health != null && health.Settings != null ? health.Settings.crashLayers.value | groundLayers.value : groundLayers.value;

        private void Crash()
        {
            if (crashed) return;
            crashed = true;
            body.useGravity = true;
            body.linearVelocity = lastVelocity * 0.4f;
            if (health != null && !health.IsDestroyed) health.ApplyDamage(health.MaxHealth * 4f, transform);
        }

        private bool IsOverWater()
        {
            return water != null && water.TryGetSurface(transform.position, out float surface) && transform.position.y <= surface + 0.5f;
        }

        // One ray from above the main wheels (the pivot): height of the wheels over the ground or the water.
        private void MeasureGround()
        {
            Vector3 origin = body.position + Vector3.up * 3f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 600f, groundLayers, QueryTriggerInteraction.Ignore);
            float nearest = 600f;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.IsChildOf(transform)) continue;
                nearest = Mathf.Min(nearest, Hits[i].distance - 3f);
            }
            if (water != null && water.TryGetSurface(body.position, out float surface))
                nearest = Mathf.Min(nearest, body.position.y - surface);
            HeightAboveGround = Mathf.Max(0f, nearest);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnPlayerPilotingChanged = null;

#if UNITY_EDITOR
        public void EditorSetBoundary(WorldBoundary worldBoundary) => boundary = worldBoundary;

        public void EditorSetup(Rigidbody rigidbody, VehicleHealth vehicleHealth, WaterManager waterManager, Transform seat, Transform enter,
            Transform exit, Transform alternateExit, Transform interaction, GameObject dummy, HelicopterCamera cameraRig, AudioSource audio,
            LayerMask ground)
        {
            body = rigidbody;
            health = vehicleHealth;
            water = waterManager;
            driverSeat = seat;
            enterPoint = enter;
            exitPoint = exit;
            alternateExitPoint = alternateExit;
            interactionPoint = interaction;
            driverDummy = dummy;
            chaseCamera = cameraRig;
            engineAudio = audio;
            groundLayers = ground;
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
