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
    /// Bright, colourful city lighting for the gameplay scene, baked so it costs what a baked city costs on a phone, and the
    /// switch between it and Hamza's lighting (both ways, re-runnable; Hamza's files are never changed or deleted).
    ///
    /// The look lives in our own assets (Assets/_Project/Lighting/Bright): day / night presets (a copy of Hamza's
    /// CityDayNightPresets with our sun, sky light and bounce), the sky material, post-processing profiles and the lighting
    /// settings the bake uses. Tune those in the Inspector and re-bake; fog and cloud colours are scene values kept in the
    /// state asset below when Hamza's lighting is restored.
    ///
    /// Bake: Baked Indirect, like the bake it replaces (the sun stays a realtime light for direct light and shadows; the bake
    /// holds sky light, sun bounce, AO and, at night, the street lamp pools), night then day through
    /// <see cref="DayNightLightBaker"/>, so <see cref="BakedLightingSwitcher"/> crossfades the two sets as before. Unity always
    /// bakes into the scene's own lighting folder, so Hamza's files there are moved aside for the bake (GUIDs kept), ours are
    /// moved to Assets/_Project/Lighting/Bright/Bake afterwards and his are moved back.
    ///
    /// Menu: Tools > OpenworldCrime > Lighting.
    /// </summary>
    public static class BrightLightingSetup
    {
        private const string Menu = "Tools/OpenworldCrime/Lighting/";
        private const string ScenePath = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";
        private const string SceneLightingFolder = "Assets/_Project/Scenes/Gangstar_Gameplay";
        private const string Folder = "Assets/_Project/Lighting/Bright";
        private const string BakeFolder = Folder + "/Bake";
        private const string SetsFolder = BakeFolder + "/Sets";
        private const string HoldFolder = "Assets/_Project/Lighting/HeldSceneLighting";
        private const string PresetsPath = Folder + "/OWC_BrightDayNightPresets.asset";
        private const string SkyPath = Folder + "/CitySkyCycle_Bright.mat";
        private const string NightSkyPath = Folder + "/OWC_BrightNightSky.mat";
        private const string DayProfilePath = Folder + "/OWC_Bright_Day.asset";
        private const string NightProfilePath = Folder + "/OWC_Bright_Night.asset";
        private const string SettingsPath = Folder + "/OWC_BrightLighting.lighting";
        private const string ParametersPath = Folder + "/OWC_BrightLightmapParameters.giparams";
        private const string HamzaStatePath = "Assets/_Project/Editor/Lighting/OWC_HamzaLightingState.asset";
        private const string BrightStatePath = "Assets/_Project/Editor/Lighting/OWC_BrightLightingState.asset";
        private const string PipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string BlendShaderName = "Hidden/OpenworldCrime/LightmapBlend";
        private const string StatusFile = "Temp/bright_bake_status.txt";

        // URP main light shadows (Mobile_RPAsset) with this look: soft, 2 cascades, 60 m.
        private const float ShadowDistance = 60f;
        private const int ShadowCascades = 2;
        private const int ShadowResolution = 2048;

        // Baked street lamps (night bake only).
        private const float LampIntensity = 11f;
        private const float LampRange = 18f;
        private static readonly Color LampColor = new Color(1f, 0.72f, 0.46f);
        private const float ShopLightIntensity = 12f;

        private static readonly string[] CloudFields = { "morning", "noon", "evening", "night" };

        private sealed class Targets
        {
            public Scene scene;
            public CityDayNightController controller;
            public CityAtmosphere atmosphere;
            public SkyCycleController sky;
            public BakedLightingSwitcher switcher;
            public SkyClouds clouds;
            public string error;
        }

        // ------------------------------------------------------------------------------------------------------ menu

        [MenuItem(Menu + "Apply Bright Lighting (ours)", false, 0)]
        private static void ApplyMenu() => Debug.Log(Apply());

        [MenuItem(Menu + "Restore Hamza Lighting", false, 1)]
        private static void RestoreMenu() => Debug.Log(RestoreHamza());

        [MenuItem(Menu + "Bright Lighting/Bake Bright Day And Night", false, 20)]
        private static void BakeMenu() => Debug.Log(Bake());

        [MenuItem(Menu + "Bright Lighting/Preview Without Bake (realtime)", false, 21)]
        private static void PreviewMenu() => Debug.Log(PreviewRealtime());

        [MenuItem(Menu + "Bright Lighting/Reset Look Assets To Defaults", false, 22)]
        private static void ResetMenu() => Debug.Log(ResetLookAssets());

        // ------------------------------------------------------------------------------------------------------ public API

        public static bool IsBrightApplied
        {
            get
            {
                var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
                return controller != null && AssetDatabase.GetAssetPath(controller.presets) == PresetsPath;
            }
        }

        /// <summary>Applies our look and our bake (when there is one) to the open gameplay scene. Marks the scene dirty.</summary>
        public static string Apply()
        {
            Targets t = Find();
            if (t.error != null) return t.error;
            if (!IsBright(t)) Capture(t, LoadOrCreateState(HamzaStatePath));
            EnsureLookAssets(false);
            SceneLightingBackup state = AssetDatabase.LoadAssetAtPath<SceneLightingBackup>(BrightStatePath);
            if (state == null) state = DefaultBrightState();
            bool baked = FillBakedData(state);
            RestoreFrom(t, state);
            Finish(t);
            return baked
                ? $"[BrightLighting] Bright lighting applied (bake: {AssetDatabase.GetAssetPath(state.lightingData)}). Save the scene to keep it."
                : "[BrightLighting] Bright look applied WITHOUT a bake (realtime preview). Run 'Bright Lighting/Bake Bright Day And Night'.";
        }

        /// <summary>Puts Hamza's lighting back exactly as it was captured. Marks the scene dirty.</summary>
        public static string RestoreHamza()
        {
            Targets t = Find();
            if (t.error != null) return t.error;
            var hamza = AssetDatabase.LoadAssetAtPath<SceneLightingBackup>(HamzaStatePath);
            if (hamza == null) return "[BrightLighting] No saved Hamza lighting state (" + HamzaStatePath + "); the bright lighting was never applied.";
            if (IsBright(t)) Capture(t, LoadOrCreateState(BrightStatePath));
            RestoreFrom(t, hamza);
            Finish(t);
            return $"[BrightLighting] Hamza's lighting restored (bake: {AssetDatabase.GetAssetPath(hamza.lightingData)}). Save the scene to keep it.";
        }

        /// <summary>Our look with no lightmaps or baked probes (realtime sun + flat ambient), to judge the target look.</summary>
        public static string PreviewRealtime()
        {
            string applied = Apply();
            Targets t = Find();
            if (t.error != null) return t.error;
            Lightmapping.lightingDataAsset = null;
            Set(t.switcher, so => so.FindProperty("m_Enabled").boolValue = false);
            Finish(t);
            return applied + "\n[BrightLighting] Realtime preview: lightmaps and baked probes unloaded. 'Apply Bright Lighting' brings the bake back.";
        }

        /// <summary>Rewrites our presets, sky, post profiles and lighting settings with the default values in this file.</summary>
        public static string ResetLookAssets()
        {
            EnsureLookAssets(true);
            AssetDatabase.DeleteAsset(BrightStatePath); // fog / clouds back to defaults on the next Apply
            return "[BrightLighting] Look assets reset to defaults. " + (IsBrightApplied ? Apply() : string.Empty);
        }

        // ------------------------------------------------------------------------------------------------------ bake

        private static bool holding;
        private static LightingDataAsset dataBeforeBake;
        private static double bakeStarted;

        public static bool IsBaking => holding;

        /// <summary>Applies our look and bakes night then day into our own folder (asynchronous; see Temp/bright_bake_status.txt).</summary>
        public static string Bake()
        {
            if (holding || DayNightLightBaker.IsRunning || Lightmapping.isRunning) return "[BrightLighting] A bake is already running.";
            string applied = Apply();
            Targets t = Find();
            if (t.error != null) return t.error;
            CityDayNightPresets presets = AssetDatabase.LoadAssetAtPath<CityDayNightPresets>(PresetsPath);

            dataBeforeBake = Lightmapping.lightingDataAsset;
            string held = HoldSceneLighting();
            if (held != null) return held;
            Lightmapping.lightingDataAsset = null;
            bakeStarted = EditorApplication.timeSinceStartup;
            Status("bake starting");

            var options = new DayNightLightBaker.Options
            {
                SetsFolder = SetsFolder,
                Settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath),
                DayBounce = presets.day.indirectIntensity,
                NightBounce = presets.night.indirectIntensity,
                LampIntensity = LampIntensity,
                LampRange = LampRange,
                LampColor = LampColor,
                ShopLightIntensity = ShopLightIntensity,
                Completed = OnBakeCompleted
            };
            string started = DayNightLightBaker.Start(options);
            return applied + "\n" + started;
        }

        private static void OnBakeCompleted(bool success, string message)
        {
            double minutes = (EditorApplication.timeSinceStartup - bakeStarted) / 60.0;
            string moveError = null;
            if (success) moveError = MoveBakeOutput();
            else DeleteSceneLightingOutputs(); // partial results of a failed bake (Hamza's are held elsewhere)
            string returned = ReturnHeldLighting();
            AssetDatabase.Refresh();
            if (!success || moveError != null) Lightmapping.lightingDataAsset = dataBeforeBake;
            string applied = success && moveError == null ? Apply() : "(previous lighting data kept)";
            Targets t = Find();
            if (t.error == null) EditorSceneManager.SaveScene(t.scene);
            Status($"{(success && moveError == null ? "DONE" : "FAILED")} in {minutes:F1} min: {message} {moveError} {returned} {applied}");
        }

        // Unity writes LightingData.asset, Lightmap-* and ReflectionProbe-* into the scene's folder: Hamza's go aside meanwhile.
        private static string HoldSceneLighting()
        {
            if (AssetDatabase.IsValidFolder(HoldFolder) && Directory.GetFiles(HoldFolder).Length > 0)
                return "[BrightLighting] " + HoldFolder + " still holds lighting files from an interrupted bake. Move them back to " + SceneLightingFolder + " first.";
            EnsureFolder(HoldFolder);
            foreach (string path in SceneLightingOutputs(SceneLightingFolder))
            {
                string error = AssetDatabase.MoveAsset(path, HoldFolder + "/" + Path.GetFileName(path));
                if (!string.IsNullOrEmpty(error))
                {
                    ReturnHeldLighting();
                    return "[BrightLighting] Could not move " + path + " aside (" + error + "); nothing baked.";
                }
            }
            holding = true;
            return null;
        }

        private static string ReturnHeldLighting()
        {
            holding = false;
            if (!AssetDatabase.IsValidFolder(HoldFolder)) return string.Empty;
            var failed = new List<string>();
            foreach (string path in SceneLightingOutputs(HoldFolder))
            {
                string error = AssetDatabase.MoveAsset(path, SceneLightingFolder + "/" + Path.GetFileName(path));
                if (!string.IsNullOrEmpty(error)) failed.Add(path + ": " + error);
            }
            if (failed.Count > 0) return "COULD NOT RETURN: " + string.Join("; ", failed);
            AssetDatabase.DeleteAsset(HoldFolder);
            return "Hamza's scene lighting files returned.";
        }

        private static string MoveBakeOutput()
        {
            EnsureFolder(BakeFolder);
            foreach (string old in SceneLightingOutputs(BakeFolder)) AssetDatabase.DeleteAsset(old);
            foreach (string path in SceneLightingOutputs(SceneLightingFolder))
            {
                string error = AssetDatabase.MoveAsset(path, BakeFolder + "/" + Path.GetFileName(path));
                if (!string.IsNullOrEmpty(error)) return "Could not move " + path + ": " + error;
            }
            return null;
        }

        private static void DeleteSceneLightingOutputs()
        {
            foreach (string path in SceneLightingOutputs(SceneLightingFolder)) AssetDatabase.DeleteAsset(path);
        }

        private static List<string> SceneLightingOutputs(string folder)
        {
            var result = new List<string>();
            if (!Directory.Exists(folder)) return result;
            foreach (string file in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".meta")) continue;
                if (name == "LightingData.asset" || name.StartsWith("Lightmap-") || name.StartsWith("ReflectionProbe-"))
                    result.Add(folder + "/" + name);
            }
            return result;
        }

        private static void Status(string text)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StatusFile, text);
            Debug.Log("[BrightLighting] " + text);
        }

        // ------------------------------------------------------------------------------------------------------ state

        // Any of our assets in use counts: a half-applied state must never be captured as Hamza's lighting.
        private static bool IsBright(Targets t)
        {
            if (AssetDatabase.GetAssetPath(t.controller.presets) == PresetsPath) return true;
            Object sky = new SerializedObject(t.sky).FindProperty("skyMaterial").objectReferenceValue;
            if (AssetDatabase.GetAssetPath(sky) == SkyPath) return true;
            string data = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
            return data.StartsWith(Folder) || AssetDatabase.GetAssetPath(Lightmapping.lightingSettings) == SettingsPath;
        }

        private static SceneLightingBackup DefaultBrightState()
        {
            var s = ScriptableObject.CreateInstance<SceneLightingBackup>();
            s.presets = AssetDatabase.LoadAssetAtPath<CityDayNightPresets>(PresetsPath);
            s.skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            s.dayProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DayProfilePath);
            s.nightProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(NightProfilePath);
            s.dayFog = new SceneLightingBackup.Fog { enabled = true, color = new Color(0.70f, 0.82f, 0.95f), start = 170f, end = 1000f };
            s.nightFog = new SceneLightingBackup.Fog { enabled = true, color = new Color(0.07f, 0.09f, 0.17f), start = 100f, end = 700f };
            s.clouds = new[]
            {
                Cloud(new Color(1f, 0.95f, 0.88f), new Color(0.76f, 0.80f, 0.90f), new Color(1f, 0.85f, 0.66f), 0.8f, 0.44f),
                Cloud(Color.white, new Color(0.86f, 0.90f, 0.97f), new Color(1f, 0.97f, 0.90f), 0.8f, 0.4f),
                Cloud(new Color(1f, 0.76f, 0.56f), new Color(0.58f, 0.48f, 0.58f), new Color(1f, 0.62f, 0.36f), 0.85f, 0.46f),
                Cloud(new Color(0.22f, 0.26f, 0.38f), new Color(0.07f, 0.085f, 0.14f), new Color(0.36f, 0.44f, 0.62f), 0.7f, 0.46f)
            };
            s.softShadows = true;
            s.softShadowQuality = (int)SoftShadowQuality.Low;
            s.shadowDistance = ShadowDistance;
            s.cascades = ShadowCascades;
            s.shadowResolution = ShadowResolution;
            return s;
        }

        // Our state always points at our latest bake (or none: realtime) and our own settings asset.
        private static bool FillBakedData(SceneLightingBackup s)
        {
            s.presets = AssetDatabase.LoadAssetAtPath<CityDayNightPresets>(PresetsPath);
            s.presetsWereOverride = true; // ours is always an override of the controller's prefab value
            s.lightingSettings =AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            s.lightingData = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(BakeFolder + "/LightingData.asset");
            s.daySet = AssetDatabase.LoadAssetAtPath<BakedLightingSet>(SetsFolder + "/Day.asset");
            s.nightSet = AssetDatabase.LoadAssetAtPath<BakedLightingSet>(SetsFolder + "/Night.asset");
            s.blendShader = Shader.Find(BlendShaderName);
            bool baked = s.lightingData != null && s.daySet != null && s.nightSet != null && s.daySet.LightmapCount > 0;
            s.switcherEnabled = baked;
            if (!baked) s.lightingData = null;
            return baked;
        }

        private static void Capture(Targets t, SceneLightingBackup b)
        {
            b.lightingData = Lightmapping.lightingDataAsset;
            b.lightingSettings = Lightmapping.lightingSettings;
            var sw = new SerializedObject(t.switcher);
            b.switcherEnabled = t.switcher.enabled;
            b.daySet = sw.FindProperty("daySet").objectReferenceValue as BakedLightingSet;
            b.nightSet = sw.FindProperty("nightSet").objectReferenceValue as BakedLightingSet;
            b.blendShader = sw.FindProperty("blendShader").objectReferenceValue as Shader;
            b.presets = t.controller.presets;
            b.presetsWereOverride = new SerializedObject(t.controller).FindProperty("presets").prefabOverride;
            b.skyMaterial = new SerializedObject(t.sky).FindProperty("skyMaterial").objectReferenceValue as Material;
            var at = new SerializedObject(t.atmosphere);
            b.dayFog = ReadFog(at.FindProperty("dayFog"));
            b.nightFog = ReadFog(at.FindProperty("nightFog"));
            b.dayProfile = ((Volume)at.FindProperty("dayVolume").objectReferenceValue).sharedProfile;
            b.nightProfile = ((Volume)at.FindProperty("nightVolume").objectReferenceValue).sharedProfile;
            var cl = new SerializedObject(t.clouds);
            b.clouds = new SceneLightingBackup.CloudLook[CloudFields.Length];
            for (int i = 0; i < CloudFields.Length; i++)
            {
                SerializedProperty p = cl.FindProperty(CloudFields[i]);
                b.clouds[i] = Cloud(p.FindPropertyRelative("lit").colorValue, p.FindPropertyRelative("shade").colorValue,
                    p.FindPropertyRelative("sunGlow").colorValue, p.FindPropertyRelative("opacity").floatValue, p.FindPropertyRelative("coverage").floatValue);
            }
            var rp = new SerializedObject(Pipeline());
            b.softShadows = rp.FindProperty("m_SoftShadowsSupported").boolValue;
            b.softShadowQuality = rp.FindProperty("m_SoftShadowQuality").intValue;
            b.shadowDistance = rp.FindProperty("m_ShadowDistance").floatValue;
            b.cascades = rp.FindProperty("m_ShadowCascadeCount").intValue;
            b.shadowResolution = rp.FindProperty("m_MainLightShadowmapResolution").intValue;
            EditorUtility.SetDirty(b);
            AssetDatabase.SaveAssets();
        }

        private static void RestoreFrom(Targets t, SceneLightingBackup b)
        {
            Lightmapping.lightingDataAsset = b.lightingData;
            if (b.lightingSettings != null) Lightmapping.lightingSettings = b.lightingSettings;
            Set(t.switcher, so =>
            {
                so.FindProperty("daySet").objectReferenceValue = b.daySet;
                so.FindProperty("nightSet").objectReferenceValue = b.nightSet;
                so.FindProperty("blendShader").objectReferenceValue = b.blendShader;
                so.FindProperty("m_Enabled").boolValue = b.switcherEnabled;
            });
            Set(t.controller, so => so.FindProperty("presets").objectReferenceValue = b.presets);
            if (!b.presetsWereOverride) Revert(t.controller, "presets");
            Set(t.sky, so => so.FindProperty("skyMaterial").objectReferenceValue = b.skyMaterial);
            Set(t.atmosphere, so => { WriteFog(so.FindProperty("dayFog"), b.dayFog); WriteFog(so.FindProperty("nightFog"), b.nightFog); });
            var at = new SerializedObject(t.atmosphere);
            SetProfile((Volume)at.FindProperty("dayVolume").objectReferenceValue, b.dayProfile);
            SetProfile((Volume)at.FindProperty("nightVolume").objectReferenceValue, b.nightProfile);
            if (b.clouds != null && b.clouds.Length == CloudFields.Length)
            {
                Set(t.clouds, so =>
                {
                    for (int i = 0; i < CloudFields.Length; i++)
                    {
                        SerializedProperty p = so.FindProperty(CloudFields[i]);
                        p.FindPropertyRelative("lit").colorValue = b.clouds[i].lit;
                        p.FindPropertyRelative("shade").colorValue = b.clouds[i].shade;
                        p.FindPropertyRelative("sunGlow").colorValue = b.clouds[i].sunGlow;
                        p.FindPropertyRelative("opacity").floatValue = b.clouds[i].opacity;
                        p.FindPropertyRelative("coverage").floatValue = b.clouds[i].coverage;
                    }
                });
            }
            Set(Pipeline(), so =>
            {
                so.FindProperty("m_SoftShadowsSupported").boolValue = b.softShadows;
                so.FindProperty("m_SoftShadowQuality").intValue = b.softShadowQuality;
                so.FindProperty("m_ShadowDistance").floatValue = b.shadowDistance;
                so.FindProperty("m_ShadowCascadeCount").intValue = b.cascades;
                so.FindProperty("m_MainLightShadowmapResolution").intValue = b.shadowResolution;
            });
        }

        private static void Finish(Targets t)
        {
            t.controller.Apply(false);
            EditorSceneManager.MarkSceneDirty(t.scene);
            AssetDatabase.SaveAssets();
        }

        private static SceneLightingBackup LoadOrCreateState(string path)
        {
            var b = AssetDatabase.LoadAssetAtPath<SceneLightingBackup>(path);
            if (b != null) return b;
            b = ScriptableObject.CreateInstance<SceneLightingBackup>();
            AssetDatabase.CreateAsset(b, path);
            return b;
        }

        // ------------------------------------------------------------------------------------------------------ look assets

        // Creates any missing look asset (copies of Hamza's presets / night sky and of our sky material), writing the
        // defaults into new ones only, or into all of them when reset.
        private static void EnsureLookAssets(bool reset)
        {
            EnsureFolder(Folder);
            var hamza = AssetDatabase.LoadAssetAtPath<SceneLightingBackup>(HamzaStatePath);
            CityDayNightPresets source = hamza != null ? hamza.presets : null;
            Material skySource = hamza != null ? hamza.skyMaterial : null;
            if (source == null || skySource == null) throw new System.InvalidOperationException("[BrightLighting] Hamza's lighting state is missing; apply from Hamza's lighting first.");

            Material sky = CopyAsset(skySource, SkyPath, out bool newSky);
            Material nightSky = CopyAsset(source.night.skybox, NightSkyPath, out _);
            CityDayNightPresets presets = CopyAsset(source, PresetsPath, out bool newPresets);
            if (newSky || reset) WriteSky(sky);
            if (newPresets || reset) WritePresets(presets, source);
            // Our own sky materials: the controller writes the day sky into the night one, which must not be Hamza's asset.
            presets.day.skybox = sky;
            presets.night.skybox = nightSky;
            EditorUtility.SetDirty(presets);

            VolumeProfile day = Profile(DayProfilePath, out bool newDay);
            VolumeProfile night = Profile(NightProfilePath, out bool newNight);
            if (newDay || reset) WriteDayProfile(day);
            if (newNight || reset) WriteNightProfile(night);

            bool newSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath) == null;
            if (newSettings || reset) WriteLightingSettings();
            AssetDatabase.SaveAssets();
        }

        // Day: high warm sun, clear blue sky light, strong bounce. Night: cool moon, blue sky light; lamps are in the bake.
        // Values are in the project's gamma colour space.
        private static void WritePresets(CityDayNightPresets p, CityDayNightPresets source)
        {
            EditorUtility.CopySerialized(source, p); // window, sign and venue glow stay as authored
            p.name = Path.GetFileNameWithoutExtension(PresetsPath);

            CityLightingLook d = p.day;
            d.lightRotation = new Vector3(50f, source.day.lightRotation.y, 0f);
            d.lightColor = new Color(1f, 0.93f, 0.80f);
            d.lightIntensity = 1.1f;
            d.indirectIntensity = 1.7f; // bounce strength in the bake (sun 1.1 x 1.7)
            d.shadows = LightShadows.Soft;
            d.shadowStrength = 0.85f;
            d.sceneBrightness = 1f;
            d.ambientMode = AmbientMode.Trilight;
            d.ambientSky = new Color(0.58f, 0.68f, 0.88f);
            d.ambientEquator = new Color(0.58f, 0.58f, 0.58f);
            d.ambientGround = new Color(0.40f, 0.36f, 0.31f);
            d.ambientIntensity = 1f;
            d.reflectionIntensity = 0.85f;

            CityLightingLook n = p.night;
            n.lightColor = new Color(0.62f, 0.74f, 1f);
            n.lightIntensity = 0.45f;
            n.indirectIntensity = 1f;
            n.sceneBrightness = 1f;
            n.shadows = LightShadows.Soft;
            n.shadowStrength = 0.6f;
            n.ambientMode = AmbientMode.Trilight;
            n.ambientSky = new Color(0.17f, 0.21f, 0.36f);
            n.ambientEquator = new Color(0.13f, 0.15f, 0.24f);
            n.ambientGround = new Color(0.07f, 0.07f, 0.10f);
            n.ambientIntensity = 1f;
            EditorUtility.SetDirty(p);
        }

        private static void WriteSky(Material m)
        {
            m.SetFloat("_CubeExposure", 0.95f);
            m.SetFloat("_CloudMix", 0.6f);
            m.SetColor("_MorningTint", new Color(0.90f, 0.88f, 0.95f));
            m.SetColor("_MorningZenith", new Color(0.24f, 0.46f, 0.84f));
            m.SetColor("_MorningGlow", new Color(1.0f, 0.74f, 0.52f));
            m.SetColor("_NoonTint", new Color(0.55f, 0.72f, 1.15f));
            m.SetColor("_NoonZenith", new Color(0.13f, 0.36f, 0.88f));
            m.SetColor("_NoonGlow", new Color(0.80f, 0.88f, 1.0f));
            m.SetColor("_EveningTint", new Color(1.05f, 0.80f, 0.66f));
            m.SetColor("_EveningZenith", new Color(0.24f, 0.30f, 0.58f));
            m.SetColor("_EveningGlow", new Color(1.0f, 0.56f, 0.28f));
            m.SetColor("_NightHorizon", new Color(0.13f, 0.18f, 0.30f));
            m.SetColor("_NightZenith", new Color(0.04f, 0.07f, 0.16f));
            m.SetVector("_CycleWeights", new Vector4(0f, 1f, 0f, 0f)); // edit mode / bake reflection: noon
            EditorUtility.SetDirty(m);
        }

        // Neutral tonemapping keeps colours saturated (ACES greys and darkens them). Everything lands in URP's one LUT.
        private static void WriteDayProfile(VolumeProfile p)
        {
            Clear(p);
            Add<Tonemapping>(p).mode.Override(TonemappingMode.Neutral);
            var color = Add<ColorAdjustments>(p);
            color.postExposure.Override(-0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(18f);
            color.colorFilter.Override(Color.white);
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(3f);
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
            color.postExposure.Override(0.35f);
            color.contrast.Override(10f);
            color.saturation.Override(14f);
            color.colorFilter.Override(Color.white);
            var white = Add<WhiteBalance>(p);
            white.temperature.Override(-6f);
            white.tint.Override(2f);
            AddBloom(p, 0.9f, 0.45f, 0.68f, new Color(1f, 0.92f, 0.95f));
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

        // Same layout budget as the bake it replaces (2 texels/m, 2048 pages, non-directional), with lighter AO and a
        // brighter indirect: albedo boost and indirect intensity keep shade and bounce close to the realtime look.
        private static void WriteLightingSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (s == null)
            {
                s = new LightingSettings { name = Path.GetFileNameWithoutExtension(SettingsPath) };
                AssetDatabase.CreateAsset(s, SettingsPath);
            }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.IndirectOnly;
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.lightmapResolution = 2f;
            s.lightmapPadding = 4;
            s.lightmapMaxSize = 2048;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            s.directionalityMode = LightmapsMode.NonDirectional;
            s.albedoBoost = 1.4f;
            s.indirectScale = 1.25f;
            s.ao = true;
            s.aoMaxDistance = 0.8f;
            s.aoExponentIndirect = 0.35f;
            s.aoExponentDirect = 0f;
            s.maxBounces = 3;
            s.directSampleCount = 32;
            s.indirectSampleCount = 256;
            s.environmentSampleCount = 256;
            s.filteringMode = LightingSettings.FilterMode.Advanced;
            s.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage;
            s.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
            s.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
            s.filterTypeDirect = LightingSettings.FilterType.Gaussian;
            s.filterTypeIndirect = LightingSettings.FilterType.Gaussian;
            s.filterTypeAO = LightingSettings.FilterType.Gaussian;
            s.filteringGaussianRadiusDirect = 1f;
            s.filteringGaussianRadiusIndirect = 3f;
            s.filteringGaussianRadiusAO = 2f;
            s.prioritizeView = false;
            s.autoGenerate = false;

            var parameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(ParametersPath);
            if (parameters == null)
            {
                parameters = new LightmapParameters { name = Path.GetFileNameWithoutExtension(ParametersPath) };
                AssetDatabase.CreateAsset(parameters, ParametersPath);
            }
            parameters.backFaceTolerance = 0.5f;
            parameters.pushoff = 0.02f;
            parameters.antiAliasingSamples = 8;
            EditorUtility.SetDirty(parameters);
            Set(s, so =>
            {
                SerializedProperty defaultParameters = so.FindProperty("m_LightmapParameters");
                if (defaultParameters != null) defaultParameters.objectReferenceValue = parameters;
            });
        }

        // ------------------------------------------------------------------------------------------------------ helpers

        private static Targets Find()
        {
            var t = new Targets { scene = SceneManager.GetActiveScene() };
            if (EditorApplication.isPlayingOrWillChangePlaymode) { t.error = "[BrightLighting] Leave play mode first."; return t; }
            if (t.scene.path != ScenePath || SceneManager.sceneCount != 1) { t.error = "[BrightLighting] Open " + ScenePath + " (only)."; return t; }
            t.controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            t.atmosphere = Object.FindFirstObjectByType<CityAtmosphere>(FindObjectsInactive.Include);
            t.sky = Object.FindFirstObjectByType<SkyCycleController>(FindObjectsInactive.Include);
            t.switcher = Object.FindFirstObjectByType<BakedLightingSwitcher>(FindObjectsInactive.Include);
            t.clouds = Object.FindFirstObjectByType<SkyClouds>(FindObjectsInactive.Include);
            if (t.controller == null || t.controller.sun == null || t.controller.presets == null || t.atmosphere == null || t.sky == null
                || t.switcher == null || t.clouds == null || Pipeline() == null)
                t.error = "[BrightLighting] The scene is missing one of its lighting systems; nothing changed.";
            return t;
        }

        private static UniversalRenderPipelineAsset Pipeline() => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);

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

        private static SceneLightingBackup.CloudLook Cloud(Color lit, Color shade, Color glow, float opacity, float coverage)
        {
            return new SceneLightingBackup.CloudLook { lit = lit, shade = shade, sunGlow = glow, opacity = opacity, coverage = coverage };
        }

        private static SceneLightingBackup.Fog ReadFog(SerializedProperty p)
        {
            return new SceneLightingBackup.Fog
            {
                enabled = p.FindPropertyRelative("enabled").boolValue, color = p.FindPropertyRelative("color").colorValue,
                start = p.FindPropertyRelative("startDistance").floatValue, end = p.FindPropertyRelative("endDistance").floatValue
            };
        }

        private static void WriteFog(SerializedProperty p, SceneLightingBackup.Fog f)
        {
            p.FindPropertyRelative("enabled").boolValue = f.enabled;
            p.FindPropertyRelative("color").colorValue = f.color;
            p.FindPropertyRelative("startDistance").floatValue = f.start;
            p.FindPropertyRelative("endDistance").floatValue = f.end;
        }

        private static void SetProfile(Volume volume, VolumeProfile profile)
        {
            if (volume == null || profile == null) return;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
