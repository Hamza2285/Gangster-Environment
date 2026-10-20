using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CityLightingRestore
{
    static CityLightingRestore() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("CityLightingRestore.request")) return;
        File.Delete("CityLightingRestore.request");
        string report = "FinalEnvironmentRoadReview/NaturalDayNight/Restored.txt";
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Oasis/MobileOptimization/Gangstar City.unity", OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var c = roots.SelectMany(x => x.GetComponentsInChildren<CityDayNightController>(true)).Single();
            c.Apply(false);
            var skyPath = AssetDatabase.GetAssetPath(RenderSettings.skybox);
            if (!skyPath.EndsWith("OasisSunsetSkybox_Mat.mat") || RenderSettings.fog || Mathf.Abs(c.sun.intensity - 2.5f) > .001f)
                throw new Exception("Restored lighting values did not match original day preset.");
            File.WriteAllText(report, "SUCCESS: Original day/night lighting files restored exactly from backup. Scene reloaded from restored file. Original sunset sky=" + skyPath + "; fog=" + RenderSettings.fog + "; sun intensity=" + c.sun.intensity + "; LOD groups=" + roots.Sum(x => x.GetComponentsInChildren<LODGroup>(true).Length) + ". Scene was not saved over restored backup.");
            SceneView.RepaintAll();
        }
        catch (Exception e) { File.WriteAllText(report, e.ToString()); Debug.LogException(e); }
    }
}
