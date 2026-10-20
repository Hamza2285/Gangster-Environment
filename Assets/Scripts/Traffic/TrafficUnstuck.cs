using System.Collections.Generic;
using UnityEngine;

// Watches traffic for cars that stop moving for the wrong reasons and makes them move:
//  - wedged at an angle / pushed off the lane (e.g. after a bad turn): straightened back onto the lane
//  - stationary far longer than any traffic light cycle (junction gridlock): recycled if unseen,
//    otherwise driven through onto the next lane and handed back to the traffic AI.
// Cars waiting at a red light, and cars already under script control (e.g. a called taxi), are left alone.
public class TrafficUnstuck : MonoBehaviour
{
    public TSMainManager roads;
    public Camera viewCamera;

    [Tooltip("Seconds a crooked / off-lane car may sit still before it is straightened.")]
    public float crookedSeconds = 10f;
    [Tooltip("Heading difference from its lane (degrees) that counts as crooked.")]
    public float crookedAngle = 25f;
    [Tooltip("Distance from its lane (metres) that counts as off-lane.")]
    public float offLaneDistance = 2f;
    [Tooltip("Seconds a car may be stationary before it is treated as stuck (longer than any light cycle).")]
    public float stuckSeconds = 90f;
    public float forcedDriveSpeed = 6f;
    [Tooltip("A forced drive that makes no progress for this long gives up (and recycles the car once unseen).")]
    public float forcedDriveTimeout = 25f;

    [Header("Following distance")]
    [Tooltip("Gap (metres) traffic cars keep to the car in front, min/max, and at junctions. iTS default is 1.5 / 3 / 5.")]
    public float gapMin = 3f;
    public float gapMax = 5f;
    public float gapJunctions = 7f;
    [Tooltip("How far ahead traffic looks for the player and script-driven cars (taxi, police, fuel). iTS default is 1.")]
    public float sensorLength = 1.6f;
    [Tooltip("Lowers every traffic car's centre of mass by this much so knocks don't roll it over.")]
    public float centreOfMassDrop = 0.3f;

    [Header("Horns")]
    [Tooltip("A car held up this long by something in front of it (not a red light) starts honking.")]
    public float hornAfterSeconds = 4f;
    public float hornMinInterval = 3f;
    public float hornMaxInterval = 7f;
    [Tooltip("A stationary car that others are honking at is cleared after this long instead of stuckSeconds.")]
    public float blockerSeconds = 15f;
    [Tooltip("Pedestrians standing in front of a honking car get out of the way.")]
    public PedestrianSpawner pedestrians;

    private class Watch
    {
        public Vector3 lastPosition;
        public float stillSince;
        public float lastAlign;
        public float lastEval;
        public float nextHorn;
    }

    private class Forced
    {
        public TaxiDriver driver;
        public int resumeLane, resumeIndex;
        public float startedAt, lastProgressAt, lastRemaining;
        public int rootLayer;
        public string rootTag;
        public List<KeyValuePair<Collider, KeyValuePair<int, string>>> colliders;
    }

    private readonly Dictionary<TSTrafficAI, Watch> _watch = new Dictionary<TSTrafficAI, Watch>();
    private readonly Dictionary<TSTrafficAI, Forced> _forced = new Dictionary<TSTrafficAI, Forced>();
    private readonly Dictionary<TSTrafficAI, float> _blamedAt = new Dictionary<TSTrafficAI, float>();
    private readonly Dictionary<int, List<TSTrafficLight>> _lightsAtLaneEnd = new Dictionary<int, List<TSTrafficLight>>();
    private TSTrafficAI[] _cars = new TSTrafficAI[0];
    private float _scanTimer;
    private int _cursor;

    public int CarsStraightened { get; private set; }
    public int CarsRecycled { get; private set; }
    public int CarsPushedThrough { get; private set; }
    public int HornsSounded { get; private set; }
    public int BlockersCleared { get; private set; }

    private void Start()
    {
        if (roads == null) roads = FindFirstObjectByType<TSMainManager>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (pedestrians == null) pedestrians = FindFirstObjectByType<PedestrianSpawner>();
        foreach (var light in FindObjectsByType<TSTrafficLight>(FindObjectsSortMode.None))
        {
            foreach (var p in light.pointsNormalLight)
            {
                List<TSTrafficLight> list;
                if (!_lightsAtLaneEnd.TryGetValue(p.lane, out list)) _lightsAtLaneEnd[p.lane] = list = new List<TSTrafficLight>();
                if (!list.Contains(light)) list.Add(light);
            }
        }
    }

    // Rolling average of this component's own cost per frame, in milliseconds.
    public float AverageMilliseconds { get; private set; }
    private readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();

    private void Update()
    {
        _timer.Reset();
        _timer.Start();
        Tick();
        _timer.Stop();
        AverageMilliseconds = Mathf.Lerp(AverageMilliseconds, (float)_timer.Elapsed.TotalMilliseconds, 0.02f);
    }

    private void Tick()
    {
        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f)
        {
            _scanTimer = 1f;
            _cars = FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None);
            foreach (var c in _cars) ApplySafety(c);
        }

        UpdateForcedDrives();

        // Check a slice of the cars each frame.
        int count = Mathf.Min(40, _cars.Length);
        for (int i = 0; i < count; i++)
        {
            if (_cursor >= _cars.Length) _cursor = 0;
            CheckCar(_cars[_cursor++]);
        }
    }

    private void CheckCar(TSTrafficAI car)
    {
        if (car == null || !car.gameObject.activeInHierarchy) return;
        if (_forced.ContainsKey(car)) return;
        var crash = car.GetComponent<BikeCrash>();   // a knocked-over bike clears itself away once out of sight
        // Cars driven by other scripts (a called taxi) are not ours to fix.
        if (car.GetComponent<TaxiDriver>() != null || car.GetComponent<ChaseDriver>() != null || !car.IsEnabled || (crash != null && crash.Crashed)) { _watch.Remove(car); return; }

        Watch w;
        Vector3 pos = car.transform.position;
        if (!_watch.TryGetValue(car, out w))
        {
            _watch[car] = new Watch { lastPosition = pos, stillSince = Time.time, lastAlign = -100f, lastEval = -100f };
            return;
        }

        if ((pos - w.lastPosition).sqrMagnitude > 1f)
        {
            w.lastPosition = pos;
            w.stillSince = Time.time;
            return;
        }

        float still = Time.time - w.stillSince;
        // Horns only matter where they can be heard (and the checks aren't free).
        if (still > hornAfterSeconds && Time.time >= w.nextHorn && viewCamera != null
            && (pos - viewCamera.transform.position).sqrMagnitude < CarHorn.MaxDistance * CarHorn.MaxDistance)
        {
            w.nextHorn = Time.time + Random.Range(hornMinInterval, hornMaxInterval);
            TryHorn(car);
        }

        // Others are honking at this car: it is holding up the road, so deal with it much sooner.
        float blamed;
        bool isBlocker = _blamedAt.TryGetValue(car, out blamed) && Time.time - blamed < 15f;
        float crookedLimit = isBlocker ? 4f : crookedSeconds;
        float stuckLimit = isBlocker ? blockerSeconds : stuckSeconds;

        // Flipped or on its side: fix it sooner.
        bool flipped = car.transform.up.y < 0.5f;
        // Finding the car's lane scans the whole road network, so only do it for cars that have been
        // still for a while, and at most every few seconds per car.
        if ((still < crookedLimit && !(flipped && still > 5f)) || Time.time - w.lastEval < (isBlocker ? 1.5f : 3f)) return;
        w.lastEval = Time.time;
        int lane, index;
        bool onConnector;
        float angle, offset;
        if (!NearestPathPoint(car, out lane, out index, out onConnector, out angle, out offset)) return;

        // Physically wedged: straighten it onto its lane.
        if ((flipped || (still > crookedLimit && (angle > crookedAngle || offset > offLaneDistance))) && Time.time - w.lastAlign > crookedLimit)
        {
            Straighten(car, lane, index, onConnector);
            w.lastAlign = Time.time;
            w.stillSince = Time.time;
            CarsStraightened++;
            if (isBlocker) { BlockersCleared++; _blamedAt.Remove(car); }
            return;
        }

        if (still < stuckLimit) return;

        // Queued at a red light: wait for green before judging. The timer keeps running, so a car that
        // still hasn't moved after a full green phase is treated as stuck.
        if (!onConnector && WaitingAtRedLight(lane, index)) return;

        if (isBlocker) { BlockersCleared++; _blamedAt.Remove(car); }
        if (!IsVisible(car))
        {
            Recycle(car);
        }
        else if (!StartForcedDrive(car, lane, index, onConnector))
        {
            w.stillSince = Time.time - stuckLimit * 0.5f;   // try again a bit later
        }
    }

    // Honk at whatever is in front, unless the car is simply waiting at a red light.
    private void TryHorn(TSTrafficAI car)
    {
        Transform front = car.FrontPoint;
        Vector3 fwd = car.transform.forward;
        Vector3 origin = (front != null ? front.position : car.transform.position + fwd * 2.2f) + Vector3.up * 0.3f;
        RaycastHit best = new RaycastHit();
        bool found = false;
        foreach (var hit in Physics.SphereCastAll(origin, 0.8f, fwd, 9f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y > 0.7f || hit.collider.transform.IsChildOf(car.transform)) continue;
            if (!found || hit.distance < best.distance) { best = hit; found = true; }
        }
        if (!found) return;

        var other = best.collider.GetComponentInParent<TSTrafficAI>();
        if (other != null)
        {
            var body = other.GetComponent<Rigidbody>();
            if (body != null && body.linearVelocity.magnitude > 1f) return;   // it's already moving off
        }
        int lane, index;
        bool onConnector;
        float angle, offset;
        if (NearestPathPoint(car, out lane, out index, out onConnector, out angle, out offset) && !onConnector && WaitingAtRedLight(lane, index)) return;

        if (!CarHorn.Honk(car.transform, Random.value < 0.4f)) return;
        HornsSounded++;
        if (other != null && other.GetComponent<TaxiDriver>() == null && other.GetComponent<ChaseDriver>() == null)
            _blamedAt[other] = Time.time;
        else if (pedestrians != null && best.collider.GetComponentInParent<PedestrianWalker>() != null)
            pedestrians.TriggerPanic(best.point, 5f);
    }

    private readonly HashSet<TSTrafficAI> _tuned = new HashSet<TSTrafficAI>();

    // Longer gaps to the car in front and an earlier look-out for script-driven cars, so a sudden stop
    // doesn't turn into a pile-up; and a lower centre of mass so a knock doesn't roll a car over.
    private void ApplySafety(TSTrafficAI car)
    {
        if (car == null || _tuned.Contains(car)) return;
        _tuned.Add(car);
        car.lengthMarginMin = Mathf.Max(car.lengthMarginMin, gapMin);
        car.lengthMarginMax = Mathf.Max(car.lengthMarginMax, gapMax);
        car.lengthMarginJunctions = Mathf.Max(car.lengthMarginJunctions, gapJunctions);
        car.playerSensorLengthMultiplier = Mathf.Max(car.playerSensorLengthMultiplier, sensorLength);
        var body = car.GetComponent<Rigidbody>();
        if (body != null) body.centerOfMass = body.centerOfMass - Vector3.up * centreOfMassDrop;
    }

    // ---------- fixes ----------

    private void Straighten(TSTrafficAI car, int lane, int index, bool onConnector)
    {
        int pointIndex;
        Vector3[] pts = PathPoints(lane, index, onConnector, out pointIndex);
        int i = Mathf.Clamp(pointIndex, 0, pts.Length - 2);
        Vector3 heading = Flat(pts[i + 1] - pts[i]);
        if (heading.sqrMagnitude < 1e-4f) return;
        var body = car.GetComponent<Rigidbody>();
        Vector3 target = pts[i] + Vector3.up * 0.3f;
        // Never drop it onto another car or a pole: only straighten into free space.
        var rot0 = Quaternion.LookRotation(heading.normalized);
        foreach (var col in Physics.OverlapBox(pts[i] + Vector3.up * 1.1f, new Vector3(1.1f, 0.7f, 2.5f), rot0, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(car.transform)) continue;
            if (col.attachedRigidbody != null || col.GetComponentInParent<TSTrafficAI>() != null) return;
            var mc = col as MeshCollider;
            if (mc == null || mc.convex) { if (col.bounds.size.y < 6f && col.bounds.max.y > pts[i].y + 0.4f) return; }
        }
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = target;
        body.rotation = Quaternion.LookRotation(heading.normalized);
        car.transform.SetPositionAndRotation(target, Quaternion.LookRotation(heading.normalized));
    }

    private void Recycle(TSTrafficAI car)
    {
        _watch.Remove(car);
        CarsRecycled++;
        car.Disable(true, false);
    }

    // Drive the car along its own path through whatever it is stuck on, then give it back to the AI.
    private bool StartForcedDrive(TSTrafficAI car, int lane, int index, bool onConnector)
    {
        var route = new List<Vector3> { car.transform.position };
        int resumeLane, resumeIndex;
        if (onConnector)
        {
            int ci;
            TSLaneConnector c = ConnectorOf(lane, index, out ci);
            for (int k = ci + 1; k < c.points.Length; k++) route.Add(c.points[k].point);
            resumeLane = c.nextLane;
        }
        else
        {
            var lanePts = roads.lanes[lane].points;
            for (int k = index + 1; k < lanePts.Length; k++) route.Add(lanePts[k].point);
            var connectors = roads.lanes[lane].connectors;
            if (connectors.Length == 0) return false;
            TSLaneConnector c = connectors[Random.Range(0, connectors.Length)];
            foreach (var p in c.points) route.Add(p.point);
            resumeLane = c.nextLane;
        }
        if (resumeLane < 0 || resumeLane >= roads.lanes.Length) return false;
        var nextPts = roads.lanes[resumeLane].points;
        resumeIndex = Mathf.Min(12, nextPts.Length - 2);
        for (int k = 0; k <= resumeIndex; k++) route.Add(nextPts[k].point);
        if (route.Count < 3) return false;

        car.Pause();
        var unreserve = typeof(TSTrafficAI).GetMethod("UnReserveAll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (unreserve != null) unreserve.Invoke(car, null);

        // Other traffic brakes for anything on its player-sensor layer, so nothing drives into it.
        var f = new Forced
        {
            resumeLane = resumeLane,
            resumeIndex = resumeIndex,
            startedAt = Time.time,
            lastProgressAt = Time.time,
            lastRemaining = float.MaxValue,
            rootLayer = car.gameObject.layer,
            rootTag = car.gameObject.tag,
            colliders = new List<KeyValuePair<Collider, KeyValuePair<int, string>>>()
        };
        foreach (var col in car.GetComponentsInChildren<Collider>(true))
            f.colliders.Add(new KeyValuePair<Collider, KeyValuePair<int, string>>(col, new KeyValuePair<int, string>(col.gameObject.layer, col.gameObject.tag)));
        int sensorLayer = LayerMask.NameToLayer("Player");
        int mask = car.playerLayer.value;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { sensorLayer = i; break; }
        car.gameObject.layer = sensorLayer;
        car.gameObject.tag = car.playerTag;
        foreach (var col in f.colliders) { col.Key.gameObject.layer = sensorLayer; col.Key.gameObject.tag = car.playerTag; }

        f.driver = car.gameObject.AddComponent<TaxiDriver>();
        f.driver.cruiseSpeed = forcedDriveSpeed;
        f.driver.followGap = 3f;
        f.driver.Begin(route, car.GetComponent<TSSimpleCar>(), car.FrontPoint);
        _forced[car] = f;
        _watch.Remove(car);
        CarsPushedThrough++;
        return true;
    }

    private void UpdateForcedDrives()
    {
        if (_forced.Count == 0) return;
        var finished = new List<TSTrafficAI>();
        foreach (var kv in _forced)
        {
            TSTrafficAI car = kv.Key;
            Forced f = kv.Value;
            if (car == null || !car.gameObject.activeInHierarchy || f.driver == null) { finished.Add(car); continue; }

            if (f.driver.RemainingDistance < f.lastRemaining - 0.5f)
            {
                f.lastRemaining = f.driver.RemainingDistance;
                f.lastProgressAt = Time.time;
            }

            bool arrived = f.driver.CurrentState == TaxiDriver.State.Arrived;
            bool blocked = Time.time - f.lastProgressAt > forcedDriveTimeout;
            if (!arrived && !blocked) continue;

            EndForcedDrive(car, f, resume: arrived || IsVisible(car));
            finished.Add(car);
        }
        foreach (var car in finished) _forced.Remove(car);
    }

    private void EndForcedDrive(TSTrafficAI car, Forced f, bool resume)
    {
        if (f.driver != null) Destroy(f.driver);
        car.gameObject.layer = f.rootLayer;
        car.gameObject.tag = f.rootTag;
        foreach (var col in f.colliders)
        {
            if (col.Key == null) continue;
            col.Key.gameObject.layer = col.Value.Key;
            col.Key.gameObject.tag = col.Value.Value;
        }

        if (!resume || !ResumeAI(car, f.resumeLane, f.resumeIndex)) car.Disable(true, false);
    }

    // Restart the car's own AI from a lane point, the way iTS starts cars placed mid-lane.
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

    // ---------- helpers ----------

    private bool WaitingAtRedLight(int lane, int index)
    {
        List<TSTrafficLight> lights;
        if (!_lightsAtLaneEnd.TryGetValue(lane, out lights)) return false;
        var pts = roads.lanes[lane].points;
        float toEnd = 0f;
        for (int i = index; i < pts.Length - 1 && toEnd < 60f; i++) toEnd += Vector3.Distance(pts[i].point, pts[i + 1].point);
        if (toEnd >= 60f) return false;
        foreach (var light in lights)
            if (light.lights[Mathf.Clamp(light.lightToPlay, 0, light.lights.Count - 1)].lightType != TSTrafficLight.LightType.Green) return true;
        return false;
    }

    public int ForcedDrivesActive { get { return _forced.Count; } }

    private bool IsVisible(TSTrafficAI car)
    {
        if (viewCamera == null) return false;
        var body = car.GetComponentInChildren<MeshRenderer>();
        if (body == null || !body.enabled) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(viewCamera);
        return GeometryUtility.TestPlanesAABB(planes, body.bounds) && (car.transform.position - viewCamera.transform.position).sqrMagnitude < 220f * 220f;
    }

    // Nearest lane or junction-curve point whose direction matches the car; index encodes the connector
    // as (connectorIndex << 16 | pointIndex) when onConnector.
    // Every lane and junction-curve segment, bucketed by area so lookups only check nearby ones.
    private struct Segment
    {
        public Vector3 a, b;
        public int lane, index;
        public bool connector;
    }

    private const float CellSize = 8f;
    private Dictionary<Vector2Int, List<Segment>> _segmentGrid;

    private void BuildSegmentGrid()
    {
        _segmentGrid = new Dictionary<Vector2Int, List<Segment>>();
        for (int l = 0; l < roads.lanes.Length; l++)
        {
            var ln = roads.lanes[l];
            var pts = ln.points;
            for (int i = 0; i < pts.Length - 1; i++)
                AddSegment(new Segment { a = pts[i].point, b = pts[i + 1].point, lane = l, index = i, connector = false });
            for (int c = 0; c < ln.connectors.Length; c++)
            {
                var cp = ln.connectors[c].points;
                for (int i = 0; i < cp.Length - 1; i++)
                    AddSegment(new Segment { a = cp[i].point, b = cp[i + 1].point, lane = l, index = (c << 16) | i, connector = true });
            }
        }
    }

    private void AddSegment(Segment s)
    {
        var ca = CellOf(s.a);
        var cb = CellOf(s.b);
        for (int x = Mathf.Min(ca.x, cb.x); x <= Mathf.Max(ca.x, cb.x); x++)
        for (int z = Mathf.Min(ca.y, cb.y); z <= Mathf.Max(ca.y, cb.y); z++)
        {
            var key = new Vector2Int(x, z);
            List<Segment> list;
            if (!_segmentGrid.TryGetValue(key, out list)) _segmentGrid[key] = list = new List<Segment>();
            list.Add(s);
        }
    }

    private static Vector2Int CellOf(Vector3 p)
    {
        return new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
    }

    private bool NearestPathPoint(TSTrafficAI car, out int lane, out int index, out bool onConnector, out float angle, out float offset)
    {
        lane = -1; index = 0; onConnector = false; angle = 0f; offset = float.MaxValue;
        if (_segmentGrid == null) BuildSegmentGrid();
        Vector3 pos = car.transform.position;
        Vector3 fwd = Flat(car.transform.forward).normalized;
        float best = 12f * 12f;
        Vector3 bestHeading = fwd;
        var centre = CellOf(pos);
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            List<Segment> list;
            if (!_segmentGrid.TryGetValue(centre + new Vector2Int(dx, dz), out list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                Segment s = list[i];
                Consider(s.a, s.b, pos, fwd, s.lane, s.index, s.connector, ref best, ref lane, ref index, ref onConnector, ref bestHeading);
            }
        }
        if (lane < 0) return false;
        angle = Vector3.Angle(fwd, bestHeading);
        offset = Mathf.Sqrt(best);
        return true;
    }

    private static void Consider(Vector3 a, Vector3 b, Vector3 pos, Vector3 fwd, int l, int i, bool connector,
        ref float best, ref int lane, ref int index, ref bool onConnector, ref Vector3 heading)
    {
        if (Mathf.Abs(a.y - pos.y) > 3f) return;
        Vector3 h = Flat(b - a);
        if (h.sqrMagnitude < 1e-4f) return;
        // Distance to the segment, not its start point, so a car between two points isn't "off lane".
        float t = Mathf.Clamp01(Vector3.Dot(Flat(pos - a), h) / h.sqrMagnitude);
        Vector3 d = Flat(a + h * t - pos);
        float sq = d.sqrMagnitude;
        if (sq >= best) return;
        // Prefer paths going roughly the way the car points; a badly rotated car still matches within 100°.
        if (Vector3.Dot(h.normalized, fwd) < -0.2f) return;
        best = sq; lane = l; index = i; onConnector = connector; heading = h.normalized;
    }

    private Vector3[] PathPoints(int lane, int index, bool onConnector, out int pointIndex)
    {
        if (onConnector)
        {
            TSLaneConnector c = ConnectorOf(lane, index, out pointIndex);
            var result = new Vector3[c.points.Length];
            for (int i = 0; i < result.Length; i++) result[i] = c.points[i].point;
            return result;
        }
        pointIndex = index;
        var pts = roads.lanes[lane].points;
        var arr = new Vector3[pts.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = pts[i].point;
        return arr;
    }

    private TSLaneConnector ConnectorOf(int lane, int encoded, out int pointIndex)
    {
        pointIndex = encoded & 0xFFFF;
        return roads.lanes[lane].connectors[encoded >> 16];
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
