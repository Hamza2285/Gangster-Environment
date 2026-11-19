using System.Collections.Generic;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds "Road Panel Chase" under ///Manager/// in the open city scene and fills it with every renderer that uses one of the
    /// road shaders with crossing panels. Re-running refreshes the list. Menu: Tools > OpenworldCrime > Lighting.
    /// </summary>
    public static class RoadPanelChaseSetup
    {
        private static readonly string[,] Pairs =
        {
            { "Gangstar City/Road Atlas Lit", "OpenworldCrime/Road Atlas Lit (Panel Chase)" },
            { "Gangstar City/Road Detail PNG Atlas", "OpenworldCrime/Road Detail PNG Atlas (Panel Chase)" },
        };

        [MenuItem("Tools/OpenworldCrime/Lighting/Set Up Road Panel Chase (open city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            Scene city = SceneManager.GetActiveScene();
            var sources = new List<Shader>();
            var pairs = new List<(Shader, Shader)>();
            for (int i = 0; i < Pairs.GetLength(0); i++)
            {
                Shader source = Shader.Find(Pairs[i, 0]);
                Shader chaseShader = Shader.Find(Pairs[i, 1]);
                if (source == null || chaseShader == null) return "[RoadPanelChaseSetup] shader missing: " + Pairs[i, 0] + " / " + Pairs[i, 1];
                sources.Add(source);
                pairs.Add((source, chaseShader));
            }

            var renderers = new List<Renderer>();
            foreach (GameObject root in city.GetRootGameObjects())
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && sources.Contains(m.shader)) { renderers.Add(r); break; }

            GameObject managers = null;
            foreach (GameObject root in city.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[RoadPanelChaseSetup] ///Manager/// not found";
            Transform existing = managers.transform.Find("Road Panel Chase");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Road Panel Chase");
            go.transform.SetParent(managers.transform, false);
            RoadPanelChase chase = go.GetComponent<RoadPanelChase>();
            if (chase == null) chase = go.AddComponent<RoadPanelChase>();

            var so = new SerializedObject(chase);
            SerializedProperty list = so.FindProperty("renderers");
            list.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            SerializedProperty shaderList = so.FindProperty("shaders");
            shaderList.arraySize = pairs.Count;
            for (int i = 0; i < pairs.Count; i++)
            {
                shaderList.GetArrayElementAtIndex(i).FindPropertyRelative("source").objectReferenceValue = pairs[i].Item1;
                shaderList.GetArrayElementAtIndex(i).FindPropertyRelative("chase").objectReferenceValue = pairs[i].Item2;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(city);
            return $"[RoadPanelChaseSetup] 'Road Panel Chase' under ///Manager/// with {renderers.Count} road renderers (save the scene)";
        }
    }
}
