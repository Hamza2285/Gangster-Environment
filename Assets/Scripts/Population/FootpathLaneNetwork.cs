using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FootpathLane
{
    public Vector3[] points;
    public float[] distances;
    public bool loop;
    [Tooltip("For open lanes: where a walker continues after reaching the end (-1 = nowhere).")]
    public int nextLane = -1;
    public float nextDistance;

    public float Length
    {
        get { return distances[distances.Length - 1]; }
    }
}

// Walking lanes baked from the footpath meshes (Tools > Population > Bake Footpath Lanes).
// Each lane runs 1m in from one footpath edge with the footpath on its left, so the two edges
// of every footpath give two lanes heading in opposite directions.
public class FootpathLaneNetwork : MonoBehaviour
{
    public List<FootpathLane> lanes = new List<FootpathLane>();

    public Vector3 Evaluate(int laneIndex, float distance, out Vector3 forward)
    {
        FootpathLane lane = lanes[laneIndex];
        float[] d = lane.distances;
        Vector3[] p = lane.points;

        int lo = 0, hi = d.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (d[mid] <= distance) lo = mid;
            else hi = mid;
        }

        float segment = d[hi] - d[lo];
        float t = segment > 1e-4f ? Mathf.Clamp01((distance - d[lo]) / segment) : 0f;
        forward = p[hi] - p[lo];
        return Vector3.Lerp(p[lo], p[hi], t);
    }

    private void OnDrawGizmosSelected()
    {
        for (int i = 0; i < lanes.Count; i++)
        {
            Vector3[] p = lanes[i].points;
            if (p == null) continue;
            Gizmos.color = (i % 2 == 0) ? Color.cyan : Color.magenta;
            for (int j = 1; j < p.Length; j++)
            {
                Gizmos.DrawLine(p[j - 1] + Vector3.up * 0.2f, p[j] + Vector3.up * 0.2f);
                if (j % 8 == 0)
                {
                    Vector3 dir = (p[j] - p[j - 1]).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.4f;
                    Vector3 tip = p[j] + Vector3.up * 0.2f;
                    Gizmos.DrawLine(tip, tip - dir * 0.8f + side);
                    Gizmos.DrawLine(tip, tip - dir * 0.8f - side);
                }
            }
        }
    }
}
