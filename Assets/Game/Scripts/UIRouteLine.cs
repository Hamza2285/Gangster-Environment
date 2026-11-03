using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws a polyline in this rect's local space. Respects UI masks.
[RequireComponent(typeof(CanvasRenderer))]
public class UIRouteLine : MaskableGraphic
{
    public float thickness = 5f;
    readonly List<Vector2> points = new List<Vector2>();

    public void SetPoints(List<Vector2> newPoints)
    {
        points.Clear();
        if (newPoints != null) points.AddRange(newPoints);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points.Count < 2) return;
        float half = thickness * 0.5f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.0001f) continue;
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x) * half;
            // Extend each piece a little so joints on bends have no gaps.
            a -= dir * half * 0.5f;
            b += dir * half * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - side, color, Vector2.zero);
            vh.AddVert(a + side, color, Vector2.zero);
            vh.AddVert(b + side, color, Vector2.zero);
            vh.AddVert(b - side, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
