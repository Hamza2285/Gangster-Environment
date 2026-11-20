using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Showroom Sign Setup: the "Car Showroom" logo (Assets/Car show room logo.fbx) stands on the blue awning over the
// showroom door at the Parking Plaza, facing the street, below the blue P sign. The map icon sits on the door (the
// entrance's MapIcon) with the neon car icon. Safe to run again: the sign is rebuilt.
public static class ShowroomSignSetup
{
    const string LogoPath = "Assets/Car show room logo.fbx";
    const string SignName = "Car Showroom Sign";
    const string EntranceName = "Car Showroom Entrance";
    // From the door: back toward the building (the door stands in front of the awning) and up onto the awning roof, just in
    // front of the balcony slabs.
    const float OutFromDoor = -2.6f;
    const float UpFromDoor = 3.2f;
    const float Scale = 0.95f;

    [MenuItem("Game/Showroom Sign Setup")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var logo = AssetDatabase.LoadAssetAtPath<GameObject>(LogoPath);
        var entrance = GameObject.Find(EntranceName);
        if (logo == null || entrance == null) return "[ShowroomSignSetup] missing " + (logo == null ? LogoPath : EntranceName);
        Transform door = entrance.transform;

        var old = GameObject.Find(SignName);
        if (old != null) Object.DestroyImmediate(old);

        var sign = (GameObject)PrefabUtility.InstantiatePrefab(logo);
        sign.name = SignName;
        if (door.parent != null) sign.transform.SetParent(door.parent, true);
        Vector3 street = Vector3.ProjectOnPlane(door.forward, Vector3.up).normalized;
        sign.transform.position = door.position + street * OutFromDoor + Vector3.up * UpFromDoor;
        // The model's face looks along its +Z (after its import rotation): turn that toward the street.
        sign.transform.rotation = Quaternion.LookRotation(street, Vector3.up) * logo.transform.rotation;
        sign.transform.localScale = logo.transform.localScale * Scale;
        foreach (var r in sign.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
        }

        // Map icon exactly on the door, with the neon car icon.
        var icon = entrance.GetComponent<MapIcon>();
        var car = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/MapIcons/MapIcon_Car.png");
        if (icon != null && car != null)
        {
            icon.icon = car;
            icon.tint = Color.white;
            icon.size = 40f;
            EditorUtility.SetDirty(icon);
        }

        EditorSceneManager.MarkSceneDirty(entrance.scene);
        return $"[ShowroomSignSetup] sign at {sign.transform.position}, map icon at {door.position}";
    }
}
