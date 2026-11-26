using OpenworldCrime.Pedestrians;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Sets up carjack victims phoning the police in the open gameplay scene (re-runnable; existing assets / objects are kept):
    /// - Assets/_Project/Art/Props/WitnessPhone.mat (dark URP Lit) and Assets/_Project/Data/Pedestrians/WitnessCallSettings.asset
    ///   (phone mesh = Unity's built-in cube; no phone model exists in the project);
    /// - "Witness Call Manager" (<see cref="WitnessCallManager"/>) under ///Manager/// with the settings assigned.
    /// Saves the scene.
    /// </summary>
    public static class WitnessCallSetup
    {
        private const string ObjectName = "Witness Call Manager";
        private const string SettingsPath = "Assets/_Project/Data/Pedestrians/WitnessCallSettings.asset";
        private const string MaterialPath = "Assets/_Project/Art/Props/WitnessPhone.mat";

        [MenuItem("Tools/OpenworldCrime/Pedestrians/Set Up Witness Phone Calls (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            if (EditorApplication.isPlaying) return "[WitnessCallSetup] Leave play mode first.";
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[WitnessCallSetup] ///Manager/// not found in the open scene (open Gangstar_Gameplay).";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                EnsureFolder("Assets/_Project/Art", "Props");
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) return "[WitnessCallSetup] URP Lit shader not found.";
                material = new Material(lit) { name = "WitnessPhone" };
                material.SetColor("_BaseColor", new Color(0.035f, 0.035f, 0.04f, 1f));
                material.SetFloat("_Smoothness", 0.65f);
                material.enableInstancing = true;
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            WitnessCallSettings settings = AssetDatabase.LoadAssetAtPath<WitnessCallSettings>(SettingsPath);
            if (settings == null)
            {
                EnsureFolder("Assets/_Project/Data", "Pedestrians");
                settings = ScriptableObject.CreateInstance<WitnessCallSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            if (settings.phoneMesh == null) settings.phoneMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (settings.phoneMaterial == null) settings.phoneMaterial = material;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Transform existing = managers.transform.Find(ObjectName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(ObjectName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Witness Call Manager");
                go.transform.SetParent(managers.transform, false);
            }
            WitnessCallManager manager = go.GetComponent<WitnessCallManager>();
            if (manager == null) manager = Undo.AddComponent<WitnessCallManager>(go);
            var so = new SerializedObject(manager);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"[WitnessCallSetup] '{ObjectName}' under ///Manager/// uses {SettingsPath}; scene saved.";
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
