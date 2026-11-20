using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Trailer
{
    /// <summary>
    /// Trailer-only "max quality": swaps the active quality level's URP asset for an in-memory copy (HDR grading, MSAA 4x,
    /// 4-cascade high-res soft shadows over a long distance, depth / opaque textures) and raises LOD bias and texture
    /// filtering, then adds a high-priority cinematic post volume (ACES, bloom with procedural lens dirt, grain, light
    /// chromatic aberration, vignette, grading, per-shot depth of field and motion blur). Nothing is written to disk:
    /// <see cref="Restore"/> puts every setting back, and the copies are destroyed.
    /// </summary>
    public class TrailerQuality
    {
        private RenderPipelineAsset prevQualityPipeline;
        private UniversalRenderPipelineAsset clone;
        private float prevLodBias;
        private int prevMaxLod, prevMip, prevPixelLights;
        private AnisotropicFiltering prevAniso;
        private UnityEngine.ShadowQuality prevShadows;
        private bool applied;

        public Volume Volume { get; private set; }
        public VolumeProfile Profile { get; private set; }
        public DepthOfField Dof { get; private set; }
        public MotionBlur Blur { get; private set; }
        public ColorAdjustments Color { get; private set; }
        public Bloom Bloom { get; private set; }
        private Texture2D dirt;

        public void Apply()
        {
            if (applied) return;
            applied = true;
            prevQualityPipeline = QualitySettings.renderPipeline;
            var source = (prevQualityPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (source != null)
            {
                clone = Object.Instantiate(source);
                clone.name = source.name + " (Trailer)";
                clone.supportsHDR = true;
                clone.colorGradingMode = ColorGradingMode.HighDynamicRange;
                clone.colorGradingLutSize = 64;
                clone.msaaSampleCount = 4;
                clone.renderScale = 1f;
                clone.supportsCameraDepthTexture = true;
                clone.supportsCameraOpaqueTexture = true;
                clone.shadowDistance = 260f;
                clone.shadowCascadeCount = 4;
                clone.cascade4Split = new Vector3(0.04f, 0.12f, 0.35f);
                clone.mainLightShadowmapResolution = 4096;
                clone.maxAdditionalLightsCount = 8;
                SetField(clone, "m_SoftShadowsSupported", true);
                SetField(clone, "m_MainLightShadowsSupported", true);
                SetField(clone, "m_AdditionalLightsRenderingMode", LightRenderingMode.PerPixel);
                SetField(clone, "m_AdditionalLightShadowsSupported", true);
                SetField(clone, "m_AdditionalLightsShadowmapResolution", 2048);
                SetField(clone, "m_HDRColorBufferPrecision", HDRColorBufferPrecision._64Bits);
                QualitySettings.renderPipeline = clone;
            }
            else Debug.LogWarning("[Trailer] No URP asset to copy; quality unchanged.");

            prevLodBias = QualitySettings.lodBias; QualitySettings.lodBias = 3f;
            prevMaxLod = QualitySettings.maximumLODLevel; QualitySettings.maximumLODLevel = 0;
            prevAniso = QualitySettings.anisotropicFiltering; QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            prevMip = QualitySettings.globalTextureMipmapLimit; QualitySettings.globalTextureMipmapLimit = 0;
            prevPixelLights = QualitySettings.pixelLightCount; QualitySettings.pixelLightCount = 8;
            prevShadows = QualitySettings.shadows; QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            BuildVolume();
        }

        public void Restore()
        {
            if (!applied) return;
            applied = false;
            QualitySettings.renderPipeline = prevQualityPipeline;
            QualitySettings.lodBias = prevLodBias;
            QualitySettings.maximumLODLevel = prevMaxLod;
            QualitySettings.anisotropicFiltering = prevAniso;
            QualitySettings.globalTextureMipmapLimit = prevMip;
            QualitySettings.pixelLightCount = prevPixelLights;
            QualitySettings.shadows = prevShadows;
            if (Volume != null) Object.Destroy(Volume.gameObject);
            if (Profile != null) Object.Destroy(Profile);
            if (clone != null) Object.Destroy(clone);
            if (dirt != null) Object.Destroy(dirt);
        }

        private static void SetField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Debug.Log("[Trailer] URP field not found: " + name); return; }
            try { f.SetValue(target, value); } catch (System.Exception e) { Debug.Log("[Trailer] URP field " + name + ": " + e.Message); }
        }

        private void BuildVolume()
        {
            var go = new GameObject("Trailer Post Volume");
            Volume = go.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = 1000f;
            Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Profile.name = "Trailer Post (runtime)";
            Volume.sharedProfile = Profile;

            var tone = Profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            Bloom = Profile.Add<Bloom>(true);
            Bloom.threshold.Override(1.05f);
            Bloom.intensity.Override(0.75f);
            Bloom.scatter.Override(0.72f);
            Bloom.highQualityFiltering.Override(true);
            dirt = MakeDirt(512, 1234);
            Bloom.dirtTexture.Override(dirt);
            Bloom.dirtIntensity.Override(2.2f);

            Color = Profile.Add<ColorAdjustments>(true);
            Color.postExposure.Override(0.35f);
            Color.contrast.Override(8f);
            Color.saturation.Override(10f);

            var smh = Profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.98f, 1.12f, 0f));     // cool teal shadows
            smh.highlights.Override(new Vector4(1.08f, 1.02f, 0.92f, 0f));  // warm highlights

            var wb = Profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-6f);

            var vig = Profile.Add<Vignette>(true);
            vig.intensity.Override(0.32f);
            vig.smoothness.Override(0.45f);

            var ca = Profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.07f);

            var grain = Profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0.18f);
            grain.response.Override(0.85f);

            Dof = Profile.Add<DepthOfField>(true);
            Dof.mode.Override(DepthOfFieldMode.Off);
            Dof.focalLength.Override(70f);
            Dof.aperture.Override(2.8f);
            Dof.bladeCount.Override(6);

            Blur = Profile.Add<MotionBlur>(true);
            Blur.mode.Override(MotionBlurMode.CameraAndObjects);
            Blur.quality.Override(MotionBlurQuality.High);
            Blur.intensity.Override(0f);
            Blur.clamp.Override(0.06f);
        }

        /// <summary>Depth of field for the coming frame: focus distance (m) and f-stop, or off when focus &lt;= 0.</summary>
        public void SetDof(float focus, float aperture = 2.8f, float focalLength = 70f)
        {
            if (Dof == null) return;
            if (focus <= 0f) { Dof.mode.Override(DepthOfFieldMode.Off); return; }
            Dof.mode.Override(DepthOfFieldMode.Bokeh);
            Dof.focusDistance.Override(focus);
            Dof.aperture.Override(aperture);
            Dof.focalLength.Override(focalLength);
        }

        /// <summary>Exposure (EV) of the grade: night needs a lot more than sunset with ACES.</summary>
        public void SetExposure(float ev)
        {
            if (Color != null) Color.postExposure.Override(ev);
        }

        public void SetMotionBlur(float intensity)
        {
            if (Blur != null) Blur.intensity.Override(Mathf.Clamp01(intensity));
        }

        // Soft blobs and a few streaks, like a lightly smudged lens.
        private static Texture2D MakeDirt(int size, int seed)
        {
            var rng = new System.Random(seed);
            var px = new Color32[size * size];
            var acc = new float[size * size];
            for (int b = 0; b < 70; b++)
            {
                float cx = (float)rng.NextDouble() * size, cy = (float)rng.NextDouble() * size;
                float r = 6f + (float)rng.NextDouble() * 40f, a = 0.08f + (float)rng.NextDouble() * 0.25f;
                int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(size - 1, (int)(cx + r));
                int y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(size - 1, (int)(cy + r));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d < 1f) acc[y * size + x] += a * (1f - d * d);
                    }
            }
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)(Mathf.Clamp01(acc[i]) * 255f);
                px[i] = new Color32(v, v, v, 255);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Trailer Lens Dirt", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
