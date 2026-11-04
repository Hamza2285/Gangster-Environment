using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RooftopSearchlightSetup
{
    const string Request = "RooftopSearchlightSetup.request";
    const string ScenePath = "Assets/Scenes/Oasis/MobileOptimization/Gangstar City.unity";
    const string MaterialPath = "Assets/Scenes/Oasis/CityBuildingSigns/City Shared Sign Atlas.mat";
    const string MeshPath = "Assets/Scenes/Oasis/MobileOptimization/Rooftop Searchlight Beam.asset";
    const string PrefabPath = "Assets/Scenes/Oasis/MobileOptimization/Rooftop Searchlight.prefab";
    const string ReportPath = "FinalEnvironmentRoadReview/RooftopSearchlights.txt";
    static readonly string[] TargetNames = {
        "hivizcayne (5)", "hivizcayne (3)", "hivizcayne (2)", "hivizcayne (4)",
        "hivizcayne", "hivizcayne (6)", "hirise7 (1)", "hirise7",
        "Building_06_01_Material #26_0 (1)", "Building_06_01_Material #26_0"
    };

    static RooftopSearchlightSetup() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Build(); }
        catch (Exception e) { File.WriteAllText(ReportPath, "FAILED: " + e); Debug.LogException(e); }
    }

    static void Build()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new Exception("Open the MobileOptimization city scene before placing rooftop lights.");
        if (SceneManager.GetActiveScene() != scene) EditorSceneManager.SetActiveScene(scene);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material || material.shader.name != "City/Mobile Shared Sign Atlas") throw new Exception("Expected shared city-sign atlas material/shader was not found.");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (!mesh)
        {
            mesh = BuildBeamMesh();
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        else
        {
            var revisedBeamMesh = BuildBeamMesh();
            EditorUtility.CopySerialized(revisedBeamMesh, mesh);
            UnityEngine.Object.DestroyImmediate(revisedBeamMesh);
            EditorUtility.SetDirty(mesh);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab)
        {
            var go = new GameObject("Rooftop Searchlight");
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var rotator = go.AddComponent<RooftopSearchlightRotator>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        var lods = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LODGroup>(true)).ToArray();
        var report = new StringBuilder();
        report.AppendLine("Target scene: " + ScenePath);
        report.AppendLine("Shared material: " + material.name + " | shader: " + material.shader.name);
        report.AppendLine("Beam mesh: " + mesh.vertexCount + " vertices / " + (mesh.triangles.Length / 3) + " triangles, one renderer, no lights.");
        int placed = 0;
        var usedRooftops = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < TargetNames.Length; i++)
        {
            var lod = lods.FirstOrDefault(x => x.gameObject.name == TargetNames[i]);
            // If the hospital facade is only a child renderer rather than the LODGroup
            // name, select the nearest unused building-sized LODGroup for the tenth light.
            if (!lod && i == TargetNames.Length - 1)
            {
                lod = lods.Where(x => !usedRooftops.Contains(x.GetInstanceID()))
                    .Where(x => x.GetLODs().Length > 0 && x.GetLODs()[0].renderers.Any(r => r && r.enabled))
                    .Select(x => new { Group = x, Bounds = GetLod0Bounds(x) })
                    .Where(x => x.Bounds.size.y >= 20f)
                    .OrderBy(x => (new Vector2(x.Bounds.center.x + 5.1f, x.Bounds.center.z + 351.9f)).sqrMagnitude)
                    .Select(x => x.Group)
                    .FirstOrDefault();
            }
            if (!lod) throw new Exception("Could not locate rooftop target: " + TargetNames[i]);
            usedRooftops.Add(lod.GetInstanceID());
            var renderers = lod.GetLODs().First().renderers.Where(r => r && r.enabled).ToArray();
            if (renderers.Length == 0) throw new Exception("No visible LOD0 renderers on " + TargetNames[i]);
            var bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);

            var instanceName = "Rooftop Searchlight - " + TargetNames[i];
            var existing = scene.GetRootGameObjects().FirstOrDefault(x => x.name == instanceName);
            var instance = existing ? existing : PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (!instance) throw new Exception("Could not instantiate reusable beam prefab.");
            instance.name = instanceName;
            instance.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + .65f, bounds.center.z), Quaternion.identity);
            float scale = Mathf.Clamp(bounds.size.y / 75f, .58f, 1.15f);
            instance.transform.localScale = new Vector3(scale, scale, scale);
            var rotatorComponent = instance.GetComponent<RooftopSearchlightRotator>();
            rotatorComponent.rotationSpeed = (i % 2 == 0 ? 1f : -1f) * (15f + (i % 4) * 2.2f);
            EditorSceneManager.MarkSceneDirty(scene);
            report.AppendLine($"Placed {instanceName} at ({bounds.center.x:F1}, {bounds.max.y + .65f:F1}, {bounds.center.z:F1}); roof bounds {bounds.size.x:F1} x {bounds.size.z:F1}, height {bounds.size.y:F1}.");
            placed++;
        }
        if (placed != 10) throw new Exception("Expected ten rooftop lights; placed " + placed);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Unity did not save the target scene.");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        report.AppendLine("SUCCESS: 10 mobile-friendly blue/pink rooftop beams; existing atlas material, day/night global, and target scene only.");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    static Bounds GetLod0Bounds(LODGroup group)
    {
        var renderers = group.GetLODs().First().renderers.Where(r => r && r.enabled).ToArray();
        if (renderers.Length == 0) return new Bounds(group.transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Mesh BuildBeamMesh()
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var colors = new System.Collections.Generic.List<Color>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var triangles = new System.Collections.Generic.List<int>();
        AddFan(Quaternion.identity, new Color(.10f, .36f, 1f, 1f));
        AddFan(Quaternion.Euler(0, 90, 0), new Color(.10f, .36f, 1f, 1f));
        AddFan(Quaternion.Euler(0, 45, 0), new Color(1f, .10f, .48f, 1f));
        AddFan(Quaternion.Euler(0, 135, 0), new Color(1f, .10f, .48f, 1f));
        var mesh = new Mesh { name = "Rooftop Searchlight Beam (Mobile Low Poly)" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;

        void AddFan(Quaternion rotation, Color tint)
        {
            int start = vertices.Count;
            Vector3[] points = {
                new Vector3(0, 0, 0),
                new Vector3(-7f, 20f, 0), new Vector3(0, 20f, 0), new Vector3(7f, 20f, 0),
                new Vector3(-19f, 70f, 0), new Vector3(0, 70f, 0), new Vector3(19f, 70f, 0)
            };
            float[] alpha = { .88f, .48f, .94f, .48f, .20f, .72f, .20f };
            foreach (var point in points) vertices.Add(rotation * point);
            foreach (var a in alpha) colors.Add(new Color(tint.r, tint.g, tint.b, a));
            for (int n = 0; n < points.Length; n++) uvs.Add(new Vector2(2f, 0f));
            int[] indices = { 0,1,2, 0,2,3, 1,4,5, 1,5,2, 2,5,6, 2,6,3 };
            foreach (var index in indices) triangles.Add(start + index);
        }
    }
}

