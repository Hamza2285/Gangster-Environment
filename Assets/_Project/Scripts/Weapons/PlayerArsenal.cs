using System;
using System.Collections;
using System.Collections.Generic;
using Invector.vItemManager;
using Invector.vShooter;
using OpenworldCrime.Economy;
using UnityEngine;

namespace OpenworldCrime.Weapons
{
    /// <summary>
    /// The player's weapons and ammo as the game sees them (per-player component on the player root, not a Singleton).
    /// Bridges the <see cref="WeaponCatalog"/> to Invector's vItemManager: the shop grants weapons and ammo through it, and it
    /// saves / restores owned weapons, reserve ammo and loaded ammo through <see cref="SaveManager"/>.
    /// Invector stays the owner of the actual inventory items.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerArsenal : MonoBehaviour
    {
        [SerializeField] private WeaponCatalog catalog;
        [SerializeField] private vItemManager itemManager;
        [SerializeField] private vShooterManager shooterManager;

        /// <summary>Raised after the saved arsenal has been applied to the inventory (the wheel / shop can refresh).</summary>
        public event Action OnArsenalReady;
        /// <summary>Raised when a weapon is granted (bought). Arg: the definition.</summary>
        public event Action<WeaponDefinition> OnWeaponGranted;
        /// <summary>Raised when owned weapons or reserve ammo change.</summary>
        public event Action OnArsenalChanged;

        public WeaponCatalog Catalog => catalog;
        public bool IsReady { get; private set; }

        private bool snapshotPending;

        private void Awake()
        {
            if (catalog == null || itemManager == null || shooterManager == null)
            {
                Debug.LogError($"[{nameof(PlayerArsenal)}] Missing references on '{name}' (WeaponCatalog, vItemManager, vShooterManager).", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!enabled) return;
            itemManager.onAddItem.AddListener(HandleItemChanged);
            itemManager.onChangeItemAmount.AddListener(HandleItemChanged);
            itemManager.onDestroyItem.AddListener(HandleItemRemoved);
            shooterManager.onUnequipWeapon.AddListener(HandleWeaponUnequipped);
        }

        private void OnDisable()
        {
            if (itemManager != null)
            {
                itemManager.onAddItem.RemoveListener(HandleItemChanged);
                itemManager.onChangeItemAmount.RemoveListener(HandleItemChanged);
                itemManager.onDestroyItem.RemoveListener(HandleItemRemoved);
            }
            if (shooterManager != null) shooterManager.onUnequipWeapon.RemoveListener(HandleWeaponUnequipped);
        }

        private IEnumerator Start()
        {
            // vItemManager.Start waits for the end of the first frame, then REPLACES its item list and adds the start items.
            // Restoring earlier would be wiped, so wait until that end-of-frame step has certainly run.
            // Wait for that list to hold the start items (or ~2 s when there are none).
            bool hasStartItems = itemManager.startItems != null && itemManager.startItems.Count > 0;
            for (int frame = 0; frame < 120; frame++)
            {
                yield return null;
                if (itemManager.items != null && (hasStartItems ? itemManager.items.Count > 0 : frame > 3)) break;
            }
            yield return null;
            RestoreFromSave();
            IsReady = true;
            OnArsenalReady?.Invoke();
        }

        private void LateUpdate()
        {
            if (!snapshotPending) return;
            snapshotPending = false;
            WriteSnapshot();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && IsReady) WriteSnapshot();
        }

        public bool OwnsWeapon(WeaponDefinition weapon) => weapon != null && (weapon.itemId < 0 || itemManager.ContainItem(weapon.itemId));

        public int GetReserveAmmo(WeaponDefinition weapon) => weapon != null && weapon.UsesAmmo ? itemManager.GetAllAmount(weapon.ammoItemId) : 0;

        /// <summary>Adds the weapon to the inventory (no equip). Returns false if already owned or unknown.</summary>
        public bool GrantWeapon(WeaponDefinition weapon)
        {
            if (!enabled || weapon == null || weapon.itemId < 0 || OwnsWeapon(weapon)) return false;
            itemManager.AddItem(new ItemReference(weapon.itemId) { amount = 1, addToEquipArea = false }, true);
            OnWeaponGranted?.Invoke(weapon);
            return true;
        }

        /// <summary>Adds reserve rounds for the weapon's ammo type, capped at its max. Returns the rounds actually added.</summary>
        public int AddAmmo(WeaponDefinition weapon, int rounds)
        {
            if (!enabled || weapon == null || !weapon.UsesAmmo || rounds <= 0) return 0;
            int room = Mathf.Max(0, weapon.maxReserveAmmo - GetReserveAmmo(weapon));
            int add = Mathf.Min(room, rounds);
            if (add > 0) AddAmmoItems(weapon.ammoItemId, add);
            return add;
        }

        private void AddAmmoItems(int ammoItemId, int amount)
        {
            if (amount <= 0) return;
            itemManager.AddItem(new ItemReference(ammoItemId) { amount = amount, addToEquipArea = false }, true);
            shooterManager.UpdateTotalAmmo();
        }

        private void RestoreFromSave()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null) return;
            GameSaveData data = save.Data;
            if (!data.hasArsenal)
            {
                WriteSnapshot();
                return;
            }

            for (int i = 0; i < data.ownedWeapons.Count; i++)
            {
                int id = data.ownedWeapons[i];
                if (!itemManager.ContainItem(id) && itemManager.itemListData.items.Exists(it => it.id == id))
                    itemManager.AddItem(new ItemReference(id) { amount = 1, addToEquipArea = false }, true);
            }

            for (int i = 0; i < data.reserveAmmo.Count; i++)
            {
                IntPair pair = data.reserveAmmo[i];
                int current = itemManager.GetAllAmount(pair.key);
                if (pair.value > current) AddAmmoItems(pair.key, pair.value - current);
                else if (pair.value < current) RemoveAmmo(pair.key, current - pair.value);
            }

            for (int i = 0; i < data.clipAmmo.Count; i++)
            {
                vItem item = itemManager.GetItem(data.clipAmmo[i].key);
                vItemAttribute attribute = item != null ? item.GetItemAttribute(vItemAttributes.AmmoCount) : null;
                if (attribute != null) attribute.value = data.clipAmmo[i].value;
            }
            shooterManager.UpdateTotalAmmo();
        }

        private void RemoveAmmo(int ammoItemId, int amount)
        {
            List<vItem> items = itemManager.items;
            for (int i = items.Count - 1; i >= 0 && amount > 0; i--)
            {
                vItem item = items[i];
                if (item == null || item.id != ammoItemId) continue;
                int take = Mathf.Min(item.amount, amount);
                itemManager.DestroyItem(item, take);
                amount -= take;
            }
        }

        private void WriteSnapshot()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null) return;
            GameSaveData data = save.Data;
            data.hasArsenal = true;
            data.ownedWeapons.Clear();
            data.reserveAmmo.Clear();
            data.clipAmmo.Clear();

            vShooterWeapon current = shooterManager.CurrentWeapon;
            IReadOnlyList<WeaponDefinition> weapons = catalog.Weapons;
            for (int i = 0; i < weapons.Count; i++)
            {
                WeaponDefinition weapon = weapons[i];
                if (weapon.itemId < 0) continue;
                vItem item = itemManager.GetItem(weapon.itemId);
                if (item == null) continue;
                data.ownedWeapons.Add(weapon.itemId);
                vItemAttribute attribute = item.GetItemAttribute(vItemAttributes.AmmoCount);
                int loaded = attribute != null ? attribute.value : 0;
                if (current != null && IsEquippedItem(item)) loaded = current.ammoCount;
                data.clipAmmo.Add(new IntPair(weapon.itemId, loaded));
                if (weapon.UsesAmmo && !ContainsKey(data.reserveAmmo, weapon.ammoItemId))
                    data.reserveAmmo.Add(new IntPair(weapon.ammoItemId, itemManager.GetAllAmount(weapon.ammoItemId)));
            }
            save.RequestSave();
        }

        private bool IsEquippedItem(vItem item)
        {
            vEquipArea[] areas = itemManager.inventory != null ? itemManager.inventory.equipAreas : null;
            if (areas == null) return false;
            for (int i = 0; i < areas.Length; i++) if (areas[i] != null && areas[i].currentEquippedItem == item) return true;
            return false;
        }

        private static bool ContainsKey(List<IntPair> list, int key)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].key == key) return true;
            return false;
        }

        private void HandleItemChanged(vItem item)
        {
            if (!IsReady) return;
            snapshotPending = true;
            OnArsenalChanged?.Invoke();
        }

        private void HandleItemRemoved(vItem item, int amount) => HandleItemChanged(item);

        private void HandleWeaponUnequipped(vShooterWeapon weapon, bool isLeftHand)
        {
            if (IsReady) snapshotPending = true;
        }
    }
}
