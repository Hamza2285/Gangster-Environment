using System.Collections.Generic;
using System.IO;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Places reflection probes along the streets cars drive on and bakes each of them twice, by day and by night, for the car
    /// paint's reflections (<see cref="ReflectionProbeDayNight"/> switches the two sets at runtime).
    /// - Placement: the iTS lanes are binned into Cell Size squares; each square with road in it gets one probe on the road point
    ///   nearest its centre, 2 m up (car height). Its influence box covers its square plus Blend Distance, so neighbouring probes
    ///   blend instead of popping. No box projection: the boxes follow a grid, not the street walls, and a wrong box warps the
    ///   reflection more than the small parallax error of an infinitely distant capture on a 120 m square.
    /// - The scene's own city-wide probes (any other ReflectionProbe in the scene) are baked with both sets too, as the fallback
    ///   outside the street squares; the street probes have a higher importance, so they win wherever they reach.
    /// - Night: CityDayNightController.Apply(true), and for every probe the street lamps nearest to it are switched on before its
    ///   capture (the controller only lights the lamps around one camera at a time).
    /// - Textures: custom baked EXR cubemaps, baked and imported at 64 px with mipmaps and compression (ASTC 6x6 on Android).
    ///   Each capture's HDR outliers are soft-clamped (SoftClampCubemapFile) before import: unclamped, the night crossing glow
    ///   and the lamps (values 20-35) and the sun (half-float overflow) tinted every probe-lit surface yellow-green / red.
    /// Runs on the editor update loop (a few probes per tick); progress in the status bar and Temp/car_probe_bake_status.txt.
    /// The day/night state of the scene is restored when it finishes. Menu: Tools > OpenworldCrime > Lighting > Bake Car Reflection Probes.
    /// </summary>
    public static class CarReflectionProbeBaker
    {
        private const string RootName = "CarReflectionProbes";
        private const string OutputFolder = "Assets/_Project/Lighting/CarReflectionProbes";
        private const string StatusFile = "Temp/car_probe_bake_status.txt";
        private const float CellSize = 120f;
        private const float BlendDistance = 20f;
        private const float ProbeHeight = 2f;
        private const float BoxHeight = 60f;
        private const int MinLanePoints = 8;
        // Baked at the size the cubemaps are imported at: a bigger bake was only downscaled on import (and bloated the EXRs).
        private const int BakeResolution = 64;
        private const int ImportSize = 64;
        // HDR soft clamp of the captures (see SoftClampCubemapFile): values pass unchanged up to the knee and approach the limit.
        private const float HdrKnee = 0.8f;
        private const float HdrLimit = 2f;
        private const float HighlightDesaturation = 0.6f;
        // Smallest of ZIP / PIZ / RLE on these cubemaps.
        private const Texture2D.EXRFlags ExrCompression = Texture2D.EXRFlags.CompressZIP;
        private const int StreetImportance = 2;
        private const int ProbesPerTick = 2;

        private static List<ReflectionProbe> queue;
        private static int index;
        private static bool night;
        private static bool restoreNight;
        private static double startTime;
        private static double dayTime;
        private static Camera lampCamera;

        public static bool IsRunning => queue != null;

        [MenuItem("Tools/OpenworldCrime/Lighting/Bake Car Reflection Probes")]
        public static void BakeMenu() => Debug.Log(Start());

        public static string Start()
        {
            if (IsRunning) return "[CarReflectionProbeBaker] Already baking.";
            if (EditorApplication.isPlaying) return "[CarReflectionProbeBaker] Stop play mode first.";
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            var roads = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
            if (controller == null || roads == null || roads.lanes == null) return "[CarReflectionProbeBaker] Needs a CityDayNightController and the iTS TSMainManager in the open scene.";

            List<ReflectionProbe> probes = PlaceProbes(roads);
            foreach (ReflectionProbe other in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!probes.Contains(other)) probes.Add(other);
            }
            EnsureFolder(OutputFolder + "/Day");
            EnsureFolder(OutputFolder + "/Night");

            restoreNight = controller.IsNight;
            controller.Apply(false);
            queue = probes;
            index = 0;
            night = false;
            startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Status($"day: 0/{queue.Count}");
            return $"[CarReflectionProbeBaker] Baking {queue.Count} probes by day, then by night. Progress: status bar / {StatusFile}.";
        }

        private static List<ReflectionProbe> PlaceProbes(TSMainManager roads)
        {
            GameObject rootObject = GameObject.Find(RootName);
            if (rootObject == null) rootObject = new GameObject(RootName);
            Transform root = rootObject.transform;
            for (int c = root.childCount - 1; c >= 0; c--) Object.DestroyImmediate(root.GetChild(c).gameObject);

            var cells = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (TSLaneInfo lane in roads.lanes)
            {
                if (lane == null || lane.points == null) continue;
                foreach (TSPoints point in lane.points)
                {
                    var key = new Vector2Int(Mathf.FloorToInt(point.point.x / CellSize), Mathf.FloorToInt(point.point.z / CellSize));
                    if (!cells.TryGetValue(key, out List<Vector3> list)) cells[key] = list = new List<Vector3>();
                    list.Add(point.point);
                }
            }

            var probes = new List<ReflectionProbe>();
            var keys = new List<Vector2Int>(cells.Keys);
            keys.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (Vector2Int key in keys)
            {
                List<Vector3> points = cells[key];
                if (points.Count < MinLanePoints) continue;
                var centre = new Vector3((key.x + 0.5f) * CellSize, 0f, (key.y + 0.5f) * CellSize);
                Vector3 best = points[0];
                float bestSqr = float.MaxValue;
                foreach (Vector3 p in points)
                {
                    float sqr = (p.x - centre.x) * (p.x - centre.x) + (p.z - centre.z) * (p.z - centre.z);
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = p;
                    }
                }
                var go = new GameObject($"CarProbe_{key.x}_{key.y}");
                go.transform.SetParent(root, false);
                go.transform.position = best + Vector3.up * ProbeHeight;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.resolution = BakeResolution;
                probe.hdr = true;
                probe.importance = StreetImportance;
                probe.boxProjection = false;
                probe.blendDistance = BlendDistance;
                probe.size = new Vector3(CellSize + BlendDistance * 2f, BoxHeight, CellSize + BlendDistance * 2f);
                probe.center = new Vector3(centre.x, best.y + ProbeHeight, centre.z) - go.transform.position;
                probe.nearClipPlane = 0.3f;
                probe.farClipPlane = 800f;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probes.Add(probe);
            }
            return probes;
        }

        private static void Tick()
        {
            if (queue == null) return;
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Finish("controller disappeared");
                return;
            }
            for (int n = 0; n < ProbesPerTick && index < queue.Count; n++, index++)
            {
                ReflectionProbe probe = queue[index];
                if (probe == null) continue;
                if (night) LightLampsAround(controller, probe.transform.position);
                Lightmapping.BakeReflectionProbe(probe, TexturePath(night, index));
                SoftClampCubemapFile(TexturePath(night, index));
            }
            EditorUtility.DisplayProgressBar("Car reflection probes", $"{(night ? "night" : "day")} {index}/{queue.Count}", (index + (night ? queue.Count : 0)) / (2f * queue.Count));
            Status($"{(night ? "night" : "day")}: {index}/{queue.Count} ({EditorApplication.timeSinceStartup - startTime:F0} s)");
            if (index < queue.Count) return;

            if (!night)
            {
                dayTime = EditorApplication.timeSinceStartup - startTime;
                night = true;
                index = 0;
                controller.Apply(true);
                return;
            }
            Finish(null);
        }

        // The controller lights only the lamps nearest one camera: a camera at the probe gets the lamps around it lit for its capture.
        private static void LightLampsAround(CityDayNightController controller, Vector3 position)
        {
            if (lampCamera == null)
            {
                var go = new GameObject("CarProbeLampCamera") { hideFlags = HideFlags.HideAndDontSave };
                lampCamera = go.AddComponent<Camera>();
                lampCamera.enabled = false;
            }
            lampCamera.transform.position = position;
            controller.RefreshStreetLights(lampCamera);
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            if (lampCamera != null) Object.DestroyImmediate(lampCamera.gameObject);
            List<ReflectionProbe> probes = queue;
            queue = null;
            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (error != null)
            {
                if (controller != null) controller.Apply(restoreNight);
                Status("failed: " + error);
                Debug.LogError("[CarReflectionProbeBaker] " + error);
                return;
            }

            AssetDatabase.Refresh();
            var day = new Texture[probes.Count];
            var nightSet = new Texture[probes.Count];
            long bytes = 0;
            for (int i = 0; i < probes.Count; i++)
            {
                day[i] = Import(TexturePath(false, i));
                nightSet[i] = Import(TexturePath(true, i));
                if (day[i] != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(day[i]);
                if (nightSet[i] != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(nightSet[i]);
                if (probes[i] == null) continue;
                probes[i].mode = ReflectionProbeMode.Custom;
                probes[i].customBakedTexture = restoreNight ? nightSet[i] : day[i];
                EditorUtility.SetDirty(probes[i]);
            }

            GameObject rootObject = GameObject.Find(RootName);
            var switcher = rootObject.GetComponent<ReflectionProbeDayNight>();
            if (switcher == null) switcher = rootObject.AddComponent<ReflectionProbeDayNight>();
            switcher.EditorSetup(controller, probes.ToArray(), day, nightSet);
            EditorUtility.SetDirty(switcher);
            if (controller != null) controller.Apply(restoreNight);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);

            double total = EditorApplication.timeSinceStartup - startTime;
            string report = $"done: {probes.Count} probes x 2 sets, day {dayTime:F0} s, night {total - dayTime:F0} s, total {total:F0} s; " +
                            $"cubemaps in memory (editor platform) {bytes / 1024f / 1024f:F2} MB";
            Status(report);
            Debug.Log("[CarReflectionProbeBaker] " + report);
        }

        /// <summary>
        /// Rewrites a baked cubemap EXR with its HDR outliers soft-clamped (brightest channel compressed above HdrKnee towards
        /// HdrLimit, hue kept; NaN / infinity become HdrLimit). Without this a few texels decide the colour of a whole probe:
        /// the night crossing glow of the road atlas and the street lamps lit for the capture reach 20-35, the sun disk
        /// overflows half floats (65504), and the blurred mips the glossy reflection reads smear that over every direction.
        /// The file is written ZIP-compressed at the bake resolution. clamp = false only re-encodes (round-trip test).
        /// </summary>
        public static void SoftClampCubemapFile(string path, bool clamp = true)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null) return;

            // A Gamma project converts HDR texels to gamma space on import; the file holds linear values.
            bool gamma = PlayerSettings.colorSpace == ColorSpace.Gamma;
            int size = cube.width;
            var strip = new Texture2D(size * 6, size, TextureFormat.RGBAHalf, false, true);
            var row = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                Color[] pixels = cube.GetPixels((CubemapFace)face);
                // Cubemap faces are stored top row first, Texture2D rows bottom first.
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Color c = pixels[y * size + x];
                        if (gamma) c = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), 1f);
                        row[(size - 1 - y) * size + x] = clamp ? SoftClamp(c) : c;
                    }
                }
                strip.SetPixels(face * size, 0, size, size, row);
            }
            strip.Apply(false);
            File.WriteAllBytes(path, strip.EncodeToEXR(ExrCompression));
            Object.DestroyImmediate(strip);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static Color SoftClamp(Color c)
        {
            float r = Finite(c.r), g = Finite(c.g), b = Finite(c.b);
            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max <= HdrKnee) return new Color(r, g, b, 1f);
            float range = HdrLimit - HdrKnee;
            float compressed = HdrKnee + range * (1f - Mathf.Exp(-(max - HdrKnee) / range));
            float scale = compressed / max;
            r *= scale;
            g *= scale;
            b *= scale;
            // The more a highlight is compressed, the closer to white it gets (as a burnt-out highlight does), so a glowing
            // marking reads as a bright reflection rather than a coloured stain.
            float luma = r * 0.2126f + g * 0.7152f + b * 0.0722f;
            float t = (1f - scale) * HighlightDesaturation;
            return new Color(Mathf.Lerp(r, luma, t), Mathf.Lerp(g, luma, t), Mathf.Lerp(b, luma, t), 1f);
        }

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? HdrLimit : Mathf.Max(0f, v);

        private static Texture Import(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.isReadable = false;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = ImportSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = ImportSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture>(path);
        }

        private static string TexturePath(bool atNight, int i) => $"{OutputFolder}/{(atNight ? "Night" : "Day")}/CarProbe_{i:000}.exr";

        private static void Status(string text)
        {
            File.WriteAllText(StatusFile, text);
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
