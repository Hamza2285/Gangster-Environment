using System.IO;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools.Environment
{
    /// <summary>
    /// Tools > OpenworldCrime > Environment > Build Ocean Horizon Prefab: writes Prefabs/Environment/OceanHorizon.prefab
    /// (an <see cref="OceanHorizon"/> that extends the city's ocean plane to the horizon at runtime). Placing it in a scene
    /// is left to whoever wants it there; the trailer adds it at runtime only.
    /// </summary>
    public static class OceanHorizonPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Environment/OceanHorizon.prefab";

        [MenuItem("Tools/OpenworldCrime/Environment/Build Ocean Horizon Prefab")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            // Built in a preview scene so the open scene is not touched (or marked dirty).
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("OceanHorizon");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
                go.AddComponent<OceanHorizon>();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Debug.Log("[OceanHorizon] Prefab written: " + PrefabPath);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
