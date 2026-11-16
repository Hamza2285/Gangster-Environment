using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Colour theme for the city map (GameMapUI) without changing it: the minimap's ground / road colours are handed to
    /// GameMapUI before it bakes its minimap texture (this runs first), and the few fixed colours it builds at runtime (minimap
    /// ring and north label, full map background, side bar, help strip, road image) are re-tinted once in Start. Blips,
    /// markers, the route and the player arrow keep their own colours. Runs once; nothing per frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class MapColorTheme : MonoBehaviour
    {
        [SerializeField] private GameMapUI map;

        [Header("Minimap (baked into its texture)")]
        [SerializeField] private Color minimapGround = new Color(0.64f, 0.6f, 0.75f, 1f);
        [SerializeField] private Color minimapGroundShade = new Color(0.33f, 0.28f, 0.44f, 1f);
        [SerializeField] private Color minimapRoad = new Color(0.9f, 0.88f, 0.95f, 1f);

        [Header("Accents")]
        [SerializeField] private Color ring = new Color(0.68f, 0.5f, 0.95f, 1f);
        [SerializeField] private Color northLabel = new Color(0.88f, 0.76f, 1f, 1f);

        [Header("Full Map")]
        [Tooltip("Multiplies the grey road map: roads become light lavender, blocks dark violet.")]
        [SerializeField] private Color fullMapTint = new Color(0.9f, 0.85f, 1f, 1f);
        [SerializeField] private Color fullMapBackground = new Color(0.11f, 0.09f, 0.15f, 1f);
        [SerializeField] private Color sideBar = new Color(0.14f, 0.11f, 0.2f, 1f);
        [SerializeField] private Color findPlayerButton = new Color(0.3f, 0.22f, 0.42f, 1f);
        [SerializeField] private Color helpStrip = new Color(0.07f, 0.04f, 0.11f, 0.8f);

        // Objects GameMapUI builds under its own transform (same convention as MapBlipOverlay).
        private const string RingPath = "Game HUD/Ring";
        private const string NorthLabelPath = "Game HUD/Ring/Text";
        private const string FullMapPath = "Game HUD/Full Map";
        private const string FullMapImagePath = "Game HUD/Full Map/Map Viewport/Map";
        private const string SideBarPath = "Game HUD/Full Map/Side Bar";
        private const string FindPlayerPath = "Game HUD/Full Map/Side Bar/Find Player";
        private const string HelpStripPath = "Game HUD/Full Map/Help Strip";

        private void Awake()
        {
            if (map == null)
            {
                Debug.LogWarning($"[{nameof(MapColorTheme)}] No GameMapUI assigned on '{name}'; the map keeps its default colours.", this);
                enabled = false;
                return;
            }
            map.minimapGroundColor = minimapGround;
            map.minimapGroundShade = minimapGroundShade;
            map.minimapRoadColor = minimapRoad;
        }

        private void Start()
        {
            int missing = 0;
            missing += Tint<Image>(RingPath, ring);
            missing += Tint<Text>(NorthLabelPath, northLabel);
            missing += Tint<Image>(FullMapPath, fullMapBackground);
            missing += Tint<RawImage>(FullMapImagePath, fullMapTint);
            missing += Tint<Image>(SideBarPath, sideBar);
            missing += Tint<Image>(FindPlayerPath, findPlayerButton);
            missing += Tint<Image>(HelpStripPath, helpStrip);
            if (missing > 0) Debug.LogWarning($"[{nameof(MapColorTheme)}] {missing} map element(s) not found (GameMapUI hierarchy changed); they keep their default colours.", this);
        }

        private int Tint<T>(string path, Color color) where T : Graphic
        {
            Transform target = map.transform.Find(path);
            T graphic = target != null ? target.GetComponent<T>() : null;
            if (graphic == null) return 1;
            graphic.color = color;
            return 0;
        }
    }
}
