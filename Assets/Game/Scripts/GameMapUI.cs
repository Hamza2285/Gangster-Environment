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
    public Color minimapGroundColor = new Color(0.58f, 0.76f, 0.52f, 1f);
    [Tooltip("The ground fades to this colour where it meets a road.")]
    public Color minimapGroundShade = new Color(0.3f, 0.42f, 0.3f, 1f);
    public Color minimapRoadColor = new Color(0.86f, 0.87f, 0.84f, 1f);
    [Tooltip("How soft the ground's edge is, in metres of world.")]
    public float minimapGroundBlur = 8f;

    [Header("Colors")]
    public Color routeArrowColor = new Color(0.15f, 0.95f, 0.3f, 1f);
    public Color markerColor = new Color(1f, 0.78f, 0.1f, 1f);
    public Color taxiColor = new Color(1f, 0.9f, 0.2f, 1f);

    public bool IsMapOpen { get; private set; }
    public bool ClosedThisFrame => closedFrame == Time.frameCount;
    public Transform TrackedTaxi { get; set; }

    bool chooseDestinationMode;
    int closedFrame = -1;
    Text fullMapHelp;
    const string MapHelp = "Left click: set destination     Drag: move map     Scroll: zoom     Right click: remove marker     M: close map";
    const string TaxiMapHelp = "Left click: choose destination     Drag: move map     Scroll: zoom     Right click: remove marker     Esc: go";

    const float MaxFullZoom = 6f, FindPlayerZoom = 3f;
    const float FindPingSeconds = 1.2f;
    const float DragThreshold = 6f;

    Canvas canvas;
    RectTransform miniContent, miniRoot, miniPlayer, miniPlayerOutline, miniMarker, miniTaxi, northLabel;
    UIRouteLine miniRoute;
    float miniContentSize;
    float miniRadius;

    GameObject fullPanel;
    RectTransform fullViewport, fullMap, fullPlayer, fullMarker, fullTaxi, fullPing;
    RectTransform findPlayerButton, zoomInButton, zoomOutButton, fullSliderArea, fullSliderTrack, fullSliderKnob;
    Image fullPingImage;
    UIRouteLine fullRoute;
    float fullZoom = 1f;
    // At zoom 1 the square map covers the screen; the lowest zoom fits the whole map on it.
    float fullMinZoom = 1f;
    Vector2 fittedViewSize;
    Vector2 fullPan;
    float findPingLeft;
    bool draggingSlider, pressingMap, movedMap;
    Vector2 pressPointer, pressPan;

    Text messageText, hintText;
    float messageUntil;
    Sprite circleSprite, ringSprite, arrowSprite, diamondSprite, personSprite;
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
        personSprite = MakeSprite(64, (u, v) =>
        {
            // Head and shoulders.
            bool head = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.72f)) < 0.2f;
            float bodyX = (u - 0.5f) / 0.36f, bodyY = (v - 0.08f) / 0.42f;
            bool body = v > 0.08f && bodyX * bodyX + bodyY * bodyY < 1f;
            return head || body ? 1f : 0f;
        });

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
        var player = GamePlayer.Current;
        if (player != null && GameInput.MapDown && (IsMapOpen || player.ControlEnabled)) ToggleMap();
        else if (IsMapOpen && GameInput.CancelDown) ToggleMap();

        if (IsMapOpen)
        {
            if (fullViewport.rect.size != fittedViewSize)
            {
                // The window changed shape while the map is open.
                FitMapToScreen();
                SetFullPan(fullPan);
                routeDirty = true;
            }
            HandleMapClicks();
        }
        if (routeDirty) RebuildRoutes();
        if (player != null)
        {
            UpdateMinimap(player.transform);
            if (IsMapOpen) UpdateFullMap(player.transform);
        }
        messageText.enabled = Time.unscaledTime < messageUntil;
        hintText.enabled = !IsMapOpen;
        canvas.enabled = !GamePlayer.MenuOpen;
    }

    void ToggleMap()
    {
        IsMapOpen = !IsMapOpen;
        fullPanel.SetActive(IsMapOpen);
        draggingSlider = pressingMap = false;
        if (IsMapOpen)
        {
            FitMapToScreen();
            CenterOnPlayer();
        }
        if (!IsMapOpen) closedFrame = Time.frameCount;
        var player = GamePlayer.Current;
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
        Vector2 pointer = GameInput.PointerPosition;
        if (GameInput.SecondaryClickDown && WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();

        if (GameInput.PrimaryClickDown)
        {
            if (Over(findPlayerButton, pointer)) FindPlayer();
            else if (Over(zoomInButton, pointer)) SetFullZoom(fullZoom * 1.5f, Vector2.zero);
            else if (Over(zoomOutButton, pointer)) SetFullZoom(fullZoom / 1.5f, Vector2.zero);
            else if (Over(fullSliderArea, pointer)) draggingSlider = true;
            else if (Over(fullViewport, pointer))
            {
                pressingMap = true;
                movedMap = false;
                pressPointer = pointer;
                pressPan = fullPan;
            }
        }

        if (draggingSlider)
        {
            if (!GameInput.PrimaryHeld) draggingSlider = false;
            else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(fullSliderTrack, pointer, null, out Vector2 onTrack))
            {
                float t = Mathf.Clamp01(onTrack.y / fullSliderTrack.rect.height + 0.5f);
                SetFullZoom(fullMinZoom * Mathf.Pow(MaxFullZoom / fullMinZoom, t), Vector2.zero);
            }
        }

        // A press that doesn't move sets the destination; one that moves drags the map.
        if (pressingMap)
        {
            Vector2 moved = pointer - pressPointer;
            if (moved.magnitude > DragThreshold) movedMap = true;
            if (movedMap) SetFullPan(pressPan + moved / canvas.scaleFactor);
            if (!GameInput.PrimaryHeld)
            {
                pressingMap = false;
                if (!movedMap) PlaceMarker(pointer);
            }
        }

        float steps = GameInput.ScrollStep;
        if (steps != 0f)
        {
            Vector2 around = Vector2.zero;
            if (Over(fullViewport, pointer)) RectTransformUtility.ScreenPointToLocalPointInRectangle(fullViewport, pointer, null, out around);
            SetFullZoom(fullZoom * Mathf.Pow(1.25f, steps), around);
        }
        float keys = GameInput.ZoomKeys;
        if (keys != 0f) SetFullZoom(fullZoom * Mathf.Exp(keys * 1.5f * Time.unscaledDeltaTime), Vector2.zero);
    }

    static bool Over(RectTransform rect, Vector2 pointer)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null);
    }

    void PlaceMarker(Vector2 pointer)
    {
        var waypoints = WaypointSystem.Instance;
        if (waypoints == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(fullMap, pointer, null, out Vector2 local)) return;
        Rect r = fullMap.rect;
        Vector2 uv = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height);
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;
        waypoints.SetMarker(mapData.UVToWorld(uv));
    }

    void FindPlayer()
    {
        if (fullZoom < FindPlayerZoom) SetFullZoom(FindPlayerZoom, Vector2.zero);
        CenterOnPlayer();
        findPingLeft = FindPingSeconds;
    }

    void CenterOnPlayer()
    {
        var player = GamePlayer.Current;
        if (player != null) SetFullPan(-FullLocal(player.transform.position) * fullZoom);
    }

    // The screen shape can change between openings, so the map is sized each time it opens.
    void FitMapToScreen()
    {
        Rect view = fullViewport.rect;
        fittedViewSize = view.size;
        float side = Mathf.Max(view.width, view.height);
        fullMinZoom = Mathf.Min(view.width, view.height) / side;
        fullMap.sizeDelta = Vector2.one * side;
        fullRoute.rectTransform.sizeDelta = Vector2.one * side;
        fullZoom = Mathf.Clamp(fullZoom, fullMinZoom, MaxFullZoom);
        ApplyFullZoom();
    }

    // Keeps the map point under "around" (in viewport space) in the same place on screen.
    void SetFullZoom(float zoom, Vector2 around)
    {
        zoom = Mathf.Clamp(zoom, fullMinZoom, MaxFullZoom);
        Vector2 mapPoint = (around - fullPan) / fullZoom;
        fullZoom = zoom;
        SetFullPan(around - mapPoint * zoom);
        ApplyFullZoom();
    }

    void SetFullPan(Vector2 pan)
    {
        // Never lets the map's edge come inside the screen; a side smaller than the screen stays centred.
        float scaled = fullMap.rect.width * fullZoom;
        Rect view = fullViewport.rect;
        float limitX = Mathf.Max(0f, (scaled - view.width) * 0.5f);
        float limitY = Mathf.Max(0f, (scaled - view.height) * 0.5f);
        fullPan = new Vector2(Mathf.Clamp(pan.x, -limitX, limitX), Mathf.Clamp(pan.y, -limitY, limitY));
        fullMap.anchoredPosition = fullPan;
    }

    void ApplyFullZoom()
    {
        fullMap.localScale = Vector3.one * fullZoom;
        // Icons and arrows stay the same size on screen while the roads grow.
        Vector3 iconScale = Vector3.one / fullZoom;
        fullPlayer.localScale = fullMarker.localScale = fullTaxi.localScale = iconScale;
        fullRoute.SetSizeScale(1f / fullZoom);
        SetKnob(fullSliderKnob, fullSliderTrack, fullZoom, fullMinZoom, MaxFullZoom);
    }

    static void SetKnob(RectTransform knob, RectTransform track, float zoom, float min, float max)
    {
        float t = Mathf.Log(zoom / min) / Mathf.Log(max / min);
        knob.anchoredPosition = new Vector2(0f, (t - 0.5f) * track.rect.height);
    }

    void UpdateMinimap(Transform player)
    {
        Vector2 playerLocal = MiniLocal(player.position);
        float yaw = player.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(0f, 0f, yaw);
        // The map turns with the camera; the arrow shows which way she is facing.
        miniPlayer.localRotation = miniPlayerOutline.localRotation = Quaternion.Euler(0f, 0f, yaw - GamePlayer.Current.FacingYaw);
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
        fullPlayer.localRotation = Quaternion.Euler(0f, 0f, -GamePlayer.Current.FacingYaw);
        var waypoints = WaypointSystem.Instance;
        bool hasMarker = waypoints != null && waypoints.HasMarker;
        fullMarker.gameObject.SetActive(hasMarker);
        if (hasMarker) fullMarker.anchoredPosition = FullLocal(waypoints.MarkerPosition);
        fullTaxi.gameObject.SetActive(TrackedTaxi != null);
        if (TrackedTaxi != null) fullTaxi.anchoredPosition = FullLocal(TrackedTaxi.position);

        // A ring that spreads out from the player after pressing Find player.
        findPingLeft -= Time.unscaledDeltaTime;
        bool pinging = findPingLeft > 0f;
        fullPing.gameObject.SetActive(pinging);
        if (pinging)
        {
            float t = 1f - findPingLeft / FindPingSeconds;
            fullPing.anchoredPosition = fullPlayer.anchoredPosition;
            fullPing.localScale = Vector3.one * (1f + t * 5f) / fullZoom;
            var c = routeArrowColor;
            c.a = 1f - t;
            fullPingImage.color = c;
        }
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
        maskImage.color = minimapGroundColor;
        maskImage.raycastTarget = false;
        miniRoot.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        miniContentSize = mapData.worldSize * (miniRadius / minimapRange);
        miniContent = NewRect("Map Content", miniRoot);
        miniContent.sizeDelta = Vector2.one * miniContentSize;

        var map = miniContent.gameObject.AddComponent<RawImage>();
        map.texture = MakeMinimapTexture();
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
        // A dark edge keeps the white arrow visible on the light roads.
        miniPlayerOutline = NewImage("Player Outline", miniRoot.parent, arrowSprite, new Color(0f, 0f, 0f, 0.85f), 36f);
        Reparent(miniPlayerOutline, ring);
        miniPlayer = NewImage("Player", miniRoot.parent, arrowSprite, Color.white, 26f);
        Reparent(miniPlayer, ring);
        miniPlayer.anchoredPosition = Vector2.zero;
        miniMarker.gameObject.SetActive(false);
        miniTaxi.gameObject.SetActive(false);

        northLabel = NewText("N", ring, 22, TextAnchor.MiddleCenter, Color.white).rectTransform;
        northLabel.sizeDelta = new Vector2(30f, 30f);
    }

    // The road map with its empty ground painted a soft, blurry light green.
    Texture2D MakeMinimapTexture()
    {
        int size = Mathf.Min(mapData.roadMap.width, 1024);
        // The road map isn't readable in memory, so it is copied through a render texture.
        var copy = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(mapData.roadMap, copy);
        var previous = RenderTexture.active;
        RenderTexture.active = copy;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(copy);

        // Grey levels from GameplaySetup: 34 is empty ground, 168 is road.
        var pixels = texture.GetPixels32();
        var road = new float[pixels.Length];
        var ground = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            road[i] = Mathf.InverseLerp(34f, 168f, pixels[i].r);
            ground[i] = road[i] < 0.05f ? 1f : 0f;
        }
        Blur(ground, size, Mathf.Max(1, Mathf.RoundToInt(minimapGroundBlur / mapData.worldSize * size)));

        float patchScale = mapData.worldSize / size / 45f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                // A block's edge blurs to about 0.5, so that is where the fade starts.
                float soft = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, ground[i]));
                float patches = 0.75f + 0.25f * Mathf.PerlinNoise(x * patchScale, y * patchScale);
                Color color = Color.Lerp(minimapGroundShade, minimapGroundColor, soft * patches);
                pixels[i] = Color.Lerp(color, minimapRoadColor, road[i]);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Three box blurs each way, which comes out close to a gaussian.
    static void Blur(float[] values, int size, int radius)
    {
        var line = new float[size];
        for (int pass = 0; pass < 6; pass++)
        {
            bool across = pass % 2 == 0;
            for (int a = 0; a < size; a++)
            {
                for (int b = 0; b < size; b++) line[b] = values[across ? a * size + b : b * size + a];
                float sum = 0f;
                for (int b = -radius; b <= radius; b++) sum += line[Mathf.Clamp(b, 0, size - 1)];
                for (int b = 0; b < size; b++)
                {
                    values[across ? a * size + b : b * size + a] = sum / (radius * 2 + 1);
                    sum += line[Mathf.Min(b + radius + 1, size - 1)] - line[Mathf.Max(b - radius, 0)];
                }
            }
        }
    }

    void BarLabel(string content, RectTransform parent, float y, int size)
    {
        var label = NewText(content, parent, size, TextAnchor.MiddleCenter, Color.white).rectTransform;
        label.sizeDelta = new Vector2(40f, 40f);
        label.anchoredPosition = new Vector2(0f, y);
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
        bg.color = new Color(0.13f, 0.13f, 0.13f, 1f);
        fullPanel = panel.gameObject;
        // Its own canvas above everything else, so the map also covers and blocks the day/night buttons.
        var panelCanvas = panel.gameObject.AddComponent<Canvas>();
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = 1000;
        panel.gameObject.AddComponent<GraphicRaycaster>();

        fullViewport = NewRect("Map Viewport", panel);
        fullViewport.anchorMin = Vector2.zero;
        fullViewport.anchorMax = Vector2.one;
        fullViewport.offsetMin = fullViewport.offsetMax = Vector2.zero;
        fullViewport.gameObject.AddComponent<RectMask2D>();

        fullMap = NewRect("Map", fullViewport);
        fullMap.sizeDelta = Vector2.one * 980f;
        var map = fullMap.gameObject.AddComponent<RawImage>();
        map.texture = mapData.roadMap;
        map.raycastTarget = false;

        fullRoute = NewRoute("Route", fullMap, 7f);
        fullMarker = NewImage("Marker", fullMap, diamondSprite, markerColor, 30f);
        fullTaxi = NewImage("Taxi", fullMap, circleSprite, taxiColor, 18f);
        fullPing = NewImage("Find Ping", fullMap, ringSprite, routeArrowColor, 40f);
        fullPingImage = fullPing.GetComponent<Image>();
        fullPing.gameObject.SetActive(false);
        fullPlayer = NewImage("Player", fullMap, arrowSprite, Color.white, 30f);

        BuildFullMapSideBar(panel);
        ApplyFullZoom();

        var helpStrip = NewRect("Help Strip", panel);
        helpStrip.anchorMin = new Vector2(0f, 0f);
        helpStrip.anchorMax = new Vector2(1f, 0f);
        helpStrip.pivot = new Vector2(0.5f, 0f);
        helpStrip.sizeDelta = new Vector2(0f, 48f);
        var stripImage = helpStrip.gameObject.AddComponent<Image>();
        stripImage.color = new Color(0f, 0f, 0f, 0.75f);
        stripImage.raycastTarget = false;

        var help = NewText(MapHelp, panel, 26, TextAnchor.LowerCenter, Color.white);
        fullMapHelp = help;
        help.rectTransform.anchorMin = new Vector2(0f, 0f);
        help.rectTransform.anchorMax = new Vector2(1f, 0f);
        help.rectTransform.pivot = new Vector2(0.5f, 0f);
        help.rectTransform.sizeDelta = new Vector2(0f, 40f);
        help.rectTransform.anchoredPosition = new Vector2(0f, 6f);

        fullPanel.SetActive(false);
    }

    void BuildFullMapSideBar(RectTransform panel)
    {
        var bar = NewRect("Side Bar", panel);
        bar.anchorMin = bar.anchorMax = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(96f, 520f);
        bar.anchoredPosition = new Vector2(-72f, 0f);
        bar.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.14f, 1f);

        findPlayerButton = NewImage("Find Player", bar, circleSprite, new Color(0.26f, 0.26f, 0.26f, 1f), 68f);
        findPlayerButton.anchoredPosition = new Vector2(0f, 205f);
        NewImage("Player Icon", findPlayerButton, personSprite, routeArrowColor, 44f);
        BarLabel("Find me", bar, 155f, 18);

        zoomInButton = NewRect("Zoom In", bar);
        zoomInButton.sizeDelta = new Vector2(60f, 44f);
        zoomInButton.anchoredPosition = new Vector2(0f, 112f);
        BarLabel("+", zoomInButton, 0f, 38);

        zoomOutButton = NewRect("Zoom Out", bar);
        zoomOutButton.sizeDelta = new Vector2(60f, 44f);
        zoomOutButton.anchoredPosition = new Vector2(0f, -222f);
        BarLabel("−", zoomOutButton, 0f, 38);

        fullSliderArea = NewRect("Zoom Slider", bar);
        fullSliderArea.sizeDelta = new Vector2(72f, 270f);
        fullSliderArea.anchoredPosition = new Vector2(0f, -55f);
        fullSliderTrack = NewImage("Track", fullSliderArea, null, new Color(0.6f, 0.6f, 0.6f, 1f), 1f);
        fullSliderTrack.sizeDelta = new Vector2(8f, 240f);
        fullSliderKnob = NewImage("Knob", fullSliderArea, circleSprite, Color.white, 30f);
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
        route.color = routeArrowColor;
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
