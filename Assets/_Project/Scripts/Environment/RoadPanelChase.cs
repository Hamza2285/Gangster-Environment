using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Makes the glowing crossing panels at the road junctions light one after another along each strip (a chaser with a
    /// soft trail) instead of all at once. The road shaders are Hamza's; this swaps the listed road renderers to copies of
    /// their materials that use our shader copies ("OpenworldCrime/Road ... (Panel Chase)"), which differ only in the
    /// crossing-panel emission. The swap is runtime only: one material copy per source material (made once, never per
    /// frame), the scene and the material assets are not changed, and the originals come back when this is disabled.
    /// Lightmaps, static batching and the bake are untouched (same renderers, same lightmap indices).
    /// The chase itself runs on the GPU (_Time); there is no Update. Lives under ///Manager/// (not a Singleton).
    /// </summary>
    [DisallowMultipleComponent]
    public class RoadPanelChase : MonoBehaviour
    {
        [Serializable]
        public class ShaderPair
        {
            public Shader source;
            public Shader chase;
        }

        [Header("Renderers and shaders")]
        [Tooltip("Road renderers that use one of the source shaders (filled by Tools > OpenworldCrime > Lighting > Set Up Road Panel Chase).")]
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        [SerializeField] private ShaderPair[] shaders = new ShaderPair[0];

        [Header("Chase")]
        [Tooltip("Panels per second the light travels.")]
        [SerializeField, Range(0.5f, 40f)] private float speed = 9f;
        [Tooltip("Panels behind the head that still glow (fading).")]
        [SerializeField, Range(0.2f, 12f)] private float trail = 3f;
        [Tooltip("Dark panels between one wave and the next.")]
        [SerializeField, Range(0f, 40f)] private float gap = 10f;
        [Tooltip("Brightness of panels the wave is not passing (0 = off, 1 = as before).")]
        [SerializeField, Range(0f, 1f)] private float idleBrightness = 0.1f;
        [Tooltip("Length of one panel along a strip in the road texture tile (matches the crossing texture).")]
        [SerializeField, Range(0.01f, 0.2f)] private float panelPitch = 0.0331f;

        private static readonly int SpeedId = Shader.PropertyToID("_ChaseSpeed");
        private static readonly int TrailId = Shader.PropertyToID("_ChaseTrail");
        private static readonly int GapId = Shader.PropertyToID("_ChaseGap");
        private static readonly int IdleId = Shader.PropertyToID("_ChaseIdle");
        private static readonly int PitchId = Shader.PropertyToID("_ChasePanelPitch");

        private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        private Material[][] originals;

        private void OnEnable()
        {
            originals = new Material[renderers.Length][];
            int swapped = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Material[] mats = r.sharedMaterials;
                originals[i] = mats;
                Material[] replaced = null;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material copy = ChaseCopy(mats[m]);
                    if (copy == null) continue;
                    if (replaced == null) replaced = (Material[])mats.Clone();
                    replaced[m] = copy;
                }
                if (replaced == null) continue;
                r.sharedMaterials = replaced;
                swapped++;
            }
            if (swapped == 0 && renderers.Length > 0) Debug.LogWarning($"[{nameof(RoadPanelChase)}] No renderer on '{name}' uses a source shader.", this);
        }

        private void OnDisable()
        {
            if (originals == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && originals[i] != null) renderers[i].sharedMaterials = originals[i];
            originals = null;
        }

        private void OnDestroy()
        {
            foreach (Material copy in copies.Values) if (copy != null) Destroy(copy);
            copies.Clear();
        }

        private void OnValidate()
        {
            foreach (Material copy in copies.Values) if (copy != null) ApplyTuning(copy);
        }

        private Material ChaseCopy(Material source)
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out Material cached)) return cached;
            Shader chase = null;
            for (int i = 0; i < shaders.Length; i++)
                if (shaders[i].source == source.shader) chase = shaders[i].chase;
            if (chase == null) return null;
            var copy = new Material(source) { name = source.name + " (Panel Chase)", shader = chase };
            ApplyTuning(copy);
            copies.Add(source, copy);
            return copy;
        }

        private void ApplyTuning(Material m)
        {
            m.SetFloat(SpeedId, speed);
            m.SetFloat(TrailId, trail);
            m.SetFloat(GapId, gap);
            m.SetFloat(IdleId, idleBrightness);
            m.SetFloat(PitchId, panelPitch);
        }
    }
}
