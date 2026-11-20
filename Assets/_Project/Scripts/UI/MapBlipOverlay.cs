using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Adapter that draws <see cref="MapBlip"/>s on the existing city map (GameMapUI) without changing it: the icons are
    /// parented under GameMapUI's rotating minimap content and its full-map image, so they move, rotate, zoom and get masked
    /// with the map. Only the counter-rotation / counter-scale is updated per frame (a handful of icons), plus the position,
    /// area size and flash colour of the few moving blips (police units, the search area).
    /// </summary>
    [DisallowMultipleComponent]
    public class MapBlipOverlay : MonoBehaviour
    {
        [Tooltip("Path of the minimap's rotating content under the GameMapUI object.")]
        [SerializeField] private string minimapContentPath = "Game HUD/Minimap/Map Content";
        [Tooltip("Path of the full map image under the GameMapUI object.")]
        [SerializeField] private string fullMapPath = "Game HUD/Full Map/Map Viewport/Map";
        [Tooltip("Background disc sprite behind each icon.")]
        [SerializeField] private Sprite discSprite;
        [Tooltip("Full map icons are drawn this much larger than on the minimap.")]
        [SerializeField, Min(0.1f)] private float fullMapScale = 1.3f;

        private class BlipIcons
        {
            public MapBlip blip;
            public RectTransform mini;
            public RectTransform full;
            public Image miniBackground;
            public Image fullBackground;
        }

        private readonly Dictionary<MapBlip, BlipIcons> icons = new Dictionary<MapBlip, BlipIcons>();
        private readonly List<BlipIcons> iconList = new List<BlipIcons>();
        private GameMapUI map;
        private RectTransform miniContent;
        private RectTransform fullMap;

        private void Start()
        {
            map = GameMapUI.Instance;
            if (map == null || map.mapData == null)
            {
                Debug.LogWarning($"[{nameof(MapBlipOverlay)}] No GameMapUI in the scene; map blips are off.", this);
                enabled = false;
                return;
            }
            miniContent = map.transform.Find(minimapContentPath) as RectTransform;
            fullMap = map.transform.Find(fullMapPath) as RectTransform;
            if (miniContent == null && fullMap == null)
            {
                Debug.LogWarning($"[{nameof(MapBlipOverlay)}] GameMapUI hierarchy changed (paths not found); map blips are off.", this);
                enabled = false;
                return;
            }
            MapBlip.OnBlipAdded += Add;
            MapBlip.OnBlipRemoved += Remove;
            IReadOnlyList<MapBlip> existing = MapBlip.Active;
            for (int i = 0; i < existing.Count; i++) Add(existing[i]);
        }

        private void OnDestroy()
        {
            MapBlip.OnBlipAdded -= Add;
            MapBlip.OnBlipRemoved -= Remove;
        }

        private void LateUpdate()
        {
            float fullScale = fullMap != null && fullMap.localScale.x > 0.0001f ? fullMapScale / fullMap.localScale.x : 1f;
            for (int i = 0; i < iconList.Count; i++)
            {
                BlipIcons entry = iconList[i];
                MapBlip blip = entry.blip;
                bool area = blip.WorldRadius > 0f;
                if (entry.mini != null) entry.mini.rotation = Quaternion.identity;
                // Areas keep their world size on the full map (they scale with the map's zoom like the map itself).
                if (entry.full != null) entry.full.localScale = area ? Vector3.one : Vector3.one * fullScale;
                if (blip.TracksMovement)
                {
                    Place(blip, entry.mini, miniContent);
                    Place(blip, entry.full, fullMap);
                }
                if (blip.Flashes)
                {
                    bool alternate = Mathf.Repeat(Time.unscaledTime, blip.FlashInterval * 2f) >= blip.FlashInterval;
                    Color colour = alternate ? blip.FlashColour : blip.BackgroundColour;
                    if (entry.miniBackground != null) entry.miniBackground.color = colour;
                    if (entry.fullBackground != null) entry.fullBackground.color = colour;
                }
            }
        }

        private void Add(MapBlip blip)
        {
            if (blip == null || icons.ContainsKey(blip)) return;
            var entry = new BlipIcons
            {
                blip = blip,
                mini = miniContent != null ? CreateIcon(blip, miniContent) : null,
                full = fullMap != null ? CreateIcon(blip, fullMap) : null,
            };
            if (entry.mini != null) entry.miniBackground = entry.mini.GetComponent<Image>();
            if (entry.full != null) entry.fullBackground = entry.full.GetComponent<Image>();
            icons.Add(blip, entry);
            iconList.Add(entry);
        }

        private void Remove(MapBlip blip)
        {
            if (blip == null || !icons.TryGetValue(blip, out BlipIcons entry)) return;
            if (entry.mini != null) Destroy(entry.mini.gameObject);
            if (entry.full != null) Destroy(entry.full.gameObject);
            icons.Remove(blip);
            iconList.Remove(entry);
        }

        private RectTransform CreateIcon(MapBlip blip, RectTransform parent)
        {
            var root = new GameObject("Blip " + blip.name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = Vector2.one * blip.Size;
            Place(blip, rect, parent);
            if (blip.WorldRadius > 0f) rect.SetAsFirstSibling(); // areas under the other icons
            var background = root.GetComponent<Image>();
            background.sprite = blip.BackgroundSprite != null ? blip.BackgroundSprite : discSprite;
            background.color = blip.BackgroundColour;
            background.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.one * blip.Size * 0.14f;
            iconRect.offsetMax = -Vector2.one * blip.Size * 0.14f;
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = blip.Icon;
            icon.color = blip.IconColour;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = blip.Icon != null;
            return rect;
        }

        // Map position (and, for areas, size from the world radius) of a blip on one of the two maps.
        private void Place(MapBlip blip, RectTransform rect, RectTransform parent)
        {
            if (rect == null) return;
            Vector3 world = blip.transform.position;
            Vector2 uv = map.mapData.WorldToUV(world) - new Vector2(0.5f, 0.5f);
            Rect bounds = parent.rect;
            rect.anchoredPosition = new Vector2(uv.x * bounds.width, uv.y * bounds.height);
            if (blip.WorldRadius <= 0f) return;
            Vector2 edge = map.mapData.WorldToUV(world + Vector3.right * blip.WorldRadius) - new Vector2(0.5f, 0.5f);
            float radius = Mathf.Abs(edge.x - uv.x) * bounds.width;
            rect.sizeDelta = Vector2.one * (radius * 2f);
        }
    }
}
