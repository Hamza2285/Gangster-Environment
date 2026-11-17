using OpenworldCrime.Player;
using UnityEngine;

// The way back out, inside the showroom by its entrance doors.
public class ShowroomExit : MonoBehaviour, IPlayerInteractable
{
    public string prompt = "Leave the showroom";

    public string InteractionPrompt => prompt;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        var travel = ShowroomTravel.Instance;
        return isActiveAndEnabled && travel != null && !travel.Busy && travel.Inside;
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (CanInteract(player)) ShowroomTravel.Instance.Exit();
    }
}
