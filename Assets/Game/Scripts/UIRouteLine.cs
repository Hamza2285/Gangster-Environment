using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws a route in this rect's local space, either as a row of arrows pointing along it or (solid) as one thick
// GPS-style band with round joins and a darker rim, wide enough to cover the road. Respects UI masks.
[RequireComponent(typeof(CanvasRenderer))]
public class UIRouteLine : MaskableGraphic
{
    public float thickness = 5f;
    [Tooltip("One continuous band instead of arrows; thickness is then its full width.")]
    public bool solid;
    [Tooltip("Solid band: colour of its rim.")]
    public Color rimColor = new Color(0.05f, 0.45f, 0.4f, 1f);
    [Tooltip("Solid band: rim width on each side.")]
    public float rimWidth = 1.5f;
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
        if (solid)
        {
            float half = thickness * sizeScale * 0.5f;
            // Rim first, then the band over it: both opaque, so the overlapping joins don't show.
            if (rimWidth > 0f) AddBand(vh, half + rimWidth * sizeScale, rimColor);
            AddBand(vh, half, color);
            return;
        }
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

    // A quad per segment and a round disc at every point, so corners and ends are rounded.
    readonly List<Vector2> bandPoints = new List<Vector2>();

    void AddBand(VertexHelper vh, float half, Color32 c)
    {
        // Points closer than about half the width add nothing visible; skipping them keeps long routes small.
        bandPoints.Clear();
        float minStep = Mathf.Max(1f, half * 0.6f);
        for (int i = 0; i < points.Count; i++)
            if (i == 0 || i == points.Count - 1 || Vector2.Distance(bandPoints[bandPoints.Count - 1], points[i]) >= minStep)
                bandPoints.Add(points[i]);

        for (int i = 1; i < bandPoints.Count; i++)
        {
            Vector2 a = bandPoints[i - 1], b = bandPoints[i];
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) continue;
            Vector2 n = new Vector2(-d.y, d.x).normalized * half;
            int start = vh.currentVertCount;
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
        const int sides = 12;
        for (int i = 0; i < bandPoints.Count; i++)
        {
            int centre = vh.currentVertCount;
            vh.AddVert(bandPoints[i], c, Vector2.zero);
            for (int s = 0; s < sides; s++)
            {
                float angle = s * Mathf.PI * 2f / sides;
                vh.AddVert(bandPoints[i] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * half, c, Vector2.zero);
            }
            for (int s = 0; s < sides; s++) vh.AddTriangle(centre, centre + 1 + s, centre + 1 + (s + 1) % sides);
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
