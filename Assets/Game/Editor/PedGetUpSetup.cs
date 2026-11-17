using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Game/Set Up Pedestrian Get-Up: a small controller with Invector's two get-up clips (from the back, from the belly), handed to
// every pedestrian prefab together with that person's humanoid avatar (the same one the carjack uses), so knocked-down people
// get up properly before they run. Safe to run again.
public static class PedGetUpSetup
{
    const string ControllerPath = "Assets/_Project/Animations/Pedestrians/PedGetUp.controller";
    const string ClipsPath = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_Actions.fbx";
    const string RosterPath = "Assets/_Project/Data/Vehicles/PedDriverRoster.asset";

    [MenuItem("Game/Set Up Pedestrian Get-Up")]
    static void Run()
    {
        Debug.Log(SetUp());
    }

    public static string SetUp()
    {
        AnimationClip fromBack = null, fromBelly = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ClipsPath))
        {
            if (o is AnimationClip clip && !clip.name.StartsWith("__preview"))
            {
                if (clip.name == "GetUpFromBack") fromBack = clip;
                if (clip.name == "GetUpFromBelly") fromBelly = clip;
            }
        }
        if (fromBack == null || fromBelly == null) return "[PedGetUpSetup] get-up clips not found in " + ClipsPath;

        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var back = machine.AddState("GetUpFromBack");
        back.motion = fromBack;
        var belly = machine.AddState("GetUpFromBelly");
        belly.motion = fromBelly;
        machine.defaultState = back;
        // Renamed copies are not needed: the clip names match the states, which the ragdoll uses to look up the length.
        EditorUtility.SetDirty(controller);

        var roster = AssetDatabase.LoadAssetAtPath<OpenworldCrime.Vehicles.PedDriverRoster>(RosterPath);
        if (roster == null) return "[PedGetUpSetup] no roster at " + RosterPath;
        var entriesField = roster.GetType().GetField("entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var entries = entriesField.GetValue(roster) as System.Array;
        int done = 0;
        foreach (object entry in entries)
        {
            var type = entry.GetType();
            var prefab = type.GetField("pedPrefab").GetValue(entry) as GameObject;
            var avatar = type.GetField("humanoidAvatar").GetValue(entry) as Avatar;
            if (prefab == null || avatar == null) continue;
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var ragdoll = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianRagdoll>();
            if (ragdoll != null)
            {
                var so = new SerializedObject(ragdoll);
                so.FindProperty("getUpAvatar").objectReferenceValue = avatar;
                so.FindProperty("getUpController").objectReferenceValue = controller;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                done++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        return "[PedGetUpSetup] controller " + ControllerPath + "; " + done + " pedestrians can get up properly";
    }
}
