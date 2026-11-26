using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Visuals of one small car health bar (dark pill, pink fill, a lighter "chip" that trails the fill down after a hit).
    /// Only sets its own images; <see cref="VehicleHealthBarHud"/> owns, pools and places it. Fill and chip are left-anchored and
    /// sized through anchorMax.x, so the rounded ends keep their shape at any fill.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleHealthBarView : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private Image background;
        [SerializeField] private RectTransform fill;
        [SerializeField] private Image fillImage;
        [SerializeField] private RectTransform chip;
        [SerializeField] private Image chipImage;

        private Color backgroundColor, fillColor, chipColor;
        private float shownFill = -1f, shownChip = -1f, shownAlpha = -1f;

        public RectTransform Root => root;

        private void Awake()
        {
            if (root == null) root = transform as RectTransform;
            backgroundColor = background != null ? background.color : Color.clear;
            fillColor = fillImage != null ? fillImage.color : Color.clear;
            chipColor = chipImage != null ? chipImage.color : Color.clear;
        }

        /// <summary>Fill and chip as shares of full health (0-1). Only touches the layout when a value changed.</summary>
        public void SetValues(float fillShare, float chipShare)
        {
            fillShare = Mathf.Clamp01(fillShare);
            chipShare = Mathf.Clamp(chipShare, fillShare, 1f);
            if (!Mathf.Approximately(fillShare, shownFill)) SetWidth(fill, shownFill = fillShare);
            if (!Mathf.Approximately(chipShare, shownChip)) SetWidth(chip, shownChip = chipShare);
        }

        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - shownAlpha) < 0.01f) return;
            shownAlpha = alpha;
            SetAlpha(background, backgroundColor, alpha);
            SetAlpha(fillImage, fillColor, alpha);
            SetAlpha(chipImage, chipColor, alpha);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        private static void SetWidth(RectTransform bar, float share)
        {
            if (bar == null) return;
            Vector2 max = bar.anchorMax;
            max.x = share;
            bar.anchorMax = max;
        }

        private static void SetAlpha(Graphic graphic, Color baseColor, float alpha)
        {
            if (graphic == null) return;
            baseColor.a *= alpha;
            graphic.color = baseColor;
        }

#if UNITY_EDITOR
        public void EditorSetup(RectTransform newRoot, Image newBackground, RectTransform newFill, Image newFillImage, RectTransform newChip, Image newChipImage)
        {
            root = newRoot; background = newBackground; fill = newFill; fillImage = newFillImage; chip = newChip; chipImage = newChipImage;
        }
#endif
    }
}
