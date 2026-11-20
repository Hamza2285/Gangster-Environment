using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Player fist-fight animation additions on our player controller (OWC_Shooter_Josh.controller, the base of Jason's and
    /// Mia's override controllers; Invector's own controllers are not touched):
    /// - "RunPunch" layer (upper body, weight driven by <see cref="PlayerRunningPunch"/>): a punch thrown on the run while the
    ///   legs keep running. States RunPunch_R (Invector WeakAttack_UnarmedA, a straight right) and RunPunch_L (Moto
    ///   AS_Punch_Front, a long left lunge).
    /// - "Medium Hit Reaction" on the FullBody layer: Moto's AS_Get_Hit_Front/Back/Left/Right (head snap / body hit) for
    ///   ReactionID 2, between Invector's small upper-body flinch (0) and big full-body stagger (1).
    /// The Moto clips are our copies in Animations/Combat/Moto (root height from the feet; the pack's import keeps the
    /// capture's root height, which lifted the body 0.27 m off the ground).
    /// And adds <see cref="PlayerRunningPunch"/> to every playable character in the open scene.
    /// Menu: Tools > OpenworldCrime > Player > Build Fist Fight Animations.
    /// </summary>
    public static class PlayerCombatAnimBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animations/OWC_Shooter_Josh.controller";
        private const string UpperBodyMask = "Assets/Invector-3rdPersonController/Basic Locomotion/Animator/AvatarMasks/UpperBody.mask";
        private const string MeleeSet = "Assets/Invector-3rdPersonController/Melee Combat/3DModels/Animations/Melee_CombatSet.fbx";
        private const string MotoPunchFront = "Assets/_Project/Animations/Combat/Moto/AS_Punch_Front.fbx";
        private const string MotoHitFolder = "Assets/_Project/Animations/Combat/Moto/";
        public const string RunPunchLayer = "RunPunch";
        private const int MediumReactionId = 2;

        [MenuItem("Tools/OpenworldCrime/Player/Build Fist Fight Animations")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[PlayerCombatAnimBuilder]\n");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return log.Append("  controller not found").ToString();

            AnimationClip straight = Clip(MeleeSet, "WeakAttack_UnarmedA");
            AnimationClip lunge = Clip(MotoPunchFront, "AS_Punch_Front");
            if (straight == null || lunge == null) return log.Append("  punch clips not found").ToString();

            // --- RunPunch layer (rebuilt every time).
            AnimatorControllerLayer[] layers = controller.layers;
            int existing = -1;
            for (int i = 0; i < layers.Length; i++) if (layers[i].name == RunPunchLayer) existing = i;
            if (existing >= 0) controller.RemoveLayer(existing);
            var sm = new AnimatorStateMachine { name = RunPunchLayer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var layer = new AnimatorControllerLayer
            {
                name = RunPunchLayer,
                stateMachine = sm,
                avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask),
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 0f,
                iKPass = false
            };
            controller.AddLayer(layer);
            AnimatorState empty = sm.AddState("Empty", new Vector3(300f, 0f, 0f));
            sm.defaultState = empty;
            AddPunch(sm, "RunPunch_R", straight, 1.15f, new Vector3(300f, 80f, 0f));
            AddPunch(sm, "RunPunch_L", lunge, 1.3f, new Vector3(300f, 160f, 0f));
            log.AppendLine($"  layer {RunPunchLayer}: RunPunch_R <- {straight.name}, RunPunch_L <- {lunge.name}");

            // --- Medium hit reactions on the FullBody layer.
            layers = controller.layers;
            int full = -1;
            for (int i = 0; i < layers.Length; i++) if (layers[i].name == "FullBody") full = i;
            if (full < 0) return log.Append("  FullBody layer not found").ToString();
            AnimatorStateMachine root = layers[full].stateMachine;
            AnimatorStateMachine medium = null;
            foreach (ChildAnimatorStateMachine child in root.stateMachines) if (child.stateMachine.name == "Medium Hit Reaction") medium = child.stateMachine;
            if (medium != null)
            {
                // Rebuild: drop its any-state transitions first.
                foreach (AnimatorStateTransition t in root.anyStateTransitions)
                    if (t.destinationState != null && System.Array.Exists(medium.states, s => s.state == t.destinationState)) root.RemoveAnyStateTransition(t);
                root.RemoveStateMachine(medium);
            }
            medium = root.AddStateMachine("Medium Hit Reaction", new Vector3(700f, 300f, 0f));
            var directions = new[] { ("Front", 0), ("Back", 180), ("Left", -90), ("Right", 90) };
            int y = 0;
            foreach (var (name, angle) in directions)
            {
                AnimationClip clip = Clip(MotoHitFolder + "AS_Get_Hit_" + name + ".fbx", "AS_Get_Hit_" + name);
                if (clip == null) { log.AppendLine("  missing Moto hit " + name); continue; }
                AnimatorState state = medium.AddState("Medium_From_" + name, new Vector3(300f, 60f * y++, 0f));
                state.motion = clip;
                state.speed = 1.15f;
                state.writeDefaultValues = true;
                AnimatorStateTransition exit = state.AddExitTransition();
                exit.hasExitTime = true;
                exit.exitTime = 0.82f;
                exit.duration = 0.2f;
                AnimatorStateTransition any = root.AddAnyStateTransition(state);
                any.AddCondition(AnimatorConditionMode.If, 0f, "TriggerReaction");
                any.AddCondition(AnimatorConditionMode.Equals, angle, "HitDirection");
                any.AddCondition(AnimatorConditionMode.Equals, MediumReactionId, "ReactionID");
                any.duration = 0.12f;
                any.canTransitionToSelf = true;
                log.AppendLine($"  FullBody Medium_From_{name} <- {clip.name} (ReactionID {MediumReactionId}, HitDirection {angle})");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // --- The component on every playable character in the scene.
            int added = 0;
            const string audio = "Assets/Invector-3rdPersonController/Melee Combat/Audio/";
            AudioClip[] hits = Sounds(audio, "PunchA.wav", "PunchB.wav", "Hit 1.wav");
            AudioClip[] swings = Sounds(audio, "Hit 3.wav");
            foreach (PlayableCharacter character in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (EditorUtility.IsPersistent(character)) continue;
                var punch = character.GetComponent<PlayerRunningPunch>();
                if (punch == null) { punch = Undo.AddComponent<PlayerRunningPunch>(character.gameObject); added++; }
                var so = new SerializedObject(punch);
                SetArray(so.FindProperty("hitSounds"), hits);
                SetArray(so.FindProperty("swingSounds"), swings);
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                log.AppendLine($"  {character.name}: PlayerRunningPunch ({hits.Length} hit / {swings.Length} swing sounds)");
            }
            log.AppendLine($"  PlayerRunningPunch added to {added} character(s)");
            return log.ToString();
        }

        private static AudioClip[] Sounds(string folder, params string[] names)
        {
            var list = new List<AudioClip>();
            foreach (string n in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + n);
                if (clip != null) list.Add(clip);
            }
            return list.ToArray();
        }

        private static void SetArray(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private static void AddPunch(AnimatorStateMachine sm, string name, AnimationClip clip, float speed, Vector3 position)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = clip;
            state.speed = speed;
            state.writeDefaultValues = true;
        }

        private static AnimationClip Clip(string path, string name)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && c.name == name) return c;
            return null;
        }
    }
}
