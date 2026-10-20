using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Combat;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Scene-side owner of the vehicles and drivers that carjacking takes out of traffic (lives under ///Manager///, not a Singleton).
    /// - Runtime-spawned vehicle prefabs cannot reference scene objects: taken-over vehicles get the scene RCC camera and a parent.
    /// - Pre-warms one inactive drivable vehicle per model outside the iTS pool (RCC's expensive Awake/Start already done), hands it
    ///   out through <see cref="TrafficVehicleAdapter.DrivableProvider"/> and replaces it a moment later.
    /// - Puts the drivable vehicles placed in the scene (Parked Scene Vehicles) to sleep beyond Dormant Distance while unused.
    /// - Owns the lifetime of taken vehicles once the player leaves them (never in the first Takeover Grace Time seconds, e.g. while
    ///   a shot driver is still falling out): far ones go dormant (RCC, wheels, audio and physics off),
    ///   ones far away and off-screen for a while are removed, and at most Max Abandoned Vehicles are kept (the oldest one that is
    ///   neither near the player nor visible goes first). The vehicle the player is in is never touched.
    /// - Removes pulled-out drivers once they are far from the player, and keeps at most Max Ejected Drivers of them.
    /// - Switches traffic cars' shot detail (window glass collider + seated driver hit zones) on within Shot Detail Range of the
    ///   player and off beyond it, so the ~120 traffic cars do not all carry the extra colliders.
    /// - Removal is two-step (moved out of reach of iTS' player sensors, destroyed a moment later) so iTS never queries a
    ///   destroyed sensor collider. Abandoned vehicles that hold up stopped traffic are removed once the player is away and
    ///   they are off-screen.
    /// All checks run on a low-frequency coroutine, not per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficTakeoverManager : MonoBehaviour
    {
        private class TakenVehicle
        {
            public RCCVehicleAdapter Adapter;
            public Renderer MainRenderer;
            public float TakenAt;
            public float AbandonedSince = -1f;
            public float OffscreenTime;
        }

        [Header("Vehicles")]
        [Tooltip("Scene RCC camera used while driving a taken-over car (the same one the player's own car uses).")]
        [SerializeField] private RCC_Camera vehicleCamera;
        [Tooltip("Hierarchy parent for taken-over (stolen / abandoned) and pre-warmed vehicles.")]
        [SerializeField] private Transform takenVehiclesParent;
        [Tooltip("Distance is measured from this transform (the player).")]
        [SerializeField] private Transform distanceReference;
        [SerializeField, Min(0.25f)] private float checkInterval = 1f;

        [Header("Pre-warmed Drivable Vehicles")]
        [Tooltip("Drivable prefabs used by traffic cars. One inactive, fully initialised instance of each is kept ready, so a takeover does not stall a frame.")]
        [SerializeField] private RCCVehicleAdapter[] prewarmedPrefabs = new RCCVehicleAdapter[0];
        [Tooltip("Where pre-warmed vehicles wait (far out of sight; they are kinematic and inactive).")]
        [SerializeField] private Vector3 prewarmPosition = new Vector3(0f, -1000f, 0f);
        [Tooltip("Seconds after a pre-warmed vehicle was used before its replacement is created.")]
        [SerializeField, Min(0f)] private float replenishDelay = 3f;

        [Header("Abandoned Vehicles")]
        [Tooltip("At most this many abandoned taken-over vehicles are kept.")]
        [SerializeField, Min(0)] private int maxAbandonedVehicles = 3;
        [Tooltip("Abandoned vehicles within this distance (m) of the player are never removed.")]
        [SerializeField, Min(0f)] private float protectedDistance = 30f;
        [Tooltip("Seconds after a takeover during which the vehicle is never removed (e.g. a car whose shot driver is still falling out).")]
        [SerializeField, Min(0f)] private float takeoverGraceTime = 15f;
        [Tooltip("Abandoned vehicles farther than this (m) go dormant; they wake up when the player comes closer.")]
        [SerializeField, Min(0f)] private float dormantDistance = 60f;
        [Tooltip("Drivable vehicles placed in the scene (not taken from traffic). They go dormant beyond Dormant Distance while " +
                 "nobody drives them, exactly like abandoned vehicles, but are never removed.")]
        [SerializeField] private RCCVehicleAdapter[] parkedSceneVehicles = new RCCVehicleAdapter[0];
        [Tooltip("Abandoned vehicles farther than this (m) ...")]
        [SerializeField, Min(0f)] private float removeDistance = 150f;
        [Tooltip("... and off-screen for this many seconds are removed.")]
        [SerializeField, Min(0f)] private float removeOffscreenTime = 5f;
        [Tooltip("Abandoned vehicles farther than this (m) are removed even if a camera still renders them (a few pixels at that range).")]
        [SerializeField, Min(0f)] private float forceRemoveDistance = 250f;

        [Tooltip("An abandoned vehicle with stopped traffic cars within this distance (m) blocks the lane ...")]
        [SerializeField, Min(0f)] private float blockingCheckRadius = 14f;
        [Tooltip("... traffic cars slower than this (m/s) count as held up ...")]
        [SerializeField, Min(0f)] private float blockedTrafficSpeed = 0.5f;
        [Tooltip("... and it is removed once the player is farther than this (m) and it has been off-screen for Blocking Offscreen Time.")]
        [SerializeField, Min(0f)] private float blockingRemoveDistance = 40f;
        [SerializeField, Min(0f)] private float blockingOffscreenTime = 2f;
        [Tooltip("Layers of the iTS traffic car colliders (their root trigger is on Traffic AI).")]
        [SerializeField] private LayerMask trafficLayers = 1 << 8;

        [Header("Removal")]
        [Tooltip("Removed vehicles are first moved out of every sensor's reach and kept this many seconds before being destroyed, so iTS' player sensors (scanning every 0.5 s) have dropped them.")]
        [SerializeField, Min(0.6f)] private float retireDelay = 1.2f;

        [Header("Shootable Traffic")]
        [Tooltip("Traffic cars within this distance (m) of the player get their window glass and driver hit zones; farther cars keep only the metal body hitbox.")]
        [SerializeField, Min(0f)] private float shotDetailRange = 60f;

        [Header("Ejected Drivers")]
        [Tooltip("Pulled-out drivers farther than this (m) are removed. 0 = never remove.")]
        [SerializeField, Min(0f)] private float driverDespawnDistance = 120f;
        [Tooltip("At most this many pulled-out drivers are kept; the oldest one no camera sees goes first.")]
        [SerializeField, Min(1)] private int maxEjectedDrivers = 6;

        private readonly List<TakenVehicle> takenVehicles = new List<TakenVehicle>();
        private readonly List<EnemyDummy> ejectedDrivers = new List<EnemyDummy>();
        private readonly Dictionary<RCCVehicleAdapter, RCCVehicleAdapter> prewarmed = new Dictionary<RCCVehicleAdapter, RCCVehicleAdapter>();
        private readonly Dictionary<RCCVehicleAdapter, bool[]> prewarmedKinematicStates = new Dictionary<RCCVehicleAdapter, bool[]>();
        private readonly Collider[] trafficHits = new Collider[16];
        private Coroutine checkRoutine;
        private int retiredCount;

        private void Awake()
        {
            if (vehicleCamera == null)
                Debug.LogError($"[{nameof(TrafficTakeoverManager)}] No RCC_Camera assigned on '{name}'; stolen traffic cars will have no vehicle camera.", this);
            if (distanceReference == null)
                Debug.LogWarning($"[{nameof(TrafficTakeoverManager)}] No distance reference on '{name}'; abandoned vehicles and pulled-out drivers are never removed.", this);
        }

        private void OnEnable()
        {
            TrafficVehicleAdapter.OnTakenOver += HandleTakenOver;
            TrafficVehicleDriver.OnDriverEjected += HandleDriverEjected;
            TrafficVehicleAdapter.DrivableProvider = TakePrewarmed;
            checkRoutine = StartCoroutine(CheckRoutine());
        }

        private void OnDisable()
        {
            TrafficVehicleAdapter.OnTakenOver -= HandleTakenOver;
            TrafficVehicleDriver.OnDriverEjected -= HandleDriverEjected;
            if (TrafficVehicleAdapter.DrivableProvider == TakePrewarmed) TrafficVehicleAdapter.DrivableProvider = null;
            if (checkRoutine != null)
            {
                StopCoroutine(checkRoutine);
                checkRoutine = null;
            }
        }

        private void Start()
        {
            for (int i = 0; i < prewarmedPrefabs.Length; i++)
            {
                if (prewarmedPrefabs[i] != null && !prewarmed.ContainsKey(prewarmedPrefabs[i])) StartCoroutine(PrewarmRoutine(prewarmedPrefabs[i], 0f));
            }
        }

        // Creates the instance active (so RCC's Awake/Start run now, not during a takeover), frozen far away, then parks it inactive.
        // Every rigidbody is frozen, not only the root: RCC's detachable parts hang on joints, and a joint is rebuilt from the
        // current pose when its object is re-activated, so a part that sagged while waiting would stay sagged for good.
        private IEnumerator PrewarmRoutine(RCCVehicleAdapter prefab, float delay)
        {
            prewarmed[prefab] = null;
            if (delay > 0f) yield return new WaitForSeconds(delay);

            RCCVehicleAdapter instance = Instantiate(prefab, prewarmPosition, Quaternion.identity, takenVehiclesParent);
            instance.name = prefab.name;
            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            var kinematicStates = new bool[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                kinematicStates[i] = bodies[i].isKinematic;
                bodies[i].isKinematic = true;
            }
            prewarmedKinematicStates[prefab] = kinematicStates;
            yield return null;
            if (instance == null) yield break;
            instance.gameObject.SetActive(false);
            prewarmed[prefab] = instance;
        }

        private RCCVehicleAdapter TakePrewarmed(RCCVehicleAdapter prefab)
        {
            if (prefab == null || !prewarmed.TryGetValue(prefab, out RCCVehicleAdapter instance) || instance == null) return null;

            prewarmed[prefab] = null;
            instance.gameObject.SetActive(true);
            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            if (prewarmedKinematicStates.TryGetValue(prefab, out bool[] kinematicStates) && kinematicStates.Length == bodies.Length)
            {
                for (int i = 0; i < bodies.Length; i++) bodies[i].isKinematic = kinematicStates[i];
            }
            if (isActiveAndEnabled) StartCoroutine(PrewarmRoutine(prefab, replenishDelay));
            return instance;
        }

        private void HandleTakenOver(TrafficVehicleAdapter trafficCar, RCCVehicleAdapter drivable)
        {
            drivable.SetVehicleCamera(vehicleCamera);
            if (takenVehiclesParent != null && drivable.transform.parent != takenVehiclesParent) drivable.transform.SetParent(takenVehiclesParent, true);

            Renderer mainRenderer = null;
            float largest = 0f;
            foreach (MeshRenderer r in drivable.GetComponentsInChildren<MeshRenderer>())
            {
                float size = r.bounds.size.sqrMagnitude;
                if (size > largest)
                {
                    largest = size;
                    mainRenderer = r;
                }
            }
            takenVehicles.Add(new TakenVehicle { Adapter = drivable, MainRenderer = mainRenderer, TakenAt = Time.time });
        }

        private void HandleDriverEjected(EnemyDummy driver)
        {
            if (driver != null && !ejectedDrivers.Contains(driver)) ejectedDrivers.Add(driver);
        }

        private IEnumerator CheckRoutine()
        {
            var wait = new WaitForSeconds(checkInterval);
            while (true)
            {
                yield return wait;
                if (distanceReference == null) continue;
                Vector3 reference = distanceReference.position;
                UpdateTakenVehicles(reference);
                UpdateParkedSceneVehicles(reference);
                UpdateEjectedDrivers(reference);
                UpdateShotDetail(reference);
            }
        }

        private void UpdateTakenVehicles(Vector3 reference)
        {
            int abandonedCount = 0;
            for (int i = takenVehicles.Count - 1; i >= 0; i--)
            {
                TakenVehicle taken = takenVehicles[i];
                if (taken.Adapter == null)
                {
                    takenVehicles.RemoveAt(i);
                    continue;
                }
                if (taken.Adapter.IsOccupied)
                {
                    taken.AbandonedSince = -1f;
                    taken.OffscreenTime = 0f;
                    continue;
                }

                if (taken.AbandonedSince < 0f) taken.AbandonedSince = Time.time;
                float distance = Vector3.Distance(taken.Adapter.transform.position, reference);
                bool visible = taken.MainRenderer != null && taken.MainRenderer.isVisible;
                taken.OffscreenTime = visible ? 0f : taken.OffscreenTime + checkInterval;
                taken.Adapter.SetDormant(distance > dormantDistance);

                bool inGrace = Time.time - taken.TakenAt < takeoverGraceTime;
                bool blockingRemovable = distance > blockingRemoveDistance && taken.OffscreenTime >= blockingOffscreenTime && IsBlockingTraffic(taken.Adapter.transform.position);
                if (!inGrace && ((distance > removeDistance && taken.OffscreenTime >= removeOffscreenTime) || (forceRemoveDistance > 0f && distance > forceRemoveDistance) || blockingRemovable))
                {
                    RemoveTaken(i);
                    continue;
                }
                abandonedCount++;
            }

            // Over the cap: remove the oldest abandoned vehicles the player can neither reach quickly nor see.
            while (abandonedCount > maxAbandonedVehicles)
            {
                int oldest = -1;
                for (int i = 0; i < takenVehicles.Count; i++)
                {
                    TakenVehicle taken = takenVehicles[i];
                    if (taken.Adapter.IsOccupied || taken.OffscreenTime <= 0f || Time.time - taken.TakenAt < takeoverGraceTime) continue;
                    if (Vector3.Distance(taken.Adapter.transform.position, reference) < protectedDistance) continue;
                    if (oldest < 0 || taken.AbandonedSince < takenVehicles[oldest].AbandonedSince) oldest = i;
                }
                if (oldest < 0) break;
                RemoveTaken(oldest);
                abandonedCount--;
            }
        }

        /// <summary>Distance (m) beyond which unoccupied abandoned and parked vehicles go dormant (graphics profiles).</summary>
        public void SetDormantDistance(float distance) => dormantDistance = Mathf.Max(5f, distance);

        private void UpdateParkedSceneVehicles(Vector3 reference)
        {
            float dormantSqr = dormantDistance * dormantDistance;
            for (int i = 0; i < parkedSceneVehicles.Length; i++)
            {
                RCCVehicleAdapter vehicle = parkedSceneVehicles[i];
                if (vehicle == null || vehicle.IsOccupied) continue;
                vehicle.SetDormant((vehicle.transform.position - reference).sqrMagnitude > dormantSqr);
            }
        }

        // True when stopped iTS traffic cars wait next to the vehicle (it holds up a lane).
        private bool IsBlockingTraffic(Vector3 position)
        {
            if (blockingCheckRadius <= 0f) return false;
            int count = Physics.OverlapSphereNonAlloc(position, blockingCheckRadius, trafficHits, trafficLayers, QueryTriggerInteraction.Collide);
            float maxSqr = blockedTrafficSpeed * blockedTrafficSpeed;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = trafficHits[i].attachedRigidbody;
                if (body != null && !body.isKinematic && body.linearVelocity.sqrMagnitude <= maxSqr) return true;
            }
            return false;
        }

        private void RemoveTaken(int index)
        {
            RCCVehicleAdapter adapter = takenVehicles[index].Adapter;
            takenVehicles.RemoveAt(index);
            StartCoroutine(RetireRoutine(adapter));
        }

        // iTS' player sensors keep the vehicle's TrafficSensorTarget collider between scans and query it every frame, so it is
        // moved out of their reach first and only destroyed after they have rescanned.
        private IEnumerator RetireRoutine(RCCVehicleAdapter adapter)
        {
            adapter.SetDormant(true);
            retiredCount++;
            adapter.transform.SetPositionAndRotation(prewarmPosition + new Vector3(50f + (retiredCount % 8) * 20f, 0f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(retireDelay);
            if (adapter != null) Destroy(adapter.gameObject);
        }

        private void UpdateShotDetail(Vector3 reference)
        {
            float rangeSqr = shotDetailRange * shotDetailRange;
            IReadOnlyList<TrafficVehicleAdapter> cars = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < cars.Count; i++)
            {
                TrafficVehicleAdapter car = cars[i];
                if (car == null) continue;
                // A car in a sequence (reserved, driver killed) keeps its detail wherever it is.
                bool near = (car.transform.position - reference).sqrMagnitude <= rangeSqr || car.State != TrafficVehicleState.TrafficControlled;
                car.SetShotDetail(near);
            }
        }

        private void UpdateEjectedDrivers(Vector3 reference)
        {
            float maxSqr = driverDespawnDistance * driverDespawnDistance;
            for (int i = ejectedDrivers.Count - 1; i >= 0; i--)
            {
                EnemyDummy driver = ejectedDrivers[i];
                if (driver == null)
                {
                    ejectedDrivers.RemoveAt(i);
                    continue;
                }
                if (driverDespawnDistance <= 0f || (driver.transform.position - reference).sqrMagnitude < maxSqr) continue;
                ejectedDrivers.RemoveAt(i);
                Destroy(driver.gameObject);
            }

            // Over the cap: the list is oldest first; remove the oldest drivers that are off-screen.
            for (int i = 0; i < ejectedDrivers.Count && ejectedDrivers.Count > maxEjectedDrivers; i++)
            {
                Renderer body = ejectedDrivers[i].GetComponentInChildren<SkinnedMeshRenderer>();
                if (body != null && body.isVisible) continue;
                Destroy(ejectedDrivers[i].gameObject);
                ejectedDrivers.RemoveAt(i);
                i--;
            }
        }
    }
}
