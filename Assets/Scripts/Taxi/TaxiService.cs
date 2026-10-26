using System;
using System.Collections;
using System.Collections.Generic;
using Invector.vCamera;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

public enum TaxiState
{
    /// <summary>No taxi called.</summary>
    Idle,
    /// <summary>A taxi is driving to the player.</summary>
    EnRoute,
    /// <summary>The taxi has pulled over beside the player.</summary>
    Waiting,
    /// <summary>The player got in and is picking a destination.</summary>
    ChoosingDestination,
    /// <summary>The player is riding to the chosen destination.</summary>
    Riding
}

/// <summary>
/// Taxi fast travel (scene service, not a Singleton).
/// On foot, E (when nothing else is focused for interaction) makes the player whistle: the best-placed iTS traffic car
/// (preferably one the camera cannot see) leaves traffic, drives along real lanes to the kerb beside the player and waits.
/// E next to it gets in; the player picks a <see cref="TaxiDestination"/>, the taxi drives there (Space skips, Esc
/// gets out early) and drops the player off. The car is then handed back to iTS traffic.
/// UI is not driven from here: <see cref="TaxiHUD"/> listens to the events below.
/// </summary>
[DisallowMultipleComponent]
public class TaxiService : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private TSMainManager roads;
    [SerializeField] private TSTrafficSpawner traffic;
    [Tooltip("The player's Main Camera (used for the ride camera and to prefer taxis coming from out of view).")]
    [SerializeField] private Camera viewCamera;
    [Tooltip("Stops offered when the player gets in.")]
    [SerializeField] private TaxiDestination[] destinations = new TaxiDestination[0];

    [Header("Player")]
    [SerializeField] private OpenworldCrime.Player.PlayerManager playerManager; // qualified: SharedAssets has a global PlayerManager
    [SerializeField] private PlayerInteractionController interactionController;
    [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
    [SerializeField] private vThirdPersonInput playerInput;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private Rigidbody playerBody;
    [Tooltip("Optional: re-aimed behind the player after a ride.")]
    [SerializeField] private vThirdPersonCamera thirdPersonCamera;

    [Header("Input")]
    [Tooltip("Whistle for a taxi / get into the waiting taxi. Only used while no interactable is focused.")]
    [SerializeField] private GenericInput callInput = new GenericInput("E", "", "");
    [SerializeField] private GenericInput skipRideInput = new GenericInput("Space", "", "");
    [Tooltip("Closes the destination list, or gets out early during a ride.")]
    [SerializeField] private GenericInput cancelInput = new GenericInput("Escape", "", "");
    [SerializeField] private GenericInput cameraViewInput = new GenericInput("C", "", "");

    [Header("Whistle")]
    [Tooltip("Animator state played on the player (Base Layer). Swap its motion for a real whistle clip in the animator.")]
    [SerializeField] private string whistleStateName = "Taxi_Whistle";
    [SerializeField] private int whistleLayer = 0;
    [SerializeField, Min(0f)] private float whistleCrossFade = 0.15f;
    [SerializeField] private AudioSource whistleSource;
    [Tooltip("Whistle sound. Empty = a short generated two-note whistle is used.")]
    [SerializeField] private AudioClip whistleClip;
    [SerializeField, Min(0f)] private float whistleCooldown = 1.5f;

    [Header("Taxi Cars")]
    [Tooltip("Traffic car prefabs preferred as taxis (matched by name). Empty = any traffic car.")]
    [SerializeField] private TSTrafficAI[] preferredTaxiModels = new TSTrafficAI[0];
    [Tooltip("Use any traffic car when none of the preferred models can reach the player.")]
    [SerializeField] private bool fallbackToAnyTrafficCar = true;
    [Tooltip("Roof sign + iTS sensor target put on the car that answers the call (one instance, reused).")]
    [SerializeField] private TaxiMarker taxiMarkerPrefab;
    [SerializeField, Min(10f)] private float searchRadius = 180f;
    [Tooltip("Longest drive a taxi will make to reach the player, in metres.")]
    [SerializeField, Min(10f)] private float maxRouteLength = 450f;
    [Tooltip("Extra route metres counted for a car the camera can see, so taxis come from out of view when possible.")]
    [SerializeField, Min(0f)] private float visibleCarPenalty = 200f;
    [SerializeField, Min(1f)] private float cruiseSpeed = 11f;
    [Tooltip("The iTS lanes run on the left side of the road (kerb on the lane's left). Off = right-hand traffic.")]
    [SerializeField] private bool driveOnLeft = true;
    [Tooltip("Ground rise (m) that counts as the kerb when looking sideways from a lane.")]
    [SerializeField, Min(0.01f)] private float kerbStepHeight = 0.04f;
    [Tooltip("How far sideways from a lane the kerb is searched for, in metres.")]
    [SerializeField, Min(1f)] private float kerbSearchDistance = 8f;
    [Tooltip("A lane counts as the kerb lane when the kerb is within this distance of it, in metres.")]
    [SerializeField, Min(1f)] private float kerbLaneMaxDistance = 6f;
    [Tooltip("Gap kept between the taxi's centre and the kerb when it pulls over (about half a car width plus a little).")]
    [SerializeField, Min(0f)] private float kerbClearance = 1.25f;
    [Tooltip("Pull-over distance from the lane centre when no kerb is found, in metres.")]
    [SerializeField, Min(0f)] private float pullOverOffset = 1.4f;
    [Tooltip("Metres over which the taxi eases from its lane to the kerb before stopping.")]
    [SerializeField, Min(1f)] private float pullInDistance = 25f;
    [Tooltip("Lanes this close to the player are considered for the pickup, in metres.")]
    [SerializeField, Min(5f)] private float pickupSearchRadius = 25f;
    [Tooltip("Extra route metres a pickup across the road from the player counts as.")]
    [SerializeField, Min(0f)] private float crossRoadPenalty = 250f;
    [Tooltip("No new traffic cars appear within this distance of a called taxi, in metres.")]
    [SerializeField, Min(0f)] private float taxiNoSpawnRadius = 50f;

    [Header("Pickup")]
    [Tooltip("If the player moves this far from where the taxi is heading, it re-plans to their new spot.")]
    [SerializeField, Min(1f)] private float replanDistance = 8f;
    [SerializeField, Min(0.25f)] private float replanInterval = 1.5f;
    [Tooltip("The taxi leaves once the player is this much further away than the closest it got, for Leave After Seconds.")]
    [SerializeField, Min(0f)] private float walkAwayMargin = 40f;
    [SerializeField, Min(0f)] private float leaveAfterSeconds = 6f;
    [Tooltip("A waiting taxi leaves after this many seconds.")]
    [SerializeField, Min(1f)] private float waitSeconds = 60f;
    [SerializeField, Min(1f)] private float abandonDistance = 60f;

    [Header("Ride")]
    [Tooltip("How close the player must be to the taxi to get in, in metres.")]
    [SerializeField, Min(1f)] private float boardDistance = 6f;
    [Tooltip("The taxi can be boarded while still rolling, as long as it is slower than this (m/s).")]
    [SerializeField, Min(0f)] private float maxBoardingSpeed = 6f;
    [SerializeField, Min(1f)] private float rideCruiseSpeed = 13f;
    [Tooltip("Longest drive a taxi ride can plan, in metres (longer trips are instant).")]
    [SerializeField, Min(10f)] private float rideMaxRouteLength = 6000f;
    [Tooltip("How far from the destination the taxi may stop, in metres.")]
    [SerializeField, Min(5f)] private float destinationSearchRadius = 45f;
    [Tooltip("Sideways distance from the taxi's centre to where the passenger steps out (kerb side).")]
    [SerializeField, Min(1f)] private float stepOutOffset = 2.6f;
    [Tooltip("Road/pavement layers: kerb detection, drop-off placement and ride camera collision.")]
    [SerializeField] private LayerMask groundLayers = 1;

    [Header("Screen Fade")]
    [Tooltip("Full-screen black overlay (alpha fade).")]
    [SerializeField] private CanvasGroup screenFade;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;
    [SerializeField, Min(0f)] private float blackSeconds = 0.35f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.6f;

    /// <summary>Raised when the taxi state changes.</summary>
    public event Action<TaxiState> OnStateChanged;
    /// <summary>Short message for the player ("Taxi is on the way", ...).</summary>
    public event Action<string> OnNotice;
    /// <summary>Raised when the "get in" prompt should appear (true) or disappear (false).</summary>
    public event Action<bool> OnBoardPromptChanged;
    /// <summary>Raised when the player whistles (animation and sound already started).</summary>
    public event Action OnWhistled;
    /// <summary>Raised when the ride camera view changes. Arg: view name.</summary>
    public event Action<string> OnRideCameraChanged;

    public TaxiState State { get; private set; } = TaxiState.Idle;
    public Transform TaxiTransform => taxi != null ? taxi.transform : null;
    public Transform Player => player;
    public Camera ViewCamera => viewCamera;
    public IReadOnlyList<TaxiDestination> Destinations => destinations;
    /// <summary>Metres left on the current drive (pickup or ride).</summary>
    public float RemainingDistance => driver != null ? driver.RemainingDistance : 0f;
    public string RideCameraName => CameraViewNames[cameraView];

    private static readonly string[] CameraViewNames = { "Chase", "Cinematic", "Overhead", "Bonnet" };

    private struct StopPoint
    {
        public int lane, index;
        public float penalty;   // extra cost for stopping across the road from the player
    }

    private Transform player;
    private vThirdPersonController controller;
    private bool isValid;
    private int whistleStateHash;
    private float nextWhistleTime;

    private TSTrafficAI taxi;
    private Rigidbody taxiBody;
    private TaxiDriver driver;
    private TaxiMarker marker;
    private bool savedReservedForEventTrigger;
    private float[][] laneAlong;
    private readonly Dictionary<long, TSTrafficLight> lightFor = new Dictionary<long, TSTrafficLight>();

    private Vector3 pickupFor;
    private float closestDistance;
    private float awayTimer;
    private float replanTimer;
    private float arrivedAt = -1f;
    private bool boardPromptShown;

    private bool inTaxi;
    private bool skipRequested;
    private bool cancelRequested;
    private int cameraView;
    private Vector3 cameraVelocity;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Collider> disabledColliders = new List<Collider>();
    private CursorLockMode savedLockState;
    private bool savedCursorVisible;

    private void Awake()
    {
        controller = playerManager != null ? playerManager.Controller : null;
        isValid = roads != null && playerManager != null && controller != null && vehicleInteraction != null && playerInput != null
                  && playerAnimator != null && playerBody != null && viewCamera != null;
        if (!isValid)
        {
            Debug.LogError($"[{nameof(TaxiService)}] Missing references on '{name}' (iTS road manager, PlayerManager, PlayerVehicleInteraction, " +
                           "vThirdPersonInput, player Animator, player Rigidbody, view camera). The taxi service is switched off.", this);
            enabled = false;
            return;
        }
        player = playerManager.transform;
        whistleStateHash = Animator.StringToHash(whistleStateName);
        if (!playerAnimator.HasState(whistleLayer, whistleStateHash))
            Debug.LogWarning($"[{nameof(TaxiService)}] The player Animator has no state '{whistleStateName}' on layer {whistleLayer}; the whistle plays without animation.", this);
        if (interactionController == null)
            Debug.LogWarning($"[{nameof(TaxiService)}] No PlayerInteractionController assigned; E will always whistle, even next to interactables.", this);
        if (traffic == null)
            Debug.LogWarning($"[{nameof(TaxiService)}] No TSTrafficSpawner assigned; traffic may spawn on top of a called taxi.", this);
        if (destinations == null || destinations.Length == 0)
            Debug.LogWarning($"[{nameof(TaxiService)}] No taxi destinations assigned; the taxi can be called but not ridden.", this);

        if (whistleSource != null && whistleSource.clip == null) whistleSource.clip = whistleClip != null ? whistleClip : BuildWhistleClip();
        if (taxiMarkerPrefab != null)
        {
            marker = Instantiate(taxiMarkerPrefab, transform);
            marker.gameObject.SetActive(false);
        }
        else Debug.LogWarning($"[{nameof(TaxiService)}] No taxi marker prefab; called taxis have no roof sign and other traffic will not brake for them.", this);

        CacheLaneDistances();
        CacheLights();
        if (screenFade != null)
        {
            screenFade.alpha = 0f;
            screenFade.blocksRaycasts = false;
            screenFade.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (!isValid) return;
        vehicleInteraction.OnVehicleApproachStarted += HandleVehicleApproachStarted;
    }

    private void OnDisable()
    {
        if (vehicleInteraction != null) vehicleInteraction.OnVehicleApproachStarted -= HandleVehicleApproachStarted;
        if (!isValid) return;
        StopAllCoroutines();
        if (inTaxi) ExitTaxi();
        if (State == TaxiState.ChoosingDestination)
        {
            RestoreCursor();
            UnlockPlayer();
        }
        if (taxi != null) Release(false);
    }

    private void Update()
    {
        // The car was recycled or taken behind our back (a ride handles this itself).
        if (taxi != null && State != TaxiState.Riding && (!taxi.gameObject.activeInHierarchy || driver == null))
        {
            if (State == TaxiState.ChoosingDestination)
            {
                RestoreCursor();
                UnlockPlayer();
            }
            Release(false);
            Notice("Taxi cancelled");
        }

        switch (State)
        {
            case TaxiState.Idle:
            case TaxiState.EnRoute:
            case TaxiState.Waiting:
                UpdateOnFoot();
                break;
            case TaxiState.ChoosingDestination:
                if (cancelInput.GetButtonDown()) CancelDestinationChoice();
                break;
            case TaxiState.Riding:
                if (inTaxi)
                {
                    if (skipRideInput.GetButtonDown()) SkipRide();
                    if (cancelInput.GetButtonDown()) CancelRide();
                    if (cameraViewInput.GetButtonDown()) NextCameraView();
                }
                break;
        }
    }

    private void LateUpdate()
    {
        if (!inTaxi || taxi == null) return;
        FollowTaxi();
        PlaceRideCamera(false);
    }

    // ---------- on foot: whistle, pickup, boarding ----------

    private void UpdateOnFoot()
    {
        bool canBoard = CanBoard();
        SetBoardPrompt(canBoard);
        if (taxi != null) UpdatePickup();

        if (!callInput.GetButtonDown() || !CanUseTaxiInput()) return;
        if (canBoard) Board();
        else CallTaxi();
    }

    // E belongs to the interaction system while an interactable is focused, and nothing taxi-related happens
    // unless the player is on foot and in control.
    private bool CanUseTaxiInput()
    {
        return !playerManager.IsInputLocked && !playerManager.IsInVehicle && playerManager.CurrentState != PlayerState.Dead
               && vehicleInteraction.State == VehicleInteractionState.OnFoot
               && (interactionController == null || interactionController.CurrentFocus == null);
    }

    /// <summary>
    /// Whistles for a taxi (what E does on foot). Plays the whistle animation and sound, then sends the best-placed
    /// traffic car to the player. Returns true if a taxi is (already) on its way or waiting.
    /// </summary>
    public bool CallTaxi()
    {
        if (!isValid || State == TaxiState.ChoosingDestination || State == TaxiState.Riding) return false;
        if (Time.time < nextWhistleTime) return taxi != null;
        nextWhistleTime = Time.time + whistleCooldown;

        PlayWhistle();
        if (taxi != null)
        {
            Notice(State == TaxiState.Waiting ? "Your taxi is waiting" : "Taxi is on the way");
            return true;
        }

        List<StopPoint> stops = FindStopPoints(player.position, pickupSearchRadius);
        if (stops.Count == 0)
        {
            Notice("No road nearby for a taxi");
            return false;
        }

        if (!FindBestTaxi(stops, out TSTrafficAI best, out List<Vector3> route, out List<TaxiDriver.StopLine> lines))
        {
            Notice("No taxi nearby, try again in a moment");
            return false;
        }

        TakeControl(best, route, lines);
        pickupFor = player.position;
        closestDistance = Flat(best.transform.position - player.position).magnitude;
        awayTimer = 0f;
        replanTimer = replanInterval;
        arrivedAt = -1f;
        SetState(TaxiState.EnRoute);
        Notice("Taxi is on the way");
        return true;
    }

    private void PlayWhistle()
    {
        bool canAnimate = controller.isGrounded && !controller.customAction && !controller.isJumping && !controller.isRolling
                          && playerAnimator.HasState(whistleLayer, whistleStateHash);
        if (canAnimate) playerAnimator.CrossFadeInFixedTime(whistleStateHash, whistleCrossFade, whistleLayer);
        if (whistleSource != null && whistleSource.clip != null) whistleSource.Play();
        OnWhistled?.Invoke();
    }

    // Preferred taxi models first; if none of them can reach the player, any traffic car (when allowed).
    private bool FindBestTaxi(List<StopPoint> stops, out TSTrafficAI best, out List<Vector3> bestRoute, out List<TaxiDriver.StopLine> bestLines)
    {
        best = null;
        bestRoute = null;
        bestLines = null;
        bool hasPreferred = preferredTaxiModels != null && preferredTaxiModels.Length > 0;
        for (int pass = 0; pass < 2 && best == null; pass++)
        {
            bool preferredOnly = hasPreferred && pass == 0;
            if (pass == 1 && hasPreferred && !fallbackToAnyTrafficCar) break;
            if (pass == 1 && !hasPreferred) break;
            FindBestTaxi(stops, preferredOnly, out best, out bestRoute, out bestLines);
        }
        return best != null;
    }

    private void FindBestTaxi(List<StopPoint> stops, bool preferredOnly, out TSTrafficAI best, out List<Vector3> bestRoute,
        out List<TaxiDriver.StopLine> bestLines)
    {
        best = null;
        bestRoute = null;
        bestLines = null;
        float bestScore = float.MaxValue;
        float radiusSqr = searchRadius * searchRadius;
        IReadOnlyList<TrafficVehicleAdapter> cars = TrafficVehicleAdapter.ActiveCars;
        for (int i = 0; i < cars.Count; i++)
        {
            TrafficVehicleAdapter adapter = cars[i];
            // Plain traffic only (not being carjacked, not a car whose driver was shot).
            if (adapter == null || !adapter.isActiveAndEnabled || adapter.State != TrafficVehicleState.TrafficControlled) continue;
            if (Flat(adapter.transform.position - player.position).sqrMagnitude > radiusSqr) continue;
            if (preferredOnly && !IsPreferredModel(adapter.name)) continue;

            TSTrafficAI car = adapter.GetComponent<TSTrafficAI>();
            if (car == null || !car.IsEnabled || car.IsUpSideDown) continue;
            if (!PlanRoute(car, stops, maxRouteLength, out List<Vector3> route, out float score, out List<TaxiDriver.StopLine> lines)) continue;
            if (IsVisible(car.transform.position)) score += visibleCarPenalty;
            if (score >= bestScore) continue;
            bestScore = score;
            best = car;
            bestRoute = route;
            bestLines = lines;
        }
    }

    private bool IsPreferredModel(string carName)
    {
        for (int i = 0; i < preferredTaxiModels.Length; i++)
        {
            if (preferredTaxiModels[i] != null && carName.StartsWith(preferredTaxiModels[i].name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private bool IsVisible(Vector3 position)
    {
        Vector3 viewport = viewCamera.WorldToViewportPoint(position + Vector3.up);
        return viewport.z > 0f && viewport.x > -0.05f && viewport.x < 1.05f && viewport.y > -0.05f && viewport.y < 1.05f;
    }

    private void UpdatePickup()
    {
        if (driver == null || (State != TaxiState.EnRoute && State != TaxiState.Waiting)) return;

        float distance = Flat(taxi.transform.position - player.position).magnitude;
        closestDistance = Mathf.Min(closestDistance, distance);

        // Walking away: the taxi gives up and drives off in normal traffic.
        bool away = distance > Mathf.Max(abandonDistance, closestDistance + walkAwayMargin);
        awayTimer = away ? awayTimer + Time.deltaTime : 0f;
        if (awayTimer > leaveAfterSeconds)
        {
            Release(true);
            Notice("Taxi left");
            return;
        }

        if (driver.CurrentState == TaxiDriver.State.Arrived)
        {
            if (State != TaxiState.Waiting)
            {
                SetState(TaxiState.Waiting);
                Notice("Your taxi is here");
            }
            if (arrivedAt < 0f) arrivedAt = Time.time;
            if (Time.time - arrivedAt > waitSeconds)
            {
                Release(true);
                Notice("Taxi left");
                return;
            }
        }
        else
        {
            arrivedAt = -1f;
        }

        // The player moved: pull over wherever they are now instead of the old spot.
        replanTimer -= Time.deltaTime;
        if (replanTimer > 0f || distance <= boardDistance) return;
        if (Flat(player.position - pickupFor).magnitude < replanDistance) return;
        replanTimer = replanInterval;

        if (PlanRoute(taxi, FindStopPoints(player.position, pickupSearchRadius), maxRouteLength, out List<Vector3> route, out _, out List<TaxiDriver.StopLine> lines))
        {
            driver.cruiseSpeed = cruiseSpeed;
            driver.Begin(route, taxi.GetComponent<TSSimpleCar>(), taxi.FrontPoint, lines);
            pickupFor = player.position;
            arrivedAt = -1f;
            if (State == TaxiState.Waiting) SetState(TaxiState.EnRoute);
        }
    }

    private bool CanBoard()
    {
        return taxi != null && driver != null && (State == TaxiState.EnRoute || State == TaxiState.Waiting)
               && Flat(taxi.transform.position - player.position).magnitude <= boardDistance
               && (taxiBody == null || taxiBody.linearVelocity.magnitude < maxBoardingSpeed);
    }

    private void SetBoardPrompt(bool show)
    {
        if (boardPromptShown == show) return;
        boardPromptShown = show;
        OnBoardPromptChanged?.Invoke(show);
    }

    private void Board()
    {
        if (destinations == null || destinations.Length == 0)
        {
            Notice("No taxi destinations available");
            return;
        }
        // Got in before it reached the pickup spot: it stops right here.
        if (driver.CurrentState != TaxiDriver.State.Arrived) driver.StopHere();
        SetBoardPrompt(false);
        LockPlayer();
        savedLockState = Cursor.lockState;
        savedCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SetState(TaxiState.ChoosingDestination);
    }

    /// <summary>Called by the destination list: ride to <see cref="Destinations"/>[index].</summary>
    public void ChooseDestination(int index)
    {
        if (State != TaxiState.ChoosingDestination || taxi == null) return;
        if (destinations == null || index < 0 || index >= destinations.Length || destinations[index] == null) return;
        RestoreCursor();
        SetState(TaxiState.Riding);
        StartCoroutine(Ride(destinations[index]));
    }

    /// <summary>Closes the destination list; the taxi keeps waiting.</summary>
    public void CancelDestinationChoice()
    {
        if (State != TaxiState.ChoosingDestination) return;
        RestoreCursor();
        UnlockPlayer();
        arrivedAt = Time.time;
        SetState(TaxiState.Waiting);
    }

    // The player walks up to the taxi's driver door to steal it (F): hand the car over to the carjack as it is.
    private void HandleVehicleApproachStarted(IVehicleInteractionTarget target)
    {
        if (taxi == null || target == null || (target as UnityEngine.Object) == null || target.transform != taxi.transform) return;
        if (State != TaxiState.EnRoute && State != TaxiState.Waiting) return;
        // The carjack reserved the car (paused, protected from recycling); only our own control is removed.
        DetachControl();
        taxi = null;
        taxiBody = null;
        SetBoardPrompt(false);
        SetState(TaxiState.Idle);
    }

    // ---------- taking and releasing the car ----------

    private void TakeControl(TSTrafficAI car, List<Vector3> route, List<TaxiDriver.StopLine> stopLines)
    {
        taxi = car;
        taxiBody = car.GetComponent<Rigidbody>();
        car.Pause();
        // Keep iTS from recycling it while it is ours, and free the lane points it had reserved so traffic behind
        // is not held up by an empty reservation (the marker's sensor target makes traffic brake for the taxi instead).
        savedReservedForEventTrigger = car.reservedForEventTrigger;
        car.reservedForEventTrigger = true;
        var unreserve = typeof(TSTrafficAI).GetMethod("UnReserveAll",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (unreserve != null) unreserve.Invoke(car, null);

        if (marker != null) marker.AttachTo(car.transform);

        driver = car.gameObject.AddComponent<TaxiDriver>();
        driver.cruiseSpeed = cruiseSpeed;
        driver.Begin(route, car.GetComponent<TSSimpleCar>(), car.FrontPoint, stopLines);
        SetSpawnGuard(car.transform, true);
    }

    // Removes everything the service added to the car, without touching the car's iTS state.
    private void DetachControl()
    {
        if (taxi != null) SetSpawnGuard(taxi.transform, false);
        if (driver != null) Destroy(driver);
        driver = null;
        if (marker != null) marker.Detach(transform);
    }

    /// <summary>
    /// Gives the car back to iTS. driveAway: it resumes traffic from the lane it is on, so it visibly drives off;
    /// otherwise it is handed to the spawner, which recycles it.
    /// </summary>
    private void Release(bool driveAway)
    {
        TSTrafficAI car = taxi;
        DetachControl();
        if (car != null)
        {
            car.reservedForEventTrigger = savedReservedForEventTrigger;
            if (car.gameObject.activeInHierarchy)
            {
                bool resumed = driveAway && ResumeTrafficAI(car);
                if (!resumed) car.Disable(true, false);
            }
        }
        taxi = null;
        taxiBody = null;
        SetBoardPrompt(false);
        if (State != TaxiState.Idle) SetState(TaxiState.Idle);
    }

    // No new traffic right on top of the taxi (it gave up its lane reservations).
    private void SetSpawnGuard(Transform target, bool on)
    {
        if (traffic == null) return;
        var guards = new List<TSTrafficSpawner.TransformsSpawningCheck>();
        if (traffic.transformSpawningCheck != null)
        {
            foreach (var guard in traffic.transformSpawningCheck)
                if (guard != null && guard.transform != target) guards.Add(guard);
        }
        if (on) guards.Add(new TSTrafficSpawner.TransformsSpawningCheck { transform = target, initialNotSpawningRadius = taxiNoSpawnRadius });
        traffic.transformSpawningCheck = guards.ToArray();
    }

    // Restart the car's own AI from the lane point it is on, the same way iTS starts cars placed mid-lane.
    private bool ResumeTrafficAI(TSTrafficAI car)
    {
        if (!FindCarLanePoint(car, out int lane, out int index)) return false;

        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        Type selectionType = typeof(TSTrafficAI).GetNestedType("TSNextLaneSelection", any | System.Reflection.BindingFlags.Static);
        if (selectionType == null) return false;
        var ctor = selectionType.GetConstructor(any, null, new[] { typeof(TSBaseInfo) }, null);
        var listType = typeof(List<>).MakeGenericType(selectionType);
        System.Reflection.MethodInfo addPath = null;
        foreach (var method in typeof(TSTrafficAI).GetMethods(any))
        {
            var ps = method.GetParameters();
            if (method.Name == "AddNextTrackToPath" && ps.Length == 2 && ps[0].ParameterType == listType && ps[1].ParameterType == typeof(int)) addPath = method;
        }
        if (ctor == null || addPath == null) return false;

        var path = (System.Collections.IList)Activator.CreateInstance(listType);
        path.Add(ctor.Invoke(new object[] { roads.lanes[lane] }));
        addPath.Invoke(car, new object[] { path, index });
        car.UnPause();
        return true;
    }

    // ---------- riding ----------

    /// <summary>Ride panel / Space: jump straight to the destination.</summary>
    public void SkipRide() { if (inTaxi) skipRequested = true; }

    /// <summary>Ride panel / Esc: the taxi stops and the passenger gets out where it is.</summary>
    public void CancelRide() { if (inTaxi) cancelRequested = true; }

    /// <summary>Ride panel / C: cycle the ride camera view.</summary>
    public void NextCameraView()
    {
        if (!inTaxi) return;
        cameraView = (cameraView + 1) % CameraViewNames.Length;
        OnRideCameraChanged?.Invoke(CameraViewNames[cameraView]);
    }

    private IEnumerator Ride(TaxiDestination destination)
    {
        skipRequested = false;
        cancelRequested = false;
        Vector3 destinationPosition = destination.transform.position;

        bool planned = PlanRoute(taxi, FindStopPoints(destinationPosition, destinationSearchRadius), rideMaxRouteLength,
            out List<Vector3> route, out _, out List<TaxiDriver.StopLine> lines);

        yield return Fade(0f, 1f, fadeOutSeconds);
        if (taxi == null)
        {
            // The car vanished during the fade: travel instantly.
            yield return FinishRide(destination.transform.position, destination.transform.rotation, false, destination.DisplayName);
            yield break;
        }

        if (!planned)
        {
            // No drivable route (too far or not connected): instant trip.
            yield return FinishRide(destination.transform.position, destination.transform.rotation, false, destination.DisplayName);
            yield break;
        }

        EnterTaxi();
        driver.cruiseSpeed = rideCruiseSpeed;
        driver.Begin(route, taxi.GetComponent<TSSimpleCar>(), taxi.FrontPoint, lines);
        yield return new WaitForSecondsRealtime(0.15f);
        yield return Fade(1f, 0f, fadeInSeconds);

        while (taxi != null && taxi.gameObject.activeInHierarchy && driver != null
               && driver.CurrentState == TaxiDriver.State.Driving && !skipRequested && !cancelRequested)
            yield return null;

        bool taxiOk = taxi != null && taxi.gameObject.activeInHierarchy && driver != null;
        if (cancelRequested && !skipRequested && taxiOk)
        {
            // Pull up where we are, then let the passenger out on the kerb side.
            driver.StopHere();
            float waitUntil = Time.time + 2.5f;
            while (taxi != null && taxiBody != null && taxiBody.linearVelocity.magnitude > 0.5f && Time.time < waitUntil) yield return null;
            yield return Fade(0f, 1f, fadeOutSeconds);
            Vector3 exitPoint = GetStepOutPoint(destinationPosition, out Quaternion exitFacing);
            ExitTaxi();
            yield return FinishRide(exitPoint, exitFacing, true, null, "Ride cancelled");
            yield break;
        }

        yield return Fade(0f, 1f, fadeOutSeconds);
        taxiOk = taxi != null && taxi.gameObject.activeInHierarchy && driver != null;
        bool arrived = !skipRequested && taxiOk && driver.CurrentState == TaxiDriver.State.Arrived;
        Vector3 dropOff = destination.transform.position;
        Quaternion dropFacing = destination.transform.rotation;
        if (arrived) dropOff = GetStepOutPoint(destinationPosition, out dropFacing);
        ExitTaxi();
        yield return FinishRide(dropOff, dropFacing, arrived, destination.DisplayName);
    }

    private IEnumerator FinishRide(Vector3 position, Quaternion facing, bool taxiDrivesAway, string destinationName, string message = null)
    {
        TeleportPlayer(position, facing);
        Release(taxiDrivesAway);
        if (thirdPersonCamera != null && thirdPersonCamera.currentTarget != null) thirdPersonCamera.ResetAngleWithoutSmooth();

        yield return new WaitForSecondsRealtime(blackSeconds);
        UnlockPlayer();
        yield return Fade(1f, 0f, fadeInSeconds);
        if (State != TaxiState.Idle) SetState(TaxiState.Idle);
        Notice(message ?? (string.IsNullOrEmpty(destinationName) ? "You have arrived" : "Arrived: " + destinationName));
    }

    // Beside the taxi on its kerb side, on the ground, facing the destination; the destination point itself
    // if that spot is blocked.
    private Vector3 GetStepOutPoint(Vector3 destinationPosition, out Quaternion facing)
    {
        Transform t = taxi.transform;
        Vector3 candidate = t.position + KerbSide(Flat(t.forward)) * stepOutOffset;
        Vector3 result = candidate;
        if (Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, groundLayers, QueryTriggerInteraction.Ignore))
            result = hit.point;
        bool blocked = Physics.CheckCapsule(result + Vector3.up * 0.4f, result + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        if (blocked) result = destinationPosition;

        Vector3 look = Flat(destinationPosition - result);
        facing = look.sqrMagnitude > 1f ? Quaternion.LookRotation(look.normalized) : Quaternion.LookRotation(Flat(t.forward).normalized);
        return result + Vector3.up * 0.05f;
    }

    private void EnterTaxi()
    {
        inTaxi = true;
        cameraView = 0;
        OnRideCameraChanged?.Invoke(CameraViewNames[cameraView]);

        playerBody.linearVelocity = Vector3.zero;
        controller.DisableGravityAndCollision();
        hiddenRenderers.Clear();
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
            if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
        disabledColliders.Clear();
        foreach (Collider c in player.GetComponentsInChildren<Collider>())
            if (c.enabled) { c.enabled = false; disabledColliders.Add(c); }

        FollowTaxi();
        PlaceRideCamera(true);
    }

    private void ExitTaxi()
    {
        inTaxi = false;
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
        hiddenRenderers.Clear();
        foreach (Collider c in disabledColliders) if (c != null) c.enabled = true;
        disabledColliders.Clear();
        controller.EnableGravityAndCollision();
        // Hand the Main Camera back to Invector's camera rig (it sits at its parent's pose).
        viewCamera.transform.localPosition = Vector3.zero;
        viewCamera.transform.localRotation = Quaternion.identity;
    }

    private void TeleportPlayer(Vector3 position, Quaternion facing)
    {
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        player.SetPositionAndRotation(position, facing);
        playerBody.position = position;
        playerBody.rotation = facing;
    }

    private void LockPlayer()
    {
        playerManager.SetInputLocked(true);
        playerInput.SetLockCameraInput(true);
        if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(true);
    }

    private void UnlockPlayer()
    {
        if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(false);
        playerInput.SetLockCameraInput(false);
        playerManager.SetInputLocked(false);
    }

    private void RestoreCursor()
    {
        Cursor.lockState = savedLockState;
        Cursor.visible = savedCursorVisible;
    }

    // The hidden player rides along, so the traffic spawner and camera rig stay centred on the taxi.
    private void FollowTaxi()
    {
        Vector3 position = taxi.transform.position + Vector3.up * 0.3f;
        Quaternion rotation = Quaternion.LookRotation(Flat(taxi.transform.forward).normalized);
        player.SetPositionAndRotation(position, rotation);
        playerBody.position = position;
        playerBody.rotation = rotation;
    }

    // Overrides the Main Camera's pose after Invector's rig has moved (the rig updates in FixedUpdate).
    private void PlaceRideCamera(bool snap)
    {
        Transform t = taxi.transform;
        Vector3 fwd = Flat(t.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 pos, look;
        switch (cameraView)
        {
            case 1:   // cinematic: low beside the front wheel, looking back across the car
                pos = t.position + right * 5.5f + fwd * 3f + Vector3.up * 1.4f;
                look = t.position + Vector3.up * 1f - fwd * 1f;
                break;
            case 2:   // overhead
                pos = t.position - fwd * 14f + Vector3.up * 18f;
                look = t.position + fwd * 8f;
                break;
            case 3:   // bonnet
                pos = t.position + fwd * 1.3f + Vector3.up * 1.45f;
                look = pos + fwd * 20f - Vector3.up * 1.5f;
                break;
            default:  // third-person chase
                pos = t.position - fwd * 7.5f + Vector3.up * 3f;
                look = t.position + fwd * 3f + Vector3.up * 1f;
                break;
        }

        // Keep chase-style views out of walls.
        if (cameraView != 3)
        {
            Vector3 from = t.position + Vector3.up * 1.5f;
            if (Physics.Raycast(from, pos - from, out RaycastHit hit, Vector3.Distance(from, pos), groundLayers, QueryTriggerInteraction.Ignore))
                pos = hit.point + hit.normal * 0.3f;
        }

        Transform cam = viewCamera.transform;
        if (snap || cameraView == 3)
        {
            cam.position = pos;
            cameraVelocity = Vector3.zero;
        }
        else
        {
            cam.position = Vector3.SmoothDamp(cam.position, pos, ref cameraVelocity, 0.15f);
        }
        Quaternion rotation = Quaternion.LookRotation(look - cam.position);
        cam.rotation = snap || cameraView == 3 ? rotation : Quaternion.Slerp(cam.rotation, rotation, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (screenFade == null) yield break;
        screenFade.gameObject.SetActive(true);
        screenFade.blocksRaycasts = true;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            screenFade.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        screenFade.alpha = to;
        if (to <= 0f)
        {
            screenFade.blocksRaycasts = false;
            screenFade.gameObject.SetActive(false);
        }
    }

    private void SetState(TaxiState newState)
    {
        if (State == newState) return;
        State = newState;
        OnStateChanged?.Invoke(newState);
    }

    private void Notice(string message) => OnNotice?.Invoke(message);

    // ---------- routing (directed, along iTS lanes and junction connectors) ----------

    private static long LightKey(int lane, int connector)
    {
        return ((long)lane << 16) | (uint)connector;
    }

    // Once at startup: which traffic light controls each junction turn.
    private void CacheLights()
    {
        lightFor.Clear();
        foreach (var light in FindObjectsByType<TSTrafficLight>(FindObjectsSortMode.None))
            foreach (var p in light.pointsNormalLight)
                lightFor[LightKey(p.lane, p.connector)] = light;
    }

    private void CacheLaneDistances()
    {
        laneAlong = new float[roads.lanes.Length][];
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var p = roads.lanes[l].points;
            var along = new float[p.Length];
            for (int i = 1; i < p.Length; i++) along[i] = along[i - 1] + Vector3.Distance(p[i - 1].point, p[i].point);
            laneAlong[l] = along;
        }
    }

    // Kerb-lane stops near a point (the player for pickups, the destination for rides). Lanes whose kerb side faces
    // the point need no road crossing.
    private List<StopPoint> FindStopPoints(Vector3 around, float radius)
    {
        List<StopPoint> stops = FindStopPoints(true, around, radius);
        return stops.Count > 0 ? stops : FindStopPoints(false, around, radius);
    }

    private List<StopPoint> FindStopPoints(bool kerbLanesOnly, Vector3 pp, float radius)
    {
        var stops = new List<StopPoint>();
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var pts = roads.lanes[l].points;
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                Vector3 d = pts[i].point - pp;
                if (Mathf.Abs(d.y) > 4f) continue;
                d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; bestIndex = i; }
            }
            if (bestIndex < 0 || best > radius * radius) continue;
            // Only the kerb lane: stopping in an inner lane would leave the taxi in the middle of the road.
            if (kerbLanesOnly && !IsKerbLane(l, bestIndex)) continue;

            Vector3 heading = pts[bestIndex + 1].point - pts[bestIndex - 1].point;
            heading.y = 0f;
            Vector3 toPoint = pp - pts[bestIndex].point;
            toPoint.y = 0f;
            bool kerbFacesPoint = Vector3.Dot(KerbSide(heading), toPoint) > 0f;
            stops.Add(new StopPoint
            {
                lane = l,
                index = bestIndex,
                penalty = Mathf.Sqrt(best) * 2f + (kerbFacesPoint ? 0f : crossRoadPenalty)
            });
        }
        return stops;
    }

    private bool PlanRoute(TSTrafficAI car, List<StopPoint> stops, float routeLimit, out List<Vector3> route, out float score, out List<TaxiDriver.StopLine> stopLines)
    {
        route = null;
        score = float.MaxValue;
        stopLines = new List<TaxiDriver.StopLine>();
        if (stops == null || stops.Count == 0) return false;

        if (!FindCarLanePoint(car, out int startLane, out int startIndex)) return false;

        int laneCount = roads.lanes.Length;
        var dist = new float[laneCount];
        var viaLane = new int[laneCount];
        var viaConnector = new int[laneCount];
        var done = new bool[laneCount];
        for (int i = 0; i < laneCount; i++) { dist[i] = float.MaxValue; viaLane[i] = -1; viaConnector[i] = -1; }

        float[] startAlong = laneAlong[startLane];

        // dist[l] = distance to the START of lane l. Leaving the start lane costs its remainder.
        float leaveStart = startAlong[startAlong.Length - 1] - startAlong[startIndex];
        var connectors = roads.lanes[startLane].connectors;
        for (int k = 0; k < connectors.Length; k++)
        {
            int next = connectors[k].nextLane;
            float d = leaveStart + connectors[k].totalDistance;
            if (next >= 0 && d < dist[next]) { dist[next] = d; viaLane[next] = startLane; viaConnector[next] = k; }
        }

        for (int iter = 0; iter < laneCount; iter++)
        {
            int u = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < laneCount; i++) if (!done[i] && dist[i] < bestD) { bestD = dist[i]; u = i; }
            if (u < 0 || bestD > routeLimit) break;
            done[u] = true;
            float laneLength = laneAlong[u][laneAlong[u].Length - 1];
            var cs = roads.lanes[u].connectors;
            for (int k = 0; k < cs.Length; k++)
            {
                int next = cs[k].nextLane;
                if (next < 0) continue;
                float d = bestD + laneLength + cs[k].totalDistance;
                if (d < dist[next]) { dist[next] = d; viaLane[next] = u; viaConnector[next] = k; }
            }
        }

        // Pick the stop with the lowest drive length plus its penalty.
        int stopLane = -1, stopIndex = 0;
        float direct = float.MaxValue, viaNetwork = float.MaxValue;
        foreach (var stop in stops)
        {
            float d = float.MaxValue;
            bool isDirect = false;
            if (stop.lane == startLane && startIndex <= stop.index)
            {
                d = laneAlong[stop.lane][stop.index] - startAlong[startIndex];
                isDirect = true;
            }
            else if (dist[stop.lane] < float.MaxValue)
            {
                d = dist[stop.lane] + laneAlong[stop.lane][stop.index];
            }
            if (d > routeLimit || d + stop.penalty >= score) continue;
            score = d + stop.penalty;
            stopLane = stop.lane;
            stopIndex = stop.index;
            direct = isDirect ? d : float.MaxValue;
            viaNetwork = isDirect ? float.MaxValue : d;
        }
        if (stopLane < 0) return false;

        route = new List<Vector3> { car.transform.position };
        if (direct <= viaNetwork)
        {
            AddLanePoints(route, startLane, startIndex + 1, stopIndex);
        }
        else
        {
            // Rebuild the chain of lanes back from the stop lane.
            var chain = new List<int>();
            int l = stopLane;
            int guard = 0;
            while (l != startLane && l >= 0 && guard++ < laneCount)
            {
                chain.Add(l);
                l = viaLane[l];
            }
            chain.Reverse();
            AddLanePoints(route, startLane, startIndex + 1, roads.lanes[startLane].points.Length - 1);
            int from = startLane;
            foreach (int lane in chain)
            {
                var connector = roads.lanes[from].connectors[viaConnector[lane]];
                // The end of the lane is the junction's stop line; remember which light controls this turn.
                if (lightFor.TryGetValue(LightKey(from, viaConnector[lane]), out TSTrafficLight light))
                    stopLines.Add(new TaxiDriver.StopLine { routeIndex = route.Count - 1, light = light });
                foreach (var p in connector.points) route.Add(p.point);
                AddLanePoints(route, lane, 0, lane == stopLane ? stopIndex : roads.lanes[lane].points.Length - 1);
                from = lane;
            }
        }

        // Finish at the kerb: ease the last stretch of the route over toward the pavement.
        var sp = roads.lanes[stopLane].points;
        Vector3 stopHeading = Flat(sp[Mathf.Min(stopIndex + 1, sp.Length - 1)].point - sp[Mathf.Max(stopIndex - 1, 0)].point);
        float kerb = KerbDistance(sp[stopIndex].point, KerbSide(stopHeading));
        PullIntoKerb(route, kerb > 0f ? Mathf.Clamp(kerb - kerbClearance, 0f, kerbSearchDistance) : pullOverOffset);
        return true;
    }

    // Unit vector from a lane toward its kerb (left in left-hand traffic, right otherwise).
    private Vector3 KerbSide(Vector3 heading)
    {
        heading.y = 0f;
        return Vector3.Cross(Vector3.up, heading.normalized) * (driveOnLeft ? -1f : 1f);
    }

    // Sideways distance from a lane point to the kerb (where the ground steps up), or -1 if there is none within reach.
    private float KerbDistance(Vector3 lanePoint, Vector3 kerbSide)
    {
        const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
        if (!Physics.Raycast(lanePoint + Vector3.up * 1.5f, Vector3.down, out RaycastHit road, 3f, groundLayers, ignore)) return -1f;
        for (float d = 0.5f; d <= kerbSearchDistance; d += 0.25f)
        {
            if (Physics.Raycast(lanePoint + kerbSide * d + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, groundLayers, ignore)
                && hit.point.y - road.point.y > kerbStepHeight) return d;
        }
        return -1f;
    }

    // Shift the final stretch of the route toward the kerb, blending from 0 to the full offset at the stop.
    private void PullIntoKerb(List<Vector3> route, float offset)
    {
        if (route.Count < 2 || offset <= 0f) return;
        var shifted = new Vector3[route.Count];
        float fromEnd = 0f;
        for (int i = route.Count - 1; i >= 0; i--)
        {
            if (i < route.Count - 1) fromEnd += Flat(route[i + 1] - route[i]).magnitude;
            Vector3 a = route[Mathf.Max(0, i - 1)], b = route[Mathf.Min(route.Count - 1, i + 1)];
            Vector3 h = Flat(b - a);
            if (fromEnd > pullInDistance || h.sqrMagnitude < 1e-4f) { shifted[i] = route[i]; continue; }
            float t = 1f - fromEnd / pullInDistance;
            t = t * t * (3f - 2f * t);
            shifted[i] = route[i] + KerbSide(h) * offset * t;
        }
        shifted[0] = route[0];   // the taxi's own position stays put
        route.Clear();
        route.AddRange(shifted);
    }

    // A lane is the kerb lane when the kerb is right beside it (on its kerb side).
    private bool IsKerbLane(int lane, int index)
    {
        var pts = roads.lanes[lane].points;
        Vector3 heading = pts[Mathf.Min(index + 1, pts.Length - 1)].point - pts[Mathf.Max(index - 1, 0)].point;
        float kerb = KerbDistance(pts[index].point, KerbSide(heading));
        return kerb > 0f && kerb <= kerbLaneMaxDistance;
    }

    private void AddLanePoints(List<Vector3> route, int lane, int from, int to)
    {
        var pts = roads.lanes[lane].points;
        for (int i = Mathf.Max(0, from); i <= to && i < pts.Length; i++)
            if ((pts[i].point - route[route.Count - 1]).sqrMagnitude > 0.04f) route.Add(pts[i].point);
    }

    // Nearest lane point under the car whose direction matches the way the car faces.
    private bool FindCarLanePoint(TSTrafficAI car, out int lane, out int index)
    {
        lane = -1;
        index = 0;
        float best = 6f * 6f;
        Vector3 pos = car.transform.position, fwd = car.transform.forward;
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var pts = roads.lanes[l].points;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 d = pts[i].point - pos;
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= best) continue;
                Vector3 h = pts[i + 1].point - pts[i].point;
                h.y = 0f;
                if (h.sqrMagnitude < 1e-4f || Vector3.Dot(h.normalized, fwd) < 0.7f) continue;
                best = sq; lane = l; index = i;
            }
        }
        // Start a little ahead so the taxi doesn't try to steer back behind itself.
        if (lane >= 0) index = Mathf.Min(index + 2, roads.lanes[lane].points.Length - 1);
        return lane >= 0;
    }

    // Two-note "fweet-fweeeet" taxi whistle, generated once when no whistle clip is assigned.
    private static AudioClip BuildWhistleClip()
    {
        const int rate = 44100;
        float[] notes = { 0.22f, 0.08f, 0.42f };   // note, gap, note
        int total = Mathf.CeilToInt((notes[0] + notes[1] + notes[2]) * rate);
        var data = new float[total];
        double phase = 0;
        for (int i = 0; i < total; i++)
        {
            float t = (float)i / rate;
            float freq = 0f, env = 0f;
            if (t < notes[0])
            {
                float u = t / notes[0];
                freq = Mathf.Lerp(1900f, 2700f, u);
                env = Mathf.Sin(Mathf.PI * u);
            }
            else if (t > notes[0] + notes[1])
            {
                float u = (t - notes[0] - notes[1]) / notes[2];
                freq = Mathf.Lerp(2100f, 3100f, Mathf.Min(1f, u * 1.6f)) + Mathf.Sin(t * 38f) * 60f;
                env = Mathf.Min(1f, u * 8f) * Mathf.Min(1f, (1f - u) * 5f);
            }
            phase += 2.0 * Mathf.PI * freq / rate;
            data[i] = (float)Math.Sin(phase) * env * 0.35f;
        }
        var clip = AudioClip.Create("TaxiWhistle", total, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
