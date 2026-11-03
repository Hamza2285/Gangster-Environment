using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the walking lanes for the Population system from the iTS road network. Traffic keeps left,
// so the curb lane of a road plus its left-turn connector traces the edge of a block; moving that
// line sideways puts it on the footpath. Two lines per footpath, one in each direction.
public static class FootpathBaker
{
    const float NearOffset = 4.6f;    // metres left of the curb lane centre
    const float FarOffset = 6.4f;
    const float MinOffset = 3.7f;     // the footpath is roughly 3.5 to 7.5 m from the curb lane centre
    const float MaxOffset = 7.3f;
    const float PropClearance = 0.6f;
    const float MaxStep = 1.2f;       // height difference from the road that still counts as this footpath
    const int EaseSamples = 4;
    const string PeopleFolder = "Assets/PopulationSystem/Prefabs/People Prefabs";
    const string RootName = "Pedestrians";

    static readonly string[] PropRoots = { "Roads Props", "Street tree ", "Plam Tree", "Road Planter" };

    struct Prop
    {
        public float x, z, halfX, halfZ;
    }

    static Dictionary<Vector2Int, List<Prop>> props;

    [MenuItem("Game/Bake Footpaths And Add People")]
    public static string BakeAndAddPeople()
    {
        var scene = SceneManager.GetActiveScene();
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        if (manager == null) return "No iTSManager in the scene";

        var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        if (existing != null) Object.DestroyImmediate(existing);

        // The player's capsule must not count as ground or as an obstacle while sampling.
        var controller = Object.FindFirstObjectByType<CharacterController>();
        if (controller != null) controller.enabled = false;
        Physics.SyncTransforms();
        CollectProps(scene);

        var chains = BuildCurbChains(manager);
        var lanes = new List<FootpathLane>();
        int rejected = 0;
        foreach (var chain in chains)
        {
            AddLanes(chain.points, chain.closed, NearOffset, false, lanes, ref rejected);
            AddLanes(chain.points, chain.closed, FarOffset, true, lanes, ref rejected);
        }
        int linked = 0, open = 0;
        LinkOpenEnds(lanes, ref linked, ref open);
        if (controller != null) controller.enabled = true;

        var go = new GameObject(RootName);
        var network = go.AddComponent<FootpathLaneNetwork>();
        network.lanes = lanes;
        var spawner = go.AddComponent<PedestrianSpawner>();
        spawner.peoplePrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PeopleFolder })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
        // The car spawner rides on the player, so this disc follows the player too.
        spawner.carSpawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
        spawner.matchCarDensity = false;
        spawner.amount = 50;
        spawner.spawnRadius = 160f;   // same as the spawner's view distance, so nobody spawns outside the disc
        spawner.minSpawnDistanceFromCamera = 40f;
        spawner.panicWhenPlayerNear = false;
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        float total = lanes.Sum(l => l.Length);
        return "Footpaths: " + chains.Count + " curb chains, " + lanes.Count + " lanes (" + lanes.Count(l => l.loop) + " loops), "
            + total.ToString("F0") + " m, rejected samples " + rejected + ", ends linked " + linked + ", open " + open
            + ". People prefabs " + spawner.peoplePrefabs.Length + ". Props avoided in " + props.Count + " cells.";
    }

    sealed class Chain
    {
        public List<Vector3> points = new List<Vector3>();
        public bool closed;
    }

    static List<Chain> BuildCurbChains(TSMainManager manager)
    {
        var lanes = manager.lanes;
        int n = lanes.Length;
        var curb = new bool[n];
        for (int i = 0; i < n; i++) curb[i] = lanes[i].totalDistance >= 10f && IsCurbLane(lanes, i);

        // Follow the curb: a left turn hugs the block corner; with no road on the left, carry straight on.
        var next = new TSLaneConnector[n];
        var hasIncoming = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (!curb[i]) continue;
            Vector3 endDir = EndDirection(lanes[i].points);
            float best = -0.3f;
            foreach (var connector in lanes[i].connectors)
            {
                int to = connector.nextLane;
                if (to < 0 || to >= n || !curb[to]) continue;
                Vector3 startDir = StartDirection(lanes[to].points);
                float cross = endDir.x * startDir.z - endDir.z * startDir.x;
                if (cross > best)
                {
                    best = cross;
                    next[i] = connector;
                }
            }
            if (next[i] != null) hasIncoming[next[i].nextLane] = true;
        }

        var chains = new List<Chain>();
        var visited = new bool[n];
        // Open chains first so each is walked from its true start; whatever is left is a closed loop.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int start = 0; start < n; start++)
            {
                if (!curb[start] || visited[start] || (pass == 0 && hasIncoming[start])) continue;
                var chain = new Chain();
                int lane = start;
                while (true)
                {
                    visited[lane] = true;
                    Append(chain.points, lanes[lane].points);
                    var link = next[lane];
                    if (link == null) break;
                    if (link.nextLane == start)
                    {
                        Append(chain.points, link.points);
                        chain.closed = true;
                        break;
                    }
                    if (visited[link.nextLane]) break;
                    Append(chain.points, link.points);
                    lane = link.nextLane;
                }
                if (chain.points.Count >= 2) chains.Add(chain);
            }
        }
        return chains;
    }

    // Curb lane: nothing else travelling the same way further to its left.
    static bool IsCurbLane(TSLaneInfo[] lanes, int index)
    {
        var points = lanes[index].points;
        Vector3 mid = points[points.Length / 2].point;
        Vector3 dir = points[Mathf.Min(points.Length / 2 + 1, points.Length - 1)].point - points[Mathf.Max(points.Length / 2 - 1, 0)].point;
        dir.y = 0f;
        dir.Normalize();
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        for (int other = 0; other < lanes.Length; other++)
        {
            if (other == index || lanes[other].totalDistance < 10f) continue;
            var pts = lanes[other].points;
            for (int i = 2; i < pts.Length - 2; i += 3)
            {
                Vector3 rel = pts[i].point - mid;
                if (Mathf.Abs(rel.y) > 3f || Mathf.Abs(Vector3.Dot(rel, dir)) > 8f) continue;
                float side = Vector3.Dot(rel, left);
                if (side < 1.5f || side > 9f) continue;
                Vector3 otherDir = pts[i + 1].point - pts[i - 1].point;
                otherDir.y = 0f;
                if (Vector3.Dot(otherDir.normalized, dir) > 0.7f) return false;
            }
        }
        return true;
    }

    static Vector3 EndDirection(TSPoints[] p)
    {
        Vector3 d = p[p.Length - 1].point - p[Mathf.Max(0, p.Length - 4)].point;
        d.y = 0f;
        return d.normalized;
    }

    static Vector3 StartDirection(TSPoints[] p)
    {
        Vector3 d = p[Mathf.Min(p.Length - 1, 3)].point - p[0].point;
        d.y = 0f;
        return d.normalized;
    }

    static void Append(List<Vector3> target, TSPoints[] source)
    {
        foreach (var p in source)
        {
            if (target.Count == 0 || (target[target.Count - 1] - p.point).sqrMagnitude > 0.04f) target.Add(p.point);
        }
    }

    static void CollectProps(Scene scene)
    {
        props = new Dictionary<Vector2Int, List<Prop>>();
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "env");
        foreach (var rootName in PropRoots)
        {
            Transform root = env != null ? env.transform.Find(rootName) : null;
            if (root == null) continue;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = renderer.bounds;
                if (b.size.x > 5f || b.size.z > 5f || b.size.y < 0.5f) continue;
                var prop = new Prop { x = b.center.x, z = b.center.z, halfX = b.extents.x, halfZ = b.extents.z };
                var cell = new Vector2Int(Mathf.FloorToInt(prop.x / 6f), Mathf.FloorToInt(prop.z / 6f));
                if (!props.TryGetValue(cell, out var list)) props[cell] = list = new List<Prop>();
                list.Add(prop);
            }
        }
    }

    static bool ClearOfProps(Vector3 p)
    {
        var cell = new Vector2Int(Mathf.FloorToInt(p.x / 6f), Mathf.FloorToInt(p.z / 6f));
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if (!props.TryGetValue(cell + new Vector2Int(x, z), out var list)) continue;
                foreach (var prop in list)
                {
                    if (Mathf.Abs(p.x - prop.x) < prop.halfX + PropClearance && Mathf.Abs(p.z - prop.z) < prop.halfZ + PropClearance) return false;
                }
            }
        }
        return true;
    }

    // Ground at the same level as the road, ignoring anything well above or below (bridges, underpasses).
    static bool FindGround(Vector3 p, float roadY, out Vector3 ground)
    {
        ground = p;
        if (!Physics.Raycast(new Vector3(p.x, roadY + MaxStep, p.z), Vector3.down, out RaycastHit hit, MaxStep * 2f, ~0, QueryTriggerInteraction.Ignore)) return false;
        ground = hit.point;
        return true;
    }

    static bool RoomToStand(Vector3 ground)
    {
        return !Physics.CheckCapsule(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
    }

    static void AddLanes(List<Vector3> curbLine, bool closed, float baseOffset, bool reverse, List<FootpathLane> lanes, ref int rejected)
    {
        var line = Resample(curbLine, 1f);
        int count = line.Count;
        if (count < 10) return;

        var left = new Vector3[count];
        var offsets = new float[count];
        var valid = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = closed
                ? line[(i + 2) % count] - line[(i - 2 + count) % count]
                : line[Mathf.Min(i + 2, count - 1)] - line[Mathf.Max(i - 2, 0)];
            dir.y = 0f;
            dir.Normalize();
            left[i] = new Vector3(-dir.z, 0f, dir.x);
            valid[i] = ChooseOffset(line[i], left[i], baseOffset, out offsets[i]);
            if (!valid[i]) rejected++;
        }

        // Spread each sideways dodge over its neighbours so people curve around things.
        var eased = new float[count];
        for (int i = 0; i < count; i++)
        {
            float shift = 0f;
            for (int k = -EaseSamples; k <= EaseSamples; k++)
            {
                int j = closed ? ((i + k) % count + count) % count : i + k;
                if (j < 0 || j >= count || !valid[j]) continue;
                float candidate = (offsets[j] - baseOffset) * (1f - Mathf.Abs(k) / (EaseSamples + 1f));
                if (Mathf.Abs(candidate) > Mathf.Abs(shift)) shift = candidate;
            }
            eased[i] = baseOffset + shift;
        }

        var runs = new List<List<Vector3>>();
        List<Vector3> run = null;
        float lastY = line[0].y;
        for (int i = 0; i < count; i++)
        {
            if (!valid[i])
            {
                run = null;
                continue;
            }
            Vector3 spot = line[i] + left[i] * eased[i];
            if (FindGround(spot, line[i].y, out Vector3 ground)) lastY = ground.y;
            else ground = new Vector3(spot.x, lastY, spot.z);
            if (run == null)
            {
                run = new List<Vector3>();
                runs.Add(run);
            }
            run.Add(ground + Vector3.up * 0.02f);
        }

        bool whole = closed && runs.Count == 1 && runs[0].Count == count;
        foreach (var r in runs)
        {
            if (r.Count < 10) continue;
            var points = new List<Vector3>();
            for (int i = 0; i < r.Count; i += 2) points.Add(r[i]);
            if (r.Count % 2 == 0) points.Add(r[r.Count - 1]);
            if (whole) points.Add(points[0]);
            if (reverse) points.Reverse();
            lanes.Add(MakeLane(points, whole));
        }
    }

    static bool ChooseOffset(Vector3 road, Vector3 left, float baseOffset, out float offset)
    {
        float[] tries = { 0f, 0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f, 2.2f, -2.2f, 3f, -3f };
        offset = baseOffset;
        bool standable = false;
        foreach (float t in tries)
        {
            float candidate = baseOffset + t;
            if (candidate < MinOffset || candidate > MaxOffset) continue;
            Vector3 spot = road + left * candidate;
            if (!FindGround(spot, road.y, out Vector3 ground) || !RoomToStand(ground)) continue;
            if (ClearOfProps(ground))
            {
                offset = candidate;
                return true;
            }
            if (!standable)
            {
                standable = true;
                offset = candidate;
            }
        }
        return standable;
    }

    static List<Vector3> Resample(List<Vector3> points, float spacing)
    {
        var result = new List<Vector3> { points[0] };
        float carried = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 a = points[i - 1], b = points[i];
            float length = Vector3.Distance(a, b);
            float at = spacing - carried;
            while (at <= length)
            {
                result.Add(Vector3.Lerp(a, b, at / length));
                at += spacing;
            }
            carried = length - (at - spacing);
        }
        return result;
    }

    static FootpathLane MakeLane(List<Vector3> points, bool loop)
    {
        var lane = new FootpathLane { points = points.ToArray(), loop = loop, nextLane = -1, distances = new float[points.Count] };
        for (int i = 1; i < points.Count; i++) lane.distances[i] = lane.distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return lane;
    }

    // Someone reaching an open end steps onto the nearest other lane instead of stopping there.
    static void LinkOpenEnds(List<FootpathLane> lanes, ref int linked, ref int open)
    {
        for (int l = 0; l < lanes.Count; l++)
        {
            if (lanes[l].loop) continue;
            Vector3 end = lanes[l].points[lanes[l].points.Length - 1];
            float best = 7f * 7f;
            int bestLane = -1;
            float bestDistance = 0f;
            for (int other = 0; other < lanes.Count; other++)
            {
                if (other == l) continue;
                var pts = lanes[other].points;
                for (int i = 0; i < pts.Length - 3; i++)
                {
                    float d = (pts[i] - end).sqrMagnitude;
                    if (d < best && Mathf.Abs(pts[i].y - end.y) < 1.5f)
                    {
                        best = d;
                        bestLane = other;
                        bestDistance = lanes[other].distances[i];
                    }
                }
            }
            if (bestLane >= 0)
            {
                lanes[l].nextLane = bestLane;
                lanes[l].nextDistance = bestDistance;
                linked++;
            }
            else
            {
                open++;
            }
        }
    }
}
