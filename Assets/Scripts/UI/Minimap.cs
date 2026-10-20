using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GTA-style circular minimap (local HUD widget, not a manager):
/// - the world is a baked top-down map texture (Tools > OpenworldCrime > Minimap > Bake Map) shown through a RawImage uvRect, so
///   nothing in the world is rendered again for the minimap;
/// - circular viewport (UI Mask), map either north-up or turning with the camera (Rotation Mode), player arrow showing the
///   player's facing, optional north label on the rim;
/// - markers: every enabled <see cref="MinimapMarker"/> is drawn from a pooled icon set with the look of its
///   <see cref="MinimapMarkerType"/> (objectives / missions can stay pinned to the rim when off-map), plus the full-screen map's
///   waypoint and the legacy blip API;
/// - put the moving parts under a nested Canvas (the Viewport) so their per-frame changes do not rebuild the whole HUD canvas.
/// Public fields are read by <see cref="FullScreenMap"/>.
/// </summary>
public class Minimap : MonoBehaviour
{
    public enum RotationMode { NorthUp, RotateWithCamera }

    public Transform target;
    [Tooltip("Heading the map turns with in Rotate With Camera mode. Empty = the active main camera (follows the vehicle camera too).")]
    public Transform headingSource;
    public RawImage mapImage;
    public RectTransform playerIcon;
    [Tooltip("World distance (m) from the centre to the rim of the minimap.")]
    public float viewRadius = 70f;

    [Header("Baked map (Tools > OpenworldCrime > Minimap > Bake Map)")]
    public Texture2D mapTexture;
    [Tooltip("Dark grayscale version used by the full-screen map.")]
    public Texture2D fullMapTexture;
    [Tooltip("World X/Z of the map texture's bottom-left corner.")]
    public Vector2 worldMin;
    [Tooltip("World width (and height) covered by the map texture, in metres.")]
    public float worldSize = 1f;

    [Header("Waypoint")]
    public RectTransform waypointIcon;
    [Tooltip("How far inside the minimap edge an off-map waypoint or edge-clamped marker is pinned, in UI units.")]
    public float waypointEdgeInset = 14f;

    [Header("Layout")]
    [SerializeField] private RotationMode rotationMode = RotationMode.RotateWithCamera;
    [Tooltip("Circular viewport (Mask). Its width defines the minimap radius in UI units.")]
    [SerializeField] private RectTransform viewport;
    [Tooltip("Turned with the camera in Rotate With Camera mode; parent of the map image and the marker layer.")]
    [SerializeField] private RectTransform rotator;
    [Tooltip("Parent of the pooled marker icons (inside the rotator).")]
    [SerializeField] private RectTransform markerLayer;
    [Tooltip("Optional 'N' label kept on the rim, pointing north.")]
    [SerializeField] private RectTransform northLabel;
    [SerializeField, Min(0f)] private float northLabelInset = 12f;
    [Tooltip("Icon used by the legacy AddBlip API.")]
    [SerializeField] private Sprite blipSprite;
    [Tooltip("Icons created up front so markers appearing later do not allocate.")]
    [SerializeField, Min(0)] private int prewarmIcons = 16;

    public bool HasWaypoint { get; private set; }
    public Vector2 Waypoint { get; private set; }
    public RotationMode Rotation => rotationMode;

    private class Icon
    {
        public RectTransform rect;
        public Image image;
    }

    private class Blip
    {
        public Transform target;
        public Icon icon;
        public Color colour, flashColour;
        public bool flashes;
    }

    private readonly Dictionary<MinimapMarker, Icon> markerIcons = new Dictionary<MinimapMarker, Icon>();
    private readonly List<MinimapMarker> markerKeys = new List<MinimapMarker>();
    private readonly List<Blip> blips = new List<Blip>();
    private readonly Stack<Icon> pool = new Stack<Icon>();
    private float mapRotation;

    public void SetWaypoint(Vector2 worldXZ)
    {
        Waypoint = worldXZ;
        HasWaypoint = true;
    }

    public void ClearWaypoint()
    {
        HasWaypoint = false;
    }

    /// <summary>Switches between north-up and camera-relative rotation (settings menu).</summary>
    public void SetRotationMode(RotationMode mode)
    {
        rotationMode = mode;
    }

    /// <summary>Legacy: a dot following this object; if flashColour differs from colour the dot alternates between them.</summary>
    public void AddBlip(Transform t, Color colour, Color flashColour, float size = 10f)
    {
        if (t == null || markerLayer == null) return;
        RemoveBlip(t);
        Icon icon = TakeIcon();
        icon.image.sprite = blipSprite;
        icon.rect.sizeDelta = new Vector2(size, size);
        icon.image.color = colour;
        blips.Add(new Blip { target = t, icon = icon, colour = colour, flashColour = flashColour, flashes = flashColour != colour });
    }

    public void RemoveBlip(Transform t)
    {
        for (int i = blips.Count - 1; i >= 0; i--)
        {
            if (blips[i].target != t && blips[i].target != null) continue;
            ReturnIcon(blips[i].icon);
            blips.RemoveAt(i);
        }
    }

    private void Awake()
    {
        if (target == null || mapImage == null || viewport == null || rotator == null || markerLayer == null)
        {
            Debug.LogError($"[{nameof(Minimap)}] Missing references on '{name}' (target, map image, viewport, rotator, marker layer). Minimap disabled.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < prewarmIcons; i++) ReturnIcon(CreateIcon());
    }

    private void Start()
    {
        if (mapImage != null) mapImage.texture = mapTexture;
    }

    private void OnEnable()
    {
        MinimapMarker.OnMarkerAdded += HandleMarkerAdded;
        MinimapMarker.OnMarkerRemoved += HandleMarkerRemoved;
        MinimapMarker.OnMarkerChanged += HandleMarkerChanged;
        IReadOnlyList<MinimapMarker> existing = MinimapMarker.Active;
        for (int i = 0; i < existing.Count; i++) HandleMarkerAdded(existing[i]);
    }

    private void OnDisable()
    {
        MinimapMarker.OnMarkerAdded -= HandleMarkerAdded;
        MinimapMarker.OnMarkerRemoved -= HandleMarkerRemoved;
        MinimapMarker.OnMarkerChanged -= HandleMarkerChanged;
        for (int i = 0; i < markerKeys.Count; i++) ReturnIcon(markerIcons[markerKeys[i]]);
        markerIcons.Clear();
        markerKeys.Clear();
    }

    private void HandleMarkerAdded(MinimapMarker marker)
    {
        if (marker == null || markerIcons.ContainsKey(marker) || markerLayer == null) return;
        Icon icon = TakeIcon();
        markerIcons.Add(marker, icon);
        markerKeys.Add(marker);
        ApplyType(marker, icon);
    }

    private void HandleMarkerRemoved(MinimapMarker marker)
    {
        if (marker == null || !markerIcons.TryGetValue(marker, out Icon icon)) return;
        ReturnIcon(icon);
        markerIcons.Remove(marker);
        markerKeys.Remove(marker);
    }

    private void HandleMarkerChanged(MinimapMarker marker)
    {
        if (marker != null && markerIcons.TryGetValue(marker, out Icon icon)) ApplyType(marker, icon);
    }

    private void ApplyType(MinimapMarker marker, Icon icon)
    {
        MinimapMarkerType type = marker.Type;
        if (type == null) return;
        icon.image.sprite = type.Icon;
        icon.image.color = type.Colour;
        icon.rect.sizeDelta = new Vector2(type.Size, type.Size);
        // Draw order: higher values last (on top). Marker counts are small, so a sibling sort on change is cheap.
        int index = 0;
        for (int i = 0; i < markerLayer.childCount; i++)
        {
            Transform child = markerLayer.GetChild(i);
            if (child == icon.rect || !child.gameObject.activeSelf) continue;
            MinimapMarker other = FindMarker(child);
            if (other != null && other.Type != null && other.Type.DrawOrder <= type.DrawOrder) index = i + 1;
        }
        icon.rect.SetSiblingIndex(Mathf.Min(index, markerLayer.childCount - 1));
    }

    private MinimapMarker FindMarker(Transform iconTransform)
    {
        for (int i = 0; i < markerKeys.Count; i++)
            if (markerIcons[markerKeys[i]].rect == iconTransform) return markerKeys[i];
        return null;
    }

    private void LateUpdate()
    {
        Vector3 p = target.position;
        float radius = viewport.rect.width * 0.5f;
        float unitsPerMetre = radius / Mathf.Max(1f, viewRadius);

        // Map: the image is larger than the viewport (corners covered while turning); its uvRect spans what it shows.
        Vector2 centre = (new Vector2(p.x, p.z) - worldMin) / worldSize;
        float imageMetres = mapImage.rectTransform.rect.width / unitsPerMetre;
        float span = imageMetres / worldSize;
        mapImage.uvRect = new Rect(centre.x - span * 0.5f, centre.y - span * 0.5f, span, span);

        Transform heading = headingSource;
        if (heading == null)
        {
            Camera cam = Camera.main;
            heading = cam != null ? cam.transform : target;
        }
        mapRotation = rotationMode == RotationMode.RotateWithCamera ? heading.eulerAngles.y : 0f;
        rotator.localEulerAngles = new Vector3(0f, 0f, mapRotation);

        if (playerIcon != null) playerIcon.localEulerAngles = new Vector3(0f, 0f, mapRotation - target.eulerAngles.y);
        if (northLabel != null)
        {
            float a = mapRotation * Mathf.Deg2Rad;
            northLabel.anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * (radius - northLabelInset);
        }

        float maxRadius = radius - waypointEdgeInset;
        for (int i = 0; i < markerKeys.Count; i++) UpdateMarker(markerKeys[i], markerIcons[markerKeys[i]], p, unitsPerMetre, radius, maxRadius);
        UpdateBlips(p, unitsPerMetre, radius);

        if (waypointIcon != null)
        {
            waypointIcon.gameObject.SetActive(HasWaypoint);
            if (HasWaypoint)
            {
                Vector2 offset = (Waypoint - new Vector2(p.x, p.z)) * unitsPerMetre;
                // Off-map waypoints stick to the rim so they still point the way.
                if (offset.magnitude > maxRadius) offset = offset.normalized * maxRadius;
                waypointIcon.anchoredPosition = Rotate(offset, mapRotation);
            }
        }
    }

    private void UpdateMarker(MinimapMarker marker, Icon icon, Vector3 p, float unitsPerMetre, float radius, float maxRadius)
    {
        MinimapMarkerType type = marker.Type;
        Transform t = marker.Target;
        Vector3 d3 = t.position - p;
        Vector2 offset = new Vector2(d3.x, d3.z);
        bool visible = type != null && (type.MaxDistance <= 0f || offset.sqrMagnitude <= type.MaxDistance * type.MaxDistance);
        offset *= unitsPerMetre;
        float length = offset.magnitude;
        if (visible && length > maxRadius)
        {
            if (type.ClampToEdge) offset *= maxRadius / length;
            else visible = length < radius;
        }
        if (icon.rect.gameObject.activeSelf != visible) icon.rect.gameObject.SetActive(visible);
        if (!visible) return;
        icon.rect.anchoredPosition = offset;
        // The marker layer turns with the map: upright icons are turned back, heading icons follow the object.
        float angle = type.RotateWithTarget ? -t.eulerAngles.y : -mapRotation;
        icon.rect.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private void UpdateBlips(Vector3 p, float unitsPerMetre, float radius)
    {
        if (blips.Count == 0) return;
        bool flashOn = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
        for (int i = blips.Count - 1; i >= 0; i--)
        {
            Blip b = blips[i];
            if (b.target == null)
            {
                ReturnIcon(b.icon);
                blips.RemoveAt(i);
                continue;
            }
            Vector2 offset = (new Vector2(b.target.position.x, b.target.position.z) - new Vector2(p.x, p.z)) * unitsPerMetre;
            bool inside = offset.magnitude < radius - 4f && b.target.gameObject.activeInHierarchy;
            b.icon.rect.gameObject.SetActive(inside);
            b.icon.rect.anchoredPosition = offset;
            b.icon.rect.localEulerAngles = new Vector3(0f, 0f, -mapRotation);
            b.icon.image.color = b.flashes && flashOn ? b.flashColour : b.colour;
        }
    }

    // Marker-layer offsets are in map space; the waypoint lives outside the rotator, so it is turned here.
    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private Icon TakeIcon()
    {
        Icon icon = pool.Count > 0 ? pool.Pop() : CreateIcon();
        icon.rect.gameObject.SetActive(true);
        return icon;
    }

    private void ReturnIcon(Icon icon)
    {
        if (icon == null || icon.rect == null) return;
        icon.rect.gameObject.SetActive(false);
        pool.Push(icon);
    }

    private Icon CreateIcon()
    {
        var go = new GameObject("MarkerIcon", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(markerLayer, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        return new Icon { rect = rect, image = image };
    }
}
