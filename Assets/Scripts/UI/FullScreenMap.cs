using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Full-screen grayscale map: opens from the minimap (tap or M), pans by dragging, re-centres on the
// player with the locate button, and closes with Escape.
public class FullScreenMap : MonoBehaviour
{
    public Minimap minimap;
    public GameObject root;
    public RawImage mapImage;
    public RectTransform playerIcon;
    [Tooltip("Half the world height shown on the full map, in metres.")]
    public float viewRadius = 350f;

    [Header("Zoom")]
    public Slider zoomSlider;
    [Tooltip("Most zoomed in: half the screen height in metres.")]
    public float minViewRadius = 80f;
    [Tooltip("Most zoomed out: half the screen height in metres.")]
    public float maxViewRadius = 800f;
    [Tooltip("Slider change per mouse-wheel notch or +/- press.")]
    public float zoomStep = 0.1f;

    [Header("Waypoint")]
    public RectTransform waypointMarker;
    [Tooltip("Clicking this close to the marker (UI units) removes it.")]
    public float markerRemoveDistance = 40f;
    [Tooltip("Seconds the view takes to glide back to the player.")]
    public float recentreTime = 0.35f;
    [Tooltip("Player input disabled while the map is open.")]
    public Behaviour playerInput;
    public StarterAssets.StarterAssetsInputs starterInputs;

    [Tooltip("Optional heading shown at the top of the map (e.g. when choosing a taxi destination).")]
    public Text title;

    [Tooltip("Turned off while the player can't use the map (e.g. riding in a taxi).")]
    public bool allowOpen = true;

    public bool IsOpen { get { return root != null && root.activeSelf; } }

    // Raised after the map closes.
    public event System.Action Closed;

    public void Open(string heading)
    {
        Open();
        if (title != null)
        {
            title.text = heading;
            title.gameObject.SetActive(!string.IsNullOrEmpty(heading));
        }
    }

    private Canvas _canvas;
    private Vector2 _viewCentre;
    private bool _recentring;
    private Vector2 _recentreVelocity;
    private CursorLockMode _previousLock;
    private bool _previousCursorVisible;

    private void Start()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (root != null) root.SetActive(false);
        if (mapImage != null && minimap != null) mapImage.texture = minimap.fullMapTexture;
        if (zoomSlider != null)
        {
            zoomSlider.minValue = 0f;
            zoomSlider.maxValue = 1f;
            zoomSlider.SetValueWithoutNotify(RadiusToZoom(viewRadius));
            zoomSlider.onValueChanged.AddListener(SetZoom);
        }
    }

    // 0 = most zoomed out, 1 = most zoomed in, on a log scale so each step feels the same.
    public void SetZoom(float zoom01)
    {
        viewRadius = Mathf.Exp(Mathf.Lerp(Mathf.Log(maxViewRadius), Mathf.Log(minViewRadius), Mathf.Clamp01(zoom01)));
        if (zoomSlider != null) zoomSlider.SetValueWithoutNotify(RadiusToZoom(viewRadius));
    }

    public void ZoomBy(float steps)
    {
        SetZoom(RadiusToZoom(viewRadius) + steps * zoomStep);
    }

    public void ZoomIn() { ZoomBy(1f); }

    public void ZoomOut() { ZoomBy(-1f); }

    private float RadiusToZoom(float radius)
    {
        return Mathf.InverseLerp(Mathf.Log(maxViewRadius), Mathf.Log(minViewRadius), Mathf.Log(Mathf.Clamp(radius, minViewRadius, maxViewRadius)));
    }

    public void OnMapClicked(Vector2 screenPosition)
    {
        var rt = (RectTransform)mapImage.transform;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPosition, null, out local)) return;

        if (minimap.HasWaypoint && waypointMarker != null
            && (local - (Vector2)waypointMarker.anchoredPosition).magnitude < markerRemoveDistance)
        {
            minimap.ClearWaypoint();
            return;
        }

        float unitsPerMetre = rt.rect.height / (viewRadius * 2f);
        Vector2 world = _viewCentre + (local - rt.rect.center) / unitsPerMetre;
        Vector2 min = minimap.worldMin, max = minimap.worldMin + Vector2.one * minimap.worldSize;
        minimap.SetWaypoint(new Vector2(Mathf.Clamp(world.x, min.x, max.x), Mathf.Clamp(world.y, min.y, max.y)));
    }

    private void Update()
    {
        if (IsOpen)
        {
            if (EscapePressed()) Close();
            else UpdateView();
        }
        else if (OpenKeyPressed())
        {
            Open();
        }
    }

    public void Open()
    {
        if (IsOpen || minimap == null || !allowOpen) return;
        root.SetActive(true);
        if (title != null) title.gameObject.SetActive(false);
        _viewCentre = PlayerPosition();
        _recentring = false;
        _previousLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (starterInputs != null)
        {
            starterInputs.MoveInput(Vector2.zero);
            starterInputs.LookInput(Vector2.zero);
            starterInputs.cursorLocked = false;
        }
        if (playerInput != null) playerInput.enabled = false;
        UpdateView();
    }

    public void Close()
    {
        if (!IsOpen) return;
        root.SetActive(false);
        if (playerInput != null) playerInput.enabled = true;
        if (starterInputs != null) starterInputs.cursorLocked = _previousLock == CursorLockMode.Locked;
        Cursor.lockState = _previousLock;
        Cursor.visible = _previousCursorVisible;
        if (Closed != null) Closed();
    }

    // World X/Z to a position on the map image (anchored at its centre), for the current pan and zoom.
    public Vector2 WorldToMap(Vector2 worldXZ)
    {
        Rect rect = ((RectTransform)mapImage.transform).rect;
        return (worldXZ - _viewCentre) * (rect.height / (viewRadius * 2f));
    }

    // Drag delta in screen pixels; the map follows the pointer.
    public void Pan(Vector2 screenDelta)
    {
        _recentring = false;
        _viewCentre -= screenDelta * MetresPerScreenPixel();
        ClampCentre();
    }

    public void RecentreOnPlayer()
    {
        _recentring = true;
        _recentreVelocity = Vector2.zero;
    }

    private void UpdateView()
    {
        if (minimap.target == null) return;

        if (_recentring)
        {
            _viewCentre = Vector2.SmoothDamp(_viewCentre, PlayerPosition(), ref _recentreVelocity, recentreTime, Mathf.Infinity, Time.unscaledDeltaTime);
            if ((_viewCentre - PlayerPosition()).sqrMagnitude < 0.01f) _recentring = false;
        }

        Rect rect = ((RectTransform)mapImage.transform).rect;
        float aspect = rect.height > 0f ? rect.width / rect.height : 16f / 9f;
        Vector2 centreUV = (_viewCentre - minimap.worldMin) / minimap.worldSize;
        float spanY = viewRadius * 2f / minimap.worldSize;
        float spanX = spanY * aspect;
        mapImage.uvRect = new Rect(centreUV.x - spanX * 0.5f, centreUV.y - spanY * 0.5f, spanX, spanY);

        if (playerIcon != null)
        {
            float unitsPerMetre = rect.height / (viewRadius * 2f);
            playerIcon.anchoredPosition = (PlayerPosition() - _viewCentre) * unitsPerMetre;
            Transform heading = minimap.headingSource != null ? minimap.headingSource : minimap.target;
            playerIcon.localEulerAngles = new Vector3(0f, 0f, -heading.eulerAngles.y);
        }

        if (waypointMarker != null)
        {
            waypointMarker.gameObject.SetActive(minimap.HasWaypoint);
            if (minimap.HasWaypoint)
                waypointMarker.anchoredPosition = (minimap.Waypoint - _viewCentre) * (rect.height / (viewRadius * 2f));
        }
    }

    private float MetresPerScreenPixel()
    {
        float heightPixels = ((RectTransform)mapImage.transform).rect.height * (_canvas != null ? _canvas.scaleFactor : 1f);
        return heightPixels > 0f ? viewRadius * 2f / heightPixels : 0f;
    }

    private void ClampCentre()
    {
        Vector2 min = minimap.worldMin, max = minimap.worldMin + Vector2.one * minimap.worldSize;
        _viewCentre = new Vector2(Mathf.Clamp(_viewCentre.x, min.x, max.x), Mathf.Clamp(_viewCentre.y, min.y, max.y));
    }

    private Vector2 PlayerPosition()
    {
        Vector3 p = minimap.target.position;
        return new Vector2(p.x, p.z);
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private static bool OpenKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.mKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.M);
#else
        return false;
#endif
    }
}
