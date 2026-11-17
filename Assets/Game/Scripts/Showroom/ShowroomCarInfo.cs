using UnityEngine;
using UnityEngine.UI;

// The info card for the car the player is standing at in the showroom: its name, class, price and four rated bars (top
// speed, acceleration, handling, braking). Fades in when the player walks up to a turntable, out when they walk away.
public class ShowroomCarInfo : MonoBehaviour
{
    static readonly Color Gold = new Color(1f, 0.78f, 0.28f, 1f);
    static readonly Color Panel = new Color(0.04f, 0.045f, 0.06f, 0.88f);
    static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Money = new Color(0.45f, 1f, 0.5f, 1f);

    CanvasGroup group;
    Text nameText, classText, priceText;
    Image[] fills = new Image[4];
    Text[] values = new Text[4];
    float[] shown = new float[4];
    ShowroomCar current;
    Font font;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void Update()
    {
        var player = GamePlayer.Current;
        var travel = ShowroomTravel.Instance;
        ShowroomCar nearest = null;
        if (player != null && (travel == null || travel.Inside))
        {
            Vector3 at = player.transform.position;
            float best = float.MaxValue;
            foreach (var car in ShowroomCar.All)
            {
                Vector3 centre = car.BayCentreWorld;
                if (Mathf.Abs(at.y - centre.y) > 2f) continue;   // the other floor
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(centre.x, centre.z));
                if (d < car.showRadius && d < best) { best = d; nearest = car; }
            }
        }

        if (nearest != null && nearest != current)
        {
            current = nearest;
            Fill(current);
            for (int i = 0; i < shown.Length; i++) shown[i] = 0f;
        }
        float target = nearest != null ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 5f);
        if (group.alpha <= 0f) current = null;

        // The bars grow in when a new car's card comes up.
        if (current != null)
        {
            float[] rated = { current.speedRating, current.acceleration, current.handling, current.braking };
            for (int i = 0; i < fills.Length; i++)
            {
                shown[i] = Mathf.MoveTowards(shown[i], rated[i] / 100f, Time.unscaledDeltaTime * 1.6f);
                fills[i].rectTransform.anchorMax = new Vector2(shown[i], 1f);
            }
        }
    }

    void Fill(ShowroomCar car)
    {
        nameText.text = car.displayName.ToUpperInvariant();
        classText.text = car.carClass.ToUpperInvariant() + "   •   " + car.drive;
        priceText.text = "$" + car.price.ToString("N0");
        values[0].text = Mathf.RoundToInt(car.topSpeed) + " km/h";
        values[1].text = car.zeroToHundred.ToString("0.0") + " s  0-100";
        values[2].text = Mathf.RoundToInt(car.handling) + " / 100";
        values[3].text = Mathf.RoundToInt(car.braking) + " / 100";
    }

    // ---------- UI ----------

    void Build()
    {
        var canvasObject = new GameObject("Showroom Car Card");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // A card on the right, below the money readout.
        var card = Rect("Card", canvasObject.transform, new Vector2(1f, 0.5f), new Vector2(-40f, -40f), new Vector2(460f, 400f));
        card.pivot = new Vector2(1f, 0.5f);
        card.gameObject.AddComponent<Image>().color = Panel;
        var accent = Rect("Accent", card, new Vector2(0f, 1f), Vector2.zero, new Vector2(460f, 5f));
        accent.pivot = new Vector2(0f, 1f);
        accent.gameObject.AddComponent<Image>().color = Gold;

        var tag = Label("Tag", card, "ON DISPLAY", 15, Gold, new Vector2(28f, -22f), new Vector2(400f, 22f));
        tag.fontStyle = FontStyle.Bold;
        nameText = Label("Name", card, "", 34, Color.white, new Vector2(28f, -46f), new Vector2(410f, 44f));
        nameText.fontStyle = FontStyle.Bold;
        classText = Label("Class", card, "", 16, new Color(1f, 1f, 1f, 0.6f), new Vector2(28f, -92f), new Vector2(410f, 22f));
        priceText = Label("Price", card, "", 38, Money, new Vector2(28f, -120f), new Vector2(410f, 48f));
        priceText.fontStyle = FontStyle.Bold;

        var line = Rect("Line", card, new Vector2(0f, 1f), new Vector2(28f, -178f), new Vector2(404f, 1f));
        line.pivot = new Vector2(0f, 1f);
        line.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

        string[] names = { "TOP SPEED", "ACCELERATION", "HANDLING", "BRAKING" };
        for (int i = 0; i < names.Length; i++)
        {
            float y = -196f - i * 50f;
            var label = Label(names[i], card, names[i], 15, new Color(1f, 1f, 1f, 0.85f), new Vector2(28f, y), new Vector2(200f, 20f));
            label.fontStyle = FontStyle.Bold;
            values[i] = Label(names[i] + " Value", card, "", 15, Gold, new Vector2(232f, y), new Vector2(200f, 20f));
            values[i].alignment = TextAnchor.UpperRight;
            var track = Rect(names[i] + " Bar", card, new Vector2(0f, 1f), new Vector2(28f, y - 26f), new Vector2(404f, 8f));
            track.pivot = new Vector2(0f, 1f);
            track.gameObject.AddComponent<Image>().color = Track;
            var fill = Rect("Fill", track, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fills[i] = fill.gameObject.AddComponent<Image>();
            fills[i].color = Gold;
        }
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    Text Label(string name, Transform parent, string text, int size, Color colour, Vector2 position, Vector2 box)
    {
        var rect = Rect(name, parent, new Vector2(0f, 1f), position, box);
        rect.pivot = new Vector2(0f, 1f);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }
}
