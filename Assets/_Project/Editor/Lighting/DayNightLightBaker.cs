using System.Collections.Generic;
using System.IO;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Bakes the city twice - day and night - and wires both lightmap sets into a <see cref="BakedLightingSwitcher"/>, which
    /// CityDayNightController switches together with the street lamps.
    /// Mode: Baked Indirect. The sun/moon stays a realtime light (direct light and moving shadows for the time-of-day cycle);
    /// the bake adds what realtime can't afford on a phone: sky light and sun/moon bounce, ambient occlusion in corners, and at
    /// night warm light pools from every street lamp (baked-only point lights, no runtime cost).
    /// The day set is copied to Assets/_Project/Lighting/Day; the night bake stays in the scene's own lighting folder, so only
    /// two sets exist. Menu: Tools > OpenworldCrime > Lighting > Bake Day And Night. Runs asynchronously (progress in the
    /// status bar and Temp/daynight_bake_status.txt).
    /// </summary>
    public static class DayNightLightBaker
    {
        private const string SettingsPath = "Assets/_Project/Lighting/OWC_DayNight.lighting";
        private const string DayFolder = "Assets/_Project/Lighting/Day";
        private const string StatusFile = "Temp/daynight_bake_status.txt";
        private const string LampRootName = "BakedStreetLamps";
        private const float DayBakeBounce = 0.75f;

        private enum Step { Idle, Day, Night }
        private static Step step = Step.Idle;
        private static float startTime;

        [MenuItem("Tools/OpenworldCrime/Lighting/Bake Day And Night")]
        public static void BakeMenu() => Debug.Log(Start());

        public static string Start()
        {
            if (Lightmapping.isRunning || step != Step.Idle) return "[DayNightLightBaker] A bake is already running.";
            if (EditorApplication.isPlaying) return "[DayNightLightBaker] Stop play mode first.";
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller == null) return "[DayNightLightBaker] No CityDayNightController in the open scene.";

            Lightmapping.lightingSettings = GetSettings();
            int lamps = BuildLamps();
            SetLampsActive(false);
            controller.Apply(false);
            // The day preset's sun is strong (2.5) with a 1.5x bounce; baked as is, that bounce plus the realtime sun on top
            // double-lights open ground and the streets wash out to white. The bake uses a gentler bounce (the preset itself
            // is unchanged; Apply() restores it afterwards).
            if (controller.sun != null) controller.sun.bounceIntensity = DayBakeBounce;
            step = Step.Day;
            startTime = (float)EditorApplication.timeSinceStartup;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            Status("day bake started");
            if (!Lightmapping.BakeAsync())
            {
                Abort("could not start the day bake");
                return "[DayNightLightBaker] Could not start the bake.";
            }
            return $"[DayNightLightBaker] Baking day, then night ({lamps} street lamps). Progress: status bar / {StatusFile}.";
        }

        private static void OnBakeCompleted()
        {
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            var switcher = GetSwitcher(controller);
            if (step == Step.Day)
            {
                // Keep the day result: copy its lightmaps (the night bake will overwrite the scene's lighting folder).
                switcher.EditorSetRenderers(LightmappedRenderers());
                switcher.EditorSetSet(false, CaptureSet(switcher.EditorRenderers, true));
                Status($"day done ({LightmapSettings.lightmaps.Length} lightmaps, {Elapsed()}); night bake started");
                SetLampsActive(true);
                controller.Apply(true);
                step = Step.Night;
                if (!Lightmapping.BakeAsync()) Abort("could not start the night bake");
                return;
            }
            if (step == Step.Night)
            {
                switcher.EditorSetSet(true, CaptureSet(switcher.EditorRenderers, false));
                SetLampsActive(false); // baked-only lights: their light is in the night lightmaps, no runtime cost
                controller.Apply(false);
                SerializedObject so = new SerializedObject(controller);
                so.FindProperty("bakedLighting").objectReferenceValue = switcher;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(switcher);
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                EditorSceneManager.SaveScene(controller.gameObject.scene);
                Status($"done: {LightmapSettings.lightmaps.Length} night lightmaps, total {Elapsed()}");
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                step = Step.Idle;
            }
        }

        private static void Abort(string why)
        {
            Status("FAILED: " + why);
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            step = Step.Idle;
        }

        private static string Elapsed() => $"{((float)EditorApplication.timeSinceStartup - startTime) / 60f:F1} min";

        private static void Status(string text)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StatusFile, text);
            Debug.Log("[DayNightLightBaker] " + text);
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static LightingSettings GetSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (s == null)
            {
                EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
                s = new LightingSettings { name = "OWC_DayNight" };
                AssetDatabase.CreateAsset(s, SettingsPath);
            }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.IndirectOnly;   // sun direct + shadows stay realtime (moving sun)
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.lightmapResolution = 2f;                          // bounce/sky/lamp light is soft: 2 texels per metre over a whole city
            s.lightmapPadding = 2;
            s.lightmapMaxSize = 2048;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            s.directionalityMode = LightmapsMode.NonDirectional; // half the memory; normal maps still get realtime sun detail
            s.ao = true;
            s.aoMaxDistance = 1.5f;
            s.aoExponentIndirect = 1f;
            s.aoExponentDirect = 0f;
            s.maxBounces = 2;
            s.directSampleCount = 32;
            s.indirectSampleCount = 256;
            s.environmentSampleCount = 128;
            s.filteringMode = LightingSettings.FilterMode.Auto;
            s.prioritizeView = false;
            s.lightmapMaxSize = 2048;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            return s;
        }

        private static BakedLightingSwitcher GetSwitcher(CityDayNightController controller)
        {
            var switcher = Object.FindFirstObjectByType<BakedLightingSwitcher>(FindObjectsInactive.Include);
            if (switcher != null) return switcher;
            var go = new GameObject("BakedLighting");
            go.transform.SetParent(controller.transform.parent, false);
            return go.AddComponent<BakedLightingSwitcher>();
        }

        private static Renderer[] LightmappedRenderers()
        {
            var list = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
                if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) list.Add(r);
            return list.ToArray();
        }

        private static BakedLightingSwitcher.LightmapSet CaptureSet(Renderer[] renderers, bool copyToDayFolder)
        {
            var set = new BakedLightingSwitcher.LightmapSet();
            LightmapData[] maps = LightmapSettings.lightmaps;
            set.color = new Texture2D[maps.Length];
            set.directional = new Texture2D[maps.Length];
            if (copyToDayFolder)
            {
                if (AssetDatabase.IsValidFolder(DayFolder)) AssetDatabase.DeleteAsset(DayFolder);
                EnsureFolder(DayFolder);
            }
            for (int i = 0; i < maps.Length; i++)
            {
                set.color[i] = copyToDayFolder ? CopyTexture(maps[i].lightmapColor) : maps[i].lightmapColor;
                set.directional[i] = maps[i].lightmapDir != null && copyToDayFolder ? CopyTexture(maps[i].lightmapDir) : maps[i].lightmapDir;
            }
            set.rendererIndex = new int[renderers.Length];
            set.rendererScaleOffset = new Vector4[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                set.rendererIndex[i] = renderers[i] != null ? renderers[i].lightmapIndex : -1;
                set.rendererScaleOffset[i] = renderers[i] != null ? renderers[i].lightmapScaleOffset : Vector4.zero;
            }
            return set;
        }

        private static Texture2D CopyTexture(Texture2D source)
        {
            if (source == null) return null;
            string from = AssetDatabase.GetAssetPath(source);
            string to = $"{DayFolder}/Day_{Path.GetFileName(from)}";
            AssetDatabase.CopyAsset(from, to);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(to);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Baked-only point light at the head of every street lamp (arm end, just under the lamp housing).
        private static int BuildLamps()
        {
            // The lamp root is kept inactive between bakes, which GameObject.Find does not see.
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == LampRootName) Object.DestroyImmediate(t.gameObject);
            var root = new GameObject(LampRootName);
            var heads = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                string n = t.name.ToLowerInvariant();
                if (!n.StartsWith("street light")) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                if (b.size.y < 4f) continue;
                Vector3 arm = new Vector3(b.size.x, 0f, b.size.z);
                Vector3 axis = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
                float half = Mathf.Max(b.size.x, b.size.z) * 0.5f - 0.15f;
                float y = b.max.y - 0.35f;
                Vector3 toCenter = b.center - t.position; toCenter.y = 0f;
                if (Mathf.Abs(Vector3.Dot(toCenter, axis)) < 0.2f)
                {
                    // Pole in the middle: lamps at both arm ends.
                    heads.Add(new Vector3(b.center.x, y, b.center.z) + axis * half);
                    heads.Add(new Vector3(b.center.x, y, b.center.z) - axis * half);
                }
                else
                {
                    float sign = Mathf.Sign(Vector3.Dot(toCenter, axis));
                    heads.Add(new Vector3(b.center.x, y, b.center.z) + axis * (sign * half));
                }
            }
            foreach (Vector3 p in heads)
            {
                var go = new GameObject("Lamp");
                go.transform.SetParent(root.transform, false);
                go.transform.position = p;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.range = 11f;
                light.intensity = 3.2f;
                light.color = new Color(1f, 0.78f, 0.52f);
                light.shadows = LightShadows.Soft;
                light.shadowRadius = 0.25f;
            }
            return heads.Count;
        }

        private static void SetLampsActive(bool active)
        {
            var root = GameObject.Find(LampRootName);
            if (root == null)
            {
                foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (go.name == LampRootName && go.parent == null) { root = go.gameObject; break; }
            }
            if (root != null) root.SetActive(active);
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
