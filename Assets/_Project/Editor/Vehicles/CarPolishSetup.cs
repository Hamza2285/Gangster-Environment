using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Car feel / look polish, all re-runnable (Tools > OpenworldCrime > Vehicles > Car Polish):
    /// - Engine Sounds: one <see cref="VehicleEngineSound"/> asset per model (created only when missing, so tuned values stay),
    ///   assigned to the drivable prefabs' RCCVehicleAdapter; the traffic twins get the model's low engine loop.
    /// - Hit Debris VFX: builds VFX_CarHitDebris.prefab (dark bits, sparks, dust; reuses the M_CarFX_* materials) and assigns
    ///   it to VehicleDamageSettings.
    /// - HUD (scene): the car health bars and the digital speed readout under Gameplay UI/HUD, and hides the RCC dashboard's
    ///   speedometer / rev-counter dials in the scene's RCC canvas instance (RCC's prefab is not edited). Saves the scene.
    /// - Paint: see <see cref="CarPaintPolish"/>.
    /// </summary>
    public static class CarPolishSetup
    {
        private const string Menu = "Tools/OpenworldCrime/Vehicles/Car Polish/";
        private const string EngineSoundFolder = "Assets/_Project/Data/Vehicles/EngineSounds";
        private const string RccEngine = "Assets/RealisticCarControllerV3/Sounds/Engine/";
        private const string DrivableFolder = "Assets/_Project/Prefabs/Vehicles/Drivable";
        private const string TrafficFolder = "Assets/_Project/Prefabs/Vehicles/Traffic";
        private const string DamageSettingsPath = "Assets/_Project/Data/Vehicles/VehicleDamageSettings.asset";
        private const string DebrisPrefabPath = "Assets/_Project/Prefabs/Vehicles/Effects/VFX_CarHitDebris.prefab";
        private const string EffectMaterials = "Assets/_Project/Materials/Vehicles/Effects/";
        private const string ScenePath = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";
        private const string FontPath = "Assets/_Project/Art/Menu/BebasKai-Regular.otf";
        private const string PillSpritePath = "Assets/_Project/Art/Radio/RadioBarPill.png";

        [MenuItem(Menu + "Run All")]
        public static void RunAllMenu()
        {
            var sb = new StringBuilder("[CarPolish]\n");
            sb.AppendLine(ApplyEngineSounds());
            sb.AppendLine(BuildHitDebris());
            sb.AppendLine(CarPaintPolish.Apply());
            sb.AppendLine(SetupHud());
            Debug.Log(sb.ToString());
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Engine sounds
        // ---------------------------------------------------------------------------------------------------------------

        private struct EngineDef
        {
            public string Low, High, LowOff, HighOff, Idle;
            public float MinPitch, MaxPitch, Volume;
            public bool KeepClips;
        }

        private static EngineDef Set(string low, string high, string lowOff, string highOff, string idle, float min, float max, float volume) =>
            new EngineDef { Low = low, High = high, LowOff = lowOff, HighOff = highOff, Idle = idle, MinPitch = min, MaxPitch = max, Volume = volume };

        private static EngineDef PitchOnly(float min, float max, float volume) =>
            new EngineDef { KeepClips = true, MinPitch = min, MaxPitch = max, Volume = volume };

        // Per model (drivable prefab name without "Drivable_"): heavier SUVs low and deep, sports cars higher and raspier, every
        // model its own clip set or pitch range. RCC's bundled engine recordings only (Sounds/Engine).
        // Pitch at the rev limit stays natural (<= 1.6 for cars, lower for big ones): the "High" recordings are already high-rev,
        // pitching them further made them shrill. Variety comes from the clip set and the idle pitch. Volumes ~30% under RCC's.
        private static readonly Dictionary<string, EngineDef> Engines = new Dictionary<string, EngineDef>
        {
            // Sports / super
            { "Car1",      Set("Sport/Sport1_Low", "Sport/Sport1_High", "Sport/Sport1_Low_Off", "Sport/Sport1_High_Off", "Sport/Sport1_Idle", 0.78f, 1.5f, 0.6f) },
            { "NewCar1",   Set("Sport/Sport1_Low", "Sport/Sport1_Med", "Sport/Sport1_Low_Off", "Sport/Sport1_Med_Off", "Sport/Sport1_Idle", 0.76f, 1.55f, 0.6f) },
            { "Car3",      Set("Sport/Sport2_Low", "Sport/Sport2_Med", "Sport/Sport2_Low_Off", "Sport/Sport2_Med_Off", "Sport/Sport2_Idle", 0.8f, 1.6f, 0.62f) },
            { "Khodge2",   Set("Fast_Low", "Fast_High", null, null, "Fast_Idle", 0.78f, 1.5f, 0.6f) },
            // Muscle
            { "Car2",      Set("Muscle/Muscle_Low", "Muscle/Muscle_High", "Muscle/Muscle_Off", "Muscle/Muscle_Off", "Muscle/Muscle_Idle", 0.68f, 1.4f, 0.63f) },
            { "NewCar3",   Set("Muscle/Muscle_Low", "Muscle/Muscle_High", "Muscle/Muscle_Off", "Muscle/Muscle_Off", "Muscle/Muscle_Idle", 0.64f, 1.32f, 0.63f) },
            // Sedans
            { "Car4",      Set("E36_Low", "E36_Med", "E36_Low_Off", "E36_Med_Off", "E36_Idle", 0.74f, 1.48f, 0.57f) },
            { "WMB",       Set("E36_Low", "E36_High", "E36_Low_Off", "E36_High_Off", "E36_Idle", 0.76f, 1.42f, 0.57f) },
            { "Talenter",  Set("Generic/Generic_Low", "Generic/Generic_Med", "Generic/Generic_Low_Off", "Generic/Generic_Med_Off", "Generic/Generic_Idle", 0.74f, 1.45f, 0.57f) },
            { "Taxi",      Set("Saloon/Saloon_Low", "Saloon/Saloon_High", null, null, "Saloon/Saloon_Idle", 0.72f, 1.4f, 0.56f) },
            // SUVs
            { "Car5",      Set("Generic/Generic_Low", "Generic/Generic_High", "Generic/Generic_Low_Off", "Generic/Generic_High_Off", "Generic/Generic_Idle", 0.66f, 1.3f, 0.63f) },
            { "GWagon",    Set("SUV_Low_On", "SUV_Med_On", "SUV_Low_Off", "SUV_Med_Off", "SUV_Idle", 0.62f, 1.25f, 0.66f) },
            { "LandSover", Set("SUV_Low_On", "SUV_High_On", "SUV_Low_Off", "SUV_High_Off", "SUV_Idle", 0.64f, 1.28f, 0.64f) },
            { "Ambulance", Set("Bus/Bus_Low", "Bus/Bus_High", null, null, "Bus/Bus_Idle", 0.7f, 1.3f, 0.6f) },
            // Bikes keep their chopper recordings, pitched apart.
            { "Bike_Chopper", PitchOnly(0.68f, 1.4f, 0.6f) },
            { "Bike_Sport",   PitchOnly(0.85f, 1.7f, 0.58f) },
        };

        [MenuItem(Menu + "Apply Engine Sounds")]
        public static void ApplyEngineSoundsMenu() => Debug.Log(ApplyEngineSounds());

        public static string ApplyEngineSounds()
        {
            var sb = new StringBuilder("[CarPolish] engine sounds\n");
            EnsureFolder(EngineSoundFolder);
            foreach (KeyValuePair<string, EngineDef> pair in Engines) GetOrCreateEngineSound(pair.Key, pair.Value);
            AssetDatabase.SaveAssets();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { DrivableFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string model = Path.GetFileNameWithoutExtension(path).Replace("Drivable_", "");
                VehicleEngineSound sound = LoadEngineSound(model);
                if (sound == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var adapter = root.GetComponent<RCCVehicleAdapter>();
                    if (adapter == null) continue;
                    adapter.EditorSetEngineSound(sound);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    sb.AppendLine($"  {model}: {sound.name} ({(sound.high != null ? sound.high.name : "own clips")}, pitch {sound.minPitch:F2}-{sound.maxPitch:F2})");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { TrafficFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string model = Path.GetFileNameWithoutExtension(path).Replace("Traffic_", "");
                if (LoadEngineSound(model) == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (ApplyTrafficEngineLoop(root, model))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        sb.AppendLine($"  traffic {model}: {root.GetComponent<AudioSource>().clip.name}");
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return sb.ToString();
        }

        /// <summary>Traffic cars' engine loop volume (was 0.74, ~30% down to sit under the player's car).</summary>
        public const float TrafficEngineVolume = 0.52f;

        /// <summary>The model's engine sound asset (null when the model has none).</summary>
        public static VehicleEngineSound LoadEngineSound(string model) =>
            AssetDatabase.LoadAssetAtPath<VehicleEngineSound>($"{EngineSoundFolder}/EngineSound_{model}.asset");

        /// <summary>Gives an iTS traffic car the model's own engine loop (root AudioSource; iTS drives its pitch).</summary>
        public static bool ApplyTrafficEngineLoop(GameObject trafficRoot, string model)
        {
            VehicleEngineSound sound = LoadEngineSound(model);
            var source = trafficRoot != null ? trafficRoot.GetComponent<AudioSource>() : null;
            if (sound == null || sound.trafficLoop == null || source == null) return false;
            source.clip = sound.trafficLoop;
            source.volume = TrafficEngineVolume;
            return true;
        }

        private static VehicleEngineSound GetOrCreateEngineSound(string model, EngineDef def)
        {
            string path = $"{EngineSoundFolder}/EngineSound_{model}.asset";
            var sound = AssetDatabase.LoadAssetAtPath<VehicleEngineSound>(path);
            if (sound != null) return sound; // already there: keep any tuning done on it
            sound = ScriptableObject.CreateInstance<VehicleEngineSound>();
            if (!def.KeepClips)
            {
                sound.low = Clip(def.Low);
                sound.high = Clip(def.High);
                sound.lowOff = Clip(def.LowOff);
                sound.highOff = Clip(def.HighOff);
                sound.idle = Clip(def.Idle);
                sound.generateOffSounds = def.LowOff == null || def.HighOff == null;
                sound.trafficLoop = sound.low;
            }
            sound.minPitch = def.MinPitch;
            sound.maxPitch = def.MaxPitch;
            sound.maxVolume = def.Volume;
            sound.idleVolume = def.Volume;
            AssetDatabase.CreateAsset(sound, path);
            return sound;
        }

        private static AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(RccEngine + name + ".wav");
            if (clip == null) Debug.LogWarning($"[CarPolish] engine clip not found: {RccEngine}{name}.wav");
            return clip;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Hit debris VFX
        // ---------------------------------------------------------------------------------------------------------------

        [MenuItem(Menu + "Build Hit Debris VFX")]
        public static void BuildHitDebrisMenu() => Debug.Log(BuildHitDebris());

        public static string BuildHitDebris()
        {
            var debris = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterials + "M_CarFX_Debris.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterials + "M_CarFX_Glow_Additive.mat");
            var dust = AssetDatabase.LoadAssetAtPath<Material>(EffectMaterials + "M_CarFX_Dust_02.mat");
            if (debris == null || glow == null || dust == null) return "[CarPolish] hit debris: M_CarFX_Debris / Glow_Additive / Dust_02 materials missing (run the car explosion VFX builder)";

            var root = new GameObject("VFX_CarHitDebris");
            try
            {
                ParticleSystem bits = Bits(root.transform, debris);
                ParticleSystem sparks = Sparks(root.transform, glow);
                ParticleSystem puff = Dust(root.transform, dust);
                var burst = root.AddComponent<VehicleHitDebrisBurst>();
                burst.EditorSetup(new[]
                {
                    new VehicleHitDebrisBurst.Layer { system = bits, minCount = 3, maxCount = 12 },
                    new VehicleHitDebrisBurst.Layer { system = sparks, minCount = 3, maxCount = 12 },
                    new VehicleHitDebrisBurst.Layer { system = puff, minCount = 1, maxCount = 3 },
                });
                root.AddComponent<PooledVehicleEffect>();
                PrefabUtility.SaveAsPrefabAsset(root, DebrisPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }

            var settings = AssetDatabase.LoadAssetAtPath<VehicleDamageSettings>(DamageSettingsPath);
            if (settings == null) return "[CarPolish] hit debris built, but VehicleDamageSettings.asset not found";
            settings.hitDebrisEffect = AssetDatabase.LoadAssetAtPath<GameObject>(DebrisPrefabPath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return $"[CarPolish] hit debris: {DebrisPrefabPath} assigned to VehicleDamageSettings";
        }

        // Dark plastic / metal chips (unlit opaque cubes, flat 3D size): thrown out of the panel, fall and fade. No collision
        // module: they are small and short-lived, so dropping through the road is never seen and costs no physics queries.
        private static ParticleSystem Bits(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Bits", material, 16);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Mostly near-black plastic, some dark grey metal.
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.06f, 0.06f, 0.065f), new Color(0.32f, 0.33f, 0.35f));
            main.gravityModifier = 1.6f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 0.12f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
            spin.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
            spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            SizeOverLife(ps, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        private static ParticleSystem Sparks(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Sparks", material, 16);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startColor = new Color(1f, 0.82f, 0.5f, 1f);
            main.gravityModifier = 1.2f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 60f;
            shape.radius = 0.05f;
            ColorOverLife(ps, new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.2f;
            return ps;
        }

        private static ParticleSystem Dust(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Dust", material, 4);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
            main.startColor = new Color(0.62f, 0.6f, 0.57f, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            SizeOverLife(ps, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            ColorOverLife(ps, new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            return ps;
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false; // VehicleHitDebrisBurst emits by hit strength
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.enabled = false;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        private static void SizeOverLife(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void ColorOverLife(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // HUD (scene)
        // ---------------------------------------------------------------------------------------------------------------

        private static readonly Color Pink = new Color(1f, 0.18f, 0.58f, 1f);
        private static readonly Color PinkChip = new Color(1f, 0.78f, 0.9f, 0.95f);
        private static readonly Color BarBack = new Color(0.07f, 0.02f, 0.06f, 0.78f);
        private static readonly Color BarFrame = new Color(1f, 0.18f, 0.58f, 0.55f);
        private static readonly Color LightGrey = new Color(0.8f, 0.82f, 0.85f, 1f);
        private const int BarCount = 4;

        [MenuItem(Menu + "Setup HUD (health bars, speed readout, hide RCC speedometer)")]
        public static void SetupHudMenu() => Debug.Log(SetupHud());

        public static string SetupHud()
        {
            if (EditorApplication.isPlaying) return "[CarPolish] HUD: leave play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return $"[CarPolish] HUD: open {ScenePath} first";

            Transform gameplayUi = FindRoot("Gameplay UI");
            Transform hud = gameplayUi != null ? gameplayUi.Find("HUD") : null;
            // Every playable character (character switching), as the radio does.
            var interaction = Object.FindObjectsByType<PlayerVehicleInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (hud == null || interaction.Length == 0) return "[CarPolish] HUD: Gameplay UI/HUD or PlayerVehicleInteraction not found";
            var canvas = gameplayUi.GetComponent<Canvas>();
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            Sprite pill = LoadSprite(PillSpritePath);

            var sb = new StringBuilder("[CarPolish] HUD\n");
            BuildHealthBars(hud, interaction, pill);
            sb.AppendLine("  Gameplay UI/HUD/Vehicle Health Bars: " + BarCount + " pooled bars");
            RectTransform[] clear = KeepClearList(sb);
            BuildSpeedReadout(hud, interaction, canvas, font, clear, SpawnedKeepClear);
            sb.AppendLine($"  Gameplay UI/HUD/Speed Readout: keeps clear of {clear.Length} HUD elements");
            sb.AppendLine(HideRccSpeedometer());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.Append("  scene saved");
            return sb.ToString();
        }

        private static void BuildHealthBars(Transform hud, PlayerVehicleInteraction[] interaction, Sprite pill)
        {
            RectTransform holder = Recreate(hud, "Vehicle Health Bars");
            Stretch(holder);
            var views = new VehicleHealthBarView[BarCount];
            for (int i = 0; i < BarCount; i++)
            {
                RectTransform bar = NewRect("Bar " + i, holder);
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.pivot = new Vector2(0.5f, 0f); // sits on the roof point
                bar.sizeDelta = new Vector2(118f, 14f);

                Image frame = NewImage("Frame", bar, pill, BarFrame, 4.3f);
                Stretch(frame.rectTransform, -1.5f);
                Image back = NewImage("Background", bar, pill, BarBack, 4.3f);
                Stretch(back.rectTransform);
                RectTransform area = NewRect("Fill Area", bar);
                Stretch(area, 2.5f);
                Image chip = NewImage("Chip", area, pill, PinkChip, 6.5f);
                LeftBar(chip.rectTransform);
                Image fill = NewImage("Fill", area, pill, Pink, 6.5f);
                LeftBar(fill.rectTransform);

                var view = bar.gameObject.AddComponent<VehicleHealthBarView>();
                view.EditorSetup(bar, back, fill.rectTransform, fill, chip.rectTransform, chip);
                bar.gameObject.SetActive(false);
                views[i] = view;
            }
            var hudComponent = holder.gameObject.AddComponent<VehicleHealthBarHud>();
            hudComponent.EditorSetup(interaction, views);
        }

        private static void BuildSpeedReadout(Transform hud, PlayerVehicleInteraction[] interaction, Canvas canvas, Font font, RectTransform[] clear, string[] spawnedClear)
        {
            RectTransform holder = Recreate(hud, "Speed Readout");
            Stretch(holder);
            RectTransform panel = NewRect("Panel", holder);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f); // right edge follows the anchor left of the car
            panel.sizeDelta = new Vector2(190f, 104f);

            Text speed = NewText("Speed", panel, font, 84, Color.white, TextAnchor.LowerRight);
            speed.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            speed.rectTransform.anchorMax = Vector2.one;
            speed.rectTransform.offsetMin = speed.rectTransform.offsetMax = Vector2.zero;
            speed.supportRichText = true;
            speed.text = "<color=#FFFFFF38>0</color>00";
            AddShadow(speed.gameObject, new Color(0f, 0f, 0f, 0.55f), new Vector2(2f, -2f));

            var accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            accent.transform.SetParent(panel, false);
            accent.color = new Color(LightGrey.r, LightGrey.g, LightGrey.b, 0.55f);
            accent.raycastTarget = false;
            RectTransform accentRect = accent.rectTransform;
            accentRect.anchorMin = accentRect.anchorMax = new Vector2(1f, 0.3f);
            accentRect.pivot = new Vector2(1f, 0.5f);
            accentRect.sizeDelta = new Vector2(118f, 2f);
            accentRect.anchoredPosition = new Vector2(-4f, 1f);

            Text unit = NewText("Unit", panel, font, 26, LightGrey, TextAnchor.UpperRight);
            unit.rectTransform.anchorMin = Vector2.zero;
            unit.rectTransform.anchorMax = new Vector2(1f, 0.28f);
            unit.rectTransform.offsetMin = Vector2.zero;
            unit.rectTransform.offsetMax = new Vector2(-4f, 0f);
            unit.text = "KM/H";
            AddShadow(unit.gameObject, new Color(0f, 0f, 0f, 0.5f), new Vector2(1.5f, -1.5f));

            var readout = holder.gameObject.AddComponent<VehicleSpeedReadout>();
            readout.EditorSetup(interaction, panel, speed, canvas, clear, spawnedClear);
            panel.gameObject.SetActive(false);
        }

        // Created at runtime by other systems (minimap, Invector's HUD and inventory): found by path when the player gets in a car.
        private static readonly string[] SpawnedKeepClear =
        {
            "Game HUD/Minimap", "UI/HUD/health", "UI/HUD/stamina", "Inventory/EquipmentDisplayWindow",
        };

        // Everything in the scene the readout must never cover: our HUD and the on-screen driving controls (RCC canvas, our
        // vehicle exit button). Only referenced, never changed.
        private static RectTransform[] KeepClearList(StringBuilder sb)
        {
            var list = new List<RectTransform>();
            Add(list, "Gameplay UI", "HUD/Radio HUD/Radio Bar");
            Add(list, "Gameplay UI", "HUD/Money");
            Add(list, "Gameplay UI", "HUD/WeaponInfo");
            Add(list, "Gameplay UI", "HUD/Cash Popup");
            Add(list, "Touch Controls", "VehicleControls/Exit");
            Transform rcc = FindRoot("RCC Mobile Controls");
            if (rcc != null)
            {
                Transform buttons = rcc.Find("Controller Buttons");
                if (buttons != null)
                    foreach (Transform child in buttons) if (child is RectTransform rect && child.GetComponent<Graphic>() != null) list.Add(rect);
                Add(list, "RCC Mobile Controls", "Dashboard/RPM/Change Camera Button");
                Add(list, "RCC Mobile Controls", "Dashboard/RPM/Lights Toggle");
            }
            return list.ToArray();
        }

        private static void Add(List<RectTransform> list, string root, string path)
        {
            Transform r = FindRoot(root);
            var t = r != null ? r.Find(path) as RectTransform : null;
            if (t != null) list.Add(t);
            else Debug.LogWarning($"[CarPolish] keep-clear element not found: {root}/{path}");
        }

        // The RCC canvas (shown while driving on touch screens) carries a needle speedometer (KMH) and a rev counter (RPM) whose
        // dial also holds the camera and lights buttons. The speedometer goes entirely; of the rev counter only those two
        // buttons stay. Overrides on the scene's instance of RCC's prefab; RCC's prefab and scripts are untouched.
        private static string HideRccSpeedometer()
        {
            Transform rcc = FindRoot("RCC Mobile Controls");
            Transform dash = rcc != null ? rcc.Find("Dashboard") : null;
            if (dash == null) return "  RCC dashboard not found";
            int hidden = 0;
            Transform kmh = dash.Find("KMH");
            if (kmh != null && kmh.gameObject.activeSelf) { Undo.RecordObject(kmh.gameObject, "Hide RCC speedometer"); kmh.gameObject.SetActive(false); hidden++; }
            Transform rpm = dash.Find("RPM");
            if (rpm != null)
            {
                var dial = rpm.GetComponent<Graphic>();
                if (dial != null && dial.enabled) { dial.enabled = false; EditorUtility.SetDirty(dial); hidden++; }
                foreach (Transform child in rpm)
                {
                    if (child.name == "Change Camera Button" || child.name == "Lights Toggle" || child.name == "Light Buttons") continue;
                    if (!child.gameObject.activeSelf) continue;
                    child.gameObject.SetActive(false);
                    hidden++;
                }
            }
            return $"  RCC speedometer / rev counter: {hidden} parts hidden (camera and lights buttons kept)";
        }

        private static Transform FindRoot(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root.transform;
            return null;
        }

        private static RectTransform Recreate(Transform parent, string name)
        {
            Transform old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            return NewRect(name, parent);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color, float pixelsPerUnitMultiplier)
        {
            RectTransform rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewText(string name, Transform parent, Font font, int size, Color color, TextAnchor alignment)
        {
            RectTransform rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void AddShadow(GameObject go, Color color, Vector2 distance)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void LeftBar(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Sprite LoadSprite(string path)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite s) return s;
            return null;
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
