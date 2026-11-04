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
    // Corner rounding works on samples this far apart, so the windows below are in samples.
    const float RoundSpacing = 0.25f;
    const int RoundWindow = 5;
    const int RoundPasses = 3;
    const int TightenPasses = 12;
    const int TightenWidth = 6;
    const float MaxTurnPerStep = 9f;   // at this spacing, a bend no tighter than about 1.6 m in radius
    const int FoldSearch = 80;
    const int KeepSpread = 16;
    const float SimplifyTurn = 5f;
    const float MaxStandStep = 0.8f;
    const int PieceTrim = 6;
    const int ClearRounds = 4;
    const int ClearCycles = 5;
    const int ShiftHold = 5;
    const float SimplifyMinLength = 0.2f;
    const int RetraceMinSpan = 8;
    const float RetraceRatio = 0.3f;
    const float SideStep = 0.2f;
    const float MaxSideShift = 2.4f;
    const int MinPieceSamples = 16;
    const float SimplifyLength = 3f;
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
            foreach (var points in RoundCorners(r, whole, out bool stillLoop))
            {
                if (points.Count < 3) continue;
                if (reverse) points.Reverse();
                lanes.Add(MakeLane(points, stillLoop));
            }
        }
    }

    // Offsetting a line toward the inside of a bend makes it fold over itself, which walkers saw as sudden
    // U-turns. This cuts those folds out, rounds what remains, and keeps every point where a person fits.
    // Anywhere a person truly can't fit, the lane is split, so there can be more than one piece.
    static List<List<Vector3>> RoundCorners(List<Vector3> raw, bool closed, out bool stillLoop)
    {
        stillLoop = closed;
        var source = new List<Vector3>(raw);
        if (closed) source.Add(raw[0]);
        var line = Resample(source, RoundSpacing);
        RemoveFolds(line);
        line = Resample(line, RoundSpacing);
        RemoveRetraces(line);
        line = Resample(line, RoundSpacing);
        if (closed && Horizontal(line[line.Count - 1] - line[0]) < RoundSpacing) line.RemoveAt(line.Count - 1);
        int count = line.Count;
        if (count < 8) return new List<List<Vector3>> { line };

        var round = new List<Vector3>(line);
        for (int pass = 0; pass < RoundPasses; pass++) Average(round, closed, RoundWindow);
        for (int pass = 0; pass < TightenPasses && AverageTightBends(round, closed); pass++) { }

        // Rounding can drift into a wall on the outside of a corner: ease back toward the safe line there.
        var keep = new float[count];
        for (int i = 0; i < count; i++)
        {
            keep[i] = 1f;
            for (float t = 1f; t >= 0f; t -= 0.25f)
            {
                if (StandsAt(Vector3.Lerp(line[i], round[i], t), line[i].y))
                {
                    keep[i] = t;
                    break;
                }
                keep[i] = 0f;
            }
        }
        var eased = new float[count];
        for (int i = 0; i < count; i++)
        {
            float low = 1f;
            for (int k = -KeepSpread; k <= KeepSpread; k++)
            {
                int j = closed ? ((i + k) % count + count) % count : Mathf.Clamp(i + k, 0, count - 1);
                low = Mathf.Min(low, keep[j] + Mathf.Abs(k) / (float)KeepSpread);
            }
            eased[i] = Mathf.Clamp01(low);
        }

        var result = new List<Vector3>(count);
        float y = line[0].y;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Vector3.Lerp(line[i], round[i], eased[i]);
            if (FindGround(p, line[i].y, out Vector3 ground)) y = ground.y + 0.02f;
            result.Add(new Vector3(p.x, y, p.z));
        }
        return ClearAndSplit(result, closed, ref stillLoop);
    }

    // Steps each blocked point sideways to the nearest free spot, easing its neighbours along with it,
    // and splits the lane at anything that still can't be stood on.
    static List<List<Vector3>> ClearAndSplit(List<Vector3> line, bool closed, ref bool stillLoop)
    {
        int count = line.Count;
        var blocked = new bool[count];
        var moved = new List<Vector3>(line);

        // Smoothing a side step can push the line back into the obstacle, so the two take turns until they agree.
        bool anyBlocked = false;
        for (int cycle = 0; cycle < ClearCycles; cycle++)
        {
            StepAroundObstacles(moved, closed, blocked);
            for (int pass = 0; pass < TightenPasses && AverageTightBends(moved, closed); pass++) { }
            anyBlocked = false;
            for (int i = 0; i < count; i++)
            {
                blocked[i] = !StandsAt(moved[i], moved[i].y);
                anyBlocked |= blocked[i];
            }
            if (!anyBlocked) break;
        }

        var pieces = new List<List<Vector3>>();
        if (!anyBlocked)
        {
            pieces.Add(Simplify(moved, closed));
            return pieces;
        }

        // A loop broken here is walked from just past the break, all the way round to just before it.
        stillLoop = false;
        int start = closed ? (System.Array.IndexOf(blocked, true) + 1) % count : 0;
        var free = new List<Vector3>();
        for (int n = 0; n < count; n++)
        {
            int i = closed ? (start + n) % count : n;
            if (!blocked[i]) free.Add(moved[i]);
            if ((blocked[i] || n == count - 1) && free.Count > 0)
            {
                FinishPiece(free, pieces);
                free = new List<Vector3>();
            }
        }
        return pieces;
    }

    static void StepAroundObstacles(List<Vector3> moved, bool closed, bool[] blocked)
    {
        int count = moved.Count;
        for (int round = 0; round < ClearRounds; round++)
        {
            var shift = new Vector3[count];
            bool anyBlocked = false;
            for (int i = 0; i < count; i++)
            {
                blocked[i] = !StandsAt(moved[i], moved[i].y);
                if (!blocked[i]) continue;
                anyBlocked = true;
                Vector3 along = moved[Mathf.Min(i + 1, count - 1)] - moved[Mathf.Max(i - 1, 0)];
                along.y = 0f;
                Vector3 side = new Vector3(-along.z, 0f, along.x).normalized;
                for (float step = SideStep; step <= MaxSideShift; step += SideStep)
                {
                    if (StandsAt(moved[i] + side * step, moved[i].y)) { shift[i] = side * step; break; }
                    if (StandsAt(moved[i] - side * step, moved[i].y)) { shift[i] = -side * step; break; }
                }
            }
            if (!anyBlocked) break;

            // Held at full strength right beside the obstacle, then eased back onto the line.
            var before = moved.ToArray();
            for (int i = 0; i < count; i++)
            {
                Vector3 strongest = Vector3.zero;
                for (int k = -KeepSpread; k <= KeepSpread; k++)
                {
                    int j = closed ? ((i + k) % count + count) % count : i + k;
                    if (j < 0 || j >= count) continue;
                    float weight = Mathf.Clamp01((KeepSpread + 1f - Mathf.Abs(k)) / (KeepSpread + 1f - ShiftHold));
                    Vector3 candidate = shift[j] * weight;
                    if (candidate.sqrMagnitude > strongest.sqrMagnitude) strongest = candidate;
                }
                moved[i] = before[i] + strongest;
            }
            Average(moved, closed, 1);
        }
    }

    // The ends of a piece were torn off beside an obstacle, so they are trimmed and smoothed, then every point is
    // proven once more: nothing in a lane is left unchecked after its last move.
    static void FinishPiece(List<Vector3> piece, List<List<Vector3>> pieces)
    {
        if (piece.Count < MinPieceSamples + PieceTrim * 2) return;
        piece.RemoveRange(piece.Count - PieceTrim, PieceTrim);
        piece.RemoveRange(0, PieceTrim);
        for (int pass = 0; pass < TightenPasses && AverageTightBends(piece, false); pass++) { }

        var free = new List<Vector3>();
        for (int i = 0; i < piece.Count; i++)
        {
            bool ok = StandsAt(piece[i], piece[i].y);
            if (ok) free.Add(piece[i]);
            if (!ok || i == piece.Count - 1)
            {
                if (free.Count >= MinPieceSamples) pieces.Add(Simplify(free, false));
                free = new List<Vector3>();
            }
        }
    }

    static float Horizontal(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    // Ground well above walking height is the top of a railing, not somewhere to walk. No ground at all is only a
    // crack between two road meshes, which a person simply steps over.
    static bool StandsAt(Vector3 p, float walkY)
    {
        if (!FindGround(p, walkY, out Vector3 ground)) ground = new Vector3(p.x, walkY, p.z);
        return ground.y - walkY < MaxStandStep && RoomToStand(ground);
    }

    // A line that runs out and comes back along nearly the same path doesn't always cross itself: this spots it
    // by the two ends of a stretch being far closer together than the distance walked between them.
    static void RemoveRetraces(List<Vector3> line)
    {
        for (int i = 0; i < line.Count - RetraceMinSpan; i++)
        {
            int longest = Mathf.Min(FoldSearch, line.Count - 1 - i);
            for (int span = longest; span >= RetraceMinSpan; span--)
            {
                if (Horizontal(line[i + span] - line[i]) >= span * RoundSpacing * RetraceRatio) continue;
                line.RemoveRange(i + 1, span - 1);
                break;
            }
        }
    }

    // Wherever the line crosses itself a short way on, the part between the two crossings is a fold.
    static void RemoveFolds(List<Vector3> line)
    {
        for (int i = 0; i < line.Count - 3; i++)
        {
            int last = Mathf.Min(line.Count - 2, i + FoldSearch);
            for (int j = last; j >= i + 2; j--)
            {
                if (!Crossing(line[i], line[i + 1], line[j], line[j + 1], out Vector3 at)) continue;
                line.RemoveRange(i + 1, j - i);
                line.Insert(i + 1, at);
                break;
            }
        }
    }

    static bool Crossing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 at)
    {
        at = a;
        float abx = b.x - a.x, abz = b.z - a.z, cdx = d.x - c.x, cdz = d.z - c.z;
        float denominator = abx * cdz - abz * cdx;
        if (Mathf.Abs(denominator) < 1e-6f) return false;
        float t = ((c.x - a.x) * cdz - (c.z - a.z) * cdx) / denominator;
        float u = ((c.x - a.x) * abz - (c.z - a.z) * abx) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        at = Vector3.Lerp(a, b, t);
        return true;
    }

    // Moving average across the line, with the window shrinking toward an open line's ends so they stay put.
    static void Average(List<Vector3> line, bool closed, int window)
    {
        int count = line.Count;
        var before = line.ToArray();
        for (int i = 0; i < count; i++)
        {
            int w = closed ? window : Mathf.Min(window, Mathf.Min(i, count - 1 - i));
            float x = 0f, z = 0f;
            for (int k = -w; k <= w; k++)
            {
                Vector3 p = before[closed ? ((i + k) % count + count) % count : i + k];
                x += p.x;
                z += p.z;
            }
            float n = 2 * w + 1;
            line[i] = new Vector3(x / n, before[i].y, z / n);
        }
    }

    static float TurnAt(IList<Vector3> line, int i, bool closed)
    {
        int count = line.Count;
        if (!closed && (i == 0 || i == count - 1)) return 0f;
        Vector3 a = line[i] - line[(i - 1 + count) % count];
        Vector3 b = line[(i + 1) % count] - line[i];
        a.y = 0f;
        b.y = 0f;
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.Angle(a, b);
    }

    static bool AverageTightBends(List<Vector3> line, bool closed)
    {
        int count = line.Count;
        var tight = new bool[count];
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            if (TurnAt(line, i, closed) <= MaxTurnPerStep) continue;
            any = true;
            for (int k = -TightenWidth; k <= TightenWidth; k++)
            {
                int j = closed ? ((i + k) % count + count) % count : i + k;
                if (j >= 0 && j < count) tight[j] = true;
            }
        }
        if (!any) return false;
        var smooth = new List<Vector3>(line);
        Average(smooth, closed, 2);
        for (int i = 0; i < count; i++) if (tight[i]) line[i] = smooth[i];
        return true;
    }

    // A straight only needs a point every few metres; a bend keeps enough to stay round.
    static List<Vector3> Simplify(List<Vector3> line, bool closed)
    {
        var kept = new List<Vector3> { line[0] };
        float turned = 0f;
        Vector3 last = line[0];
        for (int i = 1; i < line.Count - 1; i++)
        {
            turned += TurnAt(line, i, false);
            float fromLast = Vector3.Distance(line[i], last);
            if (fromLast < SimplifyMinLength || (turned < SimplifyTurn && fromLast < SimplifyLength)) continue;
            kept.Add(line[i]);
            last = line[i];
            turned = 0f;
        }
        kept.Add(line[line.Count - 1]);
        if (closed) kept.Add(line[0]);
        return kept;
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

    static bool ClearToWalk(Vector3 from, Vector3 to)
    {
        Vector3 up = Vector3.up * 1f;
        Vector3 d = to - from;
        float length = d.magnitude;
        return length < 0.01f || !Physics.SphereCast(from + up, 0.25f, d / length, out RaycastHit hit, length, ~0, QueryTriggerInteraction.Ignore);
    }

    // Someone reaching an open end steps onto the nearest other lane instead of stopping there.
    static void LinkOpenEnds(List<FootpathLane> lanes, ref int linked, ref int open)
    {
        for (int l = 0; l < lanes.Count; l++)
        {
            if (lanes[l].loop) continue;
            Vector3 end = lanes[l].points[lanes[l].points.Length - 1];
            Vector3 endDir = end - lanes[l].points[lanes[l].points.Length - 2];
            float best = 7f * 7f;
            int bestLane = -1;
            float bestDistance = 0f;
            // Prefer carrying on the same way; at a real dead end the only way on is to turn around.
            for (int pass = 0; pass < 2 && bestLane < 0; pass++)
            {
                for (int other = 0; other < lanes.Count; other++)
                {
                    if (other == l) continue;
                    var pts = lanes[other].points;
                    for (int i = 0; i < pts.Length - 3; i++)
                    {
                        float d = (pts[i] - end).sqrMagnitude;
                        bool sameWay = Vector3.Dot(pts[i + 1] - pts[i], endDir) > 0f;
                        if (d < best && Mathf.Abs(pts[i].y - end.y) < 1.5f && (pass == 1 || sameWay) && ClearToWalk(end, pts[i]))
                        {
                            best = d;
                            bestLane = other;
                            bestDistance = lanes[other].distances[i];
                        }
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
