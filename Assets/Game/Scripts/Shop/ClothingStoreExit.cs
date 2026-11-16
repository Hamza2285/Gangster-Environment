using OpenworldCrime.Player;
using UnityEngine;

// The way back out, inside the store.
public class ClothingStoreExit : MonoBehaviour, IPlayerInteractable
{
    public string prompt = "Leave the store";

    public string InteractionPrompt => prompt;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        var travel = ClothingStoreTravel.Instance;
        return isActiveAndEnabled && travel != null && !travel.Busy && travel.Inside && !ClothingStoreMenu.IsOpen;
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (CanInteract(player)) ClothingStoreTravel.Instance.Exit();
    }
}
