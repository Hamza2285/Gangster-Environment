using System.Text;
using OpenworldCrime.Environment;
using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Water and swimming (Tools > OpenworldCrime > Environment > Set Up Water & Swimming, city scene open). Safe to run again:
    /// everything is found and updated in place.
    /// - "Water System" under ///Manager///: <see cref="WaterManager"/> (one ocean body at the city water plane's height over the
    ///   playable area, the underpass road / walkway collider roots as dry ground, the sea death of World Bounds) and
    ///   <see cref="WaterSplashFx"/> (spray / foam / ripple particle systems with texture-free materials in Art/Water, three
    ///   pooled 3D audio sources, the project's waterSplash / exitWater sounds).
    /// - The generated swim clips (<see cref="SwimAnimationBuilder"/>) and Invector's ClimbUp clip.
    /// - <see cref="PlayerSwimController"/> on every playable character (Jason, Mia), and its reference on their PlayerManager and
    ///   PlayerVehicleFallDamage.
    /// Nothing of Hamza's or third-party code, prefabs or settings is changed; the scene is marked dirty (save it afterwards).
    /// </summary>
    public static class WaterSetup
    {
        private const string ArtFolder = "Assets/_Project/Art/Water";
        private const string ClimbClipModel = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_Actions.fbx";
        private const string SplashClipPath = "Assets/AudioClip 1/waterSplash.ogg";
        private const string ExitClipPath = "Assets/AudioClip 1/exitWater.ogg";
        private const string RootName = "Water System";
        private static readonly string[] DryRootNames = { "Traffic Road Colliders", "Walk Ground Colliders" };

        [MenuItem("Tools/OpenworldCrime/Environment/Set Up Water & Swimming (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[WaterSetup]\n");
            var scene = EditorSceneManager.GetActiveScene();

            // Water surface: the city's water plane (hidden at runtime by the Ocean, which builds its surface at the same height).
            float surface = 25.9f;
            MeshRenderer plane = null;
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.name == "Water" && r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Water")) plane = r;
            if (plane != null) surface = plane.bounds.center.y;
            else log.AppendLine("  WARNING: water plane 'Water' not found, surface height kept at " + surface);

            WorldBounds bounds = Object.FindAnyObjectByType<WorldBounds>(FindObjectsInactive.Include);
            Rect area = bounds != null ? bounds.area : new Rect(-810f, -1173f, 2018f, 2018f);
            area = Rect.MinMaxRect(area.xMin - 20f, area.yMin - 20f, area.xMax + 20f, area.yMax + 20f);
            var ocean = new WaterManager.WaterBody { name = "Ocean, canal and harbour", surfaceHeight = surface, unbounded = false, area = area };

            var dryRoots = new System.Collections.Generic.List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(DryRootNames, root.name) >= 0) dryRoots.Add(root.transform);

            // Manager object.
            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[WaterSetup] ///Manager/// not found in the open scene (open Gangstar_Gameplay).";
            Transform system = managers.transform.Find(RootName);
            if (system == null)
            {
                system = new GameObject(RootName).transform;
                Undo.RegisterCreatedObjectUndo(system.gameObject, "Water System");
                system.SetParent(managers.transform, false);
            }
            var waterManager = GetOrAdd<WaterManager>(system.gameObject);
            waterManager.EditorSetup(new[] { ocean }, dryRoots.ToArray(), bounds);
            EditorUtility.SetDirty(waterManager);

            // Splash FX.
            EnsureFolder(ArtFolder);
            Material sprayMat = Mat("WaterSpray", 2f, new Color(0.9f, 0.95f, 1f, 0.85f), 0.7f);
            Material foamMat = Mat("WaterFoam", 2f, new Color(0.92f, 0.96f, 1f, 0.42f), 0.75f);
            Material rippleMat = Mat("WaterRipple", 1f, new Color(0.88f, 0.94f, 1f, 0.55f), 0.75f);
            ParticleSystem spray = Spray(system, sprayMat);
            ParticleSystem foam = Foam(system, foamMat);
            ParticleSystem ripples = Ripples(system, rippleMat);
            AudioSource[] voices = Voices(system, 3);
            var splashClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SplashClipPath);
            var exitClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ExitClipPath);
            if (splashClip == null) log.AppendLine("  WARNING: no splash sound at " + SplashClipPath);
            if (exitClip == null) log.AppendLine("  WARNING: no climb-out sound at " + ExitClipPath);
            var fx = GetOrAdd<WaterSplashFx>(system.gameObject);
            fx.EditorSetup(spray, foam, ripples, splashClip, exitClip, voices);
            EditorUtility.SetDirty(fx);
            log.AppendLine($"  {RootName} under ///Manager///: surface {surface:F2} m over x {area.xMin:F0}..{area.xMax:F0}, z {area.yMin:F0}..{area.yMax:F0}; " +
                           $"{dryRoots.Count} dry ground roots; World Bounds {(bounds != null ? "linked" : "not found")}");

            // Animations.
            SwimAnimationBuilder.Build(out AnimationClip tread, out AnimationClip swim);
            AnimationClip climb = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ClimbClipModel))
                if (o is AnimationClip c && c.name == "ClimbUp") climb = c;
            if (climb == null) log.AppendLine("  WARNING: Invector ClimbUp clip not found: climbing out uses the swim pose");

            // Characters.
            foreach (PlayableCharacter character in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (EditorUtility.IsPersistent(character) || character.gameObject.scene != scene) continue;
                GameObject go = character.gameObject;
                var swimmer = GetOrAdd<PlayerSwimController>(go);
                swimmer.EditorSetup(waterManager, tread, swim, climb);
                EditorUtility.SetDirty(swimmer);
                SetReference(go.GetComponent<PlayerManager>(), "swimController", swimmer);
                SetReference(go.GetComponent<PlayerVehicleFallDamage>(), "swimController", swimmer);
                SetReference(go.GetComponent<PlayerVehicleImpact>(), "swimController", swimmer);
                log.AppendLine($"  {go.name}: PlayerSwimController (+ PlayerManager / PlayerVehicleFallDamage / PlayerVehicleImpact reference)");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        private static void SetReference(Object target, string property, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning($"[WaterSetup] '{property}' not found on {target.GetType().Name} ('{target.name}'): let the scripts finish compiling and run the setup again.", target);
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        // OpenworldCrime/WeatherParticle: shape 1 = ring, 2 = soft dot; no texture.
        private static Material Mat(string name, float shape, Color color, float ambientShare)
        {
            string path = ArtFolder + "/" + name + ".mat";
            Shader shader = Shader.Find("OpenworldCrime/WeatherParticle");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetFloat("_Shape", shape);
            m.SetColor("_Color", color);
            m.SetFloat("_AmbientShare", ambientShare);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, int maxParticles)
        {
            Transform t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true; // playing with no emission: splashes are Emit calls
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.sortMode = ParticleSystemSortMode.None;
            r.maxParticleSize = 2f;
            return ps;
        }

        private static ParticleSystem.MinMaxGradient FadeOut(float hold)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, hold), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // Drops thrown up from the impact: stretched by their speed, falling back under gravity.
        private static ParticleSystem Spray(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Splash Spray", material, 300);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
            main.gravityModifier = 1.3f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(0.6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1.3f;
            return ps;
        }

        // Soft white puffs over the impact (few, they are the big ones on screen).
        private static ParticleSystem Foam(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Splash Foam", material, 60);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.gravityModifier = 0.35f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(0.3f);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        // Flat rings spreading on the surface (splashes, swim strokes, wading).
        private static ParticleSystem Ripples(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Ripples", material, 80);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 1.8f);
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 2f), new Keyframe(1f, 1f, 0.3f, 0f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(0.1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.maxParticleSize = 10f;
            return ps;
        }

        private static AudioSource[] Voices(Transform parent, int count)
        {
            Transform holder = parent.Find("Splash Audio");
            if (holder == null)
            {
                holder = new GameObject("Splash Audio").transform;
                holder.SetParent(parent, false);
            }
            var voices = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                string voiceName = "Voice " + (i + 1);
                Transform t = holder.Find(voiceName);
                if (t == null)
                {
                    t = new GameObject(voiceName).transform;
                    t.SetParent(holder, false);
                }
                var source = t.GetComponent<AudioSource>();
                if (source == null) source = t.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 4f;
                source.maxDistance = 80f;
                voices[i] = source;
            }
            return voices;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
