using System;
using System.Collections;
using System.Collections.Generic;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Player
{
    public enum VehicleInteractionState
    {
        OnFoot,
        /// <summary>Walking around the vehicle to its enter point before the enter animation starts.</summary>
        Approaching,
        /// <summary>Pulling a seated driver out (carjack) before entering.</summary>
        Carjacking,
        Entering,
        InVehicle,
        Exiting,
        /// <summary>Jumping out of a moving vehicle: diving through the door, rolling or tumbling (ragdoll) and getting up.</summary>
        Bailing
    }

    public enum VehicleExitBlockReason
    {
        VehicleMoving,
        NoClearExit
    }

    /// <summary>
    /// Player side of getting in and out of vehicles (per-player component, not a Singleton).
    /// Detects <see cref="IVehicleInteractionTarget"/> interaction triggers (drivable vehicles and traffic cars), raises prompt/state
    /// events for the UI and runs the enter/exit sequence: taking the vehicle (traffic cars are swapped for their drivable
    /// counterpart), walking to the driver door when started elsewhere, input + combat lock, weapon stow/restore, alignment,
    /// pulling out a seated NPC driver while the door opens, the Invector
    /// "CustomAction" enter/exit clips (root motion warped with Animator.MatchTarget) and the swap between
    /// the real character and the vehicle's static seated driver visual.
    /// Exit pressed while the vehicle moves at Bail Min Speed or faster bails out (GTA IV style): the door flies open, the
    /// character dives out sideways carrying the vehicle's momentum and either rolls (slow) or tumbles as Invector's ragdoll and
    /// gets up with its get-up animations (fast); the vehicle rolls on without a driver.
    /// Everything vehicle-specific (driving control, engine, vehicle camera) is delegated to the vehicle.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleInteraction : MonoBehaviour
    {
        private const int MaxOverlapResults = 16;
        private const int BaseLayer = 0;
        private const float StateStartTimeout = 1f;
        private const float DoorTimeout = 3f;
        private const int DoorCueNone = 0;
        private const int DoorCueOpened = 1;
        private const int DoorCueClosed = 2;

        [Header("Player References")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CapsuleCollider capsule;
        [Tooltip("Visual model root (bones, hit boxes, foot triggers). Hidden while seated - the vehicle's driver visual is shown instead.")]
        [SerializeField] private GameObject characterModel;
        [Tooltip("Invector vThirdPersonCamera object (holds the Main Camera, detaches from the player at runtime). Off while the vehicle camera is active.")]
        [SerializeField] private GameObject playerCameraRoot;
        [Tooltip("Player components switched off while seated (Invector input/controller, head track, generic actions, on-foot interaction).")]
        [SerializeField] private Behaviour[] disabledWhileSeated = new Behaviour[0];
        [Tooltip("Optional foot IK for the enter/exit clips (keeps the feet on the cabin floor so legs never show under the vehicle).")]
        [SerializeField] private PlayerVehicleIK vehicleIK;

        [Header("Input")]
        [Tooltip("Enter / exit. Pressing it again while walking to the door cancels.")]
        [SerializeField] private GenericInput enterExitInput = new GenericInput("F", "Back", "Back");

        [Header("Driver Door Range")]
        [Tooltip("A vehicle can only be entered (prompt and button) while the character stands on its driver side within this " +
                 "distance (m) of the driver door (the enter point, or the steal point when a driver sits at the wheel).")]
        [SerializeField, Min(0.5f)] private float driverDoorRange = 2f;

        [Header("Approach (walk to the driver door)")]
        [Tooltip("When the character is farther than this (m) from the enter point, it first walks there around the vehicle.")]
        [SerializeField, Min(0.1f)] private float approachStartDistance = 0.6f;
        [Tooltip("Distance (m) at which a waypoint / the enter point counts as reached.")]
        [SerializeField, Min(0.05f)] private float approachArriveDistance = 0.3f;
        [Tooltip("How far (m) outside the vehicle's corners the walking path goes.")]
        [SerializeField, Min(0.1f)] private float approachCornerClearance = 0.9f;
        [Tooltip("Extra gap (m) kept between the character's capsule and the vehicle body while walking around it.")]
        [SerializeField, Min(0f)] private float approachBodyMargin = 0.15f;
        [Tooltip("If the character moves less than this (m) in Stuck Check Interval while walking, the path is rebuilt from its position.")]
        [SerializeField, Min(0.01f)] private float approachStuckDistance = 0.1f;
        [SerializeField, Min(0.1f)] private float approachStuckCheckInterval = 0.6f;
        [Tooltip("Walk instead of run while approaching.")]
        [SerializeField] private bool approachWalk = true;
        [Tooltip("With Approach Walk: farther than this (m) from the door the character jogs, then walks the last steps " +
                 "(Gangstar / GTA style - no slow stroll all the way round the car). 0 = always walk.")]
        [SerializeField, Min(0f)] private float approachJogDistance = 2.5f;
        [Tooltip("Gives up after this many seconds (e.g. the path is blocked by a wall).")]
        [SerializeField, Min(1f)] private float approachTimeout = 8f;
        [Tooltip("A reserved vehicle that is still braking (e.g. a traffic car) gets this long (s) to stop before the enter is cancelled.")]
        [SerializeField, Min(0.5f)] private float vehicleStopTimeout = 5f;
        [Tooltip("While a reserved vehicle is still stopping, the character walks toward it until this close (m) to its interaction point.")]
        [SerializeField, Min(0.1f)] private float waitForStopDistance = 1.5f;

        [Header("Enter Animation (Base Layer, OWC_Shooter.controller)")]
        [SerializeField] private string enterStateName = "Vehicle_Enter";
        [Tooltip("Exit clip frozen on its first frame - identical to the seated driver visual.")]
        [SerializeField] private string seatedStateName = "Vehicle_Seated";
        [SerializeField, Min(0f)] private float enterCrossFade = 0.2f;
        [Tooltip("Normalized window of the enter clip in which the root is warped onto the seat (Animator.MatchTarget).")]
        [SerializeField, Range(0f, 1f)] private float enterMatchStart = 0.3f;
        [SerializeField, Range(0f, 1f)] private float enterMatchEnd = 0.8f;
        [Tooltip("Normalized time of the enter clip at which the character is fully seated.")]
        [SerializeField, Range(0f, 1f)] private float enterSeatedTime = 0.95f;
        [Tooltip("Enter clip time at which the character starts climbing in (after letting go of the handle, just before the legs " +
                 "reach the sill). Raises OnVehicleClimbStarted (the entry camera swings to its get-in view then).")]
        [SerializeField, Range(0f, 1f)] private float enterClimbTime = 0.33f;
        [Tooltip("Carjacking with an animated driver door: share of the door's opening at which the seated NPC driver is pulled out.")]
        [SerializeField, Range(0f, 1f)] private float enterDriverPullDoorShare = 0.4f;
        [Tooltip("Carjacking without an animated driver door: normalized time of the enter clip at which the NPC driver is pulled out. " +
                 "In any case the driver is pulled out at Enter Match Start at the latest, before the character climbs in.")]
        [SerializeField, Range(0f, 1f)] private float enterDriverPullTime = 0.25f;
        [Tooltip("Enter clip playback speed for a normal (empty vehicle) enter. 1 = the clip's own speed.")]
        [SerializeField, Range(1f, 3f)] private float enterAnimationSpeed = 2.6f;
        [Tooltip("Enter clip time a normal enter starts from (skips the clip's idle lead-in; the hand is already reaching for the handle).")]
        [SerializeField, Range(0f, 0.2f)] private float enterStartTime = 0.16f;
        [Tooltip("Hand that takes the driver door handle and pulls the door open (IK onto the door's Handle point).")]
        [SerializeField] private AvatarIKGoal enterHandleHand = AvatarIKGoal.LeftHand;
        [Tooltip("The hand is guided onto the handle from this distance (m) ...")]
        [SerializeField, Min(0.05f)] private float enterHandleReachDistance = 0.6f;
        [Tooltip("... or from this enter clip time.")]
        [SerializeField, Range(0f, 1f)] private float enterHandleReachTime = 0.18f;
        [Tooltip("The door starts opening once that hand is this close (m) to the handle ...")]
        [SerializeField, Min(0.05f)] private float enterHandleGrabDistance = 0.3f;
        [Tooltip("... or at the latest at this enter clip time.")]
        [SerializeField, Range(0f, 1f)] private float enterHandleLatestTime = 0.24f;
        [Tooltip("Enter clip time at which that hand lets go of the handle (the door is open).")]
        [SerializeField, Range(0f, 1f)] private float enterHandleReleaseTime = 0.32f;
        [Tooltip("Enter clip playback speed after a carjack (the character is already at the open door). 1 = normal.")]
        [SerializeField, Range(1f, 2f)] private float carjackEnterAnimationSpeed = 2f;

        [Header("Carjack (OWC_Shooter.controller, Vehicle sub-state machine)")]
        [Tooltip("Opening the driver door from beside the door opening (default clip: Invector Press_Lever).")]
        [SerializeField] private string carjackOpenDoorState = "Carjack_OpenDoor";
        [Tooltip("Reaching in, grabbing and pulling the driver out (default clip: Invector Pick_Mid).")]
        [SerializeField] private string carjackPullState = "Carjack_Pull";
        [Tooltip("Shoving the pulled-out driver away (default clip: Invector WeakAttack_UnarmedB, a left-hand push).")]
        [SerializeField] private string carjackShoveState = "Carjack_Shove";
        [SerializeField, Min(0f)] private float carjackCrossFade = 0.15f;
        [Tooltip("Open-door clip time at which the door starts swinging open.")]
        [SerializeField, Range(0f, 1f)] private float carjackDoorOpenTime = 0.3f;
        [Tooltip("Playback speed of the open-door clip.")]
        [SerializeField, Range(0.5f, 2f)] private float carjackOpenDoorSpeed = 1.3f;
        [SerializeField, Min(0f)] private float carjackDoorOpenDuration = 0.4f;
        [Tooltip("Open-door clip time at which the pull begins.")]
        [SerializeField, Range(0f, 1f)] private float carjackOpenDoorEndTime = 0.5f;
        [Tooltip("Playback speed of the pull clip while reaching in (normal speed once the driver comes out).")]
        [SerializeField, Range(0.5f, 2f)] private float carjackReachSpeed = 1.3f;
        [Tooltip("Pull clip time from which the driver starts coming out, as soon as the hand is on him ...")]
        [SerializeField, Range(0f, 1f)] private float carjackPullBeginTime = 0.2f;
        [Tooltip("... i.e. within this distance (m) of his chest ...")]
        [SerializeField, Min(0.05f)] private float carjackGrabDistance = 0.4f;
        [Tooltip("... or at the latest at this pull clip time.")]
        [SerializeField, Range(0f, 1f)] private float carjackPullLatestTime = 0.45f;
        [Tooltip("Hand that grabs the driver (IK onto his collar while he is pulled out).")]
        [SerializeField] private AvatarIKGoal carjackGrabHand = AvatarIKGoal.LeftHand;
        [Tooltip("Degrees per second the character turns to keep facing the driver while pulling.")]
        [SerializeField, Min(0f)] private float carjackTurnSpeed = 180f;
        [Tooltip("Offset (vehicle space: x = right, z = forward) the character steps back by while pulling, making room for the driver.")]
        [SerializeField] private Vector3 carjackStepBack = new Vector3(-0.55f, 0f, -0.25f);
        [SerializeField, Min(0.01f)] private float carjackShoveTurnDuration = 0.12f;
        [Tooltip("Driver pull-out progress (0-1) at which the shove starts, so the hand lands just as he is out.")]
        [SerializeField, Range(0f, 1f)] private float carjackShoveAtPullProgress = 0.6f;
        [Tooltip("The character faces the driver's chest, turned this far toward the direction the driver is shoved in (the driver's Eject Point forward).")]
        [SerializeField, Range(0f, 1f)] private float carjackShoveFacingBlend = 0.5f;
        [Tooltip("Playback speed of the shove clip.")]
        [SerializeField, Range(0.5f, 2f)] private float carjackShoveSpeed = 1.3f;
        [Tooltip("Shove clip time at which the hand hits and the driver starts stumbling.")]
        [SerializeField, Range(0f, 1f)] private float carjackShoveImpactTime = 0.38f;
        [Tooltip("Shove clip time at which the character turns straight into the enter clip.")]
        [SerializeField, Range(0f, 1f)] private float carjackShoveEndTime = 0.6f;
        [Tooltip("Enter clip time the enter starts from after a carjack (the door is already open).")]
        [SerializeField, Range(0f, 0.45f)] private float carjackEnterStartTime = 0.36f;
        [Tooltip("After a carjack the character slides back onto the enter point (from where he stepped back to pull the driver out) " +
                 "while turning toward the car, taking at least this many seconds, before the enter clip's seat warp carries him in.")]
        [SerializeField, Min(0.05f)] private float carjackEnterTurnDuration = 0.2f;
        [Tooltip("Root offset (driver seat space) the enter clip ends on. Its final pose has the hips ~0.13 m further back and the head ~5 cm higher " +
                 "than the seated pose, so the root lands this far forward/lower and slides onto the seat while the pose blends to the seated pose " +
                 "(keeps the head under a low roof).")]
        [SerializeField] private Vector3 enterEndSeatOffset = new Vector3(0f, -0.06f, 0.13f);
        [SerializeField, Min(0.05f)] private float seatSettleDuration = 0.3f;

        [Header("Quick Mount (bikes, IVehicleController.UsesQuickMount)")]
        [Tooltip("Seconds of the swing from beside the bike onto the seat (and of stepping off again).")]
        [SerializeField, Min(0.05f)] private float quickMountDuration = 0.45f;
        [Tooltip("Height (m) of the leg-over arc while mounting / dismounting.")]
        [SerializeField, Min(0f)] private float quickMountHop = 0.25f;

        [Header("Exit Animation")]
        [SerializeField] private string exitStateName = "Vehicle_Exit";
        [Tooltip("Exit clip playback speed. 1 = the clip's own speed.")]
        [SerializeField, Range(1f, 2.5f)] private float exitAnimationSpeed = 1.8f;
        [Tooltip("Normalized window of the exit clip in which the root rises from the (low) seat height to the ground height of the exit point. " +
                 "Keep it to the part where the character stands up outside the door; a longer window leaves the character crouched below the ground.")]
        [SerializeField, Range(0f, 1f)] private float exitRiseStart = 0.34f;
        [SerializeField, Range(0f, 1f)] private float exitRiseEnd = 0.48f;
        [Tooltip("Normalized window of the exit clip in which the root is warped horizontally (position and facing) onto the exit point. " +
                 "It starts no earlier than Exit Rise End.")]
        [SerializeField, Range(0f, 1f)] private float exitMatchStart = 0.25f;
        [SerializeField, Range(0f, 1f)] private float exitMatchEnd = 0.92f;
        [Tooltip("Normalized time of the exit clip at which on-foot control returns (matches the Vehicle_Exit -> Free Movement transition).")]
        [SerializeField, Range(0f, 1f)] private float exitCompleteTime = 0.95f;
        [Tooltip("Seconds over which the reclined seated torso sits back up at the start of the exit.")]
        [SerializeField, Min(0.05f)] private float exitLeanRelease = 0.5f;
        [Tooltip("On-foot state used by the instant (fallback) exit.")]
        [SerializeField] private string onFootStateName = "Free Movement";

        [Header("Bail Out (exit while the vehicle moves)")]
        [Tooltip("Exit pressed at or above this speed (km/h) bails out of the moving vehicle; below it the normal exit rules apply.")]
        [SerializeField, Min(1f)] private float bailMinSpeedKmh = 15f;
        [Tooltip("Up to this speed (km/h) the character rolls out on his feet; faster bail-outs tumble as a ragdoll.")]
        [SerializeField, Min(0f)] private float bailRollMaxSpeedKmh = 25f;
        [Tooltip("Invector ragdoll of the player (tumble and get-up). Without it only the roll is possible.")]
        [SerializeField] private vRagdoll playerRagdoll;
        [Tooltip("Pose (Base Layer state) the character dives out in before rolling or tumbling.")]
        [SerializeField] private string bailDiveStateName = "Falling";
        [SerializeField, Min(0.05f)] private float bailDoorOpenDuration = 0.15f;
        [Tooltip("Seconds of the dive from the seat out through the door opening (it moves with the vehicle).")]
        [SerializeField, Min(0.05f)] private float bailDiveDuration = 0.18f;
        [Tooltip("Metres the dive carries the character sideways from the seat, clear of the door sill and the wheels.")]
        [SerializeField, Min(0.1f)] private float bailDiveDistance = 1.1f;
        [Tooltip("Share of the vehicle's velocity the body keeps when it leaves the car.")]
        [SerializeField, Range(0f, 1f)] private float bailCarriedVelocity = 0.85f;
        [Tooltip("Upper limit (m/s) of the carried velocity (keeps very fast bail-outs stable).")]
        [SerializeField, Min(1f)] private float bailMaxCarriedSpeed = 20f;
        [Tooltip("Sideways speed (m/s) of the dive, away from the vehicle.")]
        [SerializeField, Min(0f)] private float bailSideSpeed = 3f;
        [SerializeField, Min(0f)] private float bailUpSpeed = 1.2f;
        [Tooltip("Tumble (rad/s) per m/s of carried speed, rolling the body over along its travel direction.")]
        [SerializeField, Min(0f)] private float bailTumblePerSpeed = 0.35f;
        [SerializeField, Min(0f)] private float bailMaxTumble = 7f;
        [Tooltip("Speed (km/h) at which the tumble reaches its longest time on the ground.")]
        [SerializeField, Min(1f)] private float bailFullSpeedKmh = 90f;
        [Tooltip("Seconds kept on the ground at the slowest / fastest tumble (Invector's ragdoll also waits for the body to settle).")]
        [SerializeField, Min(0f)] private float bailMinGroundTime = 0.3f;
        [SerializeField, Min(0f)] private float bailMaxGroundTime = 2.2f;
        [Tooltip("Degrees the roll-out is turned away from the vehicle (off its path).")]
        [SerializeField, Range(0f, 60f)] private float bailRollAwayAngle = 35f;
        [Tooltip("Seconds the body ignores the vehicle it jumped from (longer while it still overlaps it).")]
        [SerializeField, Min(0.1f)] private float bailIgnoreVehicleTime = 1.2f;
        [Tooltip("Speed-scaled damage: none below this speed (km/h) ...")]
        [SerializeField, Min(0f)] private float bailDamageStartKmh = 40f;
        [Tooltip("... then this much damage per km/h above it ...")]
        [SerializeField, Min(0f)] private float bailDamagePerKmh = 0.5f;
        [Tooltip("... and below this speed (km/h) a bail-out never kills (at least 1 health is left).")]
        [SerializeField, Min(0f)] private float bailLethalSpeedKmh = 160f;
        [Tooltip("Safety limit (s) for the whole bail-out; control is given back after it.")]
        [SerializeField, Min(2f)] private float bailTimeout = 15f;

        [Header("Alignment")]
        [Tooltip("Speed (m/s) used to slide the character onto the enter point before the enter clip starts.")]
        [SerializeField, Min(0.1f)] private float alignSpeed = 2.5f;
        [Tooltip("Metres outside the enter/steal point the walk to the car ends at (the line-up covers the rest).")]
        [SerializeField, Min(0f)] private float approachGoalStandOff = 0.45f;
        [SerializeField, Min(0.05f)] private float maxAlignDuration = 0.6f;
        [Tooltip("Seconds needed to turn 180 degrees while aligning, so a large turn never snaps.")]
        [SerializeField, Min(0.05f)] private float alignTurnDurationPer180 = 0.35f;
        [Tooltip("Safety limit for a whole enter/exit sequence; after it the documented snap fallback is used.")]
        [SerializeField, Min(1f)] private float sequenceTimeout = 10f;

        [Header("Driver Door Timing (normalized clip time)")]
        [Tooltip("Enter clip time at which the hand reaches the handle and the driver door starts opening.")]
        [SerializeField, Range(0f, 1f)] private float enterDoorOpenTime = 0.19f;
        [Tooltip("Enter clip time at which the seated character pulls the door closed. Values >= Enter Seated Time close it once seated.")]
        [SerializeField, Range(0f, 1f)] private float enterDoorCloseTime = 0.66f;
        [Tooltip("Exit clip time at which the hand starts pushing the driver door open; it swings open in one smooth motion " +
                 "(the door clearance keeps it ahead of the pushing hand).")]
        [SerializeField, Range(0f, 1f)] private float exitDoorOpenTime = 0.1f;
        [Tooltip("Exit clip time at which the hand starts pulling the door closed. Values >= Exit Complete Time close it at the end.")]
        [SerializeField, Range(0f, 1f)] private float exitDoorCloseTime = 0.85f;

        [Header("Driver Door Swing Durations (seconds, 0 = the door's own duration)")]
        [SerializeField, Min(0f)] private float enterDoorOpenDuration;
        [SerializeField, Min(0f)] private float enterDoorCloseDuration;
        [Tooltip("Match the exit clip's push: the door swings open while the hand pushes it.")]
        [SerializeField, Min(0f)] private float exitDoorOpenDuration;
        [Tooltip("Match the exit clip's pull: the door closes while the hand sweeps it shut.")]
        [SerializeField, Min(0f)] private float exitDoorCloseDuration;

        [Header("Exit Checks")]
        [SerializeField, Min(0.1f)] private float exitClearRadius = 0.3f;
        [SerializeField, Min(0.5f)] private float exitClearHeight = 1.7f;
        [Tooltip("Layers that block an exit point (triggers are always ignored).")]
        [SerializeField] private LayerMask exitBlockingLayers = ~0;
        [Tooltip("Layers used to find the ground under enter/exit points.")]
        [SerializeField] private LayerMask groundLayers = 1;
        [SerializeField, Min(0.1f)] private float groundProbeHeight = 1.5f;
        [SerializeField, Min(0.5f)] private float groundProbeDistance = 4f;

        /// <summary>Raised when the vehicle that can be entered changes (null = none in range). Used for the prompt.</summary>
        public event Action<IVehicleInteractionTarget> OnVehicleFocusChanged;
        /// <summary>Raised when the character starts walking to the enter point (entering from elsewhere around the vehicle).</summary>
        public event Action<IVehicleInteractionTarget> OnVehicleApproachStarted;
        /// <summary>Raised when walking to the enter point is cancelled (input pressed again, timeout, vehicle gone).</summary>
        public event Action<IVehicleInteractionTarget> OnVehicleApproachCancelled;
        public event Action<IVehicleController> OnVehicleEnterStarted;
        /// <summary>Raised during the enter when the character starts climbing in (see Enter Climb Time; at once after a carjack).</summary>
        public event Action<IVehicleController> OnVehicleClimbStarted;
        public event Action<IVehicleController> OnVehicleEntered;
        public event Action<IVehicleController> OnVehicleExitStarted;
        /// <summary>Raised when the player bails out of a moving vehicle, before its camera is switched off (OnVehicleExited follows once back on his feet).</summary>
        public event Action<IVehicleController> OnVehicleBailStarted;
        public event Action<IVehicleController> OnVehicleExited;
        /// <summary>Raised when an exit request is refused.</summary>
        public event Action<VehicleExitBlockReason> OnExitBlocked;
        public event Action<VehicleInteractionState> OnStateChanged;

        public VehicleInteractionState State { get; private set; } = VehicleInteractionState.OnFoot;
        public IVehicleController CurrentVehicle { get; private set; }
        public IVehicleInteractionTarget FocusedVehicle { get; private set; }
        public bool IsInVehicle => State == VehicleInteractionState.InVehicle;
        public bool IsBusy => State == VehicleInteractionState.Approaching || State == VehicleInteractionState.Carjacking
                              || State == VehicleInteractionState.Entering || State == VehicleInteractionState.Exiting
                              || State == VehicleInteractionState.Bailing;

        private readonly List<IVehicleInteractionTarget> nearbyVehicles = new List<IVehicleInteractionTarget>();
        private readonly VehicleApproachPath approachPath = new VehicleApproachPath();
        private readonly Collider[] overlapResults = new Collider[MaxOverlapResults];
        private Collider[] playerColliders = new Collider[0];
        private vThirdPersonController controller;
        private Transform originalParent;
        private int savedWeaponSlot = -1;
        private bool stateWaitSucceeded;
        private int enterStateHash;
        private int seatedStateHash;
        private int exitStateHash;
        private int onFootStateHash;
        private int carjackOpenDoorHash;
        private int carjackPullHash;
        private int carjackShoveHash;
        private int bailDiveHash;
        private Transform[] doorClearanceBones = new Transform[0];
        private Rigidbody[] ragdollBodies = new Rigidbody[0];
        private Transform moveReference;
        private Coroutine approachRoutine;
        private IVehicleInteractionTarget reservedTarget;
        private bool savedAlwaysWalk;
        private vThirdPersonMotor.LocomotionType savedLocomotionType;
        private bool savedStrafing;
        private bool savedLockInStrafe;
        private bool approachLocomotionApplied;
        private bool weaponStowed;

        private void Awake()
        {
            controller = playerManager != null ? playerManager.Controller : null;
            if (playerManager == null || controller == null || weaponController == null || animator == null
                || body == null || capsule == null || characterModel == null || playerCameraRoot == null)
            {
                Debug.LogError($"[{nameof(PlayerVehicleInteraction)}] Missing references on '{name}' (PlayerManager, PlayerWeaponController, " +
                               "Animator, Rigidbody, CapsuleCollider, character model, player camera root).", this);
                enabled = false;
                return;
            }

            if (vehicleIK == null)
                Debug.LogWarning($"[{nameof(PlayerVehicleInteraction)}] No PlayerVehicleIK assigned on '{name}'; feet may show under the vehicle while getting in/out.", this);

            playerColliders = GetComponentsInChildren<Collider>(true);
            if (playerRagdoll != null)
            {
                var limbs = new List<Rigidbody>();
                foreach (Rigidbody limb in GetComponentsInChildren<Rigidbody>(true))
                {
                    if (limb != body) limbs.Add(limb);
                }
                ragdollBodies = limbs.ToArray();
            }
            enterStateHash = Animator.StringToHash(enterStateName);
            seatedStateHash = Animator.StringToHash(seatedStateName);
            exitStateHash = Animator.StringToHash(exitStateName);
            onFootStateHash = Animator.StringToHash(onFootStateName);
            carjackOpenDoorHash = Animator.StringToHash(carjackOpenDoorState);
            carjackPullHash = Animator.StringToHash(carjackPullState);
            carjackShoveHash = Animator.StringToHash(carjackShoveState);
            bailDiveHash = Animator.StringToHash(bailDiveStateName);
            if (playerRagdoll == null)
                Debug.LogWarning($"[{nameof(PlayerVehicleInteraction)}] No vRagdoll assigned on '{name}'; bailing out of a moving vehicle is limited to the roll speed.", this);
            doorClearanceBones = GetDoorClearanceBones();
            // Invector turns movement input into a direction relative to the Main Camera, so approach input uses the same reference.
            Camera playerCamera = playerCameraRoot.GetComponentInChildren<Camera>(true);
            moveReference = playerCamera != null ? playerCamera.transform : null;
        }

        private void OnDisable()
        {
            if (State == VehicleInteractionState.Approaching) CancelApproach();
            nearbyVehicles.Clear();
            SetFocus(null);
        }

        // Per frame: enter/exit input and choosing the focused vehicle among the (few) triggers the player is inside.
        private void Update()
        {
            switch (State)
            {
                case VehicleInteractionState.OnFoot:
                    UpdateFocus();
                    if (FocusedVehicle != null && CanActOnFoot() && enterExitInput.GetButtonDown()) RequestEnter(FocusedVehicle);
                    break;
                case VehicleInteractionState.Approaching:
                    if (enterExitInput.GetButtonDown()) CancelApproach();
                    break;
                case VehicleInteractionState.InVehicle:
                    if (enterExitInput.GetButtonDown()) RequestExit();
                    break;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.isTrigger) return;
            var vehicle = other.GetComponentInParent<IVehicleInteractionTarget>();
            if (vehicle != null && !nearbyVehicles.Contains(vehicle)) nearbyVehicles.Add(vehicle);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.isTrigger) return;
            var vehicle = other.GetComponentInParent<IVehicleInteractionTarget>();
            if (vehicle != null) nearbyVehicles.Remove(vehicle);
        }

        /// <summary>
        /// Starts entering the given vehicle: it is reserved (a traffic car brakes to a stop), the character walks to its door if
        /// needed, and once the vehicle is ready the enter sequence runs on the drivable vehicle it provides (a traffic car is
        /// swapped for its drivable counterpart only at that point). Returns false if not possible right now.
        /// </summary>
        public bool RequestEnter(IVehicleInteractionTarget target)
        {
            if (State != VehicleInteractionState.OnFoot || target == null || (target as UnityEngine.Object) == null || !target.CanEnter || !CanActOnFoot()) return false;
            if (target.GetEnterPoint() == null) return false;

            target.ReserveForEntry();
            reservedTarget = target;
            originalParent = transform.parent;
            SetFocus(null);

            Vector3 toEnter = GetGroundedPosition(GetApproachPosition(target)) - transform.position;
            toEnter.y = 0f;
            if (!target.IsReadyForEntry || toEnter.magnitude > approachStartDistance)
            {
                StartApproach(target);
                return true;
            }

            CommitEnter(target);
            return true;
        }

        // A seated driver is pulled out from the steal point; otherwise the character walks straight to the enter point.
        // The walk ends a little outside the goal (which may be right at the car body, e.g. the enter point where the hand
        // meets the door handle); the enter/carjack line-up covers the last step while its clip already plays.
        private Vector3 GetApproachPosition(IVehicleInteractionTarget target)
        {
            Transform goal = GetApproachGoal(target);
            Vector3 back = -goal.forward;
            back.y = 0f;
            return goal.position + (back.sqrMagnitude > 0.0001f ? back.normalized * approachGoalStandOff : Vector3.zero);
        }

        private static Transform GetApproachGoal(IVehicleInteractionTarget target)
        {
            Transform stealPoint = target.GetStealPoint();
            return stealPoint != null ? stealPoint : target.GetEnterPoint();
        }

        // Walking around the vehicle keeps normal locomotion (gravity, collisions, animations); only player input is replaced.
        private void StartApproach(IVehicleInteractionTarget target)
        {
            SetState(VehicleInteractionState.Approaching);
            playerManager.SetInputLocked(true);
            if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(true);
            StowWeapon();
            ApplyApproachLocomotion();

            OnVehicleApproachStarted?.Invoke(target);
            approachRoutine = StartCoroutine(ApproachRoutine(target));
        }

        // Free locomotion while walking, so the character turns to face where it goes instead of strafing toward the camera direction.
        private void ApplyApproachLocomotion()
        {
            if (approachLocomotionApplied) return;
            approachLocomotionApplied = true;
            savedAlwaysWalk = controller.alwaysWalkByDefault;
            savedLocomotionType = controller.locomotionType;
            savedStrafing = controller.isStrafing;
            savedLockInStrafe = controller.lockInStrafe;
            if (approachWalk) controller.alwaysWalkByDefault = true;
            controller.lockInStrafe = false;
            controller.isStrafing = false;
            controller.locomotionType = vThirdPersonMotor.LocomotionType.OnlyFree;
        }

        // Only undoes what ApplyApproachLocomotion changed: restoring unsaved defaults would leave the character in free
        // locomotion, where Invector's shooter never aims.
        private void RestoreLocomotion()
        {
            controller.input = Vector3.zero;
            if (!approachLocomotionApplied) return;
            approachLocomotionApplied = false;
            controller.alwaysWalkByDefault = savedAlwaysWalk;
            controller.locomotionType = savedLocomotionType;
            controller.lockInStrafe = savedLockInStrafe;
            controller.isStrafing = savedStrafing;
        }

        private static bool IsTargetGone(IVehicleInteractionTarget target)
        {
            var targetBehaviour = target as Behaviour;
            return targetBehaviour == null || !targetBehaviour.isActiveAndEnabled;
        }

        private IEnumerator ApproachRoutine(IVehicleInteractionTarget target)
        {
            // 1. A braking vehicle (e.g. a traffic car) first comes to a stop; meanwhile the character heads toward it.
            float stopTimer = 0f;
            while (!IsTargetGone(target) && !target.IsReadyForEntry)
            {
                if (playerManager.CurrentState == PlayerState.Dead || stopTimer > vehicleStopTimeout)
                {
                    approachRoutine = null;
                    CancelApproach();
                    yield break;
                }
                Vector3 toVehicle = target.GetInteractionPoint().position - transform.position;
                toVehicle.y = 0f;
                SetMoveInput(toVehicle.magnitude > waitForStopDistance ? toVehicle.normalized : Vector3.zero);
                stopTimer += Time.deltaTime;
                yield return null;
            }

            // 2. Walk around the stopped vehicle to its enter point, unless already there.
            if (!IsTargetGone(target))
            {
                Vector3 enterPosition = GetGroundedPosition(GetApproachPosition(target));
                Vector3 toEnter = enterPosition - transform.position;
                toEnter.y = 0f;
                if (toEnter.magnitude > approachStartDistance)
                {
                    approachPath.Build(target, transform.position, enterPosition, capsule.radius, approachBodyMargin, approachCornerClearance);
                    int waypoint = 0;
                    float timer = 0f;
                    float stuckTimer = 0f;
                    Vector3 stuckCheckPosition = transform.position;

                    while (true)
                    {
                        if (IsTargetGone(target) || timer > approachTimeout || playerManager.CurrentState == PlayerState.Dead)
                        {
                            if (timer > approachTimeout)
                                Debug.LogWarning($"[{nameof(PlayerVehicleInteraction)}] Could not reach the enter point of '{(target as UnityEngine.Object)?.name}' in {approachTimeout}s; enter cancelled.", this);
                            approachRoutine = null;
                            CancelApproach();
                            yield break;
                        }

                        IReadOnlyList<Vector3> path = approachPath.Waypoints;
                        Vector3 position = transform.position;

                        // Skip ahead only when the next waypoint is already in clear view, so corners are rounded without cutting into the car.
                        while (waypoint < path.Count - 1 && approachPath.IsClear(position, path[waypoint + 1])) waypoint++;

                        Vector3 toWaypoint = path[waypoint] - position;
                        toWaypoint.y = 0f;
                        if (toWaypoint.magnitude <= approachArriveDistance)
                        {
                            if (waypoint >= path.Count - 1) break;
                            waypoint++;
                            continue;
                        }

                        SetMoveInput(toWaypoint.normalized);
                        if (approachWalk && approachJogDistance > 0f)
                        {
                            // Path length still to go: to the current waypoint, then waypoint to waypoint.
                            float remaining = toWaypoint.magnitude;
                            for (int w = waypoint; w < path.Count - 1; w++) remaining += Vector3.Distance(path[w], path[w + 1]);
                            controller.alwaysWalkByDefault = remaining <= approachJogDistance;
                        }

                        // Not making progress (e.g. snagged on a mirror or wheel): rebuild the path from here.
                        stuckTimer += Time.deltaTime;
                        if (stuckTimer >= approachStuckCheckInterval)
                        {
                            Vector3 moved = position - stuckCheckPosition;
                            moved.y = 0f;
                            if (moved.magnitude < approachStuckDistance)
                            {
                                approachPath.Build(target, position, enterPosition, capsule.radius, approachBodyMargin, approachCornerClearance);
                                waypoint = 0;
                            }
                            stuckTimer = 0f;
                            stuckCheckPosition = position;
                        }

                        timer += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            approachRoutine = null;
            if (IsTargetGone(target))
            {
                CancelApproach();
                yield break;
            }
            RestoreLocomotion();
            CommitEnter(target);
        }

        // The player is at the door of a ready vehicle: get the drivable vehicle (a traffic car is swapped only here) and start entering.
        private void CommitEnter(IVehicleInteractionTarget target)
        {
            IVehicleController vehicle = target.CompleteEntry();
            reservedTarget = null;
            if (vehicle == null || (vehicle as UnityEngine.Object) == null)
            {
                // CompleteEntry released its own reservation; only the player side needs undoing.
                Debug.LogWarning($"[{nameof(PlayerVehicleInteraction)}] '{(target as UnityEngine.Object)?.name}' could not be entered; enter cancelled.", this);
                if (State == VehicleInteractionState.Approaching)
                {
                    UnlockPlayer();
                    RestoreWeapon();
                    SetState(VehicleInteractionState.OnFoot);
                    OnVehicleApproachCancelled?.Invoke(target);
                }
                RefreshNearbyVehicles();
                return;
            }

            if (!ReferenceEquals(vehicle, target)) nearbyVehicles.Remove(target);
            CurrentVehicle = vehicle;
            vehicle.SetOccupied(true);

            TrafficVehicleDriver driver = vehicle.GetNpcDriver();
            if (driver != null && driver.HasLivingDriver && driver.CarjackStandPoint != null)
            {
                StartCarjack(vehicle, driver);
                return;
            }
            BeginEnter(vehicle, 0f);
        }

        private void CancelApproach()
        {
            if (approachRoutine != null)
            {
                StopCoroutine(approachRoutine);
                approachRoutine = null;
            }

            IVehicleInteractionTarget target = reservedTarget;
            reservedTarget = null;
            RestoreLocomotion();
            UnlockPlayer();
            RestoreWeapon();
            if (target != null && (target as UnityEngine.Object) != null) target.CancelEntryReservation();
            CurrentVehicle = null;

            SetState(VehicleInteractionState.OnFoot);
            OnVehicleApproachCancelled?.Invoke(target);
            RefreshNearbyVehicles();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Carjack: align at the driver's side of the door opening -> open the door -> reach in and pull the driver out
        // (hand IK on his collar while his pull-out animation plays, stepping back to make room) -> shove him away -> enter
        // clip from Carjack Enter Start Time. Empty vehicles never come here (normal enter).
        // ---------------------------------------------------------------------------------------------------------------

        private void StartCarjack(IVehicleController vehicle, TrafficVehicleDriver driver)
        {
            SetState(VehicleInteractionState.Carjacking);
            playerManager.SetInputLocked(true);
            if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(true);
            StowWeapon();
            controller.input = Vector3.zero;
            controller.lockMovement = true;
            controller.lockRotation = true;
            SetDoorClearance(vehicle, true);
            StartCoroutine(CarjackRoutine(vehicle, driver));
        }

        private IEnumerator CarjackRoutine(IVehicleController vehicle, TrafficVehicleDriver driver)
        {
            Transform standPoint = driver.CarjackStandPoint;
            Transform vehicleTransform = vehicle.transform;

            // 1. Short slide/turn onto the stand point (the approach already brought the character close). The character leans into
            //    the door opening, so collisions with the vehicle are ignored from here (as during the enter).
            SetVehicleCollisionIgnored(vehicle, true);
            yield return AlignRoutine(GetGroundedPosition(standPoint.position), FlatRotation(standPoint.forward));
            driver.PrepareForPullOut();

            // 2. Open the door (hand on the handle at Carjack Door Open Time).
            VehicleDoor door = GetUsableDriverDoor(vehicle);
            animator.CrossFadeInFixedTime(carjackOpenDoorHash, carjackCrossFade, BaseLayer, 0f);
            yield return WaitForState(carjackOpenDoorHash);
            animator.speed = carjackOpenDoorSpeed;
            bool doorCued = door == null;
            float elapsed = 0f;
            Vector3 standPosition = transform.position;
            while (stateWaitSucceeded && GetNormalizedTime(carjackOpenDoorHash) < carjackOpenDoorEndTime && elapsed < 3f)
            {
                if (!doorCued && GetNormalizedTime(carjackOpenDoorHash) >= carjackDoorOpenTime) doorCued = door.Open(carjackDoorOpenDuration);
                if (IsCarjackAborted(vehicle)) yield break;
                HoldPosition(standPosition);
                elapsed += Time.deltaTime;
                yield return null;
            }
            animator.speed = 1f;
            if (!doorCued && door != null) door.Open(carjackDoorOpenDuration);

            // 3. Reach in and grab (hand IK onto his chest), pull: the driver's pull-out starts once the hand is on him. The
            //    character steps back in step with the driver coming out, so the driver ends up in front of him, not in him.
            Vector3 pullFrom = transform.position;
            Vector3 pullTo = GetGroundedPosition(pullFrom + vehicleTransform.TransformVector(carjackStepBack));
            Transform grabHand = animator.GetBoneTransform(carjackGrabHand == AvatarIKGoal.RightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            animator.CrossFadeInFixedTime(carjackPullHash, carjackCrossFade, BaseLayer, 0f);
            if (vehicleIK != null && driver.GrabTarget != null) vehicleIK.SetHandTarget(carjackGrabHand, driver.GrabTarget);
            yield return WaitForState(carjackPullHash);
            animator.speed = carjackReachSpeed;
            bool pullStarted = false;
            float stepProgress = 0f;
            elapsed = 0f;
            float pullTimeout = driver.PullDuration + 3f;
            while (elapsed < pullTimeout)
            {
                if (IsCarjackAborted(vehicle)) yield break;
                if (!pullStarted)
                {
                    float pullTime = stateWaitSucceeded ? GetNormalizedTime(carjackPullHash) : 1f;
                    bool handOnDriver = grabHand != null && driver.GrabTarget != null
                                        && Vector3.Distance(grabHand.position, driver.GrabTarget.position) <= carjackGrabDistance;
                    if (pullTime >= carjackPullLatestTime || (pullTime >= carjackPullBeginTime && handOnDriver))
                    {
                        pullStarted = true;
                        animator.speed = 1f;
                        driver.BeginPullOut();
                    }
                }
                stepProgress = Mathf.Max(stepProgress, driver.PullProgress);
                HoldPosition(Vector3.Lerp(pullFrom, pullTo, Mathf.SmoothStep(0f, 1f, stepProgress)));
                // Keep facing the driver while pulling him past.
                if (pullStarted && driver.GrabTarget != null) TurnTowards(driver.GrabTarget.position, carjackTurnSpeed);
                // The shove starts while he is still stumbling out, so the hand lands as he gets his feet under him.
                if (pullStarted && (driver.PullProgress >= carjackShoveAtPullProgress || driver.IsPulledOut || !driver.IsEjecting)) break;
                elapsed += Time.deltaTime;
                yield return null;
            }


            // 4. Shove him away from the car (at his chest, toward the driver's authored shove direction); the stumble starts when the hand lands.
            if (driver.IsEjecting && driver.GrabTarget != null)
            {
                Vector3 toDriver = driver.GrabTarget.position - transform.position;
                toDriver.y = 0f;
                Vector3 shoveDirection = driver.ShoveDirection;
                Vector3 facing = toDriver.sqrMagnitude > 0.0001f ? Vector3.Slerp(toDriver.normalized, shoveDirection, carjackShoveFacingBlend) : shoveDirection;

                Quaternion from = transform.rotation;
                Quaternion to = FlatRotation(facing);
                animator.CrossFadeInFixedTime(carjackShoveHash, carjackCrossFade, BaseLayer, 0f);
                for (float t = 0f; t < carjackShoveTurnDuration; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / carjackShoveTurnDuration));
                    stepProgress = Mathf.Max(stepProgress, driver.PullProgress);
                    HoldPosition(Vector3.Lerp(pullFrom, pullTo, Mathf.SmoothStep(0f, 1f, stepProgress)));
                    yield return null;
                }
                transform.rotation = to;

                yield return WaitForState(carjackShoveHash);
                animator.speed = carjackShoveSpeed;
                bool shoved = false;
                elapsed = 0f;
                while (elapsed < 2f)
                {
                    if (IsCarjackAborted(vehicle)) yield break;
                    float normalizedTime = stateWaitSucceeded ? GetNormalizedTime(carjackShoveHash) : 1f;
                    if (!shoved && normalizedTime >= carjackShoveImpactTime)
                    {
                        shoved = true;
                        driver.Shove(shoveDirection);
                        if (vehicleIK != null) vehicleIK.SetHandTarget(carjackGrabHand, null);
                    }
                    if (normalizedTime >= carjackShoveEndTime) break;
                    stepProgress = Mathf.Max(stepProgress, driver.PullProgress);
                    HoldPosition(Vector3.Lerp(pullFrom, pullTo, Mathf.SmoothStep(0f, 1f, stepProgress)));
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                animator.speed = 1f;
                if (!shoved) driver.Shove(shoveDirection);
            }
            if (vehicleIK != null) vehicleIK.SetHandTarget(carjackGrabHand, null);

            // 5. Straight into the enter clip (from Carjack Enter Start Time: the door is already open), no walking.
            BeginEnter(vehicle, carjackEnterStartTime);
        }

        // The player died during the carjack: let the driver go and give the vehicle back.
        private bool IsCarjackAborted(IVehicleController vehicle)
        {
            bool vehicleGone = (vehicle as UnityEngine.Object) == null;
            if (!vehicleGone && playerManager.CurrentState != PlayerState.Dead) return false;

            if (!vehicleGone)
            {
                TrafficVehicleDriver driver = vehicle.GetNpcDriver();
                if (driver != null && driver.IsEjecting) driver.Shove(-vehicle.transform.right);
                SetVehicleCollisionIgnored(vehicle, false);
                SetDoorClearance(vehicle, false);
                vehicle.SetOccupied(false);
            }
            if (vehicleIK != null) vehicleIK.SetHandTarget(carjackGrabHand, null);
            animator.speed = 1f;
            controller.lockMovement = false;
            controller.lockRotation = false;
            UnlockPlayer();
            RestoreWeapon();
            CurrentVehicle = null;
            SetState(VehicleInteractionState.OnFoot);
            return true;
        }

        private IEnumerator AlignRoutine(Vector3 targetPosition, Quaternion targetRotation)
        {
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float turnDuration = Quaternion.Angle(startRotation, targetRotation) / 180f * alignTurnDurationPer180;
            float duration = Mathf.Clamp(Mathf.Max(Vector3.Distance(startPosition, targetPosition) / alignSpeed, turnDuration), 0.1f, maxAlignDuration);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.SetPositionAndRotation(Vector3.Lerp(startPosition, targetPosition, k), Quaternion.Slerp(startRotation, targetRotation, k));
                yield return null;
            }
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        // Keeps the character exactly where the carjack puts it: Invector turns clip root motion into rigidbody velocity, which
        // would otherwise carry the character away from the car during the carjack clips.
        private void HoldPosition(Vector3 position)
        {
            transform.position = position;
            body.position = position;
            body.linearVelocity = Vector3.zero;
        }

        private void TurnTowards(Vector3 point, float degreesPerSecond)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction.normalized, Vector3.up), degreesPerSecond * Time.deltaTime);
        }

        // Feeds Invector's movement input with a world direction (converted to the camera-relative input it expects).
        private void SetMoveInput(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                controller.input = Vector3.zero;
                return;
            }
            if (moveReference == null || controller.rotateByWorld)
            {
                controller.input = new Vector3(worldDirection.x, 0f, worldDirection.z);
                return;
            }

            Vector3 right = moveReference.right;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = Quaternion.AngleAxis(-90f, Vector3.up) * right;
            controller.input = new Vector3(Vector3.Dot(worldDirection, right), 0f, Vector3.Dot(worldDirection, forward));
        }

        // The enter sequence proper: from here the character is locked, collisions with the vehicle ignored and the clip plays.
        private void BeginEnter(IVehicleController vehicle, float clipStartTime)
        {
            SetState(VehicleInteractionState.Entering);
            OnVehicleEnterStarted?.Invoke(vehicle);

            LockPlayer();
            StowWeapon();
            controller.DisableGravityAndCollision();
            SetVehicleCollisionIgnored(vehicle, true);
            SetDoorClearance(vehicle, true);

            StartCoroutine(vehicle.UsesQuickMount ? QuickMountRoutine(vehicle) : EnterRoutine(vehicle, clipStartTime));
        }

        /// <summary>Starts exiting the current vehicle. Returns false (and raises OnExitBlocked) if not possible.</summary>
        public bool RequestExit()
        {
            if (State != VehicleInteractionState.InVehicle || CurrentVehicle == null) return false;
            var vehicle = CurrentVehicle;

            if (!vehicle.CanExit)
            {
                if (CanBail(vehicle))
                {
                    StartBail(vehicle);
                    return true;
                }
                OnExitBlocked?.Invoke(VehicleExitBlockReason.VehicleMoving);
                return false;
            }

            bool animated = TryGetClearExit(vehicle, vehicle.GetExitPoint(), out Vector3 exitPosition, out Quaternion exitRotation);
            if (!animated && !TryGetClearExit(vehicle, vehicle.GetAlternateExitPoint(), out exitPosition, out exitRotation))
            {
                OnExitBlocked?.Invoke(VehicleExitBlockReason.NoClearExit);
                return false;
            }

            SetState(VehicleInteractionState.Exiting);
            OnVehicleExitStarted?.Invoke(vehicle);

            // Vehicle camera off before the player camera comes back, so only one camera/AudioListener is ever active.
            vehicle.DisableControl();
            playerCameraRoot.SetActive(true);

            StartCoroutine(vehicle.UsesQuickMount ? QuickDismountRoutine(vehicle, exitPosition, exitRotation)
                : animated ? ExitRoutine(vehicle, exitPosition, exitRotation) : InstantExitRoutine(vehicle, exitPosition, exitRotation));
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Bail out: door flies open -> dive out through the door (moving with the vehicle) -> roll on the feet (slow) or tumble
        // as Invector's ragdoll with the vehicle's momentum (fast) -> Invector's get-up -> control back. The vehicle rolls on.
        // ---------------------------------------------------------------------------------------------------------------

        private bool CanBail(IVehicleController vehicle)
        {
            float kmh = vehicle.CurrentSpeed * 3.6f;
            return kmh >= bailMinSpeedKmh && (playerRagdoll != null || kmh <= bailRollMaxSpeedKmh);
        }

        private void StartBail(IVehicleController vehicle)
        {
            SetState(VehicleInteractionState.Bailing);
            // Raised while the vehicle camera is still on, so the player camera can blend from its view.
            OnVehicleBailStarted?.Invoke(vehicle);
            Rigidbody vehicleBody = ((Component)vehicle).GetComponent<Rigidbody>();
            float kmh = vehicle.CurrentSpeed * 3.6f;
            // No throttle, brake or handbrake: the vehicle rolls on on its own momentum (and parks once stopped).
            vehicle.ReleaseControlRolling();
            playerCameraRoot.SetActive(true);
            StartCoroutine(BailRoutine(vehicle, vehicleBody, kmh));
        }

        private IEnumerator BailRoutine(IVehicleController vehicle, Rigidbody vehicleBody, float kmh)
        {
            Transform car = vehicle.transform;
            Transform seat = vehicle.GetDriverSeat();
            bool tumble = playerRagdoll != null && kmh > bailRollMaxSpeedKmh;

            // 1. The character replaces the seated driver visual; the door flies open and he dives out through it, moving with
            //    the vehicle (collisions with it stay ignored).
            ShowCharacterAtSeat(vehicle);
            animator.Play(seatedStateHash, BaseLayer, 0f);
            animator.Update(0f);
            if (vehicleIK != null)
            {
                vehicleIK.SetSeatedLean(1f, 0f, true);
                vehicleIK.SetSeatedLean(0f, bailDiveDuration, false);
            }
            VehicleDoor door = GetUsableDriverDoor(vehicle);
            if (door != null) door.Open(bailDoorOpenDuration);
            animator.CrossFadeInFixedTime(bailDiveHash, bailDiveDuration, BaseLayer, 0f);

            Vector3 seatLocal = car.InverseTransformPoint(seat.position);
            Transform exitPoint = vehicle.GetExitPoint();
            float side = exitPoint != null && car.InverseTransformPoint(exitPoint.position).x < seatLocal.x ? -1f : 1f;
            Quaternion seatLocalRotation = Quaternion.Inverse(car.rotation) * seat.rotation;
            Vector3 diveLocal = seatLocal + Vector3.right * (side * bailDiveDistance);
            if (!tumble)
            {
                // Rolling: the dive ends on the ground beside the car.
                diveLocal.y = car.InverseTransformPoint(GetGroundedPosition(car.TransformPoint(diveLocal))).y;
            }
            Quaternion diveLocalRotation = Quaternion.LookRotation(new Vector3(side * Mathf.Sin(bailRollAwayAngle * Mathf.Deg2Rad), 0f, 1f));
            for (float t = 0f; t < bailDiveDuration; t += Time.deltaTime)
            {
                if ((vehicle as UnityEngine.Object) == null) break;
                float k = Mathf.SmoothStep(0f, 1f, t / bailDiveDuration);
                transform.SetPositionAndRotation(car.TransformPoint(Vector3.Lerp(seatLocal, diveLocal, k)),
                    car.rotation * Quaternion.Slerp(seatLocalRotation, diveLocalRotation, k));
                yield return null;
            }

            // 2. Off the vehicle: it now counts as abandoned (it rolls on by itself).
            bool vehicleAlive = (vehicle as UnityEngine.Object) != null;
            Vector3 sideDirection = vehicleAlive ? Vector3.ProjectOnPlane(car.right * side, Vector3.up).normalized : transform.right;
            Vector3 carVelocity = vehicleBody != null ? vehicleBody.GetPointVelocity(transform.position) : Vector3.zero;
            Vector3 carried = Vector3.ClampMagnitude(carVelocity * bailCarriedVelocity, bailMaxCarriedSpeed);
            if (vehicleAlive)
            {
                SetDoorClearance(vehicle, false);
                vehicle.SetOccupied(false);
            }
            if (vehicleIK != null) vehicleIK.End();
            transform.SetParent(originalParent, true);

            float timer = 0f;
            if (tumble)
            {
                // 3a. Invector's ragdoll through Invector's damage (speed-scaled, never lethal below Bail Lethal Speed).
                float high = Mathf.InverseLerp(bailMinSpeedKmh, bailFullSpeedKmh, kmh);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var damage = new vDamage(GetBailDamage(kmh), true)
                {
                    activeRagdoll = true,
                    senselessTime = Mathf.Lerp(bailMinGroundTime, bailMaxGroundTime, high),
                    ignoreDefense = true,
                    sender = car,
                    hitPosition = hips != null ? hips.position : transform.position,
                    force = carried,
                    damageType = "VehicleBail"
                };
                controller.TakeDamage(damage);
                if (!playerRagdoll.isActive) playerRagdoll.ActivateRagdoll(damage);
                // The ragdoll's limb colliders are live now: keep them clear of the vehicle for a moment.
                if (vehicleAlive) SetVehicleCollisionIgnored(vehicle, true);

                Vector3 velocity = carried + sideDirection * bailSideSpeed + Vector3.up * bailUpSpeed;
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                float spin = Mathf.Min(carried.magnitude * bailTumblePerSpeed, bailMaxTumble);
                Vector3 angular = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) * spin : Vector3.zero;
                ThrowPlayerRagdoll(velocity, angular, hips);

                // 4a. Tumble until Invector's ragdoll has settled, played the get-up and handed control back.
                bool collisionRestored = !vehicleAlive;
                while (timer < bailTimeout)
                {
                    timer += Time.deltaTime;
                    if (!collisionRestored && timer >= bailIgnoreVehicleTime && ((vehicle as UnityEngine.Object) == null || !OverlapsVehicle(vehicle)))
                    {
                        if ((vehicle as UnityEngine.Object) != null) SetVehicleCollisionIgnored(vehicle, false);
                        collisionRestored = true;
                    }
                    if (controller.isDead) break;
                    if (timer > 0.5f && !playerRagdoll.isActive && playerRagdoll.state == vRagdoll.RagdollState.animated && !controller.ragdolled) break;
                    yield return null;
                }
                if (!collisionRestored && (vehicle as UnityEngine.Object) != null) SetVehicleCollisionIgnored(vehicle, false);
                if (timer >= bailTimeout && playerRagdoll.isActive) playerRagdoll.RestoreRagdoll();
            }
            else
            {
                // 3b. A roll on the feet along the travel direction, turned away from the vehicle's path.
                Vector3 travel = new Vector3(carried.x, 0f, carried.z);
                if (travel.sqrMagnitude < 0.01f) travel = vehicleAlive ? car.forward : transform.forward;
                Vector3 rollDirection = Quaternion.AngleAxis(side * bailRollAwayAngle, Vector3.up) * travel.normalized;
                transform.SetPositionAndRotation(GetGroundedPosition(transform.position), FlatRotation(rollDirection));
                controller.EnableGravityAndCollision();
                if (vehicleAlive) SetVehicleCollisionIgnored(vehicle, true);
                controller.Roll();

                // 4b. Until the roll is over (Invector clears isRolling from the animator tag).
                bool collisionRestored = !vehicleAlive;
                while (timer < bailTimeout)
                {
                    timer += Time.deltaTime;
                    if (!collisionRestored && timer >= bailIgnoreVehicleTime && ((vehicle as UnityEngine.Object) == null || !OverlapsVehicle(vehicle)))
                    {
                        if ((vehicle as UnityEngine.Object) != null) SetVehicleCollisionIgnored(vehicle, false);
                        collisionRestored = true;
                    }
                    if (controller.isDead || (timer > 0.3f && !controller.isRolling && collisionRestored)) break;
                    yield return null;
                }
                if (!collisionRestored && (vehicle as UnityEngine.Object) != null) SetVehicleCollisionIgnored(vehicle, false);
            }

            // 5. Back on foot.
            if (transform.parent != originalParent) transform.SetParent(originalParent, true);
            if (!controller.ragdolled) controller.EnableGravityAndCollision();
            CurrentVehicle = null;
            UnlockPlayer();
            RestoreWeapon();
            SetState(VehicleInteractionState.OnFoot);
            OnVehicleExited?.Invoke(vehicle);
            RefreshNearbyVehicles();
        }

        private int GetBailDamage(float kmh)
        {
            float value = Mathf.Max(0f, kmh - bailDamageStartKmh) * bailDamagePerKmh;
            if (kmh < bailLethalSpeedKmh) value = Mathf.Min(value, controller.currentHealth - 1f);
            return Mathf.Max(0, Mathf.RoundToInt(value));
        }

        // Every limb gets the launch velocity plus the rigid-body rotation around the hips, so the whole body tumbles.
        private void ThrowPlayerRagdoll(Vector3 velocity, Vector3 angularVelocity, Transform hips)
        {
            Vector3 core = hips != null ? hips.position : transform.position;
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody limb = ragdollBodies[i];
                if (limb == null || limb.isKinematic) continue;
                limb.linearVelocity = velocity + Vector3.Cross(angularVelocity, limb.worldCenterOfMass - core);
                limb.angularVelocity = angularVelocity;
            }
        }

        private bool OverlapsVehicle(IVehicleController vehicle)
        {
            IReadOnlyList<Collider> vehicleColliders = vehicle.GetColliders();
            for (int p = 0; p < playerColliders.Length; p++)
            {
                Collider playerCollider = playerColliders[p];
                if (playerCollider == null || !playerCollider.enabled || playerCollider.isTrigger || !playerCollider.gameObject.activeInHierarchy) continue;
                Bounds bounds = playerCollider.bounds;
                for (int v = 0; v < vehicleColliders.Count; v++)
                {
                    Collider vehicleCollider = vehicleColliders[v];
                    if (vehicleCollider != null && vehicleCollider.enabled && !vehicleCollider.isTrigger && !(vehicleCollider is WheelCollider)
                        && vehicleCollider.bounds.Intersects(bounds)) return true;
                }
            }
            return false;
        }

        // clipStartTime > 0: continues a carjack (door already open, character at the door), played at the carjack speed.
        private IEnumerator EnterRoutine(IVehicleController vehicle, float clipStartTime)
        {
            Transform seat = vehicle.GetDriverSeat();
            Transform enterPoint = vehicle.GetEnterPoint();

            // 1. Short slide/turn onto the enter point (kinematic body, Invector movement/rotation locked). After a carjack too: the
            //    character stepped back from the door to pull the driver out, and the enter clip continues at its climb-in, whose
            //    seat warp lowers the root toward the (low) seat - started away from the door he would crouch down beside the car.
            bool afterCarjack = clipStartTime > 0f;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            Vector3 targetPosition = GetGroundedPosition(enterPoint.position);
            Quaternion targetRotation = FlatRotation(enterPoint.forward);
            float turnDuration = Quaternion.Angle(startRotation, targetRotation) / 180f * alignTurnDurationPer180;
            float alignDuration = Mathf.Clamp(Mathf.Max(Vector3.Distance(startPosition, targetPosition) / alignSpeed, turnDuration),
                afterCarjack ? carjackEnterTurnDuration : 0.1f, maxAlignDuration);
            // The enter clip blends in while the character turns/slides onto the enter point (no standing still at the door).
            // A carjack continues part-way through, where the door is already open; a normal enter skips the idle lead-in.
            {
                if (vehicleIK != null) vehicleIK.Begin(vehicle, PlayerVehicleIK.Clip.Enter, targetPosition.y, false);
                float currentLength = Mathf.Max(0.1f, animator.GetCurrentAnimatorStateInfo(BaseLayer).length);
                animator.CrossFade(enterStateHash, enterCrossFade / currentLength, BaseLayer, afterCarjack ? clipStartTime : enterStartTime);
                animator.speed = afterCarjack ? carjackEnterAnimationSpeed : enterAnimationSpeed;
            }
            // The door cues already run while the character lines up (the clip is playing), so the door never waits.
            VehicleDoor door = GetUsableDriverDoor(vehicle);
            int doorCue = DoorCueNone;
            // A normal enter takes the handle: the hand goes onto it and rides with the door while it is pulled open.
            bool holdingHandle = !afterCarjack && door != null && door.Handle != null && vehicleIK != null && !door.IsOpen;
            Transform[] handleArm = GetArmBones(enterHandleHand);
            bool handOnHandle = false;
            bool climbStarted = false;
            if (holdingHandle)
            {
                foreach (Transform bone in handleArm) door.SetClearanceIgnored(bone, true);
            }
            for (float t = 0f; t < alignDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / alignDuration);
                transform.SetPositionAndRotation(Vector3.Lerp(startPosition, targetPosition, k), Quaternion.Slerp(startRotation, targetRotation, k));
                AnimatorStateInfo enterInfo = animator.GetCurrentAnimatorStateInfo(BaseLayer);
                AnimatorStateInfo nextInfo = animator.GetNextAnimatorStateInfo(BaseLayer);
                float clipTime = enterInfo.shortNameHash == enterStateHash ? enterInfo.normalizedTime
                    : nextInfo.shortNameHash == enterStateHash ? nextInfo.normalizedTime : 0f;
                handOnHandle = UpdateHandleReach(clipTime, holdingHandle, handOnHandle, handleArm, door);
                doorCue = AdvanceDoorCue(door, clipTime, EnterDoorOpenTime(clipTime, holdingHandle, handleArm, door), enterDoorCloseTime, enterDoorOpenDuration, enterDoorCloseDuration, doorCue);
                climbStarted = AdvanceClimbCue(vehicle, clipTime, climbStarted);
                yield return null;
            }
            transform.SetPositionAndRotation(targetPosition, targetRotation);

            // 2. The door follows the hand at the configured clip times.
            yield return WaitForState(enterStateHash);
            TrafficVehicleDriver npcDriver = vehicle.GetNpcDriver();
            bool driverPulled = npcDriver == null || !npcDriver.HasLivingDriver;
            if (!stateWaitSucceeded)
            {
                Debug.LogError($"[{nameof(PlayerVehicleInteraction)}] Animator state '{enterStateName}' not found/started; snapping into the seat (fallback).", this);
                if (!driverPulled) npcDriver.Eject();
                SnapTo(seat.position, seat.rotation);
            }
            else
            {
                float elapsed = 0f;
                float normalizedTime;
                animator.speed = !driverPulled || clipStartTime > 0f ? carjackEnterAnimationSpeed : enterAnimationSpeed;
                while ((normalizedTime = GetNormalizedTime(enterStateHash)) < enterSeatedTime && elapsed < sequenceTimeout)
                {
                    climbStarted = AdvanceClimbCue(vehicle, normalizedTime, climbStarted);
                    controller.MatchTarget(seat.TransformPoint(enterEndSeatOffset), seat.rotation, AvatarTarget.Root,
                        new MatchTargetWeightMask(Vector3.one, 1f), enterMatchStart, enterMatchEnd);
                    handOnHandle = UpdateHandleReach(normalizedTime, holdingHandle, handOnHandle, handleArm, door);
                    doorCue = AdvanceDoorCue(door, normalizedTime, EnterDoorOpenTime(normalizedTime, holdingHandle, handleArm, door), enterDoorCloseTime, enterDoorOpenDuration, enterDoorCloseDuration, doorCue);
                    if (holdingHandle && normalizedTime >= enterHandleReleaseTime)
                    {
                        holdingHandle = false;
                        vehicleIK.SetHandTarget(enterHandleHand, null);
                        foreach (Transform bone in handleArm) door.SetClearanceIgnored(bone, false);
                    }
                    // Carjacking: the driver is pulled out while the door swings open, before the character climbs in.
                    if (!driverPulled && (normalizedTime >= enterMatchStart || (door != null
                            ? door.OpenAngle >= door.MaxOpenAngle * enterDriverPullDoorShare
                            : normalizedTime >= enterDriverPullTime)))
                    {
                        npcDriver.Eject();
                        driverPulled = true;
                    }
                    if (vehicleIK != null) vehicleIK.SetClipTime(normalizedTime);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (!driverPulled) npcDriver.Eject();
                animator.speed = 1f;
                if (holdingHandle)
                {
                    vehicleIK.SetHandTarget(enterHandleHand, null);
                    foreach (Transform bone in handleArm) door.SetClearanceIgnored(bone, false);
                }

                // 3. Settle onto the seat while blending into the seated pose and reclining onto the seat back (the vehicle's
                //    driver visual sits reclined, so the swap at the end is seamless).
                transform.SetParent(seat, true);
                if (vehicleIK != null)
                {
                    vehicleIK.SetClipTime(1f);
                    vehicleIK.SetSeatedLean(1f, seatSettleDuration, false);
                }
                animator.CrossFadeInFixedTime(seatedStateHash, seatSettleDuration, BaseLayer, 0f);
                Vector3 fromPosition = transform.localPosition;
                Quaternion fromRotation = transform.localRotation;
                for (float t = 0f; t < seatSettleDuration; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / seatSettleDuration);
                    transform.localPosition = Vector3.Lerp(fromPosition, Vector3.zero, k);
                    transform.localRotation = Quaternion.Slerp(fromRotation, Quaternion.identity, k);
                    doorCue = AdvanceDoorCue(door, 1f, enterDoorOpenTime, enterDoorCloseTime, enterDoorOpenDuration, enterDoorCloseDuration, doorCue);
                    yield return null;
                }
            }

            // 4. Vehicle control as soon as the character is seated; the door finishes closing on its own.
            VehicleDoor closingDoor = GetUsableDriverDoor(vehicle);
            if (closingDoor != null && closingDoor.IsOpen && !closingDoor.IsMoving) closingDoor.Close(enterDoorCloseDuration);
            FinishSeating(vehicle);
        }

        private IEnumerator ExitRoutine(IVehicleController vehicle, Vector3 exitPosition, Quaternion exitRotation)
        {
            // Starts on the exact seated pose the driver visual showed, so the swap is seamless.
            // The door is still closed; the clip opens it at exitDoorOpenTime.
            ShowCharacterAtSeat(vehicle);
            SetDoorClearance(vehicle, true);
            // IK starts before the first exit frame is evaluated so the feet already rest on the cabin floor like the driver visual,
            // and the torso starts reclined like it and sits up while the character turns toward the door.
            if (vehicleIK != null)
            {
                vehicleIK.Begin(vehicle, PlayerVehicleIK.Clip.Exit, exitPosition.y, true);
                vehicleIK.SetSeatedLean(1f, 0f, true);
                vehicleIK.SetSeatedLean(0f, exitLeanRelease, false);
            }
            animator.Play(exitStateHash, BaseLayer, 0f);
            animator.Update(0f);
            animator.speed = exitAnimationSpeed;

            yield return WaitForState(exitStateHash);
            if (!stateWaitSucceeded)
            {
                Debug.LogError($"[{nameof(PlayerVehicleInteraction)}] Animator state '{exitStateName}' not found/started; snapping to the exit point (fallback).", this);
                PlaceOnFoot(exitPosition, exitRotation);
            }
            else
            {
                VehicleDoor door = GetUsableDriverDoor(vehicle);
                int doorCue = DoorCueNone;
                float elapsed = 0f;
                float normalizedTime;
                while ((normalizedTime = GetNormalizedTime(exitStateHash)) < exitCompleteTime && elapsed < sequenceTimeout)
                {
                    // Height first (while standing up), then position/facing, so the character never walks out crouched below the ground.
                    if (normalizedTime < exitRiseEnd)
                    {
                        controller.MatchTarget(exitPosition, exitRotation, AvatarTarget.Root,
                            new MatchTargetWeightMask(Vector3.up, 0f), exitRiseStart, exitRiseEnd);
                    }
                    else
                    {
                        controller.MatchTarget(exitPosition, exitRotation, AvatarTarget.Root,
                            new MatchTargetWeightMask(Vector3.one, 1f), Mathf.Max(exitMatchStart, exitRiseEnd), exitMatchEnd);
                    }
                    int previousCue = doorCue;
                    doorCue = AdvanceDoorCue(door, normalizedTime, exitDoorOpenTime, exitDoorCloseTime, exitDoorOpenDuration, exitDoorCloseDuration, doorCue);
                    // The exit clip swings the door shut and then moves the arm back out past it (above/behind the closing
                    // panel), so from the close cue on the door follows the clip's timing instead of waiting for the arm.
                    if (doorCue == DoorCueClosed && previousCue != DoorCueClosed) SetDoorClearance(vehicle, false);
                    if (vehicleIK != null) vehicleIK.SetClipTime(normalizedTime);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                if (elapsed >= sequenceTimeout) SnapTo(exitPosition, exitRotation);
            }
            animator.speed = 1f;

            // Door closes behind the character before on-foot control returns.
            yield return OperateDriverDoor(vehicle, false, exitDoorCloseDuration);
            CompleteExit(vehicle);
        }

        // Bikes: line up beside the bike, then swing onto the seat in a short arc while blending into the seated pose. No door,
        // no enter clip; the bike's rider visual takes over once seated (FinishSeating).
        private IEnumerator QuickMountRoutine(IVehicleController vehicle)
        {
            Transform seat = vehicle.GetDriverSeat();
            Transform enterPoint = vehicle.GetEnterPoint();
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            Vector3 standPosition = GetGroundedPosition(enterPoint.position);
            Quaternion standRotation = FlatRotation(enterPoint.forward);
            float alignDuration = Mathf.Clamp(Vector3.Distance(startPosition, standPosition) / alignSpeed, 0.1f, maxAlignDuration);
            for (float t = 0f; t < alignDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / alignDuration);
                transform.SetPositionAndRotation(Vector3.Lerp(startPosition, standPosition, k), Quaternion.Slerp(startRotation, standRotation, k));
                yield return null;
            }

            OnVehicleClimbStarted?.Invoke(vehicle);
            animator.CrossFadeInFixedTime(seatedStateHash, quickMountDuration, BaseLayer, 0f);
            transform.SetParent(seat, true);
            Vector3 fromPosition = transform.localPosition;
            Quaternion fromRotation = transform.localRotation;
            for (float t = 0f; t < quickMountDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / quickMountDuration);
                Vector3 local = Vector3.Lerp(fromPosition, Vector3.zero, k);
                // The arc is in world up so the leg clears the seat whatever the bike's lean.
                transform.localPosition = local + seat.InverseTransformVector(Vector3.up) * (quickMountHop * Mathf.Sin(k * Mathf.PI));
                transform.localRotation = Quaternion.Slerp(fromRotation, Quaternion.identity, k);
                yield return null;
            }
            FinishSeating(vehicle);
        }

        // Bikes: step off sideways to the exit point in a short arc while blending back into on-foot locomotion.
        private IEnumerator QuickDismountRoutine(IVehicleController vehicle, Vector3 exitPosition, Quaternion exitRotation)
        {
            ShowCharacterAtSeat(vehicle);
            animator.Play(seatedStateHash, BaseLayer, 0f);
            animator.Update(0f);
            animator.CrossFadeInFixedTime(onFootStateHash, quickMountDuration, BaseLayer, 0f);
            Vector3 fromPosition = transform.position;
            Quaternion fromRotation = transform.rotation;
            for (float t = 0f; t < quickMountDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / quickMountDuration);
                transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, exitPosition, k) + Vector3.up * (quickMountHop * Mathf.Sin(k * Mathf.PI)),
                    Quaternion.Slerp(fromRotation, exitRotation, k));
                yield return null;
            }
            SnapTo(exitPosition, exitRotation);
            CompleteExit(vehicle);
        }

        // Documented fallback: used when only the alternate exit is clear (the exit clip is driver-side only).
        // The character is placed directly at the exit point; the driver door is not used.
        private IEnumerator InstantExitRoutine(IVehicleController vehicle, Vector3 exitPosition, Quaternion exitRotation)
        {
            ShowCharacterAtSeat(vehicle);
            PlaceOnFoot(exitPosition, exitRotation);
            yield return null;
            CompleteExit(vehicle);
        }

        private void PlaceOnFoot(Vector3 position, Quaternion rotation)
        {
            SnapTo(position, rotation);
            animator.Play(onFootStateHash, BaseLayer, 0f);
            animator.Update(0f);
        }

        // The hand is guided onto the handle once it is on its way there (close enough, or late enough in the clip), so the arm
        // is not stretched toward it from a distance.
        private bool UpdateHandleReach(float clipTime, bool holdingHandle, bool handOnHandle, Transform[] handleArm, VehicleDoor door)
        {
            if (!holdingHandle || handOnHandle || handleArm.Length == 0 || handleArm[0] == null) return handOnHandle;
            if (clipTime < enterHandleReachTime && Vector3.Distance(handleArm[0].position, door.Handle.position) > enterHandleReachDistance) return false;
            vehicleIK.SetHandTarget(enterHandleHand, door.Handle, 0f);
            return true;
        }

        // With the hand going for the handle, the door opens when the hand gets there (or at the latest time); otherwise at the
        // configured clip time.
        private float EnterDoorOpenTime(float clipTime, bool holdingHandle, Transform[] handleArm, VehicleDoor door)
        {
            if (!holdingHandle || handleArm.Length == 0 || handleArm[0] == null) return enterDoorOpenTime;
            if (clipTime >= enterHandleLatestTime) return 0f;
            return Vector3.Distance(handleArm[0].position, door.Handle.position) <= enterHandleGrabDistance ? 0f : 2f;
        }

        // Hand, finger and forearm of one arm (the ones registered for the door clearance).
        private Transform[] GetArmBones(AvatarIKGoal goal)
        {
            if (!animator.isHuman) return new Transform[0];
            bool right = goal == AvatarIKGoal.RightHand;
            return new[]
            {
                animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand),
                animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleIntermediate : HumanBodyBones.LeftMiddleIntermediate),
                animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm)
            };
        }

        // Hands, fingers and forearms the driver door must not pass through while getting in or out.
        private Transform[] GetDoorClearanceBones()
        {
            if (!animator.isHuman) return new Transform[0];
            HumanBodyBones[] bones =
            {
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate
            };
            var result = new List<Transform>(bones.Length);
            foreach (HumanBodyBones bone in bones)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null) result.Add(t);
            }
            return result.ToArray();
        }

        private void SetDoorClearance(IVehicleController vehicle, bool active)
        {
            if ((vehicle as UnityEngine.Object) == null) return;
            VehicleDoor door = vehicle.GetDriverDoor();
            if (door != null) door.SetClearanceBones(active ? doorClearanceBones : null);
        }

        private static VehicleDoor GetUsableDriverDoor(IVehicleController vehicle)
        {
            VehicleDoor door = vehicle.GetDriverDoor();
            return door != null && door.IsAvailable ? door : null;
        }

        // Clip-timed door cue: opens once the clip passes openTime, closes once it passes closeTime. A request the door
        // refuses (still moving) is retried next frame, so a close cue waits for the opening swing to finish.
        // Raises OnVehicleClimbStarted once the enter clip reaches the climb-in time; returns whether it has been raised.
        private bool AdvanceClimbCue(IVehicleController vehicle, float normalizedTime, bool started)
        {
            if (started || normalizedTime < enterClimbTime) return started;
            OnVehicleClimbStarted?.Invoke(vehicle);
            return true;
        }

        private static int AdvanceDoorCue(VehicleDoor door, float normalizedTime, float openTime, float closeTime,
            float openDuration, float closeDuration, int cue)
        {
            if (door == null || !door.IsAvailable) return cue;
            if (cue == DoorCueNone && normalizedTime >= openTime && door.Open(openDuration)) cue = DoorCueOpened;
            if (cue == DoorCueOpened && normalizedTime >= closeTime && door.Close(closeDuration)) cue = DoorCueClosed;
            return cue;
        }

        // Opens/closes the vehicle's driver door (if it has a usable one) and waits until it has finished moving.
        private IEnumerator OperateDriverDoor(IVehicleController vehicle, bool open, float duration)
        {
            VehicleDoor door = vehicle.GetDriverDoor();
            if (door == null || !door.IsAvailable) yield break;

            float timer = 0f;
            while (door != null && door.IsMoving && timer < DoorTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            if (door == null) yield break;

            bool started = open ? door.Open(duration) : door.Close(duration);
            while (started && door != null && door.IsMoving && timer < DoorTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }
        }

        // Waits until the state is current and not blending; stateWaitSucceeded reports whether it started in time.
        private IEnumerator WaitForState(int stateHash)
        {
            stateWaitSucceeded = false;
            float timer = 0f;
            while (!IsCurrentState(stateHash) || animator.IsInTransition(BaseLayer))
            {
                timer += Time.deltaTime;
                if (timer > StateStartTimeout) yield break;
                yield return null;
            }
            stateWaitSucceeded = true;
        }

        private void FinishSeating(IVehicleController vehicle)
        {
            SetDoorClearance(vehicle, false);
            if (vehicleIK != null) vehicleIK.End();
            Transform seat = vehicle.GetDriverSeat();
            if (transform.parent != seat) transform.SetParent(seat, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Hide the real character; the vehicle shows its static seated driver visual instead.
            vehicle.SetDriverVisualActive(true);
            SetSeatedComponentsActive(false);

            // Player camera off before the vehicle camera turns on (one AudioListener at a time).
            playerCameraRoot.SetActive(false);
            vehicle.EnableControl();

            SetState(VehicleInteractionState.InVehicle);
            OnVehicleEntered?.Invoke(vehicle);
        }

        private void ShowCharacterAtSeat(IVehicleController vehicle)
        {
            Transform seat = vehicle.GetDriverSeat();
            transform.SetParent(originalParent, true);
            transform.SetPositionAndRotation(seat.position, seat.rotation);

            SetSeatedComponentsActive(true);
            vehicle.SetDriverVisualActive(false);
            // Re-activated colliders lose their ignore pairs, so apply them again for the exit animation.
            SetVehicleCollisionIgnored(vehicle, true);
        }

        private void CompleteExit(IVehicleController vehicle)
        {
            if (vehicleIK != null) vehicleIK.SetSeatedLean(0f, 0f, true);
            SetDoorClearance(vehicle, false);
            if (vehicleIK != null) vehicleIK.FadeOut();
            SetVehicleCollisionIgnored(vehicle, false);
            controller.EnableGravityAndCollision();
            vehicle.SetOccupied(false);
            CurrentVehicle = null;

            UnlockPlayer();
            RestoreWeapon();

            SetState(VehicleInteractionState.OnFoot);
            OnVehicleExited?.Invoke(vehicle);
            RefreshNearbyVehicles();
        }

        private void SetSeatedComponentsActive(bool active)
        {
            if (active)
            {
                capsule.enabled = true;
                characterModel.SetActive(true);
                animator.enabled = true;
                SetBehavioursEnabled(true);
            }
            else
            {
                SetBehavioursEnabled(false);
                animator.enabled = false;
                characterModel.SetActive(false);
                capsule.enabled = false;
            }
        }

        private void SetBehavioursEnabled(bool value)
        {
            for (int i = 0; i < disabledWhileSeated.Length; i++)
            {
                if (disabledWhileSeated[i] != null) disabledWhileSeated[i].enabled = value;
            }
        }

        private void LockPlayer()
        {
            playerManager.SetInVehicle(true);
            playerManager.SetInputLocked(true);
            if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(true);
            controller.lockMovement = true;
            controller.lockRotation = true;
        }

        private void UnlockPlayer()
        {
            controller.lockMovement = false;
            controller.lockRotation = false;
            if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(false);
            playerManager.SetInputLocked(false);
            playerManager.SetInVehicle(false);
        }

        // Safe to call twice (approach, then enter): the slot saved by the first call is kept.
        private void StowWeapon()
        {
            if (weaponStowed) return;
            weaponStowed = true;
            savedWeaponSlot = weaponController.HasWeapon ? weaponController.GetCurrentSlotIndex() : -1;
            if (savedWeaponSlot >= 0) weaponController.Unarm(true);
        }

        private void RestoreWeapon()
        {
            if (savedWeaponSlot >= 0) weaponController.EquipSlot(savedWeaponSlot);
            savedWeaponSlot = -1;
            weaponStowed = false;
        }

        private void SetVehicleCollisionIgnored(IVehicleController vehicle, bool ignore)
        {
            IReadOnlyList<Collider> vehicleColliders = vehicle.GetColliders();
            for (int p = 0; p < playerColliders.Length; p++)
            {
                Collider playerCollider = playerColliders[p];
                if (playerCollider == null || !playerCollider.enabled || !playerCollider.gameObject.activeInHierarchy) continue;
                for (int v = 0; v < vehicleColliders.Count; v++)
                {
                    Collider vehicleCollider = vehicleColliders[v];
                    if (vehicleCollider != null && vehicleCollider.gameObject.activeInHierarchy) Physics.IgnoreCollision(playerCollider, vehicleCollider, ignore);
                }
            }
        }

        private bool TryGetClearExit(IVehicleController vehicle, Transform point, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (point == null) return false;
            if (!Physics.Raycast(point.position + Vector3.up * groundProbeHeight, Vector3.down, out RaycastHit hit,
                    groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore)) return false;

            position = hit.point;
            rotation = FlatRotation(point.forward);

            Vector3 bottom = position + Vector3.up * (exitClearRadius + 0.05f);
            Vector3 top = position + Vector3.up * Mathf.Max(exitClearHeight - exitClearRadius, exitClearRadius + 0.05f);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, exitClearRadius, overlapResults, exitBlockingLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform other = overlapResults[i].transform;
                if (other.IsChildOf(vehicle.transform) || other.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        private Vector3 GetGroundedPosition(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * groundProbeHeight, Vector3.down, out RaycastHit hit,
                groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore) ? hit.point : point;
        }

        private static Quaternion FlatRotation(Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
        }

        private void SnapTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        private bool IsCurrentState(int stateHash) => animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash == stateHash;

        private float GetNormalizedTime(int stateHash)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(BaseLayer);
            return info.shortNameHash == stateHash ? info.normalizedTime : 1f;
        }

        private bool CanActOnFoot()
        {
            return !playerManager.IsInputLocked && playerManager.CurrentState != PlayerState.Dead;
        }

        private void UpdateFocus()
        {
            if (nearbyVehicles.Count == 0)
            {
                if (FocusedVehicle != null) SetFocus(null);
                return;
            }

            IVehicleInteractionTarget best = null;
            float bestSqr = float.MaxValue;
            Vector3 position = transform.position;
            for (int i = nearbyVehicles.Count - 1; i >= 0; i--)
            {
                var vehicle = nearbyVehicles[i];
                // Destroyed vehicles never send OnTriggerExit - drop them here.
                if (vehicle == null || (vehicle as UnityEngine.Object) == null)
                {
                    nearbyVehicles.RemoveAt(i);
                    continue;
                }
                if (!vehicle.CanEnter || !IsAtDriverDoor(vehicle, position)) continue;

                float sqr = (vehicle.GetInteractionPoint().position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = vehicle;
                }
            }
            SetFocus(best);
        }

        // On the driver's side of the vehicle and close to its driver door; getting in from the opposite side is not offered.
        private bool IsAtDriverDoor(IVehicleInteractionTarget vehicle, Vector3 position)
        {
            Transform door = GetApproachGoal(vehicle);
            if (door == null) return false;
            Vector3 toDoor = door.position - position;
            toDoor.y = 0f;
            if (toDoor.sqrMagnitude > driverDoorRange * driverDoorRange) return false;
            Transform car = vehicle.transform;
            float doorSide = car.InverseTransformPoint(door.position).x;
            return Mathf.Sign(car.InverseTransformPoint(position).x) == Mathf.Sign(doorSide);
        }

        // One-shot re-seed after exiting: trigger enter events are not guaranteed while collisions were ignored.
        private void RefreshNearbyVehicles()
        {
            nearbyVehicles.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * capsule.height * 0.5f, capsule.radius,
                overlapResults, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                if (!overlapResults[i].isTrigger) continue;
                var vehicle = overlapResults[i].GetComponentInParent<IVehicleInteractionTarget>();
                if (vehicle != null && !nearbyVehicles.Contains(vehicle)) nearbyVehicles.Add(vehicle);
            }
        }

        private void SetFocus(IVehicleInteractionTarget vehicle)
        {
            if (ReferenceEquals(vehicle, FocusedVehicle)) return;
            FocusedVehicle = vehicle;
            OnVehicleFocusChanged?.Invoke(vehicle);
        }

        private void SetState(VehicleInteractionState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(newState);
        }
    }
}
