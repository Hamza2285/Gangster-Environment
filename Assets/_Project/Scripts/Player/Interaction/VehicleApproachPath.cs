using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Walking path around a vehicle to one of its points (e.g. the driver door), so a character on the other side walks
    /// around the car instead of into it. The vehicle's footprint is the rectangle of its solid colliders in vehicle space,
    /// grown by the character radius plus a margin. The path goes through that rectangle's corners (pushed out by a
    /// clearance) and is the shortest route with clear lines of sight. Other obstacles are not considered.
    /// </summary>
    public class VehicleApproachPath
    {
        private const int NodeCount = 6; // start, 4 corners, goal
        private const int StartNode = 0;
        private const int GoalNode = 5;

        private readonly List<Vector3> waypoints = new List<Vector3>(6);
        private readonly Vector2[] nodes = new Vector2[NodeCount];
        private readonly float[] distances = new float[NodeCount];
        private readonly int[] previous = new int[NodeCount];
        private readonly bool[] visited = new bool[NodeCount];

        private Transform frame;
        private bool hasFootprint;
        private Vector2 blockMin;
        private Vector2 blockMax;

        public IReadOnlyList<Vector3> Waypoints => waypoints;

        /// <summary>
        /// Builds world-space waypoints from start (excluded) to goal (included).
        /// bodyRadius = character radius; bodyMargin = extra gap kept from the vehicle; cornerClearance = how far outside the corners the path goes.
        /// </summary>
        public void Build(IVehicleInteractionTarget vehicle, Vector3 start, Vector3 goal, float bodyRadius, float bodyMargin, float cornerClearance)
        {
            waypoints.Clear();
            frame = vehicle.transform;
            hasFootprint = TryGetFootprint(vehicle, frame, out Vector2 min, out Vector2 max);
            if (!hasFootprint)
            {
                waypoints.Add(goal);
                return;
            }

            float grow = bodyRadius + bodyMargin;
            blockMin = min - Vector2.one * grow;
            blockMax = max + Vector2.one * grow;

            Vector3 localStart = frame.InverseTransformPoint(start);
            Vector3 localGoal = frame.InverseTransformPoint(goal);
            Vector2 start2 = new Vector2(localStart.x, localStart.z);

            // A start hugging the vehicle first steps straight out to the nearest side, so it never cuts through the body.
            if (Contains(blockMin, blockMax, start2))
            {
                start2 = PushOut(start2, 0.05f);
                waypoints.Add(ToWorld(start2, start.y));
            }

            nodes[StartNode] = start2;
            nodes[GoalNode] = new Vector2(localGoal.x, localGoal.z);
            float outer = Mathf.Max(cornerClearance, grow + 0.1f);
            nodes[1] = new Vector2(min.x - outer, min.y - outer);
            nodes[2] = new Vector2(max.x + outer, min.y - outer);
            nodes[3] = new Vector2(max.x + outer, max.y + outer);
            nodes[4] = new Vector2(min.x - outer, max.y + outer);

            for (int i = 0; i < NodeCount; i++)
            {
                distances[i] = float.PositiveInfinity;
                previous[i] = -1;
                visited[i] = false;
            }
            distances[StartNode] = 0f;

            // Dijkstra over 6 nodes; an edge exists when the straight segment does not cross the grown footprint.
            for (int step = 0; step < NodeCount; step++)
            {
                int current = -1;
                for (int i = 0; i < NodeCount; i++)
                {
                    if (!visited[i] && (current < 0 || distances[i] < distances[current])) current = i;
                }
                if (current < 0 || float.IsPositiveInfinity(distances[current])) break;
                visited[current] = true;
                if (current == GoalNode) break;

                for (int next = 1; next < NodeCount; next++)
                {
                    if (visited[next] || SegmentHitsRect(nodes[current], nodes[next], blockMin, blockMax)) continue;
                    float candidate = distances[current] + Vector2.Distance(nodes[current], nodes[next]);
                    if (candidate < distances[next])
                    {
                        distances[next] = candidate;
                        previous[next] = current;
                    }
                }
            }

            if (previous[GoalNode] < 0)
            {
                waypoints.Add(goal);
                return;
            }

            int count = 0;
            for (int node = previous[GoalNode]; node > 0; node = previous[node]) count++;
            int first = waypoints.Count;
            for (int i = 0; i < count; i++) waypoints.Add(Vector3.zero);
            int slot = first + count - 1;
            for (int node = previous[GoalNode]; node > 0; node = previous[node]) waypoints[slot--] = ToWorld(nodes[node], start.y);
            waypoints.Add(goal);
        }

        /// <summary>True when walking straight from one world point to another does not cross the vehicle's grown footprint.</summary>
        public bool IsClear(Vector3 from, Vector3 to)
        {
            if (!hasFootprint || frame == null) return true;
            Vector3 a = frame.InverseTransformPoint(from);
            Vector3 b = frame.InverseTransformPoint(to);
            Vector2 a2 = new Vector2(a.x, a.z);
            // From inside the footprint only moving outward counts as clear.
            if (Contains(blockMin, blockMax, a2)) return false;
            return !SegmentHitsRect(a2, new Vector2(b.x, b.z), blockMin, blockMax);
        }

        private Vector3 ToWorld(Vector2 local, float height)
        {
            Vector3 world = frame.TransformPoint(new Vector3(local.x, 0f, local.y));
            world.y = height;
            return world;
        }

        private Vector2 PushOut(Vector2 point, float margin)
        {
            float left = point.x - blockMin.x;
            float right = blockMax.x - point.x;
            float back = point.y - blockMin.y;
            float front = blockMax.y - point.y;
            float nearest = Mathf.Min(left, Mathf.Min(right, Mathf.Min(back, front)));
            if (nearest == left) return new Vector2(blockMin.x - margin, point.y);
            if (nearest == right) return new Vector2(blockMax.x + margin, point.y);
            if (nearest == back) return new Vector2(point.x, blockMin.y - margin);
            return new Vector2(point.x, blockMax.y + margin);
        }

        private static bool TryGetFootprint(IVehicleInteractionTarget vehicle, Transform frame, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            IReadOnlyList<Collider> colliders = vehicle.GetColliders();
            bool found = false;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger || collider is WheelCollider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;

                Bounds bounds = collider.bounds;
                Vector3 c = bounds.center;
                Vector3 e = bounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new Vector3(
                        c.x + ((corner & 1) == 0 ? -e.x : e.x),
                        c.y + ((corner & 2) == 0 ? -e.y : e.y),
                        c.z + ((corner & 4) == 0 ? -e.z : e.z));
                    Vector3 local = frame.InverseTransformPoint(world);
                    min = Vector2.Min(min, new Vector2(local.x, local.z));
                    max = Vector2.Max(max, new Vector2(local.x, local.z));
                }
                found = true;
            }
            return found;
        }

        private static bool Contains(Vector2 min, Vector2 max, Vector2 point)
        {
            return point.x > min.x && point.x < max.x && point.y > min.y && point.y < max.y;
        }

        // Liang-Barsky clip of a segment against an axis-aligned rectangle.
        private static bool SegmentHitsRect(Vector2 from, Vector2 to, Vector2 min, Vector2 max)
        {
            Vector2 d = to - from;
            float t0 = 0f;
            float t1 = 1f;
            if (!Clip(-d.x, from.x - min.x, ref t0, ref t1)) return false;
            if (!Clip(d.x, max.x - from.x, ref t0, ref t1)) return false;
            if (!Clip(-d.y, from.y - min.y, ref t0, ref t1)) return false;
            if (!Clip(d.y, max.y - from.y, ref t0, ref t1)) return false;
            return t1 - t0 > 0.0001f;
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 0.00001f) return q >= 0f;
            float t = q / p;
            if (p < 0f)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }
    }
}
