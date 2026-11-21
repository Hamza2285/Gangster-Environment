using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// A slow, subtle "breathing" pulse on a UI element (scale and, optionally, a glow graphic's alpha), on unscaled time.
    /// Scale is applied to <see cref="target"/>, which should not be the same transform that a press feedback scales.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPulse : MonoBehaviour
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private Graphic glow;
        [SerializeField, Min(0.1f)] private float period = 1.8f;
        [SerializeField, Range(0f, 0.2f)] private float scaleAmount = 0.035f;
        [SerializeField] private Vector2 glowAlpha = new Vector2(0.25f, 0.75f);

        private Vector3 baseScale = Vector3.one;

        private void Awake()
        {
            if (target == null) target = (RectTransform)transform;
            baseScale = target.localScale;
        }

        private void OnDisable()
        {
            if (target != null) target.localScale = baseScale;
        }

        private void Update()
        {
            // Smooth sine in 0..1.
            float k = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / period);
            target.localScale = baseScale * (1f + scaleAmount * k);
            if (glow != null)
            {
                Color c = glow.color;
                c.a = Mathf.Lerp(glowAlpha.x, glowAlpha.y, k);
                glow.color = c;
            }
        }
    }
}
