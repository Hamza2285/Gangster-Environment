using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Random rain and lightning for the city (Tools > OpenworldCrime > Lighting > Set Up Weather (rain, lightning)):
    /// materials for the weather particles / wet ground / bolt, a generated lightning bolt texture, and the "Weather" object
    /// under ///Manager/// (WeatherManager, WetGround, PlayerWetness, rain / splash / drip particle systems, bolt quad, rain and
    /// thunder audio). Rebuilds the object if it exists. No lighting / URP / quality settings are touched.
    /// Sounds: the project's rain loop (AudioClip 1/light-rain-109591.ogg). There is no thunder recording in the project: the
    /// thunder is a placeholder, a large explosion played low and slow through a low-pass filter (replace the clip when one exists).
    /// </summary>
    public static class WeatherSetup
    {
        private const string Folder = "Assets/_Project/Art/Weather";
        private const string RainClip = "Assets/AudioClip 1/light-rain-109591.ogg";
        private const string ThunderPlaceholder = "Assets/AudioClip 1/fire_explosion_huge.ogg";
        private const string Noise = "Assets/_Project/Art/Sky/CloudNoise.png";

        [MenuItem("Tools/OpenworldCrime/Lighting/Set Up Weather (rain, lightning) (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[WeatherSetup]\n");
            if (EditorApplication.isPlaying) return log.Append("  stop play mode first").ToString();
            var dayNight = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (dayNight == null) return log.Append("  no CityDayNightController in the open scene").ToString();
            var scene = dayNight.gameObject.scene;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Weather");

            Material rainMat = Mat("Rain", "OpenworldCrime/WeatherParticle", m => { m.SetFloat("_Shape", 0f); m.SetColor("_Color", new Color(0.8f, 0.85f, 0.95f, 0.5f)); });
            Material splashMat = Mat("Splash", "OpenworldCrime/WeatherParticle", m => { m.SetFloat("_Shape", 1f); m.SetColor("_Color", new Color(0.85f, 0.9f, 1f, 0.45f)); });
            Material dripMat = Mat("Drip", "OpenworldCrime/WeatherParticle", m => { m.SetFloat("_Shape", 2f); m.SetColor("_Color", new Color(0.85f, 0.92f, 1f, 0.7f)); m.SetFloat("_AmbientShare", 0.6f); });
            Material wetMat = Mat("WetGround", "OpenworldCrime/WetGround", m => { m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Noise)); m.SetFloat("_Darken", 0.4f); m.SetFloat("_Sheen", 0.35f); });
            Texture2D boltTex = MakeBolt();
            Material boltMat = Mat("LightningBolt", "OpenworldCrime/LightningBolt", m => m.SetTexture("_MainTex", boltTex));

            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            var old = Object.FindFirstObjectByType<WeatherManager>(FindObjectsInactive.Include);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Weather");
            if (managers != null) go.transform.SetParent(managers.transform, false);
            else UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

            ParticleSystem rain = Rain(go.transform, rainMat);
            ParticleSystem splashes = Splashes(go.transform, splashMat);
            ParticleSystem drips = Drips(go.transform, dripMat);

            var boltGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            boltGo.name = "Lightning Bolt";
            Object.DestroyImmediate(boltGo.GetComponent<Collider>());
            boltGo.transform.SetParent(go.transform, false);
            var boltRenderer = boltGo.GetComponent<MeshRenderer>();
            boltRenderer.sharedMaterial = boltMat;
            boltRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boltRenderer.receiveShadows = false;
            boltRenderer.enabled = false;

            var loop = NewAudio(go.transform, "Rain Audio", AssetDatabase.LoadAssetAtPath<AudioClip>(RainClip), true);
            var thunder = NewAudio(go.transform, "Thunder Audio", null, false);
            var lowPass = thunder.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 700f;
            var thunderClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ThunderPlaceholder);

            var manager = go.AddComponent<WeatherManager>();
            manager.EditorSetup(dayNight, Object.FindFirstObjectByType<SkyClouds>(FindObjectsInactive.Include), rain, splashes, loop, thunder,
                thunderClip != null ? new[] { thunderClip } : new AudioClip[0], boltRenderer);
            var wet = go.AddComponent<WetGround>();
            wet.EditorSetup(manager, wetMat, 1); // Default only: not parked cars (RCC layer), invisible blockers or props on other layers
            var playerWet = go.AddComponent<PlayerWetness>();
            playerWet.EditorSetup(manager, drips);

            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("  Weather under " + (managers != null ? "///Manager///" : "scene root"));
            log.AppendLine("  rain loop: " + (loop.clip != null ? loop.clip.name : "MISSING") + "; thunder: " + (thunderClip != null ? thunderClip.name + " (placeholder: no thunder clip in the project)" : "none"));
            return log.ToString();
        }

        private static Material Mat(string name, string shader, System.Action<Material> setup)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader s = Shader.Find(shader);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
            m.shader = s;
            setup(m);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.sortMode = ParticleSystemSortMode.None;
            return ps;
        }

        // Thin stretched streaks falling through a box above the camera; no collisions.
        private static ParticleSystem Rain(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Rain", material);
            var main = ps.main;
            main.startLifetime = 1.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.maxParticles = 1600;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 1f, 34f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.0f, 1.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-23f, -26f);
            vel.z = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            return ps;
        }

        // Small flat rings on the ground ahead of the camera.
        private static ParticleSystem Splashes(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Splashes", material);
            var main = ps.main;
            main.startLifetime = 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.maxParticles = 70;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 0.01f, 16f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1.2f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
        }

        // A few drops falling off the wet character (parented to the chest at runtime).
        private static ParticleSystem Drips(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Player Drips", material);
            var main = ps.main;
            main.startLifetime = 0.55f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.maxParticles = 12;
            main.gravityModifier = 1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.24f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1f;
            return ps;
        }

        private static AudioSource NewAudio(Transform parent, string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = loop;
            a.playOnAwake = false;
            a.spatialBlend = 0f;
            a.volume = loop ? 0f : 1f;
            return a;
        }

        // A jagged bolt with a couple of branches and a soft glow, drawn once into a 128 x 512 texture.
        private static Texture2D MakeBolt()
        {
            string path = Folder + "/LightningBolt.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                const int w = 128, h = 512;
                var field = new float[w, h];
                var rnd = new System.Random(7);
                DrawBranch(field, rnd, new Vector2(w * 0.5f, h - 4), 0f, 24, 1f, w, h);
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float v = Mathf.Clamp01(field[x, y]);
                        t.SetPixel(x, y, new Color(v, v, v, v));
                    }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 512;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void DrawBranch(float[,] field, System.Random rnd, Vector2 start, float drift, int segments, float strength, int w, int h)
        {
            Vector2 p = start;
            float segLen = (h - 8f) / 24f;
            for (int s = 0; s < segments && p.y > 4; s++)
            {
                Vector2 next = p + new Vector2(((float)rnd.NextDouble() - 0.5f) * segLen * 1.3f + drift, -segLen);
                next.x = Mathf.Clamp(next.x, 6, w - 6);
                Line(field, p, next, strength, w, h);
                if (strength > 0.5f && rnd.NextDouble() < 0.14)
                    DrawBranch(field, rnd, next, (rnd.NextDouble() < 0.5 ? -1f : 1f) * segLen * 0.5f, 5 + rnd.Next(5), strength * 0.55f, w, h);
                p = next;
            }
        }

        private static void Line(float[,] field, Vector2 a, Vector2 b, float strength, int w, int h)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2f);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
                for (int dy = -6; dy <= 6; dy++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = (d < 1.2f ? 1f : 0f) + Mathf.Exp(-d * d / 10f) * 0.45f;
                        field[x, y] = Mathf.Max(field[x, y], v * strength);
                    }
            }
        }
    }
}
