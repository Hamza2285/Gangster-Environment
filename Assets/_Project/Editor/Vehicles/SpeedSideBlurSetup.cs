using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.EditorTools
{
    /// <summary>Adds the side blur pass to the mobile and PC URP renderers (off by default) and "Speed Side Blur" to the open city scene.</summary>
    public static class SpeedSideBlurSetup
    {
        private const string MaterialPath = "Assets/_Project/Art/Weather/SpeedSideBlur.mat";
        private static readonly string[] Renderers = { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Set Up Speed Side Blur (city scene)")]
        public static void Menu() => Debug.Log(Setup());

        public static string Setup()
        {
            Shader shader = Shader.Find("OpenworldCrime/Speed Side Blur");
            if (shader == null) return "[SpeedSideBlurSetup] shader missing";
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;

            var passes = new List<ScriptableRendererFeature>();
            foreach (string path in Renderers)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) continue;
                FullScreenPassRendererFeature pass = null;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                    if (f is FullScreenPassRendererFeature full && f.name == "Speed Side Blur") pass = full;
                if (pass == null)
                {
                    pass = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    pass.name = "Speed Side Blur";
                    AssetDatabase.AddObjectToAsset(pass, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pass, out string _, out long id);
                    var so = new SerializedObject(data);
                    SerializedProperty list = so.FindProperty("m_RendererFeatures"), map = so.FindProperty("m_RendererFeatureMap");
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = pass;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                pass.passMaterial = material;
                pass.passIndex = 0;
                pass.fetchColorBuffer = true;
                pass.requirements = ScriptableRenderPassInput.None;
                pass.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
                pass.SetActive(false);
                EditorUtility.SetDirty(pass);
                EditorUtility.SetDirty(data);
                passes.Add(pass);
            }
            AssetDatabase.SaveAssets();

            GameObject managers = null;
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[SpeedSideBlurSetup] ///Manager/// not found";
            Transform t = managers.transform.Find("Speed Side Blur");
            GameObject go = t != null ? t.gameObject : new GameObject("Speed Side Blur");
            go.transform.SetParent(managers.transform, false);
            var blur = go.GetComponent<SpeedSideBlur>();
            if (blur == null) blur = go.AddComponent<SpeedSideBlur>();
            blur.EditorSetup(passes.ToArray());
            EditorUtility.SetDirty(blur);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return "[SpeedSideBlurSetup] " + passes.Count + " renderers";
        }
    }
}
