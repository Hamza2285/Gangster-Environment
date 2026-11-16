using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// One baked lighting result of the city (day or night): its lightmaps, its light probe colours and - only when the two
    /// bakes packed the renderers differently - the per-renderer lightmap layout. Pure bake output, written by
    /// Tools > OpenworldCrime > Lighting > Bake Day And Night and read by <see cref="BakedLightingSwitcher"/>; never changed at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Lighting/Baked Lighting Set", fileName = "BakedLightingSet")]
    public class BakedLightingSet : ScriptableObject
    {
        /// <summary>Floats per probe: 3 colour channels x 9 L2 coefficients.</summary>
        public const int FloatsPerProbe = 27;

        [Header("Lightmaps")]
        [SerializeField] private Texture2D[] lightmapColor = new Texture2D[0];
        [SerializeField] private Texture2D[] lightmapDirection = new Texture2D[0];

        [Header("Light Probes")]
        [Tooltip("Raw little-endian float32 L2 coefficients, 27 per probe, in the scene's baked probe order.")]
        [SerializeField] private TextAsset probeData;
        [SerializeField] private int probeCount;
        [Tooltip("Position of the first baked probe; used to find the city's probes when an interior's probes are merged in.")]
        [SerializeField] private Vector3 firstProbePosition;

        [Header("Renderer Layout (empty when both bakes share one layout)")]
        [SerializeField] private int[] rendererLightmapIndex = new int[0];
        [SerializeField] private Vector4[] rendererScaleOffset = new Vector4[0];

        [Header("Info")]
        [SerializeField, TextArea] private string bakeInfo;

        public int LightmapCount => lightmapColor.Length;
        public Texture2D GetLightmapColor(int index) => lightmapColor[index];
        public Texture2D GetLightmapDirection(int index) => index < lightmapDirection.Length ? lightmapDirection[index] : null;
        public bool HasProbes => probeData != null && probeCount > 0;
        public int ProbeCount => probeCount;
        public Vector3 FirstProbePosition => firstProbePosition;
        public bool HasOwnLayout => rendererLightmapIndex.Length > 0;
        public int[] RendererLightmapIndex => rendererLightmapIndex;
        public Vector4[] RendererScaleOffset => rendererScaleOffset;

        /// <summary>Decodes the probe colours (allocates; the switcher caches the result).</summary>
        public UnityEngine.Rendering.SphericalHarmonicsL2[] DecodeProbes()
        {
            if (!HasProbes) return null;
            byte[] bytes = probeData.bytes;
            if (bytes.Length < probeCount * FloatsPerProbe * 4)
            {
                Debug.LogError($"[{nameof(BakedLightingSet)}] '{name}' probe data is truncated; re-run the bake.", this);
                return null;
            }
            var result = new UnityEngine.Rendering.SphericalHarmonicsL2[probeCount];
            int offset = 0;
            for (int p = 0; p < probeCount; p++)
            {
                var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
                for (int rgb = 0; rgb < 3; rgb++)
                {
                    for (int c = 0; c < 9; c++)
                    {
                        sh[rgb, c] = System.BitConverter.ToSingle(bytes, offset);
                        offset += 4;
                    }
                }
                result[p] = sh;
            }
            return result;
        }

#if UNITY_EDITOR
        public void EditorSet(Texture2D[] color, Texture2D[] direction, TextAsset probes, int probeTotal, Vector3 firstProbe,
            int[] layoutIndex, Vector4[] layoutScaleOffset, string info)
        {
            lightmapColor = color;
            lightmapDirection = direction;
            probeData = probes;
            probeCount = probeTotal;
            firstProbePosition = firstProbe;
            rendererLightmapIndex = layoutIndex ?? new int[0];
            rendererScaleOffset = layoutScaleOffset ?? new Vector4[0];
            bakeInfo = info;
        }

        public void EditorClearLayout()
        {
            rendererLightmapIndex = new int[0];
            rendererScaleOffset = new Vector4[0];
        }
#endif
    }
}
