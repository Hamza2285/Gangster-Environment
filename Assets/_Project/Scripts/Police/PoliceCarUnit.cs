using System;
using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Interiors;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Brain of one police pursuit car (pooled by <see cref="PoliceDispatcher"/>; per-instance, never a Singleton). The car is the
    /// police traffic car without its iTS traffic AI; it is driven by Hamza's <see cref="ChaseDriver"/> (pure pursuit along a path,
    /// swerving round traffic, recovery when stuck), added for the pursuit and removed afterwards. This brain only feeds it paths:
    /// - Pursue: a road route to the player (<see cref="RoadNetwork"/>), straight at him when close; behind the player's car it keeps
    ///   Chase Gap. When lethal, the passenger fires from the car within Car Fire Range.
    /// - Deployed: the suspect is on foot (or stopped in his car) close by: the car stops and its officers climb out one after the
    ///   other with the car-exit clip and the doors (<see cref="PoliceCarCrew"/>, <see cref="PoliceDispatcher.SpawnCrewOfficerFromCar"/>),
    ///   only as many as the on-foot limit of the wanted level allows; the rest stay in the car. When the suspect drives off (or the
    ///   wanted level is lost) the crew runs back, gets in and the car chases again (GTA style: police stay with their cars).
    /// - Search: drives to the last known position and waits there.
    /// - Return: siren off, drives away and is recalled once out of sight.
    /// - Wrecked: blown up; cleared through <see cref="VehicleHealth.OnWreckCleanup"/>.
    /// Hits are reported through <see cref="OnAnyDamaged"/> / <see cref="OnAnyWrecked"/> (crimes against the police).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class PoliceCarUnit : MonoBehaviour
    {
        public enum UnitState { Inactive, Pursue, Deployed, Search, Return, Wrecked, AmbientPursuit }

        private const float RouteRefreshDistance = 10f;

        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private TSSimpleCar simpleCar;
        [SerializeField] private VehicleHealth health;
        [Tooltip("Gun point at the passenger window (fires while lethal).")]
        [SerializeField] private PoliceSidearm gun;
        [SerializeField] private PoliceLightBar lightBar;
        [SerializeField] private MapBlip mapBlip;
        [SerializeField] private AudioSource siren;
        [Tooltip("Seated crew visuals, hidden once the officers have climbed out (only used without a Police Car Crew).")]
        [SerializeField] private GameObject[] seatedCrew = new GameObject[0];
        [Tooltip("Seated officers, doors and exit points. Without it the officers appear beside the car (Crew Exit Offsets).")]
        [SerializeField] private PoliceCarCrew crew;

        [Header("Crew")]
        [Tooltip("Where officers stand when they climb out (local to the car: driver side, passenger side).")]
        [SerializeField] private Vector3[] crewExitOffsets = { new Vector3(-1.8f, 0f, 0.4f), new Vector3(1.8f, 0f, -0.4f) };
        [Tooltip("Seconds after stopping before the officers are out.")]
        [SerializeField, Min(0f)] private float crewExitDelay = 0.6f;

        [Header("Driving")]
        [Tooltip("Within this distance (m) the car drives straight at the player instead of following the road.")]
        [SerializeField, Min(5f)] private float directApproachDistance = 35f;
        [Tooltip("The player off the road but within this distance (m) of the route's end: the route is extended to him.")]
        [SerializeField, Min(0f)] private float routeExtendDistance = 25f;
        [Tooltip("Suspect on foot off the road: the crew gets out at the end of the route when this close (m).")]
        [SerializeField, Min(5f)] private float crewWalkDistance = 55f;
        [Tooltip("Chasing a car and farther behind than this (m), off camera: moved up along its path to 60 % of it.")]
        [SerializeField, Min(40f)] private float catchUpDistance = 100f;
        [Tooltip("Top speed share while searching.")]
        [SerializeField, Range(0.2f, 1f)] private float searchSpeedShare = 0.55f;
        [Tooltip("A returning car is removed once out of sight and at least this far (m) from the camera.")]
        [SerializeField, Min(5f)] private float recallKeepDistance = 40f;

        [Header("Siren")]
        [SerializeField, Range(0f, 1f)] private float sirenVolume = 0.75f;
        [Tooltip("Recorded siren loop. Empty = a generated wail.")]
        [SerializeField] private AudioClip sirenRecording;

        private static readonly List<PoliceCarUnit> active = new List<PoliceCarUnit>();
        /// <summary>Police cars out right now (any state but wrecked).</summary>
        public static IReadOnlyList<PoliceCarUnit> Active => active;

        /// <summary>Raised when a police car takes damage. Args: the car, who did it (may be null), whether it was a crash.</summary>
        public static event Action<PoliceCarUnit, Transform, bool> OnAnyDamaged;
        /// <summary>Raised when a police car blows up. Args: the car, the last one who damaged it (may be null).</summary>
        public static event Action<PoliceCarUnit, Transform> OnAnyWrecked;

        public UnitState State { get; private set; } = UnitState.Inactive;
        /// <summary>Chasing (counts towards the cars the dispatcher keeps on the player).</summary>
        public bool IsChasing => State == UnitState.Pursue || State == UnitState.Search;
        /// <summary>On the job (chasing, searching or parked at the suspect with its crew out): counts towards Car Units Per Star.</summary>
        public bool IsOnDuty => IsChasing || State == UnitState.Deployed;
        public bool IsAmbient { get; private set; }
        public bool SirenActive { get; private set; }
        internal bool WatchingPlayer => IsOnDuty && !IsAmbient && !crewOut;
        public float Speed => body.linearVelocity.magnitude;
        public PoliceCarCrew Crew => crew;

        private PoliceDispatcher owner;
        private PoliceSettings settings;
        private WantedManager wanted;
        private ChaseDriver chase;
        private TrafficPanicManager ambientTraffic;
        private TrafficVehicleAdapter ambientSuspect;
        private Transform lastAttacker;
        private readonly List<Vector3> route = new List<Vector3>(128);
        private readonly List<Vector3> laneRoute = new List<Vector3>(128);
        private readonly List<RoadNetwork.Link> links = new List<RoadNetwork.Link>();
        private float nextRepath;
        private float nextShot;
        private float nextCatchUp;
        private float stateSince;
        private float stoppedSince = -1f;
        private float nextDeployTime;
        private Vector3 lastRouteTarget;
        // Each searching car checks its own part of the search area.
        private Vector3 searchOffset;
        private bool routeWasRoad;
        private bool crewOut;
        private bool boarding;
        private float boardingSince;
        private Coroutine exitRoutine;
        private PoliceOfficerUnit[] crewOnFoot = new PoliceOfficerUnit[0];
        private bool valid;

        private static AudioClip sirenClip;

        private void Awake()
        {
            valid = body != null && simpleCar != null && health != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(PoliceCarUnit)}] Missing references on '{name}' (Rigidbody, TSSimpleCar, VehicleHealth).", this);
                enabled = false;
                return;
            }
            PoliceLights.SetFlashing(lightBar, false);
            if (crew != null) crewOnFoot = new PoliceOfficerUnit[crew.SeatCount];
            if (siren != null)
            {
                siren.clip = sirenRecording != null ? sirenRecording : SirenClip;
                siren.loop = true;
                siren.playOnAwake = false;
            }
        }

        private void OnEnable()
        {
            if (!valid) return;
            health.OnDamaged += HandleDamaged;
            health.OnDestroyedWreck += HandleWrecked;
            health.OnWreckCleanup += HandleWreckCleanup;
        }

        private void OnDisable()
        {
            active.Remove(this);
            if (health == null) return;
            health.OnDamaged -= HandleDamaged;
            health.OnDestroyedWreck -= HandleWrecked;
            health.OnWreckCleanup -= HandleWreckCleanup;
        }

        /// <summary>Puts the car on the road at the given pose and speed and starts the chase.</summary>
        public void Activate(PoliceDispatcher dispatcher, Vector3 position, Quaternion rotation, Vector3 velocity) =>
            Activate(dispatcher, position, rotation, velocity, UnitState.Pursue);

        internal void ActivateAmbient(PoliceDispatcher dispatcher, TrafficPanicManager traffic, TrafficVehicleAdapter suspect,
            List<Vector3> approach, Quaternion rotation)
        {
            ambientTraffic = traffic;
            ambientSuspect = suspect;
            Activate(dispatcher, approach[0], rotation, rotation * Vector3.forward * 11f, UnitState.AmbientPursuit);
            chase.target = suspect.transform;
            SetInitialApproach(approach, suspect.transform.position);
        }

        internal void SetInitialApproach(List<Vector3> approach, Vector3 target)
        {
            chase.SetPath(approach);
            routeWasRoad = true;
            lastRouteTarget = target;
            nextRepath = Time.time + 0.5f;
        }

        private void Activate(PoliceDispatcher dispatcher, Vector3 position, Quaternion rotation, Vector3 velocity, UnitState state)
        {
            IsAmbient = state == UnitState.AmbientPursuit;
            owner = dispatcher;
            settings = dispatcher.Settings;
            wanted = WantedManager.Instance;
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;

            chase = gameObject.AddComponent<ChaseDriver>();
            chase.topSpeed = settings.carTopSpeed;
            chase.cornerSpeed = settings.carCornerSpeed;
            chase.targetGap = settings.carChaseGap;
            chase.drifting = false;
            chase.endGap = 5f;
            chase.Begin(simpleCar, settings.carEngineTorque);

            crewOut = false;
            boarding = false;
            ReleaseCrewOnFoot();
            if (crew != null) crew.Board(settings.officersPerCar);
            else for (int i = 0; i < seatedCrew.Length; i++) if (seatedCrew[i] != null) seatedCrew[i].SetActive(true);
            lastAttacker = null;
            nextRepath = 0f;
            nextShot = 0f;
            stoppedSince = -1f;
            nextDeployTime = 0f;
            SetState(state);
            if (!active.Contains(this)) active.Add(this);
        }

        /// <summary>Back to the pool (called by the dispatcher).</summary>
        public void Deactivate()
        {
            EndAmbientFlight();
            IsAmbient = false;
            active.Remove(this);
            State = UnitState.Inactive;
            if (exitRoutine != null) StopCoroutine(exitRoutine);
            exitRoutine = null;
            ReleaseCrewOnFoot();
            RemoveDriver();
            SetEmergency(false);
            if (mapBlip != null) mapBlip.enabled = false;
            gameObject.SetActive(false);
        }

        /// <summary>True when the car's crew can see the point (within Car Sight Radius, nothing solid in between).</summary>
        public bool CanSee(Vector3 point, float radius)
        {
            if (!WatchingPlayer) return false;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            Vector3 target = point + Vector3.up * 1.2f;
            float sqr = (target - eye).sqrMagnitude;
            if (sqr <= settings.awarenessRadius * settings.awarenessRadius) return true;
            if (sqr > radius * radius) return false;
            return !Physics.Linecast(eye, target, settings.sightBlockers, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            if (State == UnitState.Inactive || State == UnitState.Wrecked) return;
            // Stars: whatever else it was doing, the player comes first.
            if (IsAmbient && wanted != null && wanted.Stars > 0 && owner.HasPatrolSlot(wanted.Stars))
            {
                EndAmbientFlight();
                IsAmbient = false;
                SetState(wanted.IsSearching ? UnitState.Search : UnitState.Pursue);
            }
            if (IsAmbient)
            {
                if (State == UnitState.AmbientPursuit) PursueAmbient();
                else Return();
                return;
            }
            if (wanted == null)
            {
                owner.Release(this);
                return;
            }

            // A parked car with its crew out waits for them to get back in before it leaves (Deployed handles that).
            if (State == UnitState.Deployed && crewOut)
            {
                Deployed();
                return;
            }
            bool dispatchCruisers = settings.CarUnitsFor(wanted.Stars) > 0;
            if (!dispatchCruisers && State != UnitState.Return) SetState(UnitState.Return);
            else if (dispatchCruisers && State == UnitState.Return && owner.HasPatrolSlot(wanted.Stars))
                SetState(wanted.IsSearching ? UnitState.Search : UnitState.Pursue);
            else if (wanted.IsSearching && State == UnitState.Pursue) SetState(UnitState.Search);
            else if (!wanted.IsSearching && State == UnitState.Search) SetState(UnitState.Pursue);

            switch (State)
            {
                case UnitState.Pursue: Pursue(); break;
                case UnitState.Deployed: Deployed(); break;
                case UnitState.Search: Search(); break;
                case UnitState.Return: Return(); break;
            }
        }

        private void Pursue()
        {
            Transform targetTransform = wanted.PlayerTarget;
            Vector3 target = targetTransform.position;
            float distance = FlatDistance(target);
            bool suspectStopped = !wanted.PlayerInVehicle || wanted.PlayerSpeed < 1f;
            // As near as the road gets to a suspect in a park or an alley: the crew goes the rest of the way on foot.
            bool roadEnds = !wanted.PlayerInVehicle && routeWasRoad && distance <= crewWalkDistance && Speed < 1.5f
                            && chase.RemainingOnPath < 9f && Time.time - stateSince > 3f;
            if (suspectStopped && Speed < 9f && (distance <= settings.carDeployDistance || roadEnds) && settings.officersPerCar > 0 && Time.time >= nextDeployTime)
            {
                SetState(UnitState.Deployed);
                return;
            }

            bool ram = wanted.PlayerInVehicle && wanted.IsLethal && wanted.Stars >= settings.ramFromStars;
            FollowTarget(targetTransform, wanted.PlayerInVehicle, ram);
            float now = Time.time;

            if (wanted.IsLethal && gun != null && distance <= settings.carFireRange && now >= nextShot)
            {
                nextShot = now + UnityEngine.Random.Range(settings.fireInterval.x, settings.fireInterval.y) * 1.5f;
                if (PoliceGunfire.HasLineOfFire(gun.Muzzle.position, wanted, settings, out _)) PoliceGunfire.ShootPlayer(gun, transform, wanted, settings);
            }
        }

        private void PursueAmbient()
        {
            if (wanted == null || wanted.Stars > 0 || !settings.ambientChases || ambientTraffic == null
                || (InteriorTravelManager.Instance != null && InteriorTravelManager.Instance.IsInside)
                || !ambientTraffic.IsPoliceFlightActive(ambientSuspect))
            {
                EndAmbientFlight();
                SetState(UnitState.Return);
                return;
            }
            FollowTarget(ambientSuspect.transform, true, false);
        }

        private void EndAmbientFlight()
        {
            if (ambientTraffic != null) ambientTraffic.EndPoliceFlight(ambientSuspect);
            ambientTraffic = null;
            ambientSuspect = null;
        }

        private void FollowTarget(Transform targetTransform, bool driving, bool ram)
        {
            float now = Time.time;
            if (now < nextRepath) return;
            nextRepath = now + settings.carRepathInterval * UnityEngine.Random.Range(0.8f, 1.2f);
            chase.target = driving ? targetTransform : null;
            chase.targetGap = ram ? 0f : settings.carChaseGap;
            chase.cautious = !ram;
            // Left far behind and out of view: closes up along its path, as if it had driven faster there - but only to just beyond
            // the distance it can see him from, so a player who keeps outrunning it can still get away (it has to drive the rest).
            float catchUpTo = Mathf.Max(catchUpDistance * 0.6f, settings.carSightRadius + 25f);
            float catchUpFrom = Mathf.Max(catchUpDistance, catchUpTo + 40f);
            if (driving && !IsAmbient && now >= nextCatchUp && FlatDistance(targetTransform.position) > catchUpFrom
                && chase.CatchUp(targetTransform.position, catchUpTo)) nextCatchUp = now + 6f;

            Vector3 target = targetTransform.position;
            // On the trail of a moving car: the path extends itself. A new route only when the trail was lost or is a long way round.
            if (driving && routeWasRoad && chase.RemainingOnPath > 0.5f)
            {
                Vector3 toEnd = target - chase.LastPathPoint;
                toEnd.y = 0f;
                if (toEnd.magnitude < ChaseDriver.TrailReach && chase.RemainingOnPath < FlatDistance(target) * 2f + 60f)
                {
                    if (chase.Stopped) chase.SetStopped(false);
                    return;
                }
            }
            bool direct = FlatDistance(target) <= directApproachDistance && HasClearApproach(transform.position, target);
            bool stale = (target - lastRouteTarget).sqrMagnitude > RouteRefreshDistance * RouteRefreshDistance
                         || chase.RemainingOnPath < RouteRefreshDistance * 2f || !routeWasRoad;
            if (direct || stale) RefreshRoute(target, direct);
        }

        private void Deployed()
        {
            if (crewOut)
            {
                if (crew != null) WatchCrew();
                return;
            }
            // Nobody climbs out of a moving car: an officer placed in his seat would be left behind (and run over) by it.
            if (Speed > 1.5f)
            {
                if (Time.time - stateSince > 6f)
                {
                    nextDeployTime = Time.time + 3f;
                    SetState(UnitState.Pursue);
                }
                return;
            }
            if (stoppedSince < 0f) stoppedSince = Time.time;
            if (Time.time - stoppedSince < crewExitDelay) return;
            crewOut = true;
            if (crew != null)
            {
                exitRoutine = StartCoroutine(CrewExitRoutine());
                return;
            }
            Vector3 toPlayer = wanted.PlayerTarget.position - transform.position;
            toPlayer.y = 0f;
            Quaternion facing = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : transform.rotation;
            int count = Mathf.Min(settings.officersPerCar, crewExitOffsets.Length);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (owner.SpawnCrewOfficer(transform.TransformPoint(crewExitOffsets[i]), facing)) spawned++;
            }
            if (spawned > 0)
            {
                for (int i = 0; i < seatedCrew.Length; i++) if (seatedCrew[i] != null) seatedCrew[i].SetActive(false);
                return;
            }
            // Nobody could get out (no free officer, no NavMesh): keep chasing and try again later.
            crewOut = false;
            nextDeployTime = Time.time + 5f;
            SetState(UnitState.Pursue);
        }

        // One officer after the other (driver first), each only while the on-foot limit of the wanted level allows; the others stay seated.
        private IEnumerator CrewExitRoutine()
        {
            // Store the coroutine handle before a blocked or single-seat exit can complete.
            yield return null;
            int spawned = 0;
            for (int seat = 0; seat < crew.SeatCount && State == UnitState.Deployed; seat++)
            {
                if (!crew.IsOccupied(seat)) continue;
                if (spawned > 0) yield return new WaitForSeconds(settings.crewExitStagger);
                if (State != UnitState.Deployed) break;
                PoliceOfficerUnit officer = owner.SpawnCrewOfficerFromCar(this, seat);
                if (officer == null) continue;
                crew.SetSeated(seat, false);
                crewOnFoot[seat] = officer;
                spawned++;
            }
            exitRoutine = null;
            if (spawned > 0 || State != UnitState.Deployed) yield break;
            // Nobody could get out (on-foot limit reached, no free officer, no NavMesh): keep chasing in the car, try again later.
            crewOut = false;
            nextDeployTime = Time.time + 5f;
            SetState(UnitState.Pursue);
        }

        // Crew out: once the suspect drives away (or the wanted level is gone) they are called back; when all are in (or gave up,
        // or died) the car chases or leaves again.
        private void WatchCrew()
        {
            if (exitRoutine != null) return;
            if (!boarding)
            {
                Transform target = wanted.PlayerTarget;
                bool suspectGone = settings.CarUnitsFor(wanted.Stars) == 0 || wanted.IsSearching
                                   || (wanted.PlayerInVehicle && wanted.PlayerSpeed > settings.crewReboardSpeed && FlatDistance(target.position) > settings.crewReboardDistance)
                                   || FlatDistance(target.position) > settings.crewReboardDistance * 2.5f;
                if (!suspectGone) return;
                boarding = true;
                boardingSince = Time.time;
            }

            bool waiting = false;
            for (int i = 0; i < crewOnFoot.Length; i++)
            {
                PoliceOfficerUnit officer = crewOnFoot[i];
                if (officer == null) continue;
                if (officer.HomeCar != this || !officer.IsAlive) { crewOnFoot[i] = null; continue; }
                // An officer may still be playing the exit animation when the recall starts.
                if (officer.State != PoliceOfficerUnit.UnitState.Boarding) officer.BeginBoarding(this);
                waiting = true;
            }
            if (waiting && Time.time - boardingSince < settings.crewBoardTimeout) return;

            ReleaseCrewOnFoot();
            boarding = false;
            crewOut = false;
            nextDeployTime = Time.time + 6f;
            SetState(settings.CarUnitsFor(wanted.Stars) == 0 ? UnitState.Return
                : wanted.IsSearching ? UnitState.Search : UnitState.Pursue);
        }

        /// <summary>An officer of this car got back into his seat (he goes back to the pool; the seated mesh shows him again).</summary>
        public void OnCrewBoarded(PoliceOfficerUnit officer, int seat)
        {
            if (seat >= 0 && seat < crewOnFoot.Length && crewOnFoot[seat] == officer) crewOnFoot[seat] = null;
            if (crew == null) return;
            crew.SetSeated(seat, true);
            crew.CloseDoor(seat);
        }

        /// <summary>An officer of this car will not get back in (shot, knocked down, too far): his seat stays empty.</summary>
        public void OnCrewGaveUp(PoliceOfficerUnit officer)
        {
            for (int i = 0; i < crewOnFoot.Length; i++) if (crewOnFoot[i] == officer) crewOnFoot[i] = null;
        }

        // Officers still out on foot carry on without the car.
        private void ReleaseCrewOnFoot()
        {
            for (int i = 0; i < crewOnFoot.Length; i++)
            {
                PoliceOfficerUnit officer = crewOnFoot[i];
                crewOnFoot[i] = null;
                if (officer != null && officer.HomeCar == this) officer.ClearHomeCar();
            }
        }

        private void Search()
        {
            if (Time.time < nextRepath) return;
            nextRepath = Time.time + settings.carRepathInterval * 2f;
            chase.target = null;
            chase.topSpeed = settings.carTopSpeed * searchSpeedShare;
            Vector3 point = wanted.LastKnownPosition + searchOffset;
            if (FlatDistance(point) < 15f)
            {
                chase.SetStopped(true);
                return;
            }
            if (routeWasRoad && (point - lastRouteTarget).sqrMagnitude < RouteRefreshDistance * RouteRefreshDistance)
            {
                if (chase.RemainingOnPath < 5f) chase.SetStopped(true);
                return;
            }
            RefreshRoute(point, false);
        }

        private void Return()
        {
            if (Time.time - stateSince > 2f && owner.CanVanish(transform.position, recallKeepDistance))
            {
                owner.Release(this);
                return;
            }
            if (chase == null) return;
            if (nextRepath == float.MaxValue)
            {
                if (!chase.Stopped && chase.RemainingOnPath < 5f) chase.SetStopped(true);
                return;
            }
            nextRepath = float.MaxValue; // one route away is enough
            chase.target = null;
            chase.topSpeed = settings.carTopSpeed * searchSpeedShare;
            chase.SetStopped(false);
            RoadNetwork network = RoadNetwork.Instance;
            if (network != null && network.TryFindLaneAhead(transform.position, transform.forward, 8f, out RoadNetwork.RoadPoint start))
            {
                List<Vector3> away = network.BuildDepartPath(start, 200f, links);
                if (away != null && away.Count > 1)
                {
                    away.Insert(0, transform.position);
                    chase.SetPath(away);
                    return;
                }
            }
            chase.SetStopped(true);
        }

        private bool HasClearApproach(Vector3 from, Vector3 point) =>
            // As wide as the car, not just a thin line past a corner.
            !Physics.SphereCast(from + Vector3.up * 1.6f, 1.05f, point - from, out _, Vector3.Distance(from, point),
                settings.sightBlockers, QueryTriggerInteraction.Ignore);

        private void RefreshRoute(Vector3 point, bool direct)
        {
            if (!TryBuildRoute(point, direct))
            {
                if (chase.RemainingOnPath < 6f) chase.SetStopped(true);
                return;
            }
            lastRouteTarget = point;
            // (A straight run at a car is extended by its trail just as well.)
            routeWasRoad = !direct || chase.target != null;
            chase.SetPath(route);
            if (chase.Stopped) chase.SetStopped(false);
        }

        private bool TryBuildRoute(Vector3 point, bool direct)
        {
            Vector3 from = transform.position;
            RoadNetwork network = direct ? null : RoadNetwork.Instance;
            if (network != null && FindRoadRoute(network, from, point))
            {
                route.Insert(0, from);
                Vector3 end = route[route.Count - 1];
                end.y = point.y;
                if ((end - point).sqrMagnitude <= routeExtendDistance * routeExtendDistance && HasClearApproach(route[route.Count - 1], point))
                    route.Add(point);
                return route.Count > 1;
            }
            if (!direct) return false;
            route.Clear();
            route.Add(from);
            route.Add(point);
            return true;
        }

        // Along the lane the car is on; only turns round (or joins another lane) when that is much shorter and nothing solid is between.
        private bool FindRoadRoute(RoadNetwork network, Vector3 from, Vector3 point)
        {
            float ahead = Mathf.Clamp(Speed * 0.6f, 5f, 14f);
            bool any = network.TryFindRoute(from + transform.forward * 6f, point, route);
            bool reachable = any && HasClearApproach(from, route[0]);
            if (reachable && Vector3.Dot(transform.forward, route[0] - from) > 0f
                && Vector3.Dot(transform.forward, route[Mathf.Min(1, route.Count - 1)] - route[0]) > 0f) return true;
            if (network.TryFindLaneAhead(from, transform.forward, ahead, out RoadNetwork.RoadPoint start)
                && network.TryFindNearest(point, out RoadNetwork.RoadPoint end)
                && network.TryFindRoute(start, end, laneRoute, links)
                && (!reachable || Length(laneRoute) < Length(route) * 1.6f + 60f))
            {
                route.Clear();
                route.AddRange(laneRoute);
                return true;
            }
            return any;
        }

        private static float Length(List<Vector3> points)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
            return total;
        }

        private void SetState(UnitState state)
        {
            if (state == UnitState.Search && State != UnitState.Search && wanted != null)
            {
                Vector2 spread = UnityEngine.Random.insideUnitCircle * settings.SearchRadiusFor(wanted.Stars) * settings.carSearchSpread;
                searchOffset = new Vector3(spread.x, 0f, spread.y);
            }
            State = state;
            stateSince = Time.time;
            nextRepath = 0f;
            routeWasRoad = false;
            stoppedSince = -1f;
            if (chase == null) return;
            switch (state)
            {
                case UnitState.Pursue:
                case UnitState.AmbientPursuit:
                    chase.topSpeed = settings.carTopSpeed;
                    chase.cautious = true;
                    chase.SetStopped(false);
                    SetEmergency(true);
                    if (mapBlip != null) mapBlip.enabled = !IsAmbient;
                    break;
                case UnitState.Deployed:
                    chase.SetStopped(true);
                    break;
                case UnitState.Search:
                    chase.cautious = true;
                    SetEmergency(true);
                    if (mapBlip != null) mapBlip.enabled = true;
                    break;
                case UnitState.Return:
                    chase.cautious = true;
                    SetEmergency(false);
                    if (mapBlip != null) mapBlip.enabled = false;
                    break;
            }
        }

        // Light bar and siren together: on while chasing or searching, dark on the way back (patrol look).
        private void SetEmergency(bool on)
        {
            SirenActive = on;
            PoliceLights.SetFlashing(lightBar, on);
            if (siren == null || siren.clip == null) return;
            if (on && !siren.isPlaying)
            {
                siren.volume = sirenVolume;
                siren.time = UnityEngine.Random.Range(0f, siren.clip.length);
                siren.Play();
            }
            else if (!on && siren.isPlaying) siren.Stop();
        }

        private void RemoveDriver()
        {
            if (chase == null) return;
            chase.End();
            Destroy(chase);
            chase = null;
        }

        private float FlatDistance(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void HandleDamaged(float amount, Transform sender)
        {
            if (State == UnitState.Inactive) return;
            if (sender != null) lastAttacker = sender;
            OnAnyDamaged?.Invoke(this, sender, health.LastDamageWasCrash);
        }

        private void HandleWrecked()
        {
            if (State == UnitState.Inactive) return;
            EndAmbientFlight();
            active.Remove(this);
            State = UnitState.Wrecked;
            if (exitRoutine != null) StopCoroutine(exitRoutine);
            exitRoutine = null;
            ReleaseCrewOnFoot();
            RemoveDriver();
            SetEmergency(false);
            if (mapBlip != null) mapBlip.enabled = false;
            OnAnyWrecked?.Invoke(this, lastAttacker);
        }

        private void HandleWreckCleanup()
        {
            if (owner != null) owner.Release(this);
            else gameObject.SetActive(false);
        }

        // A two-tone wail, built once in code (the project has no siren sound): ~240 KB of audio data.
        private static AudioClip SirenClip
        {
            get
            {
                if (sirenClip != null) return sirenClip;
                const int rate = 22050;
                const float cycle = 3.2f;
                int samples = Mathf.RoundToInt(rate * cycle);
                var data = new float[samples];
                double phase = 0d;
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)rate;
                    float frequency = 620f + 720f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / cycle));
                    phase += 2d * Math.PI * frequency / rate;
                    float p = (float)phase;
                    data[i] = 0.42f * Mathf.Sin(p) + 0.18f * Mathf.Sin(2f * p) + 0.08f * Mathf.Sin(3f * p);
                }
                sirenClip = AudioClip.Create("PoliceSiren", samples, 1, rate, false);
                sirenClip.SetData(data, 0);
                return sirenClip;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
            OnAnyDamaged = null;
            OnAnyWrecked = null;
        }
    }
}
