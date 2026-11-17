using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Game/Add Shop Staff: puts employees in the clothing store (a cashier at the checkout and two assistants on the floor) and a
// cashier behind the gun shop counter, the same kind of figures as the car showroom's staff. Safe to run again.
public static class ShopStaffSetup
{
    const string ClothingScene = "Assets/Scenes/Clothing Store Interior.unity";
    const string GunShopScene = "Assets/_Project/Scenes/GunShop_Interior.unity";

    [MenuItem("Game/Add Shop Staff")]
    static void Run()
    {
        Debug.Log(SetUp());
    }

    public static string SetUp()
    {
        string a = Fill(ClothingScene, "Clothing Store Interior", holder =>
        {
            // Behind the checkout, facing the shop floor.
            ShowroomSetup.Staff(holder, "Cashier", "Girl_44", ShowroomStaff.Duty.Stand, 90f, Local(holder, new Vector3(-9.0f, 0f, 0.3f)));
            // By the racks near the door, and at the shelving on the right wall.
            ShowroomSetup.Staff(holder, "Assistant Racks", "Man_33", ShowroomStaff.Duty.Stand, 225f, Local(holder, new Vector3(4.5f, 0f, 2.0f)));
            ShowroomSetup.Staff(holder, "Assistant Shelves", "Girl_66", ShowroomStaff.Duty.Stand, 270f, Local(holder, new Vector3(7.8f, 0f, -2.5f)));
        });
        string b = Fill(GunShopScene, "GunShopInterior", holder =>
        {
            // One cashier behind the glass counter, across from where the customer buys.
            ShowroomSetup.Staff(holder, "Cashier", "Man_77", ShowroomStaff.Duty.Stand, 90f, Local(holder, new Vector3(2.7f, 0f, 9.25f)));
        });
        return "[ShopStaffSetup] " + a + "; " + b;
    }

    // Shop-root space, put on the floor.
    static Vector3 Local(Transform holder, Vector3 local)
    {
        return ShowroomSetup.Ground(holder, local);
    }

    static string Fill(string path, string rootName, System.Action<Transform> add)
    {
        var active = SceneManager.GetActiveScene();
        var existing = SceneManager.GetSceneByPath(path);
        bool wasOpen = existing.isLoaded;
        var scene = wasOpen ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        GameObject root = null;
        foreach (var g in scene.GetRootGameObjects()) if (g.name == rootName) root = g;
        if (root == null) { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); return path + ": no '" + rootName + "'"; }

        var old = root.transform.Find("Staff");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var holder = new GameObject("Staff").transform;
        holder.SetParent(root.transform, false);
        Physics.SyncTransforms();
        add(holder);

        int count = holder.childCount;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        SceneManager.SetActiveScene(active);
        return System.IO.Path.GetFileNameWithoutExtension(path) + ": " + count + " staff";
    }
}
