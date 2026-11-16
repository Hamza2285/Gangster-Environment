using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game > Set Up Ambulance: builds the ambulance (drivable and rare in traffic), the traffic bikes with their colour
// variants, and puts the paramedic missions on the open gameplay scene.
public static class AmbulanceMissionSetup
{
    const string PeopleDir = "Assets/PopulationSystem/Prefabs/People Prefabs";
    const string SpotsPath = "Assets/_Project/Data/Pedestrians/AmbientSpotData.asset";
    const string ManAnimator = "Assets/_Project/Animations/Pedestrians/PedMan_Ambient.controller";
    const string WomanAnimator = "Assets/_Project/Animations/Pedestrians/PedGirl_Ambient.controller";

    [MenuItem("Game/Set Up Ambulance And Bikes")]
    public static void Run() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var sb = new StringBuilder("[AmbulanceMissionSetup]\n");
        sb.AppendLine(OpenworldCrime.EditorTools.FinalCarBuilder.BuildAmbulance());
        BikePrefabBuilder.BuildAll();
        BikePrefabBuilder.AddToTraffic();
        sb.AppendLine("bikes built and added to traffic");
        sb.AppendLine(WireMissions());
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    public static string WireMissions()
    {
        var gameplay = GameObject.Find("Gameplay");
        if (gameplay == null) return "no Gameplay object in the open scene";
        var mission = gameplay.GetComponent<AmbulanceMission>();
        if (mission == null) mission = gameplay.AddComponent<AmbulanceMission>();
        mission.spots = AssetDatabase.LoadAssetAtPath<OpenworldCrime.Pedestrians.AmbientSpotData>(SpotsPath);
        mission.manAnimator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ManAnimator);
        mission.womanAnimator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(WomanAnimator);
        var people = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PeopleDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("Man_") && !name.StartsWith("Girl_")) continue;
            people.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }
        mission.patientPrefabs = people.ToArray();
        EditorUtility.SetDirty(mission);
        return "missions: " + people.Count + " possible patients, spots " + (mission.spots != null) + ", animators " + (mission.manAnimator != null && mission.womanAnimator != null);
    }
}
