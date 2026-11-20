using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.UI;

// The HUD's weapon box (HUD/WeaponInfo) in the look of the user's weapon selector: a dark panel with the weapon picture large on
// the left, its name bottom-right, and pink arrows either side that switch to the previous / next weapon the player has.
// UIManager still fills in the picture, name and ammo; this only lays the box out and handles the arrows.
[DisallowMultipleComponent]
public class WeaponSelectorPanel : MonoBehaviour
{
    public Vector2 size = new Vector2(400f, 112f);
    public Vector2 topRightOffset = new Vector2(-30f, -36f);
    public Color panelTint = new Color(0.36f, 0.36f, 0.38f, 0.95f);

    void Awake()
    {
        var rect = (RectTransform)transform;
        rect.sizeDelta = size;
        // Up in the corner the day / night buttons used to take, clear of the cash line below.
        rect.anchoredPosition = topRightOffset;
        var panel = GetComponent<Image>();
        if (panel != null && GameButtonStyle.WeaponPanel != null)
        {
            panel.sprite = GameButtonStyle.WeaponPanel;
            panel.type = Image.Type.Simple;
            panel.color = panelTint;
        }

        var icon = transform.Find("WeaponIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.anchoredPosition = new Vector2(42f, 6f);
            icon.sizeDelta = new Vector2(250f, 92f);
            var image = icon.GetComponent<Image>();
            if (image != null) image.preserveAspect = true;
        }
        Font font = GameButtonStyle.Font;
        var name = transform.Find("WeaponName") as RectTransform;
        if (name != null)
        {
            name.anchorMin = new Vector2(0f, 0f);
            name.anchorMax = new Vector2(1f, 0.5f);
            name.offsetMin = new Vector2(0f, 6f);
            name.offsetMax = new Vector2(-46f, 0f);
            var text = name.GetComponent<Text>();
            if (text != null)
            {
                if (font != null) { text.font = font; text.fontStyle = FontStyle.Normal; }
                text.fontSize = 34;
                text.alignment = TextAnchor.LowerRight;
            }
        }
        var ammo = transform.Find("Ammo") as RectTransform;
        if (ammo != null)
        {
            ammo.anchorMin = new Vector2(0f, 0.5f);
            ammo.anchorMax = new Vector2(1f, 1f);
            ammo.offsetMin = new Vector2(0f, 0f);
            ammo.offsetMax = new Vector2(-46f, -6f);
            var text = ammo.GetComponent<Text>();
            if (text != null)
            {
                if (font != null) { text.font = font; text.fontStyle = FontStyle.Normal; }
                text.fontSize = 22;
                text.alignment = TextAnchor.UpperRight;
            }
        }
        Arrow("Previous Weapon", GameButtonStyle.ArrowLeft, 0f, -1);
        Arrow("Next Weapon", GameButtonStyle.ArrowRight, 1f, 1);
    }

    void Arrow(string arrowName, Sprite sprite, float side, int direction)
    {
        if (sprite == null || transform.Find(arrowName) != null) return;
        var rect = new GameObject(arrowName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(side, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(side == 0f ? 20f : -20f, 0f);
        rect.sizeDelta = new Vector2(36f, 52f);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Cycle(direction));
        // A bigger, invisible touch area around the small arrow.
        var hit = new GameObject("Touch Area", typeof(RectTransform)).GetComponent<RectTransform>();
        hit.SetParent(rect, false);
        hit.sizeDelta = new Vector2(80f, 110f);
        var hitImage = hit.gameObject.AddComponent<Image>();
        hitImage.color = Color.clear;
    }

    // The next / previous weapon slot the player can use (unarmed included), wrapping round.
    void Cycle(int direction)
    {
        var player = GamePlayer.Current;
        var weapons = player != null ? player.transform.GetComponent<PlayerWeaponController>() : null;
        if (weapons == null || weapons.WeaponSlots == null || weapons.WeaponSlots.Count == 0) return;
        int count = weapons.WeaponSlots.Count;
        int current = Mathf.Max(0, weapons.GetCurrentSlotIndex());
        for (int step = 1; step <= count; step++)
        {
            int slot = ((current + direction * step) % count + count) % count;
            if (!weapons.IsSlotAvailable(slot)) continue;
            weapons.EquipSlot(slot);
            return;
        }
    }
}
