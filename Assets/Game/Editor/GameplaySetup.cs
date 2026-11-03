using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Sets up the gameplay in the open scene. Safe to run again after an environment update:
// it rebuilds its own objects and leaves everything else alone.
public static class GameplaySetup
{
    const string Root = "Assets/Game";
    const string TaxiPrefabPath = Root + "/Prefabs/Taxi.prefab";
    const string RoadMapPath = Root + "/Minimap/RoadMap.png";
    const string MapDataPath = Root + "/Minimap/MiniMapData.asset";
    const string GameplayName = "Gameplay";
    const string WalkCollidersName = "Walk Ground Colliders";
    const string BuildingCollidersName = "Building Colliders";

    static readonly string[] BuildingRoots = { "Building 01", "Building 02", "Building 03", "Bus and casino", "Cafe" };

    [MenuItem("Game/Setup Gameplay In Open Scene")]
    public static void SetupAll()
    {
        Debug.Log(BakeRoadMap());
        Debug.Log(CreateAssets());
        Debug.Log(BuildColliders());
        Debug.Log(BuildGameplayObjects());
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    [MenuItem("Game/Bake Road Map")]
    public static string BakeRoadMap()
    {
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        if (manager == null) return "No iTSManager in the scene";

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (var lane in manager.lanes)
        {
            foreach (var p in lane.points)
            {
                min = Vector2.Min(min, new Vector2(p.point.x, p.point.z));
                max = Vector2.Max(max, new Vector2(p.point.x, p.point.z));
            }
        }
        const float margin = 80f;
        float size = Mathf.Max(max.x - min.x, max.y - min.y) + margin * 2f;
        Vector2 centre = (min + max) * 0.5f;
        Vector2 worldMin = centre - Vector2.one * size * 0.5f;

        const int resolution = 2048;
        float pixelsPerMetre = resolution / size;
        // Signed "depth inside the road" in metres for every pixel; lanes stamp over each other.
        var depth = new float[resolution * resolution];
        for (int i = 0; i < depth.Length; i++) depth[i] = -100f;

        const float laneRadius = 4.6f, outline = 1.4f;
        foreach (var lane in manager.lanes)
        {
            StampLine(lane.points, worldMin, pixelsPerMetre, resolution, laneRadius, outline, depth);
            foreach (var connector in lane.connectors) StampLine(connector.points, worldMin, pixelsPerMetre, resolution, laneRadius, outline, depth);
        }

        var pixels = new Color32[depth.Length];
        byte bg = 34, edge = 88, road = 168;
        for (int i = 0; i < depth.Length; i++)
        {
            float d = depth[i];
            byte v = d >= 0f ? road : d >= -outline ? edge : bg;
            pixels[i] = new Color32(v, v, v, 255);
        }
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(RoadMapPath));
        File.WriteAllBytes(RoadMapPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RoadMapPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(RoadMapPath);
        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.maxTextureSize = resolution;
        importer.SaveAndReimport();

        var data = AssetDatabase.LoadAssetAtPath<MiniMapData>(MapDataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<MiniMapData>();
            AssetDatabase.CreateAsset(data, MapDataPath);
        }
        data.roadMap = AssetDatabase.LoadAssetAtPath<Texture2D>(RoadMapPath);
        data.worldMin = worldMin;
        data.worldSize = size;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return "Road map baked: world " + size.ToString("F0") + " m from " + worldMin;
    }

    static void StampLine(TSPoints[] points, Vector2 worldMin, float ppm, int res, float radius, float outline, float[] depth)
    {
        float reach = radius + outline;
        int pixelReach = Mathf.CeilToInt(reach * ppm) + 1;
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 a3 = points[i].point, b3 = points[i + 1].point;
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z);
            float length = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int cx = Mathf.RoundToInt((p.x - worldMin.x) * ppm), cy = Mathf.RoundToInt((p.y - worldMin.y) * ppm);
                for (int y = Mathf.Max(0, cy - pixelReach); y <= Mathf.Min(res - 1, cy + pixelReach); y++)
                {
                    for (int x = Mathf.Max(0, cx - pixelReach); x <= Mathf.Min(res - 1, cx + pixelReach); x++)
                    {
                        float dx = x / ppm + worldMin.x - p.x, dy = y / ppm + worldMin.y - p.y;
                        float d = radius - Mathf.Sqrt(dx * dx + dy * dy);
                        int index = y * res + x;
                        if (d > depth[index]) depth[index] = d;
                    }
                }
            }
        }
    }

    public static string CreateAssets()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        var route = Unlit(Root + "/Materials/RouteLine.mat", new Color(1f, 0.78f, 0.1f, 1f), false);
        var beam = Unlit(Root + "/Materials/MarkerBeam.mat", new Color(1f, 0.78f, 0.1f, 0.35f), true);
        var sign = Unlit(Root + "/Materials/TaxiSign.mat", new Color(1f, 0.95f, 0.55f, 1f), false);

        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/iTS/Vehicles/Car17.prefab");
        var car = Object.Instantiate(source);
        car.name = "Taxi";
        // Dependent components first so Unity allows removing the rest.
        foreach (var name in new[] { "TSTrafficAI", "TSNavigation", "TSAntiRollBar", "TSSimpleCar_Wheel", "TSSimpleCar" })
        {
            foreach (var c in car.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == name).ToList()) Object.DestroyImmediate(c);
        }
        foreach (var c in car.GetComponentsInChildren<WheelCollider>(true).ToList()) Object.DestroyImmediate(c);
        foreach (var c in car.GetComponentsInChildren<Collider>(true).ToList()) Object.DestroyImmediate(c);
        foreach (var c in car.GetComponentsInChildren<Rigidbody>(true).ToList()) Object.DestroyImmediate(c);
        foreach (var c in car.GetComponentsInChildren<AudioSource>(true).ToList()) Object.DestroyImmediate(c);
        car.layer = 0;
        foreach (var t in car.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        car.tag = "Untagged";

        var roofSign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofSign.name = "Taxi Roof Sign";
        Object.DestroyImmediate(roofSign.GetComponent<Collider>());
        roofSign.transform.SetParent(car.transform, false);
        roofSign.transform.localPosition = new Vector3(0f, 2.0f, 0.2f);
        roofSign.transform.localScale = new Vector3(1.0f, 0.26f, 0.42f);
        roofSign.GetComponent<MeshRenderer>().sharedMaterial = sign;

        PrefabUtility.SaveAsPrefabAsset(car, TaxiPrefabPath);
        Object.DestroyImmediate(car);
        AssetDatabase.SaveAssets();
        return "Assets ready (route " + route.name + ", beam " + beam.name + ", taxi prefab)";
    }

    static Material Unlit(string path, Color color, bool transparent)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    public static string BuildColliders()
    {
        var scene = SceneManager.GetActiveScene();
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "env");
        Transform FindEnv(string name)
        {
            if (env != null)
            {
                var t = env.transform.Find(name);
                if (t != null) return t;
            }
            return scene.GetRootGameObjects().FirstOrDefault(g => g.name == name)?.transform;
        }

        // Walkable ground that has no collider (sidewalk tops, lots, paths).
        DestroyRoot(scene, WalkCollidersName);
        var walkRoot = new GameObject(WalkCollidersName);
        int walk = 0;
        var roads = FindEnv("Track Roads");
        if (roads != null)
        {
            foreach (var filter in roads.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                if (filter.name.StartsWith("UtilityCovers") || filter.name.StartsWith("GardenPlants")) continue;
                Bounds b = renderer.bounds;
                if (b.size.y > 1.5f || Mathf.Max(b.size.x, b.size.z) < 3f) continue;
                var go = Clone(filter.transform, "Walk_" + filter.name, walkRoot.transform);
                go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                walk++;
            }
        }

        // Buildings use oriented boxes from their mesh bounds so the player cannot walk through them.
        DestroyRoot(scene, BuildingCollidersName);
        var buildingRoot = new GameObject(BuildingCollidersName);
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        var lanePoints = new List<Vector3>();
        if (manager != null) foreach (var lane in manager.lanes) for (int i = 0; i < lane.points.Length; i += 3) lanePoints.Add(lane.points[i].point);
        int boxes = 0, skipped = 0;
        foreach (var rootName in BuildingRoots)
        {
            var building = FindEnv(rootName);
            if (building == null) continue;
            foreach (var filter in building.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                if (filter.name.Contains("LOD1") || filter.name.Contains("Mobile LOD")) continue;
                Bounds b = renderer.bounds;
                if (b.size.y < 4f || Mathf.Max(b.size.x, b.size.z) > 140f || Mathf.Min(b.size.x, b.size.z) < 2f) continue;
                var go = Clone(filter.transform, "Box_" + filter.name, buildingRoot.transform);
                var box = go.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
                // A box that would block a road is wrong (L-shaped building, wide base): drop it.
                Physics.SyncTransforms();
                Bounds worldBox = box.bounds;
                worldBox.Expand(new Vector3(0f, 4f, 0f));
                bool blocksRoad = false;
                foreach (var p in lanePoints)
                {
                    if (worldBox.Contains(p) && box.ClosestPoint(p) == p) { blocksRoad = true; break; }
                }
                if (blocksRoad)
                {
                    Object.DestroyImmediate(go);
                    skipped++;
                }
                else boxes++;
            }
        }

        // Small seams where road pieces meet.
        var roadRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Traffic Road Colliders");
        int patches = 0;
        if (roadRoot != null && manager != null)
        {
            Physics.SyncTransforms();
            foreach (var old in roadRoot.GetComponentsInChildren<BoxCollider>(true).Where(c => c.name == "SeamPatch").ToList()) Object.DestroyImmediate(old.gameObject);
            var gaps = new List<Vector3>();
            foreach (var lane in manager.lanes)
            {
                foreach (var p in lane.points) if (!Physics.Raycast(p.point + Vector3.up * 1.5f, Vector3.down, 3f)) gaps.Add(p.point);
                foreach (var c in lane.connectors) foreach (var p in c.points) if (!Physics.Raycast(p.point + Vector3.up * 1.5f, Vector3.down, 3f)) gaps.Add(p.point);
            }
            foreach (var group in gaps.GroupBy(p => new Vector2Int(Mathf.RoundToInt(p.x / 3f), Mathf.RoundToInt(p.z / 3f))))
            {
                var centre = group.Aggregate(Vector3.zero, (a, b) => a + b) / group.Count();
                var go = new GameObject("SeamPatch");
                go.transform.SetParent(roadRoot.transform, false);
                go.transform.position = new Vector3(centre.x, centre.y - 0.15f, centre.z);
                go.AddComponent<BoxCollider>().size = new Vector3(5f, 0.3f, 5f);
                go.isStatic = true;
                patches++;
            }
        }
        Physics.SyncTransforms();
        return "Colliders: walk ground " + walk + ", building boxes " + boxes + " (dropped " + skipped + " over roads), seam patches " + patches;
    }

    static void DestroyRoot(Scene scene, string name)
    {
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == name).ToList()) Object.DestroyImmediate(g);
    }

    static GameObject Clone(Transform source, string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(source.position, source.rotation);
        go.transform.localScale = source.lossyScale;
        go.isStatic = true;
        return go;
    }

    public static string BuildGameplayObjects()
    {
        var scene = SceneManager.GetActiveScene();
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "env");

        // Keep the camera if a previous run moved it under the player.
        var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GameplayName);
        Camera camera = existing != null ? existing.GetComponentInChildren<Camera>(true) : null;
        if (camera != null) camera.transform.SetParent(null, true);
        if (existing != null) Object.DestroyImmediate(existing);

        if (camera == null)
        {
            var cameraObject = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Main Camera");
            camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        }
        // The camera that came with the environment defines the look; use its settings and switch it off.
        var envCameraTransform = env != null ? env.transform.Find("Camera") : null;
        if (envCameraTransform != null)
        {
            var envCamera = envCameraTransform.GetComponent<Camera>();
            EditorUtility.CopySerialized(envCamera, camera);
            var envData = envCameraTransform.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (envData != null && data != null) EditorUtility.CopySerialized(envData, data);
            envCameraTransform.gameObject.SetActive(false);
        }
        camera.tag = "MainCamera";
        camera.enabled = true;
        camera.gameObject.SetActive(true);
        camera.nearClipPlane = 0.1f;

        var gameplay = new GameObject(GameplayName);

        // Player: stand on the sidewalk beside a lane, facing along the road.
        var player = new GameObject("Player");
        player.transform.SetParent(gameplay.transform, false);
        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.stepOffset = 0.5f;
        controller.slopeLimit = 55f;
        var fps = player.AddComponent<FirstPersonPlayer>();
        camera.transform.SetParent(player.transform, false);
        camera.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        camera.transform.localRotation = Quaternion.identity;
        fps.cameraPivot = camera.transform;
        PlacePlayer(player.transform, manager);

        var waypoints = gameplay.AddComponent<WaypointSystem>();
        waypoints.routeMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/RouteLine.mat");
        waypoints.beamMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MarkerBeam.mat");
        gameplay.AddComponent<ScreenBlink>();
        gameplay.AddComponent<GameMapUI>().mapData = AssetDatabase.LoadAssetAtPath<MiniMapData>(MapDataPath);
        gameplay.AddComponent<TaxiHud>();
        gameplay.AddComponent<TaxiService>().taxiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TaxiPrefabPath);

        // Traffic spawner follows the player's camera.
        var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Traffic/iTS Traffic Spawner.prefab");
            if (prefab != null) spawner = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<TSTrafficSpawner>();
        }
        if (spawner != null)
        {
            spawner.manager = manager;
            // The spawner measures its radius from its own transform, so it travels with the player.
            spawner.transform.SetParent(player.transform, false);
            spawner.transform.localPosition = Vector3.zero;
            spawner.maxDistance = 180f;   // cars appear 100 m to 260 m from the player
            spawner.offset = 80f;
            spawner.initialNotSpawningRadius = 40f;
            var field = typeof(TSTrafficSpawner).GetField("transformSpawningCheck");
            var type = field.FieldType.GetElementType();
            var check = System.Activator.CreateInstance(type);
            type.GetField("transform").SetValue(check, camera.transform);
            type.GetField("initialNotSpawningRadius").SetValue(check, 40f);
            var array = System.Array.CreateInstance(type, 1);
            array.SetValue(check, 0);
            field.SetValue(spawner, array);
            EditorUtility.SetDirty(spawner);
        }
        return "Gameplay objects ready. Player at " + player.transform.position.ToString("F1");
    }

    static void PlacePlayer(Transform player, TSMainManager manager)
    {
        if (manager == null || manager.lanes.Length == 0) return;
        // Middle of a long lane, then out past the road edge on the curb side (traffic keeps left).
        var lane = manager.lanes.Where(l => l.totalDistance > 100f).OrderBy(l => Vector3.Distance(l.conectorA, new Vector3(449f, 27.7f, 199.5f))).First();
        var points = lane.points;
        Vector3 mid = points[points.Length / 2].point;
        Vector3 dir = points[points.Length / 2 + 1].point - points[points.Length / 2 - 1].point;
        dir.y = 0f;
        dir.Normalize();
        Vector3 curb = new Vector3(-dir.z, 0f, dir.x);
        Physics.SyncTransforms();
        Vector3 spot = mid + curb * 11.5f;
        spot = Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f) ? hit.point + Vector3.up * 0.05f : spot;
        player.SetPositionAndRotation(spot, Quaternion.LookRotation(dir));
    }
}
