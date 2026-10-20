using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// The car paint colours, as shared materials on the OpenworldCrime/Vehicle Paint shader (one material per colour, shared by
    /// every car of that colour, so cars stay SRP-Batcher friendly and no per-car material copies are made).
    /// Created and filled by Tools > OpenworldCrime > Vehicles > Setup Car Paint; edit the materials to restyle a colour.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Car Paint Palette", fileName = "CarPaintPalette")]
    public class CarPaintPalette : ScriptableObject
    {
        [SerializeField] private Material[] paints = new Material[0];

        public int Count => paints.Length;
        public Material Get(int index) => paints.Length == 0 ? null : paints[Mathf.Clamp(index, 0, paints.Length - 1)];

#if UNITY_EDITOR
        public void EditorSetPaints(Material[] newPaints) => paints = newPaints;
#endif
    }
}
