using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>Interior side of the shop door: takes the player back to the street entrance he came from.</summary>
    [DisallowMultipleComponent]
    public class InteriorExitDoor : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private string prompt = "Exit to Street";

        public string InteractionPrompt => prompt;

        public bool CanInteract(PlayerManager player)
        {
            InteriorTravelManager travel = InteriorTravelManager.Instance;
            return isActiveAndEnabled && travel != null && travel.IsInside && travel.CanTravel;
        }

        public void Interact(PlayerManager player)
        {
            if (CanInteract(player)) InteriorTravelManager.Instance.Exit();
        }
    }
}
