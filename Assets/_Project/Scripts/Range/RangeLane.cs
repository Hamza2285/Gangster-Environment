using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Range
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Shooting booth of the range: its trigger is the lane area. Standing in it shows "Start Range Challenge"; starting
    /// moves the player to the controller's fixed shooting spot.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RangeLane : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private ShootingRangeController range;
        [SerializeField] private string prompt = "Start Range Challenge";

        private int insideCount;

        public string InteractionPrompt => prompt;

        private void Awake()
        {
            if (range == null)
            {
                Debug.LogError($"[{nameof(RangeLane)}] No ShootingRangeController assigned on '{name}'.", this);
                enabled = false;
            }
        }

        public bool CanInteract(PlayerManager player) => isActiveAndEnabled && range.CanStart;

        public void Interact(PlayerManager player)
        {
            if (CanInteract(player)) range.StartChallenge(player);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount++;
            if (insideCount == 1 && range != null) range.SetPlayerInLane(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount = Mathf.Max(0, insideCount - 1);
            if (insideCount == 0 && range != null) range.SetPlayerInLane(false);
        }

        private static bool IsPlayer(Collider other) => other is CapsuleCollider && other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<PlayerManager>() != null;

        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
