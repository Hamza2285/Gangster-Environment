using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws a route in this rect's local space, either as a row of arrows pointing along it or (solid) as one GPS-style band
// wide enough to cover the road: a single see-through strip (no overlapping pieces, so no darker blotches at the bends) with
// soft glowing edges, a brighter centre line and a light shimmer running along it toward the destination. Respects UI masks.
[RequireComponent(typeof(CanvasRenderer))]
public class UIRouteLine : MaskableGraphic
{
    public float thickness = 5f;
    [Tooltip("One continuous band instead of arrows; thickness is then its full width (glow included).")]
    public bool solid;
    [Tooltip("Solid band: overall opacity (lower = more see-through).")]
    [Range(0.2f, 1f)] public float opacity = 0.8f;
    [Tooltip("Solid band: a bright pulse travelling along the line.")]
    public bool shine = true;
    [Tooltip("Solid band: shimmer speed along the line, in band widths per second.")]
    public float shineSpeed = 2.2f;
    [Tooltip("Solid band: distance between shimmers, in band widths.")]
    public float shineSpacing = 9f;
    [Tooltip("Kept for old scenes; the solid band now uses a soft glowing edge instead of a rim.")]
    public Color rimColor = new Color(0.05f, 0.45f, 0.4f, 1f);
    public float rimWidth = 0f;
    readonly List<Vector2> points = new List<Vector2>();
    readonly List<float> distance = new List<float>();
    float sizeScale = 1f;

    static Texture2D profile;

    // Across the band (v): faint glow at the edges, the body, and a bright core in the middle.
    static Texture2D Profile()
    {
        if (profile != null) return profile;
        const int h = 64;
        profile = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Route Profile" };
        var px = new Color[4 * h];
        for (int y = 0; y < h; y++)
        {
            float across = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;          // 0 centre .. 1 edge
            float body = 1f - Smooth(0.55f, 0.72f, across);       // the band itself
            float glow = (1f - Smooth(0.6f, 1f, across)) * 0.35f;  // soft halo outside it
            float core = 1f - Smooth(0.0f, 0.28f, across);        // bright centre line
            float a = Mathf.Max(body * 0.78f, glow) + core * 0.22f;
            float v = Mathf.Lerp(0.78f, 1f, Mathf.Max(core, body * 0.4f));  // edges a touch deeper, centre lighter
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(v, v, v, Mathf.Clamp01(a));
        }
        profile.SetPixels(px);
        profile.Apply();
        return profile;
    }

    // Shader-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).
    static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    public override Texture mainTexture => solid ? Profile() : base.mainTexture;

    void Update()
    {
        // The shimmer moves every frame.
        if (solid && shine && points.Count > 1 && isActiveAndEnabled) SetVerticesDirty();
    }

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
            AddStrip(vh, thickness * sizeScale * 0.5f);
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

    readonly List<Vector2> bandPoints = new List<Vector2>();
    readonly List<float> bandDistance = new List<float>();

    // One continuous strip: a pair of vertices per point, offset along the mitred normal (so the bends join without overlap),
    // plus a rounded cap at each end. v runs across the band (the glow profile); the colour carries the opacity and shimmer.
    void AddStrip(VertexHelper vh, float half)
    {
        // Points closer than about half the width add nothing visible and only make the mitres jittery.
        bandPoints.Clear();
        bandDistance.Clear();
        float minStep = Mathf.Max(1f, half * 0.6f);
        for (int i = 0; i < points.Count; i++)
            if (i == 0 || i == points.Count - 1 || Vector2.Distance(bandPoints[bandPoints.Count - 1], points[i]) >= minStep)
            {
                bandDistance.Add(bandPoints.Count == 0 ? 0f : bandDistance[bandDistance.Count - 1] + Vector2.Distance(bandPoints[bandPoints.Count - 1], points[i]));
                bandPoints.Add(points[i]);
            }
        int n = bandPoints.Count;
        if (n < 2) return;

        float width = half * 2f;
        float clock = Application.isPlaying ? Time.unscaledTime : 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = i > 0 ? (bandPoints[i] - bandPoints[i - 1]).normalized : (bandPoints[1] - bandPoints[0]).normalized;
            Vector2 next = i < n - 1 ? (bandPoints[i + 1] - bandPoints[i]).normalized : prev;
            Vector2 tangent = (prev + next).sqrMagnitude > 0.0001f ? (prev + next).normalized : next;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 prevNormal = new Vector2(-prev.y, prev.x);
            float miter = half / Mathf.Max(0.5f, Vector2.Dot(normal, prevNormal));   // capped so a hairpin can't spike
            Color32 c = StripColour(bandDistance[i], width, clock);
            vh.AddVert(bandPoints[i] + normal * miter, c, new Vector2(0.5f, 1f));
            vh.AddVert(bandPoints[i] - normal * miter, c, new Vector2(0.5f, 0f));
            if (i > 0)
            {
                int a = (i - 1) * 2;
                vh.AddTriangle(a, a + 2, a + 3);
                vh.AddTriangle(a, a + 3, a + 1);
            }
        }
        AddCap(vh, bandPoints[0], (bandPoints[0] - bandPoints[1]).normalized, half, StripColour(0f, width, clock));
        AddCap(vh, bandPoints[n - 1], (bandPoints[n - 1] - bandPoints[n - 2]).normalized, half, StripColour(bandDistance[n - 1], width, clock));
    }

    // Base colour at the set opacity, lifted toward white where a shimmer passes (it runs from the player to the destination).
    Color32 StripColour(float along, float width, float clock)
    {
        Color c = color;
        c.a *= opacity;
        if (shine && width > 0.01f)
        {
            float period = shineSpacing * width;
            float phase = Mathf.Repeat(along - clock * shineSpeed * width, period) / width;   // in band widths
            float pulse = Mathf.Exp(-phase * phase * 0.8f) + Mathf.Exp(-(shineSpacing - phase) * (shineSpacing - phase) * 0.8f);
            c = Color.Lerp(c, new Color(1f, 1f, 1f, Mathf.Min(1f, c.a + 0.15f)), Mathf.Clamp01(pulse) * 0.55f);
        }
        return c;
    }

    // A half disc closing one end, its rim on the band's edge profile and its centre on the core.
    void AddCap(VertexHelper vh, Vector2 at, Vector2 outward, float half, Color32 c)
    {
        const int steps = 8;
        Vector2 side = new Vector2(-outward.y, outward.x);
        int centre = vh.currentVertCount;
        vh.AddVert(at, c, new Vector2(0.5f, 0.5f));
        for (int s = 0; s <= steps; s++)
        {
            float angle = Mathf.PI * s / steps;
            Vector2 dir = side * Mathf.Cos(angle) + outward * Mathf.Sin(angle);
            vh.AddVert(at + dir * half, c, new Vector2(0.5f, 1f));
        }
        for (int s = 0; s < steps; s++) vh.AddTriangle(centre, centre + 1 + s, centre + 2 + s);
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
