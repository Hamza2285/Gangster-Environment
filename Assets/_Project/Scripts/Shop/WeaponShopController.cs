using System;
using System.Collections.Generic;
using Invector.vCamera;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.Weapons;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenworldCrime.Shop
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Ammu-Nation style shop logic (city scene, under ///Manager///; not a Singleton). Opens when a <see cref="ShopCounter"/>
    /// is used, owns the purchase rules (money via <see cref="MoneyManager"/>, weapons / ammo via <see cref="PlayerArsenal"/>)
    /// and the browsing: the player camera glides to the selected weapon hanging on the wall / lying on the counter
    /// (<see cref="ShopDisplaySlot"/>), frames it in the free screen area next to the side panel and lights it.
    /// Browsing: category left / right, weapon up / down (keys, panel buttons, swipes) or tapping a weapon.
    /// The panel visuals (<c>WeaponShopView</c>) only display state and forward button presses here.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponShopController : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerArsenal arsenal;
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private InvectorGamePlayer gamePlayer;
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;
        [SerializeField] private Camera playerCamera;

        [Header("Camera")]
        [SerializeField, Min(10f)] private float shopFieldOfView = 34f;
        [Tooltip("Seconds the camera takes to glide to the counter, between weapons and back to the player.")]
        [SerializeField, Min(0.05f)] private float transitionTime = 0.55f;
        [Tooltip("Extra space around the framed weapon (1 = touching the framing area's edges).")]
        [SerializeField, Min(1f)] private float framingMargin = 1.8f;
        [SerializeField, Min(0.2f)] private float minCameraDistance = 0.6f;
        [Tooltip("Screen area (UI rect) the selected weapon is framed in: the space next to the side panel. Full screen when empty.")]
        [SerializeField] private RectTransform framingArea;

        [Header("Highlight Light")]
        [Tooltip("Light position relative to the weapon: distance toward the camera and height above it (m).")]
        [SerializeField] private Vector2 lightOffset = new Vector2(0.75f, 0.55f);

        [Header("Touch")]
        [Tooltip("Swipe length (fraction of the screen height) that browses: horizontal = category, vertical = weapon.")]
        [SerializeField, Range(0.02f, 0.3f)] private float swipeThreshold = 0.07f;

        [Header("Rules")]
        [Tooltip("Magazines of reserve ammo included with a new weapon.")]
        [SerializeField, Min(0)] private int freeMagazinesWithWeapon = 2;

        private static readonly WeaponCategory[] CategoryOrder =
        {
            WeaponCategory.Pistol, WeaponCategory.SMG, WeaponCategory.Shotgun, WeaponCategory.AssaultRifle,
            WeaponCategory.Sniper, WeaponCategory.Heavy, WeaponCategory.Melee,
        };

        /// <summary>Raised when the shop opens (true) or closes (false).</summary>
        public static event Action<bool> OnShopOpenChanged;
        /// <summary>Raised when the selected weapon changes (also when the category changes).</summary>
        public event Action<WeaponDefinition> OnSelectionChanged;
        /// <summary>Raised after any purchase attempt. Args: message, success.</summary>
        public event Action<string, bool> OnPurchaseResult;

        public bool IsOpen { get; private set; }
        public WeaponDefinition Selected { get; private set; }
        public WeaponCatalog Catalog => arsenal != null ? arsenal.Catalog : null;
        public PlayerArsenal Arsenal => arsenal;
        public WeaponCategory CurrentCategory => categories.Count > 0 ? categories[categoryIndex] : WeaponCategory.Pistol;
        public int CategoryIndex => categoryIndex;
        public int CategoryCount => categories.Count;
        /// <summary>Weapons of the current category, in catalog order (the order they hang on the wall).</summary>
        public IReadOnlyList<WeaponDefinition> CategoryWeapons => categoryWeapons;

        private readonly List<WeaponCategory> categories = new List<WeaponCategory>();
        private readonly List<WeaponDefinition> categoryWeapons = new List<WeaponDefinition>();
        private readonly List<WeaponDefinition> visibleWeapons = new List<WeaponDefinition>();
        private readonly List<WeaponDefinition> buffer = new List<WeaponDefinition>();
        private readonly Vector3[] corners = new Vector3[4];
        private int categoryIndex;

        private ShopCounter counter;
        private ShopDisplaySlot highlightedSlot;
        private bool closing;
        private Transform cameraParent;
        private Vector3 cameraLocalPosition;
        private Quaternion cameraLocalRotation;
        private float cameraFieldOfView;
        private CursorLockMode cursorLockBefore;
        private bool cursorVisibleBefore;

        // Camera glide.
        private Vector3 fromPosition, toPosition;
        private Quaternion fromRotation, toRotation;
        private float fromFov, toFov;
        private float glide = 1f;

        // Pointer gesture.
        private bool pointerDown;
        private bool pointerStartedOnUi;
        private Vector2 pointerStart;

        private void Awake()
        {
            if (playerManager == null || arsenal == null || weaponController == null || gamePlayer == null || thirdPersonCamera == null || playerCamera == null)
            {
                Debug.LogError($"[{nameof(WeaponShopController)}] Missing references on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnEnable() => ShopCounter.OnBrowseRequested += Open;

        private void OnDisable()
        {
            ShopCounter.OnBrowseRequested -= Open;
            if (IsOpen || closing) FinishClose();
        }

        // Per-frame only while the shop is open or closing: camera glide, keys and touch gestures.
        private void Update()
        {
            if (!IsOpen && !closing) return;
            if (counter == null)
            {
                FinishClose(); // the interior was unloaded
                return;
            }
            UpdateGlide();
            if (closing)
            {
                if (glide >= 1f) FinishClose();
                return;
            }
            HandleKeys();
            HandlePointer();
        }

        public void Open(ShopCounter shopCounter)
        {
            if (!enabled || IsOpen || closing || shopCounter == null || playerManager.IsInputLocked) return;
            counter = shopCounter;
            BuildCategories();
            if (categories.Count == 0)
            {
                counter = null;
                return;
            }
            IsOpen = true;
            playerManager.SetInputLocked(true);

            cursorLockBefore = Cursor.lockState;
            cursorVisibleBefore = Cursor.visible;
            gamePlayer.LockCursor(false);

            thirdPersonCamera.enabled = false;
            Transform cam = playerCamera.transform;
            cameraParent = cam.parent;
            cameraLocalPosition = cam.localPosition;
            cameraLocalRotation = cam.localRotation;
            cameraFieldOfView = playerCamera.fieldOfView;
            if (counter.HighlightLight != null) counter.HighlightLight.enabled = true;

            OnShopOpenChanged?.Invoke(true); // shows the panel first: the framing area is part of its layout
            categoryIndex = 0;
            ShowCategory(true);
        }

        /// <summary>Leaves the counter: the camera glides back behind the player, then control returns.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closing = true;
            pointerDown = false;
            SetHighlight(null);
            if (counter != null && counter.HighlightLight != null) counter.HighlightLight.enabled = false;
            Vector3 position = cameraParent != null ? cameraParent.TransformPoint(cameraLocalPosition) : playerCamera.transform.position;
            Quaternion rotation = cameraParent != null ? cameraParent.rotation * cameraLocalRotation : playerCamera.transform.rotation;
            GlideTo(position, rotation, cameraFieldOfView);
            OnShopOpenChanged?.Invoke(false);
        }

        public void ChangeCategory(int direction)
        {
            if (!IsOpen || categories.Count == 0) return;
            categoryIndex = ((categoryIndex + direction) % categories.Count + categories.Count) % categories.Count;
            ShowCategory(true);
        }

        public void ChangeWeapon(int direction)
        {
            if (!IsOpen || categoryWeapons.Count == 0) return;
            int index = Mathf.Max(0, categoryWeapons.IndexOf(Selected));
            Select(categoryWeapons[((index + direction) % categoryWeapons.Count + categoryWeapons.Count) % categoryWeapons.Count]);
        }

        public void Select(WeaponDefinition weapon)
        {
            if (!IsOpen || weapon == null) return;
            if (weapon.category != CurrentCategory)
            {
                int index = categories.IndexOf(weapon.category);
                if (index < 0) return;
                categoryIndex = index;
                ShowCategory(false);
            }
            Selected = weapon;
            ShopDisplaySlot slot = counter.FindSlot(weapon.id);
            SetHighlight(slot);
            if (slot != null) FrameSlot(slot);
            OnSelectionChanged?.Invoke(weapon);
        }

        public bool IsOwned(WeaponDefinition weapon) => arsenal.OwnsWeapon(weapon);
        public int GetReserve(WeaponDefinition weapon) => arsenal.GetReserveAmmo(weapon);
        public bool IsAmmoFull(WeaponDefinition weapon) => weapon != null && weapon.UsesAmmo && GetReserve(weapon) >= weapon.maxReserveAmmo;

        public void BuySelectedWeapon()
        {
            WeaponDefinition weapon = Selected;
            if (weapon == null) return;
            if (IsOwned(weapon)) { Report("Already owned", false); return; }
            MoneyManager money = MoneyManager.Instance;
            if (money == null || !money.TrySpend(weapon.price)) { Report("Not enough cash", false); return; }
            arsenal.GrantWeapon(weapon);
            if (weapon.UsesAmmo) arsenal.AddAmmo(weapon, weapon.AmmoPerPurchase * freeMagazinesWithWeapon);
            weaponController.EquipWeapon(weapon); // becomes its group's wheel choice (drawn later; weapons are holstered in the shop)
            counter.PlayPurchaseSound();
            Report(weapon.displayName + " purchased", true);
        }

        public void BuySelectedAmmo() => BuyAmmo(1);

        public void FillSelectedAmmo()
        {
            WeaponDefinition weapon = Selected;
            if (weapon == null || !weapon.UsesAmmo) return;
            int missing = weapon.maxReserveAmmo - GetReserve(weapon);
            BuyAmmo(Mathf.CeilToInt(missing / (float)weapon.AmmoPerPurchase));
        }

        public int GetFillCost(WeaponDefinition weapon)
        {
            if (weapon == null || !weapon.UsesAmmo) return 0;
            int missing = Mathf.Max(0, weapon.maxReserveAmmo - GetReserve(weapon));
            return Mathf.CeilToInt(missing / (float)weapon.AmmoPerPurchase) * weapon.ammoPrice;
        }

        private void BuyAmmo(int magazines)
        {
            WeaponDefinition weapon = Selected;
            if (weapon == null || !weapon.UsesAmmo || magazines <= 0) { Report("Ammo full", false); return; }
            if (!IsOwned(weapon)) { Report("Buy the weapon first", false); return; }
            if (IsAmmoFull(weapon)) { Report("Ammo full", false); return; }
            MoneyManager money = MoneyManager.Instance;
            if (money == null) return;
            int affordable = weapon.ammoPrice > 0 ? Mathf.Min(magazines, money.Balance / weapon.ammoPrice) : magazines;
            if (affordable <= 0) { Report("Not enough cash", false); return; }
            int added = arsenal.AddAmmo(weapon, affordable * weapon.AmmoPerPurchase);
            int paidMagazines = Mathf.CeilToInt(added / (float)weapon.AmmoPerPurchase);
            money.TrySpend(paidMagazines * weapon.ammoPrice);
            counter.PlayPurchaseSound();
            Report("+" + added + " rounds", true);
        }

        private void Report(string message, bool success) => OnPurchaseResult?.Invoke(message, success);

        // ---------------------------------------------------------------- browsing

        private void BuildCategories()
        {
            categories.Clear();
            visibleWeapons.Clear();
            for (int c = 0; c < CategoryOrder.Length; c++)
            {
                Catalog.GetCategory(CategoryOrder[c], buffer);
                bool any = false;
                for (int i = 0; i < buffer.Count; i++)
                {
                    if (buffer[i].hiddenInShop) continue;
                    visibleWeapons.Add(buffer[i]);
                    any = true;
                }
                if (any) categories.Add(CategoryOrder[c]);
            }
        }

        private void ShowCategory(bool selectFirst)
        {
            categoryWeapons.Clear();
            for (int i = 0; i < visibleWeapons.Count; i++)
                if (visibleWeapons[i].category == CurrentCategory) categoryWeapons.Add(visibleWeapons[i]);
            if (selectFirst && categoryWeapons.Count > 0) Select(categoryWeapons[0]);
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeCategory(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeCategory(1);
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) ChangeWeapon(-1);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) ChangeWeapon(1);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Selected != null && !IsOwned(Selected)) BuySelectedWeapon();
                else BuySelectedAmmo();
            }
        }

        // Tap a weapon to select it; swipe left / right = category, up / down = weapon. Presses that start on UI are ignored.
        private void HandlePointer()
        {
            bool began, ended;
            Vector2 position;
            int pointerId;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                began = touch.phase == TouchPhase.Began;
                ended = touch.phase == TouchPhase.Ended;
                position = touch.position;
                pointerId = touch.fingerId;
            }
            else
            {
                began = Input.GetMouseButtonDown(0);
                ended = Input.GetMouseButtonUp(0);
                position = Input.mousePosition;
                pointerId = -1;
            }

            if (began)
            {
                pointerDown = true;
                pointerStart = position;
                pointerStartedOnUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
            }
            if (!ended || !pointerDown) return;
            pointerDown = false;
            if (pointerStartedOnUi) return;

            Vector2 delta = position - pointerStart;
            float threshold = swipeThreshold * Screen.height;
            if (delta.magnitude < threshold * 0.35f) PickAt(position);
            else if (Mathf.Abs(delta.x) >= threshold && Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) ChangeCategory(delta.x < 0f ? 1 : -1);
            else if (Mathf.Abs(delta.y) >= threshold) ChangeWeapon(delta.y > 0f ? 1 : -1);
        }

        private void PickAt(Vector2 screenPosition)
        {
            float pixelsPerUnitAtOne = Screen.height * 0.5f / Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            WeaponDefinition best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < visibleWeapons.Count; i++)
            {
                ShopDisplaySlot slot = counter.FindSlot(visibleWeapons[i].id);
                if (slot == null) continue;
                Bounds bounds = slot.Bounds;
                Vector3 screen = playerCamera.WorldToScreenPoint(bounds.center);
                if (screen.z <= 0.05f) continue;
                float radius = bounds.extents.magnitude * pixelsPerUnitAtOne / screen.z;
                float distance = Vector2.Distance(screenPosition, screen);
                if (distance < radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = visibleWeapons[i];
                }
            }
            if (best != null && best != Selected) Select(best);
        }

        private void SetHighlight(ShopDisplaySlot slot)
        {
            if (highlightedSlot == slot) return;
            if (highlightedSlot != null) highlightedSlot.SetHighlighted(false);
            highlightedSlot = slot;
            if (slot == null) return;
            slot.SetHighlighted(true);
            Light light = counter != null ? counter.HighlightLight : null;
            if (light == null) return;
            Vector3 center = slot.Bounds.center;
            Vector3 position = center + slot.ViewDirection * lightOffset.x + Vector3.up * lightOffset.y;
            light.transform.SetPositionAndRotation(position, Quaternion.LookRotation(center - position));
        }

        // ---------------------------------------------------------------- camera

        // Places the camera on the slot's view direction so the weapon's bounds fill the framing area (with margin) and its
        // centre sits in the middle of that area.
        private void FrameSlot(ShopDisplaySlot slot)
        {
            Bounds bounds = slot.Bounds;
            Vector3 toCamera = slot.ViewDirection;
            Quaternion rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            // Half size of the bounds box as seen along the view direction.
            Vector3 e = bounds.extents;
            float halfWidth = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.y) * e.y + Mathf.Abs(right.z) * e.z;
            float halfHeight = Mathf.Abs(up.x) * e.x + Mathf.Abs(up.y) * e.y + Mathf.Abs(up.z) * e.z;
            float halfDepth = Mathf.Abs(toCamera.x) * e.x + Mathf.Abs(toCamera.y) * e.y + Mathf.Abs(toCamera.z) * e.z;

            Rect area = FramingViewportRect();
            float tanV = Mathf.Tan(shopFieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * playerCamera.aspect;
            float distance = Mathf.Max(halfWidth * framingMargin / (area.width * tanH), halfHeight * framingMargin / (area.height * tanV));
            distance = Mathf.Max(minCameraDistance, distance + halfDepth);

            Vector3 offset = right * ((area.center.x - 0.5f) * 2f * distance * tanH) + up * ((area.center.y - 0.5f) * 2f * distance * tanV);
            GlideTo(bounds.center + toCamera * distance - offset, rotation, shopFieldOfView);
        }

        private Rect FramingViewportRect()
        {
            if (framingArea == null || !framingArea.gameObject.activeInHierarchy) return new Rect(0f, 0f, 1f, 1f);
            Canvas canvas = framingArea.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            framingArea.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
            var rect = Rect.MinMaxRect(min.x / Screen.width, min.y / Screen.height, max.x / Screen.width, max.y / Screen.height);
            return rect.width > 0.05f && rect.height > 0.05f ? rect : new Rect(0f, 0f, 1f, 1f);
        }

        private void GlideTo(Vector3 position, Quaternion rotation, float fov)
        {
            Transform cam = playerCamera.transform;
            fromPosition = cam.position;
            fromRotation = cam.rotation;
            fromFov = playerCamera.fieldOfView;
            toPosition = position;
            toRotation = rotation;
            toFov = fov;
            glide = 0f;
        }

        private void UpdateGlide()
        {
            if (glide >= 1f) return;
            glide = Mathf.Min(1f, glide + Time.unscaledDeltaTime / transitionTime);
            float k = Mathf.SmoothStep(0f, 1f, glide);
            playerCamera.transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, k), Quaternion.Slerp(fromRotation, toRotation, k));
            playerCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, k);
        }

        private void FinishClose()
        {
            bool wasActive = IsOpen || closing;
            bool announce = IsOpen; // Close() already announced it otherwise
            IsOpen = false;
            closing = false;
            glide = 1f;
            SetHighlight(null);
            if (counter != null && counter.HighlightLight != null) counter.HighlightLight.enabled = false;
            if (!wasActive) return;

            Transform cam = playerCamera.transform;
            if (cameraParent != null) cam.SetParent(cameraParent, false);
            cam.localPosition = cameraLocalPosition;
            cam.localRotation = cameraLocalRotation;
            playerCamera.fieldOfView = cameraFieldOfView;
            thirdPersonCamera.enabled = true;

            Cursor.lockState = cursorLockBefore;
            Cursor.visible = cursorVisibleBefore;
            playerManager.SetInputLocked(false);
            counter = null;
            Selected = null;
            if (announce) OnShopOpenChanged?.Invoke(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnShopOpenChanged = null;
    }
}
