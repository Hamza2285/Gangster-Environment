using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The project has no swim animations (none in Invector, Moto or the Mixamo-style packs), so two looping humanoid clips are
    /// generated from muscle curves - they retarget to any humanoid (Jason, Mia):
    /// - Swim_Tread: upright treading water (sculling arms, eggbeater legs), for floating in place;
    /// - Swim_Breaststroke: breaststroke arms and frog kick with the head lifted, for swimming (PlayerSwimController pitches the
    ///   body towards horizontal by speed; the clip itself is upright).
    /// Root motion is baked into the pose (the swim controller moves the character). Re-running rewrites the same assets
    /// (same GUIDs, so references stay). Used by WaterSetup; also on its own under Tools > OpenworldCrime > Player.
    /// Replace them with real motion-captured clips by assigning those on PlayerSwimController (same slots).
    /// </summary>
    public static class SwimAnimationBuilder
    {
        public const string Folder = "Assets/_Project/Animations/Player/Swim";
        public const string TreadPath = Folder + "/Swim_Tread.anim";
        public const string SwimPath = Folder + "/Swim_Breaststroke.anim";

        private const float TreadPeriod = 1.6f;
        private const float SwimPeriod = 1.3f;
        private const int Samples = 32;

        [MenuItem("Tools/OpenworldCrime/Player/Build Swim Animations")]
        public static void BuildMenu()
        {
            Build(out AnimationClip tread, out AnimationClip swim);
            Debug.Log($"[SwimAnimationBuilder] {AssetDatabase.GetAssetPath(tread)}, {AssetDatabase.GetAssetPath(swim)}");
        }

        public static void Build(out AnimationClip tread, out AnimationClip swim)
        {
            EnsureFolder(Folder);
            tread = Write(TreadPath, "Swim_Tread", TreadPeriod, TreadPose);
            swim = Write(SwimPath, "Swim_Breaststroke", SwimPeriod, SwimPose);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- poses (p = 0..1 through the loop)

        // Muscle conventions (checked against Invector's Crouch / Run clips): Upper Leg Front-Back ~0.5 = leg straight under the
        // body, lower = thigh forward, higher = behind; Lower Leg / Forearm Stretch 1 = straight, each 0.1 less ~8 deg of bend;
        // Spine / Chest Front-Back negative = bend forward; Neck / Head Nod positive = look up; Arm Down-Up 0.4 = horizontal,
        // -0.55 = hanging; Arm Front-Back NEGATIVE swings the (raised) arm forward. Arm keys were found by searching the muscle
        // values that point the upper arm where wanted with the body pitched (60 deg moving, 10 deg treading). The body pitch (lean towards horizontal) is NOT in the clips: Invector
        // drops root rotation, so PlayerSwimController tilts the skeleton itself by swim speed.

        // Treading water: upright, knees moderately bent and slowly cycling under the hips (never kneeling), hands sculling at
        // chest height just under the surface.
        private static void TreadPose(float p, Dictionary<string, float> m, out Vector3 bodyPos, out Quaternion bodyRot)
        {
            float a = p * Mathf.PI * 2f;
            bodyPos = new Vector3(0f, 0.96f, 0f);
            bodyRot = Quaternion.identity;

            m["Spine Front-Back"] = -0.12f;
            m["Chest Front-Back"] = -0.05f;
            m["Neck Nod Down-Up"] = 0.25f;
            m["Head Nod Down-Up"] = 0.1f;
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                float scull = Mathf.Sin(a * 2f);
                m[side + " Shoulder Down-Up"] = 0.05f;
                m[side + " Arm Down-Up"] = 0.18f + 0.05f * Mathf.Sin(a * 2f + 1.6f);  // forward-out, a little under the shoulders
                m[side + " Arm Front-Back"] = -0.15f + 0.2f * scull;                // sweeping in front and out to the side
                m[side + " Arm Twist In-Out"] = 0f;
                m[side + " Forearm Stretch"] = 0.5f + 0.1f * Mathf.Sin(a * 2f + 1.2f); // elbows ~40 deg
                m[side + " Forearm Twist In-Out"] = 0.35f * scull;                   // palms turning
                m[side + " Hand Down-Up"] = 0.25f * Mathf.Sin(a * 2f + 1.6f);
                m[side + " Hand In-Out"] = 0f;

                // Eggbeater: alternating slow circles, thighs 20-40 deg forward, knees 40-75 deg.
                float leg = a + (s == 0 ? 0f : Mathf.PI);
                m[side + " Upper Leg Front-Back"] = 0.17f + 0.11f * Mathf.Sin(leg);
                m[side + " Upper Leg In-Out"] = 0.22f + 0.06f * Mathf.Cos(leg);
                m[side + " Upper Leg Twist In-Out"] = 0.15f;
                m[side + " Lower Leg Stretch"] = 0.3f + 0.2f * Mathf.Sin(leg + Mathf.PI * 0.5f);
                m[side + " Foot Up-Down"] = 0.15f + 0.2f * Mathf.Sin(leg + Mathf.PI);
            }
            Fingers(m, 0.35f);
        }

        // Breaststroke (in the body's own frame; the controller pitches the body ~60 deg): glide with arms reaching ahead and legs
        // trailing straight -> arms sweep out and pull -> hands in under the chest while the heels come up behind (knees apart,
        // thighs only a little forward) -> arms shoot forward while the legs kick out and snap together.
        private static readonly float[] ArmPhase = { 0f, 0.2f, 0.4f, 0.55f, 0.7f, 0.85f, 1f };
        private static readonly float[] ArmDownUp = { 0.8f, 0.7f, 0.4f, 0.2f, 0.4f, 0.7f, 0.8f };
        private static readonly float[] ArmFrontBack = { -0.6f, -0.3f, 0.0f, -0.3f, -0.5f, -0.6f, -0.6f };
        private static readonly float[] Forearm = { 1.0f, 0.95f, 0.55f, -0.2f, 0.3f, 0.9f, 1.0f };
        private static readonly float[] ForearmTwist = { 0.0f, -0.4f, -0.5f, 0.2f, 0.3f, 0.1f, 0.0f };

        private static readonly float[] LegPhase = { 0f, 0.45f, 0.62f, 0.76f, 0.88f, 1f };
        private static readonly float[] UpperLeg = { 0.58f, 0.55f, 0.28f, 0.45f, 0.6f, 0.58f };
        private static readonly float[] LegInOut = { 0.04f, 0.08f, 0.35f, 0.65f, 0.05f, 0.04f };
        private static readonly float[] LowerLeg = { 0.95f, 0.92f, -0.35f, 0.4f, 1.0f, 0.95f };
        private static readonly float[] Foot = { 0.7f, 0.6f, -0.5f, -0.3f, 0.7f, 0.7f };

        private static void SwimPose(float p, Dictionary<string, float> m, out Vector3 bodyPos, out Quaternion bodyRot)
        {
            float a = p * Mathf.PI * 2f;
            bodyPos = new Vector3(0f, 0.96f, 0f);
            bodyRot = Quaternion.identity;

            // Back slightly arched and the head lifted so the face looks ahead with the body pitched (~50 deg over neck + head).
            m["Spine Front-Back"] = 0.15f;
            m["Chest Front-Back"] = 0.1f;
            m["UpperChest Front-Back"] = 0.05f;
            m["Neck Nod Down-Up"] = 0.75f + 0.1f * Mathf.Cos(a);
            m["Head Nod Down-Up"] = 0.5f;
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                m[side + " Shoulder Down-Up"] = 0.15f;
                m[side + " Arm Down-Up"] = Sample(ArmPhase, ArmDownUp, p);
                m[side + " Arm Front-Back"] = Sample(ArmPhase, ArmFrontBack, p);
                m[side + " Arm Twist In-Out"] = 0f;
                m[side + " Forearm Stretch"] = Sample(ArmPhase, Forearm, p);
                m[side + " Forearm Twist In-Out"] = Sample(ArmPhase, ForearmTwist, p);
                m[side + " Hand Down-Up"] = 0f;
                m[side + " Hand In-Out"] = 0f;

                m[side + " Upper Leg Front-Back"] = Sample(LegPhase, UpperLeg, p);
                m[side + " Upper Leg In-Out"] = Sample(LegPhase, LegInOut, p);
                m[side + " Upper Leg Twist In-Out"] = 0.2f;
                m[side + " Lower Leg Stretch"] = Sample(LegPhase, LowerLeg, p);
                m[side + " Foot Up-Down"] = Sample(LegPhase, Foot, p);
            }
            Fingers(m, 0.6f);
        }

        private static void Fingers(Dictionary<string, float> m, float stretch)
        {
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string hand in new[] { "LeftHand", "RightHand" })
                foreach (string f in fingers)
                {
                    m[hand + "." + f + ".1 Stretched"] = stretch;
                    m[hand + "." + f + ".2 Stretched"] = stretch;
                    m[hand + "." + f + ".3 Stretched"] = stretch;
                    m[hand + "." + f + ".Spread"] = 0f;
                }
        }

        // Smooth (cosine) interpolation through looping key values.
        private static float Sample(float[] phase, float[] value, float p)
        {
            for (int i = 0; i < phase.Length - 1; i++)
            {
                if (p > phase[i + 1]) continue;
                float t = Mathf.InverseLerp(phase[i], phase[i + 1], p);
                return Mathf.Lerp(value[i], value[i + 1], 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
            }
            return value[value.Length - 1];
        }

        // ---------------------------------------------------------------- clip writing

        private delegate void Pose(float p, Dictionary<string, float> muscles, out Vector3 bodyPos, out Quaternion bodyRot);

        private static AnimationClip Write(string path, string clipName, float period, Pose pose)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = clipName };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;

            var curves = new Dictionary<string, AnimationCurve>();
            var muscles = new Dictionary<string, float>();
            Quaternion previous = Quaternion.identity;
            for (int i = 0; i <= Samples; i++)
            {
                float p = i / (float)Samples;
                float time = p * period;
                muscles.Clear();
                pose(p % 1f, muscles, out Vector3 bodyPos, out Quaternion bodyRot);
                if (Quaternion.Dot(previous, bodyRot) < 0f) bodyRot = new Quaternion(-bodyRot.x, -bodyRot.y, -bodyRot.z, -bodyRot.w);
                previous = bodyRot;
                Key(curves, "RootT.x", time, bodyPos.x);
                Key(curves, "RootT.y", time, bodyPos.y);
                Key(curves, "RootT.z", time, bodyPos.z);
                Key(curves, "RootQ.x", time, bodyRot.x);
                Key(curves, "RootQ.y", time, bodyRot.y);
                Key(curves, "RootQ.z", time, bodyRot.z);
                Key(curves, "RootQ.w", time, bodyRot.w);
                foreach (KeyValuePair<string, float> muscle in muscles) Key(curves, muscle.Key, time, Mathf.Clamp(muscle.Value, -1f, 1f));
            }
            foreach (KeyValuePair<string, AnimationCurve> c in curves)
            {
                for (int k = 0; k < c.Value.length; k++) AnimationUtility.SetKeyLeftTangentMode(c.Value, k, AnimationUtility.TangentMode.ClampedAuto);
                for (int k = 0; k < c.Value.length; k++) AnimationUtility.SetKeyRightTangentMode(c.Value, k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), c.Key), c.Value);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = false;
            settings.loopBlendOrientation = true;  // bake root rotation into the pose
            settings.loopBlendPositionY = true;    // bake root height into the pose
            settings.loopBlendPositionXZ = true;   // bake root position into the pose
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void Key(Dictionary<string, AnimationCurve> curves, string property, float time, float value)
        {
            if (!curves.TryGetValue(property, out AnimationCurve curve))
            {
                curve = new AnimationCurve();
                curves.Add(property, curve);
            }
            curve.AddKey(time, value);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
