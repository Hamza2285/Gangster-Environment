using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Bakes the city twice - night, then day - and wires both results into <see cref="BakedLightingSwitcher"/>.
    ///
    /// Why this shape (Unity 6, URP, Android):
    /// - Classic lightmaps + light probes swapped per mode, not Adaptive Probe Volumes: APV does not swap lightmaps, the city's
    ///   hand-written shaders (Hamza's) sample SH / lightmaps directly and have no APV path, and a 2 km city of APV bricks costs
    ///   more memory and per-pixel sampling than a phone should pay.
    /// - Mixed mode Baked Indirect: the sun / moon is one realtime light that CityDayNightController moves and fades over a
    ///   40 s transition. Subtractive and Shadowmask both freeze the direct sun or its shadows into textures at one angle,
    ///   which would be wrong for the whole transition; Baked Indirect keeps direct light and shadows (cars, characters and
    ///   buildings alike) realtime and bakes what realtime cannot afford: sky light, bounce, ambient occlusion and, at night,
    ///   the light pools of every street lamp (baked-only lights: no runtime cost, no shadowmask textures).
    ///
    /// Night is baked first and its lightmaps copied to LightingSets/Night; the day bake stays in the scene's own lighting
    /// folder (it is what the scene loads with), so only two sets exist in memory. Light probe colours of both bakes are
    /// stored as raw float files (identical positions). Lightmaps only go to renderers whose shader samples lightmaps, that
    /// have lightmap UVs and are big enough to matter; everything else (props, foliage, signs, water) is probe lit.
    /// Menu: Tools > OpenworldCrime > Lighting. Runs asynchronously; progress in Temp/daynight_bake_status.txt.
    /// </summary>
    public static class DayNightLightBaker
    {
        private const string SettingsPath = "Assets/_Project/Lighting/OWC_CityLighting.lighting";
        private const string SetsFolder = "Assets/_Project/Scenes/Gangstar_Gameplay/LightingSets";
        private const string NightMapsFolder = SetsFolder + "/Night";
        private const string StatusFile = "Temp/daynight_bake_status.txt";
        private const string LightingRootName = "OWC_Lighting";
        private const string LampRootName = "BakedStreetLamps";
        private const string ProbeGroupName = "CityLightProbes";
        private const string SwitcherName = "CityLighting";

        // --- Bake settings (mobile budget) ---
        private const float TexelsPerMetre = 1.25f;
        private const float BuildingScale = 0.5f;   // facades: soft bounce / sky light only
        private const float TerrainScale = 0.25f;   // coast cliffs, far terrain
        private const float MinLightmapSize = 1.5f; // smaller objects use probes
        private const float DayBounce = 1.0f;
        private const float NightBounce = 1.0f;

        // --- Baked street lamps ---
        private const float LampIntensity = 7f;
        private const float LampRange = 14f;
        private static readonly Color LampColor = new Color(1f, 0.72f, 0.42f);
        private const float ShopLightIntensity = 10f;
        private const float ShopLightRange = 10f;
        private static readonly Color ShopLightColor = new Color(1f, 0.78f, 0.5f);

        // --- Light probes ---
        private const float RoadSampleSpacing = 20f;
        private const float RoadCell = 18f;
        private const float SidewalkOffset = 9f;
        private const float SidewalkCell = 15f;
        private const float LampProbeCell = 3f;

        private static readonly HashSet<string> LightmapShaders = new HashSet<string>
        {
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Baked Lit",
            "Gangstar City/Artwork Mobile Lit", "Gangstar City/City Detail Mobile", "Gangstar City/Road Detail PNG Atlas",
            "Gangstar City/Road Atlas Lit", "Gangstar City/PNG Parking Details", "Gangstar City/Beach Shoreline",
        };

        private enum Step { Idle, Night, Day }
        private static Step step = Step.Idle;
        private static double startTime;
        private static double nightSeconds;
        private static bool restoreNight;
        private static Renderer[] layoutRenderers;
        private static BakedLightingSet nightResult;

        public static bool IsRunning => step != Step.Idle;

        [MenuItem("Tools/OpenworldCrime/Lighting/Prepare City For Bake")]
        public static void PrepareMenu() => Debug.Log(Prepare());

        [MenuItem("Tools/OpenworldCrime/Lighting/Bake Day And Night")]
        public static void BakeMenu() => Debug.Log(Start());

        /// <summary>Lighting settings, renderer lightmap/probe split, baked lamps and light probes. No bake.</summary>
        public static string Prepare()
        {
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller == null) return "[DayNightLightBaker] No CityDayNightController in the open scene.";
            var report = new StringBuilder("[DayNightLightBaker] prepared:\n");
            Lightmapping.lightingSettings = GetSettings();
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
            report.AppendLine(AssignReceiveGI(controller.gameObject.scene, out layoutRenderers));
            report.AppendLine(ExcludeMismatchedLodGroups(controller.gameObject.scene));
            Transform root = GetLightingRoot(controller.gameObject.scene);
            report.AppendLine($"baked lamps: {BuildLamps(root, controller)}");
            report.AppendLine($"light probes: {BuildProbes(root, controller)}");
            SetLampsActive(root, false);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            return report.ToString();
        }

        public static string Start()
        {
            if (Lightmapping.isRunning || IsRunning) return "[DayNightLightBaker] A bake is already running.";
            if (EditorApplication.isPlaying) return "[DayNightLightBaker] Stop play mode first.";
            string prepared = Prepare();
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller == null) return prepared;

            restoreNight = controller.IsNight;
            SetLampsActive(GetLightingRoot(controller.gameObject.scene), true);
            controller.Apply(true);
            if (controller.sun != null) controller.sun.bounceIntensity = NightBounce;
            step = Step.Night;
            startTime = EditorApplication.timeSinceStartup;
            // Completion is detected by polling, not Lightmapping.bakeCompleted: another package's handler on that event
            // (SharedAssets LodLightmapCopy) can throw and stop the event before ours runs.
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            Lightmapping.bakeCancelled -= OnBakeCancelled;
            Lightmapping.bakeCancelled += OnBakeCancelled;
            Status("night bake started");
            if (!BeginBake())
            {
                Abort("could not start the night bake");
                return "[DayNightLightBaker] Could not start the bake.";
            }
            return prepared + "\nBaking night, then day. Progress: " + StatusFile;
        }

        private static bool bakeInFlight;
        private static double idleSince = -1;

        private static bool BeginBake()
        {
            bakeInFlight = Lightmapping.BakeAsync();
            idleSince = -1;
            return bakeInFlight;
        }

        private static void Poll()
        {
            if (step == Step.Idle)
            {
                EditorApplication.update -= Poll;
                return;
            }
            if (!bakeInFlight) return;
            if (Lightmapping.isRunning)
            {
                idleSince = -1;
                return;
            }
            // Give Unity a moment to finish writing the lighting data after the bake stops.
            if (idleSince < 0) idleSince = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - idleSince < 2.0) return;
            bakeInFlight = false;
            if (LightmapSettings.lightmaps.Length == 0)
            {
                Abort("the bake ended without lightmaps");
                return;
            }
            OnBakeCompleted();
        }

        private static void OnBakeCompleted()
        {
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (step == Step.Night)
            {
                nightSeconds = EditorApplication.timeSinceStartup - startTime;
                nightResult = CaptureSet("Night", true);
                Status($"night done ({nightResult.LightmapCount} lightmaps, {Minutes(nightSeconds)}); day bake started");
                SetLampsActive(GetLightingRoot(controller.gameObject.scene), false);
                controller.Apply(false);
                if (controller.sun != null) controller.sun.bounceIntensity = DayBounce;
                step = Step.Day;
                if (!BeginBake()) Abort("could not start the day bake");
                return;
            }
            if (step == Step.Day)
            {
                double total = EditorApplication.timeSinceStartup - startTime;
                BakedLightingSet day = CaptureSet("Day", false);
                bool sameLayout = SameLayout(day, nightResult);
                if (sameLayout)
                {
                    day.EditorClearLayout();
                    nightResult.EditorClearLayout();
                    EditorUtility.SetDirty(day);
                    EditorUtility.SetDirty(nightResult);
                }
                AssetDatabase.SaveAssets();
                Wire(controller, day, nightResult, sameLayout);
                controller.Apply(restoreNight);
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                EditorSceneManager.SaveScene(controller.gameObject.scene);
                Status($"DONE: {day.LightmapCount} lightmaps per set, {day.ProbeCount} probes, shared layout={sameLayout}, night {Minutes(nightSeconds)}, day {Minutes(total - nightSeconds)}, total {Minutes(total)}");
                Finish();
            }
        }

        private static void OnBakeCancelled() => Abort("bake cancelled");

        private static void Abort(string why)
        {
            Status("FAILED: " + why);
            Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Poll;
            Lightmapping.bakeCancelled -= OnBakeCancelled;
            bakeInFlight = false;
            step = Step.Idle;
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static LightingSettings GetSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (s == null)
            {
                EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
                s = new LightingSettings { name = "OWC_CityLighting" };
                AssetDatabase.CreateAsset(s, SettingsPath);
            }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.IndirectOnly;
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.lightmapResolution = TexelsPerMetre;
            s.lightmapPadding = 2;
            s.lightmapMaxSize = 2048;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            s.directionalityMode = LightmapsMode.NonDirectional; // half the memory; normal maps keep realtime sun detail
            s.ao = true;
            s.aoMaxDistance = 2f;
            s.aoExponentIndirect = 1f;
            s.aoExponentDirect = 0f;
            s.maxBounces = 2;
            s.directSampleCount = 32;
            s.indirectSampleCount = 256;
            s.environmentSampleCount = 256;
            s.filteringMode = LightingSettings.FilterMode.Auto;
            s.prioritizeView = false;
            s.autoGenerate = false;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            return s;
        }

        // Lightmaps only where the shader samples them, the mesh has lightmap UVs and the object is big enough; else probes.
        private static string AssignReceiveGI(UnityEngine.SceneManagement.Scene scene, out Renderer[] lightmapped)
        {
            int toMaps = 0, toProbes = 0, buildings = 0, terrain = 0;
            var list = new List<Renderer>();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (r.gameObject.scene != scene) continue;
                if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material material = r.sharedMaterial;
                string shader = material != null && material.shader != null ? material.shader.name : string.Empty;
                Vector3 size = r.bounds.size;
                bool big = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= MinLightmapSize;
                bool canMap = mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord1) && LightmapShaders.Contains(shader) && big;
                var so = new SerializedObject(r);
                if (canMap)
                {
                    string top = TopName(r.transform);
                    float scale = 1f;
                    if (top.StartsWith("Building")) { scale = BuildingScale; buildings++; }
                    else if (top.Contains("Terrain") || top.Contains("Cliff")) { scale = TerrainScale; terrain++; }
                    r.receiveGI = ReceiveGI.Lightmaps;
                    so.FindProperty("m_ScaleInLightmap").floatValue = scale;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    list.Add(r);
                    toMaps++;
                }
                else
                {
                    r.receiveGI = ReceiveGI.LightProbes;
                    if (r.lightProbeUsage == LightProbeUsage.Off) r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    toProbes++;
                }
            }
            lightmapped = list.ToArray();
            return $"renderers: {toMaps} lightmapped ({buildings} buildings x{BuildingScale}, {terrain} terrain x{TerrainScale}), {toProbes} probe lit";
        }

        // SharedAssets' LodLightmapCopy (runs after every bake, on scene load and while building) copies LOD0's lightmap to the
        // lower LODs and throws when a lower LOD has fewer renderers than LOD0 - unless LOD0's lightmap index is -1, which is
        // what a renderer outside the bake reports. Those few LOD groups are taken out of the bake (they stay probe lit).
        private static string ExcludeMismatchedLodGroups(UnityEngine.SceneManagement.Scene scene)
        {
            int excluded = 0;
            foreach (LODGroup group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (group.gameObject.scene != scene) continue;
                LOD[] lods = group.GetLODs();
                if (lods.Length < 2) continue;
                Renderer[] lod0 = lods[0].renderers;
                bool mismatch = false;
                for (int i = 1; i < lods.Length; i++) mismatch |= lods[i].renderers.Length < lod0.Length;
                if (!mismatch) continue;
                foreach (Renderer r in lod0)
                {
                    if (r == null) continue;
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags & ~StaticEditorFlags.ContributeGI);
                    if (r.lightProbeUsage == LightProbeUsage.Off) r.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
                excluded++;
            }
            return $"LOD groups taken out of the bake (lower LOD has fewer renderers than LOD0): {excluded}";
        }

        private static string TopName(Transform t)
        {
            // Name of the group directly under the scene's 'env' root (Building 01, Track Roads, Coastal Rocky Terrain ...).
            string name = t.name;
            while (t.parent != null)
            {
                if (t.parent.parent == null) return name;
                name = t.parent.name;
                t = t.parent;
            }
            return name;
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static Transform GetLightingRoot(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == LightingRootName) return go.transform;
            }
            var root = new GameObject(LightingRootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            return root.transform;
        }

        private static Transform GetChild(Transform root, string name)
        {
            Transform child = root.Find(name);
            if (child != null) return child;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        // Baked-only point light at every street lamp head the controller lights at night (its own list of positions).
        private static int BuildLamps(Transform root, CityDayNightController controller)
        {
            Transform lamps = GetChild(root, LampRootName);
            for (int i = lamps.childCount - 1; i >= 0; i--) Object.DestroyImmediate(lamps.GetChild(i).gameObject);
            Vector3[] heads = controller.streetLightPositions ?? new Vector3[0];
            for (int i = 0; i < heads.Length; i++)
            {
                var go = new GameObject("Lamp_" + i);
                go.transform.SetParent(lamps, false);
                go.transform.position = heads[i];
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.range = LampRange;
                light.intensity = LampIntensity;
                light.color = LampColor;
                light.bounceIntensity = 1f;
                light.shadows = LightShadows.Soft;
                light.shadowRadius = 0.3f;
            }
            // A warm pool in front of every gun shop door, so the shops read as open at night.
            int shops = 0;
            foreach (var entrance in Object.FindObjectsByType<Interiors.ShopEntrance>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                Vector3 outward = entrance.ExitPoint != null ? entrance.ExitPoint.position - entrance.transform.position : -entrance.transform.forward;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                var go = new GameObject("ShopEntranceLight_" + shops++);
                go.transform.SetParent(lamps, false);
                go.transform.position = entrance.transform.position + outward * 1.2f + Vector3.up * 3.6f;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.range = ShopLightRange;
                light.intensity = ShopLightIntensity;
                light.color = ShopLightColor;
                light.shadows = LightShadows.Soft;
                light.shadowRadius = 0.2f;
            }
            return heads.Length + shops;
        }

        private static void SetLampsActive(Transform root, bool active)
        {
            Transform lamps = root.Find(LampRootName);
            if (lamps != null) lamps.gameObject.SetActive(active);
        }

        // Probes along every iTS lane (road centre at 1.2 m and 5 m, both sidewalks at 1.2 m), under every lamp, and around
        // the gun shop entrances. A probe is dropped when it would sit inside a collider (inside a building).
        private static string BuildProbes(Transform root, CityDayNightController controller)
        {
            Transform groupTransform = GetChild(root, ProbeGroupName);
            groupTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            groupTransform.localScale = Vector3.one;
            var group = groupTransform.GetComponent<LightProbeGroup>();
            if (group == null) group = groupTransform.gameObject.AddComponent<LightProbeGroup>();

            int blocked = LayerMask.GetMask("Player", "Enemy", "Vehicle", "VehicleParts", "VehicleHitbox", "RCC", "BodyPart", "Triggers", "Traffic AI", "UI", "Ignore Raycast", "Water");
            int mask = ~blocked;
            var positions = new List<Vector3>();
            var cells = new HashSet<Vector3Int>();
            int road = 0, side = 0, lamp = 0, shop = 0, insideGeometry = 0;

            System.Func<Vector3, float, bool> add = (p, cell) =>
            {
                var key = new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / 2.5f), Mathf.FloorToInt(p.z / cell));
                if (cell < 5f) key.x += 1 << 20; // fine cells (lamps, shops) never collide with coarse road cells
                if (cells.Contains(key)) return false;
                if (Physics.CheckSphere(p, 0.3f, mask, QueryTriggerInteraction.Ignore)) { insideGeometry++; return false; }
                cells.Add(key);
                positions.Add(p);
                return true;
            };

            var roads = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
            if (roads != null && roads.lanes != null)
            {
                foreach (TSLaneInfo lane in roads.lanes)
                {
                    if (lane == null || lane.points == null || lane.points.Length < 2) continue;
                    float since = RoadSampleSpacing;
                    for (int i = 0; i < lane.points.Length; i++)
                    {
                        Vector3 p = lane.points[i].point;
                        if (i > 0) since += Vector3.Distance(p, lane.points[i - 1].point);
                        if (since < RoadSampleSpacing) continue;
                        since = 0f;
                        Vector3 dir = lane.points[Mathf.Min(i + 1, lane.points.Length - 1)].point - lane.points[Mathf.Max(i - 1, 0)].point;
                        dir.y = 0f;
                        if (dir.sqrMagnitude < 0.01f) continue;
                        Vector3 right = Vector3.Cross(Vector3.up, dir.normalized);
                        if (add(p + Vector3.up * 1.2f, RoadCell)) road++;
                        if (add(p + Vector3.up * 5f, RoadCell)) road++;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Vector3 q = p + right * (SidewalkOffset * s);
                            if (Physics.Raycast(q + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 9f, mask, QueryTriggerInteraction.Ignore)
                                && Mathf.Abs(hit.point.y - p.y) < 2.5f && add(hit.point + Vector3.up * 1.2f, SidewalkCell)) side++;
                        }
                    }
                }
            }

            foreach (Vector3 head in controller.streetLightPositions ?? new Vector3[0])
            {
                if (Physics.Raycast(head + Vector3.down * 0.5f, Vector3.down, out RaycastHit hit, 9f, mask, QueryTriggerInteraction.Ignore)
                    && add(hit.point + Vector3.up * 1.2f, LampProbeCell)) lamp++;
            }

            foreach (var entrance in Object.FindObjectsByType<Interiors.ShopEntrance>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                Vector3 c = entrance.transform.position;
                for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                for (int h = 0; h < 2; h++)
                {
                    Vector3 p = c + new Vector3(x * 2.5f, 1.0f + h * 2.2f, z * 2.5f);
                    if (add(p, 2.4f)) shop++;
                }
            }

            group.probePositions = positions.ToArray();
            EditorUtility.SetDirty(group);
            return $"{positions.Count} (road {road}, sidewalk {side}, lamp {lamp}, gun shop {shop}; {insideGeometry} dropped inside geometry)";
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static BakedLightingSet CaptureSet(string mode, bool copyLightmaps)
        {
            EnsureFolder(SetsFolder);
            LightmapData[] maps = LightmapSettings.lightmaps;
            var color = new Texture2D[maps.Length];
            var direction = new Texture2D[0];
            if (copyLightmaps)
            {
                if (AssetDatabase.IsValidFolder(NightMapsFolder)) AssetDatabase.DeleteAsset(NightMapsFolder);
                EnsureFolder(NightMapsFolder);
            }
            // The packer can emit an atlas page no renderer uses (seen: an empty second 2048 page). A copy of it would cost a
            // full page of memory on device for nothing, so unused pages keep pointing at the scene's own texture.
            var used = new bool[maps.Length];
            foreach (Renderer r in layoutRenderers)
            {
                if (r != null && r.lightmapIndex >= 0 && r.lightmapIndex < used.Length) used[r.lightmapIndex] = true;
            }
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (terrain.lightmapIndex >= 0 && terrain.lightmapIndex < used.Length) used[terrain.lightmapIndex] = true;
            }
            for (int i = 0; i < maps.Length; i++)
            {
                color[i] = copyLightmaps && used[i] ? CopyTexture(maps[i].lightmapColor, mode) : maps[i].lightmapColor;
            }

            // Probe colours, raw float32.
            LightProbes probes = LightmapSettings.lightProbes;
            SphericalHarmonicsL2[] sh = probes != null ? probes.bakedProbes : new SphericalHarmonicsL2[0];
            Vector3 first = probes != null && probes.count > 0 ? probes.positions[0] : Vector3.zero;
            var bytes = new byte[sh.Length * BakedLightingSet.FloatsPerProbe * 4];
            int o = 0;
            for (int p = 0; p < sh.Length; p++)
            for (int rgb = 0; rgb < 3; rgb++)
            for (int c = 0; c < 9; c++)
            {
                System.Buffer.BlockCopy(System.BitConverter.GetBytes(sh[p][rgb, c]), 0, bytes, o, 4);
                o += 4;
            }
            string probePath = $"{SetsFolder}/{mode}_Probes.bytes";
            File.WriteAllBytes(probePath, bytes);
            AssetDatabase.ImportAsset(probePath);
            var probeAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(probePath);

            var index = new int[layoutRenderers.Length];
            var scaleOffset = new Vector4[layoutRenderers.Length];
            for (int i = 0; i < layoutRenderers.Length; i++)
            {
                index[i] = layoutRenderers[i] != null ? layoutRenderers[i].lightmapIndex : -1;
                scaleOffset[i] = layoutRenderers[i] != null ? layoutRenderers[i].lightmapScaleOffset : Vector4.zero;
            }

            string setPath = $"{SetsFolder}/{mode}.asset";
            var set = AssetDatabase.LoadAssetAtPath<BakedLightingSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<BakedLightingSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }
            string info = $"{mode} bake {System.DateTime.Now:yyyy-MM-dd HH:mm}: {maps.Length} lightmaps, {sh.Length} probes, {layoutRenderers.Length} lightmapped renderers, {TexelsPerMetre} texels/m.";
            set.EditorSet(color, direction, probeAsset, sh.Length, first, index, scaleOffset, info);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        private static Texture2D CopyTexture(Texture2D source, string prefix)
        {
            if (source == null) return null;
            string from = AssetDatabase.GetAssetPath(source);
            string to = $"{NightMapsFolder}/{prefix}_{Path.GetFileName(from)}";
            AssetDatabase.CopyAsset(from, to);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(to);
        }

        private static bool SameLayout(BakedLightingSet a, BakedLightingSet b)
        {
            if (a == null || b == null || a.LightmapCount != b.LightmapCount) return false;
            int[] ia = a.RendererLightmapIndex, ib = b.RendererLightmapIndex;
            Vector4[] sa = a.RendererScaleOffset, sb = b.RendererScaleOffset;
            if (ia.Length != ib.Length) return false;
            for (int i = 0; i < ia.Length; i++)
            {
                if (ia[i] != ib[i] || (sa[i] - sb[i]).sqrMagnitude > 1e-10f) return false;
            }
            return true;
        }

        private static void Wire(CityDayNightController controller, BakedLightingSet day, BakedLightingSet night, bool sameLayout)
        {
            var switcher = Object.FindFirstObjectByType<BakedLightingSwitcher>(FindObjectsInactive.Include);
            if (switcher == null)
            {
                Transform manager = null;
                foreach (GameObject go in controller.gameObject.scene.GetRootGameObjects())
                {
                    if (go.name == "///Manager///") manager = go.transform;
                }
                var host = new GameObject(SwitcherName);
                if (manager != null) host.transform.SetParent(manager, false);
                else UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, controller.gameObject.scene);
                switcher = host.AddComponent<BakedLightingSwitcher>();
            }
            var streetLights = new List<GameObject>();
            if (controller.streetLights != null)
            {
                foreach (Light light in controller.streetLights)
                {
                    if (light != null) streetLights.Add(light.gameObject);
                }
            }
            switcher.EditorSetup(controller, day, night, sameLayout ? new Renderer[0] : layoutRenderers, streetLights.ToArray());
            EditorUtility.SetDirty(switcher);
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static string Minutes(double seconds) => $"{seconds / 60.0:F1} min";

        private static void Status(string text)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StatusFile, text);
            Debug.Log("[DayNightLightBaker] " + text);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
