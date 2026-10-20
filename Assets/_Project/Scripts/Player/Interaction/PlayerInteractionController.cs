using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Tracks <see cref="IPlayerInteractable"/> triggers the player is inside, keeps the closest valid one
    /// in focus (for UI prompts) and interacts with it on input.
    /// Invector's own trigger actions (vGenericAction / vTriggerGenericAction) keep working independently.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerManager playerManager;

        [Header("Input")]
        [SerializeField] private GenericInput interactInput = new GenericInput("E", "A", "A");

        /// <summary>Raised when the focused interactable changes (null = nothing in range).</summary>
        public event Action<IPlayerInteractable> OnFocusChanged;
        /// <summary>Raised after a successful interaction.</summary>
        public event Action<IPlayerInteractable> OnInteracted;

        public IPlayerInteractable CurrentFocus { get; private set; }

        private readonly List<IPlayerInteractable> candidates = new List<IPlayerInteractable>();

        private void Awake()
        {
            if (playerManager == null)
            {
                Debug.LogError($"[{nameof(PlayerInteractionController)}] Missing PlayerManager reference on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            candidates.Clear();
            SetFocus(null);
        }

        private void OnTriggerEnter(Collider other)
        {
            var interactable = other.GetComponentInParent<IPlayerInteractable>();
            if (interactable != null && !candidates.Contains(interactable)) candidates.Add(interactable);
        }

        private void OnTriggerExit(Collider other)
        {
            var interactable = other.GetComponentInParent<IPlayerInteractable>();
            if (interactable != null) candidates.Remove(interactable);
        }

        private void Update()
        {
            if (candidates.Count == 0)
            {
                if (CurrentFocus != null) SetFocus(null);
                return;
            }

            SetFocus(FindClosestValid());

            if (CurrentFocus != null && !playerManager.IsInputLocked
                && playerManager.CurrentState != PlayerState.Dead && interactInput.GetButtonDown())
            {
                var target = CurrentFocus;
                target.Interact(playerManager);
                OnInteracted?.Invoke(target);
            }
        }

        private IPlayerInteractable FindClosestValid()
        {
            IPlayerInteractable closest = null;
            float closestSqr = float.MaxValue;
            Vector3 position = transform.position;

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var candidate = candidates[i];
                // Destroyed/disabled objects never send OnTriggerExit - drop them here.
                if (candidate == null || (candidate as UnityEngine.Object) == null)
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (!candidate.CanInteract(playerManager)) continue;

                float sqr = (candidate.transform.position - position).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = candidate;
                }
            }
            return closest;
        }

        private void SetFocus(IPlayerInteractable focus)
        {
            if (ReferenceEquals(focus, CurrentFocus)) return;
            CurrentFocus = focus;
            OnFocusChanged?.Invoke(focus);
        }
    }
}
