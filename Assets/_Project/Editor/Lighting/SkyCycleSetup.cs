using OpenworldCrime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Creates the city sky-cycle material (clouds from the day preset's cubemap) and the Sky Cycle object under
    /// ///Manager/// in the open city scene. Re-running updates the references and keeps the material's look settings.
    /// Menu: Tools > OpenworldCrime > Lighting > Set Up Sky Cycle.
    /// </summary>
    public static class SkyCycleSetup
    {
        private const string MaterialPath = "Assets/_Project/Art/Sky/CitySkyCycle.mat";
        private const string ShaderName = "OpenworldCrime/Sky Cycle";

        [MenuItem("Tools/OpenworldCrime/Lighting/Set Up Sky Cycle (open city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            Scene city = SceneManager.GetActiveScene();
            CityDayNightController dayNight = Object.FindFirstObjectByType<CityDayNightController>();
            if (dayNight == null || dayNight.presets == null) return "[SkyCycleSetup] No CityDayNightController with presets in the open scene.";

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/Sky")) AssetDatabase.CreateFolder("Assets/_Project/Art", "Sky");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find(ShaderName)) { name = "CitySkyCycle" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            // Clouds: the cubemap, rotation and exposure the day preset uses.
            Material daySky = dayNight.presets.day.skybox;
            if (daySky != null)
            {
                if (daySky.HasProperty("_Tex")) material.SetTexture("_DayCube", daySky.GetTexture("_Tex"));
                if (daySky.HasProperty("_Rotation")) material.SetFloat("_CubeRotation", daySky.GetFloat("_Rotation"));
                if (daySky.HasProperty("_Exposure")) material.SetFloat("_CubeExposure", daySky.GetFloat("_Exposure"));
            }
            // Night colours as the existing night sky.
            Material nightSky = dayNight.presets.night.skybox;
            if (nightSky != null)
            {
                if (nightSky.HasProperty("_Horizon")) material.SetColor("_NightHorizon", nightSky.GetColor("_Horizon"));
                if (nightSky.HasProperty("_Zenith")) material.SetColor("_NightZenith", nightSky.GetColor("_Zenith"));
                if (nightSky.HasProperty("_MoonColor")) material.SetColor("_MoonColor", nightSky.GetColor("_MoonColor"));
            }
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            GameObject managers = null;
            foreach (GameObject root in city.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[SkyCycleSetup] ///Manager/// not found in " + city.name;
            Transform existing = managers.transform.Find("Sky Cycle");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Sky Cycle");
            go.transform.SetParent(managers.transform, false);
            var cycle = go.GetComponent<SkyCycleController>() ?? go.AddComponent<SkyCycleController>();
            var so = new SerializedObject(cycle);
            so.FindProperty("dayNight").objectReferenceValue = dayNight;
            so.FindProperty("skyMaterial").objectReferenceValue = material;
            // Noon sun of the sky where the day light comes from (direction toward the sun).
            Vector3 toSun = -(Quaternion.Euler(dayNight.presets.day.lightRotation) * Vector3.forward);
            so.FindProperty("noonYaw").floatValue = Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(city);
            return $"[SkyCycleSetup] {MaterialPath}; 'Sky Cycle' under ///Manager/// in {city.name} (save the scene)";
        }
    }
}
