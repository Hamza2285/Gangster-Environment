using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// The rider shown on a drivable motorbike while it is ridden (the bike's RCCVehicleAdapter Driver Dummy). The player's own
    /// character is hidden while seated, so each playable character has a copy posed on seat, grips and pegs (built by
    /// Build Drivable Bikes); when the dummy is shown, the one matching the character sitting on the seat is picked.
    /// Runs only when the dummy is switched on (OnEnable), never per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class BikeRiderVisual : MonoBehaviour
    {
        [System.Serializable]
        public struct Rider
        {
            [Tooltip("Name of the playable character's scene object (e.g. Player_Jason).")]
            public string characterName;
            public GameObject pose;
        }

        [SerializeField] private Rider[] riders = new Rider[0];

        private void OnEnable()
        {
            string seated = FindSeatedCharacter();
            int pick = 0;
            for (int i = 0; i < riders.Length; i++)
            {
                if (!string.IsNullOrEmpty(seated) && seated.StartsWith(riders[i].characterName)) pick = i;
            }
            for (int i = 0; i < riders.Length; i++)
            {
                if (riders[i].pose != null) riders[i].pose.SetActive(i == pick);
            }
        }

        // The player is parented to the seat while he rides (PlayerVehicleInteraction), next to this dummy.
        private string FindSeatedCharacter()
        {
            Transform seat = transform.parent;
            if (seat == null) return null;
            for (int i = 0; i < seat.childCount; i++)
            {
                Transform child = seat.GetChild(i);
                if (child != transform && child.TryGetComponent(out PlayerVehicleInteraction _)) return child.name;
            }
            return null;
        }
    }
}
