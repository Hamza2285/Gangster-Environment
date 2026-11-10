using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Switches the street reflection probes (custom baked cubemaps, Tools > OpenworldCrime > Lighting > Bake Car Reflection Probes)
    /// between their day and night bake with the city's day/night look. A day-baked probe would show a sunlit street on a car at
    /// night, so each probe has two cubemaps; the set follows CityDayNightController's night amount and is exchanged at the middle
    /// of a transition, where the probe intensity dips (the same trick the controller uses for the sky reflection), so the swap
    /// does not pop. While active it also tells the car paint shader that real reflections are present (_CarPaintProbeBlend),
    /// which then keeps only a little of its fake sky gradient.
    /// URP's Forward+ probe atlas keeps the cubemap it first copied for a probe (it only re-copies when that texture's update count
    /// changes), so a swap switches the probes off for a few frames, which evicts them from the atlas, and back on with the new
    /// cubemaps. The Forward path (mobile) samples the cubemap directly and does not need it, but it costs nothing there either.
    /// The controller has no change event (and is not ours to edit), so its night amount is read on a slow coroutine; nothing
    /// happens unless it changed.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReflectionProbeDayNight : MonoBehaviour
    {
        private static readonly int ProbeBlendId = Shader.PropertyToID("_CarPaintProbeBlend");

        [SerializeField] private CityDayNightController dayNight;
        [Tooltip("Probes in Custom mode; their cubemaps are swapped. Same order as the two texture lists.")]
        [SerializeField] private ReflectionProbe[] probes = new ReflectionProbe[0];
        [SerializeField] private Texture[] dayTextures = new Texture[0];
        [SerializeField] private Texture[] nightTextures = new Texture[0];
        [Tooltip("Probe intensity by day and by night.")]
        [SerializeField, Range(0f, 2f)] private float dayIntensity = 1f;
        [SerializeField, Range(0f, 2f)] private float nightIntensity = 1f;
        [Tooltip("Lowest intensity in the middle of a transition, where the day and night cubemaps are exchanged.")]
        [SerializeField, Range(0f, 1f)] private float swapIntensity = 0.25f;
        [Tooltip("Seconds between reads of the day/night amount.")]
        [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

        private float appliedBlend = -1f;
        private int appliedSet = -1;
        private int targetSet = -1;
        private Coroutine swapRoutine;
        private bool valid;

        public bool IsShowingNight => appliedSet == 1;

        private void Awake()
        {
            valid = probes.Length > 0 && dayTextures.Length == probes.Length && nightTextures.Length == probes.Length;
            if (!valid) Debug.LogError($"[{nameof(ReflectionProbeDayNight)}] '{name}' has no probes or its day/night cubemap lists do not match the probes. Re-run the probe bake.", this);
            if (dayNight == null) Debug.LogWarning($"[{nameof(ReflectionProbeDayNight)}] No CityDayNightController on '{name}'; the day set stays.", this);
        }

        private void OnEnable()
        {
            if (!valid) return;
            appliedBlend = -1f;
            appliedSet = -1;
            Apply(dayNight != null ? dayNight.NightBlend : 0f);
            Shader.SetGlobalFloat(ProbeBlendId, 1f);
            if (dayNight != null) StartCoroutine(Watch());
        }

        private void Start()
        {
            // The controller applies its saved look in its own OnEnable, which may run after ours.
            if (valid && dayNight != null) Apply(dayNight.NightBlend);
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(ProbeBlendId, 0f);
            // Never leave the probes switched off halfway through a swap.
            if (swapRoutine == null) return;
            swapRoutine = null;
            for (int i = 0; i < probes.Length; i++)
            {
                if (probes[i] != null) probes[i].enabled = true;
            }
        }

        private IEnumerator Watch()
        {
            var wait = new WaitForSeconds(checkInterval);
            while (true)
            {
                yield return wait;
                if (dayNight != null) Apply(dayNight.NightBlend);
            }
        }

        /// <summary>Shows the probes for a night amount (0 = day, 1 = night).</summary>
        public void Apply(float night)
        {
            night = Mathf.Clamp01(night);
            if (Mathf.Abs(night - appliedBlend) < 0.002f) return;
            appliedBlend = night;
            targetSet = night >= 0.5f ? 1 : 0;
            float dip = Mathf.Lerp(swapIntensity, 1f, SmootherStep(Mathf.Abs(night * 2f - 1f)));
            float intensity = Mathf.Lerp(dayIntensity, nightIntensity, night) * dip;
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe probe = probes[i];
                if (probe != null) probe.intensity = intensity;
            }
            if (targetSet != appliedSet && swapRoutine == null && isActiveAndEnabled) swapRoutine = StartCoroutine(Swap());
        }

        private IEnumerator Swap()
        {
            for (int i = 0; i < probes.Length; i++)
            {
                if (probes[i] != null) probes[i].enabled = false;
            }
            // The atlas drops a probe that was not rendered for more than one frame.
            yield return null;
            yield return null;
            yield return null;
            int set = targetSet;
            Texture[] textures = set == 1 ? nightTextures : dayTextures;
            for (int i = 0; i < probes.Length; i++)
            {
                if (probes[i] == null) continue;
                probes[i].customBakedTexture = textures[i];
                probes[i].enabled = true;
            }
            appliedSet = set;
            swapRoutine = null;
        }

        private static float SmootherStep(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, ReflectionProbe[] newProbes, Texture[] day, Texture[] night)
        {
            dayNight = controller;
            probes = newProbes;
            dayTextures = day;
            nightTextures = night;
        }
#endif
    }
}
