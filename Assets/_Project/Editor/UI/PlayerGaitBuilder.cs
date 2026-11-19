using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Symmetric arm swing for the players' locomotion. Some run / sprint clips swing one arm differently from the other
    /// (measured: Jason's Run holds the right upper arm 11 deg further back, Mia's Running 26 deg). For a loop of two steps
    /// the left side at time t should mirror the right side half a cycle later, so each symmetric clip takes, per Humanoid
    /// arm / shoulder / hand / finger muscle, the average of the muscle and its opposite-side muscle half a cycle on.
    /// The torso's side lean / twist keeps its alternating part but loses its constant offset. Legs, hips, root motion and
    /// IK goal curves are copied unchanged, so foot contacts stay exact.
    /// A clip can also be phase-shifted so it steps with the same foot as its neighbours in the locomotion blend tree:
    /// Jason's walk had the left foot forward at 27% of its cycle and his run / sprint at 84% / 77%, so the half-stick
    /// walk-run blend averaged a left step with a right step and neither foot planted. His walk copy starts 0.43 of a cycle in.
    /// The source clips (Invector / Hamza / the user's) are not changed; our override controllers point at the copies.
    /// Menu: Tools > OpenworldCrime > Player > Build Symmetric Locomotion Clips.
    /// </summary>
    public static class PlayerGaitBuilder
    {
        private const string Folder = "Assets/_Project/Animations/Player/Symmetric";
        private const string JasonOverride = "Assets/_Project/Animations/OWC_Shooter_Jason.overrideController";
        private const string MiaOverride = "Assets/_Project/Animations/OWC_Shooter_Mia_Smooth.overrideController";
        private const float SampleRate = 60f;
        private const float JasonWalkPhase = 0.43f; // the copy starts 0.43 cycle into the original: left foot forward 0.27 -> 0.84

        [MenuItem("Tools/OpenworldCrime/Player/Build Symmetric Locomotion Clips")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[PlayerGaitBuilder]\n");
            EnsureFolder(Folder);
            Apply(JasonOverride, new[] { "Run", "Sprint", "Josh_Walk" }, log, new Dictionary<string, float> { { "Josh_Walk", JasonWalkPhase } });
            Apply(MiaOverride, new[] { "Run", "Sprint", "Walk", "Josh_Walk" }, log, null);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static void Apply(string overridePath, string[] slots, StringBuilder log, Dictionary<string, float> phases)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
            if (controller == null) { log.AppendLine("  missing " + overridePath); return; }
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            var made = new Dictionary<AnimationClip, AnimationClip>();
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip slot = overrides[i].Key;
                if (slot == null || System.Array.IndexOf(slots, slot.name) < 0) continue;
                AnimationClip source = overrides[i].Value != null ? overrides[i].Value : slot;
                if (source.name.EndsWith("_Symmetric")) source = FindSource(source) ?? source; // re-run: rebuild from the original
                if (!made.TryGetValue(source, out AnimationClip symmetric))
                {
                    float phase = phases != null && phases.TryGetValue(slot.name, out float ph) ? ph : 0f;
                    symmetric = Symmetrize(source, phase);
                    made[source] = symmetric;
                }
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(slot, symmetric);
                log.AppendLine($"  {controller.name}: {slot.name} -> {symmetric.name} (from {AssetDatabase.GetAssetPath(source)})");
            }
            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
        }

        // The symmetric copy remembers its source in the asset's user data.
        private static AnimationClip FindSource(AnimationClip symmetric)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(symmetric));
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return null;
            string[] parts = importer.userData.Split('|');
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(parts[0]))
                if (o is AnimationClip c && c.name == parts[1]) return c;
            return null;
        }

        private static AnimationClip Symmetrize(AnimationClip source, float phase)
        {
            float length = source.length;
            float half = length * 0.5f;
            float offset = Mathf.Repeat(phase, 1f) * length;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
            var byName = new Dictionary<string, AnimationCurve>();
            foreach (EditorCurveBinding b in bindings) byName[b.propertyName] = AnimationUtility.GetEditorCurve(source, b);

            var clip = new AnimationClip { name = source.name + "_Symmetric", frameRate = SampleRate };
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * SampleRate));
            foreach (EditorCurveBinding b in bindings)
            {
                AnimationCurve curve = byName[b.propertyName];
                string name = b.propertyName;
                bool copyAsIs = b.type != typeof(Animator) || name.StartsWith("Root") || IsIkGoal(name) || !(IsArm(name) || IsTorsoTwist(name));
                AnimationCurve result;
                if (copyAsIs && offset <= 0f) result = curve;
                else if (copyAsIs) result = Shift(curve, length, offset, name.StartsWith("RootT"));
                else
                {
                    string opposite = Opposite(name);
                    AnimationCurve other = opposite != null && byName.TryGetValue(opposite, out AnimationCurve o) ? o : null;
                    bool odd = opposite == null && name.Contains("Left-Right");
                    var keys = new Keyframe[frames + 1];
                    for (int f = 0; f <= frames; f++)
                    {
                        float t = length * f / frames;
                        float shifted = Mathf.Repeat(t + half, length);
                        float own = curve.Evaluate(Mathf.Repeat(t + offset, length));
                        shifted = Mathf.Repeat(shifted + offset, length);
                        float value;
                        if (other != null) value = 0.5f * (own + other.Evaluate(shifted));      // left / right pair
                        else if (opposite == null) value = 0.5f * (own + (odd ? -1f : 1f) * curve.Evaluate(shifted)); // centre
                        else value = own;                                                         // pair missing
                        keys[f] = new Keyframe(t, value);
                    }
                    result = new AnimationCurve(keys);
                    for (int k = 0; k < result.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(result, k, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(result, k, AnimationUtility.TangentMode.ClampedAuto);
                    }
                }
                AnimationUtility.SetEditorCurve(clip, b, result);
            }
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(source);
            foreach (AnimationEvent e in events) e.time = Mathf.Repeat(e.time - offset, length);
            AnimationUtility.SetAnimationEvents(clip, events);

            string path = $"{Folder}/{Sanitize(source.name)}_Symmetric.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); clip = existing; }
            else AssetDatabase.CreateAsset(clip, path);
            AssetImporter.GetAtPath(path).userData = AssetDatabase.GetAssetPath(source) + "|" + source.name;
            return clip;
        }

        // Only the arms are made symmetric; legs, spine and head keep the original (their foot contacts stay exact).
        private static bool IsArm(string name) =>
            name.StartsWith("LeftHand.") || name.StartsWith("RightHand.") ||
            ((name.StartsWith("Left ") || name.StartsWith("Right ")) &&
             (name.Contains("Shoulder") || name.Contains(" Arm ") || name.Contains("Forearm") || name.Contains(" Hand ")));

        // The curve started `offset` seconds later in its loop. Root position keeps going forward across the wrap.
        private static AnimationCurve Shift(AnimationCurve curve, float length, float offset, bool cumulative)
        {
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * SampleRate));
            float atOffset = curve.Evaluate(offset), atStart = curve.Evaluate(0f), atEnd = curve.Evaluate(length);
            var keys = new Keyframe[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                float t = length * f / frames;
                float u = t + offset;
                float v;
                if (!cumulative) v = curve.Evaluate(Mathf.Repeat(u, length));
                else if (u <= length) v = curve.Evaluate(u) - atOffset + atStart;
                else v = curve.Evaluate(u - length) + atEnd - atOffset;
                keys[f] = new Keyframe(t, v);
            }
            var result = new AnimationCurve(keys);
            for (int k = 0; k < result.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(result, k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(result, k, AnimationUtility.TangentMode.ClampedAuto);
            }
            return result;
        }

        // Spine / chest / neck / head lean and twist: the constant part is removed (a torso held turned to one side makes
        // one arm swing further forward than the other); the alternating part of each step stays.
        private static bool IsTorsoTwist(string name) =>
            (name.StartsWith("Spine ") || name.StartsWith("Chest ") || name.StartsWith("UpperChest ") || name.StartsWith("Neck ") || name.StartsWith("Head "))
            && name.Contains("Left-Right");

        private static bool IsIkGoal(string name) =>
            name.StartsWith("LeftFootT") || name.StartsWith("LeftFootQ") || name.StartsWith("RightFootT") || name.StartsWith("RightFootQ") ||
            name.StartsWith("LeftHandT") || name.StartsWith("LeftHandQ") || name.StartsWith("RightHandT") || name.StartsWith("RightHandQ");

        private static string Opposite(string name)
        {
            if (name.StartsWith("Left ")) return "Right " + name.Substring(5);
            if (name.StartsWith("Right ")) return "Left " + name.Substring(6);
            if (name.StartsWith("LeftHand.")) return "RightHand." + name.Substring(9);
            if (name.StartsWith("RightHand.")) return "LeftHand." + name.Substring(10);
            return null;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
