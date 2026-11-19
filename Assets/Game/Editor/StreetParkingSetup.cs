using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Parks drivable cars along the kerbs of the city (Tools > Game > Street Parking). Re-runnable: the cars it placed last time
// ("Street Parked Cars") are removed first, and the same seed gives the same layout.
// A spot is used when it is like the hand-placed parked cars: Kerb Offset to the side of a traffic lane, facing along it, on flat
// road at the lane's height (not on a pavement), away from other lanes, from junctions, from other parked cars and from anything
// solid. The cars go to sleep far from the player (TrafficTakeoverManager's Parked Scene Vehicles).
public static class StreetParkingSetup
{
    const string RootName = "Street Parked Cars";
    const string PrefabFolder = "Assets/_Project/Prefabs/Vehicles/Drivable/";
    static readonly string[] Models =
    {
        "Drivable_Car1", "Drivable_Car2", "Drivable_Car3", "Drivable_Car4", "Drivable_Car5", "Drivable_GWagon",
        "Drivable_Khodge2", "Drivable_LandSover", "Drivable_NewCar1", "Drivable_NewCar3", "Drivable_Talenter", "Drivable_WMB",
    };

    // Kerb side of the outer lane, like the hand-placed parked cars: tight against the kerb on these streets. (The kerb itself
    // cannot be found by probing: pavements are nearly flush with the road and share its texture atlas.)
    const float KerbOffset = 4.9f;
    const float PivotHeight = 0.05f;
    const float Step = 35f;                 // metres between candidate spots along a lane
    const float JunctionClearance = 14f;    // no parking this close to either end of a lane
    const float MinOtherLaneDistance = 4f;  // the spot must not be on another lane
    const float MinSpacing = 28f;           // between parked cars
    const int MaxCars = 28;
    const int Seed = 4711;
    const string RoadMaterial = "Road Atlas";   // the carriageway (pavements are "Pathways")
    const float MaxAboveStreet = 2f;            // no parking on raised roads and bridges

    [MenuItem("Tools/Game/Street Parking")]
    public static void MenuSetUp() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var network = RoadNetwork.Instance;
        if (network == null || network.lanes.Count == 0) return "No road network in the open scene.";
        var anchor = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
        var scene = anchor != null ? anchor.gameObject.scene : UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        // Start again from scratch.
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        SceneManager_Move(root, scene);

        var prefabs = new List<GameObject>();
        foreach (var model in Models)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + model + ".prefab");
            if (prefab != null) prefabs.Add(prefab);
        }
        if (prefabs.Count == 0) return "No drivable car prefabs found in " + PrefabFolder;

        // Everything already parked counts for spacing.
        var taken = new List<Vector3>();
        foreach (var adapter in Object.FindObjectsByType<RCCVehicleAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None)) taken.Add(adapter.transform.position);

        // Street level: the most common lane height (raised roads and bridges are above it).
        var heights = new List<float>();
        foreach (var lane in network.lanes) if (lane.points != null && lane.points.Length > 0) heights.Add(lane.points[0].y);
        heights.Sort();
        float street = heights[heights.Count / 2];

        // Candidate spots along every lane, in a shuffled (seeded) order so the cars spread over the city.
        var candidates = new List<(Vector3 point, Vector3 direction)>();
        foreach (var lane in network.lanes)
        {
            if (lane.points == null || lane.points.Length < 2 || lane.length < JunctionClearance * 2f + 6f) continue;
            if (lane.points[0].y > street + MaxAboveStreet || lane.points[lane.points.Length - 1].y > street + MaxAboveStreet) continue;
            for (float along = JunctionClearance; along <= lane.length - JunctionClearance; along += Step)
            {
                if (TrySample(lane, along, out Vector3 point, out Vector3 direction) && point.y <= street + MaxAboveStreet) candidates.Add((point, direction));
            }
        }
        var random = new System.Random(Seed);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        Physics.SyncTransforms();
        var placed = new List<RCCVehicleAdapter>();
        int rejected = 0;
        foreach (var (lanePoint, direction) in candidates)
        {
            if (placed.Count >= MaxCars) break;
            Vector3 side = new Vector3(-direction.z, 0f, direction.x);
            Vector3 spot = lanePoint + side * KerbOffset;
            if (!IsGoodSpot(network, lanePoint, spot, direction, taken, out Vector3 ground)) { rejected++; continue; }

            var prefab = prefabs[random.Next(prefabs.Count)];
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            car.transform.SetPositionAndRotation(ground + Vector3.up * PivotHeight, Quaternion.LookRotation(direction, Vector3.up));
            car.name = prefab.name + " (Street " + (placed.Count + 1) + ")";
            taken.Add(spot);
            var adapter = car.GetComponent<RCCVehicleAdapter>();
            if (adapter != null) placed.Add(adapter);
            Physics.SyncTransforms();
        }

        // Far from the player they sleep (no RCC, no wheels, kinematic) like the hand-placed ones.
        if (anchor != null)
        {
            var so = new SerializedObject(anchor);
            var list = so.FindProperty("parkedSceneVehicles");
            var keep = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (item != null) keep.Add(item);
            }
            foreach (var adapter in placed) keep.Add(adapter);
            list.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            so.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return $"Street parking: {placed.Count} cars placed ({candidates.Count} candidate spots, {rejected} rejected).";
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    // Point and heading a given distance along a lane.
    static bool TrySample(RoadNetwork.Lane lane, float along, out Vector3 point, out Vector3 direction)
    {
        point = direction = Vector3.zero;
        for (int i = 1; i < lane.points.Length; i++)
        {
            if (lane.distance[i] < along) continue;
            float segment = lane.distance[i] - lane.distance[i - 1];
            float t = segment > 0.001f ? (along - lane.distance[i - 1]) / segment : 0f;
            point = Vector3.Lerp(lane.points[i - 1], lane.points[i], t);
            direction = lane.points[i] - lane.points[i - 1];
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return false;
            direction.Normalize();
            // Straight road only: the lane must keep its heading for a car length either way.
            Vector3 before = lane.points[Mathf.Max(0, i - 2)], after = lane.points[Mathf.Min(lane.points.Length - 1, i + 1)];
            Vector3 overall = after - before;
            overall.y = 0f;
            return overall.sqrMagnitude > 0.01f && Vector3.Dot(overall.normalized, direction) > 0.985f;
        }
        return false;
    }

    static bool IsGoodSpot(RoadNetwork network, Vector3 lanePoint, Vector3 spot, Vector3 direction, List<Vector3> taken, out Vector3 ground)
    {
        ground = spot;
        foreach (var other in taken)
            if ((other - spot).sqrMagnitude < MinSpacing * MinSpacing) return false;

        // Not on (or next to) another traffic lane, nor on a lane connector (junction turns, bus-stop pull-ins).
        if (network.TryFindNearest(spot, out RoadNetwork.RoadPoint nearest) && nearest.sqrDistance < MinOtherLaneDistance * MinOtherLaneDistance) return false;
        float linkSqr = MinOtherLaneDistance * MinOtherLaneDistance;
        foreach (var lane in network.lanes)
            foreach (var link in lane.outgoing)
            {
                if (link.points == null) continue;
                foreach (var point in link.points)
                {
                    Vector3 d = point - spot;
                    if (d.x * d.x + d.z * d.z < linkSqr && Mathf.Abs(d.y) < 3f) return false;
                }
            }

        // Road surface at the lane's own height under the whole car (a pavement or kerb is higher, a hole is lower).
        if (!GroundAt(lanePoint, out RaycastHit laneHit)) return false;
        Vector3 laneGround = laneHit.point;
        Vector3 side = new Vector3(-direction.z, 0f, direction.x);
        foreach (var corner in new[] { Vector3.zero, direction * 2.4f, -direction * 2.4f, side * 1.0f, -side * 1.0f,
                                       direction * 2.4f + side, direction * 2.4f - side, -direction * 2.4f + side, -direction * 2.4f - side })
        {
            if (!GroundAt(spot + corner, out RaycastHit hit)) return false;
            if (Mathf.Abs(hit.point.y - laneGround.y) > 0.12f || hit.normal.y < 0.97f) return false;
            // Road, not pavement (a pavement can be flush with the road, e.g. on bridges).
            if (!IsRoad(hit)) return false;
            if (corner == Vector3.zero) ground = hit.point;
        }

        // Not at a painted road marking such as a BUS STOP box (buses pull in there).
        foreach (var marking in Physics.OverlapSphere(ground, 12f, ~0, QueryTriggerInteraction.Collide))
            if (marking.name.StartsWith("Road Billboards") || marking.name.IndexOf("Bus", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

        // Nothing solid where the car goes or over it (poles, benches, walls, shelters, other cars), above the road surface.
        var box = Physics.OverlapBox(ground + Vector3.up * 2.05f, new Vector3(1.2f, 1.9f, 2.7f), Quaternion.LookRotation(direction), ~0, QueryTriggerInteraction.Ignore);
        return box.Length == 0;
    }

    // The visible surface under a point: the invisible traffic road colliders (no renderer) are skipped.
    static bool GroundAt(Vector3 point, out RaycastHit hit)
    {
        hit = default;
        var hits = Physics.RaycastAll(point + Vector3.up * 4f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponent<Renderer>() == null || h.collider.attachedRigidbody != null) continue;
            if (h.distance < best) { best = h.distance; hit = h; found = true; }
        }
        return found;
    }

    static bool IsRoad(RaycastHit hit)
    {
        string surface = SurfaceAt(hit);
        return surface.StartsWith(RoadMaterial) || surface.IndexOf("Marking", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Material of the hit triangle (one road mesh can carry both road and pavement materials).
    static string SurfaceAt(RaycastHit hit)
    {
        var renderer = hit.collider.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterials.Length == 0) return "";
        var meshCollider = hit.collider as MeshCollider;
        var mesh = meshCollider != null ? meshCollider.sharedMesh : null;
        if (mesh != null && hit.triangleIndex >= 0 && mesh.subMeshCount > 1)
        {
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                {
                    var material = renderer.sharedMaterials[Mathf.Min(s, renderer.sharedMaterials.Length - 1)];
                    return material != null ? material.name : "";
                }
            }
        }
        return renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
    }
}
