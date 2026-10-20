using UnityEngine;
using UnityEngine.Events;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Designer-friendly interactable: put it on an object with a trigger collider and hook the
    /// UnityEvent in the Inspector. For custom logic implement <see cref="IPlayerInteractable"/> directly.
    /// </summary>
    public class SimpleInteractable : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private string prompt = "Interact";
        [Tooltip("If true the interactable can only be used once.")]
        [SerializeField] private bool singleUse;
        [SerializeField] private UnityEvent onInteract = new UnityEvent();

        private bool used;

        public string InteractionPrompt => prompt;

        public bool CanInteract(PlayerManager player) => isActiveAndEnabled && !(singleUse && used);

        public void Interact(PlayerManager player)
        {
            if (!CanInteract(player)) return;
            used = true;
            onInteract.Invoke();
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}
