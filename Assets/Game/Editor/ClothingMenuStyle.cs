using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Clothing Menu Style: the clothing store screen uses Assets/Cloth-Panel.png as its side panel (imported as a sliced
// sprite, so its thin border stays sharp) with Anton for titles / prices / buttons and Bebas Kai for the rest. Applied to
// the menu in the Clothing Store Interior scene; ClothingStoreSetup applies it to a freshly built store too.
public static class ClothingMenuStyle
{
    const string PanelPath = "Assets/Cloth-Panel.png";
    const string HeadingFontPath = "Assets/ANTON.TTF";
    const string TextFontPath = "Assets/BEBASKAI.TTF";
    const string InteriorScene = "Assets/Scenes/Clothing Store Interior.unity";

    [MenuItem("Game/Clothing Menu Style")]
    public static void Menu() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var scene = EditorSceneManager.GetSceneByPath(InteriorScene);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(InteriorScene, OpenSceneMode.Additive);
        int n = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var menu in go.GetComponentsInChildren<ClothingStoreMenu>(true)) { Apply(menu); n++; }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
        return "[ClothingMenuStyle] styled " + n + " clothing menu(s)";
    }

    public static void Apply(ClothingStoreMenu menu)
    {
        menu.panelSprite = PanelSprite();
        menu.headingFont = AssetDatabase.LoadAssetAtPath<Font>(HeadingFontPath);
        menu.textFont = AssetDatabase.LoadAssetAtPath<Font>(TextFontPath);
        EditorUtility.SetDirty(menu);
    }

    static Sprite PanelSprite()
    {
        var importer = AssetImporter.GetAtPath(PanelPath) as TextureImporter;
        if (importer == null) return null;
        var border = new Vector4(6f, 6f, 6f, 6f);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
    }
}
