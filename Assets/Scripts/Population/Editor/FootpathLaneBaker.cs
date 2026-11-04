using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FootpathLaneBaker
{
    const string FootpathMaterial = "Road 04";
    const float EdgeInset = 1.0f;
    const float SampleSpacing = 1.0f;
    const float MinLaneLength = 10f;
    const float WeldTolerance = 0.05f;
    // Checked at 1m-spaced lane points; wide enough that the straight walk between two points also clears a thin pole.
    const float WalkerClearance = 0.5f;
    const float WalkerBodyRadius = 0.3f;
    const float MaxAvoidShift = 1.2f;
    const int AvoidEaseSamples = 4;

    [MenuItem("Tools/Population/Bake Footpath Lanes")]
    public static void BakeMenu()
    {
        var network = Object.FindObjectOfType<FootpathLaneNetwork>();
        if (network == null)
        {
            Debug.LogError("Add a FootpathLaneNetwork component to the scene first.");
            return;
        }
        Debug.Log(Bake(network));
    }

    struct Edge
    {
        public int a, b;
    }

    public static string Bake(FootpathLaneNetwork network)
    {
        var verts = new List<Vector3>();
        var grid = new Dictionary<Vector3Int, List<int>>();

        int Weld(Vector3 v)
        {
            var cell = new Vector3Int(Mathf.RoundToInt(v.x / WeldTolerance), Mathf.RoundToInt(v.y / WeldTolerance), Mathf.RoundToInt(v.z / WeldTolerance));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(cell + new Vector3Int(dx, dy, dz), out var list)) continue;
                foreach (int id in list)
                    if ((verts[id] - v).sqrMagnitude <= WeldTolerance * WeldTolerance) return id;
            }
            int newId = verts.Count;
            verts.Add(v);
            if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = new List<int>();
            bucket.Add(newId);
            return newId;
        }

        // Undirected edge -> use count, plus the directed edge and opposite vertex from its first triangle.
        var edgeUse = new Dictionary<long, int>();
        var edgeInfo = new Dictionary<long, (int a, int b, int c)>();
        int footpathMeshes = 0;

        foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Material[] mats = mr.sharedMaterials;
            Mesh mesh = mf.sharedMesh;
            Vector3[] local = mesh.vertices;
            Matrix4x4 m = mf.transform.localToWorldMatrix;

            for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
            {
                if (mats[s] == null || mats[s].name != FootpathMaterial) continue;
                footpathMeshes++;
                int[] tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 pa = m.MultiplyPoint3x4(local[tris[t]]);
                    Vector3 pb = m.MultiplyPoint3x4(local[tris[t + 1]]);
                    Vector3 pc = m.MultiplyPoint3x4(local[tris[t + 2]]);
                    Vector3 n = Vector3.Cross(pb - pa, pc - pa);
                    if (n.sqrMagnitude < 1e-6f || Mathf.Abs(n.normalized.y) < 0.7f) continue;

                    int a = Weld(pa), b = Weld(pb), c = Weld(pc);
                    if (a == b || b == c || c == a) continue;
                    AddEdge(edgeUse, edgeInfo, a, b, c);
                    AddEdge(edgeUse, edgeInfo, b, c, a);
                    AddEdge(edgeUse, edgeInfo, c, a, b);
                }
            }
        }

        // Boundary edges, oriented so the footpath surface lies on the left of the walking direction.
        var edges = new List<Edge>();
        foreach (var kv in edgeUse)
        {
            if (kv.Value != 1) continue;
            var info = edgeInfo[kv.Key];
            Vector3 dir = verts[info.b] - verts[info.a];
            dir.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            bool interiorOnRight = Vector3.Dot(verts[info.c] - verts[info.a], right) > 0f;
            edges.Add(interiorOnRight ? new Edge { a = info.b, b = info.a } : new Edge { a = info.a, b = info.b });
        }

        var outgoing = new Dictionary<int, List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            if (!outgoing.TryGetValue(edges[i].a, out var list)) outgoing[edges[i].a] = list = new List<int>();
            list.Add(i);
        }

        var used = new bool[edges.Count];
        var chains = new List<(List<Vector3> pts, bool closed)>();
        for (int e = 0; e < edges.Count; e++)
        {
            if (used[e]) continue;
            used[e] = true;
            int start = edges[e].a;
            var chain = new List<Vector3> { verts[start] };
            int cur = edges[e].b;
            Vector3 prevDir = Flat(verts[edges[e].b] - verts[edges[e].a]);
            bool closed = false;
            while (true)
            {
                if (cur == start) { closed = true; break; }
                chain.Add(verts[cur]);
                int best = -1;
                float bestTurn = float.MaxValue;
                if (outgoing.TryGetValue(cur, out var outs))
                {
                    foreach (int o in outs)
                    {
                        if (used[o]) continue;
                        // Sharpest left turn keeps us on the same footpath at pinch points.
                        float turn = Vector3.SignedAngle(prevDir, Flat(verts[edges[o].b] - verts[edges[o].a]), Vector3.up);
                        if (turn < bestTurn) { bestTurn = turn; best = o; }
                    }
                }
                if (best < 0) break;
                used[best] = true;
                prevDir = Flat(verts[edges[best].b] - verts[edges[best].a]);
                cur = edges[best].b;
            }
            if (chain.Count >= 2) chains.Add((chain, closed));
        }

        var overlap = new Collider[8];
        int nudged = 0, blocked = 0, rejected = 0;
        float droppedLength = 0f;
        var lanes = new List<FootpathLane>();

        foreach (var chain in chains)
        {
            List<Vector3> pts = Simplify(chain.pts, chain.closed);
            if (pts.Count < 2) continue;
            List<Vector3> offset = Offset(pts, chain.closed, EdgeInset);
            List<Vector3> samples = Resample(offset, chain.closed);

            int count = samples.Count;
            var lefts = new Vector3[count];
            var needed = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = chain.closed
                    ? samples[(i + 1) % count] - samples[(i - 1 + count) % count]
                    : samples[Mathf.Min(i + 1, count - 1)] - samples[Mathf.Max(i - 1, 0)];
                lefts[i] = Vector3.Cross(Flat(dir), Vector3.up).normalized;
                needed[i] = RequiredShift(samples[i], lefts[i], overlap);
            }

            // Samples around one obstacle must all dodge to the same side, or the path cuts straight through it.
            for (int i = 0; i < count;)
            {
                if (needed[i] == 0f) { i++; continue; }
                int start = i, lastNonZero = i;
                for (int j = i + 1; j < count && j - lastNonZero <= AvoidEaseSamples * 2; j++)
                    if (needed[j] != 0f) lastNonZero = j;

                float votes = 0f;
                for (int k = start; k <= lastNonZero; k++) votes += Mathf.Sign(needed[k]);
                float side = votes >= 0f ? 1f : -1f;
                if (!ForceSide(samples, lefts, needed, start, lastNonZero, side, overlap))
                    ForceSide(samples, lefts, needed, start, lastNonZero, -side, overlap);
                i = lastNonZero + 1;
            }

            // Spread each sideways dodge over neighbouring samples so walkers curve around obstacles.
            var eased = new float[count];
            for (int i = 0; i < count; i++)
            {
                for (int k = -AvoidEaseSamples; k <= AvoidEaseSamples; k++)
                {
                    int j = i + k;
                    if (chain.closed) j = ((j % count) + count) % count;
                    else if (j < 0 || j >= count) continue;
                    float candidate = needed[j] * (1f - Mathf.Abs(k) / (AvoidEaseSamples + 1f));
                    if (Mathf.Abs(candidate) > Mathf.Abs(eased[i])) eased[i] = candidate;
                }
            }

            var runs = new List<List<Vector3>>();
            List<Vector3> run = null;
            for (int i = 0; i < count; i++)
            {
                if (TryPlace(samples[i], lefts[i], eased[i], needed[i], overlap, out Vector3 placed, out bool wasNudged, out bool isBlocked))
                {
                    if (wasNudged) nudged++;
                    if (isBlocked) blocked++;
                    if (run == null) { run = new List<Vector3>(); runs.Add(run); }
                    run.Add(placed);
                }
                else
                {
                    rejected++;
                    run = null;
                }
            }

            bool whole = chain.closed && runs.Count == 1 && runs[0].Count == samples.Count;
            if (chain.closed && !whole && runs.Count > 1 && samples.Count > 0)
            {
                // Join the run that wraps past the loop start.
                var first = runs[0];
                var last = runs[runs.Count - 1];
                if (first.Count > 0 && last.Count > 0 && (first[0] - samples[0]).sqrMagnitude < 4f && (last[last.Count - 1] - samples[samples.Count - 1]).sqrMagnitude < 4f)
                {
                    last.AddRange(first);
                    runs.RemoveAt(0);
                }
            }

            foreach (var r in runs)
            {
                if (whole) r.Add(r[0]);
                var lane = BuildLane(r, whole);
                if (lane == null) continue;
                if (lane.Length < MinLaneLength) { droppedLength += lane.Length; continue; }
                lanes.Add(lane);
            }
        }

        int merged = MergeTouchingLanes(lanes);
        int connected = 0, openEnds = 0;
        ConnectOpenEnds(lanes, ref connected, ref openEnds);

        Undo.RecordObject(network, "Bake Footpath Lanes");
        network.lanes = lanes;
        EditorUtility.SetDirty(network);
        EditorSceneManager.MarkSceneDirty(network.gameObject.scene);

        float total = 0f;
        int loops = 0;
        foreach (var l in lanes) { total += l.Length; if (l.loop) loops++; }
        return "Footpath lanes baked: footpathSubmeshes=" + footpathMeshes + " boundaryEdges=" + edges.Count + " chains=" + chains.Count
            + " lanes=" + lanes.Count + " (loops=" + loops + ", open=" + (lanes.Count - loops) + ") totalLength=" + total.ToString("F0") + "m"
            + " nudgedSamples=" + nudged + " blockedSamples=" + blocked + " rejectedSamples=" + rejected
            + " droppedShortLength=" + droppedLength.ToString("F0") + "m mergedFragments=" + merged + " openEndsConnected=" + connected + "/" + openEnds;
    }

    // Joins an open lane whose end meets the start of another lane heading the same way (or its own start).
    static int MergeTouchingLanes(List<FootpathLane> lanes)
    {
        int merges = 0;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int a = 0; a < lanes.Count && !changed; a++)
            {
                if (lanes[a].loop) continue;
                Vector3[] pa = lanes[a].points;
                Vector3 end = pa[pa.Length - 1];
                Vector3 endDir = Flat(end - pa[Mathf.Max(0, pa.Length - 3)]).normalized;

                for (int b = 0; b < lanes.Count; b++)
                {
                    if (lanes[b].loop) continue;
                    Vector3[] pb = lanes[b].points;
                    if ((pb[0] - end).sqrMagnitude > 3f * 3f || Mathf.Abs(pb[0].y - end.y) > 1f) continue;
                    Vector3 startDir = Flat(pb[Mathf.Min(pb.Length - 1, 2)] - pb[0]).normalized;
                    if (Vector3.Dot(endDir, startDir) < 0.5f) continue;

                    if (a == b)
                    {
                        if (lanes[a].Length < MinLaneLength * 2f) continue;
                        var closedPts = new List<Vector3>(pa) { pa[0] };
                        lanes[a] = BuildLane(closedPts, true);
                    }
                    else
                    {
                        var joined = new List<Vector3>(pa);
                        joined.AddRange(pb);
                        lanes[a] = BuildLane(joined, false);
                        lanes.RemoveAt(b);
                    }
                    merges++;
                    changed = true;
                    break;
                }
            }
        }
        return merges;
    }

    static void AddEdge(Dictionary<long, int> use, Dictionary<long, (int, int, int)> info, int a, int b, int c)
    {
        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        use.TryGetValue(key, out int n);
        use[key] = n + 1;
        if (n == 0) info[key] = (a, b, c);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    static List<Vector3> Simplify(List<Vector3> pts, bool closed)
    {
        var result = new List<Vector3>();
        foreach (var p in pts)
            if (result.Count == 0 || (p - result[result.Count - 1]).sqrMagnitude > 0.3f * 0.3f) result.Add(p);
        if (closed && result.Count > 2 && (result[0] - result[result.Count - 1]).sqrMagnitude <= 0.3f * 0.3f)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    static List<Vector3> Offset(List<Vector3> pts, bool closed, float distance)
    {
        int n = pts.Count;
        var result = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            bool hasPrev = closed || i > 0;
            bool hasNext = closed || i < n - 1;
            Vector3 nPrev = hasPrev ? LeftNormal(pts[(i - 1 + n) % n], pts[i]) : Vector3.zero;
            Vector3 nNext = hasNext ? LeftNormal(pts[i], pts[(i + 1) % n]) : Vector3.zero;
            Vector3 miter = (nPrev + nNext).normalized;
            if (miter.sqrMagnitude < 1e-4f) miter = hasNext ? nNext : nPrev;
            float denom = Mathf.Max(0.4f, Vector3.Dot(miter, hasPrev ? nPrev : nNext));
            result.Add(pts[i] + miter * (distance / denom));
        }
        return result;
    }

    static Vector3 LeftNormal(Vector3 a, Vector3 b)
    {
        return Vector3.Cross(Flat(b - a), Vector3.up).normalized;
    }

    static List<Vector3> Resample(List<Vector3> pts, bool closed)
    {
        var result = new List<Vector3>();
        int segs = closed ? pts.Count : pts.Count - 1;
        float carry = 0f;
        for (int i = 0; i < segs; i++)
        {
            Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
            float len = Vector3.Distance(a, b);
            float d = carry;
            while (d < len)
            {
                result.Add(Vector3.Lerp(a, b, d / len));
                d += SampleSpacing;
            }
            carry = d - len;
        }
        if (!closed) result.Add(pts[pts.Count - 1]);
        return result;
    }

    // Smallest sideways shift (positive = toward the footpath centre) that leaves room for a walker; 0 if already clear.
    static float RequiredShift(Vector3 sample, Vector3 left, Collider[] overlap)
    {
        if (!SurfaceAt(sample, out Vector3 ground) || IsClear(ground, overlap)) return 0f;
        for (float s = 0.1f; s <= MaxAvoidShift + 1e-3f; s += 0.1f)
        {
            if (SurfaceAt(sample + left * s, out ground) && IsClear(ground, overlap)) return s;
            if (SurfaceAt(sample - left * s, out ground) && IsClear(ground, overlap)) return -s;
        }
        return 0f;
    }

    static bool ForceSide(List<Vector3> samples, Vector3[] lefts, float[] needed, int start, int end, float side, Collider[] overlap)
    {
        var forced = new float[end - start + 1];
        for (int k = start; k <= end; k++)
        {
            if (needed[k] == 0f) continue;
            float shift = 0f;
            for (float s = 0.1f; s <= MaxAvoidShift + 1e-3f; s += 0.1f)
            {
                if (SurfaceAt(samples[k] + lefts[k] * s * side, out Vector3 ground) && IsClear(ground, overlap)) { shift = s * side; break; }
            }
            if (shift == 0f) return false;
            forced[k - start] = shift;
        }
        for (int k = start; k <= end; k++)
            if (needed[k] != 0f) needed[k] = forced[k - start];
        return true;
    }

    static bool IsClear(Vector3 ground, Collider[] overlap, float radius = WalkerClearance)
    {
        // Keep the capsule's bottom above the path surface and kerb lip so only real obstacles count.
        int n = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * (radius + 0.2f), ground + Vector3.up * 1.7f, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (overlap[i].GetComponentInParent<CharacterController>() == null) return false;
        return true;
    }

    static bool TryPlace(Vector3 sample, Vector3 left, float easedShift, float neededShift, Collider[] overlap, out Vector3 placed, out bool nudged, out bool blocked)
    {
        placed = sample;
        nudged = false;
        blocked = false;
        bool haveFallback = false;
        Vector3 fallback = sample;

        float[] shifts = { easedShift, neededShift, 0f };
        foreach (float shift in shifts)
        {
            if (!SurfaceAt(sample + left * shift, out Vector3 ground)) continue;
            if (IsClear(ground, overlap))
            {
                placed = ground;
                nudged = shift != 0f;
                return true;
            }
            if (!haveFallback) { haveFallback = true; fallback = ground; }
        }

        if (!haveFallback) return false;
        placed = fallback;
        blocked = true;
        return true;
    }

    // The top surface directly below must be the footpath material (not road, plaza, or a prop on top of it).
    static bool SurfaceAt(Vector3 p, out Vector3 ground)
    {
        ground = p;
        // Start just above the path so lamp heads and railings overhead don't hide it.
        if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore)) return false;
        var mr = hit.collider.GetComponent<MeshRenderer>();
        if (mr == null) return false;
        bool footpath = false;
        foreach (var mat in mr.sharedMaterials)
            if (mat != null && mat.name == FootpathMaterial) { footpath = true; break; }
        if (!footpath) return false;
        ground = hit.point;
        return true;
    }

    static FootpathLane BuildLane(List<Vector3> pts, bool loop)
    {
        if (pts.Count < 2) return null;
        var d = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) d[i] = d[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        return new FootpathLane { points = pts.ToArray(), distances = d, loop = loop };
    }

    // Walkers glide straight across a hop between lanes, so the whole hop must be free of obstacles.
    static bool HopClear(Vector3 a, Vector3 b, Collider[] overlap)
    {
        float length = Vector3.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.25f));
        // End points are lane points the bake already accepted; only the path between them is new.
        for (int i = 1; i < steps; i++)
            if (!IsClear(Vector3.Lerp(a, b, (float)i / steps), overlap, WalkerBodyRadius)) return false;
        return true;
    }

    static void ConnectOpenEnds(List<FootpathLane> lanes, ref int connected, ref int openEnds)
    {
        var overlap = new Collider[8];
        const float cell = 6f;
        var buckets = new Dictionary<Vector2Int, List<(int lane, int index)>>();
        for (int l = 0; l < lanes.Count; l++)
        {
            var p = lanes[l].points;
            for (int i = 0; i < p.Length; i++)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p[i].x / cell), Mathf.FloorToInt(p[i].z / cell));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(int, int)>();
                list.Add((l, i));
            }
        }

        for (int l = 0; l < lanes.Count; l++)
        {
            var lane = lanes[l];
            if (lane.loop) continue;
            openEnds++;
            Vector3[] p = lane.points;
            Vector3 end = p[p.Length - 1];
            Vector3 heading = Flat(end - p[Mathf.Max(0, p.Length - 3)]).normalized;

            float bestScore = float.MaxValue;
            int bestLane = -1, bestIndex = 0;
            var baseKey = new Vector2Int(Mathf.FloorToInt(end.x / cell), Mathf.FloorToInt(end.z / cell));
            // Look nearby first; only reach further when every close connection is blocked.
            // Last pass accepts an unclear hop (e.g. brushing a building corner) rather than leaving a dead end.
            for (int pass = 0; pass < 3; pass++)
            {
            if (bestLane >= 0) break;
            float maxHop = pass == 0 ? 5f : 9f;
            bool requireClear = pass < 2;
            int reach = Mathf.CeilToInt(maxHop / cell);
            for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!buckets.TryGetValue(baseKey + new Vector2Int(dx, dz), out var list)) continue;
                foreach (var cand in list)
                {
                    var cp = lanes[cand.lane].points;
                    if (cand.lane == l && cand.index > p.Length - 8) continue;
                    if (cand.index >= cp.Length - 1) continue;
                    float dist = Vector3.Distance(end, cp[cand.index]);
                    if (dist > maxHop || Mathf.Abs(end.y - cp[cand.index].y) > 1.5f) continue;
                    if (requireClear && !HopClear(end, cp[cand.index], overlap)) continue;
                    Vector3 candHeading = Flat(cp[cand.index + 1] - cp[cand.index]).normalized;
                    float align = Vector3.Dot(heading, candHeading);
                    // Prefer continuing the same way; turning around onto the opposite lane is the fallback.
                    float score = dist + (align > 0.3f ? 0f : 20f);
                    if (score < bestScore) { bestScore = score; bestLane = cand.lane; bestIndex = cand.index; }
                }
            }
            }

            if (bestLane >= 0)
            {
                lane.nextLane = bestLane;
                lane.nextDistance = lanes[bestLane].distances[bestIndex];
                connected++;
            }
        }
    }
}
