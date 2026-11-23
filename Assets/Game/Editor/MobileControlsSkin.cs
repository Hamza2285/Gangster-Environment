using System.Collections.Generic;
using OpenworldCrime.Controls;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Game/Mobile Controls Skin: the phone controls drawn from Assets/Game_Play_buttons.png and laid out like
// Assets/Game_Play_buttons mock.png (both 1920 x 1080, the Touch Controls canvas' reference size).
// - The sheet is cut into named sprites (positions measured on the sheet).
// - Ghost's touch buttons (Touch Controls) get the icons instead of their text labels and move to the mock's places.
// - Buttons the mock has that the game lacked are added as copies of the Jump button: Sprint (M_Sprint, held),
//   Crouch (M_Crouch) and Grenade (M_Grenade: Invector's throw managers aim with it, Fire throws).
// The mock's top-right HUD row (settings, chat, music, map pin, weapon panel) is not placed: it would sit on the existing
// day / night buttons and weapon box, and most of those buttons have nothing behind them yet. Safe to run again.
public static class MobileControlsSkin
{
    // The final sheet: the same layout as the first one (Assets/Game_Play_buttons.png) plus the car controls (steering arrows,
    // brake and gas pedals, handbrake), the jump icon and the stars.
    const string SheetPath = "Assets/Game_Play buttons final_2.png";
    const float SheetHeight = 1080f;

    // Name -> rect on the sheet (x0, y0, x1, y1 from the top-left, pixels).
    static readonly Dictionary<string, RectInt> Pieces = new Dictionary<string, RectInt>
    {
        { "Joystick", Box(32, 760, 340, 1056) }, { "JoystickKnob", Box(1216, 704, 1340, 828) },
        { "Fire", Box(1636, 796, 1900, 1064) }, { "Bullet", Box(1184, 504, 1344, 664) },
        { "Sprint", Box(1216, 852, 1356, 992) }, { "Grenade", Box(1388, 856, 1524, 992) },
        // The door with the arrow pointing in is "get in", the one pointing out is "get out".
        { "Reload", Box(1388, 696, 1524, 832) }, { "EnterCar", Box(1388, 536, 1528, 672) },
        { "ExitCar", Box(1388, 392, 1524, 528) }, { "Aim", Box(1544, 392, 1684, 528) },
        { "SteerLeft", Box(37, 51, 313, 327) }, { "SteerRight", Box(393, 51, 669, 327) },
        { "JumpIcon", Box(991, 508, 1145, 662) }, { "Handbrake", Box(1188, 334, 1342, 488) },
        { "Brake", Box(820, 925, 960, 1041) }, { "Gas", Box(1001, 772, 1171, 1042) },
        { "StarYellow", Box(1315, 100, 1370, 148) }, { "StarOutline", Box(1255, 100, 1312, 148) },
        { "Punch", Box(1544, 536, 1684, 676) }, { "Stand", Box(1704, 388, 1844, 532) },
        { "Crouch", Box(1708, 552, 1844, 688) }, { "Weapon", Box(966, 178, 1086, 294) },
        { "Settings", Box(1796, 24, 1896, 124) }, { "Chat", Box(1564, 144, 1640, 220) },
        { "MapPin", Box(1648, 144, 1724, 220) }, { "ExitDoor", Box(1736, 144, 1812, 220) },
        { "Music", Box(1820, 144, 1896, 224) }, { "WeaponPanel", Box(1476, 232, 1896, 352) },
        { "ArrowLeft", Box(1376, 268, 1412, 316) }, { "ArrowRight", Box(1424, 268, 1460, 316) },
        { "AmmoPill", Box(958, 294, 1092, 340) }, { "TopPanel", Box(904, 28, 1240, 140) },
    };

    static RectInt Box(int x0, int y0, int x1, int y1) => new RectInt(x0, y0, x1 - x0, y1 - y0);

    // Button -> (sprite, centre on the mock from the top-left, size).
    struct Place { public string sprite; public Vector2 centre; public float size; public Place(string s, float x, float y, float d) { sprite = s; centre = new Vector2(x, y); size = d; } }

    // Positions from the user's updated layout (Assets/Game_Play_updated buttons.png: the same art, arranged as it should sit).
    static readonly Dictionary<string, Place> Layout = new Dictionary<string, Place>
    {
        { "Fire", new Place("Fire", 1768, 928, 260) },
        { "FireLeft", new Place("Bullet", 1265, 585, 152) },
        { "Vehicle", new Place("EnterCar", 1455, 460, 132) },
        { "Aim", new Place("Aim", 1613, 460, 130) },
        { "Jump", new Place("JumpIcon", 1775, 460, 136) },
        { "Exit", new Place("ExitCar", 1457, 605, 132) },
        { "Punch", new Place("Punch", 1612, 607, 132) },
        { "Crouch", new Place("Crouch", 1777, 618, 132) },
        { "Reload", new Place("Reload", 1455, 765, 132) },
        { "Sprint", new Place("Sprint", 1285, 922, 132) },
        { "Grenade", new Place("Grenade", 1455, 922, 132) },
        // Under the minimap on phones (the layout's spot for it, top centre, is where the weapon selector panel shows).
        { "WeaponSwitch", new Place("Weapon", 100, 445, 110) },
    };

    static readonly (string name, string input, bool hold)[] Added =
    {
        ("Sprint", "M_Sprint", true), ("Crouch", "M_Crouch", false), ("Grenade", "M_Grenade", false),
    };

    [MenuItem("Game/Mobile Controls Skin")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var log = new System.Text.StringBuilder("[MobileControlsSkin]\n");
        var sprites = SliceSheet(log);
        if (sprites.Count == 0) return log.Append("  no sprites").ToString();

        var touch = FindRoot("Touch Controls");
        if (touch == null) return log.Append("  no Touch Controls in the open scene").ToString();
        var onFoot = touch.transform.Find("OnFootControls");
        var vehicle = touch.transform.Find("VehicleControls");
        var jump = onFoot != null ? onFoot.Find("Jump") : null;

        EnsureInputName("M_Grenade", log);
        foreach (var (name, input, hold) in Added)
        {
            if (onFoot == null || jump == null || onFoot.Find(name) != null) continue;
            var copy = Object.Instantiate(jump.gameObject, onFoot);
            copy.name = name;
            var so = new SerializedObject(copy.GetComponent<VirtualButton>());
            so.FindProperty("buttonName").stringValue = input;
            so.FindProperty("toggle").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  added {name} ({input}{(hold ? ", held" : "")})");
        }
        WireGrenade(log);

        // SPRINT holds the stick's sprint (a plain virtual button only toggled Invector's sprint once, and missed from a standstill).
        var sprint = onFoot != null ? onFoot.Find("Sprint") : null;
        if (sprint != null && sprint.GetComponent<SprintHoldButton>() == null)
        {
            var plain = sprint.GetComponent<VirtualButton>();
            if (plain != null) Object.DestroyImmediate(plain);
            sprint.gameObject.AddComponent<SprintHoldButton>();
            log.AppendLine("  Sprint: hold-to-sprint");
        }

        foreach (var pair in Layout)
        {
            Transform button = (onFoot != null ? onFoot.Find(pair.Key) : null) ?? (vehicle != null ? vehicle.Find(pair.Key) : null);
            if (button == null) { log.AppendLine("  missing button " + pair.Key); continue; }
            Skin(button, sprites[pair.Value.sprite], pair.Value);
        }

        // The stick: base and knob from the sheet, at the mock's place (it is anchored to the stick area's bottom-left corner,
        // which is the screen's).
        var stick = onFoot != null ? onFoot.Find("MoveStick") : null;
        if (stick != null)
        {
            var background = stick.Find("Background") as RectTransform;
            if (background != null)
            {
                SetImage(background.gameObject, sprites["Joystick"]);
                background.anchoredPosition = new Vector2(187f, SheetHeight - 900f);
                background.sizeDelta = new Vector2(290f, 290f * 296f / 308f);
                var knob = background.Find("Handle") as RectTransform;
                if (knob != null)
                {
                    SetImage(knob.gameObject, sprites["JoystickKnob"]);
                    knob.sizeDelta = new Vector2(118f, 118f);
                }
            }
            // The sprint button replaces the "SPRINT" hint above the stick.
            var marker = stick.Find("SprintMarker");
            if (marker != null) marker.gameObject.SetActive(false);
        }

        HideEquipSlotsOnPhones(log);
        SkinCarControls(sprites, log);
        SkinWantedStars(sprites, log);

        // The contextual get-in button (MobileGameButtons) shows the same door icon.
        var style = AssetDatabase.LoadAssetAtPath<GameButtonStyleAsset>("Assets/Game/Resources/UIButtons/ButtonStyle.asset");
        if (style != null)
        {
            style.enterCar = sprites["EnterCar"];
            style.weaponPanel = sprites["WeaponPanel"];
            style.arrowLeft = sprites["ArrowLeft"];
            style.arrowRight = sprites["ArrowRight"];
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
        }

        EditorSceneManager.MarkSceneDirty(touch.scene);
        return log.ToString();
    }

    static void Skin(Transform button, Sprite sprite, Place place)
    {
        var rect = (RectTransform)button;
        // Re-anchored to the screen corner nearest the mock position, so it keeps its corner on any aspect ratio.
        bool right = place.centre.x > 960f, top = false;
        var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(right ? place.centre.x - 1920f : place.centre.x, SheetHeight - place.centre.y);
        rect.sizeDelta = new Vector2(place.size, place.size * sprite.rect.height / sprite.rect.width);
        SetImage(button.gameObject, sprite);
        foreach (var text in button.GetComponentsInChildren<Text>(true)) text.gameObject.SetActive(false);
        EditorUtility.SetDirty(button.gameObject);
    }

    static void SetImage(GameObject go, Sprite sprite)
    {
        var image = go.GetComponent<Image>();
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = new Color(1f, 1f, 1f, 0.92f);
        EditorUtility.SetDirty(image);
    }

    static Dictionary<string, Sprite> SliceSheet(System.Text.StringBuilder log)
    {
        var result = new Dictionary<string, Sprite>();
        var importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
        if (importer == null) { log.AppendLine("  missing " + SheetPath); return result; }
        var metas = new List<SpriteMetaData>();
        foreach (var pair in Pieces)
        {
            RectInt r = pair.Value;
            metas.Add(new SpriteMetaData
            {
                name = "MC_" + pair.Key,
                rect = new Rect(r.x, SheetHeight - r.y - r.height, r.width, r.height),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            });
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
#pragma warning disable 618
        importer.spritesheet = metas.ToArray();
#pragma warning restore 618
        importer.SaveAndReimport();
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(SheetPath))
            if (o is Sprite s && s.name.StartsWith("MC_")) result[s.name.Substring(3)] = s;
        log.AppendLine($"  sliced {result.Count} sprites from the sheet");
        return result;
    }

    // Invector's GenericInput asks the legacy Input Manager first, so a touch button's name has to exist there.
    static void EnsureInputName(string name, System.Text.StringBuilder log)
    {
        var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
        SerializedProperty axes = manager.FindProperty("m_Axes");
        for (int i = 0; i < axes.arraySize; i++)
            if (axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue == name) return;
        axes.arraySize++;
        SerializedProperty axis = axes.GetArrayElementAtIndex(axes.arraySize - 1);
        axis.FindPropertyRelative("m_Name").stringValue = name;
        foreach (string field in new[] { "descriptiveName", "descriptiveNegativeName", "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton" })
            axis.FindPropertyRelative(field).stringValue = string.Empty;
        axis.FindPropertyRelative("gravity").floatValue = 1000f;
        axis.FindPropertyRelative("dead").floatValue = 0.001f;
        axis.FindPropertyRelative("sensitivity").floatValue = 1000f;
        manager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        log.AppendLine("  Input Manager: added " + name);
    }

    // Every throw manager on the playable characters: the grenade button aims a throw, Fire lets it go.
    static void WireGrenade(System.Text.StringBuilder log)
    {
        int n = 0;
        foreach (var manager in Resources.FindObjectsOfTypeAll<Invector.Throw.vThrowManagerBase>())
        {
            if (EditorUtility.IsPersistent(manager)) continue;
            var so = new SerializedObject(manager);
            var aim = so.FindProperty("aimThrowInput");
            var shot = so.FindProperty("throwInput");
            if (aim == null || shot == null) continue;
            aim.FindPropertyRelative("mobile").stringValue = "M_Grenade";
            shot.FindPropertyRelative("mobile").stringValue = "M_Shoot";
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        log.AppendLine($"  grenade wired on {n} throw manager(s)");
    }

    // Invector's equipment slot boxes (with their keyboard d-pad hints) sat on the stick: keyboard-only, hidden on phones.
    static void HideEquipSlotsOnPhones(System.Text.StringBuilder log)
    {
        var manager = Object.FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
        if (manager == null) return;
        var so = new SerializedObject(manager);
        var list = so.FindProperty("keyboardOnlyObjects");
        var have = new HashSet<Object>();
        for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
        int added = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (EditorUtility.IsPersistent(t) || t.name != "EquipmentDisplayWindow" || have.Contains(t.gameObject)) continue;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = t.gameObject;
            added++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine($"  equipment slot boxes hidden on phones: {added} added");
    }

    // RCC's driving buttons keep their places and touch areas; their icons (the child image of the same name) come from the
    // sheet: steering arrows, brake pedal, gas pedal and handbrake lever. (The sheet has no nitro icon, so NOS keeps RCC's.)
    static void SkinCarControls(Dictionary<string, Sprite> sprites, System.Text.StringBuilder log)
    {
        var rcc = FindRoot("RCC Mobile Controls");
        var buttons = rcc != null ? rcc.transform.Find("Controller Buttons") : null;
        if (buttons == null) { log.AppendLine("  no RCC Controller Buttons"); return; }
        var icons = new (string button, string sprite, float height)[]
        {
            ("Left", "SteerLeft", 200f), ("Right", "SteerRight", 200f),
            ("Brake", "Brake", 150f), ("Gas", "Gas", 250f), ("Handbrake", "Handbrake", 160f),
        };
        int n = 0;
        foreach (var (name, spriteName, height) in icons)
        {
            var button = buttons.Find(name) as RectTransform;
            var icon = button != null ? button.Find(name) as RectTransform : null;
            if (icon == null) { log.AppendLine("  missing RCC " + name); continue; }
            Sprite sprite = sprites[spriteName];
            SetImage(icon.gameObject, sprite);
            icon.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
            // The tall gas pedal gets a touch area to match.
            if (button.sizeDelta.y < height + 30f) button.sizeDelta = new Vector2(button.sizeDelta.x, height + 30f);
            EditorUtility.SetDirty(button.gameObject);
            n++;
        }
        log.AppendLine($"  car controls: {n} icons from the sheet");
    }

    // Wanted level: the sheet's yellow star for earned stars, its outline star for the rest.
    static void SkinWantedStars(Dictionary<string, Sprite> sprites, System.Text.StringBuilder log)
    {
        var hud = Object.FindFirstObjectByType<OpenworldCrime.Police.WantedHudView>(FindObjectsInactive.Include);
        if (hud == null) { log.AppendLine("  no wanted HUD"); return; }
        var so = new SerializedObject(hud);
        so.FindProperty("activeStarSprite").objectReferenceValue = sprites["StarYellow"];
        so.FindProperty("inactiveStarSprite").objectReferenceValue = sprites["StarOutline"];
        so.FindProperty("activeStarColour").colorValue = Color.white;
        so.FindProperty("inactiveStarColour").colorValue = Color.white;
        var stars = so.FindProperty("stars");
        for (int i = 0; i < stars.arraySize; i++)
        {
            var image = stars.GetArrayElementAtIndex(i).objectReferenceValue as Image;
            if (image == null) continue;
            image.sprite = sprites["StarOutline"];
            image.preserveAspect = true;
            EditorUtility.SetDirty(image);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  wanted stars: yellow / outline from the sheet");
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }
}
