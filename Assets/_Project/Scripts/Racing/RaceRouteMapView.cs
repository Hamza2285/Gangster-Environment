using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// GPS route of the race on the minimap and the full map (the city's GameMapUI): a purple line along the race route, under
    /// the map icons; the part the player has already driven is taken off as he goes (shown again for the next lap). Thin UI
    /// segments built once when a race starts and removed when it ends; updated twice a second.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceRouteMapView : MonoBehaviour
    {
        [SerializeField] private RaceManager manager;
        [Tooltip("Path of the minimap's rotating content under the GameMapUI object.")]
        [SerializeField] private string minimapContentPath = "Game HUD/Minimap/Map Content";
        [Tooltip("Path of the full map image under the GameMapUI object.")]
        [SerializeField] private string fullMapPath = "Game HUD/Full Map/Map Viewport/Map";
        [SerializeField] private Color lineColour = new Color(0.86f, 0.38f, 1f, 0.95f);
        [Tooltip("Line width on the minimap / full map (UI units).")]
        [SerializeField, Min(1f)] private float minimapWidth = 5f;
        [SerializeField, Min(1f)] private float fullMapWidth = 6f;
        [Tooltip("Metres of route per line segment.")]
        [SerializeField, Min(5f)] private float step = 20f;

        private class Segment
        {
            public int routeIndex;
            public GameObject mini, full;
        }

        private readonly List<Segment> segments = new List<Segment>();
        private GameMapUI map;
        private RectTransform miniContent, fullMap;
        private float nextRefresh;
        private bool built;

        private void Start()
        {
            map = GameMapUI.Instance;
            if (manager == null || map == null || map.mapData == null)
            {
                Debug.LogWarning($"[{nameof(RaceRouteMapView)}] No race manager or GameMapUI; the race route is not drawn on the map.", this);
                enabled = false;
                return;
            }
            miniContent = map.transform.Find(minimapContentPath) as RectTransform;
            fullMap = map.transform.Find(fullMapPath) as RectTransform;
            manager.OnStateChanged += HandleState;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.OnStateChanged -= HandleState;
        }

        private void HandleState(RaceState state)
        {
            bool on = state == RaceState.Countdown || state == RaceState.Racing;
            if (on && !built) Build(manager.CurrentRace);
            if (!on) Clear();
            enabled = on;
        }

        private void Build(RaceDefinition race)
        {
            Clear();
            if (race == null) return;
            Vector3[] route = race.RoutePoints;
            int last = 0;
            float run = 0f;
            for (int i = 1; i <= route.Length; i++)
            {
                int k = i == route.Length ? (race.IsCircuit ? 0 : -1) : i;
                if (k < 0) break;
                run += Vector3.Distance(route[i - 1], route[k]);
                if (run < step && i < route.Length) continue;
                var seg = new Segment { routeIndex = last };
                seg.mini = Line(miniContent, route[last], route[k], minimapWidth);
                seg.full = Line(fullMap, route[last], route[k], fullMapWidth);
                segments.Add(seg);
                last = k;
                run = 0f;
            }
            built = true;
        }

        private GameObject Line(RectTransform parent, Vector3 from, Vector3 to, float width)
        {
            if (parent == null) return null;
            Rect bounds = parent.rect;
            Vector2 a = (map.mapData.WorldToUV(from) - new Vector2(0.5f, 0.5f)) * bounds.size;
            Vector2 b = (map.mapData.WorldToUV(to) - new Vector2(0.5f, 0.5f)) * bounds.size;
            var go = new GameObject("Race Route", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetAsFirstSibling(); // under the map icons
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = a;
            Vector2 d = b - a;
            rect.sizeDelta = new Vector2(d.magnitude + width * 0.5f, width);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var image = go.GetComponent<Image>();
            image.color = lineColour;
            image.raycastTarget = false;
            return go;
        }

        private void Clear()
        {
            foreach (Segment s in segments)
            {
                if (s.mini != null) Destroy(s.mini);
                if (s.full != null) Destroy(s.full);
            }
            segments.Clear();
            built = false;
        }

        // The driven part goes; on a circuit it comes back for the next lap (the player's index starts again near 0).
        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.5f;
            int at = manager.PlayerRouteIndex;
            foreach (Segment s in segments)
            {
                bool show = s.routeIndex >= at - 2;
                if (s.mini != null && s.mini.activeSelf != show) s.mini.SetActive(show);
                if (s.full != null && s.full.activeSelf != show) s.full.SetActive(show);
            }
        }
    }
}
