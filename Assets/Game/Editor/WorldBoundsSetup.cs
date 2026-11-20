using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/World Bounds Setup: puts the "World Bounds" object (invisible walls at the map's edge, death in the sea or below the
// world) at the top of the open scene. Safe to run again: an existing one is kept and only its settings are left as they are.
public static class WorldBoundsSetup
{
    const string RootName = "World Bounds";

    [MenuItem("Game/World Bounds Setup")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) root = go;
        bool made = root == null;
        if (made) root = new GameObject(RootName);
        if (root.GetComponent<WorldBounds>() == null) root.AddComponent<WorldBounds>();
        root.transform.SetSiblingIndex(2);
        EditorSceneManager.MarkSceneDirty(scene);
        return "[WorldBoundsSetup] " + (made ? "added" : "kept") + " " + RootName;
    }
}
