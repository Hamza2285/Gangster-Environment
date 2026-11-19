using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Club
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// A club seat the player can use ("Sit" prompt through <see cref="PlayerInteractionController"/>). Needs a trigger
    /// collider in front of the seat; the floor marker is a child and hides while the seat is taken.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubSeat : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private ClubPlayerSeating seating;
        [Tooltip("Hips target on the cushion; its forward is the seated facing.")]
        [SerializeField] private Transform seatPoint;
        [Tooltip("Camera framing while seated (toward the stage).")]
        [SerializeField] private Transform cameraPoint;
        [SerializeField] private GameObject marker;
        [SerializeField] private string prompt = "Sit";

        public string InteractionPrompt => prompt;

        private bool occupied;

        private void Awake()
        {
            if (seating == null || seatPoint == null)
            {
                Debug.LogError($"[{nameof(ClubSeat)}] Missing seating or seat point on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (seating != null) seating.OnSeatedChanged += HandleSeatedChanged;
        }

        private void OnDisable()
        {
            if (seating != null) seating.OnSeatedChanged -= HandleSeatedChanged;
        }

        public bool CanInteract(PlayerManager player) => isActiveAndEnabled && player != null && !seating.IsSeated && !player.IsInVehicle;

        public void Interact(PlayerManager player)
        {
            if (!CanInteract(player)) return;
            occupied = seating.Sit(player, seatPoint, cameraPoint);
            if (occupied && marker != null) marker.SetActive(false);
        }

        private void HandleSeatedChanged(bool seated)
        {
            if (seated || !occupied) return;
            occupied = false;
            if (marker != null) marker.SetActive(true);
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}
