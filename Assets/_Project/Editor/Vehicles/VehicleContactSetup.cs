using OpenworldCrime.Combat;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Contact feedback between vehicles and people, set up once (safe to run again):
    /// - every drivable RCC vehicle (Drivable_*) gets <see cref="VehicleHumanContactFx"/> (no metal sparks on people) and a NavMesh
    ///   obstacle carved while it stands still (officers on foot walk round parked cars instead of through them);
    /// - the blood-mark material, and <see cref="BloodEffects"/> under ///Manager/// in the open gameplay scene, wired to both
    ///   playable characters.
    /// </summary>
    public static class VehicleContactSetup
    {
        private const string DrivableDir = "Assets/_Project/Prefabs/Vehicles/Drivable";
        // Other developers' prefabs: not modified here; RCCVehicleAdapter adds the same components to them at runtime.
        private static readonly string[] NotOurs = { "Drivable_Ambulance" };
        private const string MarkMaterialPath = "Assets/_Project/Materials/Effects/M_BloodMark.mat";
        private const string MarkTexture = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Particles/DamageEffect/textures/blood_PNG6107.png";
        private const string SplashPrefab = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Particles/DamageEffect/prefabs/Resources/defaultDamageEffect.prefab";

        [MenuItem("Tools/OpenworldCrime/Vehicles/Setup Contact FX, Blood And NavMesh Obstacles")]
        public static void Run()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { DrivableDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.Array.IndexOf(NotOurs, System.IO.Path.GetFileNameWithoutExtension(path)) >= 0) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<RCC_CarControllerV3>() == null) continue;
                    if (root.GetComponent<VehicleHumanContactFx>() == null) root.AddComponent<VehicleHumanContactFx>();
                    var obstacle = root.GetComponent<NavMeshObstacle>();
                    if (obstacle == null) obstacle = root.AddComponent<NavMeshObstacle>();
                    PoliceCrewSetup.ConfigureObstacle(obstacle, root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Material mark = CreateMarkMaterial();
            WireBlood(mark);
            AssetDatabase.SaveAssets();
            Debug.Log("[VehicleContactSetup] " + count + " drivable vehicles set up (contact FX, NavMesh obstacle); blood effects wired.");
        }

        private static Material CreateMarkMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MarkMaterialPath);
            if (material != null) return material;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials/Effects")) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Effects");
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_BloodMark" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MarkTexture));
            material.SetColor("_BaseColor", new Color(0.32f, 0.02f, 0.02f, 0.9f));
            // Transparent, alpha blended, no depth write, both sides off (lies on the ground).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            material.SetOverrideTag("RenderType", "Transparent");
            AssetDatabase.CreateAsset(material, MarkMaterialPath);
            return material;
        }

        private static void WireBlood(Material mark)
        {
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managers = null;
            foreach (GameObject g in scene.GetRootGameObjects()) if (g.name == "///Manager///") managers = g;
            if (managers == null)
            {
                Debug.LogError("[VehicleContactSetup] No ///Manager/// in the open scene; Blood Effects not added.");
                return;
            }
            BloodEffects blood = managers.GetComponentInChildren<BloodEffects>(true);
            if (blood == null)
            {
                var go = new GameObject("BloodEffects");
                go.transform.SetParent(managers.transform, false);
                blood = go.AddComponent<BloodEffects>();
            }
            var players = Object.FindObjectsByType<OpenworldCrime.Player.PlayerVehicleInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var so = new SerializedObject(blood);
            so.FindProperty("splashPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(SplashPrefab);
            so.FindProperty("markMaterial").objectReferenceValue = mark;
            SerializedProperty list = so.FindProperty("players");
            list.arraySize = players.Length;
            for (int i = 0; i < players.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = players[i].GetComponent<Invector.vHealthController>();
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
