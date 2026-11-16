using System.IO;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds our own post-processing profiles (Assets/_Project/Settings/PostProcessing) and wires the city's Day / Night
    /// volumes and <see cref="CityAtmosphere"/> into the open gameplay scene. Re-running rebuilds the profiles with the values
    /// below (they are plain assets; tune them in the Inspector afterwards if preferred).
    ///
    /// Mobile notes: Mobile_RPAsset grades in Low Dynamic Range, where URP has no tonemapping step (the LDR LUT builder has no
    /// tonemapper), so the look is built from colour adjustments, white balance and lift / gamma / gain, which all bake into
    /// the one 32^3 LUT URP already builds - their cost is the same as one. Tonemapping is explicitly None so the editor and
    /// other pipeline assets show the same image as the phone. Bloom runs at quarter resolution with few iterations.
    /// Menu: Tools > OpenworldCrime > Lighting > Setup City Post Processing.
    /// </summary>
    public static class CityPostProcessSetup
    {
        public const string Folder = "Assets/_Project/Settings/PostProcessing";
        private const string LightingRootName = "OWC_Lighting";
        private const string SwitcherName = "CityLighting";
        private const int DayPriority = 110;   // above the scene's Global / Outdoor volumes and Hamza's Night Effects (100)
        private const int NightPriority = 120;

        [MenuItem("Tools/OpenworldCrime/Lighting/Setup City Post Processing")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller == null) return "[CityPostProcessSetup] Open the gameplay scene (no CityDayNightController).";
            VolumeProfile day = BuildDayProfile();
            VolumeProfile night = BuildNightProfile();

            var scene = controller.gameObject.scene;
            Transform root = FindRoot(scene, LightingRootName, true);
            Volume dayVolume = GetVolume(root, "PostProcess Day", day, DayPriority, 1f);
            Volume nightVolume = GetVolume(root, "PostProcess Night", night, NightPriority, 0f);

            Transform manager = FindRoot(scene, "///Manager///", false);
            Transform host = manager != null ? manager.Find(SwitcherName) : null;
            if (host == null)
            {
                host = new GameObject(SwitcherName).transform;
                if (manager != null) host.SetParent(manager, false);
            }
            var atmosphere = host.GetComponent<CityAtmosphere>();
            if (atmosphere == null) atmosphere = host.gameObject.AddComponent<CityAtmosphere>();
            atmosphere.EditorSetup(controller, dayVolume, nightVolume);
            EditorUtility.SetDirty(atmosphere);

            string cameras = EnableCameraPostProcessing(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            return $"[CityPostProcessSetup] profiles: {AssetDatabase.GetAssetPath(day)}, {AssetDatabase.GetAssetPath(night)}; volumes under {LightingRootName}; CityAtmosphere on ///Manager///{SwitcherName}; cameras: {cameras}";
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static VolumeProfile BuildDayProfile()
        {
            VolumeProfile p = NewProfile("OWC_City_Day");
            Add<Tonemapping>(p).mode.Override(TonemappingMode.None);
            // The day preset is already a warm golden-hour light: a gentle contrast / saturation lift is enough. postExposure
            // replaces the Outdoor volume's +0.25 (this volume has the higher priority), slightly lower to hold the facades.
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(0.2f);
            color.contrast.Override(6f);
            color.saturation.Override(10f);
            color.colorFilter.Override(Color.white);
            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);
            var vignette = Add<Vignette>(p);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            return Save(p);
        }

        private static VolumeProfile BuildNightProfile()
        {
            VolumeProfile p = NewProfile("OWC_City_Night");
            Add<Tonemapping>(p).mode.Override(TonemappingMode.None);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(0.1f);
            color.contrast.Override(12f);
            color.saturation.Override(12f);
            color.colorFilter.Override(new Color(0.96f, 0.98f, 1f));
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(-8f);
            white.tint.Override(4f);
            var lgg = Add<LiftGammaGain>(p);
            lgg.lift.Override(new Vector4(0.97f, 0.99f, 1.05f, -0.03f));
            lgg.gamma.Override(new Vector4(1f, 1f, 1.02f, 0f));
            lgg.gain.Override(new Vector4(1.03f, 1f, 0.97f, 0.02f));
            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.85f));
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);
            var vignette = Add<Vignette>(p);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            return Save(p);
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static VolumeProfile NewProfile(string name)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (sub is VolumeComponent) Object.DestroyImmediate(sub, true);
            }
            profile.components.Clear();
            return profile;
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static VolumeProfile Save(VolumeProfile profile)
        {
            profile.isDirty = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static Volume GetVolume(Transform root, string name, VolumeProfile profile, int priority, float weight)
        {
            Transform t = root.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(root, false);
            }
            t.gameObject.layer = 0; // the cameras' volume mask is Default
            var volume = t.GetComponent<Volume>();
            if (volume == null) volume = t.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.weight = weight;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
            return volume;
        }

        // Gameplay camera already renders post; the RCC car camera has no URP camera data (post off by default).
        private static string EnableCameraPostProcessing(UnityEngine.SceneManagement.Scene scene)
        {
            var report = new System.Text.StringBuilder();
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (cam.gameObject.scene != scene || cam.targetTexture != null) continue;
                bool gameplay = cam.CompareTag("MainCamera");
                if (!gameplay) continue;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                bool was = data.renderPostProcessing;
                data.renderPostProcessing = true;
                data.volumeLayerMask |= 1;
                EditorUtility.SetDirty(data);
                report.Append($"{cam.name} ({(was ? "already on" : "enabled")}) ");
            }
            return report.ToString();
        }

        private static Transform FindRoot(UnityEngine.SceneManagement.Scene scene, string name, bool create)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == name) return go.transform;
            }
            if (!create) return null;
            var root = new GameObject(name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            return root.transform;
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
