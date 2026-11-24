using OpenworldCrime.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// The car radio bar (visuals only; the state lives in <see cref="RadioManager"/>): a small semi-transparent bar at the top
    /// of the screen with three station logos - the centre one is on air (larger, opaque, soft glow), its neighbours smaller and
    /// dimmed - and two arrow buttons (prev / next) that call the manager. The ring loops: the stations, then a Radio Off slot,
    /// then the first station again, so the neighbours of the last station are the Off slot and the first station.
    /// - Station change: the logos slide one slot (about 0.2 s) toward the new centre instead of swapping.
    /// - Shown only while the player is in a vehicle (fades in / out); hidden (SetActive false) otherwise.
    /// - Stays clear of the HUD panels that can share the top of the screen (Avoid list, e.g. a mission panel): while one of
    ///   them is visible the bar sits just below it. Respects the device safe area.
    /// Five logo images are reused for the whole ring (no instantiation); per frame only while sliding or fading.
    /// </summary>
    public class RadioStationView : MonoBehaviour
    {
        [SerializeField] private RadioManager radio;
        [Tooltip("The bar root (shown / hidden).")]
        [SerializeField] private RectTransform bar;
        [Tooltip("Fades the bar in and out (alpha only).")]
        [SerializeField] private CanvasGroup fader;
        [Tooltip("Logo images in slot order -2, -1, 0 (centre), +1, +2; the outer two are only seen while sliding.")]
        [SerializeField] private Image[] slots = new Image[5];
        [SerializeField] private Sprite offIcon;
        [Tooltip("Shown under the centre logo.")]
        [SerializeField] private Text stationName;

        [Header("Look")]
        [SerializeField, Min(10f)] private float slotSpacing = 112f;
        [SerializeField, Min(10f)] private float centreSize = 104f;
        [SerializeField, Min(10f)] private float sideSize = 72f;
        [SerializeField, Range(0f, 1f)] private float sideAlpha = 0.45f;
        [SerializeField, Min(0.05f)] private float slideSeconds = 0.2f;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.25f;
        [SerializeField] private string offText = "RADIO OFF";
        [Tooltip("The station name under the bar shows this long after a change (then hides, so the bar stays compact and clear " +
                 "of panels below it, e.g. a mission objective).")]
        [SerializeField, Min(0.5f)] private float nameSeconds = 2.5f;

        [Header("Placement")]
        [Tooltip("Distance from the top of the screen (canvas units).")]
        [SerializeField] private float topMargin = 16f;
        [Tooltip("Panels that may use the top-centre of the screen; while one is visible the bar moves below it.")]
        [SerializeField] private RectTransform[] avoid = new RectTransform[0];

        private int ringIndex;      // centre of the ring now (stations 0..n-1, n = Off)
        private float slide;        // -1..1 offset (in slots) being animated back to 0
        private float slideStartAt;
        private float slideFrom;
        private float alpha, alphaTarget;
        private float nextPlacement;
        private float nameUntil;
        private Canvas canvas;
        private readonly Vector3[] corners = new Vector3[4];

        private int RingCount => radio.StationCount + 1;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
        }

        private void OnEnable()
        {
            if (radio == null || bar == null || fader == null || slots.Length != 5) { Debug.LogError($"[{nameof(RadioStationView)}] Missing references on '{name}'.", this); enabled = false; return; }
            radio.OnStationChanged += HandleStationChanged;
            radio.OnRadioActiveChanged += HandleActiveChanged;
            ringIndex = ToRing(radio.CurrentIndex);
            alpha = alphaTarget = radio.IsActive ? 1f : 0f;
            fader.alpha = alpha;
            bar.gameObject.SetActive(radio.IsActive);
            if (stationName != null) stationName.gameObject.SetActive(false);
            Layout();
        }

        private void OnDisable()
        {
            if (radio == null) return;
            radio.OnStationChanged -= HandleStationChanged;
            radio.OnRadioActiveChanged -= HandleActiveChanged;
        }

        // ------------------------------------------------------------------ buttons (wired in the Inspector)

        public void Next() => radio.NextStation();
        public void Previous() => radio.PreviousStation();

        // ------------------------------------------------------------------ events

        private void HandleActiveChanged(bool active)
        {
            alphaTarget = active ? 1f : 0f;
            if (active)
            {
                ringIndex = ToRing(radio.CurrentIndex);
                slide = 0f;
                bar.gameObject.SetActive(true);
                nextPlacement = 0f;
                ShowName();
                Layout();
            }
            fader.interactable = fader.blocksRaycasts = active;
        }

        private void HandleStationChanged(RadioStation station)
        {
            int target = ToRing(radio.CurrentIndex);
            int n = RingCount;
            int step = ((target - ringIndex) % n + n) % n;
            int dir = step == 0 ? 0 : step <= n / 2 ? 1 : -1;
            ringIndex = target;
            if (dir != 0 && bar.gameObject.activeInHierarchy)
            {
                // The new centre was one slot to the side: start there and slide in.
                slideFrom = Mathf.Clamp(slide + dir, -1.5f, 1.5f);
                slide = slideFrom;
                slideStartAt = Time.unscaledTime;
            }
            ShowName();
            Layout();
        }

        // ------------------------------------------------------------------ per frame (only while animating)

        private void Update()
        {
            bool changed = false;
            if (!Mathf.Approximately(alpha, alphaTarget))
            {
                alpha = Mathf.MoveTowards(alpha, alphaTarget, Time.unscaledDeltaTime / fadeSeconds);
                fader.alpha = alpha;
                if (alpha <= 0f && alphaTarget <= 0f) bar.gameObject.SetActive(false);
            }
            if (slide != 0f)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - slideStartAt) / slideSeconds);
                float ease = 1f - (1f - k) * (1f - k) * (1f - k);
                slide = Mathf.Lerp(slideFrom, 0f, ease);
                if (k >= 1f) slide = 0f;
                changed = true;
            }
            if (Time.unscaledTime >= nextPlacement && bar.gameObject.activeInHierarchy)
            {
                nextPlacement = Time.unscaledTime + 0.3f;
                Place();
            }
            if (nameUntil > 0f && Time.unscaledTime >= nameUntil)
            {
                nameUntil = 0f;
                if (stationName != null) stationName.gameObject.SetActive(false);
            }
            if (changed) Layout();
        }

        private void ShowName()
        {
            if (stationName == null) return;
            stationName.gameObject.SetActive(true);
            nameUntil = Time.unscaledTime + nameSeconds;
        }

        // Slot i (-2..+2) shows ring entry centre+i, drawn at (i + slide) slots from the middle.
        private void Layout()
        {
            int n = RingCount;
            for (int i = -2; i <= 2; i++)
            {
                Image img = slots[i + 2];
                if (img == null) continue;
                int entry = ((ringIndex + i) % n + n) % n;
                RadioStation s = radio.GetStation(entry);
                img.sprite = s != null ? s.Logo : offIcon;
                float pos = i + slide;
                float d = Mathf.Min(1f, Mathf.Abs(pos));
                float size = Mathf.Lerp(centreSize, sideSize, d);
                float a = Mathf.Abs(pos) <= 1f ? Mathf.Lerp(1f, sideAlpha, d) : Mathf.Lerp(sideAlpha, 0f, Mathf.Abs(pos) - 1f);
                RectTransform r = img.rectTransform;
                r.anchoredPosition = new Vector2(pos * slotSpacing, 0f);
                r.sizeDelta = new Vector2(size, size);
                img.color = new Color(1f, 1f, 1f, a);
                if (i == 0) r.SetAsLastSibling(); // centre drawn on top
            }
            if (stationName != null)
            {
                RadioStation on = radio.GetStation(ringIndex);
                stationName.text = on != null ? on.DisplayName.ToUpperInvariant() : offText;
            }
        }

        // Top-centre inside the safe area, below any visible panel of the Avoid list.
        private void Place()
        {
            RectTransform parent = bar.parent as RectTransform;
            if (parent == null) return;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            float safeTop = (Screen.height - Screen.safeArea.yMax) / Mathf.Max(0.01f, scale);
            float y = -(topMargin + safeTop);
            float barHalfWidth = bar.rect.width * 0.5f;
            foreach (RectTransform r in avoid)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                Graphic g = r.GetComponent<Graphic>();
                if (g != null && !g.enabled) continue;
                r.GetWorldCorners(corners);
                Vector3 bottomLeft = parent.InverseTransformPoint(corners[0]);
                Vector3 topRight = parent.InverseTransformPoint(corners[2]);
                if (topRight.x < -barHalfWidth || bottomLeft.x > barHalfWidth) continue; // not over the bar
                // Bottom of that panel, measured from the top of the parent.
                float below = bottomLeft.y - parent.rect.yMax - 10f;
                if (below < y) y = below;
            }
            bar.anchoredPosition = new Vector2(0f, y);
        }

        private int ToRing(int stationIndex) => stationIndex >= 0 ? stationIndex : radio.StationCount;

#if UNITY_EDITOR
        public void EditorSetup(RadioManager manager, RectTransform barRoot, CanvasGroup group, Image[] logoSlots, Sprite off, Text label, RectTransform[] avoidPanels)
        {
            radio = manager; bar = barRoot; fader = group; slots = logoSlots; offIcon = off; stationName = label; avoid = avoidPanels;
        }
#endif
    }
}
