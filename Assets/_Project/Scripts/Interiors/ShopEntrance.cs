using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// A street door into an interior (GTA "yellow marker"). Needs a trigger collider; the player's
    /// <see cref="PlayerInteractionController"/> shows the prompt and calls <see cref="Interact"/>.
    /// Add more entrances by placing more instances of the entrance prefab; each one needs a unique <see cref="EntranceId"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopEntrance : MonoBehaviour, IPlayerInteractable
    {
        [Tooltip("Unique per entrance; the player returns to the entrance he used.")]
        [SerializeField] private string entranceId = "gunshop_1";
        [Tooltip("Scene (in Build Settings) loaded additively when entering.")]
        [SerializeField] private string interiorSceneName = "GunShop_Interior";
        [SerializeField] private string prompt = "Enter Gun Shop";
        [Tooltip("Where the player stands, facing away from the door, after leaving the interior.")]
        [SerializeField] private Transform exitPoint;

        public string EntranceId => entranceId;
        public string InteriorSceneName => interiorSceneName;
        public Transform ExitPoint => exitPoint != null ? exitPoint : transform;
        public string InteractionPrompt => prompt;

        public bool CanInteract(PlayerManager player)
        {
            InteriorTravelManager travel = InteriorTravelManager.Instance;
            return isActiveAndEnabled && player != null && !player.IsInVehicle && travel != null && travel.CanTravel;
        }

        public void Interact(PlayerManager player)
        {
            if (!CanInteract(player)) return;
            InteriorTravelManager.Instance.Enter(this);
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}
