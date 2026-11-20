using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The game's ocean (Tools > OpenworldCrime > Environment > Set Up Ocean, city scene open): an "Ocean" object (OceanHorizon)
    /// that turns the city's square water plane plus an open sea out to about 12 km into one continuous surface with one
    /// material (OpenworldCrime/OceanSurface, colours and normal map taken from the city's OasisWater_Mat). The city's water
    /// object and material are not changed (the plane is only hidden at runtime, while the ocean replaces it). No lighting
    /// settings, probes, fog, sky or camera values are touched.
    /// </summary>
    public static class OceanSetup
    {
        private const string MaterialPath = "Assets/_Project/Art/Water/OceanSurface.mat";

        [MenuItem("Tools/OpenworldCrime/Environment/Set Up Ocean (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            MeshRenderer water = null;
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.name == "Water" && r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Water")) water = r;
            if (water == null) return "[OceanSetup] city water plane 'Water' not found";
            Material city = water.sharedMaterial;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/Water")) AssetDatabase.CreateFolder("Assets/_Project/Art", "Water");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("OpenworldCrime/OceanSurface"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            if (city.HasProperty("_Normal_Map")) material.SetTexture("_NormalMap", city.GetTexture("_Normal_Map"));
            if (city.HasProperty("_ColorBright")) material.SetColor("_ShallowColor", city.GetColor("_ColorBright"));
            if (city.HasProperty("_NormalTiling1")) material.SetFloat("_NormalTiling1", city.GetFloat("_NormalTiling1"));
            if (city.HasProperty("_NormalTiling2")) material.SetFloat("_NormalTiling2", city.GetFloat("_NormalTiling2"));
            if (city.HasProperty("_OffsetSpeed")) material.SetFloat("_NormalSpeed", city.GetFloat("_OffsetSpeed"));
            Bounds b = water.bounds;
            material.SetVector("_CityCenter", new Vector4(b.center.x, b.center.z, b.extents.x, b.extents.z));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            var scene = EditorSceneManager.GetActiveScene();
            GameObject ocean = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "Ocean") ocean = root;
            if (ocean == null) ocean = new GameObject("Ocean");
            OceanHorizon horizon = ocean.GetComponent<OceanHorizon>();
            if (horizon == null) horizon = ocean.AddComponent<OceanHorizon>();
            var so = new SerializedObject(horizon);
            so.FindProperty("sourceWater").objectReferenceValue = water;
            so.FindProperty("oceanMaterial").objectReferenceValue = material;
            so.FindProperty("outerHalfSize").floatValue = 12000f;
            so.FindProperty("ringGrowth").floatValue = 1.08f;
            so.FindProperty("segmentsPerSide").intValue = 64;
            so.FindProperty("replaceSource").boolValue = true;
            so.FindProperty("seabedDepth").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"[OceanSetup] 'Ocean' with {MaterialPath} over '{water.name}' (city water square {b.size.x:F0} m at {b.center:F0})";
        }
    }
}
