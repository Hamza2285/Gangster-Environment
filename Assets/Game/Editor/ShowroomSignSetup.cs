using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Showroom Sign Setup: the car showroom's way in from the city is the big garage door of the tall cream building south
// of the Parking Plaza block (ShowroomSetup.DefaultDoor). This moves the existing entrance there (trigger, exit point and
// map icon, so the minimap's car icon sits on the door) and mounts the "Car Showroom" logo (Assets/Car show room logo.fbx)
// over the door's old "PARKING residents only" sign, facing the street. Safe to run again: the sign is rebuilt.
public static class ShowroomSignSetup
{
    const string LogoPath = "Assets/Car show room logo.fbx";
    const string SignName = "Car Showroom Sign";
    const string EntranceName = "Car Showroom Entrance";
    // Over the parking sign in the door's frame: centre of the door, 5 m up, just proud of the facade.
    static readonly Vector3 SignPosition = new Vector3(745f, 32.55f, -420.8f);
    const float Scale = 1.05f; // ~8.4 m wide: fills the 9 m door frame

    [MenuItem("Game/Showroom Sign Setup")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var logo = AssetDatabase.LoadAssetAtPath<GameObject>(LogoPath);
        var entrance = GameObject.Find(EntranceName);
        if (logo == null || entrance == null) return "[ShowroomSignSetup] missing " + (logo == null ? LogoPath : EntranceName);

        // The entrance on the door: trigger 1.2 m up as ShowroomSetup makes it, the way out on the pavement in front.
        Vector3 facing = ShowroomSetup.DefaultFacing;
        facing.y = 0f;
        facing.Normalize();
        Vector3 door = ShowroomSetup.DefaultDoor;
        entrance.transform.SetPositionAndRotation(door + Vector3.up * 1.2f, Quaternion.LookRotation(facing));
        var interact = entrance.GetComponent<ShowroomEntrance>();
        if (interact != null && interact.exitPoint != null)
        {
            Vector3 exitAt = door + facing * 1.6f;
            if (Physics.Raycast(exitAt + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) exitAt.y = hit.point.y;
            interact.exitPoint.SetPositionAndRotation(exitAt, Quaternion.LookRotation(facing));
            EditorUtility.SetDirty(interact.exitPoint);
        }
        EditorUtility.SetDirty(entrance.transform);

        var old = GameObject.Find(SignName);
        if (old != null) Object.DestroyImmediate(old);
        var sign = (GameObject)PrefabUtility.InstantiatePrefab(logo);
        sign.name = SignName;
        if (entrance.transform.parent != null) sign.transform.SetParent(entrance.transform.parent, true);
        sign.transform.position = SignPosition;
        // The model's face looks along its +Z (after its import rotation): turn that toward the street.
        sign.transform.rotation = Quaternion.LookRotation(facing, Vector3.up) * logo.transform.rotation;
        sign.transform.localScale = logo.transform.localScale * Scale;
        foreach (var r in sign.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
        }

        // Map icon on the door, with the neon car icon.
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
        return $"[ShowroomSignSetup] entrance at {entrance.transform.position}, sign at {sign.transform.position}";
    }
}
