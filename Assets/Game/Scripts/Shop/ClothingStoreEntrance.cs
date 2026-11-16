using OpenworldCrime.Player;
using UnityEngine;

// The clothing store's front door in the city: walk up to it and press the interact key to go in.
public class ClothingStoreEntrance : MonoBehaviour, IPlayerInteractable
{
    public string prompt = "Enter Miami Clothing Store";
    [Tooltip("Where the player stands on coming back out, facing the street.")]
    public Transform exitPoint;

    public string InteractionPrompt => prompt;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        var travel = ClothingStoreTravel.Instance;
        return isActiveAndEnabled && travel != null && !travel.Busy && !travel.Inside && !GamePlayer.InVehicle && ClothingStoreTravel.ForCurrentCharacter;
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (CanInteract(player)) ClothingStoreTravel.Instance.Enter(this);
    }
}
