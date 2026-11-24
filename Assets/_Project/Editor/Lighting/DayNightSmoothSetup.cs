using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Smooth day/night for the city scene (Tools > OpenworldCrime > Lighting > Set Up Smooth Day Night And Clouds):
    /// - the baked lighting crossfade shader on our BakedLightingSwitcher;
    /// - a NightGlowFader for the controller's lamp-bulb mesh (Hamza's material untouched: property block at runtime);
    /// - the sky clouds: a tileable noise texture, the OpenworldCrime/SkyClouds material and a SkyClouds object under ///Manager///;
    /// - the sky cycle's day length back to its default (a 1-minute test value made dusk last seconds).
    /// No lighting / URP / quality / lightmap settings are touched.
    /// </summary>
    public static class DayNightSmoothSetup
    {
        private const string ArtFolder = "Assets/_Project/Art/Sky";
        private const string NoisePath = ArtFolder + "/CloudNoise.png";
        private const string MaterialPath = ArtFolder + "/SkyClouds.mat";
        private const float DayLengthMinutes = 16f;

        [MenuItem("Tools/OpenworldCrime/Lighting/Set Up Smooth Day Night And Clouds (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[DayNightSmoothSetup]\n");
            if (EditorApplication.isPlaying) return log.Append("  stop play mode first").ToString();
            var dayNight = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (dayNight == null) return log.Append("  no CityDayNightController in the open scene").ToString();
            var scene = dayNight.gameObject.scene;

            // Baked lighting crossfade.
            var switcher = Object.FindFirstObjectByType<BakedLightingSwitcher>(FindObjectsInactive.Include);
            if (switcher != null)
            {
                var so = new SerializedObject(switcher);
                so.FindProperty("blendShader").objectReferenceValue = Shader.Find("Hidden/OpenworldCrime/LightmapBlend");
                so.ApplyModifiedProperties();
                log.AppendLine("  BakedLightingSwitcher: crossfade shader " + (so.FindProperty("blendShader").objectReferenceValue != null ? "set" : "MISSING"));
            }

            // Lamp bulbs fade.
            var fader = switcher != null ? switcher.GetComponent<NightGlowFader>() : null;
            GameObject faderHost = switcher != null ? switcher.gameObject : dayNight.gameObject;
            if (fader == null) fader = Undo.AddComponent<NightGlowFader>(faderHost);
            var entries = dayNight.nightLampBulbs != null
                ? new[] { new NightGlowFader.Entry { renderer = dayNight.nightLampBulbs, colorProperty = "_Color", window = new Vector2(0.12f, 0.62f), offShare = 0.03f } }
                : new NightGlowFader.Entry[0];
            fader.EditorSetup(dayNight, entries);
            EditorUtility.SetDirty(fader);
            log.AppendLine($"  NightGlowFader on '{faderHost.name}': {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}");

            // Sky cycle day length.
            var cycle = Object.FindFirstObjectByType<SkyCycleController>(FindObjectsInactive.Include);
            if (cycle != null)
            {
                var so = new SerializedObject(cycle);
                float before = so.FindProperty("dayLengthMinutes").floatValue;
                so.FindProperty("dayLengthMinutes").floatValue = DayLengthMinutes;
                so.ApplyModifiedProperties();
                log.AppendLine($"  SkyCycleController day length {before} -> {DayLengthMinutes} min");
            }

            // Clouds.
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Sky");
            MakeNoise();
            Shader cloudShader = Shader.Find("OpenworldCrime/SkyClouds");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) { mat = new Material(cloudShader); AssetDatabase.CreateAsset(mat, MaterialPath); }
            mat.shader = cloudShader;
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);

            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            var clouds = Object.FindFirstObjectByType<SkyClouds>(FindObjectsInactive.Include);
            if (clouds == null)
            {
                var go = new GameObject("Sky Clouds");
                Undo.RegisterCreatedObjectUndo(go, "Sky Clouds");
                if (managers != null) go.transform.SetParent(managers.transform, false);
                else SceneManager_MoveToScene(go, scene);
                clouds = go.AddComponent<SkyClouds>();
            }
            clouds.EditorSetup(mat, cycle, dayNight);
            EditorUtility.SetDirty(clouds);
            log.AppendLine("  SkyClouds: " + clouds.name + (managers != null ? " under ///Manager///" : ""));

            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        private static void SceneManager_MoveToScene(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        // Tileable fractal noise: R = large soft shapes, G = finer detail. 256 px, wraps on both axes.
        private static void MakeNoise()
        {
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var rnd = new System.Random(1234);
            float[,] lattice = new float[64, 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) lattice[x, y] = (float)rnd.NextDouble();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float big = Fbm(lattice, u, v, 4, 4, 0.55f);
                    float detail = Fbm(lattice, u + 0.37f, v + 0.11f, 16, 4, 0.5f);
                    t.SetPixel(x, y, new Color(Contrast(big), Contrast(detail), 0f, 1f));
                }
            File.WriteAllBytes(NoisePath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(NoisePath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(NoisePath);
            ti.sRGBTexture = false;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 256;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }

        private static float Contrast(float v) => Mathf.Clamp01((v - 0.5f) * 1.6f + 0.5f);

        private static float Fbm(float[,] lattice, float u, float v, int baseFreq, int octaves, float gain)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(lattice, u * freq, v * freq, freq);
                norm += amp;
                amp *= gain;
                freq *= 2;
            }
            return sum / norm;
        }

        // Smooth value noise, periodic with 'period' cells (tileable).
        private static float Value(float[,] lattice, float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Cell(lattice, x0, y0, period), b = Cell(lattice, x0 + 1, y0, period);
            float c = Cell(lattice, x0, y0 + 1, period), d = Cell(lattice, x0 + 1, y0 + 1, period);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Cell(float[,] lattice, int x, int y, int period)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            return lattice[x % 64, y % 64];
        }
    }
}
