using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// Atmosphere over the splash / loading artwork (per screen; cheap): a soft diagonal light sweep crossing the screen now and
    /// then, a breathing vignette, and a few lens-dust motes drifting slowly upward and twinkling. Unscaled time; only its own
    /// handful of images are touched per frame, and only while the screen is shown.
    /// </summary>
    [DisallowMultipleComponent]
    public class LoadingAmbience : MonoBehaviour
    {
        [SerializeField] private RectTransform area;
        [Header("Light sweep")]
        [SerializeField] private Image sweep;
        [SerializeField, Min(0.5f)] private float sweepEvery = 3.2f;
        [SerializeField, Min(0.2f)] private float sweepSeconds = 1.6f;
        [SerializeField, Range(0f, 1f)] private float sweepAlpha = 0.16f;
        [Header("Vignette")]
        [SerializeField] private Image vignette;
        [SerializeField] private Vector2 vignetteAlpha = new Vector2(0.45f, 0.62f);
        [SerializeField, Min(0.5f)] private float vignettePeriod = 4.5f;
        [Header("Dust")]
        [SerializeField] private Image[] motes = new Image[0];
        [SerializeField] private Vector2 moteSpeed = new Vector2(14f, 34f);
        [SerializeField] private Vector2 moteSize = new Vector2(6f, 18f);
        [SerializeField, Range(0f, 1f)] private float moteAlpha = 0.35f;

        private struct Mote { public Vector2 Pos; public float Speed, Phase, Sway, Size; }
        private Mote[] state = new Mote[0];
        private float started;

        private void OnEnable()
        {
            started = Time.unscaledTime;
            if (area == null) area = (RectTransform)transform;
            Rect r = area.rect;
            state = new Mote[motes.Length];
            for (int i = 0; i < motes.Length; i++) state[i] = NewMote(r, true);
        }

        private Mote NewMote(Rect r, bool anywhere)
        {
            return new Mote
            {
                Pos = new Vector2(Random.Range(r.xMin, r.xMax), anywhere ? Random.Range(r.yMin, r.yMax) : r.yMin - 20f),
                Speed = Random.Range(moteSpeed.x, moteSpeed.y),
                Phase = Random.value * 10f,
                Sway = Random.Range(8f, 26f),
                Size = Random.Range(moteSize.x, moteSize.y)
            };
        }

        private void Update()
        {
            float now = Time.unscaledTime - started;
            float dt = Time.unscaledDeltaTime;
            Rect r = area.rect;

            if (sweep != null)
            {
                // Every few seconds a soft band crosses from left to right (eased), invisible in between.
                float cycle = now % sweepEvery;
                float k = Mathf.Clamp01(cycle / sweepSeconds);
                RectTransform st = sweep.rectTransform;
                st.anchoredPosition = new Vector2(Mathf.Lerp(r.xMin - st.rect.width, r.xMax + st.rect.width, Smooth(k)), 0f);
                Color c = sweep.color;
                c.a = sweepAlpha * Mathf.Sin(k * Mathf.PI);
                sweep.color = c;
            }
            if (vignette != null)
            {
                Color c = vignette.color;
                c.a = Mathf.Lerp(vignetteAlpha.x, vignetteAlpha.y, 0.5f - 0.5f * Mathf.Cos(now * Mathf.PI * 2f / vignettePeriod));
                vignette.color = c;
            }
            for (int i = 0; i < motes.Length; i++)
            {
                if (motes[i] == null) continue;
                Mote m = state[i];
                m.Pos.y += m.Speed * dt;
                if (m.Pos.y > r.yMax + 20f) m = NewMote(r, false);
                state[i] = m;
                RectTransform mt = motes[i].rectTransform;
                mt.anchoredPosition = new Vector2(m.Pos.x + Mathf.Sin(now * 0.6f + m.Phase) * m.Sway, m.Pos.y);
                mt.sizeDelta = new Vector2(m.Size, m.Size);
                Color c = motes[i].color;
                c.a = moteAlpha * (0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(now * 1.7f + m.Phase * 3f)));
                motes[i].color = c;
            }
        }

        private static float Smooth(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI);
    }
}
