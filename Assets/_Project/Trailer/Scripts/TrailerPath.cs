using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Trailer
{
    /// <summary>
    /// Smooth path for the trailer (centripetal Catmull-Rom through points, sampled into an arc-length table), evaluated by
    /// distance. Points can come from iTS lanes (the real road centre lines) or be typed in.
    /// </summary>
    public class TrailerPath
    {
        private readonly List<Vector3> samples = new List<Vector3>();
        private readonly List<float> dist = new List<float>();

        public float Length => dist.Count > 0 ? dist[dist.Count - 1] : 0f;

        public TrailerPath(IList<Vector3> points, float lateralOffset = 0f, int stepsPerSegment = 12)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
                if (pts.Count == 0 || (points[i] - pts[pts.Count - 1]).sqrMagnitude > 0.04f) pts.Add(points[i]);
            if (pts.Count < 2) { pts.Add(pts.Count == 1 ? pts[0] + Vector3.forward : Vector3.forward); }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = i > 0 ? pts[i - 1] : pts[i] * 2f - pts[i + 1];
                Vector3 p1 = pts[i], p2 = pts[i + 1];
                Vector3 p3 = i + 2 < pts.Count ? pts[i + 2] : pts[i + 1] * 2f - pts[i];
                for (int s = 0; s < stepsPerSegment; s++)
                {
                    float t = s / (float)stepsPerSegment;
                    samples.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            samples.Add(pts[pts.Count - 1]);

            if (Mathf.Abs(lateralOffset) > 0.001f)
            {
                var shifted = new List<Vector3>(samples.Count);
                for (int i = 0; i < samples.Count; i++)
                {
                    Vector3 a = samples[Mathf.Max(0, i - 1)], b = samples[Mathf.Min(samples.Count - 1, i + 1)];
                    Vector3 fwd = b - a; fwd.y = 0f;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
                    shifted.Add(samples[i] + right * lateralOffset);
                }
                samples.Clear();
                samples.AddRange(shifted);
            }

            float d = 0f;
            dist.Add(0f);
            for (int i = 1; i < samples.Count; i++) { d += Vector3.Distance(samples[i - 1], samples[i]); dist.Add(d); }
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            // Centripetal parameterisation (alpha 0.5) avoids loops and overshoot on uneven spacing.
            float t0 = 0f;
            float t1 = t0 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p0, p1)), 0.5f);
            float t2 = t1 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p1, p2)), 0.5f);
            float t3 = t2 + Mathf.Pow(Mathf.Max(1e-4f, Vector3.Distance(p2, p3)), 0.5f);
            float u = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }

        public Vector3 Pos(float s)
        {
            if (s <= 0f) return samples[0] + Dir(0f) * s;
            if (s >= Length) return samples[samples.Count - 1] + Dir(Length) * (s - Length);
            int i = Index(s);
            float k = Mathf.InverseLerp(dist[i], dist[i + 1], s);
            return Vector3.Lerp(samples[i], samples[i + 1], k);
        }

        /// <summary>Flat (xz) unit direction of travel at distance s, smoothed over a few metres.</summary>
        public Vector3 Dir(float s)
        {
            float a = Mathf.Clamp(s - 2f, 0f, Length), b = Mathf.Clamp(s + 2f, 0f, Length);
            if (b - a < 0.5f) { a = Mathf.Max(0f, Length - 4f); b = Length; }
            Vector3 d = PosClamped(b) - PosClamped(a);
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        public float Yaw(float s) { Vector3 d = Dir(s); return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// <summary>Signed curvature (deg per metre) at s; positive = turning right.</summary>
        public float Turn(float s) { return Mathf.DeltaAngle(Yaw(s - 4f), Yaw(s + 4f)) / 8f; }

        private Vector3 PosClamped(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = Index(s);
            if (i >= samples.Count - 1) return samples[samples.Count - 1];
            float k = Mathf.InverseLerp(dist[i], dist[i + 1], s);
            return Vector3.Lerp(samples[i], samples[i + 1], k);
        }

        private int Index(float s)
        {
            int lo = 0, hi = dist.Count - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (dist[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return Mathf.Clamp(lo, 0, dist.Count - 2);
        }

        /// <summary>Closest distance along the path to a world point (coarse search on the samples).</summary>
        public float Project(Vector3 p)
        {
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                float d = (samples[i] - p).sqrMagnitude;
                if (d < best) { best = d; bi = i; }
            }
            return dist[bi];
        }

        /// <summary>Points of iTS lanes (by index, joined in order) from the scene's TSMainManager, via reflection.</summary>
        public static List<Vector3> FromLanes(params int[] laneIndices)
        {
            var result = new List<Vector3>();
            MonoBehaviour manager = null;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb.GetType().Name == "TSMainManager") { manager = mb; break; }
            if (manager == null) { Debug.LogError("[Trailer] No TSMainManager in the scene."); return result; }
            var lanes = (System.Array)manager.GetType().GetField("lanes").GetValue(manager);
            foreach (int li in laneIndices)
            {
                if (li < 0 || li >= lanes.Length) { Debug.LogError("[Trailer] Lane index out of range: " + li); continue; }
                object lane = lanes.GetValue(li);
                var pts = (System.Array)lane.GetType().GetField("points").GetValue(lane);
                for (int j = 0; j < pts.Length; j++)
                {
                    object p = pts.GetValue(j);
                    result.Add((Vector3)p.GetType().GetField("point").GetValue(p));
                }
            }
            return result;
        }
    }
}
