using System.Collections.Generic;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Falling from a height (re-runnable; menu Tools > OpenworldCrime > Parachute > Set Up Free Fall):
    /// - the generated skydiving pose Assets/_Project/Animations/Player/Parachute/Parachute_Skydive.anim (spread-eagle, upright:
    ///   <see cref="PlayerFreeFall"/> pitches the body belly-down);
    /// - <see cref="PlayerFreeFall"/> on each playable character in the open scene (Jason, Mia), wired to their ragdoll, parachute
    ///   and the crash screams of their PlayerVehicleInteraction, and assigned to PlayerVehicleInteraction (jumping out of a
    ///   helicopter in the air) and PlayerParachute (letting go of the canopy);
    /// - the "RELEASE PARACHUTE [B]" key hint next to the open-parachute one, on the ParachuteButtonView.
    /// Run Parachute > Set Up Parachutes first.
    /// </summary>
    public static class FreeFallSetup
    {
        private const string ClipPath = "Assets/_Project/Animations/Player/Parachute/Parachute_Skydive.anim";
        private const string OpenHintName = "Parachute Prompt";
        private const string ReleaseHintName = "Parachute Release Prompt";

        [MenuItem("Tools/OpenworldCrime/Parachute/Set Up Free Fall")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[FreeFallSetup]\n");
            Scene scene = SceneManager.GetActiveScene();
            AnimationClip skydive = SwimAnimationBuilder.Write(ClipPath, "Parachute_Skydive", 1.6f, SkydivePose);
            AssetDatabase.SaveAssets();
            log.AppendLine("  clip: " + ClipPath);

            foreach (PlayableCharacter character in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (EditorUtility.IsPersistent(character) || character.gameObject.scene != scene) continue;
                GameObject go = character.gameObject;
                var fall = go.GetComponent<PlayerFreeFall>();
                if (fall == null) fall = Undo.AddComponent<PlayerFreeFall>(go);
                var interaction = go.GetComponent<PlayerVehicleInteraction>();
                AudioClip[] screams = new AudioClip[0];
                vRagdoll ragdoll = go.GetComponent<vRagdoll>();
                if (interaction != null)
                {
                    var iso = new SerializedObject(interaction);
                    SerializedProperty list = iso.FindProperty("crashScreams");
                    var found = new List<AudioClip>();
                    for (int i = 0; i < list.arraySize; i++)
                        if (list.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip clip) found.Add(clip);
                    screams = found.ToArray();
                    if (iso.FindProperty("playerRagdoll").objectReferenceValue is vRagdoll r) ragdoll = r;
                    iso.FindProperty("freeFall").objectReferenceValue = fall;
                    iso.ApplyModifiedProperties();
                }
                fall.EditorSetup(ragdoll, skydive, screams);
                EditorUtility.SetDirty(fall);
                var parachute = go.GetComponent<PlayerParachute>();
                if (parachute != null)
                {
                    parachute.EditorSetFreeFall(fall);
                    EditorUtility.SetDirty(parachute);
                }
                log.AppendLine($"  {go.name}: ragdoll {(ragdoll != null)}, screams {screams.Length}, parachute {(parachute != null)}");
            }

            log.AppendLine("  " + BuildReleaseHint(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        private static string BuildReleaseHint(Scene scene)
        {
            GameObject gameplayUi = ParachuteSetup.FindRoot(scene, "Gameplay UI");
            Transform open = gameplayUi != null ? gameplayUi.transform.Find(OpenHintName) : null;
            var view = gameplayUi != null ? gameplayUi.GetComponentInChildren<ParachuteButtonView>(true) : null;
            if (open == null || view == null) return "WARNING: no '" + OpenHintName + "' / ParachuteButtonView: no release hint";
            Transform old = gameplayUi.transform.Find(ReleaseHintName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            GameObject hint = Object.Instantiate(open.gameObject, open.parent);
            Undo.RegisterCreatedObjectUndo(hint, ReleaseHintName);
            hint.name = ReleaseHintName;
            hint.transform.SetSiblingIndex(open.GetSiblingIndex() + 1);
            Text text = hint.GetComponentInChildren<Text>(true);
            if (text != null) text.text = "RELEASE PARACHUTE  [B]";
            Image image = hint.GetComponent<Image>();
            if (image != null) image.color = new Color(1f, 0.55f, 0.35f, 1f);
            hint.SetActive(false);
            var so = new SerializedObject(view);
            so.FindProperty("releaseKeyHint").objectReferenceValue = hint;
            so.ApplyModifiedProperties();
            return "UI: '" + ReleaseHintName + "' on " + view.name;
        }

        // Stable belly-down skydiving (in the body's own frame; PlayerFreeFall pitches it ~78 deg): back arched, head up, arms
        // spread out and up beside the head with the elbows bent, legs apart with the knees bent so the shins point up behind.
        // A slight flutter. Muscle conventions as in SwimAnimationBuilder.
        private static void SkydivePose(float p, Dictionary<string, float> m, out Vector3 bodyPos, out Quaternion bodyRot)
        {
            float a = p * Mathf.PI * 2f;
            float flutter = Mathf.Sin(a * 2f);
            bodyPos = new Vector3(0f, 0.96f, 0f);
            bodyRot = Quaternion.identity;
            m["Spine Front-Back"] = 0.25f;
            m["Chest Front-Back"] = 0.2f;
            m["Neck Nod Down-Up"] = 0.45f;
            m["Head Nod Down-Up"] = 0.35f;
            m["Spine Left-Right"] = 0.03f * Mathf.Sin(a);
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                float f = flutter * (s == 0 ? 1f : -1f);
                m[side + " Shoulder Down-Up"] = 0.25f;
                m[side + " Arm Down-Up"] = 0.55f + 0.03f * f;
                m[side + " Arm Front-Back"] = 0.25f;
                m[side + " Arm Twist In-Out"] = 0.1f;
                m[side + " Forearm Stretch"] = 0.35f + 0.04f * f;
                m[side + " Forearm Twist In-Out"] = 0f;
                m[side + " Hand Down-Up"] = 0f;
                m[side + " Hand In-Out"] = 0f;

                m[side + " Upper Leg Front-Back"] = 0.62f + 0.02f * f;
                m[side + " Upper Leg In-Out"] = 0.45f;
                m[side + " Upper Leg Twist In-Out"] = 0f;
                m[side + " Lower Leg Stretch"] = 0.45f + 0.04f * f;
                m[side + " Foot Up-Down"] = -0.3f;
            }
            SwimAnimationBuilder.Fingers(m, 0.6f); // open hands
        }
    }
}
