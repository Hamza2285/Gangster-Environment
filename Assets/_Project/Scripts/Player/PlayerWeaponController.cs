using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Project-side facade over Invector's weapon systems for the HUD and the weapon wheel.
    /// Read side: which shooter weapon is equipped and how much ammo it has (events).
    /// Write side: <see cref="EquipSlot"/> equips a weapon-wheel slot through Invector's vItemManager
    /// equip API (the same path the Invector inventory uses) - it never spawns weapons itself.
    /// Also reads the quick-switch inputs (number keys pick a wheel slot, the scroll wheel cycles the slots).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerWeaponController : MonoBehaviour
    {
        /// <summary>One weapon-wheel entry mapped to an Invector item ID (-1 = Unarmed).</summary>
        [Serializable]
        public class WeaponSlot
        {
            [SerializeField] private string displayName = "Unarmed";
            [Tooltip("Item ID in the player's vItemListData. Use -1 for Unarmed (holsters the current weapon).")]
            [SerializeField] private int itemId = -1;

            public string DisplayName => displayName;
            public int ItemId => itemId;
            public bool IsUnarmed => itemId < 0;
        }

        [Header("Invector References")]
        [SerializeField] private vShooterManager shooterManager;
        [SerializeField] private vItemManager itemManager;

        [Header("Equipment")]
        [Tooltip("Index of the Invector inventory equip area that holds the weapons (EquipMentArea_ShooterMelee = 0).")]
        [SerializeField, Min(0)] private int weaponEquipAreaIndex = 0;
        [Tooltip("Play Invector's draw/holster animations when equipping from the weapon wheel.")]
        [SerializeField] private bool playEquipAnimations = true;
        [Tooltip("Weapon wheel slots, in wheel order (clockwise from the top).")]
        [SerializeField] private List<WeaponSlot> weaponSlots = new List<WeaponSlot>();

        [Header("Quick Switch")]
        [Tooltip("Blocks the quick switch while the player's input is locked (vehicles, taxi, map) or the weapon wheel is open.")]
        [SerializeField] private PlayerManager playerManager;
        [Tooltip("One input per weapon slot, in the same order.")]
        [SerializeField] private GenericInput[] slotKeys =
        {
            new GenericInput("Alpha1", "", ""),
            new GenericInput("Alpha2", "", ""),
            new GenericInput("Alpha3", "", ""),
        };
        [SerializeField] private GenericInput cycleInput = new GenericInput("Mouse ScrollWheel", "", "");
        [Tooltip("Seconds between scroll-wheel switches, so one flick moves one slot.")]
        [SerializeField, Min(0f)] private float cycleCooldown = 0.2f;

        /// <summary>Raised when a shooter weapon is equipped. Args: weapon, isLeftHand.</summary>
        public event Action<vShooterWeapon, bool> OnWeaponEquipped;
        /// <summary>Raised when a shooter weapon is unequipped. Args: weapon, isLeftHand.</summary>
        public event Action<vShooterWeapon, bool> OnWeaponUnequipped;
        /// <summary>
        /// Raised (at most once per frame, in LateUpdate) after the equipped weapon or its ammo changed.
        /// Args: ammo in clip, extra (reserve) ammo. Listeners can also re-read <see cref="HasWeapon"/>.
        /// </summary>
        public event Action<int, int> OnAmmoChanged;

        public vShooterWeapon CurrentWeapon => shooterManager != null ? shooterManager.CurrentWeapon : null;
        public bool HasWeapon => CurrentWeapon != null;
        public int AmmoInClip => CurrentWeapon != null ? CurrentWeapon.ammoCount : 0;
        public int ClipSize => CurrentWeapon != null ? CurrentWeapon.clipSize : 0;
        public int ExtraAmmo => shooterManager != null && CurrentWeapon != null ? shooterManager.ExtraAmmo : 0;

        public IReadOnlyList<WeaponSlot> WeaponSlots => weaponSlots;

        /// <summary>False while Invector's inventory window is open (the wheel must not fight it).</summary>
        public bool CanChangeWeapon => itemManager != null && itemManager.inventory != null && !itemManager.inventory.isOpen;

        // Invector raises its equip/unequip/ammo notifications before it finishes updating its own state
        // (onUnequipWeapon fires before the weapon reference is cleared; reserve ammo totals update after
        // the ammo item events). Changes are therefore collected here and reported once in LateUpdate.
        private bool weaponStateDirty;
        private float nextCycleTime;

        private void Awake()
        {
            if (playerManager == null) Debug.LogWarning($"[{nameof(PlayerWeaponController)}] No PlayerManager on '{name}'; quick switch is off.", this);
            if (shooterManager == null || itemManager == null)
            {
                Debug.LogError($"[{nameof(PlayerWeaponController)}] Missing Invector references on '{name}' (vShooterManager, vItemManager).", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (shooterManager == null || itemManager == null) return;
            shooterManager.onEquipWeapon.AddListener(HandleEquip);
            shooterManager.onUnequipWeapon.AddListener(HandleUnequip);
            shooterManager.onShot.AddListener(HandleWeaponEvent);
            shooterManager.onFinishReloadWeapon.AddListener(HandleWeaponEvent);
            itemManager.onAddItem.AddListener(HandleItemChanged);
            itemManager.onChangeItemAmount.AddListener(HandleItemChanged);
            itemManager.onDestroyItem.AddListener(HandleItemRemoved);
        }

        private void OnDisable()
        {
            if (shooterManager == null || itemManager == null) return;
            shooterManager.onEquipWeapon.RemoveListener(HandleEquip);
            shooterManager.onUnequipWeapon.RemoveListener(HandleUnequip);
            shooterManager.onShot.RemoveListener(HandleWeaponEvent);
            shooterManager.onFinishReloadWeapon.RemoveListener(HandleWeaponEvent);
            itemManager.onAddItem.RemoveListener(HandleItemChanged);
            itemManager.onChangeItemAmount.RemoveListener(HandleItemChanged);
            itemManager.onDestroyItem.RemoveListener(HandleItemRemoved);
        }

        // Per-frame only for the quick-switch inputs.
        private void Update()
        {
            if (playerManager == null || playerManager.IsInputLocked || playerManager.IsInVehicle || playerManager.CurrentState == PlayerState.Dead) return;
            if (playerManager.Combat == null || !playerManager.Combat.CanSwitchWeapon || !CanChangeWeapon) return;

            for (int i = 0; i < slotKeys.Length && i < weaponSlots.Count; i++)
            {
                if (!slotKeys[i].GetButtonDown()) continue;
                if (i != GetCurrentSlotIndex() && IsSlotAvailable(i)) EquipSlot(i);
                return;
            }

            float scroll = cycleInput.GetAxisRaw();
            if (scroll == 0f || Time.unscaledTime < nextCycleTime) return;
            nextCycleTime = Time.unscaledTime + cycleCooldown;
            if (scroll > 0f) EquipNextSlot();
            else EquipPreviousSlot();
        }

        // Single bool check per frame; the event is only raised when something actually changed.
        private void LateUpdate()
        {
            if (!weaponStateDirty) return;
            weaponStateDirty = false;
            OnAmmoChanged?.Invoke(AmmoInClip, ExtraAmmo);
        }

        /// <summary>True when the slot can be selected (Unarmed always; weapons only when owned).</summary>
        public bool IsSlotAvailable(int slotIndex)
        {
            if (!IsValidSlotIndex(slotIndex)) return false;
            WeaponSlot slot = weaponSlots[slotIndex];
            return slot.IsUnarmed || (itemManager != null && itemManager.ContainItem(slot.ItemId));
        }

        /// <summary>Index of the wheel slot matching the weapon currently in the equip area (Unarmed slot if none).</summary>
        public int GetCurrentSlotIndex()
        {
            vEquipArea area = GetWeaponEquipArea();
            vItem equippedItem = area != null ? area.currentEquippedItem : null;
            int unarmedIndex = -1;
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                if (weaponSlots[i].IsUnarmed)
                {
                    if (unarmedIndex < 0) unarmedIndex = i;
                }
                else if (equippedItem != null && weaponSlots[i].ItemId == equippedItem.id)
                {
                    return i;
                }
            }
            return unarmedIndex;
        }

        /// <summary>Display name of the current weapon slot, or empty when nothing matches.</summary>
        public string GetCurrentSlotName()
        {
            int index = GetCurrentSlotIndex();
            return IsValidSlotIndex(index) ? weaponSlots[index].DisplayName : string.Empty;
        }

        /// <summary>
        /// Equips the given wheel slot through Invector's vItemManager (draw/holster animations, holsters,
        /// IK, ammo and shooter setup are all handled by Invector). Returns false if it could not equip.
        /// </summary>
        public bool EquipSlot(int slotIndex)
        {
            if (!enabled || !IsValidSlotIndex(slotIndex) || !CanChangeWeapon) return false;

            WeaponSlot slot = weaponSlots[slotIndex];
            if (slot.IsUnarmed)
            {
                Unarm();
                return true;
            }

            vItem item = FindOwnedItem(slot.ItemId);
            if (item == null)
            {
                Debug.LogWarning($"[{nameof(PlayerWeaponController)}] Cannot equip '{slot.DisplayName}': item ID {slot.ItemId} is not in the player's inventory.", this);
                return false;
            }

            vEquipArea area = GetWeaponEquipArea();
            if (area == null) return false;
            if (area.currentEquippedItem == item) return true;

            int equipSlotIndex = FindEquipSlotFor(area, item);
            itemManager.EquipItemToEquipSlot(weaponEquipAreaIndex, equipSlotIndex, item, true, !playEquipAnimations);
            return true;
        }

        /// <summary>Equips the next available slot after the current one (wraps around; on-screen "next weapon" button).</summary>
        public void EquipNextSlot() => CycleSlot(1);

        /// <summary>Equips the previous available slot before the current one (wraps around).</summary>
        public void EquipPreviousSlot() => CycleSlot(-1);

        private void CycleSlot(int direction)
        {
            int count = weaponSlots.Count;
            if (count == 0 || !CanChangeWeapon) return;
            int current = GetCurrentSlotIndex();
            for (int step = 1; step <= count; step++)
            {
                int index = ((current + step * direction) % count + count) % count;
                if (index != current && IsSlotAvailable(index))
                {
                    EquipSlot(index);
                    return;
                }
            }
        }

        /// <summary>Holsters/removes the current weapon from the weapon equip area (player goes unarmed).</summary>
        public void Unarm() => Unarm(!playEquipAnimations);

        /// <summary>Removes the current weapon; <paramref name="immediate"/> skips Invector's holster animation (e.g. getting into a vehicle).</summary>
        public void Unarm(bool immediate)
        {
            vEquipArea area = GetWeaponEquipArea();
            if (area == null || area.currentEquippedItem == null) return;
            itemManager.UnequipCurrentEquipedItem(weaponEquipAreaIndex, immediate);
        }

        private vItem FindOwnedItem(int itemId)
        {
            List<vItem> items = itemManager.items;
            if (items == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].id == itemId) return items[i];
            }
            return null;
        }

        // Prefer the slot that already holds the item, then an empty compatible slot, then the current slot.
        private static int FindEquipSlotFor(vEquipArea area, vItem item)
        {
            List<vEquipSlot> slots = area.equipSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].item == item) return i;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                vEquipSlot slot = slots[i];
                if (slot != null && slot.isValid && slot.item == null && slot.itemType.Contains(item.type)) return i;
            }
            return area.indexOfEquippedItem;
        }

        private vEquipArea GetWeaponEquipArea()
        {
            if (itemManager == null || itemManager.inventory == null) return null;
            vEquipArea[] areas = itemManager.inventory.equipAreas;
            if (areas == null || weaponEquipAreaIndex >= areas.Length)
            {
                Debug.LogWarning($"[{nameof(PlayerWeaponController)}] Weapon equip area index {weaponEquipAreaIndex} is not available (inventory not initialised yet?).", this);
                return null;
            }
            return areas[weaponEquipAreaIndex];
        }

        private bool IsValidSlotIndex(int index) => index >= 0 && index < weaponSlots.Count;

        private void HandleEquip(vShooterWeapon weapon, bool isLeftHand)
        {
            OnWeaponEquipped?.Invoke(weapon, isLeftHand);
            weaponStateDirty = true;
        }

        private void HandleUnequip(vShooterWeapon weapon, bool isLeftHand)
        {
            OnWeaponUnequipped?.Invoke(weapon, isLeftHand);
            weaponStateDirty = true;
        }

        private void HandleWeaponEvent(vShooterWeapon weapon) => weaponStateDirty = true;

        private void HandleItemChanged(vItem item)
        {
            if (item != null && item.type == vItemType.Ammo) weaponStateDirty = true;
        }

        private void HandleItemRemoved(vItem item, int amount) => HandleItemChanged(item);
    }
}
