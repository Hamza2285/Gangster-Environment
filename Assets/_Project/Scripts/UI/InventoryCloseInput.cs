using Invector.vCharacterController;
using Invector.vItemManager;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Closes the Invector inventory with Escape (keyboard) or B (gamepad). Invector only closes it with its own
    /// open/close toggle (I / Start). Enabled only while the inventory is open (it listens to vInventory's open/close
    /// event), so it does not poll input during gameplay. No Invector script is changed.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryCloseInput : MonoBehaviour
    {
        [SerializeField] private vInventory inventory;
        [SerializeField] private GenericInput closeInput = new GenericInput("Escape", "B", "B");

        private void Awake()
        {
            if (inventory == null)
            {
                Debug.LogWarning($"[{nameof(InventoryCloseInput)}] No vInventory assigned on '{name}'; Escape will not close the inventory.", this);
                enabled = false;
                return;
            }

            inventory.onOpenCloseInventory.AddListener(HandleOpenClose);
            enabled = inventory.isOpen;
        }

        private void OnDestroy()
        {
            if (inventory != null) inventory.onOpenCloseInventory.RemoveListener(HandleOpenClose);
        }

        private void HandleOpenClose(bool isOpen)
        {
            enabled = isOpen;
        }

        // Runs only while the inventory is open.
        private void Update()
        {
            if (inventory.isOpen && closeInput.GetButtonDown()) inventory.CloseInventory();
        }
    }
}
