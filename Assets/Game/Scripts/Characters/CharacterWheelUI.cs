using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The character selector: a ring of portraits around the middle of the screen while the switch key is held. Moving the
// mouse (or A / D, the arrow keys) points at a character; letting go of the key switches to them.
public class CharacterWheelUI : MonoBehaviour
{
    const float Radius = 230f, SlotSize = 170f;

    Canvas canvas;
    RectTransform wheel;
    CanvasGroup group;
    Text centreName, centreHint, message;
    Image hub;
    readonly List<Slot> slots = new List<Slot>();
    Font font;
    Sprite disc, ring, softDisc;
    Vector2 pick;
    float messageUntil, openedAt;

    public bool IsOpen { get; private set; }
    public int Selected { get; private set; } = -1;
    // Where the finger holding the SWITCH button is (screen pixels), or null. Set by MobileGameButtons.
    public static Vector2? TouchPoint;
    int openedWith;

    sealed class Slot
    {
        public RectTransform rect;
        public Image glow, rim, backing, portrait;
        public Text name, tag;
        public float angle;
        public PlayableCharacter character;
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        disc = MakeCircle(256, 0f, 1f, false);
        ring = MakeCircle(256, 0.86f, 1f, false);
        softDisc = MakeCircle(256, 0f, 1f, true);
        Build();
        canvas.gameObject.SetActive(false);
    }

    public void Open(IList<PlayableCharacter> characters, int current)
    {
        foreach (var s in slots) Destroy(s.rect.gameObject);
        slots.Clear();
        int n = characters.Count;
        for (int i = 0; i < n; i++)
        {
            // Spread evenly, the first on the left: two characters sit left and right of the middle.
            float angle = 180f - i * (360f / Mathf.Max(2, n));
            slots.Add(MakeSlot(characters[i], angle, i == current));
        }
        pick = Vector2.zero;
        Selected = current;
        openedWith = current;
        IsOpen = true;
        openedAt = Time.unscaledTime;
        canvas.gameObject.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        IsOpen = false;
        canvas.gameObject.SetActive(false);
    }

    // Brief note in the middle of the screen, e.g. why switching is not possible right now.
    public void Flash(string text)
    {
        if (!canvas.gameObject.activeSelf)
        {
            canvas.gameObject.SetActive(true);
            wheel.gameObject.SetActive(false);
        }
        message.text = text;
        message.enabled = true;
        messageUntil = Time.unscaledTime + 2f;
    }

    void Update()
    {
        if (message.enabled && Time.unscaledTime > messageUntil)
        {
            message.enabled = false;
            if (!IsOpen) canvas.gameObject.SetActive(false);
        }
        if (!IsOpen) return;
        wheel.gameObject.SetActive(true);
        group.alpha = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.12f);
        wheel.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, group.alpha);

        // A finger: the character it is dragged onto (off the wheel = stay as you are).
        if (TouchPoint.HasValue)
        {
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(null, wheel.position);
            Vector2 finger = (TouchPoint.Value - centre) / Mathf.Max(0.01f, canvas.scaleFactor);
            if (finger.magnitude > Radius + SlotSize) Selected = openedWith;
            else if (finger.magnitude > 40f) Selected = Nearest(Mathf.Atan2(finger.y, finger.x) * Mathf.Rad2Deg);
            Refresh();
            return;
        }

        // Mouse points at a slot; keys step through them.
        pick += GameInput.Look * 1.6f;
        if (pick.magnitude > 60f) pick = pick.normalized * 60f;
        if (pick.magnitude > 18f) Selected = Nearest(Mathf.Atan2(pick.y, pick.x) * Mathf.Rad2Deg);
        if (GameInput.MenuLeftDown) StepTo(180f);
        if (GameInput.MenuRightDown) StepTo(0f);
        if (GameInput.MenuUpDown) StepTo(90f);
        if (GameInput.MenuDownDown) StepTo(-90f);
        Refresh();
    }

    void StepTo(float angle)
    {
        Selected = Nearest(angle);
        pick = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 40f;
    }

    int Nearest(float angle)
    {
        int best = Selected;
        float bestGap = 999f;
        for (int i = 0; i < slots.Count; i++)
        {
            float gap = Mathf.Abs(Mathf.DeltaAngle(angle, slots[i].angle));
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        return best;
    }

    void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            bool on = i == Selected;
            float scale = on ? 1.12f : 0.9f;
            s.rect.localScale = Vector3.Lerp(s.rect.localScale, Vector3.one * scale, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            var accent = s.character.accent;
            s.rim.color = on ? accent : new Color(1f, 1f, 1f, 0.35f);
            s.glow.color = on ? new Color(accent.r, accent.g, accent.b, 0.55f) : new Color(0f, 0f, 0f, 0f);
            s.backing.color = on ? Color.Lerp(new Color(0.08f, 0.08f, 0.1f, 1f), accent, 0.35f) : new Color(0.08f, 0.08f, 0.1f, 0.95f);
            s.portrait.color = on ? Color.white : new Color(0.62f, 0.62f, 0.66f, 1f);
            s.name.color = on ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }
        if (Selected >= 0 && Selected < slots.Count)
        {
            var s = slots[Selected];
            centreName.text = s.character.displayName.ToUpperInvariant();
            centreName.color = s.character.accent;
            centreHint.text = s.tag.enabled ? "PLAYING NOW" : "RELEASE TO SWITCH";
            hub.color = new Color(0.04f, 0.04f, 0.05f, 0.88f);
        }
    }

    void Build()
    {
        var go = new GameObject("Character Wheel Canvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Darkened screen, darker towards the middle so the portraits stand out.
        var shade = NewImage("Shade", go.transform, null, new Color(0f, 0f, 0f, 0.35f));
        Stretch(shade.rectTransform);
        shade.raycastTarget = false;

        wheel = new GameObject("Wheel", typeof(RectTransform)).GetComponent<RectTransform>();
        wheel.SetParent(go.transform, false);
        wheel.sizeDelta = Vector2.one * (Radius * 2f + SlotSize * 1.4f);
        group = wheel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        var pool = NewImage("Pool", wheel, softDisc, new Color(0f, 0f, 0f, 0.6f));
        pool.rectTransform.sizeDelta = Vector2.one * (Radius * 2f + SlotSize * 1.6f);
        var track = NewImage("Track", wheel, ring, new Color(1f, 1f, 1f, 0.12f));
        track.rectTransform.sizeDelta = Vector2.one * Radius * 2f;

        hub = NewImage("Hub", wheel, disc, new Color(0.04f, 0.04f, 0.05f, 0.88f));
        hub.rectTransform.sizeDelta = Vector2.one * 210f;
        var hubRim = NewImage("Hub Rim", wheel, ring, new Color(1f, 1f, 1f, 0.25f));
        hubRim.rectTransform.sizeDelta = Vector2.one * 210f;
        centreName = NewText("Name", wheel, 34, FontStyle.Bold, new Vector2(0f, 16f));
        centreHint = NewText("Hint", wheel, 15, FontStyle.Normal, new Vector2(0f, -26f));
        centreHint.color = new Color(1f, 1f, 1f, 0.65f);

        message = NewText("Message", go.transform, 26, FontStyle.Bold, new Vector2(0f, -330f));
        message.rectTransform.sizeDelta = new Vector2(1200f, 60f);
        message.enabled = false;
    }

    Slot MakeSlot(PlayableCharacter character, float angle, bool current)
    {
        var s = new Slot { character = character, angle = angle };
        s.rect = new GameObject(character.displayName, typeof(RectTransform)).GetComponent<RectTransform>();
        s.rect.SetParent(wheel, false);
        s.rect.sizeDelta = Vector2.one * SlotSize;
        s.rect.anchoredPosition = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * Radius;
        s.rect.localScale = Vector3.one * 0.6f;

        s.glow = NewImage("Glow", s.rect, softDisc, Color.clear);
        s.glow.rectTransform.sizeDelta = Vector2.one * SlotSize * 1.5f;
        s.backing = NewImage("Backing", s.rect, disc, Color.black);
        s.backing.rectTransform.sizeDelta = Vector2.one * SlotSize;
        s.backing.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        s.portrait = NewImage("Portrait", s.backing.rectTransform, character.portrait, Color.white);
        s.portrait.preserveAspect = true;
        s.portrait.rectTransform.sizeDelta = Vector2.one * SlotSize * 1.08f;
        s.portrait.rectTransform.anchoredPosition = new Vector2(0f, -6f);
        if (character.portrait == null) s.portrait.color = Color.clear;
        s.rim = NewImage("Rim", s.rect, ring, Color.white);
        s.rim.rectTransform.sizeDelta = Vector2.one * SlotSize;

        s.name = NewText("Label", s.rect, 22, FontStyle.Bold, new Vector2(0f, -SlotSize * 0.5f - 22f));
        s.name.text = character.displayName.ToUpperInvariant();
        s.tag = NewText("Now", s.rect, 13, FontStyle.Bold, new Vector2(0f, SlotSize * 0.5f + 16f));
        s.tag.text = "PLAYING";
        s.tag.color = new Color(1f, 1f, 1f, 0.7f);
        s.tag.enabled = current;
        return s;
    }

    Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(parent, false);
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text NewText(string name, Transform parent, int size, FontStyle style, Vector2 position)
    {
        var text = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.rectTransform.sizeDelta = new Vector2(320f, 50f);
        text.rectTransform.anchoredPosition = position;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // A filled disc (inner 0) or ring, anti-aliased; soft fades from the middle out.
    static Sprite MakeCircle(int size, float inner, float outer, bool soft)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float half = size * 0.5f, edge = 1.5f / half;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
            float a = soft ? Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) : Mathf.Clamp01((outer - d) / edge) * (inner <= 0f ? 1f : Mathf.Clamp01((d - inner) / edge));
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
