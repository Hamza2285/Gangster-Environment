using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Taxi UI (local view, lives next to the taxi canvas, not on it): listens to <see cref="TaxiService"/> events and
/// shows the taxi pointer, notices, the "get in" prompt, the destination list and the ride panel with SetActive.
/// Buttons call back into TaxiService; this script owns no taxi state.
/// </summary>
[DisallowMultipleComponent]
public class TaxiHUD : MonoBehaviour
{
    [SerializeField] private TaxiService taxiService;

    [Header("Taxi Pointer")]
    [Tooltip("On-screen marker over the called taxi (clamped to the screen edge when it is off-screen).")]
    [SerializeField] private RectTransform pointer;
    [SerializeField] private Text pointerText;
    [SerializeField] private RectTransform pointerArrow;
    [Tooltip("Rect of the canvas the pointer lives on (Screen Space - Overlay).")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private Vector2 pointerEdgeMargin = new Vector2(70f, 60f);

    [Header("Notice")]
    [SerializeField] private Text notice;
    [SerializeField, Min(0.1f)] private float noticeSeconds = 2.5f;

    [Header("Get In Prompt")]
    [SerializeField] private GameObject boardPrompt;

    [Header("Destination List")]
    [SerializeField] private GameObject destinationPanel;
    [Tooltip("Parent of the destination buttons.")]
    [SerializeField] private RectTransform destinationList;
    [Tooltip("Inactive button copied once per destination.")]
    [SerializeField] private Button destinationButtonTemplate;
    [SerializeField, Min(1f)] private float destinationButtonSpacing = 52f;
    [SerializeField] private Button destinationCancelButton;

    [Header("Ride Panel")]
    [SerializeField] private GameObject ridePanel;
    [SerializeField] private Text rideDistanceText;
    [SerializeField] private Text rideCameraText;

    private readonly List<Button> destinationButtons = new List<Button>();
    private float noticeTimer;
    private int shownPointerMetres = -1;
    private bool isValid;

    private void Awake()
    {
        isValid = taxiService != null;
        if (!isValid)
        {
            Debug.LogError($"[{nameof(TaxiHUD)}] Missing TaxiService reference on '{name}'. Taxi UI is disabled.", this);
            enabled = false;
            return;
        }
        if (pointer != null && canvasRect == null) Debug.LogWarning($"[{nameof(TaxiHUD)}] No canvas rect assigned; the taxi pointer is hidden.", this);
        if (destinationButtonTemplate != null) destinationButtonTemplate.gameObject.SetActive(false);
        if (destinationCancelButton != null) destinationCancelButton.onClick.AddListener(taxiService.CancelDestinationChoice);

        SetActive(pointer, false);
        SetActive(notice, false);
        SetActive(boardPrompt, false);
        SetActive(destinationPanel, false);
        SetActive(ridePanel, false);
    }

    private void OnEnable()
    {
        if (!isValid) return;
        taxiService.OnStateChanged += HandleStateChanged;
        taxiService.OnNotice += ShowNotice;
        taxiService.OnBoardPromptChanged += HandleBoardPromptChanged;
        taxiService.OnRideCameraChanged += HandleRideCameraChanged;
    }

    private void OnDisable()
    {
        if (taxiService == null) return;
        taxiService.OnStateChanged -= HandleStateChanged;
        taxiService.OnNotice -= ShowNotice;
        taxiService.OnBoardPromptChanged -= HandleBoardPromptChanged;
        taxiService.OnRideCameraChanged -= HandleRideCameraChanged;
    }

    private void OnDestroy()
    {
        if (destinationCancelButton != null && taxiService != null) destinationCancelButton.onClick.RemoveListener(taxiService.CancelDestinationChoice);
    }

    // Per frame only for visuals that follow moving things: the taxi pointer, the ride distance and the notice timer.
    private void Update()
    {
        if (noticeTimer > 0f)
        {
            noticeTimer -= Time.unscaledDeltaTime;
            if (noticeTimer <= 0f) SetActive(notice, false);
        }

        TaxiState state = taxiService.State;
        if (state == TaxiState.EnRoute || state == TaxiState.Waiting) UpdatePointer();
        else if (state == TaxiState.Riding && rideDistanceText != null) rideDistanceText.text = FormatDistance(taxiService.RemainingDistance);
    }

    private void HandleStateChanged(TaxiState state)
    {
        bool showPointer = state == TaxiState.EnRoute || state == TaxiState.Waiting;
        SetActive(pointer, showPointer && canvasRect != null);
        if (showPointer) UpdatePointer();

        if (state == TaxiState.ChoosingDestination) OpenDestinationList();
        else SetActive(destinationPanel, false);

        SetActive(ridePanel, state == TaxiState.Riding);
        if (state != TaxiState.EnRoute && state != TaxiState.Waiting) SetActive(boardPrompt, false);
    }

    private void HandleBoardPromptChanged(bool show) => SetActive(boardPrompt, show);

    private void HandleRideCameraChanged(string viewName)
    {
        if (rideCameraText != null) rideCameraText.text = "Camera: " + viewName + "  (C)";
    }

    public void ShowNotice(string message)
    {
        if (notice == null) return;
        notice.text = message;
        notice.gameObject.SetActive(true);
        noticeTimer = noticeSeconds;
    }

    private void OpenDestinationList()
    {
        if (destinationPanel == null) return;
        BuildDestinationButtons();
        destinationPanel.SetActive(true);
    }

    // Built once (and again only if the destination count changes), positioned manually from the top of the list.
    private void BuildDestinationButtons()
    {
        IReadOnlyList<TaxiDestination> destinations = taxiService.Destinations;
        if (destinationButtonTemplate == null || destinationList == null || destinations == null) return;
        if (destinationButtons.Count == destinations.Count) return;

        foreach (Button button in destinationButtons) if (button != null) Destroy(button.gameObject);
        destinationButtons.Clear();

        var templateRect = (RectTransform)destinationButtonTemplate.transform;
        for (int i = 0; i < destinations.Count; i++)
        {
            Button button = Instantiate(destinationButtonTemplate, destinationList);
            button.name = "Destination_" + i;
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = templateRect.anchoredPosition - new Vector2(0f, destinationButtonSpacing * i);
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = destinations[i] != null ? destinations[i].DisplayName : "-";
            int index = i;
            button.onClick.AddListener(() => taxiService.ChooseDestination(index));
            button.interactable = destinations[i] != null;
            button.gameObject.SetActive(true);
            destinationButtons.Add(button);
        }
    }

    private void UpdatePointer()
    {
        Transform taxi = taxiService.TaxiTransform;
        Camera viewCamera = taxiService.ViewCamera;
        if (pointer == null || canvasRect == null || taxi == null || viewCamera == null) return;

        if (pointerText != null && taxiService.Player != null)
        {
            Vector3 offset = taxi.position - taxiService.Player.position;
            offset.y = 0f;
            int metres = Mathf.RoundToInt(offset.magnitude);
            if (metres != shownPointerMetres)
            {
                shownPointerMetres = metres;
                pointerText.text = "TAXI  " + metres + " m";
            }
        }

        Vector3 screen = viewCamera.WorldToScreenPoint(taxi.position + Vector3.up * 2.6f);
        bool behind = screen.z < 0f;
        if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);

        // Keep the pointer inside the screen; when pinned to an edge, the arrow shows which way to look.
        Vector2 half = canvasRect.rect.size * 0.5f - pointerEdgeMargin;
        bool offScreen = behind || Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y;
        if (offScreen)
        {
            Vector2 dir = local.sqrMagnitude > 1e-4f ? local.normalized : Vector2.down;
            float scale = Mathf.Min(half.x / Mathf.Max(1e-4f, Mathf.Abs(dir.x)), half.y / Mathf.Max(1e-4f, Mathf.Abs(dir.y)));
            local = dir * scale;
        }
        pointer.anchoredPosition = local;
        if (pointerArrow != null)
        {
            if (pointerArrow.gameObject.activeSelf != offScreen) pointerArrow.gameObject.SetActive(offScreen);
            if (offScreen) pointerArrow.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg - 90f);
        }
    }

    private static string FormatDistance(float metres)
    {
        return metres >= 1000f ? (metres / 1000f).ToString("0.0") + " km" : Mathf.RoundToInt(metres) + " m";
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null && component.gameObject.activeSelf != active) component.gameObject.SetActive(active);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }
}
