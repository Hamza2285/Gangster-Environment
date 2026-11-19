using System.Collections.Generic;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Player feel setup (open city scene):
    /// - "Player Feel" under ///Manager/// with every playable character's controller and head track.
    /// - Jason's override controller (ours) plays Invector's stock Run again (it used the Sprint clip in the Run slot).
    /// - Our camera states: the on-foot Default state is lower and further back with almost no forced downward tilt.
    /// Menu: Tools > OpenworldCrime > Player > Set Up Player Feel.
    /// </summary>
    public static class PlayerFeelSetup
    {
        private const string JasonOverridePath = "Assets/_Project/Animations/OWC_Shooter_Jason.overrideController";
        private const string CameraStatesPath = "Assets/_Project/Data/Camera/OWC_ShooterMelee_CameraStates.asset";
        private const string MiaOverridePath = "Assets/_Project/Animations/OWC_Shooter_Mia.overrideController";
        private const string MiaSmoothOverridePath = "Assets/_Project/Animations/OWC_Shooter_Mia_Smooth.overrideController";
        private const string MiaIdleSource = "Assets/Game/Player/Mia Idle.fbx";
        private const string MiaIdleLoopPath = "Assets/_Project/Animations/Player/Mia_Idle_Loop.anim";
        // Section of Mia's 13.3 s idle that loops cleanly (pose + velocity match measured over the whole clip) and is about as
        // long as her walk cycle, so the locomotion blend tree's time sync no longer slows the walk at start / stop.
        private const float MiaLoopStart = 6.30f;
        private const float MiaLoopEnd = 7.50f;
        private const float LoopBlendSeconds = 0.15f;

        [MenuItem("Tools/OpenworldCrime/Player/Set Up Player Feel (open city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[PlayerFeelSetup]\n");
            Scene city = SceneManager.GetActiveScene();

            var controllers = new List<vThirdPersonController>();
            var heads = new List<vHeadTrack>();
            foreach (PlayableCharacter pc in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (pc.gameObject.scene != city) continue;
                var c = pc.GetComponent<vThirdPersonController>();
                var h = pc.GetComponent<vHeadTrack>();
                if (c != null) controllers.Add(c);
                if (h != null) heads.Add(h);
                log.AppendLine("  character " + pc.name);
            }
            GameObject managers = null;
            foreach (GameObject root in city.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[PlayerFeelSetup] ///Manager/// not found";
            Transform existing = managers.transform.Find("Player Feel");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Player Feel");
            go.transform.SetParent(managers.transform, false);
            PlayerFeelSettings feel = go.GetComponent<PlayerFeelSettings>();
            if (feel == null) feel = go.AddComponent<PlayerFeelSettings>();
            var so = new SerializedObject(feel);
            SetArray(so.FindProperty("controllers"), controllers.ToArray());
            SetArray(so.FindProperty("headTracks"), heads.ToArray());
            so.FindProperty("animationSmooth").floatValue = 0.05f;
            // Mia: our override copy with the short idle loop.
            AnimatorOverrideController miaSmooth = BuildMiaSmoothOverride(log);
            var overridesProp = so.FindProperty("animatorControllers");
            overridesProp.arraySize = 0;
            foreach (PlayableCharacter pc in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (pc.gameObject.scene != city || pc.id != "girl" || miaSmooth == null) continue;
                overridesProp.arraySize++;
                var e = overridesProp.GetArrayElementAtIndex(overridesProp.arraySize - 1);
                e.FindPropertyRelative("animator").objectReferenceValue = pc.GetComponent<Animator>();
                e.FindPropertyRelative("controller").objectReferenceValue = miaSmooth;
                log.AppendLine("  " + pc.name + " -> " + MiaSmoothOverridePath);
            }
            // Symmetric arm swing in the locomotion clips (rebuilt on top of the override controllers).
            log.Append(PlayerGaitBuilder.Build());
            // Stride match: per-character speeds and natural clip speeds are PlayerFeelSettings' profile defaults (keyed by id).
            so.ApplyModifiedPropertiesWithoutUndo();

            // Smooth (interpolated) character and camera motion.
            Transform smoothExisting = managers.transform.Find("Smooth Player Motion");
            GameObject smoothGo = smoothExisting != null ? smoothExisting.gameObject : new GameObject("Smooth Player Motion");
            smoothGo.transform.SetParent(managers.transform, false);
            SmoothPlayerMotion smooth = smoothGo.GetComponent<SmoothPlayerMotion>();
            if (smooth == null) smooth = smoothGo.AddComponent<SmoothPlayerMotion>();
            var smoothSo = new SerializedObject(smooth);
            var rig = Object.FindFirstObjectByType<Invector.vCamera.vThirdPersonCamera>(FindObjectsInactive.Include);
            smoothSo.FindProperty("cameraRig").objectReferenceValue = rig != null ? rig.transform : null;
            Camera view = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            smoothSo.FindProperty("cameraView").objectReferenceValue = view != null ? view.transform : null;
            var bodies = new List<Rigidbody>();
            var models = new List<Transform>();
            foreach (vThirdPersonController c in controllers)
            {
                if (c == null || c.GetComponent<Rigidbody>() == null) continue;
                var game = c.GetComponent<InvectorGamePlayer>();
                Object model = game != null ? new SerializedObject(game).FindProperty("characterModel").objectReferenceValue : null;
                if (model == null) { log.AppendLine("  no model object on " + c.name); continue; }
                bodies.Add(c.GetComponent<Rigidbody>());
                models.Add(((GameObject)model).transform);
            }
            SetArray(smoothSo.FindProperty("bodies"), bodies.ToArray());
            SetArray(smoothSo.FindProperty("models"), models.ToArray());
            smoothSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  'Smooth Player Motion': rig {(rig != null ? rig.name : "MISSING")}, view {(view != null ? view.name : "MISSING")}, models {string.Join(", ", models.ConvertAll(m => m.name))}");
            EditorSceneManager.MarkSceneDirty(city);

            // Jason: stock Run in the Run slot.
            var jason = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(JasonOverridePath);
            if (jason != null)
            {
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                jason.GetOverrides(overrides);
                for (int i = 0; i < overrides.Count; i++)
                    if (overrides[i].Key != null && overrides[i].Key.name == "Run" && overrides[i].Value != null)
                    {
                        log.AppendLine($"  Jason override: Run -> {overrides[i].Value.name} removed (stock Run)");
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, null);
                    }
                jason.ApplyOverrides(overrides);
                EditorUtility.SetDirty(jason);
            }

            // Camera: lower, further back, little forced tilt (on-foot Default state only).
            Object states = AssetDatabase.LoadMainAssetAtPath(CameraStatesPath);
            var cso = new SerializedObject(states);
            SerializedProperty list = cso.FindProperty("tpCameraStates");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty s = list.GetArrayElementAtIndex(i);
                if (s.FindPropertyRelative("Name").stringValue != "Default") continue;
                log.AppendLine($"  camera Default before: height {s.FindPropertyRelative("height").floatValue}, distance {s.FindPropertyRelative("defaultDistance").floatValue}, right {s.FindPropertyRelative("right").floatValue}, tilt {s.FindPropertyRelative("rotationOffSet").vector3Value.x}");
                s.FindPropertyRelative("height").floatValue = 1.42f;
                s.FindPropertyRelative("defaultDistance").floatValue = 2.5f;
                s.FindPropertyRelative("right").floatValue = 0.3f;
                s.FindPropertyRelative("rotationOffSet").vector3Value = new Vector3(2f, 0f, 0f);
                // Follow smoothing: 6 (0.1 s smooth-damp, a rubber-band surge at every start / stop) -> 2, the
                // value of Invector's basic-locomotion camera states.
                log.AppendLine($"  camera Default smoothDamp {s.FindPropertyRelative("smoothDamp").floatValue} -> 2");
                s.FindPropertyRelative("smoothDamp").floatValue = 2f;
                log.AppendLine("  camera Default after: height 1.42, distance 2.5, right 0.3, tilt 2");
            }
            cso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(states);
            AssetDatabase.SaveAssetIfDirty(states);
            AssetDatabase.SaveAssetIfDirty(jason);
            log.AppendLine($"  'Player Feel' under ///Manager/// with {controllers.Count} characters (save the scene)");
            return log.ToString();
        }

        private static AnimatorOverrideController BuildMiaSmoothOverride(StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(MiaOverridePath);
            AnimationClip idle = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MiaIdleSource))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) idle = c;
            if (source == null || idle == null) { log.AppendLine("  Mia override or idle not found"); return null; }

            // Loop clip: the section, shifted to 0, with the last LoopBlendSeconds eased into the first pose.
            var loop = new AnimationClip { name = "Mia_Idle_Loop" };
            float length = MiaLoopEnd - MiaLoopStart;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(idle))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(idle, binding);
                var keys = new List<Keyframe>();
                float startValue = curve.Evaluate(MiaLoopStart);
                for (float t = 0f; t < length - 0.0001f; t += 1f / 30f)
                {
                    float v = curve.Evaluate(MiaLoopStart + t);
                    float blend = Mathf.InverseLerp(length - LoopBlendSeconds, length, t);
                    keys.Add(new Keyframe(t, Mathf.Lerp(v, startValue, blend * blend * (3f - 2f * blend))));
                }
                keys.Add(new Keyframe(length, startValue));
                var trimmed = new AnimationCurve(keys.ToArray());
                for (int k = 0; k < trimmed.length; k++) AnimationUtility.SetKeyLeftTangentMode(trimmed, k, AnimationUtility.TangentMode.ClampedAuto);
                for (int k = 0; k < trimmed.length; k++) AnimationUtility.SetKeyRightTangentMode(trimmed, k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(loop, binding, trimmed);
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(idle);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.startTime = 0f;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(loop, settings);
            string folder = System.IO.Path.GetDirectoryName(MiaIdleLoopPath).Replace(System.IO.Path.DirectorySeparatorChar, '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project/Animations", "Player");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(MiaIdleLoopPath);
            if (existing != null) { EditorUtility.CopySerialized(loop, existing); Object.DestroyImmediate(loop); loop = existing; }
            else AssetDatabase.CreateAsset(loop, MiaIdleLoopPath);

            // Override copy: same base and clips as Hamza's Mia override, idle replaced.
            var smooth = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(MiaSmoothOverridePath);
            if (smooth == null)
            {
                smooth = new AnimatorOverrideController(source.runtimeAnimatorController) { name = "OWC_Shooter_Mia_Smooth" };
                AssetDatabase.CreateAsset(smooth, MiaSmoothOverridePath);
            }
            smooth.runtimeAnimatorController = source.runtimeAnimatorController;
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            source.GetOverrides(list);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Key != null && list[i].Key.name == "Josh_Idle") list[i] = new KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, loop);
            smooth.ApplyOverrides(list);
            EditorUtility.SetDirty(smooth);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  {MiaIdleLoopPath}: {length:F2} s loop (Mia idle {MiaLoopStart}-{MiaLoopEnd} s, humanoid={loop.humanMotion}); {MiaSmoothOverridePath}");
            return smooth;
        }

        private static void SetArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
