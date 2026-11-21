using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// Scale feedback on a button (per-instance): grows a little on hover, squashes on press, springs back on release. Eased
    /// on unscaled time, so it also works while the game is paused. Update only runs while the scale is moving.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPressFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform target;
        [SerializeField, Min(0.5f)] private float hoverScale = 1.08f;
        [SerializeField, Min(0.5f)] private float pressedScale = 0.9f;
        [Tooltip("Higher is snappier.")]
        [SerializeField, Min(1f)] private float sharpness = 18f;

        private Vector3 baseScale = Vector3.one;
        private bool hovered, pressed;

        private void Awake()
        {
            if (target == null) target = (RectTransform)transform;
            baseScale = target.localScale;
            enabled = false;
        }

        private void OnDisable()
        {
            hovered = pressed = false;
            if (target != null) target.localScale = baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; enabled = true; }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; enabled = true; }
        public void OnPointerDown(PointerEventData eventData) { pressed = true; enabled = true; }
        public void OnPointerUp(PointerEventData eventData) { pressed = false; enabled = true; }

        private void Update()
        {
            float goal = pressed ? pressedScale : hovered ? hoverScale : 1f;
            Vector3 wanted = baseScale * goal;
            target.localScale = Vector3.Lerp(target.localScale, wanted, 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime));
            if ((target.localScale - wanted).sqrMagnitude < 0.000001f)
            {
                target.localScale = wanted;
                if (!pressed && !hovered) enabled = false;
            }
        }
    }
}
