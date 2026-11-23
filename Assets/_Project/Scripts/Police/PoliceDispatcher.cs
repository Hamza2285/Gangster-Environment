using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Interiors;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Owns the police units on the street (scene manager under ///Manager///, not a Singleton; <see cref="WantedManager"/> holds the
    /// reference). Responsibilities:
    /// - Pools: officers on foot (<see cref="PoliceOfficerUnit"/>) and pursuit cars (<see cref="PoliceCarUnit"/>), pre-warmed one per
    ///   frame at start. Nothing is instantiated or destroyed during a chase.
    /// - Witnesses (<see cref="TryWitness"/>): our units, the patrol cars of the iTS traffic and the street officers of the pedestrian
    ///   system. A patrol car or street officer who sees a crime is swapped in place for a pursuit unit (same model, same pose), so
    ///   the cop who saw it is the one who comes after the player.
    /// - Dispatch: keeps Car Units Per Star on the player, one reinforcement per Dispatch Interval, appearing out of sight (back
    ///   along the road on the way to him). Officers on foot are capped by Foot Units Per Star (car crews included); only Walking
    ///   Reinforcements Per Star of them come on foot (on the NavMesh around the player), the rest climb out of the cars.
    /// - Recall: units far away and out of sight go back to the pool; all of them when the wanted level is lost.
    /// - Keeps the pedestrians' panic NavMesh loaded while officers are on foot.
    /// - Patrol cars of the iTS traffic drive with their light bar dark (it only flashes on pursuit cars, see <see cref="PoliceCarUnit"/>);
    ///   every police traffic car is switched dark when iTS spawns or reuses it.
    /// Runs a low-frequency tick (Tick Interval), not per-frame logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceDispatcher : MonoBehaviour
    {
        private const int BodyPartLayer = 15;

        [SerializeField] private PoliceSettings settings;
        [Tooltip("The street pedestrian system: street officers, the panic NavMesh and the layer traffic brakes for.")]
        [SerializeField] private PedestrianSpawner pedestrians;

        [Header("Units")]
        [Tooltip("Officer prefabs (street officer models). A street officer is replaced by the prefab whose name shares his model number.")]
        [SerializeField] private PoliceOfficerUnit[] officerPrefabs = new PoliceOfficerUnit[0];
        [SerializeField] private PoliceCarUnit carPrefab;
        [Tooltip("Officers in the pool (reinforcements plus crews climbing out of cars).")]
        [SerializeField, Min(1)] private int officerPoolSize = 8;
        [SerializeField, Min(1)] private int carPoolSize = 3;
        [Tooltip("The pools grow up to this many (one instantiation at a time) when every pooled unit is dead or parked in view.")]
        [SerializeField, Min(1)] private int officerPoolMax = 12;
        [SerializeField, Min(1)] private int carPoolMax = 5;

        [Header("Timing")]
        [SerializeField, Min(0.1f)] private float tickInterval = 0.5f;
        [Tooltip("Reinforcements and recalls wait until the unit is out of sight and this far (m) from the camera.")]
        [SerializeField, Min(0f)] private float outOfSightDistance = 25f;
        [Tooltip("Beyond this distance (m) from the camera a unit may be removed even on screen (too small to notice).")]
        [SerializeField, Min(30f)] private float vanishDistance = 150f;
        [Tooltip("New police cars and reinforcements on foot only appear out of view and at least this far (m) from the camera.")]
        [SerializeField, Min(10f)] private float spawnKeepDistance = 60f;
        [Tooltip("A new unit must also be at least this share of its spawn distance from the suspect in a straight line (a route " +
                 "round a block can be long while its end is just behind the corner).")]
        [SerializeField, Range(0.3f, 1f)] private float straightLineShare = 0.75f;

        public PoliceSettings Settings => settings;
        /// <summary>Officers and cars currently out (for tests and debugging).</summary>
        public int ActiveOfficerCount => PoliceOfficerUnit.Active.Count;
        public int ActiveCarCount => PoliceCarUnit.Active.Count;

        private List<PoliceOfficerUnit>[] freeOfficers;
        private readonly List<PoliceOfficerUnit> allOfficers = new List<PoliceOfficerUnit>();
        private readonly Dictionary<PoliceOfficerUnit, int> officerModel = new Dictionary<PoliceOfficerUnit, int>();
        private readonly List<PoliceCarUnit> freeCars = new List<PoliceCarUnit>();
        private readonly List<PoliceCarUnit> allCars = new List<PoliceCarUnit>();
        private readonly Collider[] overlap = new Collider[96];
        private readonly HashSet<PedestrianWalker> witnessed = new HashSet<PedestrianWalker>();
        private readonly List<RoadNetwork.Link> links = new List<RoadNetwork.Link>();
        private Transform unitRoot;
        private float nextTick;
        private float nextFootDispatch;
        private float nextCarDispatch;
        private float nextAmbientChase;
        private int lastStars;
        private bool valid;
        private PoliceCarUnit ambientCar;
        private TrafficPanicManager trafficPanic;
        private TrafficSimulationLOD trafficLod;

        private void Awake()
        {
            valid = settings != null && pedestrians != null && officerPrefabs.Length > 0 && carPrefab != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(PoliceDispatcher)}] Missing references on '{name}' (settings, PedestrianSpawner, officer prefabs, car prefab). No police units.", this);
                enabled = false;
                return;
            }
            freeOfficers = new List<PoliceOfficerUnit>[officerPrefabs.Length];
            for (int i = 0; i < freeOfficers.Length; i++) freeOfficers[i] = new List<PoliceOfficerUnit>();
            // Units are street objects, not managers: they live under their own scene root.
            unitRoot = new GameObject("Police Units").transform;
            trafficLod = FindFirstObjectByType<TrafficSimulationLOD>();
            if (!TryGetComponent<TrafficSirenYield>(out _)) gameObject.AddComponent<TrafficSirenYield>();
            nextAmbientChase = Time.time + RandomSeconds(settings.ambientChaseInterval) * 0.55f;
        }

        private void OnEnable()
        {
            if (!valid) return;
            TrafficVehicleAdapter.OnCarEnabled += HandleTrafficCarEnabled;
            IReadOnlyList<TrafficVehicleAdapter> traffic = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < traffic.Count; i++) HandleTrafficCarEnabled(traffic[i]);
        }

        private void OnDisable()
        {
            TrafficVehicleAdapter.OnCarEnabled -= HandleTrafficCarEnabled;
        }

        // A patrol car is not chasing anybody: no flashing lights (a witness is swapped for a pursuit car, which flashes).
        private static void HandleTrafficCarEnabled(TrafficVehicleAdapter car)
        {
            if (car == null || !car.IsPolice) return;
            PoliceLights.SetFlashing(car.GetComponent<PoliceLightBar>(), false);
        }

        private IEnumerator Start()
        {
            if (!valid) yield break;
            IReadOnlyList<TrafficVehicleAdapter> traffic = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < traffic.Count; i++) HandleTrafficCarEnabled(traffic[i]);
            for (int i = 0; i < officerPoolSize; i++)
            {
                CreateOfficer(i % officerPrefabs.Length);
                yield return null;
            }
            for (int i = 0; i < carPoolSize; i++)
            {
                CreateCar();
                yield return null;
            }
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextTick) return;
            nextTick = now + tickInterval;
            WantedManager wanted = WantedManager.Instance;
            if (wanted == null || wanted.Player == null) return;

            if (PoliceOfficerUnit.Active.Count > 0) pedestrians.EnsurePanicNavMesh();
            // More stars: reinforcements go out at once instead of waiting for the next dispatch slot.
            if (wanted.Stars > lastStars)
            {
                nextFootDispatch = 0f;
                nextCarDispatch = 0f;
            }
            lastStars = wanted.Stars;
            if (wanted.Stars > 0 && !wanted.IsBustInProgress) Dispatch(wanted, now);
            if (wanted.Stars == 0 && !wanted.IsBustInProgress) TryAmbientChase(wanted, now);
            Recall(wanted);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Witnesses
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// True when any police saw (or, for gunshots, heard) the crime. Street officers and patrol cars who did are swapped for
        /// pursuit units.
        /// </summary>
        public bool TryWitness(CrimeReport report, int responseStars)
        {
            if (!valid) return false;
            bool gunshot = report.Type == CrimeType.GunshotFired;
            Vector3 point = report.Position;
            bool seen = false;

            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            for (int i = 0; i < officers.Count && !seen; i++)
                seen = Witnesses(officers[i].IsStanding, officers[i].EyePosition, point, gunshot);
            IReadOnlyList<PoliceCarUnit> cars = PoliceCarUnit.Active;
            for (int i = 0; i < cars.Count && !seen; i++)
                seen = Witnesses(cars[i].WatchingPlayer,
                    cars[i].transform.position + Vector3.up * 1.5f, point, gunshot);

            seen |= WitnessPatrolCars(point, gunshot, responseStars);
            seen |= WitnessStreetOfficers(point, gunshot, responseStars);
            return seen;
        }

        private bool Witnesses(bool able, Vector3 eye, Vector3 point, bool gunshot)
        {
            if (!able) return false;
            float sqr = (eye - point).sqrMagnitude;
            if (gunshot && sqr <= settings.hearingRadius * settings.hearingRadius) return true;
            if (sqr > settings.sightRadius * settings.sightRadius) return false;
            return !Physics.Linecast(eye, point + Vector3.up * 1.2f, settings.sightBlockers, QueryTriggerInteraction.Ignore);
        }

        private bool WitnessPatrolCars(Vector3 point, bool gunshot, int responseStars)
        {
            IReadOnlyList<TrafficVehicleAdapter> traffic = TrafficVehicleAdapter.ActiveCars;
            bool seen = false;
            for (int i = traffic.Count - 1; i >= 0; i--)
            {
                TrafficVehicleAdapter car = traffic[i];
                if (car == null || !car.IsPolice || !car.HasDriver || car.IsDriverKilled || car.IsWrecked) continue;
                if (!Witnesses(true, car.transform.position + Vector3.up * 1.5f, point, gunshot)) continue;
                seen = true;
                TakeOverPatrolCar(car, responseStars);
            }
            return seen;
        }

        private bool WitnessStreetOfficers(Vector3 point, bool gunshot, int responseStars)
        {
            float radius = Mathf.Max(settings.sightRadius, gunshot ? settings.hearingRadius : 0f);
            int count = Physics.OverlapSphereNonAlloc(point, radius, overlap, 1 << BodyPartLayer, QueryTriggerInteraction.Collide);
            witnessed.Clear();
            bool seen = false;
            for (int i = 0; i < count; i++)
            {
                PedestrianWalker walker = overlap[i].GetComponentInParent<PedestrianWalker>();
                if (walker == null || !IsStreetOfficer(walker) || walker.State == PedestrianWalker.Mode.Down || !witnessed.Add(walker)) continue;
                if (!Witnesses(true, walker.transform.position + Vector3.up * settings.eyeHeight, point, gunshot)) continue;
                seen = true;
                TakeOverStreetOfficer(walker, responseStars);
            }
            witnessed.Clear();
            return seen;
        }

        // The patrol car leaves the iTS traffic and a pursuit car takes its place at the same pose and speed. Panic's flee
        // hand-over (TryBeginFlee + EndFlee) is the adapter's public way to hand a car straight back to the iTS pool.
        private void TakeOverPatrolCar(TrafficVehicleAdapter car, int responseStars)
        {
            if (!car.CanLeaveTraffic || !HasCruiserSlot(responseStars)) return;
            PoliceCarUnit unit = TakeCar();
            if (unit == null) return;
            if (!WakeTrafficForPursuit(car))
            {
                ReturnCar(unit);
                return;
            }
            Vector3 position = car.transform.position;
            Quaternion rotation = car.transform.rotation;
            Vector3 velocity = car.Body != null ? car.Body.linearVelocity : Vector3.zero;
            if (!car.TryBeginFlee(settings.carEngineTorque, 6000f))
            {
                ReturnCar(unit);
                return;
            }
            car.EndFlee();
            unit.Activate(this, position, rotation, velocity);
        }

        private void TakeOverStreetOfficer(PedestrianWalker walker, int responseStars)
        {
            if (CountStandingOfficers() >= settings.FootUnitsFor(responseStars)) return;
            PoliceOfficerUnit unit = TakeOfficer(PrefabIndexFor(walker.name));
            if (unit == null) return;
            Vector3 position = walker.transform.position;
            Quaternion rotation = walker.transform.rotation;
            pedestrians.ReleaseDown(walker); // back to the crowd's pool (works for walking and running people alike)
            pedestrians.EnsurePanicNavMesh();
            if (!unit.Activate(this, position, rotation, pedestrians.CarSensorLayer)) ReturnOfficer(unit);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Queries for the WantedManager
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>True when any officer or police car can see the point (line of sight checks, stops at the first).</summary>
        public bool CanAnyUnitSee(Vector3 point)
        {
            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            for (int i = 0; i < officers.Count; i++)
                if (officers[i].CanSee(point, settings.officerSightRadius)) return true;
            IReadOnlyList<PoliceCarUnit> cars = PoliceCarUnit.Active;
            for (int i = 0; i < cars.Count; i++)
                if (!cars[i].IsAmbient && cars[i].CanSee(point, settings.carSightRadius)) return true;
            return false;
        }

        /// <summary>True when an officer on his feet is within <paramref name="radius"/> (flat) of the point.</summary>
        public bool IsOfficerWithin(Vector3 point, float radius)
        {
            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            float sqr = radius * radius;
            for (int i = 0; i < officers.Count; i++)
            {
                if (!officers[i].TrackingSuspect) continue;
                Vector3 d = officers[i].transform.position - point;
                if (Mathf.Abs(d.y) > 2.5f) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= sqr) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Dispatch and recall
        // ---------------------------------------------------------------------------------------------------------------

        private void Dispatch(WantedManager wanted, float now)
        {
            int stars = Mathf.Max(1, wanted.Stars);
            int missing = Mathf.Min(MaxOfficers() - CountStandingOfficers(), settings.WalkingReinforcementsFor(stars) - CountWalkingOfficers());
            if (now >= nextFootDispatch && missing > 0)
            {
                nextFootDispatch = now + settings.dispatchInterval;
                // Two at a time when the street has been cleared of police (e.g. the player shot them all).
                SpawnFootReinforcement(wanted);
                if (missing > 1) SpawnFootReinforcement(wanted);
            }
            if (now >= nextCarDispatch && CountChasingCars() < settings.CarUnitsFor(stars))
            {
                nextCarDispatch = now + (SpawnCar(wanted) ? settings.dispatchInterval : Mathf.Min(1.5f, settings.dispatchInterval));
            }
        }

        private void Recall(WantedManager wanted)
        {
            // Searching units work around the last known position, however far the player has got.
            Vector3 player = wanted.IsSearching ? wanted.LastKnownPosition : wanted.PlayerPosition;
            float sqr = settings.recallDistance * settings.recallDistance;
            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            for (int i = officers.Count - 1; i >= 0; i--)
            {
                PoliceOfficerUnit unit = officers[i];
                if ((unit.transform.position - player).sqrMagnitude > sqr && CanVanish(unit.transform.position, outOfSightDistance)) Release(unit);
            }
            IReadOnlyList<PoliceCarUnit> cars = PoliceCarUnit.Active;
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                PoliceCarUnit unit = cars[i];
                if (unit.IsAmbient) continue;
                if ((unit.transform.position - player).sqrMagnitude > sqr && CanVanish(unit.transform.position, outOfSightDistance)) Release(unit);
            }
        }

        private void SpawnFootReinforcement(WantedManager wanted)
        {
            if (!pedestrians.EnsurePanicNavMesh()) return;
            PoliceOfficerUnit unit = TakeOfficer(Random.Range(0, officerPrefabs.Length));
            if (unit == null) return;
            // Reinforcements go where police last saw (or, right after a crime, know) the player, never straight to where he
            // really is: a player who slipped away is not found by units appearing next to him.
            Vector3 player = wanted.LastKnownPosition;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                float distance = Random.Range(settings.footSpawnDistance.x, settings.footSpawnDistance.y);
                Vector3 candidate = player + new Vector3(direction.x, 0f, direction.y) * distance;
                if (!pedestrians.TrySampleOpenNavPoint(candidate, 8f, out Vector3 point)) continue;
                if (FlatDistance(point, player) < settings.footSpawnDistance.x * straightLineShare) continue;
                if (!VehicleHealth.IsOutOfSight(point, spawnKeepDistance)) continue;
                Vector3 toPlayer = player - point;
                toPlayer.y = 0f;
                Quaternion facing = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
                if (unit.Activate(this, point, facing, pedestrians.CarSensorLayer)) return;
            }
            ReturnOfficer(unit);
        }

        private bool SpawnCar(WantedManager wanted)
        {
            RoadNetwork network = RoadNetwork.Instance;
            Vector3 target = wanted.LastKnownPosition;
            if (network == null || !network.TryFindNearest(target, out RoadNetwork.RoadPoint near)) return false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!TryFindCarApproach(network, near, target, settings.carSpawnDistance,
                    spawnKeepDistance, out var path, out Quaternion rotation)) continue;
                PoliceCarUnit unit = TakeCar();
                if (unit == null) return false;
                unit.Activate(this, path[0], rotation, rotation * Vector3.forward * 10f);
                unit.SetInitialApproach(path, target);
                return true;
            }
            return false;
        }

        private bool TryFindCarApproach(RoadNetwork network, RoadNetwork.RoadPoint destination, Vector3 target,
            Vector2 distance, float keepDistance, out List<Vector3> path, out Quaternion rotation)
        {
            path = network.BuildApproachPath(destination, Random.Range(distance.x, Mathf.Max(distance.x, distance.y)), links, out _);
            rotation = Quaternion.identity;
            // Walk back from the player along the approach to the first clear, unseen spot at least Car Spawn Distance away.
            float travelled = 0f;
            for (int i = path.Count - 2; i >= 0; i--)
            {
                travelled += Vector3.Distance(path[i], path[i + 1]);
                if (travelled < distance.x) continue;
                if (FlatDistance(path[i], target) < distance.x * straightLineShare) continue;
                Vector3 direction = path[i + 1] - path[i];
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f) continue;
                rotation = Quaternion.LookRotation(direction.normalized);
                if (!TryGetClearRoadSpot(path[i], rotation, out Vector3 spot)) continue;
                if (!VehicleHealth.IsOutOfSight(spot, keepDistance)) continue;
                path.RemoveRange(0, i);
                path[0] = spot;
                return true;
            }
            return false;
        }

        private void TryAmbientChase(WantedManager wanted, float now)
        {
            if (!settings.ambientChases || now < nextAmbientChase || (ambientCar != null && ambientCar.IsAmbient)
                || GamePlayer.MenuOpen || (InteriorTravelManager.Instance != null && InteriorTravelManager.Instance.IsInside)
                || freeCars.Count == 0 || wanted.PlayerManager.Health.IsDead) return;

            nextAmbientChase = now + 7f;
            if (trafficPanic == null) trafficPanic = FindFirstObjectByType<TrafficPanicManager>();
            RoadNetwork network = RoadNetwork.Instance;
            if (trafficPanic == null || network == null) return;
            IReadOnlyList<TrafficVehicleAdapter> traffic = TrafficVehicleAdapter.ActiveCars;
            if (traffic.Count == 0) return;
            int first = Random.Range(0, traffic.Count);
            int routeAttempts = 0;
            for (int i = 0; i < traffic.Count && routeAttempts < 6; i++)
            {
                TrafficVehicleAdapter suspect = traffic[(i + first) % traffic.Count];
                if (suspect == null || suspect.IsPolice || !suspect.CanLeaveTraffic) continue;
                float speed = trafficLod != null ? trafficLod.SpeedForPursuit(suspect) : suspect.CurrentSpeed;
                if (speed < 4f) continue;
                Vector3 target = suspect.transform.position;
                Vector3 towardPlayer = wanted.PlayerPosition - target;
                float height = Mathf.Abs(towardPlayer.y);
                towardPlayer.y = 0f;
                float playerDistance = towardPlayer.magnitude;
                if (playerDistance < 25f || playerDistance > 120f || height > 8f
                    || Vector3.Dot(suspect.transform.forward, towardPlayer) < -0.35f * playerDistance) continue;
                if (!network.TryFindLaneAhead(target, suspect.transform.forward, 7f, out RoadNetwork.RoadPoint near)) continue;
                routeAttempts++;
                if (!TryFindCarApproach(network, near, target, new Vector2(28f, 68f), outOfSightDistance,
                    out var path, out Quaternion rotation)) continue;
                PoliceCarUnit cruiser = TakeCar();
                if (cruiser == null) return;
                if (!WakeTrafficForPursuit(suspect)
                    || !trafficPanic.TryStartPoliceFlight(suspect, cruiser.transform, RandomSeconds(settings.ambientChaseDuration)))
                {
                    ReturnCar(cruiser);
                    continue;
                }
                ambientCar = cruiser;
                cruiser.ActivateAmbient(this, trafficPanic, suspect, path, rotation);
                nextAmbientChase = now + RandomSeconds(settings.ambientChaseInterval);
                return;
            }
        }

        private bool WakeTrafficForPursuit(TrafficVehicleAdapter car) =>
            !car.Body.isKinematic || (trafficLod != null && trafficLod.WakeForPursuit(car));

        private static float RandomSeconds(Vector2 range) => Random.Range(Mathf.Max(1f, range.x), Mathf.Max(1f, range.x, range.y));

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static bool TryGetClearRoadSpot(Vector3 point, Quaternion rotation, out Vector3 spot)
        {
            spot = point;
            const int groundMask = ~((1 << 2) | (1 << 8) | (1 << 9) | (1 << BodyPartLayer) | (1 << 21));
            if (!Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit ground, 10f, groundMask, QueryTriggerInteraction.Ignore)) return false;
            if (ground.normal.y < 0.85f) return false;
            spot = ground.point + Vector3.up * 0.25f;
            return !Physics.CheckBox(ground.point + Vector3.up * 1.2f, new Vector3(1.3f, 0.8f, 2.8f), rotation, ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        /// <summary>True when a unit at the point can disappear unnoticed: out of the camera's view and at least
        /// <paramref name="keepDistance"/> away, or simply too far (Vanish Distance) to be noticed.</summary>
        /// <summary>
        /// A cruiser placed right where the caller wants it (e.g. on the player's tail in a cutscene), already moving, chasing the
        /// wanted player like any dispatched car. False when no car is free.
        /// </summary>
        public bool SpawnChaseCar(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            PoliceCarUnit unit = TakeCar();
            if (unit == null) return false;
            unit.Activate(this, position, rotation, velocity);
            return true;
        }

        public bool CanVanish(Vector3 point, float keepDistance)
        {
            Camera camera = Camera.main;
            if (camera != null && (camera.transform.position - point).sqrMagnitude > vanishDistance * vanishDistance) return true;
            return VehicleHealth.IsOutOfSight(point, keepDistance);
        }

        /// <summary>
        /// The officer sitting in <paramref name="seat"/> of <paramref name="car"/> climbs out (same model as the seated mesh), as long as
        /// the on-foot limit of the wanted level (Foot Units Per Star, crews included) is not reached. Null when he stays in the car.
        /// </summary>
        public PoliceOfficerUnit SpawnCrewOfficerFromCar(PoliceCarUnit car, int seat)
        {
            if (car == null || car.Crew == null || CountStandingOfficers() >= MaxOfficers() || !pedestrians.EnsurePanicNavMesh()) return null;
            Vehicles.PedDriverRoster.Entry entry = car.Crew.EntryAt(seat);
            int prefab = entry != null ? PrefabIndexFor(entry.pedName) : Random.Range(0, officerPrefabs.Length);
            PoliceOfficerUnit unit = TakeOfficer(prefab);
            if (unit == null) return null;
            if (unit.ActivateFromCar(this, car, seat, pedestrians.CarSensorLayer)) return unit;
            ReturnOfficer(unit);
            return null;
        }

        /// <summary>An officer climbing out of a police car (crews may go beyond the reinforcement count, not beyond the pool).</summary>
        public bool SpawnCrewOfficer(Vector3 position, Quaternion rotation)
        {
            if (CountStandingOfficers() >= MaxOfficers() || !pedestrians.EnsurePanicNavMesh()) return false;
            PoliceOfficerUnit unit = TakeOfficer(Random.Range(0, officerPrefabs.Length));
            if (unit == null) return false;
            if (unit.Activate(this, position, rotation, pedestrians.CarSensorLayer)) return true;
            ReturnOfficer(unit);
            return false;
        }

        /// <summary>Sends every unit back to the pool at once (e.g. behind the BUSTED fade).</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < allOfficers.Count; i++) if (allOfficers[i].State != PoliceOfficerUnit.UnitState.Inactive) Release(allOfficers[i]);
            for (int i = 0; i < allCars.Count; i++) if (allCars[i].State != PoliceCarUnit.UnitState.Inactive) Release(allCars[i]);
        }

        public void Release(PoliceOfficerUnit unit)
        {
            if (unit == null) return;
            unit.Deactivate();
            ReturnOfficer(unit);
        }

        public void Release(PoliceCarUnit unit)
        {
            if (unit == null) return;
            unit.Deactivate();
            ReturnCar(unit);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Pools
        // ---------------------------------------------------------------------------------------------------------------

        private int MaxOfficers() => settings.FootUnitsFor(WantedManager.Instance != null ? Mathf.Max(1, WantedManager.Instance.Stars) : 1);

        internal bool HasCruiserSlot(int stars) => CountChasingCars() < settings.CarUnitsFor(stars);

        private static int CountStandingOfficers()
        {
            int count = 0;
            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            for (int i = 0; i < officers.Count; i++) if (officers[i].IsAlive) count++;
            return count;
        }

        // Officers on foot who did not come out of a car (reinforcements on foot, street officers who saw a crime).
        private static int CountWalkingOfficers()
        {
            int count = 0;
            IReadOnlyList<PoliceOfficerUnit> officers = PoliceOfficerUnit.Active;
            for (int i = 0; i < officers.Count; i++) if (officers[i].IsAlive && officers[i].HomeCar == null) count++;
            return count;
        }

        private static int CountChasingCars()
        {
            int count = 0;
            IReadOnlyList<PoliceCarUnit> cars = PoliceCarUnit.Active;
            // Parked cars whose crew is out count too, or every stop would bring another car (and two more officers on foot).
            for (int i = 0; i < cars.Count; i++) if (cars[i].IsOnDuty && !cars[i].IsAmbient) count++;
            return count;
        }

        // Free officer from the pool. When every pooled officer is out (typically lying dead where the player shot them), a body out
        // of sight is cleared, then the pool grows up to Officer Pool Max, and only then the body farthest from the camera is cleared.
        private PoliceOfficerUnit TakeOfficer(int preferredPrefab)
        {
            PoliceOfficerUnit unit = TakeFreeOfficer(preferredPrefab);
            if (unit != null) return unit;
            if (ReclaimCorpse(true)) return TakeFreeOfficer(preferredPrefab);
            if (allOfficers.Count < officerPoolMax)
            {
                CreateOfficer(preferredPrefab % officerPrefabs.Length);
                return TakeFreeOfficer(preferredPrefab);
            }
            return ReclaimCorpse(false) ? TakeFreeOfficer(preferredPrefab) : null;
        }

        private PoliceOfficerUnit TakeFreeOfficer(int preferredPrefab)
        {
            for (int pass = 0; pass < freeOfficers.Length; pass++)
            {
                List<PoliceOfficerUnit> list = freeOfficers[(preferredPrefab + pass) % freeOfficers.Length];
                if (list.Count == 0) continue;
                PoliceOfficerUnit unit = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                return unit;
            }
            return null;
        }

        private bool ReclaimCorpse(bool onlyUnseen)
        {
            PoliceOfficerUnit pick = null;
            float best = -1f;
            Camera camera = Camera.main;
            for (int i = 0; i < allOfficers.Count; i++)
            {
                PoliceOfficerUnit unit = allOfficers[i];
                if (unit.State != PoliceOfficerUnit.UnitState.Dead) continue;
                Vector3 position = unit.transform.position;
                if (onlyUnseen && !CanVanish(position, outOfSightDistance)) continue;
                float distance = camera != null ? (camera.transform.position - position).sqrMagnitude : 0f;
                if (distance <= best) continue;
                best = distance;
                pick = unit;
            }
            if (pick == null) return false;
            Release(pick);
            return true;
        }

        private void ReturnOfficer(PoliceOfficerUnit unit)
        {
            List<PoliceOfficerUnit> list = freeOfficers[officerModel[unit]];
            if (!list.Contains(unit)) list.Add(unit);
        }

        // Free car from the pool. When all are out, a parked (crew out) or wrecked car out of sight is recalled, else the pool grows.
        private PoliceCarUnit TakeCar()
        {
            if (freeCars.Count == 0 && !ReclaimParkedCar() && allCars.Count < carPoolMax) CreateCar();
            if (freeCars.Count == 0) return null;
            PoliceCarUnit unit = freeCars[freeCars.Count - 1];
            freeCars.RemoveAt(freeCars.Count - 1);
            return unit;
        }

        private bool ReclaimParkedCar()
        {
            for (int i = 0; i < allCars.Count; i++)
            {
                PoliceCarUnit unit = allCars[i];
                bool parked = unit.State == PoliceCarUnit.UnitState.Deployed || unit.State == PoliceCarUnit.UnitState.Wrecked;
                if (!parked || !CanVanish(unit.transform.position, outOfSightDistance)) continue;
                Release(unit);
                return true;
            }
            return false;
        }

        private void ReturnCar(PoliceCarUnit unit)
        {
            if (!freeCars.Contains(unit)) freeCars.Add(unit);
        }

        // Created active far below the city so Awake (ragdoll, damage, physics) runs now instead of on first use, then parked.
        private void CreateOfficer(int prefabIndex)
        {
            PoliceOfficerUnit unit = Instantiate(officerPrefabs[prefabIndex], new Vector3(0f, -2000f, 0f), Quaternion.identity, unitRoot);
            unit.name = officerPrefabs[prefabIndex].name + " " + allOfficers.Count;
            unit.gameObject.SetActive(false);
            allOfficers.Add(unit);
            officerModel.Add(unit, prefabIndex);
            freeOfficers[prefabIndex].Add(unit);
        }

        private void CreateCar()
        {
            // Cars stay scene roots: iTS' wheels find their car through transform.root.
            PoliceCarUnit unit = Instantiate(carPrefab, new Vector3(0f, -2000f, 0f), Quaternion.identity);
            unit.name = carPrefab.name + " " + allCars.Count;
            unit.gameObject.SetActive(false);
            allCars.Add(unit);
            freeCars.Add(unit);
        }

        private int PrefabIndexFor(string walkerName)
        {
            for (int i = 0; i < officerPrefabs.Length; i++)
            {
                string model = officerPrefabs[i].name;
                char number = model[model.Length - 1];
                if (walkerName.IndexOf("_" + number) >= 0) return i;
            }
            return 0;
        }

        /// <summary>True for a street officer of the pedestrian system (Hamza's police walkers).</summary>
        public static bool IsStreetOfficer(PedestrianWalker walker) => walker != null && IsStreetOfficerName(walker.name);

        public static bool IsStreetOfficerName(string objectName) => objectName.IndexOf("Police", System.StringComparison.Ordinal) >= 0;
    }
}
