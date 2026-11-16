using System;
using System.Collections.Generic;
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
    /// - Deployed: the suspect is on foot (or stopped in his car) close by: the car stops and its officers climb out
    ///   (<see cref="PoliceDispatcher.SpawnCrewOfficer"/>), then it stays parked with the lights on.
    /// - Search: drives to the last known position and waits there.
    /// - Return: siren off, drives away and is recalled once out of sight.
    /// - Wrecked: blown up; cleared through <see cref="VehicleHealth.OnWreckCleanup"/>.
    /// Hits are reported through <see cref="OnAnyDamaged"/> / <see cref="OnAnyWrecked"/> (crimes against the police).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class PoliceCarUnit : MonoBehaviour
    {
        public enum UnitState { Inactive, Pursue, Deployed, Search, Return, Wrecked }

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
        [Tooltip("Seated crew visuals, hidden once the officers have climbed out.")]
        [SerializeField] private GameObject[] seatedCrew = new GameObject[0];

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
        [Tooltip("Top speed share while searching.")]
        [SerializeField, Range(0.2f, 1f)] private float searchSpeedShare = 0.55f;
        [Tooltip("A returning car is removed once out of sight and at least this far (m) from the camera.")]
        [SerializeField, Min(5f)] private float recallKeepDistance = 40f;

        [Header("Siren")]
        [SerializeField, Range(0f, 1f)] private float sirenVolume = 0.75f;

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
        public float Speed => body.linearVelocity.magnitude;

        private PoliceDispatcher owner;
        private PoliceSettings settings;
        private WantedManager wanted;
        private ChaseDriver chase;
        private Transform lastAttacker;
        private readonly List<Vector3> route = new List<Vector3>(128);
        private readonly List<RoadNetwork.Link> links = new List<RoadNetwork.Link>();
        private float nextRepath;
        private float nextShot;
        private float stateSince;
        private float stoppedSince = -1f;
        private float nextDeployTime;
        private Vector3 lastRouteTarget;
        private bool routeWasRoad;
        private bool crewOut;
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
            if (siren != null)
            {
                siren.clip = SirenClip;
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
        public void Activate(PoliceDispatcher dispatcher, Vector3 position, Quaternion rotation, Vector3 velocity)
        {
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
            chase.Begin(simpleCar, settings.carEngineTorque);

            crewOut = false;
            for (int i = 0; i < seatedCrew.Length; i++) if (seatedCrew[i] != null) seatedCrew[i].SetActive(true);
            if (mapBlip != null) mapBlip.enabled = true;
            SetEmergency(true);
            lastAttacker = null;
            nextRepath = 0f;
            nextShot = 0f;
            stoppedSince = -1f;
            nextDeployTime = 0f;
            SetState(UnitState.Pursue);
            if (!active.Contains(this)) active.Add(this);
        }

        /// <summary>Back to the pool (called by the dispatcher).</summary>
        public void Deactivate()
        {
            active.Remove(this);
            State = UnitState.Inactive;
            RemoveDriver();
            SetEmergency(false);
            if (mapBlip != null) mapBlip.enabled = false;
            gameObject.SetActive(false);
        }

        /// <summary>True when the car's crew can see the point (within Car Sight Radius, nothing solid in between).</summary>
        public bool CanSee(Vector3 point, float radius)
        {
            if (State == UnitState.Inactive || State == UnitState.Wrecked || crewOut) return false;
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
            if (wanted == null)
            {
                owner.Release(this);
                return;
            }

            if (wanted.Stars == 0 && State != UnitState.Return) SetState(UnitState.Return);
            else if (wanted.Stars > 0 && State == UnitState.Return) SetState(UnitState.Pursue);
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
            if (suspectStopped && distance <= settings.carDeployDistance && settings.officersPerCar > 0 && Time.time >= nextDeployTime)
            {
                SetState(UnitState.Deployed);
                return;
            }

            float now = Time.time;
            if (now >= nextRepath)
            {
                // Staggered so several cars never plan in the same frame.
                nextRepath = now + settings.carRepathInterval * UnityEngine.Random.Range(0.8f, 1.2f);
                chase.target = wanted.PlayerInVehicle ? targetTransform : null;
                bool direct = distance <= directApproachDistance;
                // A road route costs a graph search: only re-plan when the suspect has moved on or the route is nearly used up.
                bool stale = (target - lastRouteTarget).sqrMagnitude > RouteRefreshDistance * RouteRefreshDistance
                             || chase.RemainingOnPath < RouteRefreshDistance * 2f || !routeWasRoad;
                if (direct || stale)
                {
                    BuildRouteTo(target, direct);
                    lastRouteTarget = target;
                    routeWasRoad = !direct;
                    chase.SetPath(route);
                }
            }

            if (wanted.IsLethal && gun != null && distance <= settings.carFireRange && now >= nextShot)
            {
                nextShot = now + UnityEngine.Random.Range(settings.fireInterval.x, settings.fireInterval.y) * 1.5f;
                if (PoliceGunfire.HasLineOfFire(gun.Muzzle.position, wanted, settings, out _)) PoliceGunfire.ShootPlayer(gun, transform, wanted, settings);
            }
        }

        private void Deployed()
        {
            if (crewOut) return;
            if (Speed > 1.5f && Time.time - stateSince < 4f) return;
            if (stoppedSince < 0f) stoppedSince = Time.time;
            if (Time.time - stoppedSince < crewExitDelay) return;
            crewOut = true;
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

        private void Search()
        {
            if (Time.time < nextRepath) return;
            nextRepath = Time.time + settings.carRepathInterval * 2f;
            chase.target = null;
            chase.topSpeed = settings.carTopSpeed * searchSpeedShare;
            Vector3 point = wanted.LastKnownPosition;
            if (FlatDistance(point) < 15f)
            {
                chase.SetStopped(true);
                return;
            }
            chase.SetStopped(false);
            if (routeWasRoad && (point - lastRouteTarget).sqrMagnitude < RouteRefreshDistance * RouteRefreshDistance) return;
            BuildRouteTo(point, false);
            lastRouteTarget = point;
            routeWasRoad = true;
            chase.SetPath(route);
        }

        private void Return()
        {
            if (Time.time - stateSince > 2f && owner.CanVanish(transform.position, recallKeepDistance))
            {
                owner.Release(this);
                return;
            }
            if (Time.time < nextRepath || chase == null) return;
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
            route.Clear();
            route.Add(transform.position);
            route.Add(transform.position + transform.forward * 80f);
            chase.SetPath(route);
        }

        // Road route from just ahead of the car to the point; straight at it when close (or when there is no road route).
        private void BuildRouteTo(Vector3 point, bool direct)
        {
            Vector3 from = transform.position;
            RoadNetwork network = direct ? null : RoadNetwork.Instance;
            if (network != null && network.TryFindRoute(from + transform.forward * 6f, point, route))
            {
                route.Insert(0, from);
                Vector3 end = route[route.Count - 1];
                end.y = point.y;
                if ((end - point).sqrMagnitude <= routeExtendDistance * routeExtendDistance) route.Add(point);
                return;
            }
            route.Clear();
            route.Add(from);
            route.Add(point);
        }

        private void SetState(UnitState state)
        {
            State = state;
            stateSince = Time.time;
            nextRepath = 0f;
            routeWasRoad = false;
            stoppedSince = -1f;
            if (chase == null) return;
            switch (state)
            {
                case UnitState.Pursue:
                    chase.topSpeed = settings.carTopSpeed;
                    chase.SetStopped(false);
                    SetEmergency(true);
                    if (mapBlip != null) mapBlip.enabled = true;
                    break;
                case UnitState.Deployed:
                    chase.SetStopped(true);
                    break;
                case UnitState.Search:
                    SetEmergency(true);
                    break;
                case UnitState.Return:
                    SetEmergency(false);
                    if (mapBlip != null) mapBlip.enabled = false;
                    break;
            }
        }

        // Light bar and siren together: on while chasing or searching, dark on the way back (patrol look).
        private void SetEmergency(bool on)
        {
            PoliceLights.SetFlashing(lightBar, on);
            if (siren == null) return;
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
            active.Remove(this);
            State = UnitState.Wrecked;
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
