using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// The rider shown on a drivable motorbike while it is ridden (the bike's RCCVehicleAdapter Driver Dummy). The player's own
    /// character is hidden while seated, so each playable character has a copy posed on seat, grips and pegs (built by
    /// Build Drivable Bikes); when the dummy is shown, the one matching the character sitting on the seat is picked.
    /// The posed copy then takes the character's current look: every skinned mesh part (clothes from the clothing store, our
    /// outfit slots) is shown or hidden and uses the same materials as on the character, matched by name, so the rider wears
    /// what the player wears instead of the default outfit the copy was built with.
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

        private readonly System.Collections.Generic.Dictionary<string, SkinnedMeshRenderer> characterParts =
            new System.Collections.Generic.Dictionary<string, SkinnedMeshRenderer>();

        private void OnEnable()
        {
            Transform seated = FindSeatedCharacter();
            int pick = 0;
            for (int i = 0; i < riders.Length; i++)
            {
                if (seated != null && seated.name.StartsWith(riders[i].characterName)) pick = i;
            }
            for (int i = 0; i < riders.Length; i++)
            {
                if (riders[i].pose != null) riders[i].pose.SetActive(i == pick);
            }
            if (seated != null && pick < riders.Length && riders[pick].pose != null) CopyLook(seated, riders[pick].pose);
        }

        // Shows the same clothing parts with the same materials as the character.
        private void CopyLook(Transform character, GameObject pose)
        {
            characterParts.Clear();
            foreach (SkinnedMeshRenderer r in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!characterParts.ContainsKey(r.name)) characterParts[r.name] = r;
            foreach (SkinnedMeshRenderer r in pose.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!characterParts.TryGetValue(r.name, out SkinnedMeshRenderer source)) continue;
                bool shown = source.gameObject.activeSelf && source.enabled;
                if (r.gameObject.activeSelf != source.gameObject.activeSelf) r.gameObject.SetActive(source.gameObject.activeSelf);
                r.enabled = source.enabled;
                if (shown) r.sharedMaterials = source.sharedMaterials;
            }
        }

        // The player is parented to the seat while he rides (PlayerVehicleInteraction), next to this dummy.
        private Transform FindSeatedCharacter()
        {
            Transform seat = transform.parent;
            if (seat == null) return null;
            for (int i = 0; i < seat.childCount; i++)
            {
                Transform child = seat.GetChild(i);
                if (child != transform && child.TryGetComponent(out PlayerVehicleInteraction _)) return child;
            }
            return null;
        }
    }
}
