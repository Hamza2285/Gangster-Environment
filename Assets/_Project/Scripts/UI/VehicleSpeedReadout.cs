using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Digital speed readout shown while the player drives: a white 3-digit number (leading zeros dimmed) with a light-grey
    /// "KM/H" label, floating a little to the left of and above the player's car on screen. The anchor is the car's projected
    /// position plus a fixed offset, smoothed so it never jitters, pushed out of the HUD elements listed in Keep Clear Of (radio
    /// bar, minimap, health, money, on-screen controls) and clamped inside the safe area. Shown/hidden from the vehicle
    /// interaction's entered/exited events; LateUpdate only runs while shown. The number's text only changes when the whole
    /// km/h value changes (cached strings, no per-frame allocation). Not shown for aircraft (<see cref="IFlyingVehicle"/>).
    /// A HUD widget script: it only drives its own elements.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleSpeedReadout : MonoBehaviour
    {
        private const int MaxShownSpeed = 999;

        [Header("References")]
        [Tooltip("Vehicle interaction of every playable character (character switching): whoever drives gets the readout.")]
        [SerializeField] private PlayerVehicleInteraction[] players = new PlayerVehicleInteraction[0];
        [Tooltip("The readout's visual root (number + label). Shown while driving, its pivot is its right-centre edge.")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private Text speedText;
        [Tooltip("Canvas the panel lives on (Screen Space Overlay): its scale factor turns the reference-pixel offsets into pixels.")]
        [SerializeField] private Canvas canvas;

        [Header("Placement")]
        [Tooltip("Height (m) above the car's root of the point the readout follows (about the car's mid height).")]
        [SerializeField, Min(0f)] private float carAnchorHeight = 1.1f;
        [Tooltip("Offset (reference pixels at 1920x1080) from the car's projected point to the readout's right edge: left of the car and a little up.")]
        [SerializeField] private Vector2 screenOffset = new Vector2(-330f, 70f);
        [Tooltip("Where the readout sits (viewport 0-1, its right edge) when the car is not on screen (bonnet camera, cut-scenes).")]
        [SerializeField] private Vector2 fallbackViewport = new Vector2(0.36f, 0.62f);
        [Tooltip("Smoothing time (s) of the readout's movement. Higher = calmer, but lags more on hard turns.")]
        [SerializeField, Range(0.02f, 0.5f)] private float smoothTime = 0.14f;
        [Tooltip("Gap (reference pixels) kept from the safe-area edges.")]
        [SerializeField, Min(0f)] private float edgeMargin = 40f;

        [Header("Keep Clear Of")]
        [Tooltip("HUD elements in this scene the readout never overlaps (inactive ones are ignored).")]
        [SerializeField] private RectTransform[] keepClearOf = new RectTransform[0];
        [Tooltip("HUD elements other systems spawn at runtime (Invector's HUD and inventory, the minimap), as 'Root/Child/...' paths. " +
                 "Looked up when the player gets into a car until found (no per-frame search), then treated like Keep Clear Of.")]
        [SerializeField] private string[] keepClearOfSpawned = new string[0];
        [Tooltip("Gap (reference pixels) kept around those elements.")]
        [SerializeField, Min(0f)] private float clearance = 16f;

        [Header("Look")]
        [Tooltip("Colour of the leading zeros of the 3-digit number (dim, digital-display look).")]
        [SerializeField] private Color leadingZeroColor = new Color(1f, 1f, 1f, 0.22f);

        private string[] speedStrings;
        private int shownSpeed = -1;
        private Vector2 position, velocity;
        private bool placed;
        private Transform car;
        private IVehicleController vehicle;
        private readonly Vector3[] corners = new Vector3[4];
        private RectTransform[] spawned = new RectTransform[0];

        private void Awake()
        {
            if (players.Length == 0 || panel == null || speedText == null)
            {
                Debug.LogError($"[{nameof(VehicleSpeedReadout)}] Missing references on '{name}' (players, panel, speed text).", this);
                enabled = false;
                return;
            }
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            BuildStrings();
            spawned = new RectTransform[keepClearOfSpawned.Length];
            IVehicleController driven = null;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].OnVehicleEntered += HandleEntered;
                players[i].OnVehicleExited += HandleExited;
                if (players[i].IsInVehicle && players[i].isActiveAndEnabled) driven = players[i].CurrentVehicle;
            }
            Show(driven);
        }

        // Subscribed for the object's lifetime: the component itself switches off while hidden.
        private void OnDestroy()
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].OnVehicleEntered -= HandleEntered;
                players[i].OnVehicleExited -= HandleExited;
            }
        }

        private void HandleEntered(IVehicleController entered) => Show(entered);

        private void HandleExited(IVehicleController exited)
        {
            if (exited == null || exited == vehicle || vehicle == null) Show(null);
        }

        private void Show(IVehicleController driven)
        {
            // Helicopter / plane: the flight instruments show speed and altitude instead.
            if (driven is IFlyingVehicle) driven = null;
            vehicle = driven;
            car = driven != null ? driven.transform : null;
            bool visible = car != null;
            panel.gameObject.SetActive(visible);
            placed = false;
            shownSpeed = -1;
            enabled = visible;
            if (visible) ResolveSpawned();
        }

        // Once per entry, only for paths not found yet: these canvases are created by other systems after the scene loads.
        private void ResolveSpawned()
        {
            for (int i = 0; i < keepClearOfSpawned.Length; i++)
            {
                if (spawned[i] != null || string.IsNullOrEmpty(keepClearOfSpawned[i])) continue;
                GameObject found = GameObject.Find(keepClearOfSpawned[i]);
                if (found != null) spawned[i] = found.transform as RectTransform;
            }
        }

        private void LateUpdate()
        {
            if (car == null || !car.gameObject.activeInHierarchy)
            {
                Show(null);
                return;
            }

            int kmh = Mathf.Clamp(Mathf.RoundToInt(vehicle.CurrentSpeed * 3.6f), 0, MaxShownSpeed);
            if (kmh != shownSpeed)
            {
                shownSpeed = kmh;
                speedText.text = speedStrings[kmh];
            }

            float scale = canvas != null ? canvas.scaleFactor : 1f;
            Vector2 target = Target(scale);
            if (!placed)
            {
                position = target;
                velocity = Vector2.zero;
                placed = true;
            }
            else position = Vector2.SmoothDamp(position, target, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            panel.position = new Vector3(position.x, position.y, 0f);
        }

        private Vector2 Target(float scale)
        {
            Camera view = Camera.main;
            Vector2 target;
            Vector3 screen = view != null ? view.WorldToScreenPoint(car.position + car.up * carAnchorHeight) : Vector3.zero;
            if (view != null && screen.z > 0.5f && screen.x > 0f && screen.x < Screen.width && screen.y > 0f && screen.y < Screen.height)
                target = new Vector2(screen.x, screen.y) + screenOffset * scale;
            else
                target = new Vector2(fallbackViewport.x * Screen.width, fallbackViewport.y * Screen.height);

            // Panel size in pixels around its pivot (right-centre by default).
            Vector2 size = panel.rect.size * scale;
            Vector2 pivot = panel.pivot;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < keepClearOf.Length; i++)
                {
                    RectTransform other = keepClearOf[i];
                    if (other == null || !other.gameObject.activeInHierarchy) continue;
                    target = PushOut(target, size, pivot, other, clearance * scale);
                }
                for (int i = 0; i < spawned.Length; i++)
                {
                    RectTransform other = spawned[i];
                    if (other == null || !other.gameObject.activeInHierarchy) continue;
                    target = PushOut(target, size, pivot, other, clearance * scale);
                }
                target = ClampToSafeArea(target, size, pivot, edgeMargin * scale);
            }
            return target;
        }

        // Moves the panel the shortest way out of the other element's screen rectangle (overlay canvases: world = pixels).
        private Vector2 PushOut(Vector2 target, Vector2 size, Vector2 pivot, RectTransform other, float gap)
        {
            other.GetWorldCorners(corners);
            float oMinX = Mathf.Min(corners[0].x, corners[2].x) - gap, oMaxX = Mathf.Max(corners[0].x, corners[2].x) + gap;
            float oMinY = Mathf.Min(corners[0].y, corners[2].y) - gap, oMaxY = Mathf.Max(corners[0].y, corners[2].y) + gap;
            float minX = target.x - size.x * pivot.x, maxX = minX + size.x;
            float minY = target.y - size.y * pivot.y, maxY = minY + size.y;
            if (maxX <= oMinX || minX >= oMaxX || maxY <= oMinY || minY >= oMaxY) return target;

            float left = maxX - oMinX, right = oMaxX - minX, down = maxY - oMinY, up = oMaxY - minY;
            float best = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
            if (best == left) target.x -= left;
            else if (best == right) target.x += right;
            else if (best == down) target.y -= down;
            else target.y += up;
            return target;
        }

        private static Vector2 ClampToSafeArea(Vector2 target, Vector2 size, Vector2 pivot, float margin)
        {
            Rect safe = Screen.safeArea;
            float minX = safe.xMin + margin + size.x * pivot.x, maxX = safe.xMax - margin - size.x * (1f - pivot.x);
            float minY = safe.yMin + margin + size.y * pivot.y, maxY = safe.yMax - margin - size.y * (1f - pivot.y);
            target.x = maxX > minX ? Mathf.Clamp(target.x, minX, maxX) : (safe.xMin + safe.xMax) * 0.5f;
            target.y = maxY > minY ? Mathf.Clamp(target.y, minY, maxY) : (safe.yMin + safe.yMax) * 0.5f;
            return target;
        }

        // "007" style: leading zeros dimmed through rich text. Built once.
        private void BuildStrings()
        {
            string dim = ColorUtility.ToHtmlStringRGBA(leadingZeroColor);
            speedStrings = new string[MaxShownSpeed + 1];
            for (int i = 0; i <= MaxShownSpeed; i++)
            {
                string digits = i.ToString("000");
                int zeros = i < 10 ? 2 : i < 100 ? 1 : 0;
                speedStrings[i] = zeros > 0
                    ? "<color=#" + dim + ">" + digits.Substring(0, zeros) + "</color>" + digits.Substring(zeros)
                    : digits;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(PlayerVehicleInteraction[] interactions, RectTransform newPanel, Text newSpeedText, Canvas newCanvas, RectTransform[] clearOf, string[] clearOfSpawned)
        {
            players = interactions; panel = newPanel; speedText = newSpeedText; canvas = newCanvas; keepClearOf = clearOf; keepClearOfSpawned = clearOfSpawned;
        }
#endif
    }
}
