using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Gives a final car one of its paint colours by swapping its body material for a shared colour variant (made by
    /// Tools > OpenworldCrime > Vehicles > Apply Final Car Paint: one material asset per model and colour on the
    /// OpenworldCrime/Car Paint shader). Shared assets, no MaterialPropertyBlocks and no runtime copies, so the SRP Batcher keeps
    /// batching cars of the same model and colour.
    /// - Traffic cars (Pick On Enable) roll a colour every time iTS spawns or reuses them.
    /// - Drivable and parked cars roll once when created; a traffic car handed over to its drivable twin (carjack, bail-out,
    ///   shot driver) passes its colour on through <see cref="ApplyVariant"/>.
    /// Variant 0 is the model's own colour. A wreck's burnt tint (VehicleHealth, per-renderer block) works on every variant.
    /// Colours are picked with the relative weights of <see cref="VehiclePaintSettings"/> (black, white, silver and grey
    /// common, bright colours rare), looked up by each variant's colour name; without settings every colour is equally likely.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehiclePaintVariants : MonoBehaviour
    {
        [Serializable]
        public struct Slot
        {
            public Renderer renderer;
            public int materialIndex;
        }

        [Tooltip("Every renderer slot that shows the body paint material.")]
        [SerializeField] private Slot[] slots = new Slot[0];
        [Tooltip("Colour variants of the body material. Element 0 = the model's own colour.")]
        [SerializeField] private Material[] variants = new Material[0];
        [Tooltip("Colour name of each variant (same order as Variants), used to look up its weight in Paint Settings.")]
        [SerializeField] private string[] colourNames = new string[0];
        [Tooltip("How common each colour is. Empty = all colours equally likely.")]
        [SerializeField] private VehiclePaintSettings paintSettings;
        [Tooltip("Roll a new colour every time the object is enabled (pooled traffic). Off = once, when created.")]
        [SerializeField] private bool pickOnEnable;

        /// <summary>Index of the colour shown now (-1 before the first pick).</summary>
        public int CurrentIndex { get; private set; } = -1;
        public int Count => variants.Length;

        private void Awake()
        {
            if (variants.Length == 0 || slots.Length == 0)
            {
                Debug.LogWarning($"[{nameof(VehiclePaintVariants)}] '{name}' has no colour variants or no paint slots; it keeps its material. Re-run Apply Final Car Paint.", this);
                enabled = false;
                return;
            }
            if (!pickOnEnable && CurrentIndex < 0) ApplyVariant(PickIndex());
        }

        private void OnEnable()
        {
            if (pickOnEnable && variants.Length > 0) ApplyVariant(PickIndex());
        }

        /// <summary>A random colour index, weighted by the paint settings (uniform without them).</summary>
        public int PickIndex()
        {
            float total = 0f;
            for (int i = 0; i < variants.Length; i++) total += GetWeight(i);
            if (total <= 0f) return UnityEngine.Random.Range(0, variants.Length);
            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < variants.Length; i++)
            {
                roll -= GetWeight(i);
                if (roll < 0f) return i;
            }
            return variants.Length - 1;
        }

        private float GetWeight(int index)
        {
            if (variants[index] == null) return 0f;
            if (paintSettings == null || index >= colourNames.Length) return 1f;
            return paintSettings.GetWeight(colourNames[index]);
        }

        /// <summary>Shows colour <paramref name="index"/> (ignored when out of range).</summary>
        public void ApplyVariant(int index)
        {
            if (index < 0 || index >= variants.Length || variants[index] == null) return;
            CurrentIndex = index;
            Material material = variants[index];
            for (int i = 0; i < slots.Length; i++)
            {
                Renderer r = slots[i].renderer;
                if (r == null) continue;
                Material[] materials = r.sharedMaterials;
                int m = slots[i].materialIndex;
                if (m < 0 || m >= materials.Length || materials[m] == material) continue;
                materials[m] = material;
                r.sharedMaterials = materials;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Slot[] newSlots, Material[] newVariants, string[] newColourNames, VehiclePaintSettings settings, bool rollOnEnable)
        {
            slots = newSlots;
            variants = newVariants;
            colourNames = newColourNames;
            paintSettings = settings;
            pickOnEnable = rollOnEnable;
        }
#endif
    }
}
