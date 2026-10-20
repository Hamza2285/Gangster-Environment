using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Anything the player can interact with (doors, NPCs, shops, pickups with custom logic...).
    /// The object needs a trigger collider so <see cref="PlayerInteractionController"/> can detect it.
    /// </summary>
    public interface IPlayerInteractable
    {
        /// <summary>Short prompt text for UI, e.g. "Open door".</summary>
        string InteractionPrompt { get; }
        Transform transform { get; }
        bool CanInteract(PlayerManager player);
        void Interact(PlayerManager player);
    }
}
