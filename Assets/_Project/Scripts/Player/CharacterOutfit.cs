using System;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Picks one option per outfit slot (hair, top, bottoms, shoes...) on a character model that ships with every variant
    /// skinned in. Only the chosen parts' renderers are enabled (disabled skinned meshes cost nothing); change Selected in the
    /// inspector to restyle the character. Set up by Tools > OpenworldCrime > Characters > Swap Player Model.
    /// </summary>
    public class CharacterOutfit : MonoBehaviour
    {
        [Serializable]
        public class Option
        {
            public string label;
            [Tooltip("Renderers shown together for this option (e.g. a T-shirt and the bare arms).")]
            public Renderer[] parts = new Renderer[0];
        }

        [Serializable]
        public class Slot
        {
            public string name;
            public Option[] options = new Option[0];
            [Tooltip("Index into Options; -1 = none.")]
            public int selected;
        }

        [SerializeField] private Slot[] slots = new Slot[0];

        public int SlotCount => slots.Length;

        private void Awake() => Apply();

        private void OnValidate() => Apply();

        /// <summary>Selects option <paramref name="option"/> (-1 = none) of slot <paramref name="slot"/>.</summary>
        public void Select(int slot, int option)
        {
            if (slot < 0 || slot >= slots.Length) return;
            slots[slot].selected = option;
            Apply();
        }

        public void Apply()
        {
            if (slots == null) return;
            foreach (Slot slot in slots)
            {
                if (slot?.options == null) continue;
                // Hide every option first, then show the chosen one, so parts shared between options stay visible.
                foreach (Option o in slot.options) SetParts(o, false);
                if (slot.selected >= 0 && slot.selected < slot.options.Length) SetParts(slot.options[slot.selected], true);
            }
        }

        private static void SetParts(Option option, bool on)
        {
            if (option?.parts == null) return;
            foreach (Renderer r in option.parts) if (r != null) r.enabled = on;
        }

#if UNITY_EDITOR
        public void EditorSetSlots(Slot[] newSlots)
        {
            slots = newSlots;
            Apply();
        }
#endif
    }
}
