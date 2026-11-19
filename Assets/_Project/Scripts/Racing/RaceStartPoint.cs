using OpenworldCrime.UI;
using UnityEngine;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// A street race start in the city: the glowing marker on the road and the map / minimap icon. While the race is not
    /// available (wrong time of day) the marker is hidden and the icon is shown greyed (desaturated and see-through, still
    /// visible). The <see cref="RaceManager"/> owns the state and calls <see cref="SetAvailable"/>; no Update here.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceStartPoint : MonoBehaviour
    {
        [SerializeField] private RaceDefinition race;
        [Tooltip("Marker visuals on the road (hidden while unavailable or during a race).")]
        [SerializeField] private GameObject marker;
        [SerializeField] private MapBlip blip;

        [Header("Icon colours")]
        [SerializeField] private Color iconColour = new Color(1f, 0.86f, 0.35f, 1f);
        [SerializeField] private Color backgroundColour = new Color(0.42f, 0.2f, 0.66f, 0.95f);
        [Tooltip("How far the unavailable icon goes towards grey (0 = full colour, 1 = grey).")]
        [SerializeField, Range(0f, 1f)] private float greyAmount = 0.55f;
        [SerializeField, Range(0f, 1f)] private float greyAlpha = 0.6f;

        [Tooltip("Radius (m) the player's car has to be within to be offered the race.")]
        [SerializeField, Min(2f)] private float triggerRadius = 9f;

        public RaceDefinition Race => race;
        public float TriggerRadius => triggerRadius;
        public bool IsAvailable { get; private set; } = true;
        public Vector3 Position => transform.position;

        private bool markerShown = true;

        /// <summary>Availability by time of day (the icon greys out, the marker hides).</summary>
        public void SetAvailable(bool available)
        {
            IsAvailable = available;
            if (blip != null)
            {
                Color icon = available ? iconColour : Grey(iconColour);
                Color back = available ? backgroundColour : Grey(backgroundColour);
                blip.SetColours(icon, back);
            }
            RefreshMarker();
        }

        /// <summary>Hides the marker while any race is running.</summary>
        public void SetMarkerShown(bool shown)
        {
            markerShown = shown;
            RefreshMarker();
        }

        private void RefreshMarker()
        {
            if (marker != null) marker.SetActive(markerShown && IsAvailable);
        }

        private Color Grey(Color c)
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            Color g = Color.Lerp(c, new Color(l, l, l, c.a), greyAmount);
            g.a = c.a * greyAlpha;
            return g;
        }

        private void OnValidate()
        {
            if (race == null) Debug.LogWarning($"[{nameof(RaceStartPoint)}] '{name}' has no RaceDefinition.", this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, triggerRadius);
            if (race == null || race.RoutePoints.Length < 2) return;
            Vector3[] p = race.RoutePoints;
            for (int i = 1; i < p.Length; i++) Gizmos.DrawLine(p[i - 1] + Vector3.up, p[i] + Vector3.up);
            Gizmos.color = Color.magenta;
            foreach (int c in race.Checkpoints) if (c >= 0 && c < p.Length) Gizmos.DrawWireSphere(p[c] + Vector3.up * 3f, 3f);
        }
    }
}
