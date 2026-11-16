using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Weapons
{
    /// <summary>GTA-style weapon groups. The order is the weapon wheel order (clockwise from the top).</summary>
    public enum WeaponCategory
    {
        Unarmed = 0,
        Melee = 1,
        Pistol = 2,
        SMG = 3,
        Shotgun = 4,
        AssaultRifle = 5,
        Sniper = 6,
        Heavy = 7,
    }

    /// <summary>
    /// One weapon as the shop, the wheel and the HUD see it. Pure configuration: owned / equipped / ammo state lives on
    /// the player (<see cref="PlayerArsenal"/>, Invector's vItemManager), never in this asset.
    /// The combat stats are written into the Invector weapon prefab by Tools > OpenworldCrime > Weapons > Build Weapons From Catalog.
    /// </summary>
    [Serializable]
    public class WeaponDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable save key. Never change it after release.")]
        public string id = "weapon";
        public string displayName = "Weapon";
        public WeaponCategory category = WeaponCategory.Pistol;
        [Tooltip("Item ID in the player's vItemListData.")]
        public int itemId = -1;
        [Tooltip("Ammo item ID in the vItemListData (-1 = melee / no ammo).")]
        public int ammoItemId = -1;
        public Sprite icon;
        [Tooltip("Model shown on the shop's display stand and used to render the icon (no Invector components).")]
        public GameObject displayPrefab;

        [Header("Shop")]
        [Min(0)] public int price = 1000;
        [Tooltip("Price of one magazine.")]
        [Min(0)] public int ammoPrice = 50;
        [Tooltip("Rounds per bought magazine (0 = clip size).")]
        [Min(0)] public int ammoPerPurchase;
        [Tooltip("Reserve ammo limit; the shop refuses ammo above it.")]
        [Min(0)] public int maxReserveAmmo = 300;
        [Tooltip("Hidden from the shop (e.g. starter weapons that cannot be bought again).")]
        public bool hiddenInShop;

        [Header("Combat (applied to the Invector prefab)")]
        [Min(1)] public int minDamage = 15;
        [Min(1)] public int maxDamage = 30;
        [Tooltip("Seconds between shots.")]
        [Min(0.03f)] public float shotInterval = 0.25f;
        public bool automatic;
        [Min(1)] public int clipSize = 12;
        [Min(1)] public int pelletsPerShot = 1;
        [Tooltip("Spread in degrees.")]
        [Min(0f)] public float spread;
        [Min(0f)] public float recoilUp = 1f;
        [Min(0f)] public float recoilSide = 0.5f;
        [Min(1f)] public float range = 50f;

        public int AmmoPerPurchase => ammoPerPurchase > 0 ? ammoPerPurchase : clipSize;
        public bool UsesAmmo => ammoItemId >= 0;

        // 0-1 bars for the shop UI, normalised against sensible GTA-like extremes.
        public float DamageBar => Mathf.Clamp01(maxDamage * pelletsPerShot / 110f);
        public float FireRateBar => category == WeaponCategory.Melee ? 0.3f : Mathf.Clamp01(0.1f / shotInterval);
        public float AccuracyBar => Mathf.Clamp01(1f - spread / 8f - recoilUp / 20f);
        public float RangeBar => Mathf.Clamp01(range / 150f);
    }

    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "OpenworldCrime/Weapons/Weapon Catalog")]
    public class WeaponCatalog : ScriptableObject
    {
        [SerializeField] private List<WeaponDefinition> weapons = new List<WeaponDefinition>();

        public IReadOnlyList<WeaponDefinition> Weapons => weapons;

        public WeaponDefinition FindById(string id)
        {
            for (int i = 0; i < weapons.Count; i++) if (weapons[i].id == id) return weapons[i];
            return null;
        }

        public WeaponDefinition FindByItemId(int itemId)
        {
            for (int i = 0; i < weapons.Count; i++) if (weapons[i].itemId == itemId) return weapons[i];
            return null;
        }

        /// <summary>Fills <paramref name="result"/> with the weapons of one category, in catalog order.</summary>
        public void GetCategory(WeaponCategory category, List<WeaponDefinition> result)
        {
            result.Clear();
            for (int i = 0; i < weapons.Count; i++) if (weapons[i].category == category) result.Add(weapons[i]);
        }

#if UNITY_EDITOR
        public List<WeaponDefinition> EditorWeapons => weapons;
#endif
    }
}
