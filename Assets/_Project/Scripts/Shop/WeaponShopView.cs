using System.Collections.Generic;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Shop
{
    /// <summary>
    /// Visuals of the GTA-style gun shop side panel: category bar (with arrows), the category's weapons (name + price /
    /// OWNED), stats bars, ammo line and Buy / Ammo / Fill. Displays the <see cref="WeaponShopController"/>'s state and
    /// forwards presses to it; it never changes money or inventory itself. Rows are built once from a disabled template row
    /// and reused; the panel shrinks to the category's row count.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponShopView : MonoBehaviour
    {
        [SerializeField] private WeaponShopController controller;

        [Header("Panel Layout")]
        [Tooltip("Panel background; its height follows the number of rows.")]
        [SerializeField] private RectTransform panel;
        [Tooltip("Block under the list (stats, ammo, buttons); moved below the last row.")]
        [SerializeField] private RectTransform details;
        [Tooltip("Height above the first row (header + category bar).")]
        [SerializeField, Min(0f)] private float listTop = 176f;
        [SerializeField, Min(0f)] private float detailsGap = 14f;

        [Header("Category Bar")]
        [SerializeField] private Text categoryText;
        [SerializeField] private Text categoryCountText;
        [SerializeField] private Button previousCategoryButton;
        [SerializeField] private Button nextCategoryButton;

        [Header("List")]
        [Tooltip("Disabled template row: Button + Image, children 'Name' and 'Price' (Text).")]
        [SerializeField] private Button rowTemplate;
        [SerializeField] private RectTransform rowContainer;
        [SerializeField, Min(10f)] private float rowHeight = 52f;
        [SerializeField] private Color rowNormal = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color rowSelected = new Color(0.94f, 0.94f, 0.94f, 0.96f);
        [SerializeField] private Color textNormal = Color.white;
        [SerializeField] private Color textSelected = new Color(0.08f, 0.08f, 0.08f, 1f);
        [SerializeField] private Color priceColor = new Color(0.45f, 1f, 0.45f);
        [SerializeField] private Color ownedColor = new Color(1f, 0.8f, 0.3f);

        [Header("Details")]
        [SerializeField] private Text ammoText;
        [SerializeField] private Text messageText;
        [SerializeField] private Text moneyText;
        [Tooltip("Left-pivoted fill bars: damage, fire rate, accuracy, range. Their X scale is the stat value.")]
        [SerializeField] private RectTransform[] statBars = new RectTransform[4];

        [Header("Buttons")]
        [SerializeField] private Button buyButton;
        [SerializeField] private Text buyLabel;
        [SerializeField] private Button ammoButton;
        [SerializeField] private Text ammoLabel;
        [SerializeField] private Button fillButton;
        [SerializeField] private Text fillLabel;
        [SerializeField] private Button closeButton;
        [SerializeField, Min(0.5f)] private float messageDuration = 2f;

        private class Row
        {
            public Button button;
            public Image background;
            public Text name;
            public Text price;
            public WeaponDefinition weapon;
        }

        private readonly List<Row> rows = new List<Row>();
        private float messageTimer;

        private void Awake()
        {
            if (controller == null || rowTemplate == null || rowContainer == null || panel == null || details == null)
            {
                Debug.LogError($"[{nameof(WeaponShopView)}] Missing references on '{name}'.", this);
                enabled = false;
                return;
            }
            rowTemplate.gameObject.SetActive(false);
            if (previousCategoryButton != null) previousCategoryButton.onClick.AddListener(() => controller.ChangeCategory(-1));
            if (nextCategoryButton != null) nextCategoryButton.onClick.AddListener(() => controller.ChangeCategory(1));
            if (buyButton != null) buyButton.onClick.AddListener(() => controller.BuySelectedWeapon());
            if (ammoButton != null) ammoButton.onClick.AddListener(() => controller.BuySelectedAmmo());
            if (fillButton != null) fillButton.onClick.AddListener(() => controller.FillSelectedAmmo());
            if (closeButton != null) closeButton.onClick.AddListener(() => controller.Close());
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnSelectionChanged += HandleSelectionChanged;
            controller.OnPurchaseResult += HandlePurchaseResult;
            MoneyManager.OnMoneyChanged += HandleMoneyChanged;
            if (controller.Arsenal != null) controller.Arsenal.OnArsenalChanged += Refresh;
            messageTimer = 0f;
            if (messageText != null) messageText.text = string.Empty;
            if (MoneyManager.Instance != null && moneyText != null) moneyText.text = "$" + MoneyManager.Instance.Balance.ToString("N0");
            Rebuild();
        }

        private void OnDisable()
        {
            if (controller == null) return;
            controller.OnSelectionChanged -= HandleSelectionChanged;
            controller.OnPurchaseResult -= HandlePurchaseResult;
            MoneyManager.OnMoneyChanged -= HandleMoneyChanged;
            if (controller.Arsenal != null) controller.Arsenal.OnArsenalChanged -= Refresh;
        }

        // Per-frame only for the purchase message timeout while the panel is open.
        private void Update()
        {
            if (messageTimer <= 0f) return;
            messageTimer -= Time.unscaledDeltaTime;
            if (messageTimer <= 0f && messageText != null) messageText.text = string.Empty;
        }

        private void HandleSelectionChanged(WeaponDefinition weapon) => Rebuild();

        // Rows of the current category, the panel height and the category bar.
        private void Rebuild()
        {
            if (!isActiveAndEnabled) return;
            IReadOnlyList<WeaponDefinition> weapons = controller.CategoryWeapons;
            while (rows.Count < weapons.Count) rows.Add(CreateRow());
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < weapons.Count;
                if (rows[i].button.gameObject.activeSelf != used) rows[i].button.gameObject.SetActive(used);
                rows[i].weapon = used ? weapons[i] : null;
            }
            float listHeight = weapons.Count * rowHeight;
            rowContainer.sizeDelta = new Vector2(rowContainer.sizeDelta.x, listHeight);
            details.anchoredPosition = new Vector2(details.anchoredPosition.x, -(listTop + listHeight + detailsGap));
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, listTop + listHeight + detailsGap + details.sizeDelta.y);

            if (categoryText != null) categoryText.text = CategoryTitle(controller.CurrentCategory);
            if (categoryCountText != null) categoryCountText.text = (controller.CategoryIndex + 1) + " / " + controller.CategoryCount;
            Refresh();
        }

        private Row CreateRow()
        {
            Button button = Instantiate(rowTemplate, rowContainer);
            var row = new Row
            {
                button = button,
                background = button.GetComponent<Image>(),
                name = FindText(button.transform, "Name"),
                price = FindText(button.transform, "Price"),
            };
            ((RectTransform)button.transform).anchoredPosition = new Vector2(0f, -rows.Count * rowHeight);
            button.onClick.AddListener(() => { if (row.weapon != null) controller.Select(row.weapon); });
            return row;
        }

        private static Text FindText(Transform parent, string child)
        {
            Transform t = parent.Find(child);
            return t != null ? t.GetComponent<Text>() : null;
        }

        private void Refresh()
        {
            if (!isActiveAndEnabled) return;
            WeaponDefinition selected = controller.Selected;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.weapon == null) continue;
                bool isSelected = row.weapon == selected;
                bool owned = controller.IsOwned(row.weapon);
                if (row.background != null) row.background.color = isSelected ? rowSelected : rowNormal;
                if (row.name != null)
                {
                    row.name.text = row.weapon.displayName;
                    row.name.color = isSelected ? textSelected : textNormal;
                }
                if (row.price != null)
                {
                    row.price.text = owned ? "OWNED" : "$" + row.weapon.price.ToString("N0");
                    Color color = owned ? ownedColor : priceColor;
                    row.price.color = isSelected ? Color.Lerp(color, textSelected, 0.55f) : color;
                }
            }

            bool has = selected != null;
            bool isOwned = has && controller.IsOwned(selected);
            if (ammoText != null)
            {
                ammoText.text = !has ? string.Empty
                    : !selected.UsesAmmo ? PlayerWeaponController.CategoryLabel(selected.category) + "  -  no ammo needed"
                    : "AMMO  " + controller.GetReserve(selected) + " / " + selected.maxReserveAmmo + "      CLIP  " + selected.clipSize;
            }
            SetBar(0, has ? selected.DamageBar : 0f);
            SetBar(1, has ? selected.FireRateBar : 0f);
            SetBar(2, has ? selected.AccuracyBar : 0f);
            SetBar(3, has ? selected.RangeBar : 0f);

            int money = MoneyManager.Instance != null ? MoneyManager.Instance.Balance : 0;
            SetButton(buyButton, buyLabel, has, isOwned ? "OWNED" : has ? "BUY  $" + selected.price.ToString("N0") : "BUY", has && !isOwned && money >= selected.price);
            bool ammoUsable = has && isOwned && selected.UsesAmmo;
            bool full = ammoUsable && controller.IsAmmoFull(selected);
            SetButton(ammoButton, ammoLabel, has && selected.UsesAmmo, full ? "FULL" : has && selected.UsesAmmo ? "+" + selected.AmmoPerPurchase + "  $" + selected.ammoPrice : "AMMO", ammoUsable && !full && money >= selected.ammoPrice);
            SetButton(fillButton, fillLabel, has && selected.UsesAmmo, full ? "FULL" : ammoUsable ? "FILL  $" + controller.GetFillCost(selected).ToString("N0") : "FILL", ammoUsable && !full && money >= selected.ammoPrice);
        }

        private static string CategoryTitle(WeaponCategory category)
        {
            switch (category)
            {
                case WeaponCategory.Pistol: return "HANDGUNS";
                case WeaponCategory.SMG: return "SUBMACHINE GUNS";
                case WeaponCategory.Shotgun: return "SHOTGUNS";
                case WeaponCategory.AssaultRifle: return "ASSAULT RIFLES";
                case WeaponCategory.Sniper: return "SNIPER RIFLES";
                case WeaponCategory.Heavy: return "HEAVY WEAPONS";
                case WeaponCategory.Melee: return "MELEE";
                default: return category.ToString().ToUpperInvariant();
            }
        }

        private static void SetButton(Button button, Text label, bool visible, string text, bool interactable)
        {
            if (button == null) return;
            if (button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible);
            button.interactable = interactable;
            if (label != null) label.text = text;
        }

        private void SetBar(int index, float value)
        {
            if (index < statBars.Length && statBars[index] != null) statBars[index].localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
        }

        private void HandlePurchaseResult(string message, bool success)
        {
            if (messageText != null)
            {
                messageText.text = message;
                messageText.color = success ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.4f, 0.35f);
            }
            messageTimer = messageDuration;
            Refresh();
        }

        private void HandleMoneyChanged(int balance, int delta)
        {
            if (moneyText != null) moneyText.text = "$" + balance.ToString("N0");
            Refresh();
        }
    }
}
