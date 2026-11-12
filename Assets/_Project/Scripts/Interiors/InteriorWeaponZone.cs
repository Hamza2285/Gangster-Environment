using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Trigger volume inside an interior where weapons may be drawn and fired (the shooting range). Everywhere else in an
    /// interior the <see cref="InteriorTravelManager"/> keeps weapons holstered.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class InteriorWeaponZone : MonoBehaviour
    {
        private int insideCount;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount++;
            if (insideCount == 1 && InteriorTravelManager.Instance != null) InteriorTravelManager.Instance.SetWeaponsAllowed(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount = Mathf.Max(0, insideCount - 1);
            if (insideCount == 0 && InteriorTravelManager.Instance != null) InteriorTravelManager.Instance.SetWeaponsAllowed(false);
        }

        private void OnDisable()
        {
            if (insideCount > 0 && InteriorTravelManager.Instance != null) InteriorTravelManager.Instance.SetWeaponsAllowed(false);
            insideCount = 0;
        }

        // Only the player's own capsule counts (not his weapons' hitboxes or ragdoll parts).
        private static bool IsPlayer(Collider other) => other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<PlayerManager>() != null && other is CapsuleCollider;

        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
