using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds "Intro Chase Exit Lock" (<see cref="IntroChaseExitLock"/>) under ///Manager/// in the open gameplay scene.
    /// Re-runnable: an existing one is kept. Saves the scene.
    /// </summary>
    public static class IntroChaseExitLockSetup
    {
        private const string ObjectName = "Intro Chase Exit Lock";

        [MenuItem("Tools/OpenworldCrime/Player/Set Up Intro Chase Exit Lock (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            if (EditorApplication.isPlaying) return "[IntroChaseExitLockSetup] Leave play mode first.";
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            if (managers == null) return "[IntroChaseExitLockSetup] ///Manager/// not found in the open scene (open Gangstar_Gameplay).";

            Transform existing = managers.transform.Find(ObjectName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(ObjectName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Intro Chase Exit Lock");
                go.transform.SetParent(managers.transform, false);
            }
            if (go.GetComponent<IntroChaseExitLock>() == null) Undo.AddComponent<IntroChaseExitLock>(go);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"[IntroChaseExitLockSetup] '{ObjectName}' under ///Manager/// ready; scene saved.";
        }
    }
}
