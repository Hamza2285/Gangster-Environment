using OpenworldCrime.Player;
using UnityEngine;

// The car showroom's front door in the city: walk up to it and press the interact key to go in.
public class ShowroomEntrance : MonoBehaviour, IPlayerInteractable
{
    public string prompt = "Enter the Car Showroom";
    [Tooltip("Where the player stands on coming back out, facing the street.")]
    public Transform exitPoint;

    public string InteractionPrompt => prompt;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        var travel = ShowroomTravel.Instance;
        return isActiveAndEnabled && travel != null && !travel.Busy && !travel.Inside && !GamePlayer.InVehicle;
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (CanInteract(player)) ShowroomTravel.Instance.Enter(this);
    }
}
