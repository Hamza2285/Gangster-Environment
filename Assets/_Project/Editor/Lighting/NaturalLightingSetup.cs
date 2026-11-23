using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Natural daylight (GTA V style) instead of the pink "Vice" sunset: the day preset gets a high warm-white sun with crisp
    /// shadows, neutral blue-grey sky light, a light distance haze; the sky cycle's morning loses its pink and the clock starts
    /// late morning. The post profiles come from <see cref="CityPostProcessSetup"/> (neutral grade); the baked bounce / sky
    /// light then comes from <see cref="DayNightLightBaker"/>. Night keeps its neon and street lamps.
    /// Menu: Tools > OpenworldCrime > Lighting > Natural Daylight. Run it, then the post setup and the bake.
    /// </summary>
    public static class NaturalLightingSetup
    {
        const string PresetsPath = "Assets/Scenes/Oasis/DayNight/City Day Night Presets.asset";
        const string SkyPath = "Assets/_Project/Art/Sky/CitySkyCycle.mat";

        [MenuItem("Tools/OpenworldCrime/Lighting/Natural Daylight")]
        public static void Menu() => Debug.Log(Apply());

        public static string Apply()
        {
            var log = new System.Text.StringBuilder("[NaturalLightingSetup]\n");
            var presets = AssetDatabase.LoadAssetAtPath<CityDayNightPresets>(PresetsPath);
            if (presets == null) return log.Append("  presets missing").ToString();
            var day = presets.day;
            // Sun: high late-morning sun, warm white (~5600 K), crisp shadows.
            day.lightRotation = new Vector3(48f, 315f, 0f);
            day.lightColor = new Color(1f, 0.955f, 0.885f);
            day.lightIntensity = 1.9f;
            day.indirectIntensity = 1.4f;   // stronger sun bounce: shaded streets and walls stay readable
            day.useColorTemperature = false;
            day.shadows = LightShadows.Soft;
            day.shadowStrength = 0.75f;   // shade is lighter, so nothing in shadow goes black
            // Sky light: blue from above, neutral grey at the horizon, warm dark bounce from the ground.
            day.ambientMode = AmbientMode.Trilight;
            day.ambientSky = new Color(0.62f, 0.7f, 0.85f);
            day.ambientEquator = new Color(0.58f, 0.59f, 0.6f);
            day.ambientGround = new Color(0.34f, 0.31f, 0.28f);
            day.ambientIntensity = 1.2f;
            // Haze: far buildings fade toward a pale blue, like a real city on a clear day.
            day.fog = true;
            day.fogMode = FogMode.Linear;
            day.fogColor = new Color(0.7f, 0.78f, 0.86f);
            day.fogStart = 260f;
            day.fogEnd = 1700f;
            EditorUtility.SetDirty(presets);
            log.AppendLine("  day preset: natural sun, sky light and haze");

            // Night: moonlit, not purple. A slightly brighter cool-white moon, a neutral blue-grey sky light (the neon signs and
            // the orange street lamps bring the colour), and a haze that lets the skyline read. Baked into the night lightmaps.
            var night = presets.night;
            night.lightColor = new Color(0.72f, 0.8f, 1f);
            night.lightIntensity = 0.65f;
            night.ambientMode = AmbientMode.Trilight;
            night.ambientSky = new Color(0.2f, 0.23f, 0.32f);
            night.ambientEquator = new Color(0.15f, 0.15f, 0.2f);
            night.ambientGround = new Color(0.06f, 0.055f, 0.06f);
            night.ambientIntensity = 1f;
            night.fogColor = new Color(0.08f, 0.11f, 0.17f);
            log.AppendLine("  night preset: neutral moonlight");

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky != null)
            {
                sky.SetColor("_MorningTint", new Color(1.03f, 0.99f, 0.95f));
                sky.SetColor("_MorningZenith", new Color(0.3f, 0.5f, 0.85f));
                sky.SetColor("_MorningGlow", new Color(1f, 0.78f, 0.55f));
                sky.SetColor("_NoonTint", new Color(1f, 1f, 1.03f));
                // Natural blue sky with white clouds (rendered from the iTS Sunny2 sky) instead of the pink sunset clouds.
                var dayCube = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Project/Art/Sky/NaturalDay_Cube.asset");
                if (dayCube != null) { sky.SetTexture("_DayCube", dayCube); sky.SetFloat("_CubeRotation", 0f); sky.SetFloat("_CubeExposure", 1f); sky.SetFloat("_CloudMix", 0.85f); }
                sky.SetColor("_NoonZenith", new Color(0.22f, 0.48f, 0.9f));
                sky.SetColor("_NoonGlow", new Color(0.85f, 0.9f, 1f));
                sky.SetColor("_SunColor", new Color(4f, 3.75f, 3.3f));
                EditorUtility.SetDirty(sky);
                log.AppendLine("  sky: natural morning and noon");
            }

            if (sky != null)
            {
                // The day preset (used in the editor and by the bake) shows the same natural sky as the runtime cycle, at noon.
                day.skybox = sky;
                sky.SetVector("_CycleWeights", new Vector4(0f, 1f, 0f, 0f));
                sky.SetVector("_SunDirection", -(Quaternion.Euler(day.lightRotation) * Vector3.forward));
                EditorUtility.SetDirty(presets);
            }

            var cycle = Object.FindFirstObjectByType<OpenworldCrime.Environment.SkyCycleController>(FindObjectsInactive.Include);
            if (cycle != null)
            {
                var so = new SerializedObject(cycle);
                so.FindProperty("startHour").floatValue = 10.5f;   // starts in full daylight, not the pink dawn
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(cycle.gameObject.scene);
                log.AppendLine("  sky cycle starts at 10:30");
            }

            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            if (controller != null && !controller.IsNight) controller.Apply(false);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
