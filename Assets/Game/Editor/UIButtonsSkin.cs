using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Game/UI Buttons Skin: the button look of Assets/UI-Buttons.png across the HUD. Its buttons have their words painted in, so
// plain pink / green buttons and the dark panel were cut from it (Resources/UIButtons: the end caps and a text-free slice of a
// real button, so any text fits) and are used as 9-sliced backgrounds with Anton labels:
// - Resources/UIButtons/ButtonStyle.asset for runtime-built HUDs (taxi trip choices, shop prompts: GameButtonStyle);
// - the gameplay scene's interact / vehicle prompts;
// - the club HUD buttons (Throw Cash green, the others pink) in the club scene.
// Safe to run again.
public static class UIButtonsSkin
{
    const string Folder = "Assets/Game/Resources/UIButtons/";
    const string ClubScene = "Assets/_Project/Scenes/Club_Interior.unity";

    [MenuItem("Game/UI Buttons Skin")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var log = new System.Text.StringBuilder("[UIButtonsSkin]\n");
        var pink = ImportSprite(Folder + "Btn_Pink.png", new Vector4(40f, 12f, 40f, 12f));
        var green = ImportSprite(Folder + "Btn_Green.png", new Vector4(40f, 12f, 40f, 12f));
        var panel = ImportSprite(Folder + "Panel_Dark.png", new Vector4(10f, 10f, 10f, 10f));
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ANTON.TTF");

        string assetPath = Folder + "ButtonStyle.asset";
        var style = AssetDatabase.LoadAssetAtPath<GameButtonStyleAsset>(assetPath);
        if (style == null)
        {
            style = ScriptableObject.CreateInstance<GameButtonStyleAsset>();
            AssetDatabase.CreateAsset(style, assetPath);
        }
        style.pink = pink;
        style.green = green;
        style.panel = panel;
        style.font = font;
        EditorUtility.SetDirty(style);
        AssetDatabase.SaveAssets();
        log.AppendLine("  style asset: " + assetPath);

        // Gameplay scene prompts.
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name != "InteractPrompt" && image.name != "VehiclePrompt") continue;
                Style(image, pink, font, false);
                // Just above the phone controls' bottom row (grenade, reload, crouch).
                if (image.name == "VehiclePrompt") ((RectTransform)image.transform).anchoredPosition = new Vector2(0f, 215f);
                log.AppendLine("  " + image.name);
            }
        // The weapon box becomes the weapon selector (panel, big picture, name, arrows): WeaponSelectorPanel lays it out at runtime.
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == "WeaponInfo" && t.GetComponent<WeaponSelectorPanel>() == null)
                {
                    t.gameObject.AddComponent<WeaponSelectorPanel>();
                    log.AppendLine("  weapon selector on " + t.name);
                }
        // No day / night buttons in the gameplay HUD (the city lighting builder's test buttons).
        foreach (var root in scene.GetRootGameObjects())
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name != "Day" && button.name != "Night") continue;
                Transform holder = button.transform.parent;
                bool onlyThese = holder != null && holder.childCount <= 2 && holder.GetComponent<Canvas>() == null;
                var target = onlyThese ? holder.gameObject : button.gameObject;
                if (target.activeSelf) { target.SetActive(false); log.AppendLine("  hidden " + target.name); }
            }
        EditorSceneManager.MarkSceneDirty(scene);

        // Club HUD.
        var club = EditorSceneManager.GetSceneByPath(ClubScene);
        bool opened = !club.isLoaded;
        if (opened) club = EditorSceneManager.OpenScene(ClubScene, OpenSceneMode.Additive);
        int n = 0;
        foreach (var root in club.GetRootGameObjects())
            foreach (var hud in root.GetComponentsInChildren<OpenworldCrime.Club.ClubHudView>(true))
                foreach (var button in hud.GetComponentsInChildren<Button>(true))
                {
                    var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
                    bool cash = button.name.ToLower().Contains("throw");
                    Style(image, cash ? green : pink, font, true);
                    log.AppendLine($"  club {button.name} -> {(cash ? "green" : "pink")}");
                    n++;
                }
        EditorSceneManager.MarkSceneDirty(club);
        EditorSceneManager.SaveScene(club);
        if (opened) EditorSceneManager.CloseScene(club, true);
        return log.ToString();
    }

    static void Style(Image image, Sprite sprite, Font font, bool button)
    {
        if (image == null || sprite == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        var selectable = image.GetComponent<Selectable>();
        if (selectable != null && selectable.transition == Selectable.Transition.ColorTint)
        {
            var colours = selectable.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1f, 0.9f, 1f, 1f);
            colours.pressedColor = new Color(0.85f, 0.75f, 0.85f, 1f);
            colours.selectedColor = Color.white;
            selectable.colors = colours;
        }
        foreach (var text in image.GetComponentsInChildren<Text>(true))
        {
            if (font != null) { text.font = font; text.fontStyle = FontStyle.Normal; }
            if (text.GetComponent<Outline>() == null)
            {
                var outline = text.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.25f, 0.05f, 0.2f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            EditorUtility.SetDirty(text);
        }
        EditorUtility.SetDirty(image);
    }

    static Sprite ImportSprite(string path, Vector4 border)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = border;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
