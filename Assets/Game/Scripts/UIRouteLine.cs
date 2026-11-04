using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws a route as a row of arrows pointing along it, in this rect's local space. Respects UI masks.
[RequireComponent(typeof(CanvasRenderer))]
public class UIRouteLine : MaskableGraphic
{
    public float thickness = 5f;
    readonly List<Vector2> points = new List<Vector2>();
    readonly List<float> distance = new List<float>();
    float sizeScale = 1f;

    public void SetPoints(List<Vector2> newPoints)
    {
        points.Clear();
        if (newPoints != null) points.AddRange(newPoints);
        distance.Clear();
        float total = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) total += Vector2.Distance(points[i - 1], points[i]);
            distance.Add(total);
        }
        SetVerticesDirty();
    }

    // The map zooms by scaling this rect, so this keeps the arrows the same size on screen.
    public void SetSizeScale(float scale)
    {
        sizeScale = scale;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points.Count < 2) return;
        float size = thickness * sizeScale;
        float spacing = size * 3.2f;
        float total = distance[distance.Count - 1];
        int segment = 0;
        for (float at = spacing * 0.5f; at < total; at += spacing)
        {
            while (segment < points.Count - 2 && distance[segment + 1] < at) segment++;
            Vector2 centre = Sample(at, segment);
            // Measured over the whole arrow so small kinks in the road don't twist it.
            Vector2 dir = Sample(at + size, segment) - Sample(Mathf.Max(0f, at - size), 0);
            if (dir.sqrMagnitude < 0.0001f) continue;
            AddArrow(vh, centre, dir.normalized, size);
        }
    }

    Vector2 Sample(float at, int segment)
    {
        at = Mathf.Min(at, distance[distance.Count - 1]);
        while (segment < points.Count - 2 && distance[segment + 1] < at) segment++;
        float length = distance[segment + 1] - distance[segment];
        float k = length > 0.0001f ? (at - distance[segment]) / length : 0f;
        return Vector2.Lerp(points[segment], points[segment + 1], Mathf.Clamp01(k));
    }

    void AddArrow(VertexHelper vh, Vector2 centre, Vector2 dir, float size)
    {
        Vector2 side = new Vector2(-dir.y, dir.x) * size * 1.05f;
        Vector2 tip = centre + dir * size * 0.75f;
        Vector2 back = centre - dir * size * 0.75f;
        Vector2 bar = dir * size * 0.8f;
        AddArm(vh, tip, tip - bar, back + side - bar, back + side);
        AddArm(vh, tip, back - side, back - side - bar, tip - bar);
    }

    void AddArm(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddVert(d, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
