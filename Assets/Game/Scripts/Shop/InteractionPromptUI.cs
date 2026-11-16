using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.UI;

// Help box in the top-left corner for whatever the player can interact with right now ("Press E to enter...").
public class InteractionPromptUI : MonoBehaviour
{
    public string keyLabel = "E";

    PlayerInteractionController interaction;
    GameObject box;
    Text text;
    CanvasGroup group;
    float shown;

    void Awake()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("Interaction Prompt Canvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        box = new GameObject("Prompt", typeof(RectTransform));
        var rect = box.GetComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(36f, -96f);
        box.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.04f, 0.82f);
        group = box.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        var fit = box.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var layout = box.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(22, 26, 16, 16);

        text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        text.transform.SetParent(rect, false);
        text.font = font;
        text.fontSize = 28;
        text.color = Color.white;
        text.supportRichText = true;
        text.raycastTarget = false;
        box.SetActive(false);
    }

    void Update()
    {
        if (interaction == null) interaction = FindFirstObjectByType<PlayerInteractionController>();
        var focus = interaction != null && interaction.isActiveAndEnabled ? interaction.CurrentFocus : null;
        bool show = focus != null && !ClothingStoreMenu.IsOpen && (ClothingStoreTravel.Instance == null || !ClothingStoreTravel.Instance.Busy);
        if (show)
        {
            text.text = "Press <color=#FFC83D><b> " + keyLabel + " </b></color> to " + Lower(focus.InteractionPrompt);
            if (!box.activeSelf) shown = 0f;
        }
        box.SetActive(show);
        if (show)
        {
            shown += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(shown / 0.15f);
        }
    }

    // "Enter Miami Clothing Store" reads "Press E to enter Miami Clothing Store".
    static string Lower(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return "interact";
        return char.ToLowerInvariant(prompt[0]) + prompt.Substring(1);
    }
}
