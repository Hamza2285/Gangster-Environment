using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Intro Mission Setup: the opening mission object (IntroMission) at the top of the gameplay scene, with the red Challenger.
// Game/Reset Intro Mission: plays it again on the next start (it plays once per install).
public static class IntroMissionSetup
{
    const string CarPath = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car2.prefab";

    [MenuItem("Game/Intro Mission Setup")]
    public static void Menu() => Debug.Log(SetUp());

    [MenuItem("Game/Reset Intro Mission")]
    public static void Reset()
    {
        PlayerPrefs.DeleteKey("IntroMissionDone");
        PlayerPrefs.Save();
        Debug.Log("[IntroMission] will play on the next start");
    }

    public static string SetUp()
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == "Intro Mission") root = go;
        if (root == null) root = new GameObject("Intro Mission");
        var mission = root.GetComponent<IntroMission>() ?? root.AddComponent<IntroMission>();
        mission.carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
        mission.policeCars = 4;
        mission.cutsceneSpeed = 24f;
        mission.carPaint = 12;
        mission.routeFrom = new Vector3(95f, 28f, 330f);
        mission.routeFromDirection = Vector3.back;
        mission.routeTo = new Vector3(93f, 28f, -410f);
        mission.routeToDirection = Vector3.back;
        EditorUtility.SetDirty(mission);
        EditorSceneManager.MarkSceneDirty(scene);
        return "[IntroMissionSetup] Intro Mission in " + scene.name + ", car " + (mission.carPrefab != null ? mission.carPrefab.name : "MISSING");
    }
}
