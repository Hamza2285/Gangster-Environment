using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The played character's soft sun shadow (Tools > OpenworldCrime > Player > Set Up Sun Shadow, city scene open): the
    /// receiver material and the "Player Sun Shadow" scene object (sun's-eye mask camera + ground receiver). No lighting,
    /// URP, quality, light or post-processing setting is touched.
    /// </summary>
    public static class PlayerSunShadowSetup
    {
        private const string Folder = "Assets/_Project/Art/Shadows";
        private const string RootName = "Player Sun Shadow";
        // The world: not UI / ignore-raycast / traffic AI / player / enemies / triggers / body parts / vehicles.
        private const int WorldLayers = ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 15) | (1 << 19) | (1 << 20) | (1 << 21) | (1 << 30));

        [MenuItem("Tools/OpenworldCrime/Player/Set Up Sun Shadow (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Shadows");
            string matPath = Folder + "/PlayerSunShadow.mat";
            Shader shader = Shader.Find("OpenworldCrime/PlayerSunShadow");
            if (shader == null) return "[PlayerSunShadowSetup] Shader OpenworldCrime/PlayerSunShadow not found.";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, matPath); }
            m.shader = shader;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);

            var scene = EditorSceneManager.GetActiveScene();
            Light sun = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootName) { Object.DestroyImmediate(root); continue; }
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                    if (l.name == "MainLight" && l.type == LightType.Directional) sun = l;
            }
            if (sun == null) sun = RenderSettings.sun;

            var go = new GameObject(RootName);
            var camGo = new GameObject("Sun View Camera");
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << 30;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.depth = -50f;

            var recGo = new GameObject("Ground Receiver");
            recGo.transform.SetParent(go.transform, false);
            var filter = recGo.AddComponent<MeshFilter>();
            var r = recGo.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.enabled = false;

            var shadow = go.AddComponent<PlayerSunShadow>();
            var so = new SerializedObject(shadow);
            so.FindProperty("sun").objectReferenceValue = sun;
            so.FindProperty("shadowCamera").objectReferenceValue = cam;
            so.FindProperty("receiver").objectReferenceValue = filter;
            so.FindProperty("worldLayers").intValue = WorldLayers;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            return "[PlayerSunShadowSetup] '" + RootName + "' (sun: " + (sun != null ? sun.name : "none") + ") with " + matPath;
        }
    }
}
