using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// A static point of interest on the city map (gun shop, garage...). Registers itself while enabled;
    /// <see cref="MapBlipOverlay"/> draws it on the minimap and the full map. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapBlip : MonoBehaviour
    {
        [SerializeField] private Sprite icon;
        [SerializeField] private Color iconColour = Color.white;
        [SerializeField] private Color backgroundColour = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        [SerializeField, Min(4f)] private float size = 26f;

        private static readonly List<MapBlip> active = new List<MapBlip>();

        public static IReadOnlyList<MapBlip> Active => active;
        public static event Action<MapBlip> OnBlipAdded;
        public static event Action<MapBlip> OnBlipRemoved;

        public Sprite Icon => icon;
        public Color IconColour => iconColour;
        public Color BackgroundColour => backgroundColour;
        public float Size => size;

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
