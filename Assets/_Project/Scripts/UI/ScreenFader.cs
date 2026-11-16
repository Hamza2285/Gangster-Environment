using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>Full-screen black fade (visuals only). The panel is active only while it is not fully transparent.</summary>
    [DisallowMultipleComponent]
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField] private Image image;
        [Tooltip("Optional text shown while black (e.g. the interior name).")]
        [SerializeField] private Text caption;

        public bool IsBlack => image != null && image.gameObject.activeSelf && image.color.a >= 0.999f;

        private void Awake()
        {
            if (image == null)
            {
                Debug.LogError($"[{nameof(ScreenFader)}] No Image assigned on '{name}'.", this);
                enabled = false;
                return;
            }
            SetAlpha(0f);
        }

        /// <summary>Fades to <paramref name="alpha"/> over <paramref name="duration"/> seconds (unscaled time).</summary>
        public IEnumerator FadeTo(float alpha, float duration, string captionText = null)
        {
            if (!enabled) yield break;
            if (caption != null)
            {
                caption.text = captionText ?? string.Empty;
                caption.enabled = !string.IsNullOrEmpty(captionText);
            }
            float start = image.gameObject.activeSelf ? image.color.a : 0f;
            image.gameObject.SetActive(true);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(start, alpha, duration > 0f ? t / duration : 1f));
                yield return null;
            }
            SetAlpha(alpha);
        }

        private void SetAlpha(float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
            if (caption != null)
            {
                Color cc = caption.color;
                cc.a = alpha;
                caption.color = cc;
            }
            image.gameObject.SetActive(alpha > 0.001f);
        }
    }
}
