using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Game/Police Pedestrians Fight Back: the street police (Ped_Police_2 / _3, humanoid) get the hit reactions, fist-fight moves
// and get-up clips that Ghost's PedestrianBrawler plays, straight from Invector's humanoid melee clips (no baking needed),
// and are set to always fight back and never give up and run. Safe to run again.
public static class PolicePedFightSetup
{
    const string ControllerPath = "Assets/Game/Police/PoliceOfficer.controller";
    const string MeleeSet = "Assets/Invector-3rdPersonController/Melee Combat/3DModels/Animations/Melee_CombatSet.fbx";
    const string ActionsSet = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_Actions.fbx";
    const string AudioFolder = "Assets/Invector-3rdPersonController/Melee Combat/Audio/";
    static readonly string[] Prefabs =
    {
        "Assets/_Project/Prefabs/Pedestrians/Ped_Police_2.prefab",
        "Assets/_Project/Prefabs/Pedestrians/Ped_Police_3.prefab",
    };

    // state, clip, speed, held pose at (normalized time; < 0 = plays through)
    static readonly (string state, string clip, float speed, float hold)[] Jobs =
    {
        ("hit_front", "Hit Small Front", 1.3f, -1f),
        ("hit_left", "Hit Small Left", 1.3f, -1f),
        ("hit_right", "Hit Small Right", 1.3f, -1f),
        ("hit_back", "Hit Small Back", 1.3f, -1f),
        ("hit_big", "Hit Big Front", 1.15f, -1f),
        ("brawl_guard", "Defense_Unarmed", 0f, 0.45f),
        ("brawl_block", "Defense_Unarmed", 1.4f, -1f),
        ("brawl_jab", "WeakAttack_UnarmedA", 1f, -1f),
        ("brawl_cross", "WeakAttack_UnarmedB", 1f, -1f),
        ("brawl_hook", "StrongAttack_PunchA", 1.1f, -1f),
        ("getup_back", "GetUpFromBack", 1.25f, -1f),
        ("getup_belly", "GetUpFromBelly", 1.1f, -1f),
    };

    [MenuItem("Game/Police Pedestrians Fight Back")]
    static void Run() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var log = new System.Text.StringBuilder("[PolicePedFightSetup]\n");
        var clips = new Dictionary<string, AnimationClip>();
        foreach (string file in new[] { MeleeSet, ActionsSet })
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(file))
                if (o is AnimationClip c && !c.name.StartsWith("__preview") && !clips.ContainsKey(c.name)) clips[c.name] = c;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return "[PolicePedFightSetup] no controller at " + ControllerPath;
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        int added = 0;
        foreach (var job in Jobs)
        {
            if (!clips.TryGetValue(job.clip, out AnimationClip clip)) { log.AppendLine("  !! clip missing: " + job.clip); continue; }
            AnimatorState state = null;
            foreach (ChildAnimatorState s in sm.states) if (s.state.name == job.state) state = s.state;
            if (state == null) { state = sm.AddState(job.state, new Vector3(650f, 40f * sm.states.Length, 0f)); added++; }
            state.motion = clip;
            // A held pose (the guard stance) is the clip frozen at one point; everything else plays through and holds its
            // last frame until the brawler cross-fades to the next state.
            state.speed = job.hold >= 0f ? 0f : job.speed;
            state.cycleOffset = job.hold >= 0f ? job.hold : 0f;
            state.writeDefaultValues = true;
            state.iKOnFeet = true;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  controller: {added} states added, {Jobs.Length} set");

        AudioClip[] hits = Audio("PunchA.wav", "PunchB.wav", "Hit 1.wav", "Hit 2.wav", "Hit 3.wav");
        AudioClip[] blocks = Audio("Hit 1.wav", "Hit 2.wav");
        AudioClip[] swings = Audio("Hit 3.wav");
        float getUpSeconds = clips.TryGetValue("GetUpFromBack", out AnimationClip back) ? back.length / 1.25f : 1.6f;
        foreach (string path in Prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var bridge = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamageBridge>();
                var damage = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
                if (bridge == null || damage == null) { log.AppendLine("  !! no damage bridge on " + root.name); continue; }
                var so = new SerializedObject(bridge);
                SerializedProperty brawl = so.FindProperty("brawl");
                // An officer always answers a punch with his fists and keeps at it.
                brawl.FindPropertyRelative("fightBackChance").floatValue = 1f;
                brawl.FindPropertyRelative("giveUpHealth").floatValue = 0f;
                brawl.FindPropertyRelative("giveUpDistance").floatValue = 25f;
                brawl.FindPropertyRelative("blockChance").floatValue = 0.35f;
                brawl.FindPropertyRelative("dodgeChance").floatValue = 0.15f;
                brawl.FindPropertyRelative("damageScale").floatValue = 1.2f;
                brawl.FindPropertyRelative("runSpeed").floatValue = 4.2f;
                brawl.FindPropertyRelative("attackIntervalMin").floatValue = 0.8f;
                brawl.FindPropertyRelative("attackIntervalMax").floatValue = 1.4f;
                brawl.FindPropertyRelative("bystanderPanicRadius").floatValue = 10f;
                SetClips(brawl.FindPropertyRelative("hitSounds"), hits);
                SetClips(brawl.FindPropertyRelative("blockSounds"), blocks);
                SetClips(brawl.FindPropertyRelative("swingSounds"), swings);
                so.ApplyModifiedPropertiesWithoutUndo();

                var dso = new SerializedObject(damage);
                dso.FindProperty("meleeHitsToKnockDown").intValue = 4; // a bit sturdier than a civilian
                dso.ApplyModifiedPropertiesWithoutUndo();

                // Get up with the clip after a knock-down, then back into the fight.
                var ragdoll = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianRagdoll>();
                if (ragdoll != null)
                {
                    var rso = new SerializedObject(ragdoll);
                    rso.FindProperty("getUpFromBackState").stringValue = "getup_back";
                    rso.FindProperty("getUpFromBellyState").stringValue = "getup_belly";
                    rso.FindProperty("getUpClipSeconds").floatValue = getUpSeconds;
                    rso.FindProperty("getUpBlendTime").floatValue = 0.4f;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine("  " + root.name + ": always fights back, never gives up, get-up clips");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return log.ToString();
    }

    static AudioClip[] Audio(params string[] names)
    {
        var list = new List<AudioClip>();
        foreach (string n in names)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + n);
            if (clip != null) list.Add(clip);
        }
        return list.ToArray();
    }

    static void SetClips(SerializedProperty array, AudioClip[] clips)
    {
        if (array == null) return;
        array.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }
}
