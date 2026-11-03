using System.Collections.Generic;
using UnityEngine;

// Read-only road graph built from the iTS lanes. Used for the GPS route and the taxi.
public sealed class RoadNetwork
{
    public sealed class Link
    {
        public int from, to;
        public TSLaneConnector source;
        public Vector3[] points;
        public float length;
    }

    public sealed class Lane
    {
        public TSLaneInfo source;
        public Vector3[] points;
        public float[] distance;   // distance from the lane start at each point
        public float length;
        public readonly List<Link> outgoing = new List<Link>();
        public readonly List<Link> incoming = new List<Link>();
    }

    public struct RoadPoint
    {
        public int lane;
        public int index;
        public Vector3 position;
        public Vector3 direction;
        public float sqrDistance;
    }

    static RoadNetwork instance;
    public readonly List<Lane> lanes = new List<Lane>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    public static RoadNetwork Instance
    {
        get
        {
            if (instance == null)
            {
                var manager = Object.FindFirstObjectByType<TSMainManager>();
                if (manager == null) return null;
                instance = new RoadNetwork(manager);
            }
            return instance;
        }
    }

    RoadNetwork(TSMainManager manager)
    {
        foreach (var source in manager.lanes)
        {
            var lane = new Lane { source = source };
            lane.points = Clean(source.points);
            lane.distance = Cumulative(lane.points, out lane.length);
            lanes.Add(lane);
        }
        for (int i = 0; i < manager.lanes.Length; i++)
        {
            foreach (var connector in manager.lanes[i].connectors)
            {
                if (connector.nextLane < 0 || connector.nextLane >= lanes.Count) continue;
                var link = new Link { from = i, to = connector.nextLane, source = connector, points = Clean(connector.points) };
                Cumulative(link.points, out link.length);
                lanes[i].outgoing.Add(link);
                lanes[link.to].incoming.Add(link);
            }
        }
    }

    static Vector3[] Clean(TSPoints[] source)
    {
        var result = new List<Vector3>(source.Length);
        foreach (var p in source)
        {
            if (result.Count == 0 || (result[result.Count - 1] - p.point).sqrMagnitude > 0.01f) result.Add(p.point);
        }
        return result.ToArray();
    }

    static float[] Cumulative(Vector3[] points, out float total)
    {
        var d = new float[points.Length];
        for (int i = 1; i < points.Length; i++) d[i] = d[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        total = points.Length > 0 ? d[points.Length - 1] : 0f;
        return d;
    }

    // Height counts extra so a point under a bridge does not snap onto the deck above.
    static float ScoredSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dy = (a.y - b.y) * 3f, dz = a.z - b.z;
        return dx * dx + dy * dy + dz * dz;
    }

    public bool TryFindNearest(Vector3 position, out RoadPoint result)
    {
        result = default;
        float best = float.MaxValue;
        bool found = false;
        for (int l = 0; l < lanes.Count; l++)
        {
            var pts = lanes[l].points;
            if (lanes[l].length < 10f) continue;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = ScoredSqrDistance(pts[i], position);
                if (d < best)
                {
                    best = d;
                    result = MakePoint(l, i, d);
                    found = true;
                }
            }
        }
        return found;
    }

    RoadPoint MakePoint(int lane, int index, float sqrDistance)
    {
        var pts = lanes[lane].points;
        int a = Mathf.Max(0, index - 1), b = Mathf.Min(pts.Length - 1, index + 1);
        Vector3 dir = pts[b] - pts[a];
        dir.y = 0f;
        return new RoadPoint { lane = lane, index = index, position = pts[index], direction = dir.normalized, sqrDistance = sqrDistance };
    }

    // Nearest point on every lane within radius of the position (both travel directions of a road).
    List<RoadPoint> Candidates(Vector3 position, float radius)
    {
        var list = new List<RoadPoint>();
        float limit = radius * radius;
        for (int l = 0; l < lanes.Count; l++)
        {
            if (lanes[l].length < 10f) continue;
            var pts = lanes[l].points;
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = ScoredSqrDistance(pts[i], position);
                if (d < best) { best = d; bestIndex = i; }
            }
            if (bestIndex >= 0 && best <= limit) list.Add(MakePoint(l, bestIndex, best));
        }
        if (list.Count == 0 && TryFindNearest(position, out RoadPoint nearest)) list.Add(nearest);
        return list;
    }

    // Shortest drivable route that respects lane directions. Output is spaced about every spacing metres.
    public bool TryFindRoute(Vector3 from, Vector3 to, List<Vector3> output, float spacing = 4f)
    {
        output.Clear();
        var starts = Candidates(from, 24f);
        var ends = Candidates(to, 24f);
        if (starts.Count == 0 || ends.Count == 0) return false;
        return FindRoute(starts, ends, output, spacing);
    }

    // Route between two exact road points, for a car that is already on a lane.
    public bool TryFindRoute(RoadPoint from, RoadPoint to, List<Vector3> output, List<Link> linksUsed, float spacing = 4f)
    {
        output.Clear();
        linksUsed.Clear();
        return FindRoute(new List<RoadPoint> { from }, new List<RoadPoint> { to }, output, spacing, linksUsed);
    }

    bool FindRoute(List<RoadPoint> starts, List<RoadPoint> ends, List<Vector3> output, float spacing, List<Link> linksUsed = null)
    {
        int n = lanes.Count;
        var entryCost = new float[n];       // cost to reach the start of a lane
        var exitCost = new float[n];        // cost to reach the end of a lane
        var viaLink = new Link[n];
        var startIndex = new int[n];
        var done = new bool[n];
        for (int i = 0; i < n; i++) { entryCost[i] = float.MaxValue; exitCost[i] = float.MaxValue; startIndex[i] = -1; }

        foreach (var s in starts)
        {
            exitCost[s.lane] = lanes[s.lane].length - lanes[s.lane].distance[s.index];
            startIndex[s.lane] = s.index;
        }

        for (int step = 0; step < n; step++)
        {
            int current = -1;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!done[i] && exitCost[i] < best) { best = exitCost[i]; current = i; }
            }
            if (current < 0) break;
            done[current] = true;
            foreach (var link in lanes[current].outgoing)
            {
                float entry = exitCost[current] + link.length;
                if (entry < entryCost[link.to])
                {
                    entryCost[link.to] = entry;
                    viaLink[link.to] = link;
                    float exit = entry + lanes[link.to].length;
                    if (exit < exitCost[link.to]) { exitCost[link.to] = exit; if (!done[link.to]) startIndex[link.to] = -1; }
                }
            }
        }

        float bestTotal = float.MaxValue;
        RoadPoint bestEnd = default;
        bool sameLane = false;
        foreach (var e in ends)
        {
            float along = lanes[e.lane].distance[e.index];
            if (startIndex[e.lane] >= 0 && startIndex[e.lane] <= e.index)
            {
                float direct = along - lanes[e.lane].distance[startIndex[e.lane]];
                if (direct < bestTotal) { bestTotal = direct; bestEnd = e; sameLane = true; }
            }
            if (entryCost[e.lane] < float.MaxValue && entryCost[e.lane] + along < bestTotal)
            {
                bestTotal = entryCost[e.lane] + along;
                bestEnd = e;
                sameLane = false;
            }
        }
        if (bestTotal == float.MaxValue) return false;

        var raw = new List<Vector3>();
        if (sameLane)
        {
            AppendRange(raw, lanes[bestEnd.lane].points, startIndex[bestEnd.lane], bestEnd.index);
        }
        else
        {
            var chain = new List<Link>();
            int lane = bestEnd.lane;
            while (viaLink[lane] != null && chain.Count <= n)
            {
                chain.Add(viaLink[lane]);
                lane = viaLink[lane].from;
                if (startIndex[lane] >= 0) break;
            }
            chain.Reverse();
            linksUsed?.AddRange(chain);
            int first = startIndex[lane] >= 0 ? startIndex[lane] : 0;
            AppendRange(raw, lanes[lane].points, first, lanes[lane].points.Length - 1);
            for (int i = 0; i < chain.Count; i++)
            {
                raw.AddRange(chain[i].points);
                int nextLane = chain[i].to;
                int last = i == chain.Count - 1 ? bestEnd.index : lanes[nextLane].points.Length - 1;
                AppendRange(raw, lanes[nextLane].points, 0, last);
            }
        }
        Resample(raw, spacing, output);
        return output.Count >= 2;
    }

    static void AppendRange(List<Vector3> target, Vector3[] source, int from, int to)
    {
        for (int i = from; i <= to && i < source.Length; i++) target.Add(source[i]);
    }

    static void Resample(List<Vector3> raw, float spacing, List<Vector3> output)
    {
        if (raw.Count == 0) return;
        output.Add(raw[0]);
        Vector3 last = raw[0];
        for (int i = 1; i < raw.Count; i++)
        {
            if ((raw[i] - last).sqrMagnitude >= spacing * spacing)
            {
                output.Add(raw[i]);
                last = raw[i];
            }
        }
        if (raw[raw.Count - 1] != last) output.Add(raw[raw.Count - 1]);
    }

    // Best place for a car to pull over near a position: the lane next to the footpath on that side.
    public bool TryFindCurbStop(Vector3 position, out RoadPoint result)
    {
        if (!TryFindNearest(position, out result)) return false;
        Vector3 left = new Vector3(-result.direction.z, 0f, result.direction.x);
        float best = 0f;
        Vector3 origin = result.position;
        Vector3 forward = result.direction;
        foreach (var candidate in Candidates(position, 30f))
        {
            if (Vector3.Dot(candidate.direction, forward) < 0.85f) continue;
            float side = Vector3.Dot(candidate.position - origin, left);
            if (side > best && side <= 9f)
            {
                best = side;
                result = candidate;
            }
        }
        return true;
    }

    // Where a car already driving can join its lane: the point at least "ahead" metres further along.
    // False when the car isn't on a lane heading that way (inside a junction) or the lane ends too soon.
    public bool TryFindLaneAhead(Vector3 position, Vector3 forward, float ahead, out RoadPoint result)
    {
        result = default;
        float best = 49f;
        int bestLane = -1, bestIndex = -1;
        for (int l = 0; l < lanes.Count; l++)
        {
            if (lanes[l].length < 10f) continue;
            var pts = lanes[l].points;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = ScoredSqrDistance(pts[i], position);
                if (d < best && Vector3.Dot(MakePoint(l, i, d).direction, forward) > 0.7f)
                {
                    best = d;
                    bestLane = l;
                    bestIndex = i;
                }
            }
        }
        if (bestLane < 0) return false;
        var lane = lanes[bestLane];
        for (int i = bestIndex; i < lane.points.Length; i++)
        {
            if (lane.distance[i] - lane.distance[bestIndex] < ahead) continue;
            result = MakePoint(bestLane, i, best);
            return true;
        }
        return false;
    }

    // Traffic keeps left, so the curb is to the left of travel. Moves the path sideways toward it,
    // fading in over the first and last stretches so the car eases in and out of the curb.
    public static void OffsetToCurb(List<Vector3> path, float offset, float startBlend, float endBlend)
    {
        if (path.Count < 2) return;
        var distance = new float[path.Count];
        for (int i = 1; i < path.Count; i++) distance[i] = distance[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        float total = distance[path.Count - 1];
        var moved = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            float start = startBlend > 0f ? Mathf.SmoothStep(1f, 0f, distance[i] / startBlend) : 0f;
            float end = endBlend > 0f ? Mathf.SmoothStep(1f, 0f, (total - distance[i]) / endBlend) : 0f;
            float weight = Mathf.Max(start, end);
            Vector3 dir = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
            dir.y = 0f;
            dir.Normalize();
            moved[i] = path[i] + new Vector3(-dir.z, 0f, dir.x) * (offset * weight);
        }
        for (int i = 0; i < path.Count; i++) path[i] = moved[i];
    }

    // Path of roughly the given length that ends at the road point, walking lanes backwards.
    public List<Vector3> BuildApproachPath(RoadPoint end, float length, List<Link> linksUsed, out int firstLane)
    {
        linksUsed.Clear();
        var reversed = new List<Vector3>();
        float travelled = 0f;
        int lane = end.lane;
        int index = end.index;
        int guard = 0;
        while (travelled < length && guard++ < 64)
        {
            var pts = lanes[lane].points;
            for (int i = index; i >= 0; i--) reversed.Add(pts[i]);
            travelled += lanes[lane].distance[index];
            var incoming = lanes[lane].incoming;
            if (incoming.Count == 0) break;
            var link = incoming[Random.Range(0, incoming.Count)];
            for (int i = link.points.Length - 1; i >= 0; i--) reversed.Add(link.points[i]);
            travelled += link.length;
            linksUsed.Add(link);
            lane = link.from;
            index = lanes[lane].points.Length - 1;
        }
        firstLane = lane;
        linksUsed.Reverse();
        reversed.Reverse();
        return reversed;
    }

    // Path of roughly the given length that starts at the road point, following traffic direction.
    public List<Vector3> BuildDepartPath(RoadPoint start, float length, List<Link> linksUsed)
    {
        linksUsed.Clear();
        var path = new List<Vector3>();
        float travelled = 0f;
        int lane = start.lane;
        int index = start.index;
        int guard = 0;
        while (travelled < length && guard++ < 64)
        {
            var pts = lanes[lane].points;
            for (int i = index; i < pts.Length; i++) path.Add(pts[i]);
            travelled += lanes[lane].length - lanes[lane].distance[index];
            var outgoing = lanes[lane].outgoing;
            if (outgoing.Count == 0) break;
            var link = outgoing[Random.Range(0, outgoing.Count)];
            path.AddRange(link.points);
            travelled += link.length;
            linksUsed.Add(link);
            lane = link.to;
            index = 0;
        }
        return path;
    }
}
