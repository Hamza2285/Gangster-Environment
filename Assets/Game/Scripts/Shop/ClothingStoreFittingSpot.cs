using OpenworldCrime.Player;
using UnityEngine;

// The fitting area in front of the mirror: the only place clothes can be changed.
public class ClothingStoreFittingSpot : MonoBehaviour, IPlayerInteractable
{
    public string prompt = "Browse Clothes";

    public string InteractionPrompt => prompt;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        var menu = ClothingStoreMenu.Instance;
        return isActiveAndEnabled && menu != null && !ClothingStoreMenu.IsOpen && ClothingStoreTravel.Instance != null && !ClothingStoreTravel.Instance.Busy;
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (CanInteract(player)) ClothingStoreMenu.Instance.Open();
    }
}
