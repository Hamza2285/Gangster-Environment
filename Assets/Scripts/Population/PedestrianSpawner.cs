using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Mirrors the traffic strategy for people: walkers exist only inside the car spawner's disc around
// the player, are never placed where the camera can see them appear, and are only rendered inside
// the same view radius the cars use. They move only along the baked footpath lanes.
[RequireComponent(typeof(FootpathLaneNetwork))]
public class PedestrianSpawner : MonoBehaviour
{
    const float SlotSpacing = 3f;
    const float SlotCellSize = 40f;
    const float MinSpawnGap = 4f;

    [Header("References")]
    public GameObject[] peoplePrefabs;
    public TSTrafficSpawner carSpawner;
    public TrafficViewRadius viewRadius;

    [Header("Density")]
    [Tooltip("Scale the number of people to the number of cars in the same areas.")]
    public bool matchCarDensity = true;
    [Tooltip("People per car when matching car density (2 = twice as many people as cars in the same area).")]
    public float peoplePerCar = 2f;
    [Tooltip("Pool size; also the active count when not matching car density.")]
    public int amount = 240;
    public float spawnRadius = 220f;

    [Header("Spawn rules")]
    public float minSpawnDistanceFromCamera = 40f;
    [Tooltip("Extra viewport margin treated as 'on screen', so people don't appear just past the screen edge.")]
    public float viewportMargin = 0.25f;
    public float walkSpeedMin = 1.1f;
    public float walkSpeedMax = 1.6f;
    [Tooltip("Only people this close to the camera cast shadows.")]
    public float shadowDistance = 60f;

    [Header("Panic")]
    [Tooltip("NavMesh over roads, footpaths and plazas; loaded only while someone is off their lane.")]
    public NavMeshData panicNavMesh;
    [Tooltip("Defaults to the player's CharacterController.")]
    public Transform player;
    public bool panicWhenPlayerNear = true;
    public float panicTriggerRadius = 12f;
    [Tooltip("Fleeing people keep running while the player is within this distance.")]
    public float calmDistance = 30f;
    public float panicDurationMin = 8f;
    public float panicDurationMax = 14f;
    public float runSpeedMin = 3.5f;
    public float runSpeedMax = 5f;
    [Tooltip("Seconds the panic NavMesh stays loaded after everyone is back on their lanes.")]
    public float panicNavMeshUnloadDelay = 10f;

    [Header("Budget")]
    public int instantiatePerFrame = 10;
    public int spawnAttemptsPerFrame = 40;
    public int peopleCheckedPerFrame = 30;

    private FootpathLaneNetwork _network;
    private readonly List<PedestrianWalker> _active = new List<PedestrianWalker>();
    private readonly Queue<PedestrianWalker> _inactive = new Queue<PedestrianWalker>();
    private List<PedestrianWalker>[] _laneWalkers;
    private Transform _poolRoot;
    private Transform _activeRoot;
    private int _created;
    private int _cursor;
    private int _targetActive;
    private int _targetInView;
    private int _peopleInView;
    private float _retargetTimer;
    private NavMeshDataInstance _panicNavMeshInstance;
    private float _lastOffLaneTime;
    private int _offLaneCount;
    private int _carSensorLayer = -1;

    public int CarSensorLayer
    {
        get
        {
            if (_carSensorLayer >= 0) return _carSensorLayer;
            _carSensorLayer = LayerMask.NameToLayer("Player");
            if (carSpawner != null && carSpawner.cars.Length > 0 && carSpawner.cars[0].cars != null)
            {
                var ai = carSpawner.cars[0].cars.GetComponent<TSTrafficAI>();
                int mask = ai != null ? ai.playerLayer.value : 0;
                for (int i = 0; i < 32; i++)
                    if ((mask & (1 << i)) != 0) { _carSensorLayer = i; break; }
            }
            if (_carSensorLayer < 0) _carSensorLayer = 0;
            return _carSensorLayer;
        }
    }

    // Candidate spawn points along every lane, bucketed by area.
    private int[] _slotLane;
    private float[] _slotDistance;
    private Vector3[] _slotPosition;
    private readonly Dictionary<Vector2Int, List<int>> _slotCells = new Dictionary<Vector2Int, List<int>>();
    private readonly List<int> _slotsInDisc = new List<int>();
    private readonly List<int> _slotsInView = new List<int>();

    private Camera Cam
    {
        get { return viewRadius != null && viewRadius.targetCamera != null ? viewRadius.targetCamera : Camera.main; }
    }

    private Vector3 Center
    {
        get { return carSpawner != null ? carSpawner.transform.position : Cam.transform.position; }
    }

    private float VisibleRadius
    {
        get { return viewRadius != null ? viewRadius.visibleRadius : 160f; }
    }

    private float HideMargin
    {
        get { return viewRadius != null ? viewRadius.hideMargin : 25f; }
    }

    private int OutsideTarget
    {
        get { return Mathf.Max(0, _targetActive - _targetInView); }
    }

    private void Start()
    {
        _network = GetComponent<FootpathLaneNetwork>();
        if (matchCarDensity && carSpawner != null)
        {
            amount = Mathf.CeilToInt(carSpawner.amount * peoplePerCar);
            spawnRadius = carSpawner.maxDistance;
        }
        _targetActive = matchCarDensity ? 0 : amount;
        _targetInView = matchCarDensity ? 0 : amount;

        _laneWalkers = new List<PedestrianWalker>[_network.lanes.Count];
        for (int i = 0; i < _laneWalkers.Length; i++) _laneWalkers[i] = new List<PedestrianWalker>();
        BuildSlots();

        // Pooled people sit under an inactive root so they never run or render until placed.
        _poolRoot = new GameObject("PedestrianPool").transform;
        _poolRoot.SetParent(transform, false);
        _poolRoot.gameObject.SetActive(false);
        _activeRoot = new GameObject("Pedestrians").transform;
        _activeRoot.SetParent(transform, false);

        if (player == null)
        {
            foreach (var cc in FindObjectsOfType<CharacterController>())
                if (cc.CompareTag("Player")) { player = cc.transform; break; }
        }
    }

    private void OnDisable()
    {
        if (_panicNavMeshInstance.valid) _panicNavMeshInstance.Remove();
    }

    private void Update()
    {
        Camera cam = Cam;
        if (cam == null || peoplePrefabs.Length == 0 || _network.lanes.Count == 0) return;

        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.5f;
            if (matchCarDensity && viewRadius != null)
            {
                _targetActive = Mathf.Min(amount, Mathf.RoundToInt(viewRadius.CountCarsWithin(Center, spawnRadius) * peoplePerCar));
                _targetInView = Mathf.RoundToInt(viewRadius.CountCarsWithin(cam.transform.position, VisibleRadius) * peoplePerCar);
            }
            _peopleInView = CountPeopleWithin(cam.transform.position, VisibleRadius);
            CollectSlots(_slotsInDisc, Center, spawnRadius);
            CollectSlots(_slotsInView, cam.transform.position, VisibleRadius);
        }

        ReleaseParked();
        CreatePooled();
        SpawnPooled(cam);
        if (panicWhenPlayerNear && player != null) TriggerPanic(player.position, panicTriggerRadius);
        MoveWalkers(Time.deltaTime);
        CheckActive(cam);
        UpdatePanicNavMesh();
    }

    // Makes everyone within radius of the point leave their lanes and run from it (player proximity, crime scenes, etc).
    public void TriggerPanic(Vector3 point, float radius)
    {
        float radiusSqr = radius * radius;
        for (int i = 0; i < _active.Count; i++)
        {
            PedestrianWalker walker = _active[i];
            Vector3 d = walker.transform.position - point;
            d.y = 0f;
            if (d.sqrMagnitude > radiusSqr) continue;

            PedestrianWalker.Mode before = walker.State;
            if (!walker.StartPanic(point, Random.Range(panicDurationMin, panicDurationMax), Random.Range(runSpeedMin, runSpeedMax))) continue;
            if (before == PedestrianWalker.Mode.Lane) _laneWalkers[walker.Lane].Remove(walker);
        }
    }

    public bool EnsurePanicNavMesh()
    {
        if (panicNavMesh == null) return false;
        if (!_panicNavMeshInstance.valid) _panicNavMeshInstance = NavMesh.AddNavMeshData(panicNavMesh);
        _lastOffLaneTime = Time.time;
        return _panicNavMeshInstance.valid;
    }

    private void UpdatePanicNavMesh()
    {
        if (!_panicNavMeshInstance.valid) return;
        if (_offLaneCount > 0) _lastOffLaneTime = Time.time;
        else if (Time.time - _lastOffLaneTime > panicNavMeshUnloadDelay) _panicNavMeshInstance.Remove();
    }

    public void OnRejoinedLane(PedestrianWalker walker)
    {
        _laneWalkers[walker.Lane].Add(walker);
    }

    public bool TrySampleOpenNavPoint(Vector3 around, float maxDistance, out Vector3 point)
    {
        NavMeshHit hit;
        point = around;
        if (!NavMesh.SamplePosition(around, out hit, maxDistance, NavMesh.AllAreas)) return false;
        point = hit.position;
        // A few hollow building shells still have walkable floors; never run into anything with a roof overhead.
        RaycastHit roof;
        return !(Physics.Raycast(point + Vector3.up * 120f, Vector3.down, out roof, 118f, ~0, QueryTriggerInteraction.Ignore)
                 && roof.point.y > point.y + 2.5f && roof.collider.GetComponentInParent<PedestrianWalker>() == null);
    }

    public bool TryFindNearestLanePoint(Vector3 from, NavMeshAgent agent, out int lane, out float distance, out Vector3 point)
    {
        lane = -1;
        distance = 0f;
        point = from;

        var candidates = new List<int>();
        for (float radius = 40f; radius <= 160f && candidates.Count == 0; radius *= 2f)
            CollectSlots(candidates, from, radius);
        candidates.Sort((a, b) => (_slotPosition[a] - from).sqrMagnitude.CompareTo((_slotPosition[b] - from).sqrMagnitude));

        var path = new NavMeshPath();
        int tried = 0;
        for (int i = 0; i < candidates.Count && tried < 8; i++)
        {
            int slot = candidates[i];
            if (!LaneHasRoom(_slotLane[slot], _slotDistance[slot])) continue;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(_slotPosition[slot], out hit, 1f, NavMesh.AllAreas)) continue;
            tried++;
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            lane = _slotLane[slot];
            distance = _slotDistance[slot];
            point = hit.position;
            return true;
        }
        return false;
    }

    private void BuildSlots()
    {
        var lanes = new List<int>();
        var distances = new List<float>();
        var positions = new List<Vector3>();
        for (int l = 0; l < _network.lanes.Count; l++)
        {
            FootpathLane lane = _network.lanes[l];
            for (float d = 0f; d < lane.Length - 1f; d += SlotSpacing)
            {
                Vector3 forward;
                Vector3 p = _network.Evaluate(l, d, out forward);
                int index = positions.Count;
                lanes.Add(l);
                distances.Add(d);
                positions.Add(p);
                Vector2Int cell = CellOf(p);
                List<int> list;
                if (!_slotCells.TryGetValue(cell, out list)) _slotCells[cell] = list = new List<int>();
                list.Add(index);
            }
        }
        _slotLane = lanes.ToArray();
        _slotDistance = distances.ToArray();
        _slotPosition = positions.ToArray();
    }

    private static Vector2Int CellOf(Vector3 p)
    {
        return new Vector2Int(Mathf.FloorToInt(p.x / SlotCellSize), Mathf.FloorToInt(p.z / SlotCellSize));
    }

    private void CollectSlots(List<int> into, Vector3 center, float radius)
    {
        into.Clear();
        float radiusSqr = radius * radius;
        Vector2Int min = CellOf(center - new Vector3(radius, 0f, radius));
        Vector2Int max = CellOf(center + new Vector3(radius, 0f, radius));
        for (int x = min.x; x <= max.x; x++)
        for (int z = min.y; z <= max.y; z++)
        {
            List<int> list;
            if (!_slotCells.TryGetValue(new Vector2Int(x, z), out list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 d = _slotPosition[list[i]] - center;
                d.y = 0f;
                if (d.sqrMagnitude <= radiusSqr) into.Add(list[i]);
            }
        }
    }

    private void CreatePooled()
    {
        for (int i = 0; i < instantiatePerFrame && _created < amount && _active.Count + _inactive.Count + _parked.Count < _targetActive; i++, _created++)
        {
            GameObject prefab = peoplePrefabs[Random.Range(0, peoplePrefabs.Length)];
            GameObject go = Instantiate(prefab, _poolRoot);

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);

            var walker = go.AddComponent<PedestrianWalker>();
            walker.Setup(_network, this, agent);
            _inactive.Enqueue(walker);
        }
    }

    private void SpawnPooled(Camera cam)
    {
        Vector3 camPos = cam.transform.position;
        for (int attempt = 0; attempt < spawnAttemptsPerFrame && _inactive.Count > 0; attempt++)
        {
            // People inside the view radius match the cars there; the rest of the disc matches the remaining cars.
            bool needInside = _peopleInView < _targetInView && _slotsInView.Count > 0;
            bool needOutside = _active.Count - _peopleInView < OutsideTarget && _slotsInDisc.Count > 0;
            if (!needInside && !needOutside) break;

            bool pickInside = needInside && (!needOutside || Random.value < 0.5f);
            List<int> source = pickInside ? _slotsInView : _slotsInDisc;
            int slot = source[Random.Range(0, source.Count)];
            Vector3 point = _slotPosition[slot];

            if (!IsHiddenFromCamera(cam, point)) continue;
            bool insideView = (point - camPos).sqrMagnitude <= VisibleRadius * VisibleRadius;
            if (insideView ? !needInside : !needOutside) continue;
            if (!LaneHasRoom(_slotLane[slot], _slotDistance[slot])) continue;

            PedestrianWalker walker = _inactive.Dequeue();
            walker.transform.SetParent(_activeRoot, true);
            walker.Place(_slotLane[slot], _slotDistance[slot], Random.Range(walkSpeedMin, walkSpeedMax));
            walker.SetVisible(insideView);
            _laneWalkers[walker.Lane].Add(walker);
            _active.Add(walker);
            if (insideView) _peopleInView++;
        }
    }

    private bool LaneHasRoom(int lane, float distance)
    {
        List<PedestrianWalker> list = _laneWalkers[lane];
        for (int i = 0; i < list.Count; i++)
            if (Mathf.Abs(list[i].Distance - distance) < MinSpawnGap) return false;
        return true;
    }

    private void MoveWalkers(float dt)
    {
        for (int l = 0; l < _laneWalkers.Length; l++)
        {
            List<PedestrianWalker> list = _laneWalkers[l];
            int count = list.Count;
            if (count == 0) continue;
            SortByDistance(list);
            FootpathLane lane = _network.lanes[l];
            for (int i = 0; i < count; i++)
            {
                float gap;
                if (i + 1 < count) gap = list[i + 1].Distance - list[i].Distance;
                else if (lane.loop && count > 1) gap = list[0].Distance + lane.Length - list[i].Distance;
                else gap = float.MaxValue;
                list[i].GapAhead = gap;
            }
        }

        Vector3 playerPos = player != null ? player.position : Cam.transform.position;
        _offLaneCount = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            PedestrianWalker walker = _active[i];
            if (walker.State != PedestrianWalker.Mode.Lane)
            {
                _offLaneCount++;
                walker.TickOffLane(dt, playerPos, calmDistance);
                continue;
            }
            int previousLane = walker.Advance(dt);
            if (previousLane != walker.Lane)
            {
                _laneWalkers[previousLane].Remove(walker);
                _laneWalkers[walker.Lane].Add(walker);
            }
        }
    }

    private static void SortByDistance(List<PedestrianWalker> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            PedestrianWalker item = list[i];
            int j = i - 1;
            while (j >= 0 && list[j].Distance > item.Distance)
            {
                list[j + 1] = list[j];
                j--;
            }
            list[j + 1] = item;
        }
    }

    private void CheckActive(Camera cam)
    {
        if (_active.Count == 0) return;

        Vector3 center = Center;
        Vector3 camPos = cam.transform.position;
        float despawnSqr = (spawnRadius + 15f) * (spawnRadius + 15f);
        float showSqr = VisibleRadius * VisibleRadius;
        float hide = VisibleRadius + HideMargin;
        float hideSqr = hide * hide;

        int count = Mathf.Min(peopleCheckedPerFrame, _active.Count);
        for (int i = 0; i < count; i++)
        {
            if (_cursor >= _active.Count) _cursor = 0;
            PedestrianWalker walker = _active[_cursor];

            Vector3 pos = walker.transform.position;
            Vector3 fromCenter = pos - center;
            fromCenter.y = 0f;
            bool outsideDisc = fromCenter.sqrMagnitude > despawnSqr;
            float distSqr = (pos - camPos).sqrMagnitude;
            bool insideView = distSqr <= showSqr;
            // Surplus people (car count dropped) are only removed where the camera can't see them vanish.
            bool surplus = insideView
                ? _peopleInView > _targetInView + 3
                : _active.Count - _peopleInView > OutsideTarget + 5;
            bool removableUnseen = (walker.WantsRecycle || surplus) && IsHiddenFromCamera(cam, pos);

            if (outsideDisc || removableUnseen)
            {
                if (insideView) _peopleInView--;
                Despawn(walker);
                _active[_cursor] = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                continue;
            }

            bool visible = walker.IsVisible;
            if (visible && distSqr >= hideSqr) walker.SetVisible(false);
            else if (!visible && distSqr <= showSqr) walker.SetVisible(true);
            walker.SetCastShadows(distSqr <= shadowDistance * shadowDistance);
            _cursor++;
        }
    }

    private void Despawn(PedestrianWalker walker)
    {
        if (walker.State == PedestrianWalker.Mode.Lane)
        {
            _laneWalkers[walker.Lane].Remove(walker);
            walker.OnDespawned();
            walker.transform.SetParent(_poolRoot, true);
            _inactive.Enqueue(walker);
            return;
        }

        // A car's player sensor keeps braking for a collider that is deactivated in place, so move the
        // runner out of range first and only pool it once physics has seen it leave.
        walker.OnDespawned();
        walker.transform.position = ParkingSpot;
        _parked.Add(walker);
        _parkedAtFixedTime.Add(Time.fixedTime);
    }

    private static readonly Vector3 ParkingSpot = new Vector3(0f, -2000f, 0f);
    private readonly List<PedestrianWalker> _parked = new List<PedestrianWalker>();
    private readonly List<float> _parkedAtFixedTime = new List<float>();

    private void ReleaseParked()
    {
        for (int i = _parked.Count - 1; i >= 0; i--)
        {
            if (Time.fixedTime <= _parkedAtFixedTime[i] + Time.fixedDeltaTime) continue;
            _parked[i].transform.SetParent(_poolRoot, true);
            _inactive.Enqueue(_parked[i]);
            _parked.RemoveAt(i);
            _parkedAtFixedTime.RemoveAt(i);
        }
    }

    private int CountPeopleWithin(Vector3 point, float radius)
    {
        float radiusSqr = radius * radius;
        int count = 0;
        for (int i = 0; i < _active.Count; i++)
            if ((_active[i].transform.position - point).sqrMagnitude <= radiusSqr) count++;
        return count;
    }

    [Tooltip("Extra places nobody may be spawned near (e.g. a called taxi), using minSpawnDistanceFromCamera.")]
    public List<Transform> noSpawnAround = new List<Transform>();

    private bool IsHiddenFromCamera(Camera cam, Vector3 point)
    {
        Vector3 camPos = cam.transform.position;
        float dist = Vector3.Distance(point, camPos);
        if (dist < minSpawnDistanceFromCamera) return false;
        for (int i = 0; i < noSpawnAround.Count; i++)
            if (noSpawnAround[i] != null && (noSpawnAround[i].position - point).sqrMagnitude < minSpawnDistanceFromCamera * minSpawnDistanceFromCamera)
                return false;
        if (dist > VisibleRadius + HideMargin) return true;

        Vector3 vp = cam.WorldToViewportPoint(point + Vector3.up);
        bool onScreen = vp.z > 0f
            && vp.x > -viewportMargin && vp.x < 1f + viewportMargin
            && vp.y > -viewportMargin && vp.y < 1f + viewportMargin;
        return !onScreen;
    }
}
