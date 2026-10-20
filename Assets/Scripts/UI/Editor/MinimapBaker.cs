using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Editor tools for the HUD minimap (<see cref="Minimap"/>):
/// - Bake Map: draws the roads from the iTS lane network and renders the building roots once from an orthographic top-down
///   camera, turns them into masks and writes a stylised, readable map texture (dark ground, grey blocks, light outlined roads) plus a
///   dark grayscale copy for the full-screen map. The scene is only touched temporarily (renderers of other roots are hidden
///   for the passes and restored in a finally block) and nothing is rendered for the minimap at runtime.
/// - Create Marker Types: icon sprites (generated, 64 px) and MinimapMarkerType assets for objective, mission, enemy, vehicle
///   and shop markers.
/// - Create HUD Minimap: builds the minimap widget under GameUI_Canvas/HUD_Panel and wires it (scene change: save the scene).
/// </summary>
public static class MinimapBaker
{
    private const string OutputFolder = "Assets/_Project/Textures/Minimap";
    private const string IconFolder = OutputFolder + "/Icons";
    private const string TypeFolder = "Assets/_Project/Data/Minimap";
    private const string MapPath = OutputFolder + "/Minimap_Map.png";
    private const string FullMapPath = OutputFolder + "/Minimap_FullMap.png";
    private const int Resolution = 2048;
    private const float Margin = 25f;

    // Scene roots holding the building blocks (Gangstar City).
    private static readonly string[] BuildingRoots = { "Building 01", "Building 02", "Building 03", "Cafe", "Bus and casino", "Petrol pump" };

    private static readonly Color Ground = new Color(0.17f, 0.21f, 0.19f);
    private static readonly Color Block = new Color(0.30f, 0.32f, 0.35f);
    private static readonly Color Road = new Color(0.78f, 0.78f, 0.76f);
    private static readonly Color RoadEdge = new Color(0.08f, 0.08f, 0.09f);

    [MenuItem("Tools/OpenworldCrime/Minimap/Bake Map")]
    public static void BakeMap()
    {
        // Roads come from the iTS lane network (the road meshes share one ground/sidewalk mesh, so they cannot be told apart),
        // building blocks from one top-down render of the building roots.
        var traffic = Object.FindFirstObjectByType<TSMainManager>();
        List<Renderer> blocks = CollectRenderers(BuildingRoots);
        if (traffic == null || traffic.lanes == null || traffic.lanes.Length == 0)
        {
            Debug.LogError("[MinimapBaker] No iTS TSMainManager with lanes in the open scene; cannot draw the roads.");
            return;
        }
        var segments = CollectLaneSegments(traffic, out Bounds bounds);
        float size = Mathf.Max(bounds.size.x, bounds.size.z) + Margin * 2f;
        Vector2 worldMin = new Vector2(bounds.center.x - size * 0.5f, bounds.center.z - size * 0.5f);

        float[] roadMask = RasterizeRoads(segments, worldMin, size);
        float[] blockMask = blocks.Count > 0 ? RenderMask(blocks, worldMin, size, 600f) : new float[Resolution * Resolution];
        var roads = segments;

        // Road edge: roads dilated by one or two pixels minus the road itself.
        float[] dilated = Dilate(roadMask, 2);
        var map = new Color[Resolution * Resolution];
        var full = new Color[Resolution * Resolution];
        for (int i = 0; i < map.Length; i++)
        {
            Color c = Color.Lerp(Ground, Block, blockMask[i]);
            c = Color.Lerp(c, RoadEdge, Mathf.Clamp01(dilated[i] - roadMask[i]) * 0.9f);
            c = Color.Lerp(c, Road, roadMask[i]);
            c.a = 1f;
            map[i] = c;
            float g = c.grayscale * 0.75f;
            full[i] = new Color(g, g, g * 1.05f, 1f);
        }

        Directory.CreateDirectory(OutputFolder);
        WriteTexture(MapPath, map);
        WriteTexture(FullMapPath, full);

        var minimap = Object.FindFirstObjectByType<Minimap>(FindObjectsInactive.Include);
        if (minimap != null)
        {
            Undo.RecordObject(minimap, "Bake minimap");
            minimap.mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
            minimap.fullMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FullMapPath);
            minimap.worldMin = worldMin;
            minimap.worldSize = size;
            if (minimap.mapImage != null) minimap.mapImage.texture = minimap.mapTexture;
            EditorUtility.SetDirty(minimap);
            EditorSceneManager.MarkSceneDirty(minimap.gameObject.scene);
        }
        Debug.Log($"[MinimapBaker] Baked {Resolution}px map: worldMin {worldMin}, worldSize {size:F0} m ({size / Resolution:F2} m/px), " +
                  $"{roads.Count} road segments and {blocks.Count} building renderers. Minimap in scene: {(minimap != null ? minimap.name : "none (run Create HUD Minimap)")}.");
    }

    private struct Segment
    {
        public Vector2 a, b;
        public float halfWidth;
    }

    private static List<Segment> CollectLaneSegments(TSMainManager traffic, out Bounds bounds)
    {
        var result = new List<Segment>();
        bounds = new Bounds(traffic.lanes[0].points[0].point, Vector3.zero);
        foreach (TSLaneInfo lane in traffic.lanes)
        {
            if (lane == null || lane.points == null) continue;
            // Lanes are drawn a bit wider than the lane so neighbouring lanes merge into one road.
            float half = Mathf.Max(lane.laneWidth, 3.5f) * 0.5f + 1.6f;
            for (int i = 0; i + 1 < lane.points.Length; i++)
            {
                Vector3 a = lane.points[i].point, b = lane.points[i + 1].point;
                bounds.Encapsulate(a);
                result.Add(new Segment { a = new Vector2(a.x, a.z), b = new Vector2(b.x, b.z), halfWidth = half });
            }
            if (lane.connectors == null) continue;
            foreach (TSLaneConnector c in lane.connectors)
            {
                if (c == null || c.points == null) continue;
                for (int i = 0; i + 1 < c.points.Length; i++)
                {
                    Vector3 a = c.points[i].point, b = c.points[i + 1].point;
                    result.Add(new Segment { a = new Vector2(a.x, a.z), b = new Vector2(b.x, b.z), halfWidth = half });
                }
            }
        }
        return result;
    }

    // Thick anti-aliased line segments into a coverage mask (world -> texture pixels).
    private static float[] RasterizeRoads(List<Segment> segments, Vector2 worldMin, float size)
    {
        var mask = new float[Resolution * Resolution];
        float pxPerM = Resolution / size;
        foreach (Segment s in segments)
        {
            Vector2 a = (s.a - worldMin) * pxPerM, b = (s.b - worldMin) * pxPerM;
            float hw = s.halfWidth * pxPerM;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - hw - 1)), x1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + hw + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - hw - 1)), y1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + hw + 1));
            Vector2 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float d = Vector2.Distance(p, a + ab * t);
                float c = Mathf.Clamp01(hw + 0.5f - d);
                int i = y * Resolution + x;
                if (c > mask[i]) mask[i] = c;
            }
        }
        return mask;
    }

    private static List<Renderer> CollectRenderers(string[] roots)
    {
        var result = new List<Renderer>();
        var scene = EditorSceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(roots, root.name) < 0 || !root.activeInHierarchy) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) result.Add(r);
        }
        return result;
    }

    // Renders only 'visible' (every other scene renderer is hidden for the pass) and returns a 0..1 coverage mask.
    private static float[] RenderMask(List<Renderer> visible, Vector2 worldMin, float size, float height)
    {
        var keep = new HashSet<Renderer>(visible);
        var hidden = new List<Renderer>();
        var camGo = new GameObject("MinimapBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
        try
        {
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (keep.Contains(r) || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }

            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size * 0.5f;
            cam.transform.SetPositionAndRotation(new Vector3(worldMin.x + size * 0.5f, height, worldMin.y + size * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 1f;
            cam.farClipPlane = height + 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color32[] px = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            var mask = new float[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                bool covered = c.a > 8 || c.r > 3 || c.g > 3 || c.b > 3;
                mask[i] = covered ? 1f : 0f;
            }
            return mask;
        }
        finally
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    private static float[] Dilate(float[] mask, int radius)
    {
        var result = new float[mask.Length];
        for (int y = 0; y < Resolution; y++)
        for (int x = 0; x < Resolution; x++)
        {
            float m = 0f;
            for (int dy = -radius; dy <= radius && m < 1f; dy++)
            {
                int yy = y + dy;
                if (yy < 0 || yy >= Resolution) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= Resolution) continue;
                    if (mask[yy * Resolution + xx] > m) m = mask[yy * Resolution + xx];
                }
            }
            result[y * Resolution + x] = m;
        }
        return result;
    }

    private static void WriteTexture(string path, Color[] pixels)
    {
        var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = true;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = Resolution;
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.alphaSource = TextureImporterAlphaSource.None;
        imp.SaveAndReimport();
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Marker types and icons
    // ---------------------------------------------------------------------------------------------------------------------

    private enum Shape { Dot, Ring, Diamond, Square, Arrow }

    [MenuItem("Tools/OpenworldCrime/Minimap/Create Marker Types")]
    public static void CreateMarkerTypes()
    {
        Directory.CreateDirectory(IconFolder);
        Directory.CreateDirectory(TypeFolder);
        CreateType("Objective", Shape.Diamond, new Color(1f, 0.85f, 0.1f), 18f, true, false, 0f, 30);
        CreateType("Mission", Shape.Ring, new Color(0.25f, 0.65f, 1f), 18f, true, false, 0f, 20);
        CreateType("Enemy", Shape.Dot, new Color(0.95f, 0.2f, 0.2f), 10f, false, false, 0f, 10);
        CreateType("Vehicle", Shape.Arrow, new Color(0.9f, 0.9f, 0.9f), 12f, false, true, 120f, 5);
        CreateType("Shop", Shape.Square, new Color(0.3f, 0.9f, 0.4f), 14f, true, false, 400f, 15);
        GetIcon(Shape.Dot);
        AssetDatabase.SaveAssets();
        Debug.Log("[MinimapBaker] Marker types in " + TypeFolder + ", icons in " + IconFolder + ".");
    }

    private static MinimapMarkerType CreateType(string name, Shape shape, Color colour, float size, bool clamp, bool rotate, float maxDistance, int order)
    {
        string path = TypeFolder + "/MinimapMarker_" + name + ".asset";
        var type = AssetDatabase.LoadAssetAtPath<MinimapMarkerType>(path);
        if (type == null)
        {
            type = ScriptableObject.CreateInstance<MinimapMarkerType>();
            AssetDatabase.CreateAsset(type, path);
        }
        var so = new SerializedObject(type);
        so.FindProperty("icon").objectReferenceValue = GetIcon(shape);
        so.FindProperty("colour").colorValue = colour;
        so.FindProperty("size").floatValue = size;
        so.FindProperty("clampToEdge").boolValue = clamp;
        so.FindProperty("rotateWithTarget").boolValue = rotate;
        so.FindProperty("maxDistance").floatValue = maxDistance;
        so.FindProperty("drawOrder").intValue = order;
        so.ApplyModifiedPropertiesWithoutUndo();
        return type;
    }

    private static Sprite GetIcon(Shape shape)
    {
        string path = IconFolder + "/MinimapIcon_" + shape + ".png";
        if (File.Exists(path) && AssetImporter.GetAtPath(path) is TextureImporter existing && existing.spriteImportMode != SpriteImportMode.Single)
        {
            existing.textureType = TextureImporterType.Sprite;
            existing.spriteImportMode = SpriteImportMode.Single;
            existing.SaveAndReimport();
        }
        if (!File.Exists(path))
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float a = Coverage(shape, u, v);
                float outline = Coverage(shape, u * 0.82f, v * 0.82f) - a;
                // White fill (tinted by the marker colour) with a dark outline for contrast on the map.
                Color c = Color.Lerp(new Color(0.05f, 0.05f, 0.05f, 0f), Color.white, a);
                c.a = Mathf.Clamp01(a + outline);
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Anti-aliased coverage (0..1) of a shape in [-1, 1] space, sized to leave room for the outline.
    private static float Coverage(Shape shape, float u, float v)
    {
        const float aa = 0.06f;
        float d;
        switch (shape)
        {
            case Shape.Ring:
                float r = Mathf.Sqrt(u * u + v * v);
                d = Mathf.Max(r - 0.75f, 0.42f - r);
                break;
            case Shape.Diamond:
                d = Mathf.Abs(u) + Mathf.Abs(v) - 0.8f;
                break;
            case Shape.Square:
                d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - 0.62f;
                break;
            case Shape.Arrow:
                // Upward-pointing chevron triangle.
                float edge = Mathf.Max(Mathf.Abs(u) * 1.6f + v - 0.8f, -v - 0.6f);
                d = edge * 0.7f;
                break;
            default:
                d = Mathf.Sqrt(u * u + v * v) - 0.7f;
                break;
        }
        return Mathf.Clamp01(0.5f - d / aa);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // HUD widget
    // ---------------------------------------------------------------------------------------------------------------------

    [MenuItem("Tools/OpenworldCrime/Minimap/Create HUD Minimap")]
    public static void CreateHudMinimap()
    {
        var hud = GameObject.Find("UI/GameUI_Canvas/HUD_Panel");
        if (hud == null)
        {
            Debug.LogError("[MinimapBaker] UI/GameUI_Canvas/HUD_Panel not found in the open scene.");
            return;
        }
        var existing = hud.transform.Find("Minimap_Root");
        if (existing != null)
        {
            Debug.LogWarning("[MinimapBaker] HUD_Panel already has a Minimap_Root; nothing created.", existing);
            return;
        }
        var player = GameObject.Find("Player/Player_Jason");
        Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        Sprite arrow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Minimap/MinimapPlayerArrow.png");
        const float size = 260f;

        var root = NewUI("Minimap_Root", hud.transform);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = new Vector2(40f, 84f);
        root.sizeDelta = new Vector2(size, size);

        var frame = NewUI("Minimap_Frame", root);
        Stretch(frame, -5f);
        var frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.sprite = circle;
        frameImage.color = new Color(0f, 0f, 0f, 0.85f);
        frameImage.raycastTarget = false;

        var viewport = NewUI("Minimap_Viewport", root);
        Stretch(viewport, 0f);
        // Nested canvas: the minimap's per-frame changes only rebuild this canvas, not the whole HUD.
        viewport.gameObject.AddComponent<Canvas>();
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.sprite = circle;
        viewportImage.color = Ground;
        viewportImage.raycastTarget = false;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var rotator = NewUI("Minimap_Rotator", viewport);
        Stretch(rotator, 0f);

        var map = NewUI("Minimap_Map", rotator);
        map.anchorMin = map.anchorMax = map.pivot = new Vector2(0.5f, 0.5f);
        map.sizeDelta = new Vector2(size * 1.45f, size * 1.45f); // covers the corners while turning
        var mapImage = map.gameObject.AddComponent<RawImage>();
        mapImage.raycastTarget = false;
        mapImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);

        var markers = NewUI("Minimap_Markers", rotator);
        markers.anchorMin = markers.anchorMax = markers.pivot = new Vector2(0.5f, 0.5f);
        markers.sizeDelta = Vector2.zero;

        var waypoint = NewUI("Minimap_Waypoint", viewport);
        waypoint.anchorMin = waypoint.anchorMax = waypoint.pivot = new Vector2(0.5f, 0.5f);
        waypoint.sizeDelta = new Vector2(16f, 16f);
        var waypointImage = waypoint.gameObject.AddComponent<Image>();
        waypointImage.sprite = GetIcon(Shape.Diamond);
        waypointImage.color = new Color(0.8f, 0.35f, 1f);
        waypointImage.raycastTarget = false;
        waypoint.gameObject.SetActive(false);

        var playerArrow = NewUI("Minimap_PlayerArrow", viewport);
        playerArrow.anchorMin = playerArrow.anchorMax = playerArrow.pivot = new Vector2(0.5f, 0.5f);
        playerArrow.sizeDelta = new Vector2(22f, 22f);
        var arrowImage = playerArrow.gameObject.AddComponent<Image>();
        arrowImage.sprite = arrow != null ? arrow : GetIcon(Shape.Arrow);
        arrowImage.preserveAspect = true;
        arrowImage.raycastTarget = false;

        var north = NewUI("Minimap_North", viewport);
        north.anchorMin = north.anchorMax = north.pivot = new Vector2(0.5f, 0.5f);
        north.sizeDelta = new Vector2(20f, 20f);
        var northText = north.gameObject.AddComponent<Text>();
        northText.text = "N";
        northText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        northText.fontSize = 15;
        northText.fontStyle = FontStyle.Bold;
        northText.alignment = TextAnchor.MiddleCenter;
        northText.color = Color.white;
        northText.raycastTarget = false;

        var minimap = root.gameObject.AddComponent<Minimap>();
        minimap.target = player != null ? player.transform : null;
        minimap.mapImage = mapImage;
        minimap.playerIcon = playerArrow;
        minimap.waypointIcon = waypoint;
        minimap.mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
        minimap.fullMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FullMapPath);
        var so = new SerializedObject(minimap);
        so.FindProperty("viewport").objectReferenceValue = viewport;
        so.FindProperty("rotator").objectReferenceValue = rotator;
        so.FindProperty("markerLayer").objectReferenceValue = markers;
        so.FindProperty("northLabel").objectReferenceValue = north;
        so.FindProperty("blipSprite").objectReferenceValue = GetIcon(Shape.Dot);
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create HUD minimap");
        EditorSceneManager.MarkSceneDirty(hud.scene);
        Selection.activeGameObject = root.gameObject;
        Debug.Log("[MinimapBaker] Created HUD minimap under HUD_Panel. Run Bake Map to fill in the map texture and world bounds, then save the scene.", root);
    }

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, float outset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(-outset, -outset);
        rt.offsetMax = new Vector2(outset, outset);
    }
}
