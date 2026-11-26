using System.Collections.Generic;
using System.IO;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Switches the gameplay scene between its baked lighting and a bright realtime look (both ways, re-runnable; nothing is
    /// baked or deleted).
    /// - Set Up: the scene stops using its LightingData (lightmaps, light probes) and the day / night bakes; the sun is fully
    ///   realtime with soft shadows; our own presets, sky, fog and post profiles (Assets/_Project/Lighting/Realtime) replace the
    ///   old ones in this scene only; the street lamps get fake ground pools and four pooled lights (NightStreetLights); small
    ///   props stop casting shadows at runtime (ShadowCasterBudget). What was there is kept in the backup asset.
    /// - Restore: puts every value of the backup back.
    /// - Reset Values: rewrites our presets / profiles / sky with the values below (Set Up keeps the ones tuned in the Inspector).
    /// </summary>
    public static class RealtimeLightingSetup
    {
        private const string Menu = "Tools/OpenworldCrime/Lighting/";
        private const string ScenePath = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";
        private const string Folder = "Assets/_Project/Lighting/Realtime";
        private const string BackupPath = "Assets/_Project/Editor/Lighting/OWC_BakedLightingBackup.asset";
        private const string PresetsPath = Folder + "/OWC_RealtimeDayNightPresets.asset";
        private const string NightSkyPath = Folder + "/OWC_RealtimeNightSky.mat";
        private const string SkyPath = Folder + "/CitySkyCycle_Bright.mat";
        private const string DayProfilePath = Folder + "/OWC_Bright_Day.asset";
        private const string NightProfilePath = Folder + "/OWC_Bright_Night.asset";
        private const string PoolMaterialPath = Folder + "/StreetLightPool.mat";
        private const string PoolMeshPath = Folder + "/StreetLightPools.asset";
        private const string PipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string LightingRoot = "OWC_Lighting";
        private const string ManagerRoot = "///Manager///";
        private const string RealtimeName = "Realtime Lighting";
        private const string PoolRootName = "Street Light Pools";

        // Shadows (Mobile_RPAsset).
        private const float ShadowDistance = 80f;
        private const int ShadowCascades = 2;
        private const int ShadowResolution = 2048;

        // Street lamp pools.
        private const float PoolRadius = 11f;
        private const int PoolGrid = 5;      // quads per side
        private const float PoolCell = 150f; // city cell per mesh (culling)
        private const float MaxStep = 0.6f;  // ground further above / below the lamp's foot than this is not lit

        private sealed class Targets
        {
            public Scene scene;
            public CityDayNightController controller;
            public CityAtmosphere atmosphere;
            public SkyCycleController sky;
            public BakedLightingSwitcher switcher;
            public ReflectionProbeDayNight probes;
            public SkyClouds clouds;
            public string error;
        }

        // ------------------------------------------------------------------------------------------------------ menu

        [MenuItem(Menu + "Set Up Realtime Bright Lighting")]
        public static void SetUpMenu() => Debug.Log(SetUp(false));

        [MenuItem(Menu + "Restore Baked Lighting")]
        public static void RestoreMenu() => Debug.Log(Restore());

        [MenuItem(Menu + "Reset Realtime Lighting Values")]
        public static void ResetMenu() => Debug.Log(SetUp(true));

        // ------------------------------------------------------------------------------------------------------ set up

        /// <summary>Applies the realtime lighting to the open gameplay scene (saves assets, marks the scene dirty).</summary>
        public static string SetUp(bool resetValues)
        {
            Targets t = Find();
            if (t.error != null) return t.error;
            EnsureFolder(Folder);
            RealtimeLightingBackup backup = EnsureBackup(t);
            if (backup == null) return "[RealtimeLighting] Could not write the backup; nothing changed.";

            Material sky = CopyAsset(backup.skyMaterial, SkyPath, out bool newSky);
            Material nightSky = CopyAsset(backup.presets.night.skybox, NightSkyPath, out _);
            CityDayNightPresets presets = CopyAsset(backup.presets, PresetsPath, out bool newPresets);
            if (newSky || resetValues) WriteSky(sky);
            if (newPresets || resetValues) WritePresets(presets, backup.presets);
            // Our own materials: the controller writes the day sky into the night one, which must not be Hamza's asset.
            presets.day.skybox = sky;
            presets.night.skybox = nightSky;
            EditorUtility.SetDirty(presets);
            VolumeProfile day = Profile(DayProfilePath, out bool newDay);
            VolumeProfile night = Profile(NightProfilePath, out bool newNight);
            if (newDay || resetValues) WriteDayProfile(day);
            if (newNight || resetValues) WriteNightProfile(night);
            AssetDatabase.SaveAssets();

            // Baked data out of this scene (the files stay).
            Set(t.switcher, so =>
            {
                so.FindProperty("daySet").objectReferenceValue = null;
                so.FindProperty("nightSet").objectReferenceValue = null;
                so.FindProperty("blendShader").objectReferenceValue = null;
                so.FindProperty("m_Enabled").boolValue = false;
            });
            Lightmapping.lightingDataAsset = null;
            Set(t.controller.sun, so => so.FindProperty("m_Lightmapping").intValue = (int)LightmapBakeType.Realtime);

            // Look.
            Set(t.controller, so => so.FindProperty("presets").objectReferenceValue = presets);
            Set(t.sky, so => so.FindProperty("skyMaterial").objectReferenceValue = sky);
            SetAtmosphere(t.atmosphere,
                new RealtimeLightingBackup.Fog { enabled = true, color = new Color(0.62f, 0.80f, 0.97f), start = 160f, end = 950f },
                new RealtimeLightingBackup.Fog { enabled = true, color = new Color(0.085f, 0.105f, 0.19f), start = 100f, end = 700f });
            SetProfiles(t.atmosphere, day, night);
            SetClouds(t.clouds, BrightClouds());

            // Street lamps and shadow budget.
            BuildNightLights(t);
            SetPipeline(true, (int)SoftShadowQuality.Low, ShadowDistance, ShadowCascades, ShadowResolution);

            t.controller.Apply(false);
            EditorSceneManager.MarkSceneDirty(t.scene);
            AssetDatabase.SaveAssets();
            return $"[RealtimeLighting] Realtime bright lighting set up in {t.scene.name}. Lightmaps in use: {LightmapSettings.lightmaps.Length}. Save the scene to keep it; '{Menu}Restore Baked Lighting' undoes it.";
        }

        // ------------------------------------------------------------------------------------------------------ restore

        /// <summary>Puts the baked lighting back in the open gameplay scene (marks the scene dirty).</summary>
        public static string Restore()
        {
            Targets t = Find();
            if (t.error != null) return t.error;
            var backup = AssetDatabase.LoadAssetAtPath<RealtimeLightingBackup>(BackupPath);
            if (backup == null) return "[RealtimeLighting] No backup: the realtime lighting was never set up.";

            Lightmapping.lightingDataAsset = backup.lightingData;
            Set(t.switcher, so =>
            {
                so.FindProperty("daySet").objectReferenceValue = backup.daySet;
                so.FindProperty("nightSet").objectReferenceValue = backup.nightSet;
                so.FindProperty("blendShader").objectReferenceValue = backup.blendShader;
                so.FindProperty("m_Enabled").boolValue = backup.switcherEnabled;
            });
            if (t.probes != null) Set(t.probes, so => so.FindProperty("m_Enabled").boolValue = backup.probeSwitcherEnabled);
            Set(t.controller.sun, so => so.FindProperty("m_Lightmapping").intValue = backup.sunLightmapping);
            if (!backup.sunLightmappingWasOverride) Revert(t.controller.sun, "m_Lightmapping");
            Set(t.controller, so => so.FindProperty("presets").objectReferenceValue = backup.presets);
            if (!backup.presetsWereOverride) Revert(t.controller, "presets");
            Set(t.sky, so => so.FindProperty("skyMaterial").objectReferenceValue = backup.skyMaterial);
            SetAtmosphere(t.atmosphere, backup.dayFog, backup.nightFog);
            SetProfiles(t.atmosphere, backup.dayProfile, backup.nightProfile);
            SetClouds(t.clouds, backup.clouds);
            RemoveNightLights(t);
            SetPipeline(backup.softShadows, backup.softShadowQuality, backup.shadowDistance, backup.cascades, backup.shadowResolution);

            t.controller.Apply(false);
            EditorSceneManager.MarkSceneDirty(t.scene);
            AssetDatabase.SaveAssets();
            return $"[RealtimeLighting] Baked lighting restored in {t.scene.name}. Lightmaps in use: {LightmapSettings.lightmaps.Length}. Save the scene to keep it.";
        }

        // ------------------------------------------------------------------------------------------------------ look values

        private static void WritePresets(CityDayNightPresets p, CityDayNightPresets source)
        {
            EditorUtility.CopySerialized(source, p); // window, sign and venue glow stay as authored
            p.name = Path.GetFileNameWithoutExtension(PresetsPath);

            CityLightingLook d = p.day;
            d.lightRotation = new Vector3(48f, source.day.lightRotation.y, 0f);
            d.lightColor = new Color(1f, 0.94f, 0.80f);
            d.lightIntensity = 1.3f;
            d.shadows = LightShadows.Soft;
            d.shadowStrength = 0.82f;
            d.sceneBrightness = 1f;
            d.ambientMode = AmbientMode.Trilight;
            d.ambientSky = new Color(0.56f, 0.66f, 0.84f);
            d.ambientEquator = new Color(0.52f, 0.54f, 0.58f);
            d.ambientGround = new Color(0.38f, 0.35f, 0.31f);
            d.reflectionIntensity = 0.8f;

            CityLightingLook n = p.night;
            n.lightColor = new Color(0.66f, 0.76f, 1f);
            n.lightIntensity = 0.5f;
            n.sceneBrightness = 1.2f;
            n.shadows = LightShadows.Soft;
            n.shadowStrength = 0.65f;
            n.ambientSky = new Color(0.22f, 0.27f, 0.45f);
            n.ambientEquator = new Color(0.17f, 0.19f, 0.32f);
            n.ambientGround = new Color(0.11f, 0.11f, 0.15f);
            p.nearbyStreetLights = 0; // NightStreetLights lights the nearest lamps
            EditorUtility.SetDirty(p);
        }

        private static void WriteSky(Material m)
        {
            m.SetFloat("_CubeExposure", 0.9f);
            m.SetFloat("_CloudMix", 0.08f);
            m.SetColor("_MorningTint", new Color(0.82f, 0.90f, 1.05f));
            m.SetColor("_MorningZenith", new Color(0.22f, 0.47f, 0.88f));
            m.SetColor("_MorningGlow", new Color(0.95f, 0.80f, 0.64f));
            m.SetColor("_NoonTint", new Color(0.55f, 0.85f, 1.35f));
            m.SetColor("_NoonZenith", new Color(0.18f, 0.50f, 0.98f));
            m.SetColor("_NoonGlow", new Color(1.75f, 2.2f, 2.6f));
            m.SetColor("_EveningTint", new Color(0.98f, 0.80f, 0.68f));
            m.SetColor("_EveningZenith", new Color(0.24f, 0.36f, 0.66f));
            m.SetColor("_EveningGlow", new Color(1.0f, 0.64f, 0.36f));
            m.SetColor("_NightHorizon", new Color(0.12f, 0.17f, 0.28f));
            m.SetColor("_NightZenith", new Color(0.035f, 0.065f, 0.15f));
            EditorUtility.SetDirty(m);
        }

        private static RealtimeLightingBackup.CloudLook[] BrightClouds()
        {
            return new[]
            {
                Cloud(new Color(1f, 0.97f, 0.93f), new Color(0.82f, 0.86f, 0.94f), new Color(1f, 0.88f, 0.72f), 0.75f, 0.44f),
                Cloud(Color.white, new Color(0.88f, 0.92f, 0.98f), new Color(1f, 0.97f, 0.90f), 0.8f, 0.42f),
                Cloud(new Color(1f, 0.80f, 0.62f), new Color(0.56f, 0.50f, 0.62f), new Color(1f, 0.68f, 0.42f), 0.8f, 0.46f),
                Cloud(new Color(0.20f, 0.24f, 0.34f), new Color(0.07f, 0.085f, 0.13f), new Color(0.35f, 0.42f, 0.58f), 0.7f, 0.46f)
            };
        }

        private static RealtimeLightingBackup.CloudLook Cloud(Color lit, Color shade, Color glow, float opacity, float coverage)
        {
            return new RealtimeLightingBackup.CloudLook { lit = lit, shade = shade, sunGlow = glow, opacity = opacity, coverage = coverage };
        }

        // Neutral tonemapping keeps colours saturated (ACES greys and darkens them). Everything lands in URP's one LUT.
        private static void WriteDayProfile(VolumeProfile p)
        {
            Clear(p);
            Add<Tonemapping>(p).mode.Override(TonemappingMode.Neutral);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(30f);
            color.colorFilter.Override(Color.white);
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(5f);
            white.tint.Override(0f);
            AddBloom(p, 1f, 0.15f, 0.6f, Color.white);
            AddVignette(p, 0.12f);
            Save(p);
        }

        private static void WriteNightProfile(VolumeProfile p)
        {
            Clear(p);
            Add<Tonemapping>(p).mode.Override(TonemappingMode.Neutral);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(0.7f);
            color.contrast.Override(12f);
            color.saturation.Override(20f);
            color.colorFilter.Override(Color.white);
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(-6f);
            white.tint.Override(2f);
            AddBloom(p, 0.9f, 0.6f, 0.68f, new Color(1f, 0.9f, 0.95f));
            AddVignette(p, 0.2f);
            Save(p);
        }

        private static void AddBloom(VolumeProfile p, float threshold, float intensity, float scatter, Color tint)
        {
            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(threshold);
            bloom.intensity.Override(intensity);
            bloom.scatter.Override(scatter);
            bloom.tint.Override(tint);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);
        }

        private static void AddVignette(VolumeProfile p, float intensity)
        {
            var vignette = Add<Vignette>(p);
            vignette.intensity.Override(intensity);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
        }

        // ------------------------------------------------------------------------------------------------------ street lamps

        private static void BuildNightLights(Targets t)
        {
            Transform root = FindRoot(t.scene, LightingRoot);
            Transform manager = FindRoot(t.scene, ManagerRoot);
            Transform host = Child(manager, RealtimeName);
            Transform poolRoot = Child(root, PoolRootName);

            var lights = new List<Light>();
            for (int i = 0; i < 4; i++)
            {
                Transform l = Child(host, "Street Light " + (i + 1));
                var light = l.GetComponent<Light>();
                if (light == null) light = l.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.enabled = false;
                EditorUtility.SetDirty(light);
                lights.Add(light);
            }

            for (int i = poolRoot.childCount - 1; i >= 0; i--) Object.DestroyImmediate(poolRoot.GetChild(i).gameObject);
            var renderers = new List<Renderer>();
            foreach (Mesh mesh in BuildPoolMeshes(t.controller.streetLightPositions))
            {
                var go = new GameObject(mesh.name);
                go.transform.SetParent(poolRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = PoolMaterial();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.enabled = false;
                renderers.Add(r);
            }

            var night = host.GetComponent<NightStreetLights>();
            if (night == null) night = host.gameObject.AddComponent<NightStreetLights>();
            night.EditorSetup(t.controller, lights.ToArray(), renderers.ToArray());
            EditorUtility.SetDirty(night);
            var budget = host.GetComponent<ShadowCasterBudget>();
            if (budget == null) budget = host.gameObject.AddComponent<ShadowCasterBudget>();
            Transform env = FindRoot(t.scene, "env");
            budget.EditorSetup(env != null ? new[] { env } : new Transform[0]);
            EditorUtility.SetDirty(budget);
        }

        private static void RemoveNightLights(Targets t)
        {
            Transform manager = FindRoot(t.scene, ManagerRoot);
            Transform host = manager.Find(RealtimeName);
            if (host != null) Object.DestroyImmediate(host.gameObject);
            Transform root = FindRoot(t.scene, LightingRoot);
            Transform pools = root.Find(PoolRootName);
            if (pools != null) Object.DestroyImmediate(pools.gameObject);
        }

        // A small grid per lamp, laid on the ground colliders under it; cells make one mesh each.
        private static List<Mesh> BuildPoolMeshes(Vector3[] lamps)
        {
            var cells = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (Vector3 lamp in lamps)
            {
                var key = new Vector2Int(Mathf.FloorToInt(lamp.x / PoolCell), Mathf.FloorToInt(lamp.z / PoolCell));
                if (!cells.TryGetValue(key, out List<Vector3> list)) cells[key] = list = new List<Vector3>();
                list.Add(lamp);
            }
            foreach (Object old in AssetDatabase.LoadAllAssetsAtPath(PoolMeshPath)) if (old is Mesh) Object.DestroyImmediate(old, true);
            if (!File.Exists(PoolMeshPath)) AssetDatabase.CreateAsset(new Mesh { name = "StreetLightPools" }, PoolMeshPath);

            var result = new List<Mesh>();
            foreach (KeyValuePair<Vector2Int, List<Vector3>> cell in cells)
            {
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                foreach (Vector3 lamp in cell.Value) AddPool(lamp, vertices, uvs, triangles);
                if (triangles.Count == 0) continue;
                var mesh = new Mesh { name = $"Pools {cell.Key.x} {cell.Key.y}" };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, PoolMeshPath);
                result.Add(mesh);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void AddPool(Vector3 lamp, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            if (!Ground(lamp, lamp.y, out float foot) || lamp.y - foot > 20f) return;
            int first = vertices.Count;
            int side = PoolGrid + 1;
            var valid = new bool[side * side];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                var uv = new Vector2(x / (float)PoolGrid * 2f - 1f, z / (float)PoolGrid * 2f - 1f);
                var p = new Vector3(lamp.x + uv.x * PoolRadius, 0f, lamp.z + uv.y * PoolRadius);
                bool hit = Ground(p, lamp.y, out float y);
                float slope = uv.magnitude * PoolRadius * 0.12f;
                valid[z * side + x] = hit && Mathf.Abs(y - foot) <= MaxStep + slope;
                p.y = valid[z * side + x] ? y : foot;
                vertices.Add(p + Vector3.up * 0.04f);
                uvs.Add(uv);
            }
            for (int z = 0; z < PoolGrid; z++)
            for (int x = 0; x < PoolGrid; x++)
            {
                int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                // Whole cell on the ground and inside the circle.
                if (!valid[a] || !valid[b] || !valid[c] || !valid[d]) continue;
                if (uvs[first + a].magnitude >= 1f && uvs[first + b].magnitude >= 1f && uvs[first + c].magnitude >= 1f && uvs[first + d].magnitude >= 1f) continue;
                triangles.Add(first + a); triangles.Add(first + c); triangles.Add(first + b);
                triangles.Add(first + b); triangles.Add(first + c); triangles.Add(first + d);
            }
        }

        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        private static bool Ground(Vector3 at, float fromY, out float y)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(at.x, fromY - 0.6f, at.z), Vector3.down, GroundHits, 25f, 1, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (GroundHits[i].collider.attachedRigidbody != null || GroundHits[i].normal.y < 0.7f) continue;
                best = Mathf.Max(best, GroundHits[i].point.y);
            }
            y = best;
            return best > float.MinValue;
        }

        private static Material PoolMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(PoolMaterialPath);
            if (m != null) return m;
            m = new Material(Shader.Find("OpenworldCrime/Street Light Pool"));
            AssetDatabase.CreateAsset(m, PoolMaterialPath);
            return m;
        }

        // ------------------------------------------------------------------------------------------------------ backup

        // Written once, before the first set up; a later run (already realtime) keeps it.
        private static RealtimeLightingBackup EnsureBackup(Targets t)
        {
            var backup = AssetDatabase.LoadAssetAtPath<RealtimeLightingBackup>(BackupPath);
            bool alreadyRealtime = AssetDatabase.GetAssetPath(t.controller.presets) == PresetsPath;
            if (backup != null && alreadyRealtime) return backup;
            if (backup == null && alreadyRealtime) return null;
            bool create = backup == null;
            if (create) backup = ScriptableObject.CreateInstance<RealtimeLightingBackup>();

            backup.scenePath = t.scene.path;
            backup.lightingData = Lightmapping.lightingDataAsset;
            backup.presets = t.controller.presets;
            var sun = new SerializedObject(t.controller.sun).FindProperty("m_Lightmapping");
            backup.sunLightmapping = sun.intValue;
            backup.sunLightmappingWasOverride = sun.prefabOverride;
            backup.presetsWereOverride = new SerializedObject(t.controller).FindProperty("presets").prefabOverride;
            var sw = new SerializedObject(t.switcher);
            backup.switcherEnabled = t.switcher.enabled;
            backup.daySet = sw.FindProperty("daySet").objectReferenceValue as BakedLightingSet;
            backup.nightSet = sw.FindProperty("nightSet").objectReferenceValue as BakedLightingSet;
            backup.blendShader = sw.FindProperty("blendShader").objectReferenceValue as Shader;
            backup.probeSwitcherEnabled = t.probes != null && t.probes.enabled;
            var at = new SerializedObject(t.atmosphere);
            backup.dayFog = ReadFog(at.FindProperty("dayFog"));
            backup.nightFog = ReadFog(at.FindProperty("nightFog"));
            backup.dayProfile = ((Volume)at.FindProperty("dayVolume").objectReferenceValue).sharedProfile;
            backup.nightProfile = ((Volume)at.FindProperty("nightVolume").objectReferenceValue).sharedProfile;
            backup.skyMaterial = new SerializedObject(t.sky).FindProperty("skyMaterial").objectReferenceValue as Material;
            var cl = new SerializedObject(t.clouds);
            backup.clouds = new RealtimeLightingBackup.CloudLook[CloudFields.Length];
            for (int i = 0; i < CloudFields.Length; i++)
            {
                SerializedProperty p = cl.FindProperty(CloudFields[i]);
                backup.clouds[i] = Cloud(p.FindPropertyRelative("lit").colorValue, p.FindPropertyRelative("shade").colorValue,
                    p.FindPropertyRelative("sunGlow").colorValue, p.FindPropertyRelative("opacity").floatValue, p.FindPropertyRelative("coverage").floatValue);
            }
            var rp = new SerializedObject(Pipeline());
            backup.softShadows = rp.FindProperty("m_SoftShadowsSupported").boolValue;
            backup.softShadowQuality = rp.FindProperty("m_SoftShadowQuality").intValue;
            backup.shadowDistance = rp.FindProperty("m_ShadowDistance").floatValue;
            backup.cascades = rp.FindProperty("m_ShadowCascadeCount").intValue;
            backup.shadowResolution = rp.FindProperty("m_MainLightShadowmapResolution").intValue;
            if (create) AssetDatabase.CreateAsset(backup, BackupPath);
            EditorUtility.SetDirty(backup);
            AssetDatabase.SaveAssets();
            return backup;
        }

        // ------------------------------------------------------------------------------------------------------ helpers

        private static readonly string[] CloudFields = { "morning", "noon", "evening", "night" };

        private static Targets Find()
        {
            var t = new Targets { scene = SceneManager.GetActiveScene() };
            if (EditorApplication.isPlayingOrWillChangePlaymode) { t.error = "[RealtimeLighting] Leave play mode first."; return t; }
            if (t.scene.path != ScenePath || SceneManager.sceneCount != 1) { t.error = "[RealtimeLighting] Open " + ScenePath + " (only)."; return t; }
            t.controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            t.atmosphere = Object.FindFirstObjectByType<CityAtmosphere>(FindObjectsInactive.Include);
            t.sky = Object.FindFirstObjectByType<SkyCycleController>(FindObjectsInactive.Include);
            t.switcher = Object.FindFirstObjectByType<BakedLightingSwitcher>(FindObjectsInactive.Include);
            t.probes = Object.FindFirstObjectByType<ReflectionProbeDayNight>(FindObjectsInactive.Include);
            t.clouds = Object.FindFirstObjectByType<SkyClouds>(FindObjectsInactive.Include);
            if (t.controller == null || t.controller.sun == null || t.controller.presets == null || t.atmosphere == null || t.sky == null || t.switcher == null || t.clouds == null
                || FindRoot(t.scene, LightingRoot) == null || FindRoot(t.scene, ManagerRoot) == null || Pipeline() == null)
                t.error = "[RealtimeLighting] The scene is missing one of its lighting systems; nothing changed.";
            return t;
        }

        private static UniversalRenderPipelineAsset Pipeline() => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);

        private static void SetPipeline(bool soft, int quality, float distance, int cascades, int resolution)
        {
            Set(Pipeline(), so =>
            {
                so.FindProperty("m_SoftShadowsSupported").boolValue = soft;
                so.FindProperty("m_SoftShadowQuality").intValue = quality;
                so.FindProperty("m_ShadowDistance").floatValue = distance;
                so.FindProperty("m_ShadowCascadeCount").intValue = cascades;
                so.FindProperty("m_MainLightShadowmapResolution").intValue = resolution;
            });
        }

        private static void Set(Object target, System.Action<SerializedObject> write)
        {
            var so = new SerializedObject(target);
            write(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void Revert(Object target, string property)
        {
            SerializedProperty p = new SerializedObject(target).FindProperty(property);
            if (p != null && p.prefabOverride) PrefabUtility.RevertPropertyOverride(p, InteractionMode.AutomatedAction);
        }

        private static RealtimeLightingBackup.Fog ReadFog(SerializedProperty p)
        {
            return new RealtimeLightingBackup.Fog
            {
                enabled = p.FindPropertyRelative("enabled").boolValue, color = p.FindPropertyRelative("color").colorValue,
                start = p.FindPropertyRelative("startDistance").floatValue, end = p.FindPropertyRelative("endDistance").floatValue
            };
        }

        private static void SetAtmosphere(CityAtmosphere atmosphere, RealtimeLightingBackup.Fog day, RealtimeLightingBackup.Fog night)
        {
            Set(atmosphere, so => { WriteFog(so.FindProperty("dayFog"), day); WriteFog(so.FindProperty("nightFog"), night); });
        }

        private static void WriteFog(SerializedProperty p, RealtimeLightingBackup.Fog f)
        {
            p.FindPropertyRelative("enabled").boolValue = f.enabled;
            p.FindPropertyRelative("color").colorValue = f.color;
            p.FindPropertyRelative("startDistance").floatValue = f.start;
            p.FindPropertyRelative("endDistance").floatValue = f.end;
        }

        private static void SetProfiles(CityAtmosphere atmosphere, VolumeProfile day, VolumeProfile night)
        {
            var so = new SerializedObject(atmosphere);
            SetProfile((Volume)so.FindProperty("dayVolume").objectReferenceValue, day);
            SetProfile((Volume)so.FindProperty("nightVolume").objectReferenceValue, night);
        }

        private static void SetProfile(Volume volume, VolumeProfile profile)
        {
            if (volume == null) return;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        private static void SetClouds(SkyClouds clouds, RealtimeLightingBackup.CloudLook[] looks)
        {
            Set(clouds, so =>
            {
                for (int i = 0; i < CloudFields.Length && i < looks.Length; i++)
                {
                    SerializedProperty p = so.FindProperty(CloudFields[i]);
                    p.FindPropertyRelative("lit").colorValue = looks[i].lit;
                    p.FindPropertyRelative("shade").colorValue = looks[i].shade;
                    p.FindPropertyRelative("sunGlow").colorValue = looks[i].sunGlow;
                    p.FindPropertyRelative("opacity").floatValue = looks[i].opacity;
                    p.FindPropertyRelative("coverage").floatValue = looks[i].coverage;
                }
            });
        }

        private static T CopyAsset<T>(T source, string path, out bool created) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            created = existing == null;
            if (!created) return existing;
            T copy = Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        private static VolumeProfile Profile(string path, out bool created)
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            created = p == null;
            if (!created) return p;
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            return p;
        }

        private static void Clear(VolumeProfile p)
        {
            foreach (VolumeComponent c in p.components) if (c != null) Object.DestroyImmediate(c, true);
            p.components.Clear();
        }

        private static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            T c = p.Add<T>(false);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        private static void Save(VolumeProfile p)
        {
            p.isDirty = true;
            EditorUtility.SetDirty(p);
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == name) return go.transform;
            return null;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
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
