using Invector.vItemManager;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Renders the inventory's character preview camera (a RenderTexture camera from the Invector inventory prefab) only while
    /// the inventory is open, instead of every frame. Listens to vInventory's open/close event; no Invector script is changed.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryPreviewCameraToggle : MonoBehaviour
    {
        [SerializeField] private vInventory inventory;
        [Tooltip("Camera that renders the character preview into the inventory's RenderTexture.")]
        [SerializeField] private Camera previewCamera;

        private void Awake()
        {
            if (inventory == null || previewCamera == null)
                Debug.LogWarning($"[{nameof(InventoryPreviewCameraToggle)}] Missing vInventory or preview Camera on '{name}'; the preview camera keeps rendering every frame.", this);
        }

        private void OnEnable()
        {
            if (inventory == null || previewCamera == null) return;
            inventory.onOpenCloseInventory.AddListener(HandleOpenClose);
            previewCamera.enabled = inventory.isOpen;
        }

        private void OnDisable()
        {
            if (inventory == null) return;
            inventory.onOpenCloseInventory.RemoveListener(HandleOpenClose);
        }

        private void HandleOpenClose(bool isOpen)
        {
            if (previewCamera != null) previewCamera.enabled = isOpen;
        }
    }
}
