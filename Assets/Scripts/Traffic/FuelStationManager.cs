using System.Collections.Generic;
using UnityEngine;

// Now and then a traffic car passing a petrol station pulls in, stops at a pump, refuels (hose to the
// car, pump sound) and drives back out onto the road as ordinary traffic again.
// Each station's pumps stand in one row, so cars use it like a single-lane drive-through: they pull
// up to the furthest free pump and leave in the order they arrived.
public class FuelStationManager : MonoBehaviour
{
    public TSMainManager roads;
    public Transform player;
    public Camera viewCamera;
    [Tooltip("Parent of the station models; every child named fuel_machine* is a pump.")]
    public Transform stationsRoot;

    [Header("Behaviour")]
    [Tooltip("Stations further than this from the player are left alone.")]
    public float activeRadius = 250f;
    [Tooltip("Seconds between looks for a car to send in, per station.")]
    public float checkInterval = 3f;
    [Range(0f, 1f)] public float pullInChance = 0.5f;
    public float minRefuelSeconds = 3f;
    public float maxRefuelSeconds = 6f;
    public float forecourtSpeed = 6f;
    [Tooltip("Excluded from refuelling (taxis).")]
    public string excludePrefix = "Car17";

    [Header("Layout")]
    [Tooltip("Distance from the pump row to the centre of a refuelling car.")]
    public float bayOffset = 3.4f;
    [Tooltip("How far out from the pump row cars enter and leave the forecourt.")]
    public float gateOffset = 18f;
    [Tooltip("How far along the row (from its middle) the entry and exit gates are.")]
    public float gateSide = 22f;

    public int CarsRefuelled { get; private set; }
    public int CarsInStations { get { return _visits.Count; } }
    public int CarsNudged { get; private set; }
    public PedestrianSpawner pedestrians;

    private class Station
    {
        public Vector3 centre, rowAxis, outward, travel;   // travel = driving direction along the row
        public List<Vector3> bays = new List<Vector3>();     // ordered upstream -> downstream
        public List<Transform> pumps = new List<Transform>();
        public Visit[] occupant;
        public int entryLane, exitLane;
        public Vector3 entryGate, exitGate;
        public bool entryClear, exitClear;
        public float nextCheck;
    }

    // Entering: driving to the start of the pump row. Queued: waiting there for a pump.
    // ToPump: rolling along the row to its pump.
    private enum Phase { Entering, Queued, ToPump, Refuelling, WaitingToLeave, Leaving }

    private class Visit
    {
        public TSTrafficAI car;
        public TaxiDriver driver;
        public Station station;
        public int bay;
        public Phase phase;
        public float phaseStarted, refuelUntil, lastProgress, lastRemaining;
        public int resumeLane, resumeIndex;
        public int rootLayer;
        public string rootTag;
        public List<KeyValuePair<Collider, KeyValuePair<int, string>>> colliders;
        public LineRenderer hose;
        public AudioSource sound;
        public List<Vector3> route;
        public float nextHorn;
        public Vector3 joinPoint;
        public bool merged;
        public float holdSince;
    }

    private readonly List<Station> _stations = new List<Station>();
    private readonly List<Visit> _visits = new List<Visit>();
    private TSTrafficAI[] _cars = new TSTrafficAI[0];
    private float _carScan;
    private static AudioClip _pumpClip;
    private static Material _hoseMaterial;

    private void Start()
    {
        if (roads == null) roads = FindFirstObjectByType<TSMainManager>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (pedestrians == null) pedestrians = FindFirstObjectByType<PedestrianSpawner>();
        // Distance to the stations is measured from the camera: it follows whatever the player is doing, on foot
        // or driving (the character itself can be left parked elsewhere while in a car).
        if (player == null && viewCamera != null) player = viewCamera.transform;
        if (stationsRoot == null)
        {
            var go = GameObject.Find("Petrol pump");
            if (go != null) stationsRoot = go.transform;
        }
        BuildStations();
    }

    // ---------- layout ----------

    private void BuildStations()
    {
        if (stationsRoot == null || roads == null) return;
        var pumps = new List<Transform>();
        foreach (Transform t in stationsRoot) if (t.name.StartsWith("fuel_machine")) pumps.Add(t);

        // Pumps within 30 m of each other belong to the same station.
        var used = new bool[pumps.Count];
        for (int i = 0; i < pumps.Count; i++)
        {
            if (used[i]) continue;
            var group = new List<Transform> { pumps[i] };
            used[i] = true;
            for (int j = i + 1; j < pumps.Count; j++)
                if (!used[j] && Vector3.Distance(Centre(pumps[i]), Centre(pumps[j])) < 30f) { group.Add(pumps[j]); used[j] = true; }
            var s = BuildStation(group);
            if (s != null) _stations.Add(s);
        }
    }

    private Station BuildStation(List<Transform> group)
    {
        if (group.Count < 1) return null;
        Vector3 centre = Vector3.zero;
        foreach (var p in group) centre += Centre(p);
        centre /= group.Count;

        // Row direction: along the line of pumps (their longest spread).
        Vector3 axis = Vector3.right;
        if (group.Count > 1)
        {
            float best = 0f;
            foreach (var a in group) foreach (var b in group)
            {
                Vector3 d = Flat(Centre(b) - Centre(a));
                if (d.magnitude > best) { best = d.magnitude; axis = d.normalized; }
            }
        }

        // The road side is where the nearest traffic lane is.
        int nearLane, nearIndex;
        Vector3 nearPoint;
        if (!NearestLane(centre, Vector3.zero, 60f, out nearLane, out nearIndex, out nearPoint)) return null;
        Vector3 outward = Vector3.Cross(Vector3.up, axis).normalized;
        if (Vector3.Dot(outward, Flat(nearPoint - centre)) < 0f) outward = -outward;
        Vector3 laneDir = LaneDirection(nearLane, nearIndex);
        Vector3 travel = Vector3.Dot(laneDir, axis) >= 0f ? axis : -axis;

        var s = new Station { centre = centre, rowAxis = axis, outward = outward, travel = travel };
        // Upstream first, so index 0 is the first pump a car passes.
        group.Sort((x, y) => Vector3.Dot(Centre(x), travel).CompareTo(Vector3.Dot(Centre(y), travel)));
        foreach (var p in group)
        {
            Vector3 c = Centre(p);
            s.pumps.Add(p);
            s.bays.Add(new Vector3(c.x, centre.y, c.z) + outward * bayOffset);
        }
        s.occupant = new Visit[s.bays.Count];
        s.entryGate = centre - travel * gateSide + outward * gateOffset;
        s.exitGate = centre + travel * gateSide + outward * gateOffset;

        int idx;
        Vector3 pt;
        if (!NearestLane(s.entryGate, travel, 40f, out s.entryLane, out idx, out pt)) return null;
        if (!NearestLane(s.exitGate, travel, 40f, out s.exitLane, out idx, out pt)) return null;

        // Slide each gate along the row until the drive between road and forecourt clears islands and signs.
        Vector3 baseEntry = s.entryGate, baseExit = s.exitGate;
        // Try the default spot first, then alternately a little further from and nearer to the row.
        float[] shifts = { 0f, 2f, -2f, 4f, -4f, 6f, -6f, 8f, 10f, 12f };
        foreach (float shift in shifts)
        {
            if (s.entryClear) break;
            s.entryGate = baseEntry - travel * shift;
            if (ShapeClear(EntryShape(s, EntryTurnPoint(s)))) s.entryClear = true;
        }
        if (!s.entryClear) s.entryGate = baseEntry;
        foreach (float shift in shifts)
        {
            if (s.exitClear) break;
            s.exitGate = baseExit + travel * shift;
            int joinIndex;
            if (ShapeClear(ExitShape(s, RowEnd(s), out joinIndex))) s.exitClear = true;
        }
        if (!s.exitClear) s.exitGate = baseExit;
        if (!s.entryClear || !s.exitClear)
            Debug.LogWarning("[FuelStations] No fully clear " + (!s.entryClear ? "entry" : "exit") + " path for the station at " + centre.ToString("F0") + " (blocked by " + LastBlocker + "); using the default gate.");
        s.nextCheck = Time.time + Random.Range(0f, checkInterval);
        return s;
    }

    private static Vector3 Centre(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    // ---------- main loop ----------

    private void Update()
    {
        _carScan -= Time.deltaTime;
        if (_carScan <= 0f)
        {
            _carScan = 1f;
            _cars = FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None);
        }

        foreach (var s in _stations)
        {
            if (Time.time < s.nextCheck) continue;
            s.nextCheck = Time.time + checkInterval;
            if (player != null && Vector3.Distance(player.position, s.centre) > activeRadius) continue;
            if (Random.value > pullInChance) continue;
            TrySendCar(s);
        }

        for (int i = _visits.Count - 1; i >= 0; i--)
        {
            if (!UpdateVisit(_visits[i])) _visits.RemoveAt(i);
        }
    }

    // Furthest free pump with nothing between it and the entrance.
    private int FreeBay(Station s)
    {
        int bay = -1;
        for (int i = 0; i < s.bays.Count; i++)
        {
            if (s.occupant[i] != null) break;
            bay = i;
        }
        return bay;
    }

    // Lanes a car can be on when it is picked: the lane past the station, and every lane that feeds into it
    // through a junction (the lane past the station often starts only a few metres before the entrance).
    private class Approach
    {
        public int lane;
        public List<Vector3> tail = new List<Vector3>();   // junction curve + entry-lane points up to the turn
        public float tailLength;
        public int connectorKey = -1;                       // (lane << 16 | connector) for the traffic light
    }

    private readonly Dictionary<Station, List<Approach>> _approaches = new Dictionary<Station, List<Approach>>();
    private Dictionary<int, TSTrafficLight> _lights;

    private List<Approach> ApproachesFor(Station s)
    {
        List<Approach> list;
        if (_approaches.TryGetValue(s, out list)) return list;
        list = new List<Approach>();
        var entryPts = roads.lanes[s.entryLane].points;
        int turnIndex = IndexBefore(s.entryLane, ClosestIndex(s.entryLane, s.entryGate), 14f);
        list.Add(new Approach { lane = s.entryLane });
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var connectors = roads.lanes[l].connectors;
            for (int c = 0; c < connectors.Length; c++)
            {
                if (connectors[c].nextLane != s.entryLane || l == s.entryLane) continue;
                var a = new Approach { lane = l, connectorKey = (l << 16) | c };
                foreach (var p in connectors[c].points) a.tail.Add(p.point);
                for (int k = 0; k <= turnIndex; k++) a.tail.Add(entryPts[k].point);
                for (int k = 1; k < a.tail.Count; k++) a.tailLength += Vector3.Distance(a.tail[k - 1], a.tail[k]);
                list.Add(a);
            }
        }
        _approaches[s] = list;
        return list;
    }

    private TSTrafficLight LightFor(int connectorKey)
    {
        if (_lights == null)
        {
            _lights = new Dictionary<int, TSTrafficLight>();
            foreach (var light in FindObjectsByType<TSTrafficLight>(FindObjectsSortMode.None))
                foreach (var p in light.pointsNormalLight)
                    _lights[(p.lane << 16) | p.connector] = light;
        }
        TSTrafficLight result;
        return _lights.TryGetValue(connectorKey, out result) ? result : null;
    }

    private void TrySendCar(Station s)
    {
        // A pump is only chosen once a car reaches the row (cars can overtake each other on the way),
        // but never send more cars than there are pumps.
        int busy = 0;
        foreach (var other in _visits) if (other.station == s && other.phase != Phase.Leaving) busy++;
        if (busy >= s.bays.Count) return;
        int turnIndex = IndexBefore(s.entryLane, ClosestIndex(s.entryLane, s.entryGate), 14f);
        var approaches = ApproachesFor(s);

        foreach (var car in _cars)
        {
            if (!Eligible(car)) continue;
            Vector3 pos = car.transform.position;
            foreach (var a in approaches)
            {
                var lanePts = roads.lanes[a.lane].points;
                int ci = ClosestIndex(a.lane, pos);
                if (Flat(lanePts[ci].point - pos).magnitude > 2.5f) continue;
                if (Mathf.Abs(lanePts[ci].point.y - pos.y) > 3f) continue;
                if (Vector3.Dot(car.transform.forward, LaneDirection(a.lane, ci)) < 0.8f) continue;
                bool onEntryLane = a.tail.Count == 0;
                if (onEntryLane && ci >= turnIndex) continue;
                float toTurn = onEntryLane ? AlongLane(a.lane, ci, turnIndex) : AlongLane(a.lane, ci, lanePts.Length - 1) + a.tailLength;
                if (toTurn < 15f || toTurn > 90f) continue;
                if (car.GetComponent<Rigidbody>().linearVelocity.magnitude < 2f) continue;   // queued at a light

                var route = new List<Vector3> { pos };
                int lastLane = onEntryLane ? turnIndex : lanePts.Length - 1;
                for (int k = ci + 1; k <= lastLane; k++) route.Add(lanePts[k].point);
                Vector3 junctionStart = a.tail.Count > 0 ? a.tail[0] : Vector3.zero;
                route.AddRange(a.tail);
                // Smooth curves off the road, through the gate and round onto the pump row, then straight
                // along the row to the pump.
                route = Densify(route, 2f);
                route.AddRange(EntryShape(s, route[route.Count - 1]));
                // A short straight run onto the row so the car arrives lined up with the pumps.
                route.AddRange(Densify(new List<Vector3> { RowStart(s), RowStart(s) + s.travel * 3f }, 1f));

                // Stop for the junction's light like any other car.
                List<TaxiDriver.StopLine> stops = null;
                TSTrafficLight light = a.connectorKey >= 0 ? LightFor(a.connectorKey) : null;
                if (light != null)
                {
                    int idx = 0;
                    float best = float.MaxValue;
                    for (int k = 0; k < route.Count; k++)
                    {
                        float d = (route[k] - junctionStart).sqrMagnitude;
                        if (d < best) { best = d; idx = k; }
                    }
                    stops = new List<TaxiDriver.StopLine> { new TaxiDriver.StopLine { routeIndex = idx, light = light } };
                }
                StartVisit(car, s, -1, route, stops);
                return;
            }
        }
    }

    private bool Eligible(TSTrafficAI car)
    {
        if (car == null || !car.gameObject.activeInHierarchy || !car.IsEnabled) return false;
        if (car.name.StartsWith(excludePrefix)) return false;
        if (car.GetComponent<TaxiDriver>() != null || car.GetComponent<ChaseDriver>() != null) return false;
        var crash = car.GetComponent<BikeCrash>();
        if (crash != null && crash.Crashed) return false;
        if (car.transform.up.y < 0.9f) return false;
        foreach (var v in _visits) if (v.car == car) return false;
        return true;
    }

    private void StartVisit(TSTrafficAI car, Station s, int bay, List<Vector3> route, List<TaxiDriver.StopLine> stops)
    {
        car.Pause();
        var unreserve = typeof(TSTrafficAI).GetMethod("UnReserveAll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (unreserve != null) unreserve.Invoke(car, null);

        var v = new Visit
        {
            car = car, station = s, bay = bay, phase = Phase.Entering, phaseStarted = Time.time,
            lastProgress = Time.time, lastRemaining = float.MaxValue,
            rootLayer = car.gameObject.layer, rootTag = car.gameObject.tag,
            colliders = new List<KeyValuePair<Collider, KeyValuePair<int, string>>>()
        };
        // Other traffic brakes for anything on its player-sensor layer, so nobody drives into it.
        foreach (var col in car.GetComponentsInChildren<Collider>(true))
            v.colliders.Add(new KeyValuePair<Collider, KeyValuePair<int, string>>(col, new KeyValuePair<int, string>(col.gameObject.layer, col.gameObject.tag)));
        int sensorLayer = LayerMask.NameToLayer("Player");
        int mask = car.playerLayer.value;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { sensorLayer = i; break; }
        car.gameObject.layer = sensorLayer;
        car.gameObject.tag = car.playerTag;
        foreach (var c in v.colliders) { c.Key.gameObject.layer = sensorLayer; c.Key.gameObject.tag = car.playerTag; }

        v.driver = car.gameObject.AddComponent<TaxiDriver>();
        v.driver.cruiseSpeed = 10f;
        v.driver.turnSpeed = 4f;
        v.driver.followGap = 4f;
        v.driver.ignoreStaticObstacles = true;
        v.route = route;
        v.driver.Begin(route, car.GetComponent<TSSimpleCar>(), car.FrontPoint, stops);
        if (bay >= 0) s.occupant[bay] = v;
        _visits.Add(v);
    }

    // Returns false once the visit is over.
    private bool UpdateVisit(Visit v)
    {
        if (v.car == null || !v.car.gameObject.activeInHierarchy || v.driver == null)
        {
            EndVisit(v, false);
            return false;
        }

        // Slow down on the forecourt.
        if (v.phase == Phase.Entering || v.phase == Phase.ToPump || v.phase == Phase.Leaving)
        {
            float onForecourt = Vector3.Distance(Flat(v.car.transform.position), Flat(v.station.centre));
            v.driver.cruiseSpeed = onForecourt < gateSide + gateOffset ? forecourtSpeed : 10f;
            if (v.driver.RemainingDistance < v.lastRemaining - 0.5f)
            {
                v.lastRemaining = v.driver.RemainingDistance;
                v.lastProgress = Time.time;
            }
            // Wedged somewhere: give up once nobody is watching. Out on the road it may just be queuing at
            // a red light, so it gets longer there.
            bool onRoad = onForecourt > gateSide + gateOffset;
            float stalled = Time.time - v.lastProgress;
            if (stalled > (onRoad ? 120f : 40f) && !IsVisible(v.car.transform.position))
            {
                EndVisit(v, false);
                return false;
            }
            // On the forecourt nothing should hold a car up for long: honk at whatever is in the way,
            // then hop it a few metres along its path.
            if (!onRoad && stalled > 5f && Time.time > v.nextHorn)
            {
                v.nextHorn = Time.time + Random.Range(3f, 5f);
                CarHorn.Honk(v.car.transform, Random.value < 0.5f);
                if (pedestrians != null) pedestrians.TriggerPanic(v.car.transform.position + v.car.transform.forward * 5f, 6f);
            }
            if (!onRoad && stalled > 14f && Nudge(v))
            {
                v.lastProgress = Time.time;
                v.lastRemaining = float.MaxValue;
                CarsNudged++;
            }
        }

        switch (v.phase)
        {
            case Phase.Entering:
                if (v.driver.CurrentState == TaxiDriver.State.Arrived)
                {
                    v.phase = Phase.Queued;
                    v.phaseStarted = Time.time;
                }
                return true;

            case Phase.Queued:
            {
                // Take the furthest free pump that nothing blocks the way to.
                int bay = FreeBay(v.station);
                if (bay < 0)
                {
                    // Nothing free for a long time: give up and rejoin the road instead.
                    if (Time.time - v.phaseStarted > 60f && !IsVisible(v.car.transform.position)) { EndVisit(v, false); return false; }
                    return true;
                }
                v.bay = bay;
                v.station.occupant[bay] = v;
                v.phase = Phase.ToPump;
                v.lastProgress = Time.time;
                v.lastRemaining = float.MaxValue;
                // Straight along the row line to the pump.
                Vector3 onRow = v.station.bays[bay] + v.station.travel * Vector3.Dot(v.car.transform.position - v.station.bays[bay], v.station.travel);
                v.route = Densify(new List<Vector3> { v.car.transform.position, onRow + v.station.travel * 1.5f, v.station.bays[bay] }, 1f);
                v.driver.Begin(v.route, v.car.GetComponent<TSSimpleCar>(), v.car.FrontPoint);
                return true;
            }

            case Phase.ToPump:
                if (v.driver.CurrentState == TaxiDriver.State.Arrived)
                {
                    v.phase = Phase.Refuelling;
                    v.phaseStarted = Time.time;
                    v.refuelUntil = Time.time + Random.Range(minRefuelSeconds, maxRefuelSeconds);
                    AttachHose(v);
                }
                return true;

            case Phase.Refuelling:
                UpdateHose(v);
                if (Time.time < v.refuelUntil) return true;
                DetachHose(v);
                v.phase = Phase.WaitingToLeave;
                return true;

            case Phase.WaitingToLeave:
                // Cars further along the row have to go first.
                for (int i = v.bay + 1; i < v.station.occupant.Length; i++)
                    if (v.station.occupant[i] != null) return true;
                if (!BeginLeaving(v)) { EndVisit(v, false); return false; }
                return true;

            case Phase.Leaving:
                GiveWay(v);
                if (v.driver.CurrentState == TaxiDriver.State.Arrived)
                {
                    CarsRefuelled++;
                    EndVisit(v, true);
                    return false;
                }
                return true;
        }
        return true;
    }

    // At the forecourt exit, wait for a gap in the traffic coming along the road before pulling out.
    private void GiveWay(Visit v)
    {
        if (v.merged) { v.driver.hold = false; return; }
        Vector3 pos = v.car.transform.position;
        Station s = v.station;
        float toGate = Flat(s.exitGate - pos).magnitude;
        if (toGate > 6f)
        {
            // Already past the gate (on the road): done.
            if (Vector3.Dot(Flat(pos - s.exitGate), s.outward) > 2f) { v.merged = true; v.driver.hold = false; }
            return;
        }
        bool traffic = false;
        foreach (var other in _cars)
        {
            if (other == null || other == v.car || !other.gameObject.activeInHierarchy) continue;
            Vector3 d = Flat(v.joinPoint - other.transform.position);
            float upstream = Vector3.Dot(d, s.travel);
            if (upstream < -4f || upstream > 40f || Mathf.Abs(Vector3.Dot(d, s.outward)) > 4f) continue;
            var body = other.GetComponent<Rigidbody>();
            // Approaching (or sitting right where we want to go).
            if (upstream < 8f || (body != null && Vector3.Dot(body.linearVelocity, s.travel) > 1f)) { traffic = true; break; }
        }
        // Don't wait forever in heavy traffic: after a while nose out (traffic brakes for us).
        if (traffic && v.holdSince <= 0f) v.holdSince = Time.time;
        if (traffic && Time.time - v.holdSince > 20f) traffic = false;
        v.driver.hold = traffic;
        if (!traffic && toGate < 2.5f) v.merged = true;
        if (traffic) v.lastProgress = Time.time;   // waiting to merge isn't being stuck
    }

    // Put a wedged car back on its path a little further on, facing along it, at a spot free of other cars.
    private bool Nudge(Visit v)
    {
        if (v.route == null || v.route.Count < 2) return false;
        Vector3 pos = v.car.transform.position;
        int carMask = (1 << 8) | (1 << v.car.gameObject.layer);
        foreach (var hit in Physics.SphereCastAll(pos + Vector3.up + v.car.transform.forward * 2f, 1f, v.car.transform.forward, 7f, carMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(v.car.transform)) continue;
            var other = hit.collider.GetComponentInParent<TSTrafficAI>();
            foreach (var o in _visits) if (o != v && o.car == other) return false;   // just queuing behind another customer
        }
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < v.route.Count; i++)
        {
            float d = Flat(v.route[i] - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = i; }
        }
        int mask = (1 << 8) | (1 << v.car.gameObject.layer);
        for (int i = nearest + 2; i < v.route.Count - 1; i++)
        {
            Vector3 p = v.route[i];
            if (Flat(p - pos).magnitude < 3f) continue;
            bool blocked = false;
            foreach (var c in Physics.OverlapSphere(p + Vector3.up, 2.3f, mask, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(v.car.transform)) { blocked = true; break; }
            if (blocked) continue;
            Vector3 heading = Flat(v.route[i + 1] - p);
            if (heading.sqrMagnitude < 0.01f) continue;
            RaycastHit ground;
            if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out ground, 10f, ~mask, QueryTriggerInteraction.Ignore)) p.y = ground.point.y;
            var body = v.car.GetComponent<Rigidbody>();
            var rot = Quaternion.LookRotation(heading.normalized);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = p + Vector3.up * 0.4f;
            body.rotation = rot;
            v.car.transform.SetPositionAndRotation(p + Vector3.up * 0.4f, rot);
            return true;
        }
        return false;
    }

    // ---------- forecourt path shapes ----------

    private Vector3 RowStart(Station s) { return s.centre - s.travel * (RowHalfLength(s) + 6f) + s.outward * bayOffset; }
    private Vector3 RowEnd(Station s) { return s.centre + s.travel * (RowHalfLength(s) + 6f) + s.outward * bayOffset; }

    private Vector3 EntryTurnPoint(Station s)
    {
        return roads.lanes[s.entryLane].points[IndexBefore(s.entryLane, ClosestIndex(s.entryLane, s.entryGate), 14f)].point;
    }

    // From the lane (heading along the road) through the gate (heading into the forecourt) and round onto
    // the start of the pump row (heading along it).
    private List<Vector3> EntryShape(Station s, Vector3 turn)
    {
        var pts = new List<Vector3>();
        Vector3 gate = s.entryGate, rowStart = RowStart(s);
        float k1 = Vector3.Distance(turn, gate) * 0.45f, k2 = Vector3.Distance(gate, rowStart) * 0.45f;
        Bezier(pts, turn, turn + s.travel * k1, gate + s.outward * k1, gate);
        Bezier(pts, gate, gate - s.outward * k2, rowStart - s.travel * k2, rowStart);
        return pts;
    }

    // From the end of the pump row, out through the exit gate and round onto the lane.
    private List<Vector3> ExitShape(Station s, Vector3 rowEnd, out int joinIndex)
    {
        var pts = new List<Vector3>();
        Vector3 gate = s.exitGate;
        joinIndex = Mathf.Min(IndexAfter(s.exitLane, ClosestIndex(s.exitLane, gate), 14f), roads.lanes[s.exitLane].points.Length - 1);
        Vector3 join = roads.lanes[s.exitLane].points[joinIndex].point;
        float k1 = Vector3.Distance(rowEnd, gate) * 0.45f, k2 = Vector3.Distance(gate, join) * 0.45f;
        Bezier(pts, rowEnd, rowEnd + s.travel * k1, gate - s.outward * k1, gate);
        Bezier(pts, gate, gate + s.outward * k2, join - s.travel * k2, join);
        return pts;
    }

    private static void Bezier(List<Vector3> into, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float approx = Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, d);
        int n = Mathf.Max(4, Mathf.CeilToInt(approx / 1.5f));
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1f - t;
            into.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d);
        }
    }

    private bool ShapeClear(List<Vector3> pts)
    {
        for (int i = 1; i < pts.Count; i++) if (!PathClear(pts[i - 1], pts[i])) return false;
        return true;
    }

    // True if a car-sized box (wider than a car, since cars cut corners) can travel from a to b without
    // touching fixed scenery such as kerbed islands, walls or sign posts.
    private bool PathClear(Vector3 a, Vector3 b)
    {
        RaycastHit ground;
        float groundY = a.y;
        if (Physics.Raycast(new Vector3(a.x, a.y + 6f, a.z), Vector3.down, out ground, 20f, ~0, QueryTriggerInteraction.Ignore)) groundY = ground.point.y;
        Vector3 from = new Vector3(a.x, groundY + 0.85f, a.z);
        Vector3 dir = Flat(b - a);
        float len = dir.magnitude;
        if (len < 0.1f) return true;
        dir /= len;
        var half = new Vector3(1.3f, 0.6f, 0.3f);
        foreach (var hit in Physics.BoxCastAll(from, half, dir, Quaternion.LookRotation(dir), len, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y > 0.7f || hit.collider.attachedRigidbody != null) continue;
            if (hit.collider.CompareTag("Player") || hit.collider.GetComponentInParent<PedestrianWalker>() != null) continue;
            if (hit.distance <= 0f && hit.point == Vector3.zero) continue;   // started inside something (e.g. the road mesh)
            LastBlocker = hit.collider.name;
            return false;
        }
        return true;
    }

    public string LastBlocker { get; private set; }

    private bool BeginLeaving(Visit v)
    {
        Station s = v.station;
        var route = Densify(new List<Vector3> { v.car.transform.position, RowEnd(s) }, 1.5f);
        int joinIndex;
        route.AddRange(ExitShape(s, RowEnd(s), out joinIndex));
        v.joinPoint = roads.lanes[s.exitLane].points[joinIndex].point;
        v.merged = false;

        // Carry on down the lane a little before handing back to the traffic AI.
        int lane = s.exitLane;
        var pts = roads.lanes[lane].points;
        int endIndex = IndexAfter(lane, joinIndex, 12f);
        if (endIndex >= pts.Length - 2)
        {
            // Lane ends right there: continue onto the lane after the junction.
            var connectors = roads.lanes[lane].connectors;
            if (connectors.Length == 0) return false;
            var c = connectors[Random.Range(0, connectors.Length)];
            for (int k = joinIndex + 1; k < pts.Length; k++) route.Add(pts[k].point);
            foreach (var p in c.points) route.Add(p.point);
            lane = c.nextLane;
            pts = roads.lanes[lane].points;
            joinIndex = Mathf.Min(8, pts.Length - 2);
            for (int k = 0; k <= joinIndex; k++) route.Add(pts[k].point);
        }
        else
        {
            for (int k = joinIndex + 1; k <= endIndex; k++) route.Add(pts[k].point);
            joinIndex = endIndex;
        }
        v.resumeLane = lane;
        v.resumeIndex = joinIndex;

        if (v.bay >= 0) s.occupant[v.bay] = null;
        v.phase = Phase.Leaving;
        v.phaseStarted = Time.time;
        v.lastProgress = Time.time;
        v.lastRemaining = float.MaxValue;
        v.route = Densify(route, 1.5f);
        v.driver.Begin(v.route, v.car.GetComponent<TSSimpleCar>(), v.car.FrontPoint);
        return true;
    }

    private void EndVisit(Visit v, bool resume)
    {
        DetachHose(v);
        if (v.bay >= 0 && v.station.occupant[v.bay] == v) v.station.occupant[v.bay] = null;
        if (v.car == null) return;
        if (v.driver != null) Destroy(v.driver);
        v.car.gameObject.layer = v.rootLayer;
        v.car.gameObject.tag = v.rootTag;
        foreach (var c in v.colliders)
        {
            if (c.Key == null) continue;
            c.Key.gameObject.layer = c.Value.Key;
            c.Key.gameObject.tag = c.Value.Value;
        }
        if (!v.car.gameObject.activeInHierarchy) return;
        if (!resume || !ResumeAI(v.car, v.resumeLane, v.resumeIndex)) v.car.Disable(true, false);
    }

    // ---------- hose and sound ----------

    private void AttachHose(Visit v)
    {
        Transform pump = v.station.pumps[v.bay];
        if (_hoseMaterial == null)
        {
            _hoseMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _hoseMaterial.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
        }
        var go = new GameObject("FuelHose");
        go.transform.SetParent(pump, true);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = _hoseMaterial;
        line.positionCount = 14;
        line.widthMultiplier = 0.05f;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        v.hose = line;
        UpdateHose(v);

        if (_pumpClip == null) _pumpClip = BuildPumpClip();
        v.sound = go.AddComponent<AudioSource>();
        v.sound.clip = _pumpClip;
        v.sound.loop = true;
        v.sound.spatialBlend = 1f;
        v.sound.rolloffMode = AudioRolloffMode.Linear;
        v.sound.maxDistance = 25f;
        v.sound.volume = 0.35f;
        v.sound.Play();
    }

    // A sagging hose from the pump nozzle to the car's filler cap on the pump side.
    private void UpdateHose(Visit v)
    {
        if (v.hose == null) return;
        Transform pump = v.station.pumps[v.bay];
        Bounds pb = pump.GetComponent<Renderer>().bounds;
        Transform car = v.car.transform;
        Vector3 toPump = Flat(pb.center - car.position).normalized;
        Vector3 start = new Vector3(pb.center.x, pb.min.y + pb.size.y * 0.55f, pb.center.z) - toPump * (Mathf.Min(pb.extents.x, pb.extents.z) + 0.05f);
        Vector3 end = car.position + toPump * 0.95f - car.forward * 1.3f + Vector3.up * 0.75f;
        float sag = Mathf.Clamp(Vector3.Distance(start, end) * 0.35f, 0.3f, 1f);
        for (int i = 0; i < v.hose.positionCount; i++)
        {
            float t = i / (float)(v.hose.positionCount - 1);
            Vector3 p = Vector3.Lerp(start, end, t);
            p.y -= Mathf.Sin(t * Mathf.PI) * sag;
            v.hose.SetPosition(i, p);
        }
    }

    private void DetachHose(Visit v)
    {
        if (v.hose != null) Destroy(v.hose.gameObject);
        v.hose = null;
        v.sound = null;
    }

    // Pump motor hum with a soft gurgle, made in code.
    private static AudioClip BuildPumpClip()
    {
        const int rate = 22050;
        int n = rate * 2;
        var data = new float[n];
        var rng = new System.Random(3);
        float noise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            noise = noise * 0.97f + ((float)rng.NextDouble() * 2f - 1f) * 0.03f;
            float hum = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.1f;
            float gurgle = noise * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * t)) * 4f;
            data[i] = (hum + gurgle) * 0.5f;
        }
        var clip = AudioClip.Create("FuelPump", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------- traffic AI hand-back ----------

    private bool ResumeAI(TSTrafficAI car, int lane, int index)
    {
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        System.Type selectionType = typeof(TSTrafficAI).GetNestedType("TSNextLaneSelection", any | System.Reflection.BindingFlags.Static);
        if (selectionType == null) return false;
        var ctor = selectionType.GetConstructor(any, null, new[] { typeof(TSBaseInfo) }, null);
        var listType = typeof(List<>).MakeGenericType(selectionType);
        System.Reflection.MethodInfo addPath = null;
        foreach (var m in typeof(TSTrafficAI).GetMethods(any))
        {
            var ps = m.GetParameters();
            if (m.Name == "AddNextTrackToPath" && ps.Length == 2 && ps[0].ParameterType == listType && ps[1].ParameterType == typeof(int)) addPath = m;
        }
        if (ctor == null || addPath == null) return false;
        var path = (System.Collections.IList)System.Activator.CreateInstance(listType);
        path.Add(ctor.Invoke(new object[] { roads.lanes[lane] }));
        addPath.Invoke(car, new object[] { path, index });
        car.UnPause();
        return true;
    }

    // ---------- lane helpers ----------

    private bool NearestLane(Vector3 pos, Vector3 direction, float maxDistance, out int lane, out int index, out Vector3 point)
    {
        lane = -1; index = 0; point = pos;
        float best = maxDistance * maxDistance;
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var pts = roads.lanes[l].points;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i].point;
                if (Mathf.Abs(a.y - pos.y) > 4f) continue;
                float d = Flat(a - pos).sqrMagnitude;
                if (d >= best) continue;
                if (direction != Vector3.zero && Vector3.Dot(Flat(pts[i + 1].point - a).normalized, direction) < 0.9f) continue;
                best = d; lane = l; index = i; point = a;
            }
        }
        return lane >= 0;
    }

    private Vector3 LaneDirection(int lane, int index)
    {
        var pts = roads.lanes[lane].points;
        int i = Mathf.Clamp(index, 0, pts.Length - 2);
        return Flat(pts[i + 1].point - pts[i].point).normalized;
    }

    private int ClosestIndex(int lane, Vector3 pos)
    {
        var pts = roads.lanes[lane].points;
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Flat(pts[i].point - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private int IndexBefore(int lane, int index, float distance)
    {
        var pts = roads.lanes[lane].points;
        float acc = 0f;
        int i = index;
        while (i > 0 && acc < distance) { acc += Vector3.Distance(pts[i].point, pts[i - 1].point); i--; }
        return i;
    }

    private int IndexAfter(int lane, int index, float distance)
    {
        var pts = roads.lanes[lane].points;
        float acc = 0f;
        int i = index;
        while (i < pts.Length - 1 && acc < distance) { acc += Vector3.Distance(pts[i].point, pts[i + 1].point); i++; }
        return i;
    }

    private float AlongLane(int lane, int from, int to)
    {
        var pts = roads.lanes[lane].points;
        float acc = 0f;
        for (int i = from; i < to; i++) acc += Vector3.Distance(pts[i].point, pts[i + 1].point);
        return acc;
    }

    private static float RowHalfLength(Station s)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var b in s.bays)
        {
            float d = Vector3.Dot(b - s.centre, s.travel);
            min = Mathf.Min(min, d);
            max = Mathf.Max(max, d);
        }
        return Mathf.Max(Mathf.Abs(min), Mathf.Abs(max));
    }

    // Extra points between corners so the pure-pursuit driver follows the shape closely.
    private static List<Vector3> Densify(List<Vector3> pts, float step)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0) { result.Add(pts[0]); continue; }
            Vector3 a = pts[i - 1], b = pts[i];
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int k = 1; k <= n; k++) result.Add(Vector3.Lerp(a, b, k / (float)n));
        }
        return result;
    }

    private bool IsVisible(Vector3 point)
    {
        if (viewCamera == null) return false;
        if ((point - viewCamera.transform.position).sqrMagnitude > 220f * 220f) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(viewCamera);
        return GeometryUtility.TestPlanesAABB(planes, new Bounds(point + Vector3.up, Vector3.one * 3f));
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // Debug view of each station's bays, gates and lanes in the Scene view.
    private void OnDrawGizmosSelected()
    {
        foreach (var s in _stations)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(s.entryGate, 1.5f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(s.exitGate, 1.5f);
            Gizmos.color = Color.yellow;
            foreach (var b in s.bays) Gizmos.DrawWireCube(b + Vector3.up, new Vector3(2f, 1.5f, 2f));
        }
    }
}
