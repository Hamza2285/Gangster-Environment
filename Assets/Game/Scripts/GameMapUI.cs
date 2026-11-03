using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Builds the HUD at runtime: GTA-style minimap, full-screen map for placing markers, and messages.
public class GameMapUI : MonoBehaviour
{
    public static GameMapUI Instance { get; private set; }

    public MiniMapData mapData;

    [Header("Minimap")]
    public float minimapSize = 280f;
    public float minimapMargin = 36f;
    [Tooltip("Metres of world shown from the centre to the edge of the minimap.")]
    public float minimapRange = 170f;

    [Header("Colors")]
    public Color routeColor = new Color(1f, 0.78f, 0.1f, 1f);
    public Color markerColor = new Color(1f, 0.78f, 0.1f, 1f);
    public Color taxiColor = new Color(1f, 0.9f, 0.2f, 1f);

    public bool IsMapOpen { get; private set; }
    public bool ClosedThisFrame => closedFrame == Time.frameCount;
    public Transform TrackedTaxi { get; set; }

    bool chooseDestinationMode;
    int closedFrame = -1;
    Text fullMapHelp;
    const string MapHelp = "Left click: set destination     Right click: remove marker     M: close map";
    const string TaxiMapHelp = "Left click: choose destination     Right click: remove marker     Esc: go";

    Canvas canvas;
    RectTransform miniContent, miniRoot, miniPlayer, miniMarker, miniTaxi, northLabel;
    UIRouteLine miniRoute;
    float miniContentSize;
    float miniRadius;

    GameObject fullPanel;
    RectTransform fullMap, fullPlayer, fullMarker, fullTaxi;
    UIRouteLine fullRoute;

    Text messageText, hintText;
    float messageUntil;
    Sprite circleSprite, ringSprite, arrowSprite, diamondSprite;
    Font font;
    readonly List<Vector2> linePoints = new List<Vector2>();
    bool routeDirty;

    void Awake()
    {
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        circleSprite = MakeSprite(128, (u, v) => Mathf.Clamp01((0.5f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f))) * 128f));
        ringSprite = MakeSprite(256, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
            return Mathf.Clamp01((0.5f - d) * 256f) * Mathf.Clamp01((d - 0.475f) * 256f);
        });
        arrowSprite = MakeSprite(64, (u, v) =>
        {
            // Arrow pointing up with a notch at the base.
            float x = Mathf.Abs(u - 0.5f) * 2f;
            float notch = 0.18f + x * 0.34f;
            return v > notch && x < (1f - v) * 0.92f ? 1f : 0f;
        });
        diamondSprite = MakeSprite(64, (u, v) => Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) < 0.48f ? 1f : 0f);

        BuildCanvas();
        BuildMinimap();
        BuildFullMap();
        BuildMessages();
    }

    void Start()
    {
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.RouteChanged += () => routeDirty = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ShowMessage(string text, float seconds = 3.5f)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + seconds;
    }

    public void SetHint(string text)
    {
        hintText.text = text;
    }

    void Update()
    {
        var player = FirstPersonPlayer.Instance;
        if (player != null && GameInput.MapDown && (IsMapOpen || player.ControlEnabled)) ToggleMap();
        else if (IsMapOpen && GameInput.CancelDown) ToggleMap();

        if (IsMapOpen) HandleMapClicks();
        if (routeDirty) RebuildRoutes();
        if (player != null)
        {
            UpdateMinimap(player.transform);
            if (IsMapOpen) UpdateFullMap(player.transform);
        }
        messageText.enabled = Time.unscaledTime < messageUntil;
        hintText.enabled = !IsMapOpen;
    }

    void ToggleMap()
    {
        IsMapOpen = !IsMapOpen;
        fullPanel.SetActive(IsMapOpen);
        if (!IsMapOpen) closedFrame = Time.frameCount;
        var player = FirstPersonPlayer.Instance;
        if (player != null)
        {
            // While sitting in the taxi, the taxi service owns the player's control.
            if (!chooseDestinationMode) player.SetControl(!IsMapOpen);
            player.LockCursor(!IsMapOpen);
        }
        if (!IsMapOpen)
        {
            chooseDestinationMode = false;
            fullMapHelp.text = MapHelp;
        }
        routeDirty = true;
    }

    // Opened from inside the taxi: closing it (Esc or M) is how the player confirms the destination.
    public void OpenToChooseDestination()
    {
        if (IsMapOpen) return;
        chooseDestinationMode = true;
        fullMapHelp.text = TaxiMapHelp;
        ToggleMap();
    }

    void HandleMapClicks()
    {
        bool place = GameInput.PrimaryClickDown, remove = GameInput.SecondaryClickDown;
        if (!place && !remove) return;
        var waypoints = WaypointSystem.Instance;
        if (waypoints == null) return;
        if (remove)
        {
            waypoints.ClearMarker();
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(fullMap, GameInput.PointerPosition, null, out Vector2 local)) return;
        Rect r = fullMap.rect;
        Vector2 uv = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height);
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;
        waypoints.SetMarker(mapData.UVToWorld(uv));
    }

    void UpdateMinimap(Transform player)
    {
        Vector2 playerLocal = MiniLocal(player.position);
        float yaw = player.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(0f, 0f, yaw);
        miniContent.localRotation = rot;
        miniContent.anchoredPosition = -(Vector2)(rot * playerLocal);
        northLabel.anchoredPosition = (Vector2)(rot * Vector2.up) * (miniRadius - 14f);

        var waypoints = WaypointSystem.Instance;
        bool hasMarker = waypoints != null && waypoints.HasMarker;
        miniMarker.gameObject.SetActive(hasMarker);
        if (hasMarker) miniMarker.anchoredPosition = Clamp(rot * (MiniLocal(waypoints.MarkerPosition) - playerLocal));

        bool hasTaxi = TrackedTaxi != null;
        miniTaxi.gameObject.SetActive(hasTaxi);
        if (hasTaxi) miniTaxi.anchoredPosition = Clamp(rot * (MiniLocal(TrackedTaxi.position) - playerLocal));
    }

    Vector2 Clamp(Vector2 v)
    {
        return Vector2.ClampMagnitude(v, miniRadius - 12f);
    }

    void UpdateFullMap(Transform player)
    {
        fullPlayer.anchoredPosition = FullLocal(player.position);
        fullPlayer.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
        var waypoints = WaypointSystem.Instance;
        bool hasMarker = waypoints != null && waypoints.HasMarker;
        fullMarker.gameObject.SetActive(hasMarker);
        if (hasMarker) fullMarker.anchoredPosition = FullLocal(waypoints.MarkerPosition);
        fullTaxi.gameObject.SetActive(TrackedTaxi != null);
        if (TrackedTaxi != null) fullTaxi.anchoredPosition = FullLocal(TrackedTaxi.position);
    }

    Vector2 MiniLocal(Vector3 world)
    {
        return (mapData.WorldToUV(world) - new Vector2(0.5f, 0.5f)) * miniContentSize;
    }

    Vector2 FullLocal(Vector3 world)
    {
        return (mapData.WorldToUV(world) - new Vector2(0.5f, 0.5f)) * fullMap.rect.width;
    }

    void RebuildRoutes()
    {
        routeDirty = false;
        var waypoints = WaypointSystem.Instance;
        linePoints.Clear();
        if (waypoints != null && waypoints.HasMarker)
        {
            var route = waypoints.Route;
            for (int i = waypoints.RouteIndex; i < route.Count; i++) linePoints.Add(MiniLocal(route[i]));
        }
        miniRoute.SetPoints(linePoints);

        if (IsMapOpen)
        {
            linePoints.Clear();
            if (waypoints != null && waypoints.HasMarker)
            {
                var route = waypoints.Route;
                for (int i = waypoints.RouteIndex; i < route.Count; i++) linePoints.Add(FullLocal(route[i]));
            }
            fullRoute.SetPoints(linePoints);
        }
    }

    void BuildCanvas()
    {
        var go = new GameObject("Game HUD");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    void BuildMinimap()
    {
        miniRadius = minimapSize * 0.5f;

        miniRoot = NewRect("Minimap", canvas.transform);
        miniRoot.anchorMin = miniRoot.anchorMax = Vector2.zero;
        miniRoot.pivot = new Vector2(0.5f, 0.5f);
        miniRoot.sizeDelta = Vector2.one * minimapSize;
        miniRoot.anchoredPosition = Vector2.one * (minimapMargin + miniRadius);

        var maskImage = miniRoot.gameObject.AddComponent<Image>();
        maskImage.sprite = circleSprite;
        maskImage.color = new Color(0.09f, 0.09f, 0.09f, 1f);
        maskImage.raycastTarget = false;
        miniRoot.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        miniContentSize = mapData.worldSize * (miniRadius / minimapRange);
        miniContent = NewRect("Map Content", miniRoot);
        miniContent.sizeDelta = Vector2.one * miniContentSize;

        var map = miniContent.gameObject.AddComponent<RawImage>();
        map.texture = mapData.roadMap;
        map.raycastTarget = false;

        miniRoute = NewRoute("Route", miniContent, 7f);

        // Icons sit outside the rotating content so they stay upright and can clamp to the edge.
        var ring = NewImage("Ring", miniRoot.parent, ringSprite, new Color(0.82f, 0.82f, 0.82f, 1f), minimapSize + 4f);
        ring.anchorMin = ring.anchorMax = Vector2.zero;
        ring.anchoredPosition = miniRoot.anchoredPosition;

        miniMarker = NewImage("Marker", miniRoot.parent, diamondSprite, markerColor, 22f);
        Reparent(miniMarker, ring);
        miniTaxi = NewImage("Taxi", miniRoot.parent, circleSprite, taxiColor, 15f);
        Reparent(miniTaxi, ring);
        miniPlayer = NewImage("Player", miniRoot.parent, arrowSprite, Color.white, 26f);
        Reparent(miniPlayer, ring);
        miniPlayer.anchoredPosition = Vector2.zero;
        miniMarker.gameObject.SetActive(false);
        miniTaxi.gameObject.SetActive(false);

        northLabel = NewText("N", ring, 22, TextAnchor.MiddleCenter, Color.white).rectTransform;
        northLabel.sizeDelta = new Vector2(30f, 30f);
    }

    static void Reparent(RectTransform child, RectTransform parent)
    {
        child.SetParent(parent, false);
        child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
        child.anchoredPosition = Vector2.zero;
    }

    void BuildFullMap()
    {
        var panel = NewRect("Full Map", canvas.transform);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.96f);
        fullPanel = panel.gameObject;

        float side = 980f;
        fullMap = NewRect("Map", panel);
        fullMap.sizeDelta = Vector2.one * side;
        var map = fullMap.gameObject.AddComponent<RawImage>();
        map.texture = mapData.roadMap;
        map.raycastTarget = false;

        fullRoute = NewRoute("Route", fullMap, 5f);
        fullMarker = NewImage("Marker", fullMap, diamondSprite, markerColor, 30f);
        fullTaxi = NewImage("Taxi", fullMap, circleSprite, taxiColor, 18f);
        fullPlayer = NewImage("Player", fullMap, arrowSprite, Color.white, 30f);

        var help = NewText(MapHelp, panel, 26, TextAnchor.LowerCenter, Color.white);
        fullMapHelp = help;
        help.rectTransform.anchorMin = new Vector2(0f, 0f);
        help.rectTransform.anchorMax = new Vector2(1f, 0f);
        help.rectTransform.pivot = new Vector2(0.5f, 0f);
        help.rectTransform.sizeDelta = new Vector2(0f, 40f);
        help.rectTransform.anchoredPosition = new Vector2(0f, 6f);

        fullPanel.SetActive(false);
    }

    void BuildMessages()
    {
        messageText = NewText("", canvas.transform, 34, TextAnchor.MiddleCenter, Color.white);
        var m = messageText.rectTransform;
        m.anchorMin = m.anchorMax = new Vector2(0.5f, 1f);
        m.pivot = new Vector2(0.5f, 1f);
        m.sizeDelta = new Vector2(1400f, 60f);
        m.anchoredPosition = new Vector2(0f, -70f);
        AddShadow(messageText);
        messageText.enabled = false;

        hintText = NewText("M: open map", canvas.transform, 26, TextAnchor.LowerCenter, Color.white);
        var h = hintText.rectTransform;
        h.anchorMin = h.anchorMax = new Vector2(0.5f, 0f);
        h.pivot = new Vector2(0.5f, 0f);
        h.sizeDelta = new Vector2(1200f, 44f);
        h.anchoredPosition = new Vector2(0f, 36f);
        AddShadow(hintText);
    }

    static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    UIRouteLine NewRoute(string name, RectTransform parent, float thickness)
    {
        var rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = parent.sizeDelta;
        var route = rect.gameObject.AddComponent<UIRouteLine>();
        route.thickness = thickness;
        route.color = routeColor;
        route.raycastTarget = false;
        return route;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static RectTransform NewImage(string name, Transform parent, Sprite sprite, Color color, float size)
    {
        var rect = NewRect(name, parent);
        rect.sizeDelta = Vector2.one * size;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    Text NewText(string content, Transform parent, int size, TextAnchor anchor, Color color)
    {
        var rect = NewRect("Text", parent);
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

    static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte a = (byte)(Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size)) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
