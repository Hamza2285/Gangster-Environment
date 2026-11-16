using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// A point of interest on the city map: static places (gun shop, garage...) or moving ones (police units), optionally
    /// flashing between two colours, or an area of a world radius (the police search area). Registers itself while enabled;
    /// <see cref="MapBlipOverlay"/> draws it on the minimap and the full map. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapBlip : MonoBehaviour
    {
        [SerializeField] private Sprite icon;
        [SerializeField] private Color iconColour = Color.white;
        [SerializeField] private Color backgroundColour = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        [SerializeField, Min(4f)] private float size = 26f;

        [Header("Moving / Flashing / Area")]
        [Tooltip("The icon follows this object (units, vehicles). Off for places that never move: they cost nothing per frame.")]
        [SerializeField] private bool tracksMovement;
        [Tooltip("The background alternates with Flash Colour (police lights).")]
        [SerializeField] private bool flashes;
        [SerializeField] private Color flashColour = new Color(0.1f, 0.35f, 1f, 0.95f);
        [SerializeField, Min(0.05f)] private float flashInterval = 0.4f;
        [Tooltip("Above 0 the blip is an area of this radius in world metres (Size is ignored). Areas track movement.")]
        [SerializeField, Min(0f)] private float worldRadius;

        private static readonly List<MapBlip> active = new List<MapBlip>();

        public static IReadOnlyList<MapBlip> Active => active;
        public static event Action<MapBlip> OnBlipAdded;
        public static event Action<MapBlip> OnBlipRemoved;

        public Sprite Icon => icon;
        public Color IconColour => iconColour;
        public Color BackgroundColour => backgroundColour;
        public float Size => size;
        public bool TracksMovement => tracksMovement || worldRadius > 0f;
        public bool Flashes => flashes;
        public Color FlashColour => flashColour;
        public float FlashInterval => flashInterval;
        public float WorldRadius => worldRadius;

        /// <summary>Changes an area blip's radius (world metres); the overlay picks it up next frame.</summary>
        public void SetWorldRadius(float radius) => worldRadius = Mathf.Max(0f, radius);

        private void OnEnable()
        {
            active.Add(this);
            OnBlipAdded?.Invoke(this);
        }

        private void OnDisable()
        {
            if (active.Remove(this)) OnBlipRemoved?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
            OnBlipAdded = null;
            OnBlipRemoved = null;
        }
    }
}
