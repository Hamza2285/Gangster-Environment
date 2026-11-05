using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// T: whistle > taxi drives to the curb > walk up and press Enter > map opens > mark a destination
// > Esc closes the map > choose Watch the trip or Skip > arrive on the footpath.
public class TaxiService : MonoBehaviour
{
    public enum State { Idle, Whistling, TaxiComing, TaxiWaiting, ChoosingDestination, ChoosingTrip, Watching, Arriving }

    public static TaxiService Instance { get; private set; }

    [Tooltip("Yellow car without AI or physics.")]
    public GameObject taxiPrefab;
    public float approachDistance = 170f;
    [Tooltip("The lanes are signed 50 km/h, about 13.8 m/s.")]
    public float taxiSpeed = 13.5f;
    public float tripSpeed = 13.5f;
    public float braking = 7f;
    [Tooltip("Enter gets in the taxi when the player is this close to it.")]
    public float enterDistance = 7f;
    [Tooltip("How far the parked taxi moves from the lane centre toward the footpath.")]
    public float curbParkOffset = 2.3f;
    [Tooltip("A taxi on its way changes course when the player's nearest curb moves this far from where it was heading.")]
    public float followThreshold = 15f;
    [Tooltip("When the drive to the player would be longer than this, a new taxi comes from nearby instead.")]
    public float followRouteLimit = 700f;
    [Tooltip("Seconds a parked taxi waits for the player to get in before it drives away.")]
    public float taxiWaitTime = 30f;

    [Header("Fare")]
    public float baseFare = 3f;
    public float farePerKm = 2.5f;

    public State CurrentState { get; private set; } = State.Idle;

    struct CameraAngle
    {
        public string name;
        public Vector3 offset;
        public float lookHeight;
    }

    // Offsets are relative to the taxi's heading. The side view stays on the road side, away from buildings.
    static readonly CameraAngle[] Angles =
    {
        new CameraAngle { name = "Chase", offset = new Vector3(0f, 3.2f, -8.5f), lookHeight = 1.3f },
        new CameraAngle { name = "High", offset = new Vector3(0f, 11f, -14f), lookHeight = 1f },
        new CameraAngle { name = "Side", offset = new Vector3(7.5f, 2.4f, -1f), lookHeight = 1.1f },
        new CameraAngle { name = "Front", offset = new Vector3(0f, 2.4f, 8.5f), lookHeight = 1.1f },
    };

    AudioSource whistleSource;
    AudioClip whistleClip;
    GameObject taxi;
    readonly RaycastHit[] sensorHits = new RaycastHit[16];
    public TaxiRoadUser RoadUser { get; private set; }
    RoadNetwork.RoadPoint taxiStop;

    // The drive to the player, replaced whenever the player moves somewhere else
    List<Vector3> followPath;
    readonly List<RoadNetwork.Link> followLinks = new List<RoadNetwork.Link>();
    int followLane;
    float followSpeed;
    bool followKeepHeading;
    bool followRerouted;
    bool followRespawn;
    RoadNetwork.RoadPoint followStop;
    float nextFollowCheck;
    float waitLeft;

    // Trip state
    bool tripWatch, tripSkip, tripBack;
    bool skipRequested;
    int cameraIndex;
    bool cameraFollowing;
    Vector3 cameraSavedLocal;
    float tripFare;

    void Awake()
    {
        Instance = this;
        whistleSource = gameObject.AddComponent<AudioSource>();
        whistleSource.spatialBlend = 0f;
        whistleSource.playOnAwake = false;
        whistleClip = MakeWhistle();
    }

    void Start()
    {
        var hud = TaxiHud.Instance;
        hud.WatchChosen = () => tripWatch = true;
        hud.SkipChosen = () => tripSkip = true;
        hud.RideSkip = () => skipRequested = true;
        hud.RideCamera = NextCamera;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void NextCamera()
    {
        cameraIndex = (cameraIndex + 1) % Angles.Length;
    }

    void Update()
    {
        var map = GameMapUI.Instance;
        var player = GamePlayer.Current;
        var hud = TaxiHud.Instance;
        if (map == null || player == null || hud == null) return;

        switch (CurrentState)
        {
            case State.Idle:
                map.SetHint("T: whistle for a taxi     M: map" + GamePlayer.ExtraKeysHint);
                if (GameInput.WhistleDown && !map.IsMapOpen && !GamePlayer.MenuOpen && !GamePlayer.InVehicle) StartCoroutine(CallTaxi());
                break;
            case State.Whistling:
                map.SetHint("");
                break;
            case State.TaxiComing:
                map.SetHint("");
                hud.SetTaxiLabel(taxi != null ? taxi.transform : null, "TAXI ON ITS WAY");
                break;
            case State.TaxiWaiting:
                hud.SetTaxiLabel(taxi != null ? taxi.transform : null, "YOUR TAXI");
                UpdateWaiting(map, player);
                break;
            case State.ChoosingTrip:
                if (GameInput.WatchOptionDown) tripWatch = true;
                else if (GameInput.SkipOptionDown || GameInput.EnterDown) tripSkip = true;
                else if (GameInput.CancelDown) tripBack = true;
                break;
            case State.Watching:
                if (GameInput.EnterDown) skipRequested = true;
                if (GameInput.CameraDown) NextCamera();
                break;
        }
    }

    void LateUpdate()
    {
        if (!cameraFollowing || taxi == null) return;
        var player = GamePlayer.Current;
        var cameraTransform = player.CameraPivot;
        Transform car = taxi.transform;

        // The player has no body in the car; it comes along so traffic and the minimap follow the trip.
        player.SetRidingPose(car.position, car.eulerAngles.y);

        var angle = Angles[cameraIndex];
        Quaternion heading = Quaternion.Euler(0f, car.eulerAngles.y, 0f);
        Vector3 look = car.position + Vector3.up * angle.lookHeight;
        Vector3 desired = car.position + heading * angle.offset;

        // Pull in rather than clip through a wall.
        Vector3 toCamera = desired - look;
        if (Physics.Raycast(look, toCamera.normalized, out RaycastHit hit, toCamera.magnitude, ~LayerMask.GetMask("Traffic AI"), QueryTriggerInteraction.Ignore))
        {
            desired = hit.point - toCamera.normalized * 0.4f;
        }

        float smoothing = 1f - Mathf.Exp(-6f * Time.deltaTime);
        cameraTransform.position = Vector3.Lerp(cameraTransform.position, desired, smoothing);
        cameraTransform.rotation = Quaternion.LookRotation(look - cameraTransform.position);
    }

    void UpdateWaiting(GameMapUI map, IGamePlayer player)
    {
        if (map.IsMapOpen) return;
        if (taxi == null)
        {
            CurrentState = State.Idle;
            return;
        }
        waitLeft -= Time.deltaTime;
        if (waitLeft <= 0f)
        {
            TaxiLeaves(map);
            return;
        }
        Vector3 offset = taxi.transform.position - player.transform.position;
        offset.y = 0f;
        bool inRange = offset.magnitude <= enterDistance;
        string leaving = "     Leaving in " + Mathf.CeilToInt(waitLeft) + "s";
        map.SetHint((inRange ? "Enter: get in the taxi" : "Walk to the taxi     T: whistle again") + leaving);
        if (inRange && GameInput.EnterDown) StartCoroutine(EnterTaxi());
        else if (!inRange && GameInput.WhistleDown) StartCoroutine(CallTaxi());
    }

    // Waited long enough: it pulls out and drives off down the road.
    void TaxiLeaves(GameMapUI map)
    {
        TaxiHud.Instance.SetTaxiLabel(null, null);
        map.TrackedTaxi = null;
        map.ShowMessage("Your taxi got tired of waiting");
        var departLinks = new List<RoadNetwork.Link>();
        var departure = RoadNetwork.Instance.BuildDepartPath(taxiStop, 110f, departLinks);
        RoadNetwork.OffsetToCurb(departure, curbParkOffset, 20f, 0f);
        StartCoroutine(DriveAwayAndRemove(taxi, departure, departLinks, taxiStop.lane));
        taxi = null;
        CurrentState = State.Idle;
    }

    IEnumerator CallTaxi()
    {
        CurrentState = State.Whistling;
        var map = GameMapUI.Instance;
        var player = GamePlayer.Current;
        TaxiHud.Instance.SetTaxiLabel(null, null);
        whistleSource.PlayOneShot(whistleClip);
        map.ShowMessage("*whistle*", 1.2f);
        yield return new WaitForSeconds(whistleClip.length + 0.2f);

        var network = RoadNetwork.Instance;
        if (network == null || !network.TryFindCurbStop(player.transform.position, out RoadNetwork.RoadPoint stop))
        {
            map.ShowMessage("No road nearby for a taxi");
            CurrentState = taxi != null ? State.TaxiWaiting : State.Idle;
            yield break;
        }

        // A taxi already out there drives over; otherwise one comes from down the road.
        if (taxi == null || !TryRouteFromTaxi(stop, 0f, out bool _)) SpawnTaxiBehind(stop);
        CurrentState = State.TaxiComing;

        while (true)
        {
            followRerouted = followRespawn = false;
            nextFollowCheck = Time.time + 1f;
            yield return DriveAlong(taxi.transform, followPath, taxiSpeed, true, PlayerLeftStop, followLinks, followLane, null, followSpeed, followKeepHeading);
            if (taxi == null)
            {
                CurrentState = State.Idle;
                yield break;
            }
            if (followRespawn) SpawnTaxiBehind(followStop);
            if (followRerouted || followRespawn) continue;

            // Reached the stop, but the player may have kept walking during the last stretch.
            network.TryFindCurbStop(player.transform.position, out stop);
            if (FlatDistance(stop.position, taxiStop.position) < followThreshold) break;
            if (!TryRouteFromTaxi(stop, 0f, out bool _)) SpawnTaxiBehind(stop);
        }

        map.ShowMessage("Your taxi is here");
        waitLeft = taxiWaitTime;
        CurrentState = State.TaxiWaiting;
    }

    void SpawnTaxiBehind(RoadNetwork.RoadPoint stop)
    {
        followPath = RoadNetwork.Instance.BuildApproachPath(stop, approachDistance, followLinks, out followLane);
        RoadNetwork.OffsetToCurb(followPath, curbParkOffset, 0f, 30f);
        if (taxi != null) Destroy(taxi);
        taxi = Instantiate(taxiPrefab, followPath[0], Quaternion.identity);
        taxi.name = "Taxi";
        GameMapUI.Instance.TrackedTaxi = taxi.transform;
        taxiStop = stop;
        followSpeed = 0f;
        followKeepHeading = false;
    }

    // Plans a drive from wherever the taxi is now, without stopping or turning around on the spot.
    // tooFar means there is no sensible route; otherwise false only means "not from here, try again shortly".
    bool TryRouteFromTaxi(RoadNetwork.RoadPoint stop, float speed, out bool tooFar)
    {
        tooFar = false;
        var network = RoadNetwork.Instance;
        Transform car = taxi.transform;
        float stoppingDistance = speed * speed / (2f * braking);
        RoadNetwork.RoadPoint start;
        if (!network.TryFindLaneAhead(car.position, car.forward, stoppingDistance + 8f, out start)
            && (speed > 1f || !network.TryFindLaneAhead(car.position, car.forward, 3f, out start))) return false;

        // The stop is just ahead, too close to reach from here: carry on until the current drive ends.
        Vector3 toStop = stop.position - car.position;
        if (start.lane == stop.lane && start.index > stop.index && Vector3.Dot(toStop, car.forward) > 0f) return false;

        var route = new List<Vector3>();
        var links = new List<RoadNetwork.Link>();
        if (!network.TryFindRoute(start, stop, route, links) || PathLength(route) > followRouteLimit)
        {
            tooFar = true;
            return false;
        }
        RoadNetwork.OffsetToCurb(route, curbParkOffset, 0f, 30f);
        route.Insert(0, car.position);

        followPath = route;
        followLinks.Clear();
        followLinks.AddRange(links);
        followLane = start.lane;
        followSpeed = speed;
        followKeepHeading = true;
        taxiStop = stop;
        return true;
    }

    // Checked while the taxi drives over. True ends the current drive so CallTaxi can start the new one.
    bool PlayerLeftStop(float speed)
    {
        if (Time.time < nextFollowCheck) return false;
        nextFollowCheck = Time.time + 0.5f;
        var network = RoadNetwork.Instance;
        if (!network.TryFindCurbStop(GamePlayer.Current.transform.position, out followStop)) return false;
        if (FlatDistance(followStop.position, taxiStop.position) < followThreshold) return false;

        followRerouted = TryRouteFromTaxi(followStop, speed, out followRespawn);
        return followRerouted || followRespawn;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    IEnumerator EnterTaxi()
    {
        var player = GamePlayer.Current;
        var map = GameMapUI.Instance;
        var hud = TaxiHud.Instance;
        var waypoints = WaypointSystem.Instance;
        var network = RoadNetwork.Instance;
        player.SetControl(false);
        hud.SetTaxiLabel(null, null);

        var tripPath = new List<Vector3>();
        var tripLinks = new List<RoadNetwork.Link>();
        RoadNetwork.RoadPoint destination;

        while (true)
        {
            CurrentState = State.ChoosingDestination;
            map.SetHint("");
            map.OpenToChooseDestination();
            while (map.IsMapOpen) yield return null;

            if (!waypoints.HasMarker)
            {
                // Closed the map without choosing: the taxi keeps waiting.
                player.SetControl(true);
                map.ShowMessage("No destination chosen");
                waitLeft = taxiWaitTime;
                CurrentState = State.TaxiWaiting;
                yield break;
            }

            network.TryFindCurbStop(waypoints.MarkerPosition, out destination);
            bool hasRoute = network.TryFindRoute(taxiStop, destination, tripPath, tripLinks);
            if (!hasRoute)
            {
                tripPath.Clear();
                tripPath.Add(taxiStop.position);
                tripPath.Add(destination.position);
            }
            RoadNetwork.OffsetToCurb(tripPath, curbParkOffset, 25f, 30f);
            float length = PathLength(tripPath);
            tripFare = FareFor(length);

            // Let the player choose how to get there.
            tripWatch = tripSkip = tripBack = false;
            CurrentState = State.ChoosingTrip;
            player.LockCursor(false);
            hud.ShowOptions((length / 1000f).ToString("0.0") + " km     " + FormatTime(length / AverageSpeed()) + "     Fare " + TaxiHud.FormatFare(tripFare));
            yield return null;
            while (!tripWatch && !tripSkip && !tripBack) yield return null;
            hud.HideOptions();
            if (tripBack) continue;

            // Without a drivable route there is nothing to watch.
            if (tripWatch && hasRoute) yield return WatchTrip(tripPath, tripLinks, destination);
            else yield return FinishTrip(destination, false);
            yield break;
        }
    }

    float AverageSpeed()
    {
        // Turns and slowing down keep the real average below top speed.
        return tripSpeed * 0.8f;
    }

    float FareFor(float metres)
    {
        return baseFare + farePerKm * metres / 1000f;
    }

    static float PathLength(List<Vector3> path)
    {
        float total = 0f;
        for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
        return total;
    }

    static string FormatTime(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
    }

    IEnumerator WatchTrip(List<Vector3> path, List<RoadNetwork.Link> links, RoadNetwork.RoadPoint destination)
    {
        CurrentState = State.Watching;
        var player = GamePlayer.Current;
        var hud = TaxiHud.Instance;
        var waypoints = WaypointSystem.Instance;
        var blink = ScreenBlink.Instance;
        var cameraTransform = player.CameraPivot;

        skipRequested = false;
        cameraIndex = 0;
        waypoints.UseRoute(path);

        // Third person: free the camera from the player and cut to the chase view behind a blink.
        yield return blink.Close(0.2f);
        cameraSavedLocal = cameraTransform.localPosition;
        cameraTransform.SetParent(null, true);
        Transform car = taxi.transform;
        Quaternion heading = Quaternion.Euler(0f, car.eulerAngles.y, 0f);
        cameraTransform.position = car.position + heading * Angles[0].offset;
        cameraFollowing = true;
        player.LockCursor(false);
        hud.ShowRideBox(true);
        hud.UpdateRideBox(baseFare, PathLength(path) / AverageSpeed(), Angles[cameraIndex].name);
        yield return blink.Open(0.3f);

        yield return DriveAlong(car, path, tripSpeed, true,
            _ => skipRequested,
            links, taxiStop.lane,
            (travelled, total) => hud.UpdateRideBox(FareFor(travelled), (total - travelled) / AverageSpeed(), Angles[cameraIndex].name));

        hud.ShowRideBox(false);
        yield return FinishTrip(destination, !skipRequested);
    }

    // Blink, then the player is standing on the footpath. A taxi that really drove there pulls away.
    IEnumerator FinishTrip(RoadNetwork.RoadPoint destination, bool taxiDroveThere)
    {
        CurrentState = State.Arriving;
        var player = GamePlayer.Current;
        var waypoints = WaypointSystem.Instance;
        var map = GameMapUI.Instance;
        var blink = ScreenBlink.Instance;
        var network = RoadNetwork.Instance;
        var cameraTransform = player.CameraPivot;

        yield return blink.Blink(0.18f, 0.14f);
        yield return new WaitForSeconds(0.1f);
        yield return blink.Close(0.3f);

        cameraFollowing = false;
        if (cameraTransform.parent != player.transform)
        {
            cameraTransform.SetParent(player.transform, false);
            cameraTransform.localPosition = cameraSavedLocal;
            cameraTransform.localRotation = Quaternion.identity;
        }
        player.Teleport(FindFootpath(destination), Quaternion.LookRotation(destination.direction).eulerAngles.y);
        player.ResetLook();
        waypoints.ClearMarker();
        map.TrackedTaxi = null;

        var leaving = taxi;
        taxi = null;
        if (taxiDroveThere && leaving != null)
        {
            var departLinks = new List<RoadNetwork.Link>();
            var departure = network.BuildDepartPath(destination, 110f, departLinks);
            RoadNetwork.OffsetToCurb(departure, curbParkOffset, 20f, 0f);
            StartCoroutine(DriveAwayAndRemove(leaving, departure, departLinks, destination.lane));
        }
        else if (leaving != null)
        {
            Destroy(leaving);
        }

        yield return new WaitForSeconds(0.4f);
        player.SetControl(true);
        player.LockCursor(true);
        yield return blink.Open(0.45f);
        map.ShowMessage("You have arrived     Fare " + TaxiHud.FormatFare(tripFare));
        CurrentState = State.Idle;
    }

    IEnumerator DriveAwayAndRemove(GameObject car, List<Vector3> path, List<RoadNetwork.Link> links, int lane)
    {
        yield return DriveAlong(car.transform, path, taxiSpeed, false, null, links, lane);
        if (car != null) Destroy(car);
    }

    // A spot on the footpath beside the stop, with ground under it and room to stand.
    // Bridges have no wide footpath, so it tries closer spots until one fits.
    Vector3 FindFootpath(RoadNetwork.RoadPoint stop)
    {
        int mask = ~LayerMask.GetMask("Traffic AI");
        Vector3 curb = new Vector3(-stop.direction.z, 0f, stop.direction.x);
        foreach (float distance in new[] { 5.5f, 4.8f, 4f, 3.2f, 2.4f })
        {
            Vector3 spot = stop.position + curb * distance;
            if (!Physics.Raycast(spot + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.point.y - stop.position.y) > 1.2f) continue;
            Vector3 feet = hit.point + Vector3.up * 0.05f;
            if (Physics.CheckCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.5f, 0.3f, mask, QueryTriggerInteraction.Ignore)) continue;
            return feet;
        }
        return stop.position + curb * curbParkOffset + Vector3.up * 0.05f;
    }

    IEnumerator DriveAlong(Transform car, List<Vector3> path, float topSpeed, bool stopAtEnd,
        Func<float, bool> cancel, List<RoadNetwork.Link> links, int firstLane, Action<float, float> progress = null,
        float startSpeed = 0f, bool keepHeading = false)
    {
        if (path.Count < 2) yield break;
        var distance = new float[path.Count];
        for (int i = 1; i < path.Count; i++) distance[i] = distance[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        float total = distance[path.Count - 1];
        var roadUser = new TaxiRoadUser(path, distance, RoadNetwork.Instance, firstLane, links);
        RoadUser = roadUser;
        float travelled = 0f, speed = startSpeed;
        int segment = 0;
        car.position = path[0];
        Vector3 startAhead = SampleFrom(path, distance, 3f, 0);
        if (!keepHeading && (startAhead - path[0]).sqrMagnitude > 0.01f) car.rotation = Quaternion.LookRotation(startAhead - path[0]);

        while (travelled < total)
        {
            if (car == null || (cancel != null && cancel(speed)))
            {
                roadUser.ReleaseAll();
                yield break;
            }
            float dt = Time.deltaTime;

            float target = topSpeed;
            if (stopAtEnd) target = Mathf.Min(target, Mathf.Sqrt(2f * braking * Mathf.Max(total - travelled, 0f)) + 0.4f);

            // Slow for bends: compare the direction now with the direction a little further on.
            Vector3 here = SampleFrom(path, distance, travelled, segment);
            Vector3 near = SampleFrom(path, distance, travelled + 5f, segment);
            Vector3 far = SampleFrom(path, distance, travelled + 22f, segment);
            float bend = Vector3.Angle(near - here, far - near);
            target = Mathf.Min(target, Mathf.Lerp(topSpeed, 7f, Mathf.Clamp01(bend / 55f)));

            // Junction right of way and the queue ahead, through the same reservations traffic uses.
            target = Mathf.Min(target, roadUser.Tick(travelled, speed, braking, dt));

            // Stop for anyone actually in the way: cars, people and the player.
            if (!roadUser.GaveUpWaiting && SomeoneAhead(car, 4f + speed)) target = 0f;

            speed = Mathf.MoveTowards(speed, target, (target < speed ? braking * 2f : 5f) * dt);
            travelled = Mathf.Min(total, travelled + speed * dt);

            while (segment < path.Count - 2 && distance[segment + 1] < travelled) segment++;
            Vector3 position = SampleFrom(path, distance, travelled, segment);
            car.position = position;
            Vector3 ahead = SampleFrom(path, distance, travelled + 3f, segment);
            if ((ahead - position).sqrMagnitude > 0.01f)
            {
                car.rotation = Quaternion.Slerp(car.rotation, Quaternion.LookRotation(ahead - position), 1f - Mathf.Exp(-8f * dt));
            }
            progress?.Invoke(travelled, total);
            yield return null;
        }
        // Parked: other cars may pass, or use the lane again once it has gone.
        roadUser.ReleaseAll();
    }

    bool SomeoneAhead(Transform car, float range)
    {
        // High enough to clear the road surface, narrow enough to stay in the lane.
        int count = Physics.SphereCastNonAlloc(car.position + car.forward * 2.5f + Vector3.up * 1.2f, 0.9f, car.forward, sensorHits, range, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = sensorHits[i].collider;
            if (hit.attachedRigidbody != null && hit.GetComponentInParent<TSTrafficAI>() != null) return true;
            if (hit.GetComponentInParent<PedestrianWalker>() != null) return true;
            if (hit is CharacterController) return true;
        }
        return false;
    }

    static Vector3 SampleFrom(List<Vector3> path, float[] distance, float at, int segment)
    {
        at = Mathf.Min(at, distance[distance.Length - 1]);
        while (segment < path.Count - 2 && distance[segment + 1] < at) segment++;
        float length = distance[segment + 1] - distance[segment];
        float k = length > 0.0001f ? (at - distance[segment]) / length : 0f;
        return Vector3.Lerp(path[segment], path[segment + 1], Mathf.Clamp01(k));
    }

    // Generated in code so the project needs no audio file: rising then falling two-finger whistle.
    static AudioClip MakeWhistle()
    {
        const int rate = 44100;
        const float duration = 0.95f;
        var samples = new float[(int)(rate * duration)];
        float phase = 0f;
        var random = new System.Random(7);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float frequency, volume;
            if (t < 0.28f)
            {
                frequency = Mathf.Lerp(1500f, 2900f, t / 0.28f);
                volume = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.28f - t) / 0.03f);
            }
            else if (t < 0.36f)
            {
                frequency = 2200f;
                volume = 0f;
            }
            else
            {
                float k = (t - 0.36f) / (duration - 0.36f);
                frequency = Mathf.Lerp(2900f, 1500f, k * k);
                volume = Mathf.Clamp01((t - 0.36f) / 0.03f) * Mathf.Clamp01((duration - t) / 0.12f);
            }
            frequency += Mathf.Sin(t * 2f * Mathf.PI * 7f) * 25f;
            phase += 2f * Mathf.PI * frequency / rate;
            float breath = (float)(random.NextDouble() * 2.0 - 1.0) * 0.05f;
            samples[i] = (Mathf.Sin(phase) + breath) * volume * 0.5f;
        }
        var clip = AudioClip.Create("Whistle", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
