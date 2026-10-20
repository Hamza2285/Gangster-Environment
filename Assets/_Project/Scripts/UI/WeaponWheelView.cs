using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Visuals of the radial weapon wheel panel only: slot labels, availability and highlight.
    /// It owns no gameplay data and does not equip anything - UIManager drives it.
    /// Slots are laid out clockwise starting at the top (slot 0 = 12 o'clock).
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponWheelView : MonoBehaviour
    {
        [Serializable]
        private class SlotView
        {
            public Image background;
            public Text label;
        }

        [Header("Slots (clockwise from top)")]
        [SerializeField] private List<SlotView> slots = new List<SlotView>();

        [Header("Selection")]
        [Tooltip("Cursor distance (0-1) required before the highlight follows the direction.")]
        [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.35f;
        [Tooltip("Optional marker that shows the current selection direction.")]
        [SerializeField] private RectTransform directionMarker;
        [SerializeField, Min(0f)] private float markerRadius = 90f;
        [Tooltip("Optional text in the wheel centre showing the highlighted slot name.")]
        [SerializeField] private Text selectedNameText;

        [Header("Colors")]
        [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.6f);
        [SerializeField] private Color highlightColor = new Color(1f, 0.75f, 0.1f, 0.9f);
        [SerializeField] private Color unavailableColor = new Color(0.3f, 0.3f, 0.3f, 0.35f);

        public int SlotCount => slots.Count;
        public int HighlightedIndex { get; private set; } = -1;

        private readonly List<bool> availability = new List<bool>();
        private readonly List<string> names = new List<string>();

        /// <summary>Sets label and availability for one slot.</summary>
        public void SetSlot(int index, string displayName, bool available)
        {
            if (index < 0 || index >= slots.Count) return;
            while (availability.Count <= index) { availability.Add(false); names.Add(string.Empty); }
            availability[index] = available;
            names[index] = displayName;
            if (slots[index].label != null) slots[index].label.text = displayName;
            RefreshColors();
        }

        /// <summary>Highlights a slot directly (e.g. the currently equipped weapon when the wheel opens).</summary>
        public void SetHighlighted(int index)
        {
            HighlightedIndex = index >= 0 && index < slots.Count ? index : -1;
            if (selectedNameText != null) selectedNameText.text = HighlightedIndex >= 0 && HighlightedIndex < names.Count ? names[HighlightedIndex] : string.Empty;
            RefreshColors();
        }

        /// <summary>Updates the highlight from a cursor vector (length 0-1, +Y = up). Unavailable slots are skipped.</summary>
        public void UpdateSelection(Vector2 cursor)
        {
            if (directionMarker != null) directionMarker.anchoredPosition = cursor * markerRadius;
            if (slots.Count == 0 || cursor.magnitude < deadZone) return;

            // Clockwise angle from the top, 0-360.
            float angle = Mathf.Atan2(cursor.x, cursor.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            float step = 360f / slots.Count;
            int index = Mathf.RoundToInt(angle / step) % slots.Count;

            if (index != HighlightedIndex && IsAvailable(index)) SetHighlighted(index);
        }

        private bool IsAvailable(int index) => index < availability.Count && availability[index];

        private void RefreshColors()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].background == null) continue;
                slots[i].background.color = !IsAvailable(i) ? unavailableColor : (i == HighlightedIndex ? highlightColor : normalColor);
            }
        }
    }
}
