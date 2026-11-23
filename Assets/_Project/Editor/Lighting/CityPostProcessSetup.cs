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
    /// Mobile notes: Mobile_RPAsset grades in Low Dynamic Range. URP still applies the tonemapper there (ALU in the uber
    /// pass, before the internal LUT), and colour adjustments, white balance and split toning all bake into the one 32^3 LUT
    /// URP already builds, so the whole grade costs the same as one LUT lookup. Bloom runs at quarter resolution, few iterations.
    /// Menu: Tools > OpenworldCrime > Lighting > Setup City Post Processing.
    /// </summary>
    public static class CityPostProcessSetup
    {
        public const string Folder = "Assets/_Project/Settings/PostProcessing";
        private const string LightingRootName = "OWC_Lighting";
        private const string SwitcherName = "CityLighting";
        private const int DayPriority = 110;   // above the scene's Global / Outdoor volumes and Hamza's Night Effects (100)
        private const int NightPriority = 120;
        // ACES darkens mid-tones a little, so exposure is higher than it was with no tonemapping.
        private const float DayExposure = 0.2f;
        private const float DayContrast = 14f;
        private const float DaySaturation = 8f;
        private const float DayTemperature = 2f;
        private const float DayTint = 0f;
        private const float NightExposure = 0.8f; // Ghost's night exposure

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
            // Natural daylight (the user asked for "everything natural", GTA V style; the pink "Vice" grade is gone): ACES filmic
            // tonemapping rolls bright facades and signs off softly instead of clipping (URP applies it in the LDR grading path
            // too, inside the same LUT pass, so the phone pays nothing extra), a little contrast and saturation, neutral colour.
            Add<Tonemapping>(p).mode.Override(TonemappingMode.ACES);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(DayExposure);
            color.contrast.Override(DayContrast);
            color.saturation.Override(DaySaturation);
            color.colorFilter.Override(Color.white);
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(DayTemperature);
            white.tint.Override(DayTint);
            var split = Add<SplitToning>(p);
            // Barely there: sunlit highlights a touch warm, shade a touch cool, like real daylight.
            split.shadows.Override(new Color(0.48f, 0.5f, 0.54f));
            split.highlights.Override(new Color(0.54f, 0.51f, 0.48f));
            split.balance.Override(0f);
            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.18f);
            bloom.scatter.Override(0.6f);
            bloom.tint.Override(Color.white);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);
            var vignette = Add<Vignette>(p);
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            return Save(p);
        }

        private static VolumeProfile BuildNightProfile()
        {
            VolumeProfile p = NewProfile("OWC_City_Night");
            // Neon night: ACES keeps signs and lamp pools from clipping, cool blue-violet shadows, magenta tint for the neon.
            Add<Tonemapping>(p).mode.Override(TonemappingMode.ACES);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(NightExposure);
            color.contrast.Override(14f);
            color.saturation.Override(14f);
            color.colorFilter.Override(new Color(0.96f, 0.97f, 1f)); // a faint moonlit cool; the neon signs bring the colour
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(-8f);
            white.tint.Override(0f);
            var split = Add<SplitToning>(p);
            split.shadows.Override(new Color(0.45f, 0.48f, 0.6f));
            split.highlights.Override(new Color(0.55f, 0.52f, 0.48f));
            split.balance.Override(-10f);
            var lgg = Add<LiftGammaGain>(p);
            lgg.lift.Override(new Vector4(0.92f, 0.94f, 1f, -0.03f)); // Ghost's lift / gamma / gain
            lgg.gamma.Override(new Vector4(0.98f, 0.98f, 1f, 0f));
            lgg.gain.Override(new Vector4(1f, 0.97f, 0.94f, 0.02f));
            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(Color.white);
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
