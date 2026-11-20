using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Map Icons Setup: the neon icon set (Assets/Neon Vice City Icon Sheet.png, cut into Assets/Game/UI/MapIcons) on the city map.
// - Every shop gets its icon, including the duplicate buildings that had none (second Gangfire gun shop, second and third
//   clothing store): placed where the original's entrance sits on the same building.
// - Every beach food stall gets a food icon (burger / pizza / taco in turn).
// - Ghost's map blips (races, gun shops, club) and the clothing store / showroom icons switch to the neon set. Gun icons only on
//   the Gangfire gun shops: the downtown gun shop entrance keeps working but shows no icon.
// - A glowing chequered flag lies on the road at every race start.
// Safe to run again: it rebuilds what it made (the "Map Icons" root and the "Road Flag Icon" children).
public static class MapIconsSetup
{
    const string IconFolder = "Assets/Game/UI/MapIcons";
    const string FlagMaterialPath = IconFolder + "/RoadFlagIcon.mat";
    const string RootName = "Map Icons";
    const string RoadFlagName = "Road Flag Icon";

    [MenuItem("Game/Map Icons Setup")]
    static void Run() => Debug.Log(SetUp());

    static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/MapIcon_{name}.png");

    public static string SetUp()
    {
        var log = new System.Text.StringBuilder("[MapIconsSetup]\n");
        ImportIcons();
        Sprite gun = Icon("Gun"), clothes = Icon("Clothes"), car = Icon("Car"), club = Icon("Club"), flag = Icon("RaceFlag");
        Sprite[] food = { Icon("Burger"), Icon("Pizza"), Icon("Taco") };
        Sprite glow = Icon("Glow");
        Color halo = new Color(1f, 0.82f, 0.96f, 0.95f);
        if (gun == null || flag == null || food[0] == null) return "[MapIconsSetup] icons missing in " + IconFolder;

        var scene = EditorSceneManager.GetActiveScene();
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) root = go;
        if (root != null) Object.DestroyImmediate(root);
        root = new GameObject(RootName);

        // 1. Ghost's blips: neon icons, no dark disc behind them.
        foreach (var blip in Object.FindObjectsByType<OpenworldCrime.UI.MapBlip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string current = blip.Icon != null ? blip.Icon.name.ToLowerInvariant() : "";
            string where = blip.transform.parent != null ? blip.transform.parent.name : "";
            Sprite neon = current.Contains("race") ? flag
                : where.Contains("Gun Shop") ? gun
                : where.Contains("Club") ? club : null;
            if (neon == null) continue;
            // Gun icons only where a Gangfire gun shop building stands (the downtown entrance is a plain building with a sign).
            bool gunShop = neon == gun;
            blip.enabled = !gunShop || blip.name.ToLowerInvariant().Contains("gangfire");
            EditorUtility.SetDirty(blip);
            if (!blip.enabled) { log.AppendLine($"  blip {blip.name} hidden (no Gangfire shop there)"); continue; }
            var so = new SerializedObject(blip);
            so.FindProperty("icon").objectReferenceValue = neon;
            so.FindProperty("iconColour").colorValue = Color.white;
            // A soft pink halo instead of the dark disc (the icon fills the inner 72%).
            so.FindProperty("backgroundSprite").objectReferenceValue = glow;
            so.FindProperty("backgroundColour").colorValue = halo;
            so.FindProperty("size").floatValue = blip.gameObject.name.Contains("Checkpoint") ? 44f : 56f;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  blip {blip.name} -> {neon.name}");
        }

        // Race starts re-colour their blip at runtime (available / greyed out): white neon flag, no disc.
        foreach (var start in Object.FindObjectsByType<OpenworldCrime.Racing.RaceStartPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(start);
            so.FindProperty("iconColour").colorValue = Color.white;
            so.FindProperty("backgroundColour").colorValue = halo;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // Full map icons a bit bigger than the minimap's.
        foreach (var overlay in Object.FindObjectsByType<OpenworldCrime.UI.MapBlipOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(overlay);
            so.FindProperty("fullMapScale").floatValue = 1.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 2. My shop icons.
        foreach (var icon in Object.FindObjectsByType<MapIcon>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (icon.transform.IsChildOf(root.transform)) continue;
            Sprite neon = icon.label.Contains("Clothing") ? clothes : icon.label.Contains("Showroom") ? car : null;
            if (neon == null) continue;
            icon.icon = neon;
            icon.tint = Color.white;
            icon.size = 40f;
            EditorUtility.SetDirty(icon);
            log.AppendLine($"  icon {icon.label} -> {neon.name}");
        }

        // 3. Duplicate shop buildings: an icon where the original's entrance is, on the same spot of the copy.
        AddForCopies(root.transform, "Gangfire Weapon Shop", FindEntrance("Gun Shop Entrance - gunshop_gangfire"), "Gun Shop", gun, log);
        AddEntrancesForCopies("Miami Clothing Store", FindEntrance("Miami Clothing Store Entrance"), log);

        // 4. Food stalls.
        int n = 0;
        foreach (var stall in Object.FindObjectsByType<FoodStall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = new GameObject("Food Stall Icon " + (n + 1));
            go.transform.SetParent(root.transform, false);
            go.transform.position = stall.transform.position;
            var icon = go.AddComponent<MapIcon>();
            icon.icon = food[n % food.Length];
            icon.label = "Food";
            icon.size = 32f;
            n++;
        }
        log.AppendLine($"  food stalls: {n}");

        // 5. Glowing flag on the road at every race start.
        Material flagMat = FlagMaterial(flag.texture);
        int flags = 0;
        var races = GameObject.Find("Street Races");
        if (races != null)
        {
            foreach (Transform start in races.transform)
            {
                if (!start.name.StartsWith("Race Start")) continue;
                Transform old = start.Find(RoadFlagName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = RoadFlagName;
                quad.transform.SetParent(start, false);
                float y = 0.06f;
                if (Physics.Raycast(start.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                    y = start.InverseTransformPoint(hit.point).y + 0.06f;
                quad.transform.localPosition = new Vector3(0f, y, 0f);
                // Lying flat, its top toward the race direction so it reads from a car driving up to it.
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = Vector3.one * 5.5f;
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = flagMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(quad, 0);
                flags++;
            }
        }
        log.AppendLine($"  road flags: {flags}");

        // 6. Race flags on the middle of the road as the map draws it.
        SnapRaceIconsToRoads(log);

        EditorSceneManager.MarkSceneDirty(scene);
        return log.ToString();
    }

    static Transform FindEntrance(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t;
        return null;
    }

    // The copies are named like the original with " (1)", " (2)"... under the env root.
    static void AddForCopies(Transform root, string buildingName, Transform entrance, string label, Sprite sprite, System.Text.StringBuilder log)
    {
        Transform original = null;
        var copies = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.parent == null || t.parent.name != "env") continue;
            if (t.name == buildingName) original = t;
            else if (t.name.StartsWith(buildingName + " (")) copies.Add(t);
        }
        if (original == null) { log.AppendLine("  !! no " + buildingName); return; }
        Vector3 local = entrance != null ? original.InverseTransformPoint(entrance.position) : Vector3.zero;
        foreach (var copy in copies)
        {
            var go = new GameObject($"{label} Icon ({copy.name})");
            go.transform.SetParent(root, false);
            go.transform.position = copy.TransformPoint(local);
            var icon = go.AddComponent<MapIcon>();
            icon.icon = sprite;
            icon.label = label;
            icon.size = 40f;
            log.AppendLine($"  {label} icon for {copy.name} at {go.transform.position}");
        }
    }

    // Every copy of the building gets a working entrance: a clone of the original's (trigger, ClothingStoreEntrance, exit point and
    // its own map icon), at the same spot of the copy as the original's door, the way out put on the ground in front.
    static void AddEntrancesForCopies(string buildingName, Transform entrance, System.Text.StringBuilder log)
    {
        if (entrance == null) { log.AppendLine("  !! no entrance for " + buildingName); return; }
        Transform original = null;
        var copies = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.parent == null || t.parent.name != "env") continue;
            if (t.name == buildingName) original = t;
            else if (t.name.StartsWith(buildingName + " (")) copies.Add(t);
        }
        if (original == null) { log.AppendLine("  !! no " + buildingName); return; }
        // Clones from an earlier run are rebuilt.
        foreach (Transform sibling in entrance.parent.Cast<Transform>().ToArray())
            if (sibling != entrance && sibling.name.StartsWith(entrance.name + " (")) Object.DestroyImmediate(sibling.gameObject);
        Vector3 local = original.InverseTransformPoint(entrance.position);
        Quaternion turn = Quaternion.Inverse(original.rotation) * entrance.rotation;
        foreach (var copy in copies)
        {
            var clone = Object.Instantiate(entrance.gameObject, entrance.parent);
            clone.name = $"{entrance.name} ({copy.name})";
            clone.transform.SetPositionAndRotation(copy.TransformPoint(local), copy.rotation * turn);
            var door = clone.GetComponent<ClothingStoreEntrance>();
            if (door != null && door.exitPoint != null)
            {
                Vector3 exitAt = door.exitPoint.position;
                if (Physics.Raycast(exitAt + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore)) exitAt.y = hit.point.y;
                door.exitPoint.position = exitAt;
            }
            log.AppendLine($"  clothing store entrance for {copy.name} at {clone.transform.position}");
        }
    }

    static void ImportIcons()
    {
        foreach (string guid in AssetDatabase.FindAssets("MapIcon_ t:Texture2D", new[] { IconFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool flagIcon = path.EndsWith("MapIcon_RaceFlag.png");
            if (importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency && importer.mipmapEnabled == flagIcon) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = flagIcon; // the flag is also drawn on the road
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();
        }
    }

    // A race start sits on its route's first point, often near the kerb of a wide road, so its flag looked off the road on the
    // map. Its map icon moves to a "Map Icon" child at the centre of the road in the baked road map (the race start stays).
    const string RaceIconName = "Map Icon";

    static void SnapRaceIconsToRoads(System.Text.StringBuilder log)
    {
        var ui = Object.FindFirstObjectByType<GameMapUI>(FindObjectsInactive.Include);
        var data = ui != null ? ui.mapData : null;
        if (data == null || data.roadMap == null) { log.AppendLine("  race icons: no road map"); return; }
        var roads = new Texture2D(2, 2);
        roads.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(data.roadMap)));
        foreach (var start in Object.FindObjectsByType<OpenworldCrime.Racing.RaceStartPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var startSo = new SerializedObject(start);
            var blipProp = startSo.FindProperty("blip");
            var blip = blipProp.objectReferenceValue as OpenworldCrime.UI.MapBlip;
            if (blip == null) continue;
            Transform holder = start.transform.Find(RaceIconName);
            if (holder == null)
            {
                holder = new GameObject(RaceIconName).transform;
                holder.SetParent(start.transform, false);
            }
            if (blip.gameObject != holder.gameObject)
            {
                var moved = holder.gameObject.GetComponent<OpenworldCrime.UI.MapBlip>();
                if (moved == null) moved = holder.gameObject.AddComponent<OpenworldCrime.UI.MapBlip>();
                EditorUtility.CopySerialized(blip, moved);
                Object.DestroyImmediate(blip);
                blipProp.objectReferenceValue = moved;
                startSo.ApplyModifiedPropertiesWithoutUndo();
            }
            Vector2 uv = data.WorldToUV(start.transform.position);
            Vector2 centre = RoadCentre(roads, new Vector2(uv.x * roads.width, uv.y * roads.height));
            Vector3 world = data.UVToWorld(new Vector2(centre.x / roads.width, centre.y / roads.height), start.transform.position.y);
            holder.position = world;
            log.AppendLine($"  race icon {start.name}: moved {Vector3.Distance(world, start.transform.position):F1} m to the road centre");
        }
        Object.DestroyImmediate(roads);
    }

    // Nearest road pixel, then a few mean-shift steps over the road pixels around it: on a straight road that settles on the
    // centre line, at a junction on its middle.
    static Vector2 RoadCentre(Texture2D roads, Vector2 pixel)
    {
        System.Func<int, int, bool> isRoad = (x, y) => x >= 0 && y >= 0 && x < roads.width && y < roads.height && roads.GetPixel(x, y).r > 0.5f;
        Vector2 c = pixel;
        if (!isRoad((int)c.x, (int)c.y))
        {
            float best = float.MaxValue;
            for (int dy = -30; dy <= 30; dy++)
                for (int dx = -30; dx <= 30; dx++)
                    if (isRoad((int)pixel.x + dx, (int)pixel.y + dy) && dx * dx + dy * dy < best) { best = dx * dx + dy * dy; c = pixel + new Vector2(dx, dy); }
        }
        const int radius = 26; // px (~22 m): wide enough to reach both edges of the widest avenues
        for (int step = 0; step < 14; step++)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = (int)c.x + dx, y = (int)c.y + dy;
                    if (!isRoad(x, y)) continue;
                    sum += new Vector2(x + 0.5f, y + 0.5f);
                    n++;
                }
            if (n == 0) break;
            c = sum / n;
        }
        return c;
    }

    static Material FlagMaterial(Texture texture)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FlagMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, FlagMaterialPath);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
        mat.SetTexture("_BaseMap", texture);
        // HDR white above 1 so the bloom gives it a neon glow.
        mat.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f, 1f));
        // Transparent, alpha blended, drawn after the road.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
