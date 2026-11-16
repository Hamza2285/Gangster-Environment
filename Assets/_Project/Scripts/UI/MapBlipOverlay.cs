using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Adapter that draws <see cref="MapBlip"/>s on the existing city map (GameMapUI) without changing it: the icons are
    /// parented under GameMapUI's rotating minimap content and its full-map image, so they move, rotate, zoom and get masked
    /// with the map. Only the counter-rotation / counter-scale is updated per frame (a handful of icons).
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
            public RectTransform mini;
            public RectTransform full;
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
                if (entry.mini != null) entry.mini.rotation = Quaternion.identity;
                if (entry.full != null) entry.full.localScale = Vector3.one * fullScale;
            }
        }

        private void Add(MapBlip blip)
        {
            if (blip == null || icons.ContainsKey(blip)) return;
            var entry = new BlipIcons
            {
                mini = miniContent != null ? CreateIcon(blip, miniContent) : null,
                full = fullMap != null ? CreateIcon(blip, fullMap) : null,
            };
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
            Vector2 uv = map.mapData.WorldToUV(blip.transform.position) - new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(uv.x * parent.rect.width, uv.y * parent.rect.height);
            var background = root.GetComponent<Image>();
            background.sprite = discSprite;
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
            return rect;
        }
    }
}
