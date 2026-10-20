using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Gives an iTS traffic car a random paint from a <see cref="TrafficPaintPalette"/> every time it spawns
    /// (iTS re-enables pooled cars with SetActive, so OnEnable re-rolls the paint on every reuse).
    /// Only the body paint is recoloured: the car's body slot is swapped to a "paint-ready" copy of its material whose
    /// albedo has the paint areas baked to neutral grey and a paint mask in alpha (TrafficPaintBaker, editor). The paint colour is
    /// multiplied (x2) onto the masked pixels only, so interior, trim, lights, glass and wheels keep their original look:
    /// with the "OpenworldCrime/Vehicle Paint" shader through its _PaintColor (which also limits clear coat, metallic and
    /// smoothness to the paint), with URP Lit through its masked detail albedo (a 1x1 colour texture). Shared material assets are never modified: painted materials are runtime
    /// copies, one per (paint-ready material, paint) pair, shared by every car using that pair so batching still works.
    /// When the player takes the car over, the drivable counterpart gets the same paint (same body material on both).
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficCarPaint : MonoBehaviour
    {
        [Tooltip("Paints picked at random each time the car spawns.")]
        [SerializeField] private TrafficPaintPalette palette;
        [Tooltip("Renderer holding the car body paint (MergedBody).")]
        [SerializeField] private Renderer bodyRenderer;
        [Tooltip("Material slot of the body paint on the body renderer. Other slots (glass, lights, plates) are never touched.")]
        [SerializeField, Min(0)] private int paintSlot;
        [Tooltip("Paint-ready copy of this car's body material (baked by Tools > OpenworldCrime > Traffic > Bake Paint-Ready Materials).")]
        [SerializeField] private Material paintReadyMaterial;
        [Tooltip("Optional: the traffic adapter, so a taken-over car's drivable counterpart gets the same paint.")]
        [SerializeField] private TrafficVehicleAdapter adapter;

        private static readonly int DetailAlbedoMapId = Shader.PropertyToID("_DetailAlbedoMap");
        private static readonly int PaintColorId = Shader.PropertyToID("_PaintColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // Runtime-only caches (never saved): one painted material per (paint-ready material, palette, paint) and one
        // 1x1 colour texture per (palette, paint). Bounded by models x paints.
        private static readonly Dictionary<(Material, TrafficPaintPalette, int), Material> paintedMaterials = new Dictionary<(Material, TrafficPaintPalette, int), Material>();
        private static readonly Dictionary<(TrafficPaintPalette, int), Texture2D> paintTextures = new Dictionary<(TrafficPaintPalette, int), Texture2D>();
        private static readonly HashSet<Material> paintedSet = new HashSet<Material>();

        /// <summary>Index of the current paint in the palette, or -1 when the car shows its original material.</summary>
        public int CurrentPaintIndex { get; private set; } = -1;
        public string CurrentPaintName => CurrentPaintIndex >= 0 ? palette.Get(CurrentPaintIndex).name : "(original)";

        private Material originalMaterial;
        private Material currentMaterial;
        private bool isValid;

        private void Awake()
        {
            isValid = palette != null && palette.Count > 0 && bodyRenderer != null && paintReadyMaterial != null
                      && paintSlot < bodyRenderer.sharedMaterials.Length;
            if (!isValid)
            {
                Debug.LogWarning($"[{nameof(TrafficCarPaint)}] '{name}' is missing its palette, body renderer, paint-ready material " +
                                 "or has an invalid paint slot. The car keeps its original paint.", this);
                return;
            }
            originalMaterial = bodyRenderer.sharedMaterials[paintSlot];
        }

        private void OnEnable()
        {
            if (!isValid) return;
            ApplyPaint(Random.Range(0, palette.Count));
            if (adapter != null) TrafficVehicleAdapter.OnTakenOver += HandleTakenOver;
        }

        private void OnDisable()
        {
            if (adapter != null) TrafficVehicleAdapter.OnTakenOver -= HandleTakenOver;
        }

        private void ApplyPaint(int index)
        {
            currentMaterial = GetPaintedMaterial(paintReadyMaterial, palette, index);
            CurrentPaintIndex = index;
            Material[] mats = bodyRenderer.sharedMaterials;
            mats[paintSlot] = currentMaterial;
            bodyRenderer.sharedMaterials = mats;
        }

        // The drivable counterpart uses the same body material (body, door, steering wheel); swap it there as well so the
        // car does not change colour when the player gets in.
        private void HandleTakenOver(TrafficVehicleAdapter traffic, RCCVehicleAdapter drivable)
        {
            if (traffic != adapter || drivable == null || currentMaterial == null) return;
            foreach (Renderer r in drivable.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || (mats[i] != originalMaterial && !paintedSet.Contains(mats[i]))) continue;
                    mats[i] = currentMaterial;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static Material GetPaintedMaterial(Material paintReady, TrafficPaintPalette palette, int index)
        {
            var key = (paintReady, palette, index);
            if (paintedMaterials.TryGetValue(key, out Material m) && m != null) return m;

            TrafficPaintPalette.Paint paint = palette.Get(index);
            m = new Material(paintReady)
            {
                name = $"{paintReady.name} ({paint.name})",
                hideFlags = HideFlags.DontSave
            };
            // Vehicle Paint shader: same x2 convention as the detail albedo (0.5 grey x 2 x colour = colour on a lit panel).
            if (m.HasProperty(PaintColorId)) m.SetColor(PaintColorId, new Color(paint.color.r, paint.color.g, paint.color.b, 1f));
            else m.SetTexture(DetailAlbedoMapId, GetPaintTexture(palette, index, paint.color));
            m.SetFloat(MetallicId, paint.metallic);
            m.SetFloat(SmoothnessId, paint.smoothness);
            paintedMaterials[key] = m;
            paintedSet.Add(m);
            return m;
        }

        // Detail albedo is applied as albedo * (2 * detail) under the mask: the baked paint areas are 0.5 grey (times the
        // panel's baked shading), so 0.5 * 2 * colour gives exactly the paint colour on a fully lit panel.
        private static Texture2D GetPaintTexture(TrafficPaintPalette palette, int index, Color color)
        {
            var key = (palette, index);
            if (paintTextures.TryGetValue(key, out Texture2D tex) && tex != null) return tex;
            tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "TrafficPaint_" + index,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixel(0, 0, new Color(color.r, color.g, color.b, 1f));
            tex.Apply(false, true);
            paintTextures[key] = tex;
            return tex;
        }

        // Runtime copies are released when play mode / the app ends (also covers disabled domain reload in the editor).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            foreach (Material m in paintedMaterials.Values) if (m != null) Destroy(m);
            foreach (Texture2D t in paintTextures.Values) if (t != null) Destroy(t);
            paintedMaterials.Clear();
            paintTextures.Clear();
            paintedSet.Clear();
        }
    }
}
