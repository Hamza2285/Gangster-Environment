using System;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Shop
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Gun shop service counter inside an interior scene. Interacting asks the city's <see cref="WeaponShopController"/>
    /// (through a static event: interiors are separate scenes) to open the shop on this counter's displayed weapons
    /// (wall rack and counter top), which the shop camera then browses.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopCounter : MonoBehaviour, IPlayerInteractable
    {
        [SerializeField] private string prompt = "Browse Weapons";
        [Tooltip("Weapons on display that the shop camera browses (one per catalog weapon).")]
        [SerializeField] private ShopDisplaySlot[] displaySlots = new ShopDisplaySlot[0];
        [Tooltip("Spot light that follows the selected weapon while browsing (disabled otherwise).")]
        [SerializeField] private Light highlightLight;
        [Tooltip("Optional sound played on a purchase (cash register).")]
        [SerializeField] private AudioSource purchaseAudio;

        /// <summary>Raised when the player uses a counter.</summary>
        public static event Action<ShopCounter> OnBrowseRequested;

        public string InteractionPrompt => prompt;
        public Light HighlightLight => highlightLight;

        private void Awake()
        {
            if (displaySlots.Length == 0) Debug.LogWarning($"[{nameof(ShopCounter)}] '{name}' has no display slots; the shop cannot open.", this);
            if (highlightLight != null) highlightLight.enabled = false;
        }

        public bool CanInteract(PlayerManager player) => isActiveAndEnabled && OnBrowseRequested != null && displaySlots.Length > 0;

        public void Interact(PlayerManager player)
        {
            if (CanInteract(player)) OnBrowseRequested?.Invoke(this);
        }

        /// <summary>The display slot showing the weapon with the given catalog id (null when it is not on display).</summary>
        public ShopDisplaySlot FindSlot(string weaponId)
        {
            for (int i = 0; i < displaySlots.Length; i++)
                if (displaySlots[i] != null && displaySlots[i].WeaponId == weaponId) return displaySlots[i];
            return null;
        }

        public void PlayPurchaseSound()
        {
            if (purchaseAudio != null && purchaseAudio.clip != null) purchaseAudio.PlayOneShot(purchaseAudio.clip);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnBrowseRequested = null;
    }
}
