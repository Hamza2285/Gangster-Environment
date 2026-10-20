using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Gives a car one colour of the <see cref="CarPaintPalette"/> by swapping its body paint material slots to that colour.
    /// A slot can carry the car's own body texture (panel lines / AO atlas) and normal map: the colour is then painted over that
    /// texture through a runtime material shared by every car of the same model and colour (one material per model x colour,
    /// so cars stay SRP-Batcher friendly and no per-car copies are made). Traffic cars pick a random colour each time they spawn
    /// or are reused from the pool; when a traffic car is stolen, its drivable counterpart gets the same colour.
    /// Only the listed paint slots are touched; light slots are read back by VehicleLights and left alone.
    /// </summary>
    public class CarPaintVariant : MonoBehaviour
    {
        [Serializable]
        public struct PaintSlot
        {
            public Renderer renderer;
            public int materialIndex;
            [Tooltip("Optional body texture (white/grey panel atlas) painted over by the palette colour.")]
            public Texture bodyTexture;
            public Texture bodyNormal;
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly Dictionary<(Material, Texture, Texture), Material> textured = new Dictionary<(Material, Texture, Texture), Material>();

        [SerializeField] private CarPaintPalette palette;
        [SerializeField] private PaintSlot[] slots = new PaintSlot[0];
        [Tooltip("Pick a random colour whenever the car is enabled (spawn / pool reuse).")]
        [SerializeField] private bool randomizeOnEnable = true;
        [Tooltip("Colour used when not randomizing (each model has its own).")]
        [SerializeField] private int defaultIndex;

        public int CurrentIndex { get; private set; } = -1;

        // Colour handed over by a takeover before this car was enabled (a pooled drivable is activated after the swap event).
        private int pendingIndex = -1;

        private void OnEnable()
        {
            if (palette == null || palette.Count == 0)
            {
                Debug.LogWarning($"[{nameof(CarPaintVariant)}] No paint palette on '{name}'.", this);
                return;
            }
            int index = pendingIndex >= 0 ? pendingIndex : randomizeOnEnable ? UnityEngine.Random.Range(0, palette.Count) : defaultIndex;
            pendingIndex = -1;
            SetIndex(index);
        }

        /// <summary>Paints the car with palette colour <paramref name="index"/>.</summary>
        public void SetIndex(int index)
        {
            if (palette == null || palette.Count == 0) return;
            CurrentIndex = Mathf.Clamp(index, 0, palette.Count - 1);
            Material paint = palette.Get(CurrentIndex);
            for (int i = 0; i < slots.Length; i++)
            {
                Renderer r = slots[i].renderer;
                if (r == null) continue;
                Material target = GetMaterial(paint, slots[i].bodyTexture, slots[i].bodyNormal);
                Material[] buffer = r.sharedMaterials;
                if (slots[i].materialIndex < 0 || slots[i].materialIndex >= buffer.Length || buffer[slots[i].materialIndex] == target) continue;
                buffer[slots[i].materialIndex] = target;
                r.sharedMaterials = buffer;
            }
        }

        private static Material GetMaterial(Material paint, Texture body, Texture normal)
        {
            if (paint == null || (body == null && normal == null)) return paint;
            var key = (paint, body, normal);
            if (textured.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(paint) { name = paint.name + "_" + (body != null ? body.name : normal.name), hideFlags = HideFlags.DontSave };
            if (body != null) m.SetTexture(BaseMapId, body);
            if (normal != null) m.SetTexture(BumpMapId, normal);
            textured[key] = m;
            return m;
        }

        // A stolen traffic car keeps its colour on the drivable car that replaces it.
        private static void HandleTakenOver(TrafficVehicleAdapter traffic, RCCVehicleAdapter drivable)
        {
            if (traffic == null || drivable == null) return;
            var from = traffic.GetComponent<CarPaintVariant>();
            var to = drivable.GetComponent<CarPaintVariant>();
            if (from == null || to == null || from.CurrentIndex < 0) return;
            to.pendingIndex = from.CurrentIndex; // survives the OnEnable that follows when the pooled car is activated
            if (to.isActiveAndEnabled) { to.pendingIndex = -1; to.SetIndex(from.CurrentIndex); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            textured.Clear();
            TrafficVehicleAdapter.OnTakenOver -= HandleTakenOver;
            TrafficVehicleAdapter.OnTakenOver += HandleTakenOver;
        }

#if UNITY_EDITOR
        public void EditorSetup(CarPaintPalette newPalette, PaintSlot[] newSlots, bool randomize, int newDefaultIndex = 0)
        {
            palette = newPalette; slots = newSlots; randomizeOnEnable = randomize; defaultIndex = newDefaultIndex;
        }
#endif
    }
}
