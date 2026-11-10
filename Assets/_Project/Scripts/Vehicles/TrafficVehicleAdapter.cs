using System;
using System.Collections;
using System.Collections.Generic;
using Invector;
using OpenworldCrime.Pedestrians;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Player-facing side of an iTS traffic car (lives on the traffic prefab root next to TSTrafficAI). Traffic cars stay
    /// lightweight: iTS drives them with TSSimpleCar, the driver is a static seated mesh and there is no RCC on them.
    /// - <see cref="ReserveForEntry"/> (player pressed enter): iTS pauses the car (TSTrafficAI.Pause = full brake, AI off) and
    ///   reservedForEventTrigger keeps TSTrafficSpawner from recycling it while the player walks to the door.
    /// - <see cref="CancelEntryReservation"/>: the car is un-paused and simply drives on in traffic.
    /// - <see cref="CompleteEntry"/> (player at the door, car stopped): a drivable RCC counterpart with the same visuals is placed
    ///   at exactly the same pose, the driver is carried over (<see cref="TrafficVehicleDriver"/>) and this car is handed back
    ///   to iTS through its own disable path (lane reservations released, pooled and respawned elsewhere).
    /// The drivable vehicle is not part of the iTS pool, so iTS can never despawn it, and iTS and RCC never control the same object.
    /// Shooting the driver (<see cref="SeatedDriverHealth"/>, trigger hit zones reached through the window glass):
    /// - wounded: the seated mesh flinches and the driver panics (runs red lights - an iTS setting, reset on respawn);
    /// - killed: iTS brakes the car to a stop (as for a carjack) and keeps it from being recycled; once stopped it is handed over
    ///   exactly like a takeover (drivable counterpart, <see cref="OnTakenOver"/>), whose driver door opens and the dead body
    ///   falls out (<see cref="TrafficVehicleDriver.DropDead"/>). The car is then an empty parked car the player can simply enter.
    ///   A car without a drivable twin (police) has no door to open: the body drops beside it and it returns to the pool out of sight.
    /// Shot detail (window glass collider + driver hit zones) is only switched on near the player (<see cref="SetShotDetail"/>,
    /// driven by <see cref="TrafficTakeoverManager"/>'s low-frequency check); the metal body hitbox always stays on.
    /// Bumped or rammed by the player's car, the driver never panics: the car takes the hit (physics, crash damage), the driver
    /// may honk and stops for a moment, then iTS drives on along its route. For a while after a hit the car is watched (once a
    /// second): knocked onto its side or roof, or unable to get going, it goes back to the iTS pool as soon as it is out of
    /// sight, so a crash never leaves a car stuck for good. Only bullets (<see cref="OnCarThreatened"/>) scare drivers.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficVehicleAdapter : MonoBehaviour, IVehicleInteractionTarget
    {
        [Header("Traffic (iTS)")]
        [SerializeField] private TSTrafficAI trafficAI;
        [SerializeField] private Rigidbody body;
        [Tooltip("iTS car physics; its brake torque is eased while the car stops for the player.")]
        [SerializeField] private TSSimpleCar simpleCar;
        [Tooltip("Brake torque multiplier while the car stops for the player (1 = iTS' full emergency brake).")]
        [SerializeField, Range(0.1f, 1f)] private float reservedBrakeScale = 0.25f;

        [Header("Driver")]
        [Tooltip("Static seated driver mesh under the DriverSeat. Inactive or empty = the car has no driver.")]
        [SerializeField] private GameObject driverVisual;
        [Tooltip("Outfits picked at random each time the car spawns (also used by the pulled-out driver). Empty = keep the mesh's material. " +
                 "Ignored when a Driver Roster is set.")]
        [SerializeField] private Material[] driverOutfits = new Material[0];
        [Tooltip("Real street pedestrians who drive this car: one is picked each time the car spawns (seated mesh at the wheel), " +
                 "and the same pedestrian is pulled out when the car is stolen.")]
        [SerializeField] private PedDriverRoster driverRoster;
        [Tooltip("How each roster driver sits in this car so his head clears the roof (one per roster entry). Set by Build Pedestrian Drivers.")]
        [SerializeField] private PedDriverRoster.SeatFit[] driverFits = new PedDriverRoster.SeatFit[0];
        [Tooltip("Hit zones and health of the seated driver (child of the bullet hitbox). Optional: without it the driver cannot be shot.")]
        [SerializeField] private SeatedDriverHealth driverHealth;
        [Tooltip("Window glass collider of the bullet hitbox (switched off with the driver zones while the car is far from the player).")]
        [SerializeField] private Collider glassCollider;
        [Tooltip("A wounded driver panics and runs red lights.")]
        [SerializeField] private bool panicWhenWounded = true;
        [Tooltip("Seconds a car with a killed driver gets to brake to a stop before it is handed over anyway.")]
        [SerializeField, Min(0.5f)] private float killedDriverStopTimeout = 6f;
        [Tooltip("Officers instead of civilians from the roster (police cars).")]
        [SerializeField] private bool policeDriver;

        [Header("Damage")]
        [Tooltip("Health of this car. Blown up it stays as a wreck (iTS brakes it, no driver can be stolen) until off camera, then returns to the iTS pool, so traffic keeps its count.")]
        [SerializeField] private VehicleHealth health;

        [Header("Bumped By The Player's Car")]
        [Tooltip("Impact speed (m/s) from which the driver reacts (honk, short stop). Gentler touches are ignored.")]
        [SerializeField, Min(0f)] private float bumpReactSpeed = 2.5f;
        [Tooltip("Chance the driver honks when bumped.")]
        [SerializeField, Range(0f, 1f)] private float bumpHonkChance = 0.75f;
        [Tooltip("Seconds the driver stops after a bump before driving on (random between the two).")]
        [SerializeField] private Vector2 bumpStopSeconds = new Vector2(0.8f, 1.8f);
        [Tooltip("Seconds before another bump makes him react again.")]
        [SerializeField, Min(0f)] private float bumpCooldown = 4f;
        [Tooltip("After a bump the car is watched this long (s): flipped, or stopped for Stuck Seconds, it is recycled out of sight.")]
        [SerializeField, Min(0f)] private float bumpWatchSeconds = 30f;
        [SerializeField, Min(1f)] private float bumpStuckSeconds = 10f;
        [Tooltip("Recycling waits until the car is off screen and at least this far (m) from the camera.")]
        [SerializeField, Min(0f)] private float bumpRecycleKeepDistance = 25f;

        [Header("Takeover")]
        [Tooltip("Off for cars the player cannot steal (no drivable counterpart). A killed driver's car is then recycled once it is out of sight.")]
        [SerializeField] private bool stealable = true;
        [SerializeField, Min(0f)] private float killedDriverRecycleTime = 15f;
        [SerializeField, Min(0f)] private float killedDriverKeepDistance = 15f;
        [Tooltip("How far (m) from the seat, sideways, a dead driver's body drops when the car has no drivable twin.")]
        [SerializeField, Min(0f)] private float deadBodySideDistance = 1.4f;
        [Tooltip("Drivable RCC counterpart of this car (same model), placed here when the player gets in.")]
        [SerializeField] private RCCVehicleAdapter drivablePrefab;
        [Tooltip("Where the drivable prefab's root goes. Place it so the drivable model overlaps this car's model exactly.")]
        [SerializeField] private Transform drivableAnchor;
        [Tooltip("Used to rank nearby vehicles (usually beside the driver door).")]
        [SerializeField] private Transform interactionPoint;
        [Tooltip("Where the character stands to start the enter animation (same place as on the drivable prefab).")]
        [SerializeField] private Transform enterPoint;
        [Tooltip("Where the character walks to before pulling the driver out: just outside the door, clear of the body (the carjack then " +
                 "steps in to the drivable prefab's Carjack Stand Point).")]
        [SerializeField] private Transform stealPoint;
        [Tooltip("Max speed (m/s) at which the prompt is offered; the car brakes to a stop once the player commits.")]
        [SerializeField, Min(0f)] private float maxTakeoverSpeed = 8f;
        [Tooltip("Speed (m/s) below which the stopping car counts as stopped and the player can get in.")]
        [SerializeField, Min(0f)] private float stoppedSpeed = 1f;

        /// <summary>
        /// Optional source of drivable instances (e.g. pre-warmed ones handed out by a scene system). Receives the prefab and
        /// returns an active instance, or null to fall back to Instantiate.
        /// </summary>
        public static Func<RCCVehicleAdapter, RCCVehicleAdapter> DrivableProvider;

        /// <summary>Raised when a traffic car was replaced by its drivable counterpart (before the enter animation starts).</summary>
        public static event Action<TrafficVehicleAdapter, RCCVehicleAdapter> OnTakenOver;

        private static readonly List<TrafficVehicleAdapter> activeCars = new List<TrafficVehicleAdapter>();
        /// <summary>Traffic cars currently in the scene (enabled), for low-frequency scene checks such as the shot detail range.</summary>
        public static IReadOnlyList<TrafficVehicleAdapter> ActiveCars => activeCars;
        /// <summary>Raised every time a traffic car is enabled (spawned or reused from the iTS pool), after it joined <see cref="ActiveCars"/>.</summary>
        public static event Action<TrafficVehicleAdapter> OnCarEnabled;
        /// <summary>Raised every time a traffic car is disabled (pooled by iTS or taken over), after it left <see cref="ActiveCars"/>.</summary>
        public static event Action<TrafficVehicleAdapter> OnCarDisabled;
        /// <summary>Raised when something shot a traffic car (crashes do not count). Args: the car, who did it (may be null).</summary>
        public static event Action<TrafficVehicleAdapter, Transform> OnCarThreatened;
        /// <summary>Raised when the player's car bumps a traffic car hard enough for its driver to react (honk, short stop). Args: the car, impact speed (m/s).</summary>
        public static event Action<TrafficVehicleAdapter, float> OnBumpedByPlayer;

        private const float MaxWheelSlip = 0.5f;
        private const float TractionControlMinSpeed = 2f;
        private static readonly System.Reflection.MethodInfo UnReserveAll = typeof(TSTrafficAI).GetMethod("UnReserveAll",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        public TrafficVehicleState State { get; private set; } = TrafficVehicleState.TrafficControlled;
        public bool HasDriver => driverVisual != null && driverVisual.activeSelf;
        /// <summary>True once the driver was shot dead (until the car respawns in traffic).</summary>
        public bool IsDriverKilled { get; private set; }
        public float CurrentSpeed => body != null ? body.linearVelocity.magnitude : 0f;
        public bool IsWrecked => health != null && health.IsDestroyed;
        public bool IsPolice => policeDriver;
        public Rigidbody Body => body;
        /// <summary>A living driver in a normal physics car (not a far kinematic one) who can panic (bail out or flee).</summary>
        public bool CanPanic => isActiveAndEnabled && State == TrafficVehicleState.TrafficControlled && HasDriver && !IsDriverKilled && !IsWrecked
                                      && trafficAI != null && trafficAI.IsEnabled && simpleCar != null && body != null && !body.isKinematic;

        public bool CanEnter => isValid && isActiveAndEnabled && State == TrafficVehicleState.TrafficControlled
                                && !IsWrecked && trafficAI.IsEnabled && CurrentSpeed <= maxTakeoverSpeed;
        public bool IsReadyForEntry => CurrentSpeed <= stoppedSpeed;
        public VehicleEntryPrompt EntryPrompt => HasDriver ? VehicleEntryPrompt.Steal : VehicleEntryPrompt.Enter;

        private readonly List<Collider> solidColliders = new List<Collider>();
        private bool isValid;
        private Renderer[] driverRenderers = new Renderer[0];
        private Material currentOutfit;
        private int currentDriver = -1;
        private float baseBrakeTorque;
        private float baseEngineTorque;
        private float baseMaxAcceleration;
        private bool baseIgnoreTrafficLight;
        private Vector3 killDirection;
        private Vector3 bailOutThreat;
        private Coroutine handOverRoutine;
        private bool shotDetail;
        private Coroutine holdRoutine;
        private Coroutine bumpRoutine;
        private float nextBumpReaction;
        private VehiclePaintVariants paint;
        private VehicleLights lights;

        private void Awake()
        {
            if (driverVisual != null) driverRenderers = driverVisual.GetComponentsInChildren<Renderer>(true);
            paint = GetComponent<VehiclePaintVariants>();
            lights = GetComponent<VehicleLights>();
            if (simpleCar != null) baseBrakeTorque = simpleCar.brakeTorque;
            if (trafficAI != null) baseIgnoreTrafficLight = trafficAI.ignoreTrafficLight;
            if (!stealable) return;

            // Validated up front, so a takeover can never leave a half-set-up vehicle behind.
            isValid = trafficAI != null && body != null && drivablePrefab != null && drivablePrefab.HasEntrySetup
                      && drivableAnchor != null && enterPoint != null;
            if (!isValid)
            {
                Debug.LogError($"[{nameof(TrafficVehicleAdapter)}] Missing references on '{name}' (TSTrafficAI, Rigidbody, drivable prefab with " +
                               "seat/enter/exit points, drivable anchor, enter point). This traffic car cannot be entered.", this);
                return;
            }
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                if (!c.isTrigger) solidColliders.Add(c);
            }
            if (simpleCar == null) Debug.LogWarning($"[{nameof(TrafficVehicleAdapter)}] No TSSimpleCar on '{name}'; the car stops with iTS' full brake for the player.", this);
        }

        // iTS re-enables pooled cars through TSTrafficAI.Enable (GameObject.SetActive), so every reuse starts clean.
        private void OnEnable()
        {
            State = TrafficVehicleState.TrafficControlled;
            IsDriverKilled = false;
            handOverRoutine = null;
            bumpRoutine = null;
            nextBumpReaction = 0f;
            if (trafficAI != null)
            {
                trafficAI.enabled = true;
                trafficAI.ignoreTrafficLight = baseIgnoreTrafficLight;
            }
            RestoreBrakes();
            if (driverVisual != null) driverVisual.SetActive(true);
            PickOutfit();
            if (driverHealth != null)
            {
                driverHealth.ResetHealth();
                driverHealth.OnWounded += HandleDriverWounded;
                driverHealth.OnKilled += HandleDriverKilled;
            }
            if (health != null)
            {
                health.OnDestroyedWreck += HandleWrecked;
                health.OnWreckCleanup += ReturnToTraffic;
                health.OnDamaged += HandleCarDamaged;
            }
            // Detail starts off; the takeover manager's next range check switches it on near the player.
            shotDetail = true;
            SetShotDetail(false);
            activeCars.Add(this);
            OnCarEnabled?.Invoke(this);
        }

        private void OnDisable()
        {
            activeCars.Remove(this);
            OnCarDisabled?.Invoke(this);
            holdRoutine = null;
            if (driverHealth != null)
            {
                driverHealth.OnWounded -= HandleDriverWounded;
                driverHealth.OnKilled -= HandleDriverKilled;
            }
            if (health != null)
            {
                health.OnDestroyedWreck -= HandleWrecked;
                health.OnWreckCleanup -= ReturnToTraffic;
                health.OnDamaged -= HandleCarDamaged;
            }
            // Deactivated while reserved (e.g. scene teardown): make sure iTS owns the car again.
            if (State != TrafficVehicleState.TrafficControlled && trafficAI != null) trafficAI.reservedForEventTrigger = false;
            if (State == TrafficVehicleState.Fleeing) RestoreBrakes();
        }

#if UNITY_EDITOR
        public void EditorSetDriverRoster(PedDriverRoster roster, PedDriverRoster.SeatFit[] fits)
        {
            driverRoster = roster;
            driverFits = fits;
        }
        public GameObject EditorDriverVisual => driverVisual;
#endif

        public Transform GetInteractionPoint() => interactionPoint != null ? interactionPoint : transform;
        public Transform GetEnterPoint() => enterPoint;
        public Transform GetStealPoint() => HasDriver ? stealPoint : null;
        public IReadOnlyList<Collider> GetColliders() => solidColliders;

        public void ReserveForEntry()
        {
            if (!isValid || State != TrafficVehicleState.TrafficControlled) return;
            State = TrafficVehicleState.BeingCarjacked;
            trafficAI.reservedForEventTrigger = true;
            HoldCar(baseBrakeTorque * reservedBrakeScale);
        }

        public void CancelEntryReservation()
        {
            if (State != TrafficVehicleState.BeingCarjacked) return;
            State = TrafficVehicleState.TrafficControlled;
            trafficAI.reservedForEventTrigger = false;
            ReleaseCar();
            RestoreBrakes();
            if (isActiveAndEnabled) trafficAI.UnPause();
        }

        public IVehicleController CompleteEntry()
        {
            if (!isValid || State != TrafficVehicleState.BeingCarjacked || !isActiveAndEnabled)
            {
                CancelEntryReservation();
                return null;
            }

            RCCVehicleAdapter drivable = SpawnDrivable();
            OnTakenOver?.Invoke(this, drivable);
            ReturnToTraffic();
            return drivable;
        }

        // The drivable counterpart at exactly this car's pose and velocity, with the driver carried over (a killed one falls out).
        private RCCVehicleAdapter SpawnDrivable()
        {
            RCCVehicleAdapter drivable = DrivableProvider != null ? DrivableProvider(drivablePrefab) : null;
            if (drivable == null)
            {
                drivable = Instantiate(drivablePrefab);
                drivable.name = drivablePrefab.name;
            }
            drivable.transform.SetPositionAndRotation(drivableAnchor.position, drivableAnchor.rotation);
            Rigidbody drivableBody = drivable.GetComponent<Rigidbody>();
            if (drivableBody != null)
            {
                drivableBody.position = drivableAnchor.position;
                drivableBody.rotation = drivableAnchor.rotation;
                drivableBody.linearVelocity = body.linearVelocity;
                drivableBody.angularVelocity = body.angularVelocity;
            }

            // Same colour on the twin.
            if (paint != null && paint.CurrentIndex >= 0 && drivable.TryGetComponent(out VehiclePaintVariants drivablePaint)) drivablePaint.ApplyVariant(paint.CurrentIndex);

            if (health != null)
            {
                VehicleHealth drivableHealth = drivable.GetComponent<VehicleHealth>();
                if (drivableHealth != null) drivableHealth.CopyDamageFrom(health);
            }

            TrafficVehicleDriver driver = drivable.GetNpcDriver();
            if (driver != null)
            {
                if (driverRoster != null && currentDriver >= 0) driver.SetSeatedPedestrian(HasDriver, driverRoster, currentDriver);
                else driver.SetSeated(HasDriver, currentOutfit);
                if (HasDriver && IsDriverKilled) driver.DropDead(killDirection);
                else if (HasDriver && State == TrafficVehicleState.BailingOut) driver.BailOut(bailOutThreat);
            }
            else if (HasDriver) Debug.LogWarning($"[{nameof(TrafficVehicleAdapter)}] '{drivable.name}' has no TrafficVehicleDriver; the traffic driver is dropped.", drivable);
            return drivable;
        }

        /// <summary>
        /// Switches the window glass collider and the driver hit zones on (near the player) or off (far away, where they would
        /// only cost physics time). The metal body hitbox is not affected.
        /// </summary>
        public void SetShotDetail(bool on)
        {
            if (on == shotDetail) return;
            shotDetail = on;
            if (IsWrecked) on = false;
            if (glassCollider != null) glassCollider.enabled = on;
            if (driverHealth != null) driverHealth.gameObject.SetActive(on && HasDriver);
        }

        // Only while it plays: a wounded driver panics (iTS lets him run red lights).
        private void HandleDriverWounded(SeatedDriverHealth health, vDamage damage)
        {
            if (panicWhenWounded && trafficAI != null) trafficAI.ignoreTrafficLight = true;
        }

        private void HandleDriverKilled(SeatedDriverHealth health, vDamage damage)
        {
            IsDriverKilled = true;
            killDirection = health.LastHitDirection;
            // Reserved for the player (he is walking to the door): the body falls out when the car is taken over.
            bool panicking = State == TrafficVehicleState.Fleeing || State == TrafficVehicleState.BailingOut;
            if (trafficAI == null || (State != TrafficVehicleState.TrafficControlled && !panicking) || !isActiveAndEnabled) return;

            // Shot while braking to bail out: he dies at the wheel instead.
            if (handOverRoutine != null) StopCoroutine(handOverRoutine);
            State = TrafficVehicleState.DriverKilled;
            trafficAI.reservedForEventTrigger = true;
            HoldCar(baseBrakeTorque * reservedBrakeScale);
            handOverRoutine = StartCoroutine(isValid ? HandOverWhenStopped(TrafficVehicleState.DriverKilled) : RecycleWhenOutOfSight());
        }

        // No drivable twin (police car): it coasts to a stop with the slumped driver and goes back to the pool out of sight.
        private IEnumerator RecycleWhenOutOfSight()
        {
            float timer = 0f;
            while (CurrentSpeed > stoppedSpeed && timer < killedDriverStopTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            yield return DropDeadBody();

            var wait = new WaitForSeconds(1f);
            float age = 0f;
            while (age < killedDriverRecycleTime || !VehicleHealth.IsOutOfSight(transform.position, killedDriverKeepDistance))
            {
                yield return wait;
                age += 1f;
            }
            handOverRoutine = null;
            ReturnToTraffic();
        }

        // The same person who sat at the wheel, full size: spawned beside the seat and killed, so he falls as a ragdoll.
        private IEnumerator DropDeadBody()
        {
            if (driverVisual == null || !driverVisual.activeSelf) yield break;
            driverVisual.SetActive(false);
            if (driverHealth != null) driverHealth.gameObject.SetActive(false);

            PedDriverRoster.Entry entry = driverRoster != null ? driverRoster.Get(currentDriver) : null;
            if (entry == null || entry.pedPrefab == null || TrafficVehicleDriver.PersonFactory == null) yield break;
            Transform seat = driverVisual.transform.parent;
            Vector3 side = Vector3.Dot(seat.position - transform.position, transform.right) >= 0f ? transform.right : -transform.right;
            Vector3 point = seat.position + side * deadBodySideDistance;
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 5f, 1, QueryTriggerInteraction.Ignore)) point = ground.point;
            PedestrianWalker person = TrafficVehicleDriver.PersonFactory(entry.pedPrefab, point, seat.rotation);
            if (person == null) yield break;
            // One frame for the new character to initialise; damage before that does not count.
            yield return null;
            if (person != null && person.TryGetComponent(out PedestrianDamage body)) body.Kill(transform, point);
        }

        private void HandleWrecked()
        {
            if (handOverRoutine != null) StopCoroutine(handOverRoutine);
            handOverRoutine = null;
            State = TrafficVehicleState.Wrecked;
            if (driverHealth != null) driverHealth.gameObject.SetActive(false);
            if (glassCollider != null) glassCollider.enabled = false;
            if (trafficAI == null) return;
            trafficAI.reservedForEventTrigger = true;
            HoldCar(baseBrakeTorque);
        }

        // iTS' Pause alone does not hold: the AI goes back to driving after a few seconds. With the AI component off the car physics
        // keep the last input, so it brakes at the given torque, and with full brakes once it has stopped (it never rolls away).
        private void HoldCar(float brakeTorque)
        {
            trafficAI.Pause();
            trafficAI.enabled = false;
            if (lights != null) lights.ReportBrake(1f);
            if (simpleCar == null) return;
            RestoreEngine();
            simpleCar.brakeTorque = brakeTorque;
            simpleCar.OnAIUpdate(0f, 1f, 0f, false);
            if (holdRoutine != null) StopCoroutine(holdRoutine);
            if (isActiveAndEnabled) holdRoutine = StartCoroutine(FullBrakeOnceStopped());
        }

        private IEnumerator FullBrakeOnceStopped()
        {
            var wait = new WaitForSeconds(0.25f);
            while (CurrentSpeed > stoppedSpeed) yield return wait;
            holdRoutine = null;
            simpleCar.brakeTorque = baseBrakeTorque;
            simpleCar.OnAIUpdate(0f, 1f, 0f, false);
        }

        private void ReleaseCar()
        {
            if (holdRoutine != null) StopCoroutine(holdRoutine);
            holdRoutine = null;
            if (lights != null) lights.ReportBrake(0f);
            if (trafficAI != null) trafficAI.enabled = true;
        }

        // Once the car has stopped it leaves traffic like a taken-over car: an empty drivable car the player can enter,
        // protected from iTS' recycling and owned by the takeover manager's abandoned-vehicle rules.
        private IEnumerator HandOverWhenStopped(TrafficVehicleState expected)
        {
            float timer = 0f;
            while (CurrentSpeed > stoppedSpeed && timer < killedDriverStopTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            handOverRoutine = null;
            if (!isActiveAndEnabled || State != expected) yield break;

            RCCVehicleAdapter drivable = SpawnDrivable();
            OnTakenOver?.Invoke(this, drivable);
            ReturnToTraffic();
        }

        // TSTrafficAI.Disable releases the lane reservations and deactivates the car; TSTrafficSpawner then marks the
        // inactive car as free and respawns it somewhere else like any car that drove out of range.
        private void ReturnToTraffic()
        {
            trafficAI.reservedForEventTrigger = false;
            ReleaseCar();
            RestoreBrakes();
            trafficAI.Disable(true, true);
        }

        private void RestoreBrakes()
        {
            if (simpleCar != null && baseBrakeTorque > 0f) simpleCar.brakeTorque = baseBrakeTorque;
            RestoreEngine();
        }

        /// <summary>
        /// Panic, first choice: the car brakes to a stop, then it is handed over exactly like a car whose driver was shot (drivable
        /// twin, <see cref="OnTakenOver"/>, abandoned-vehicle rules), whose driver opens the door, gets out and runs from
        /// <paramref name="threat"/> (<see cref="TrafficVehicleDriver.BailOut"/>). False for cars without a drivable twin (police).
        /// </summary>
        public bool TryBeginBailOut(Vector3 threat, float brakeScale)
        {
            if (!isValid || !CanPanic) return false;
            State = TrafficVehicleState.BailingOut;
            bailOutThreat = threat;
            trafficAI.reservedForEventTrigger = true;
            HoldCar(baseBrakeTorque * Mathf.Clamp(brakeScale, 0.1f, 1f));
            handOverRoutine = StartCoroutine(HandOverWhenStopped(TrafficVehicleState.BailingOut));
            return true;
        }

        /// <summary>
        /// Panic, second choice: takes the car out of traffic so the panic manager can floor it away. No AI, no lane reservations,
        /// nothing recycles it, and a stronger engine and brakes (iTS' are sized for city traffic). The AI has to stay off, or it
        /// fights the driver for the wheels.
        /// </summary>
        public bool TryBeginFlee(float engineTorque, float brakeTorque)
        {
            if (!CanPanic) return false;
            State = TrafficVehicleState.Fleeing;
            trafficAI.reservedForEventTrigger = true;
            trafficAI.Pause();
            if (UnReserveAll != null) UnReserveAll.Invoke(trafficAI, null);
            trafficAI.enabled = false;
            if (baseEngineTorque <= 0f)
            {
                baseEngineTorque = simpleCar.EngineTorque;
                baseMaxAcceleration = simpleCar.maxAcceleration;
            }
            simpleCar.EngineTorque = engineTorque;
            simpleCar.maxAcceleration = 15f;
            simpleCar.brakeTorque = Mathf.Max(baseBrakeTorque, brakeTorque);
            return true;
        }

        /// <summary>
        /// A fleeing driver who got stuck gives up on the car: it stops and is handed over like a bail-out (door open, he runs).
        /// False for cars without a drivable twin.
        /// </summary>
        public bool TryBailOutFromFlee(Vector3 threat, float brakeScale)
        {
            if (!isValid || State != TrafficVehicleState.Fleeing || !isActiveAndEnabled || !HasDriver || IsDriverKilled) return false;
            State = TrafficVehicleState.BailingOut;
            bailOutThreat = threat;
            HoldCar(baseBrakeTorque * Mathf.Clamp(brakeScale, 0.1f, 1f));
            handOverRoutine = StartCoroutine(HandOverWhenStopped(TrafficVehicleState.BailingOut));
            return true;
        }

        /// <summary>The fleeing driver's hands and feet: the same inputs the iTS AI gives the car physics.</summary>
        public void DriveFlee(float steering, float brake, float throttle)
        {
            if (State != TrafficVehicleState.Fleeing) return;
            // Traction control: the iTS wheels spin up and lose all grip under full torque.
            if (throttle > 0f && simpleCar.BackWheels.Length > 0 && simpleCar.BackWheels[0].GetGroundHit(out WheelHit hit) && Mathf.Abs(hit.forwardSlip) > MaxWheelSlip && CurrentSpeed > TractionControlMinSpeed) throttle *= 0.4f;
            simpleCar.OnAIUpdate(steering, brake, throttle, false);
            if (lights != null) lights.ReportBrake(brake);
        }

        /// <summary>The flight is over: back to the iTS pool, which respawns the car elsewhere like any car that drove out of range.</summary>
        public void EndFlee()
        {
            if (State != TrafficVehicleState.Fleeing) return;
            State = TrafficVehicleState.TrafficControlled;
            ReturnToTraffic();
        }

        private void RestoreEngine()
        {
            if (simpleCar == null || baseEngineTorque <= 0f) return;
            simpleCar.EngineTorque = baseEngineTorque;
            simpleCar.maxAcceleration = baseMaxAcceleration;
        }

        // Metal shot by anybody (the manager decides whether it was the player). Crash damage never scares the driver.
        private void HandleCarDamaged(float amount, Transform sender)
        {
            if (health != null && health.LastDamageWasCrash) return;
            OnCarThreatened?.Invoke(this, sender);
        }

        // Bumped or rammed by the player's car: a honk and a short stop, then iTS drives on. Never a panic.
        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time < nextBumpReaction || State != TrafficVehicleState.TrafficControlled || !HasDriver || IsDriverKilled || IsWrecked) return;
            Rigidbody other = collision.rigidbody;
            if (other == null || collision.relativeVelocity.sqrMagnitude < bumpReactSpeed * bumpReactSpeed) return;
            if (!other.TryGetComponent(out RCCVehicleAdapter vehicle) || !vehicle.IsControlled) return;
            nextBumpReaction = Time.time + bumpCooldown;
            if (UnityEngine.Random.value < bumpHonkChance) CarHorn.Honk(transform, UnityEngine.Random.value < 0.5f);
            OnBumpedByPlayer?.Invoke(this, collision.relativeVelocity.magnitude);
            if (trafficAI == null || !trafficAI.enabled || !trafficAI.IsEnabled || body == null || body.isKinematic || !isActiveAndEnabled) return;
            if (bumpRoutine != null) StopCoroutine(bumpRoutine);
            bumpRoutine = StartCoroutine(BumpStopRoutine());
        }

        // iTS' own pause (brakes, AI on hold), lifted after a moment; any other sequence (panic, carjack, wreck) takes precedence.
        // Then, for Bump Watch Seconds, a flipped car or one that cannot get going is recycled once out of sight.
        private IEnumerator BumpStopRoutine()
        {
            trafficAI.Pause();
            yield return new WaitForSeconds(UnityEngine.Random.Range(bumpStopSeconds.x, Mathf.Max(bumpStopSeconds.x, bumpStopSeconds.y)));
            if (State != TrafficVehicleState.TrafficControlled) { bumpRoutine = null; yield break; }
            if (trafficAI.enabled) trafficAI.UnPause();

            var wait = new WaitForSeconds(1f);
            float watched = 0f, stopped = 0f;
            while (watched < bumpWatchSeconds && State == TrafficVehicleState.TrafficControlled)
            {
                yield return wait;
                watched += 1f;
                stopped = CurrentSpeed < stoppedSpeed ? stopped + 1f : 0f;
                bool stuck = transform.up.y < 0.5f || stopped >= bumpStuckSeconds;
                if (stuck && VehicleHealth.IsOutOfSight(transform.position, bumpRecycleKeepDistance))
                {
                    bumpRoutine = null;
                    ReturnToTraffic();
                    yield break;
                }
                if (stuck) watched = Mathf.Min(watched, bumpWatchSeconds - 1f); // keep watching until it can go unseen
            }
            bumpRoutine = null;
        }

        // Once per spawn (pooled cars are re-enabled by iTS), so there is no per-frame cost.
        private void PickOutfit()
        {
            if (driverRoster != null && driverRoster.Count > 0)
            {
                currentDriver = driverRoster.RandomIndex(policeDriver);
                driverRoster.ApplySeated(driverVisual, currentDriver, PedDriverRoster.FitOf(driverFits, currentDriver));
                PedDriverRoster.Entry entry = driverRoster.Get(currentDriver);
                if (driverHealth != null && entry != null) driverHealth.FitToDriver(entry.seatedHips, entry.seatedHeadCenter);
                return;
            }
            if (driverOutfits == null || driverOutfits.Length == 0 || driverRenderers.Length == 0) return;
            currentOutfit = driverOutfits[UnityEngine.Random.Range(0, driverOutfits.Length)];
            if (currentOutfit == null) return;
            for (int i = 0; i < driverRenderers.Length; i++) driverRenderers[i].sharedMaterial = currentOutfit;
        }
    }
}
