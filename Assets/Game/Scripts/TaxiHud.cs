using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Taxi screens: the label that follows the taxi, the Watch/Skip choice and the box shown during a ride.
// Buttons are checked by hand so they work without an EventSystem in the scene.
public class TaxiHud : MonoBehaviour
{
    public static TaxiHud Instance { get; private set; }

    public Color accent = new Color(1f, 0.82f, 0.1f, 1f);

    sealed class HudButton
    {
        public RectTransform rect;
        public Image image;
        public Text label;
        public Action onClick;
    }

    static readonly Color ButtonColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    static readonly Color ButtonHover = new Color(0.36f, 0.33f, 0.16f, 1f);

    readonly List<HudButton> optionButtons = new List<HudButton>();
    readonly List<HudButton> rideButtons = new List<HudButton>();

    RectTransform canvasRect;
    Text taxiLabel;
    Transform labelTarget;
    string labelTitle;

    GameObject optionsPanel;
    Text optionsInfo;
    public Action WatchChosen, SkipChosen;

    GameObject rideBox;
    Text fareText, timeText;
    HudButton cameraButton;
    public Action RideSkip, RideCamera;
    Font font;

    void Awake()
    {
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject("Taxi HUD");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = go.GetComponent<RectTransform>();

        BuildLabel();
        BuildOptions();
        BuildRideBox();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetTaxiLabel(Transform target, string title)
    {
        labelTarget = target;
        labelTitle = title;
        taxiLabel.gameObject.SetActive(target != null);
    }

    public void ShowOptions(string info)
    {
        optionsInfo.text = info;
        optionsPanel.SetActive(true);
    }

    public void HideOptions()
    {
        optionsPanel.SetActive(false);
    }

    public void ShowRideBox(bool show)
    {
        rideBox.SetActive(show);
    }

    public void UpdateRideBox(float fare, float secondsLeft, string cameraName)
    {
        int seconds = Mathf.CeilToInt(Mathf.Max(secondsLeft, 0f));
        fareText.text = "Fare   " + FormatFare(fare);
        timeText.text = "Time to reach   " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        cameraButton.label.text = "Camera: " + cameraName + "  [C]";
    }

    public static string FormatFare(float fare)
    {
        return "$" + fare.ToString("0.00");
    }

    void Update()
    {
        UpdateLabel();
        if (optionsPanel.activeSelf) HandleButtons(optionButtons);
        if (rideBox.activeSelf) HandleButtons(rideButtons);
    }

    void UpdateLabel()
    {
        if (labelTarget == null)
        {
            if (taxiLabel.gameObject.activeSelf) taxiLabel.gameObject.SetActive(false);
            return;
        }
        var camera = Camera.main;
        var player = GamePlayer.Current;
        if (camera == null || player == null) return;

        Vector3 world = labelTarget.position + Vector3.up * 2.8f;
        Vector3 screen = camera.WorldToScreenPoint(world);
        // Behind the camera the projection flips; push it to the screen edge instead.
        if (screen.z < 0f)
        {
            screen.x = Screen.width - screen.x;
            screen.y = 0f;
        }
        const float margin = 90f;
        screen.x = Mathf.Clamp(screen.x, margin * 2f, Screen.width - margin * 2f);
        screen.y = Mathf.Clamp(screen.y, margin * 1.4f, Screen.height - margin);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        taxiLabel.rectTransform.localPosition = local;

        Vector3 offset = labelTarget.position - player.transform.position;
        offset.y = 0f;
        taxiLabel.text = labelTitle + "\n" + Mathf.RoundToInt(offset.magnitude) + " m";
    }

    void HandleButtons(List<HudButton> buttons)
    {
        Vector2 pointer = GameInput.PointerPosition;
        bool click = GameInput.PrimaryClickDown;
        foreach (var button in buttons)
        {
            bool over = RectTransformUtility.ScreenPointToLocalPointInRectangle(button.rect, pointer, null, out Vector2 local)
                && button.rect.rect.Contains(local);
            button.image.color = over ? ButtonHover : ButtonColor;
            if (over && click)
            {
                button.onClick?.Invoke();
                return;
            }
        }
    }

    void BuildLabel()
    {
        taxiLabel = NewText("", canvasRect, 28, TextAnchor.MiddleCenter, accent);
        taxiLabel.fontStyle = FontStyle.Bold;
        taxiLabel.rectTransform.sizeDelta = new Vector2(360f, 80f);
        var shadow = taxiLabel.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(2f, -2f);
        taxiLabel.gameObject.SetActive(false);
    }

    void BuildOptions()
    {
        var panel = NewPanel("Trip Options", canvasRect, new Vector2(620f, 330f));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        optionsPanel = panel.gameObject;

        var title = NewText("Your trip", panel, 36, TextAnchor.UpperCenter, accent);
        Place(title.rectTransform, new Vector2(0f, 128f), new Vector2(580f, 50f));

        optionsInfo = NewText("", panel, 26, TextAnchor.UpperCenter, Color.white);
        Place(optionsInfo.rectTransform, new Vector2(0f, 74f), new Vector2(580f, 44f));

        optionButtons.Add(NewButton("Watch the whole trip  [1]", panel, new Vector2(0f, 0f), new Vector2(520f, 66f), () => WatchChosen?.Invoke()));
        optionButtons.Add(NewButton("Skip to destination  [2 / Enter]", panel, new Vector2(0f, -80f), new Vector2(520f, 66f), () => SkipChosen?.Invoke()));

        var back = NewText("Esc: back to map", panel, 22, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f, 1f));
        Place(back.rectTransform, new Vector2(0f, -140f), new Vector2(580f, 30f));

        optionsPanel.SetActive(false);
    }

    void BuildRideBox()
    {
        var box = NewPanel("Ride Box", canvasRect, new Vector2(390f, 240f));
        box.anchorMin = box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(1f, 0f);
        box.anchoredPosition = new Vector2(-36f, 36f);
        rideBox = box.gameObject;

        var title = NewText("TAXI", box, 30, TextAnchor.MiddleCenter, accent);
        Place(title.rectTransform, new Vector2(0f, 88f), new Vector2(360f, 40f));

        fareText = NewText("", box, 26, TextAnchor.MiddleCenter, Color.white);
        Place(fareText.rectTransform, new Vector2(0f, 50f), new Vector2(360f, 34f));
        timeText = NewText("", box, 26, TextAnchor.MiddleCenter, Color.white);
        Place(timeText.rectTransform, new Vector2(0f, 16f), new Vector2(360f, 34f));

        rideButtons.Add(NewButton("Skip to destination  [Enter]", box, new Vector2(0f, -34f), new Vector2(360f, 54f), () => RideSkip?.Invoke()));
        cameraButton = NewButton("", box, new Vector2(0f, -94f), new Vector2(360f, 54f), () => RideCamera?.Invoke());
        rideButtons.Add(cameraButton);

        rideBox.SetActive(false);
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static RectTransform NewPanel(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.07f, 0.07f, 0.07f, 0.93f);
        image.raycastTarget = false;
        return rect;
    }

    HudButton NewButton(string text, Transform parent, Vector2 position, Vector2 size, Action onClick)
    {
        var rect = new GameObject("Button", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = ButtonColor;
        image.raycastTarget = false;
        var label = NewText(text, rect, 24, TextAnchor.MiddleCenter, Color.white);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return new HudButton { rect = rect, image = image, label = label, onClick = onClick };
    }

    Text NewText(string content, Transform parent, int size, TextAnchor anchor, Color color)
    {
        var rect = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
