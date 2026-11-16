using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vShooter;
using OpenworldCrime.Weapons;
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
            public bool IsUnarmed => itemId < 0 && !IsCategorySlot;

            /// <summary>Catalog mode: the GTA weapon group this slot stands for (null for hand-authored slots).</summary>
            public WeaponCategory? Category { get; private set; }
            /// <summary>Catalog mode: the weapon of this group that the slot currently equips.</summary>
            public WeaponDefinition Selected { get; private set; }
            public Sprite Icon => Selected != null ? Selected.icon : null;
            private bool IsCategorySlot => Category.HasValue && Category.Value != WeaponCategory.Unarmed;

            public WeaponSlot() { }

            internal WeaponSlot(WeaponCategory category)
            {
                Category = category;
                SetSelected(null);
            }

            internal void SetSelected(WeaponDefinition weapon)
            {
                Selected = weapon;
                itemId = weapon != null ? weapon.itemId : -1;
                displayName = weapon != null ? weapon.displayName : CategoryLabel(Category ?? WeaponCategory.Unarmed);
            }
        }

        /// <summary>Short wheel / HUD label for a weapon group.</summary>
        public static string CategoryLabel(WeaponCategory category)
        {
            switch (category)
            {
                case WeaponCategory.Unarmed: return "Unarmed";
                case WeaponCategory.Melee: return "Melee";
                case WeaponCategory.Pistol: return "Pistols";
                case WeaponCategory.SMG: return "SMGs";
                case WeaponCategory.Shotgun: return "Shotguns";
                case WeaponCategory.AssaultRifle: return "Rifles";
                case WeaponCategory.Sniper: return "Snipers";
                default: return "Heavy";
            }
        }

        [Header("Invector References")]
        [SerializeField] private vShooterManager shooterManager;
        [SerializeField] private vItemManager itemManager;

        [Header("Equipment")]
        [Tooltip("Index of the Invector inventory equip area that holds the weapons (EquipMentArea_ShooterMelee = 0).")]
        [SerializeField, Min(0)] private int weaponEquipAreaIndex = 0;
        [Tooltip("Play Invector's draw/holster animations when equipping from the weapon wheel.")]
        [SerializeField] private bool playEquipAnimations = true;
        [Tooltip("Weapon wheel slots, in wheel order (clockwise from the top). Ignored when an Arsenal is assigned: the wheel then has one slot per WeaponCategory, filled from the catalog.")]
        [SerializeField] private List<WeaponSlot> weaponSlots = new List<WeaponSlot>();
        [Tooltip("Catalog-driven GTA wheel: one slot per weapon group; pressing a group's key again cycles inside the group.")]
        [SerializeField] private PlayerArsenal arsenal;
        [Tooltip("Start the game with no weapon in hand (attack = punch), GTA style.")]
        [SerializeField] private bool startUnarmed = true;

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
        /// <summary>Raised when the wheel slots' contents change (weapon bought, group selection changed).</summary>
        public event Action OnSlotsChanged;
        public bool UsesCategories => arsenal != null;

        /// <summary>False while Invector's inventory window is open (the wheel must not fight it).</summary>
        public bool CanChangeWeapon => !WeaponsBlocked && itemManager != null && itemManager.inventory != null && !itemManager.inventory.isOpen;

        /// <summary>True while weapons are forced holstered (e.g. inside a shop outside its shooting range).</summary>
        public bool WeaponsBlocked { get; private set; }

        /// <summary>Forces weapons holstered (instantly) and blocks drawing them until unblocked.</summary>
        public void SetWeaponsBlocked(bool blocked)
        {
            WeaponsBlocked = blocked;
            if (blocked) Unarm(true);
        }

        // Invector raises its equip/unequip/ammo notifications before it finishes updating its own state
        // (onUnequipWeapon fires before the weapon reference is cleared; reserve ammo totals update after
        // the ammo item events). Changes are therefore collected here and reported once in LateUpdate.
        private bool weaponStateDirty;
        private float nextCycleTime;

        private readonly List<WeaponDefinition> categoryBuffer = new List<WeaponDefinition>();

        private void Awake()
        {
            if (arsenal != null && arsenal.Catalog != null) BuildCategorySlots();
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
            if (arsenal != null)
            {
                arsenal.OnArsenalReady += HandleArsenalReady;
                arsenal.OnArsenalChanged += RefreshCategorySlots;
            }
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
            if (arsenal != null)
            {
                arsenal.OnArsenalReady -= HandleArsenalReady;
                arsenal.OnArsenalChanged -= RefreshCategorySlots;
            }
        }

        // Per-frame only for the quick-switch inputs.
        private void Update()
        {
            if (playerManager == null || playerManager.IsInputLocked || playerManager.IsInVehicle || playerManager.CurrentState == PlayerState.Dead) return;
            if (playerManager.Combat == null || !playerManager.Combat.CanSwitchWeapon || !CanChangeWeapon) return;

            for (int i = 0; i < slotKeys.Length && i < weaponSlots.Count; i++)
            {
                if (!slotKeys[i].GetButtonDown()) continue;
                if (i != GetCurrentSlotIndex())
                {
                    if (IsSlotAvailable(i)) EquipSlot(i);
                }
                else if (UsesCategories && CycleWithinSlot(i, 1))
                {
                    EquipSlot(i);
                }
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
            if (slot.Category.HasValue && slot.Category.Value == WeaponCategory.Unarmed) return true;
            return slot.IsUnarmed || (slot.ItemId >= 0 && itemManager != null && itemManager.ContainItem(slot.ItemId));
        }

        /// <summary>
        /// Catalog mode: moves the slot's selection to the next/previous owned weapon of its group (does not equip).
        /// Returns true when the selection changed.
        /// </summary>
        public bool CycleWithinSlot(int slotIndex, int direction)
        {
            if (!UsesCategories || !IsValidSlotIndex(slotIndex)) return false;
            WeaponSlot slot = weaponSlots[slotIndex];
            if (!slot.Category.HasValue) return false;
            arsenal.Catalog.GetCategory(slot.Category.Value, categoryBuffer);
            int count = categoryBuffer.Count;
            if (count < 2) return false;
            int current = Mathf.Max(0, categoryBuffer.IndexOf(slot.Selected));
            for (int step = 1; step < count; step++)
            {
                WeaponDefinition candidate = categoryBuffer[((current + step * direction) % count + count) % count];
                if (!arsenal.OwnsWeapon(candidate)) continue;
                slot.SetSelected(candidate);
                OnSlotsChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>Catalog mode: makes <paramref name="weapon"/> its group's wheel choice and equips it.</summary>
        public bool EquipWeapon(WeaponDefinition weapon)
        {
            if (!UsesCategories || weapon == null) return false;
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                if (weaponSlots[i].Category != weapon.category) continue;
                weaponSlots[i].SetSelected(weapon);
                OnSlotsChanged?.Invoke();
                return EquipSlot(i);
            }
            return false;
        }

        /// <summary>Catalog definition of the equipped weapon (null when unarmed or not in the catalog).</summary>
        public WeaponDefinition GetCurrentWeaponDefinition()
        {
            if (!UsesCategories) return null;
            vEquipArea area = GetWeaponEquipArea();
            vItem equippedItem = area != null ? area.currentEquippedItem : null;
            return equippedItem != null ? arsenal.Catalog.FindByItemId(equippedItem.id) : null;
        }

        private void BuildCategorySlots()
        {
            weaponSlots = new List<WeaponSlot>();
            // One wheel group per category the catalog sells (Unarmed / fists always); e.g. no Melee group without melee weapons.
            foreach (WeaponCategory category in Enum.GetValues(typeof(WeaponCategory)))
            {
                if (category != WeaponCategory.Unarmed && !HasCatalogWeapon(category)) continue;
                weaponSlots.Add(new WeaponSlot(category));
            }
            if (slotKeys.Length < weaponSlots.Count)
            {
                var keys = new GenericInput[weaponSlots.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = i < slotKeys.Length ? slotKeys[i] : new GenericInput("Alpha" + (i + 1), "", "");
                slotKeys = keys;
            }
        }

        private bool HasCatalogWeapon(WeaponCategory category)
        {
            arsenal.Catalog.GetCategory(category, categoryBuffer);
            return categoryBuffer.Count > 0;
        }

        // GTA start: fists out. Invector's start items land in the weapon equip slot (holstered but "equipped"), which made the
        // attack input draw and fire the pistol instead of punching.
        private void HandleArsenalReady()
        {
            RefreshCategorySlots();
            if (startUnarmed) Unarm(true);
        }

        // Keeps each group's selection on an owned weapon (first owned one when the current choice is not owned).
        private void RefreshCategorySlots()
        {
            if (!UsesCategories) return;
            bool changed = false;
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                WeaponSlot slot = weaponSlots[i];
                if (!slot.Category.HasValue || slot.Category.Value == WeaponCategory.Unarmed) continue;
                if (slot.Selected != null && arsenal.OwnsWeapon(slot.Selected)) continue;
                arsenal.Catalog.GetCategory(slot.Category.Value, categoryBuffer);
                WeaponDefinition owned = null;
                for (int w = 0; w < categoryBuffer.Count && owned == null; w++) if (arsenal.OwnsWeapon(categoryBuffer[w])) owned = categoryBuffer[w];
                if (owned == slot.Selected) continue;
                slot.SetSelected(owned);
                changed = true;
            }
            if (changed) OnSlotsChanged?.Invoke();
        }

        /// <summary>Index of the wheel slot matching the weapon currently in the equip area (Unarmed slot if none).</summary>
        public int GetCurrentSlotIndex()
        {
            vEquipArea area = GetWeaponEquipArea();
            vItem equippedItem = area != null ? area.currentEquippedItem : null;
            int unarmedIndex = -1;
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                if (weaponSlots[i].IsUnarmed || weaponSlots[i].Category == WeaponCategory.Unarmed)
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
            if (slot.IsUnarmed || slot.Category == WeaponCategory.Unarmed)
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

        /// <summary>
        /// Makes sure a firearm is in hand (e.g. at the shooting range): keeps the current one, otherwise equips the first
        /// owned firearm group of the wheel. Returns false when the player owns no firearm.
        /// </summary>
        public bool DrawFirearm()
        {
            if (CurrentWeapon != null) return true;
            for (int i = 0; i < weaponSlots.Count; i++)
            {
                WeaponCategory? category = weaponSlots[i].Category;
                bool firearm = category.HasValue ? category.Value != WeaponCategory.Unarmed && category.Value != WeaponCategory.Melee : !weaponSlots[i].IsUnarmed;
                if (firearm && IsSlotAvailable(i) && EquipSlot(i)) return true;
            }
            return false;
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
