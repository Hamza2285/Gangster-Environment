using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Rooftop sky searchlights (Tools > OpenworldCrime > Environment > Build Rooftop Searchlights, city scene open): a rig of
    /// three lamps with sweeping beams (RooftopSkySearchlight), built as a prefab and placed on the roofs of the two tallest
    /// towers (the two "hirise7" towers, flat roofs at about 138 m). Beam: generated cone, additive OpenworldCrime/SkyBeam.
    /// Scene: "Rooftop Searchlights" root (our object).
    /// </summary>
    public static class SkySearchlightBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Searchlights";
        private const string PrefabPath = "Assets/_Project/Prefabs/Environment/RooftopSkySearchlights.prefab";
        private static readonly Vector2[] Roofs = { new Vector2(-265f, 206f), new Vector2(594f, -251f) };

        [MenuItem("Tools/OpenworldCrime/Environment/Build Rooftop Searchlights (city scene)")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[SkySearchlightBuilder]\n");
            EnsureFolder(ArtFolder);
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            Mesh cone = AssetDatabase.LoadAssetAtPath<Mesh>(ArtFolder + "/SkyBeamCone.asset");
            if (cone == null) { cone = Cone(320f, 0.9f, 18f, 24); AssetDatabase.CreateAsset(cone, ArtFolder + "/SkyBeamCone.asset"); }
            Material beam = LoadOrCreate(ArtFolder + "/SkyBeam.mat", "OpenworldCrime/SkyBeam");
            beam.SetColor("_Color", new Color(0.72f, 0.8f, 1f, 1f));
            beam.SetFloat("_Intensity", 1f);
            Material housing = LoadOrCreate(ArtFolder + "/SearchlightHousing.mat", "Universal Render Pipeline/Lit");
            housing.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
            housing.SetFloat("_Metallic", 0.7f);
            housing.SetFloat("_Smoothness", 0.5f);
            Material lens = LoadOrCreate(ArtFolder + "/SearchlightLens.mat", "Universal Render Pipeline/Lit");
            lens.SetColor("_BaseColor", new Color(1f, 0.97f, 0.9f));
            lens.EnableKeyword("_EMISSION");
            lens.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.85f) * 6f);
            EditorUtility.SetDirty(beam);
            EditorUtility.SetDirty(housing);
            EditorUtility.SetDirty(lens);

            GameObject prefab = BuildPrefab(cone, beam, housing, lens);
            log.AppendLine("- prefab " + PrefabPath);

            var scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Rooftop Searchlights") Object.DestroyImmediate(root);
            var holder = new GameObject("Rooftop Searchlights");
            for (int i = 0; i < Roofs.Length; i++)
            {
                Vector3 top = new Vector3(Roofs[i].x, 400f, Roofs[i].y);
                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 450f, ~0, QueryTriggerInteraction.Ignore)) { log.AppendLine("- no roof at " + Roofs[i]); continue; }
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                rig.name = "Searchlights - " + hit.collider.name;
                rig.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, i * 47f, 0f));
                // The second roof sweeps the other way so the two sets cross over the city.
                if (i == 1)
                {
                    var so = new SerializedObject(rig.GetComponent<RooftopSkySearchlight>());
                    SerializedProperty list = so.FindProperty("beams");
                    for (int b = 0; b < list.arraySize; b++)
                    {
                        SerializedProperty e = list.GetArrayElementAtIndex(b);
                        e.FindPropertyRelative("yawSpeed").floatValue *= -1.15f;
                        e.FindPropertyRelative("phase").floatValue += 1.3f;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                log.AppendLine($"- rig on '{hit.collider.name}' roof at {hit.point:F1}");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static GameObject BuildPrefab(Mesh cone, Material beam, Material housing, Material lens)
        {
            var root = new GameObject("RooftopSkySearchlights");
            var rig = root.AddComponent<RooftopSkySearchlight>();
            var beams = new List<RooftopSkySearchlight.Beam>();
            Vector3[] spots = { new Vector3(-4f, 0f, -3f), new Vector3(4f, 0f, -3f), new Vector3(0f, 0f, 4.5f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stand.name = "Lamp " + (i + 1);
                Object.DestroyImmediate(stand.GetComponent<Collider>());
                stand.transform.SetParent(root.transform, false);
                stand.transform.localPosition = spots[i] + Vector3.up * 0.4f;
                stand.transform.localScale = new Vector3(0.9f, 0.4f, 0.9f);
                stand.GetComponent<Renderer>().sharedMaterial = housing;

                var pivot = new GameObject("Beam Pivot " + (i + 1)).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = spots[i] + Vector3.up * 1.2f;

                var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                head.name = "Head";
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.transform.SetParent(pivot, false);
                head.transform.localScale = new Vector3(1.3f, 0.45f, 1.3f);
                head.GetComponent<Renderer>().sharedMaterial = housing;
                var glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                glass.name = "Lens";
                Object.DestroyImmediate(glass.GetComponent<Collider>());
                glass.transform.SetParent(pivot, false);
                glass.transform.localPosition = new Vector3(0f, 0.46f, 0f);
                glass.transform.localScale = new Vector3(1.1f, 0.02f, 1.1f);
                var glassRenderer = glass.GetComponent<Renderer>();
                glassRenderer.sharedMaterial = lens;

                var beamGo = new GameObject("Beam");
                beamGo.transform.SetParent(pivot, false);
                beamGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                beamGo.AddComponent<MeshFilter>().sharedMesh = cone;
                var mr = beamGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = beam;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                beams.Add(new RooftopSkySearchlight.Beam
                {
                    pivot = pivot, beam = mr, lens = glassRenderer,
                    tilt = 20f + i * 4f, tiltSwing = 11f, tiltSpeed = 0.19f + i * 0.04f,
                    yaw = i * 120f, yawSwing = 70f, yawSpeed = 0.11f + i * 0.03f, phase = i * 2.1f
                });
            }
            var so = new SerializedObject(rig);
            SerializedProperty list = so.FindProperty("beams");
            list.arraySize = beams.Count;
            for (int i = 0; i < beams.Count; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                RooftopSkySearchlight.Beam b = beams[i];
                e.FindPropertyRelative("pivot").objectReferenceValue = b.pivot;
                e.FindPropertyRelative("beam").objectReferenceValue = b.beam;
                e.FindPropertyRelative("lens").objectReferenceValue = b.lens;
                e.FindPropertyRelative("tilt").floatValue = b.tilt;
                e.FindPropertyRelative("tiltSwing").floatValue = b.tiltSwing;
                e.FindPropertyRelative("tiltSpeed").floatValue = b.tiltSpeed;
                e.FindPropertyRelative("yaw").floatValue = b.yaw;
                e.FindPropertyRelative("yawSwing").floatValue = b.yawSwing;
                e.FindPropertyRelative("yawSpeed").floatValue = b.yawSpeed;
                e.FindPropertyRelative("phase").floatValue = b.phase;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // Open cone along +Y: uv.y 0 at the lamp, 1 at the far end; normals point outwards.
        private static Mesh Cone(float length, float radiusStart, float radiusEnd, int segments)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float slope = (radiusEnd - radiusStart) / length;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = (dir - Vector3.up * slope).normalized;
                verts.Add(dir * radiusStart); normals.Add(n); uvs.Add(new Vector2((float)i / segments, 0f));
                verts.Add(dir * radiusEnd + Vector3.up * length); normals.Add(n); uvs.Add(new Vector2((float)i / segments, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 2;
                tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var mesh = new Mesh { name = "SkyBeamCone" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material LoadOrCreate(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
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
