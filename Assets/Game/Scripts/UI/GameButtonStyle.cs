using UnityEngine;
using UnityEngine.UI;

// The game's button look, from Assets/UI-Buttons.png: pink buttons (green for paying cash), the dark panel, and Anton for
// the labels. The art lives in Resources/UIButtons (ButtonStyle asset, made by Game/UI Buttons Skin) so runtime-built HUDs
// (taxi, shop prompts) can use it too. Without the asset everything keeps its old flat colours.
public static class GameButtonStyle
{
    static GameButtonStyleAsset asset;
    static bool looked;

    static GameButtonStyleAsset Asset
    {
        get
        {
            if (!looked) { asset = Resources.Load<GameButtonStyleAsset>("UIButtons/ButtonStyle"); looked = true; }
            return asset;
        }
    }

    public static bool Available => Asset != null && Asset.pink != null;

    /// <summary>The car-door icon of the mobile controls sheet (null when not set up).</summary>
    public static Sprite EnterCar => Asset != null ? Asset.enterCar : null;
    public static Sprite WeaponPanel => Asset != null ? Asset.weaponPanel : null;
    public static Sprite ArrowLeft => Asset != null ? Asset.arrowLeft : null;
    public static Sprite ArrowRight => Asset != null ? Asset.arrowRight : null;
    public static Font Font => Asset != null ? Asset.font : null;

    /// <summary>Pink (or green) button background, white so its own colours show.</summary>
    public static bool Button(Image image, bool green = false)
    {
        var a = Asset;
        Sprite sprite = a == null ? null : green && a.green != null ? a.green : a.pink;
        if (image == null || sprite == null) return false;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        return true;
    }

    /// <summary>The dark gradient panel.</summary>
    public static bool Panel(Image image)
    {
        var a = Asset;
        if (image == null || a == null || a.panel == null) return false;
        image.sprite = a.panel;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        return true;
    }

    /// <summary>Anton with a dark outline, like the sheet's labels.</summary>
    public static void Label(Text text)
    {
        var a = Asset;
        if (text == null || a == null || a.font == null) return;
        text.font = a.font;
        text.fontStyle = FontStyle.Normal;
        if (text.GetComponent<Outline>() == null)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.05f, 0.2f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    /// <summary>Hover / pressed tint on a styled button.</summary>
    public static Color Tint(bool highlighted) => highlighted ? new Color(1f, 0.86f, 1f, 1f) : Color.white;
}
