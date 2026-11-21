using UnityEngine;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// Keeps a full-screen RectTransform inside the device's safe area (notches, rounded corners, gesture bars). Put the
    /// interactive UI under it; full-bleed art (video, loading artwork) stays outside. Re-checks only when the safe area or the
    /// screen size changes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect applied;
        private Vector2Int screen;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != applied || screen.x != Screen.width || screen.y != Screen.height) Apply();
        }

        private void Apply()
        {
            applied = Screen.safeArea;
            screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0) return;
            Vector2 min = applied.position, max = applied.position + applied.size;
            min.x /= screen.x; min.y /= screen.y;
            max.x /= screen.x; max.y /= screen.y;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
