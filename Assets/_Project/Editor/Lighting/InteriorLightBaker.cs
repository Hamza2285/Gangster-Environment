using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Bakes the shop interiors that had no baked lighting (the clothing store ran 24 realtime lights; the car showroom's lights
    /// were set to Baked but never baked, so they lit nothing). For each scene, opened on its own:
    /// - lamps (everything but the directional light) become Baked lights: their light lives in the lightmaps, free on a phone;
    /// - the room's architecture gets lightmap UVs; small props (hangers, shirts, shoes) and anything without lightmap UVs take
    ///   light probes instead, so nothing samples a lightmap it has no UVs for;
    /// - a light-probe grid (characters walking inside get the baked lamps too) and a baked reflection probe are added if missing;
    /// - its own lighting settings asset, then an async bake, then the scene is saved.
    /// The gun shop and the club were already baked. Menu: Tools > OpenworldCrime > Lighting > Bake Shop Interiors.
    /// Progress in Temp/interior_bake_status.txt.
    /// </summary>
    public static class InteriorLightBaker
    {
        private const string StatusFile = "Temp/interior_bake_status.txt";
        private const string SettingsFolder = "Assets/_Project/Art/Interiors";
        private const float SmallProp = 0.9f;   // largest side (m) below which a prop takes probes

        private sealed class Job
        {
            public string Scene;
            public string SettingsName;
            public string[] UnwrapMeshAssets;   // .asset meshes that are architecture and need lightmap UVs
            public bool BakeLamps;              // realtime lamps -> Baked
        }

        private static readonly Job[] Jobs =
        {
            new Job
            {
                Scene = "Assets/Scenes/Clothing Store Interior.unity",
                SettingsName = "ClothingStoreInterior",
                BakeLamps = true,
                UnwrapMeshAssets = new[]
                {
                    "Assets/Texture/City Mirage/Props Floor Walls Entrance 4K UVs.asset",
                    "Assets/Texture/City Mirage/Architecture Shared Detail Meshes.asset",
                    "Assets/Texture/City Mirage/Ceiling and Sign Detail.asset",
                    "Assets/Texture/City Mirage/Ceiling Structure Combined.asset",
                    "Assets/Texture/City Mirage/Entrance Feature Meshes.asset",
                    "Assets/Texture/City Mirage/Upper Banner Architecture UVs.asset",
                    "Assets/Texture/City Mirage/Rounded Display Wall Mirror.asset",
                    "Assets/Texture/City Mirage/Showcase Detail Meshes.asset",
                    "Assets/Texture/City Mirage/Stockroom Door Hardware.asset",
                },
            },
            new Job
            {
                Scene = "Assets/Scenes/Car Showroom Interior.unity",
                SettingsName = "CarShowroomInterior",
                BakeLamps = true,
                UnwrapMeshAssets = new string[0],
            },
        };

        private static int current = -1;
        private static string returnScene;
        private static readonly System.Text.StringBuilder Report = new System.Text.StringBuilder();

        [MenuItem("Tools/OpenworldCrime/Lighting/Bake Shop Interiors")]
        public static void Start()
        {
            if (Lightmapping.isRunning) { Status("busy: a bake is already running"); return; }
            EditorSceneManager.SaveOpenScenes();
            returnScene = SceneManager.GetActiveScene().path;
            Report.Clear();
            current = -1;
            Next();
        }

        private static void Next()
        {
            current++;
            if (current >= Jobs.Length)
            {
                if (!string.IsNullOrEmpty(returnScene)) EditorSceneManager.OpenScene(returnScene, OpenSceneMode.Single);
                Status("DONE\n" + Report);
                return;
            }
            Job job = Jobs[current];
            Scene scene = EditorSceneManager.OpenScene(job.Scene, OpenSceneMode.Single);
            Report.AppendLine(Path.GetFileNameWithoutExtension(job.Scene) + ": " + Prepare(scene, job));
            Status("baking " + Path.GetFileNameWithoutExtension(job.Scene) + " (" + (current + 1) + "/" + Jobs.Length + ")");
            Lightmapping.bakeCompleted -= OnBaked;
            Lightmapping.bakeCompleted += OnBaked;
            if (!Lightmapping.BakeAsync())
            {
                Lightmapping.bakeCompleted -= OnBaked;
                Report.AppendLine("  bake could not start");
                Next();
            }
        }

        private static void OnBaked()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (ReflectionProbe probe in root.GetComponentsInChildren<ReflectionProbe>(true))
                    if (probe.mode == ReflectionProbeMode.Baked) Lightmapping.BakeReflectionProbe(probe, ReflectionProbePath(scene, probe));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            int maps = LightmapSettings.lightmaps.Length;
            Report.AppendLine("  baked: " + maps + " lightmaps");
            // Let the editor breathe between scenes.
            EditorApplication.delayCall += Next;
        }

        private static string ReflectionProbePath(Scene scene, ReflectionProbe probe)
        {
            string folder = Path.Combine(Path.GetDirectoryName(scene.path), scene.name).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(scene.path).Replace('\\', '/'), scene.name);
            return folder + "/ReflectionProbe-" + probe.name.Replace(' ', '_') + ".exr";
        }

        private static string Prepare(Scene scene, Job job)
        {
            var log = new System.Text.StringBuilder();
            var unwrap = new HashSet<Mesh>();
            foreach (string path in job.UnwrapMeshAssets)
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh mesh) unwrap.Add(mesh);

            int unwrapped = 0, lamps = 0, toMaps = 0, toProbes = 0;
            var done = new HashSet<Mesh>();
            Bounds bounds = default;
            bool hasBounds = false;
            var rooms = new List<GameObject>(scene.GetRootGameObjects());

            foreach (GameObject root in rooms)
            {
                if (job.BakeLamps)
                {
                    foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    {
                        if (light.type == LightType.Directional || light.lightmapBakeType == LightmapBakeType.Baked) continue;
                        light.lightmapBakeType = LightmapBakeType.Baked;
                        if (light.shadows == LightShadows.None) light.shadows = LightShadows.Soft;   // baked shadows cost nothing
                        EditorUtility.SetDirty(light);
                        lamps++;
                    }
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null) continue;
                    if (!GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI)) continue;

                    if (unwrap.Contains(mesh) && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1) && done.Add(mesh))
                    {
                        if (Unwrap(mesh)) unwrapped++;
                    }

                    Vector3 size = renderer.bounds.size;
                    bool small = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallProp;
                    bool canMap = mesh.HasVertexAttribute(VertexAttribute.TexCoord1) && !small;
                    renderer.receiveGI = canMap ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                    if (!canMap && renderer.lightProbeUsage == LightProbeUsage.Off) renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    EditorUtility.SetDirty(renderer);
                    if (canMap) toMaps++; else toProbes++;

                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }
            AssetDatabase.SaveAssets();

            if (hasBounds)
            {
                bool hasProbes = false, hasReflection = false;
                foreach (GameObject root in rooms)
                {
                    hasProbes |= root.GetComponentInChildren<LightProbeGroup>(true) != null;
                    hasReflection |= root.GetComponentInChildren<ReflectionProbe>(true) != null;
                }
                if (!hasProbes) { AddProbeGrid(scene, bounds); log.Append("probe grid added, "); }
                if (!hasReflection) { AddReflectionProbe(scene, bounds); log.Append("reflection probe added, "); }
            }

            Lightmapping.SetLightingSettingsForScene(scene, GetSettings(job.SettingsName));
            log.Append(lamps + " lamps baked, " + unwrapped + " meshes unwrapped, " + toMaps + " lightmapped, " + toProbes + " on probes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return log.ToString();
        }

        // The store's meshes are saved non-readable: readable just long enough to generate their lightmap UVs.
        private static bool Unwrap(Mesh mesh)
        {
            var so = new SerializedObject(mesh);
            SerializedProperty readable = so.FindProperty("m_IsReadable");
            bool wasReadable = readable == null || readable.boolValue;
            if (!wasReadable) { readable.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            bool ok = false;
            try
            {
                Unwrapping.GenerateSecondaryUVSet(mesh);
                ok = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[InteriorLightBaker] no lightmap UVs for " + mesh.name + ": " + e.Message);
            }
            if (!wasReadable)
            {
                so.Update();
                so.FindProperty("m_IsReadable").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(mesh);
            return ok;
        }

        // Probes every ~2 m over the floor area, at knee, chest and head-and-above heights: enough for walking characters.
        private static void AddProbeGrid(Scene scene, Bounds bounds)
        {
            var go = new GameObject("Interior Light Probes");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Vector3.zero;
            var group = go.AddComponent<LightProbeGroup>();
            var points = new List<Vector3>();
            int nx = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / 2f) + 1);
            int nz = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / 2f) + 1);
            float floor = bounds.min.y;
            float[] heights = { 0.4f, 1.4f, Mathf.Min(2.8f, Mathf.Max(2f, bounds.size.y - 0.3f)) };
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                    foreach (float h in heights)
                        points.Add(new Vector3(Mathf.Lerp(bounds.min.x + 0.5f, bounds.max.x - 0.5f, x / (float)(nx - 1)), floor + h,
                                               Mathf.Lerp(bounds.min.z + 0.5f, bounds.max.z - 0.5f, z / (float)(nz - 1))));
            group.probePositions = points.ToArray();
        }

        private static void AddReflectionProbe(Scene scene, Bounds bounds)
        {
            var go = new GameObject("Interior Reflection Probe");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(bounds.center.x, bounds.min.y + 1.6f, bounds.center.z);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = bounds.size + Vector3.one;
            probe.center = bounds.center - go.transform.position;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.importance = 2;
        }

        private static LightingSettings GetSettings(string name)
        {
            string path = SettingsFolder + "/" + name + ".lighting";
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s == null)
            {
                s = new LightingSettings { name = name };
                AssetDatabase.CreateAsset(s, path);
            }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.IndirectOnly;
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.lightmapResolution = 10f;
            s.lightmapPadding = 4;
            s.lightmapMaxSize = 1024;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            s.directionalityMode = LightmapsMode.NonDirectional;
            s.ao = true;
            s.aoMaxDistance = 0.6f;
            s.aoExponentIndirect = 0.8f;
            s.aoExponentDirect = 0f;
            s.maxBounces = 2;
            s.directSampleCount = 32;
            s.indirectSampleCount = 256;
            s.environmentSampleCount = 128;
            s.filteringMode = LightingSettings.FilterMode.Advanced;
            s.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage;
            s.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage;
            s.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
            s.autoGenerate = false;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            return s;
        }

        private static void Status(string text)
        {
            File.WriteAllText(StatusFile, text);
            Debug.Log("[InteriorLightBaker] " + text);
        }
    }
}
