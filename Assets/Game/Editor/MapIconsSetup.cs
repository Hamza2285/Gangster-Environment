using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Map Icons Setup: the neon icon set (Assets/Neon Vice City Icon Sheet.png, cut into Assets/Game/UI/MapIcons) on the city map.
// - Every shop gets its icon, including the duplicate buildings that had none (second Gangfire gun shop, second and third
//   clothing store): placed where the original's entrance sits on the same building.
// - Every beach food stall gets a food icon (burger / pizza / taco in turn).
// - Ghost's map blips (races, gun shops, club) and the clothing store / showroom icons switch to the neon set.
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
            var so = new SerializedObject(blip);
            so.FindProperty("icon").objectReferenceValue = neon;
            so.FindProperty("iconColour").colorValue = Color.white;
            so.FindProperty("backgroundColour").colorValue = new Color(0f, 0f, 0f, 0f);
            so.FindProperty("size").floatValue = blip.gameObject.name.Contains("Checkpoint") ? 26f : 34f;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  blip {blip.name} -> {neon.name}");
        }

        // Race starts re-colour their blip at runtime (available / greyed out): white neon flag, no disc.
        foreach (var start in Object.FindObjectsByType<OpenworldCrime.Racing.RaceStartPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(start);
            so.FindProperty("iconColour").colorValue = Color.white;
            so.FindProperty("backgroundColour").colorValue = new Color(0f, 0f, 0f, 0f);
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
            icon.size = 34f;
            EditorUtility.SetDirty(icon);
            log.AppendLine($"  icon {icon.label} -> {neon.name}");
        }

        // 3. Duplicate shop buildings: an icon where the original's entrance is, on the same spot of the copy.
        AddForCopies(root.transform, "Gangfire Weapon Shop", FindEntrance("Gun Shop Entrance - gunshop_gangfire"), "Gun Shop", gun, log);
        AddForCopies(root.transform, "Miami Clothing Store", FindEntrance("Miami Clothing Store Entrance"), "Clothing Store", clothes, log);

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
            icon.size = 26f;
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
            icon.size = 34f;
            log.AppendLine($"  {label} icon for {copy.name} at {go.transform.position}");
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
