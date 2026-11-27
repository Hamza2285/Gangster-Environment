using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// A door that takes the player to another spot in the city behind a short fade: the street door of a tower up to its roof,
    /// and the roof's stair-house door back down (one component on each, pointing at the other's arrival point). Needs a
    /// trigger collider; <see cref="PlayerInteractionController"/> shows the prompt and calls <see cref="Interact"/>.
    /// The move itself is <see cref="InteriorTravelManager.TravelInCity"/>. Built by Tools > OpenworldCrime > Rooftops.
    /// </summary>
    [DisallowMultipleComponent]
    public class RooftopDoor : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private string prompt = "Go to Roof";
        [Tooltip("Where the player stands (feet, facing its forward) after going through.")]
        [SerializeField] private Transform destination;
        [Tooltip("Optional caption on the black screen.")]
        [SerializeField] private string caption = "";

        public string InteractionPrompt => prompt;
        public Transform Destination => destination;

        public bool CanInteract(PlayerManager player)
        {
            InteriorTravelManager travel = InteriorTravelManager.Instance;
            return isActiveAndEnabled && destination != null && player != null && !player.IsInVehicle
                   && player.CurrentState != PlayerState.Swim && travel != null && travel.CanTravel && !travel.IsInside;
        }

        public void Interact(PlayerManager player)
        {
            if (!CanInteract(player)) return;
            InteriorTravelManager.Instance.TravelInCity(destination, caption);
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

#if UNITY_EDITOR
        public void EditorSetup(string promptText, Transform target, string captionText)
        {
            prompt = promptText;
            destination = target;
            caption = captionText;
        }

        private void OnDrawGizmosSelected()
        {
            if (destination == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, destination.position);
            Gizmos.DrawWireSphere(destination.position, 0.4f);
        }
#endif
    }
}
