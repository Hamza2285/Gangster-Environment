using OpenworldCrime.Interiors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Calibrates an interior's baked light probes after its bake. Run it with the interior scene open, after every re-bake.
    ///
    /// Why: the gun shop's light rig is very intense (softboxes at intensity 9 a metre above head height). Its lightmaps look
    /// right because the lightmap encoding clamps the result, but probes store the unclamped value - measured ~25x the room's
    /// ambient at the spawn point - which is why the player glowed when the package's probes were used. Without any interior
    /// probes, though, the player would be lit by the city's probes (extrapolated from kilometres away) once the city is baked.
    /// So the interior keeps character-height probes (PlayerLightProbes) and this scales all of them uniformly so the spawn
    /// point reads <see cref="TargetOverAmbient"/> x the scene's ambient (the look the player had before), keeping the probes'
    /// colour and direction variation across the room. The scaled values are saved into the scene's LightingData asset.
    /// </summary>
    public static class InteriorProbeCalibrator
    {
        private const float TargetOverAmbient = 1.3f;

        [MenuItem("Tools/OpenworldCrime/Lighting/Calibrate Interior Probes (open interior scene)")]
        public static void CalibrateMenu() => Debug.Log(Calibrate());

        public static string Calibrate()
        {
            var interior = Object.FindFirstObjectByType<InteriorScene>(FindObjectsInactive.Include);
            LightProbes probes = LightmapSettings.lightProbes;
            if (interior == null || probes == null || probes.count == 0 || Lightmapping.lightingDataAsset == null)
                return "[InteriorProbeCalibrator] Open a baked interior scene (InteriorScene root + baked light probes) alone first.";
            if (UnityEngine.SceneManagement.SceneManager.loadedSceneCount != 1)
                return "[InteriorProbeCalibrator] Close other scenes first (probes of several scenes are merged).";

            LightProbes.GetInterpolatedProbe(interior.SpawnPoint.position + Vector3.up, null, out SphericalHarmonicsL2 atSpawn);
            SphericalHarmonicsL2 ambient = RenderSettings.ambientProbe;
            float probeLum = Luminance(atSpawn);
            float target = TargetOverAmbient * Luminance(ambient);
            if (probeLum <= 0.0001f) return "[InteriorProbeCalibrator] Probes at the spawn are black; bake the interior first.";
            float scale = target / probeLum;

            SphericalHarmonicsL2[] sh = probes.bakedProbes;
            for (int i = 0; i < sh.Length; i++) sh[i] *= scale;
            probes.bakedProbes = sh;
            EditorUtility.SetDirty(probes);
            EditorUtility.SetDirty(Lightmapping.lightingDataAsset);
            AssetDatabase.SaveAssets();
            return $"[InteriorProbeCalibrator] scaled {sh.Length} probes by {scale:F4} (spawn {probeLum:F3} -> {target:F3}).";
        }

        private static float Luminance(SphericalHarmonicsL2 sh) => sh[0, 0] * 0.3f + sh[1, 0] * 0.59f + sh[2, 0] * 0.11f;
    }
}
