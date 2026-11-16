using System.Collections.Generic;
using System.Text;
using Invector;
using Invector.vCamera;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vShooter;
using OpenworldCrime.Controls;
using OpenworldCrime.Economy;
using OpenworldCrime.Interiors;
using OpenworldCrime.Player;
using OpenworldCrime.Range;
using OpenworldCrime.Shop;
using OpenworldCrime.UI;
using OpenworldCrime.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Sets up the gun shop feature in the open city scene: player components (PlayerArsenal, catalog wheel), managers
    /// (Save, Money, Interior Travel, Weapon Shop) under ///Manager///, HUD additions (money, weapon icon, interaction
    /// prompt, 8-group weapon wheel, fader, shop menu, range HUD) and the map blip adapter. Re-running replaces what it built.
    /// </summary>
    public static class GunShopCitySetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";
        private const string ManagerRootName = "///Manager///";
        private static readonly Color Panel = new Color(0.05f, 0.05f, 0.08f, 0.82f);
        private static readonly Color Accent = new Color(0.85f, 0.12f, 0.16f, 0.95f);

        private static Font font;
        private static Sprite panelSprite, discSprite;

        [MenuItem("Tools/OpenworldCrime/Gun Shop/Setup City Scene (player, managers, UI)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[GunShopCitySetup]\n");
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponBuilder.CatalogPath);
            if (catalog == null) return "Build the weapons first (Tools/OpenworldCrime/Weapons/Build Weapons From Catalog).";

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            discSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            SetupPlayerPrefab(catalog, log);
            log.AppendLine(EnsureInteriorCameraState());

            var scene = EditorSceneManager.GetActiveScene();
            GameObject managerRoot = FindRoot(scene, ManagerRootName);
            GameObject playerGo = FindRoot(scene, "Player_Jason");
            GameObject gameplayUi = FindRoot(scene, "Gameplay UI");
            GameObject touch = FindRoot(scene, "Touch Controls");
            if (managerRoot == null || playerGo == null || gameplayUi == null) return log.Append("Scene needs ///Manager///, Player_Jason and Gameplay UI.").ToString();

            var player = playerGo.GetComponent<PlayerManager>();
            var camera = playerGo.GetComponentInChildren<vThirdPersonCamera>(true);
            var playerCamera = camera != null ? camera.GetComponentInChildren<Camera>(true) : null;

            // Managers
            GetOrAddManager<SaveManager>(managerRoot, "SaveManager");
            GetOrAddManager<MoneyManager>(managerRoot, "MoneyManager");

            // UI
            Transform uiRoot = gameplayUi.transform;
            Remove(uiRoot, "HUD/Money");
            Remove(uiRoot, "HUD/WeaponInfo/WeaponIcon");
            Remove(uiRoot, "InteractPrompt");
            Remove(uiRoot, "ScreenFader");
            Remove(uiRoot, "WeaponShop");
            Remove(uiRoot, "RangeHud");
            Remove(uiRoot, "MapBlips");

            Text money = NewText("Money", uiRoot.Find("HUD"), "$0", 44, TextAnchor.MiddleRight, new Color(0.45f, 1f, 0.45f));
            Place(money.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -212), new Vector2(420, 64)); // under the weapon box (the map's day / night buttons own the top-right corner)
            money.rectTransform.pivot = new Vector2(1, 1);
            money.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);

            var weaponInfo = (RectTransform)uiRoot.Find("HUD/WeaponInfo");
            Image weaponIcon = NewImage("WeaponIcon", weaponInfo, null, Color.white);
            Place(weaponIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-12, 0), new Vector2(170, 85));
            weaponIcon.rectTransform.pivot = new Vector2(1, 0.5f);
            weaponIcon.preserveAspect = true;
            weaponIcon.enabled = false;

            // Interaction prompt; on touch screens the panel itself is the interact button.
            Image prompt = NewImage("InteractPrompt", uiRoot, panelSprite, new Color(0, 0, 0, 0.7f));
            Place(prompt.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 260), new Vector2(560, 74));
            prompt.raycastTarget = true;
            Text promptText = NewText("Text", prompt.transform, "", 30, TextAnchor.MiddleCenter, Color.white);
            Stretch(promptText.rectTransform);
            var promptButton = prompt.gameObject.AddComponent<VirtualButton>();
            var promptSo = new SerializedObject(promptButton);
            promptSo.FindProperty("buttonName").stringValue = "M_Interact";
            promptSo.FindProperty("targetGraphic").objectReferenceValue = prompt;
            promptSo.ApplyModifiedPropertiesWithoutUndo();
            prompt.gameObject.SetActive(false);

            WeaponWheelView wheel = RebuildWheel(uiRoot.Find("WeaponWheel"), WheelGroupCount(catalog));

            ScreenFader fader = BuildFader(uiRoot);
            WeaponShopController shopController = GetOrAddManager<WeaponShopController>(managerRoot, "WeaponShopController");
            GameObject shopPanel = BuildShop(uiRoot, shopController);
            BuildRangeHud(uiRoot);

            var blipGo = new GameObject("MapBlips", typeof(RectTransform));
            blipGo.transform.SetParent(uiRoot, false);
            var overlay = blipGo.AddComponent<MapBlipOverlay>();
            SetRef(overlay, "discSprite", discSprite);

            // Travel manager
            InteriorTravelManager travel = GetOrAddManager<InteriorTravelManager>(managerRoot, "InteriorTravelManager");
            var travelSo = new SerializedObject(travel);
            travelSo.FindProperty("playerManager").objectReferenceValue = player;
            travelSo.FindProperty("gamePlayer").objectReferenceValue = playerGo.GetComponent<InvectorGamePlayer>();
            travelSo.FindProperty("weaponController").objectReferenceValue = playerGo.GetComponent<PlayerWeaponController>();
            travelSo.FindProperty("shooterInput").objectReferenceValue = playerGo.GetComponent<vShooterMeleeInput>();
            travelSo.FindProperty("thirdPersonCamera").objectReferenceValue = camera;
            travelSo.FindProperty("fader").objectReferenceValue = fader;
            var doorAudio = travel.GetComponent<AudioSource>();
            if (doorAudio == null) doorAudio = travel.gameObject.AddComponent<AudioSource>();
            doorAudio.playOnAwake = false;
            doorAudio.spatialBlend = 0f;
            doorAudio.clip = FindClip("door", "Door");
            travelSo.FindProperty("doorAudio").objectReferenceValue = doorAudio;
            var sun = FindSun(scene);
            var disable = travelSo.FindProperty("disableWhileInside");
            disable.arraySize = sun != null ? 1 : 0;
            if (sun != null) disable.GetArrayElementAtIndex(0).objectReferenceValue = sun;
            GameObject dayNightButtons = FindDayNightControls(scene);
            var hide = travelSo.FindProperty("hideWhileInside");
            hide.arraySize = dayNightButtons != null ? 1 : 0;
            if (dayNightButtons != null) hide.GetArrayElementAtIndex(0).objectReferenceValue = dayNightButtons;
            travelSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("  InteriorTravelManager: sun disabled indoors = " + (sun != null ? sun.name : "none") + ", hidden indoors = " + (dayNightButtons != null ? dayNightButtons.name : "none"));

            // Shop controller
            var shopSo = new SerializedObject(shopController);
            shopSo.FindProperty("playerManager").objectReferenceValue = player;
            shopSo.FindProperty("arsenal").objectReferenceValue = playerGo.GetComponent<PlayerArsenal>();
            shopSo.FindProperty("weaponController").objectReferenceValue = playerGo.GetComponent<PlayerWeaponController>();
            shopSo.FindProperty("gamePlayer").objectReferenceValue = playerGo.GetComponent<InvectorGamePlayer>();
            shopSo.FindProperty("thirdPersonCamera").objectReferenceValue = camera;
            shopSo.FindProperty("playerCamera").objectReferenceValue = playerCamera;
            shopSo.ApplyModifiedPropertiesWithoutUndo();

            // UIManager wiring
            var uiManager = managerRoot.GetComponentInChildren<UIManager>(true);
            var uiSo = new SerializedObject(uiManager);
            uiSo.FindProperty("moneyText").objectReferenceValue = money;
            uiSo.FindProperty("weaponIcon").objectReferenceValue = weaponIcon;
            uiSo.FindProperty("interactionController").objectReferenceValue = playerGo.GetComponent<PlayerInteractionController>();
            uiSo.FindProperty("interactPromptPanel").objectReferenceValue = prompt.gameObject;
            uiSo.FindProperty("interactPromptText").objectReferenceValue = promptText;
            uiSo.FindProperty("weaponShopPanel").objectReferenceValue = shopPanel;
            uiSo.FindProperty("weaponWheelView").objectReferenceValue = wheel;
            var hidden = new List<Object>();
            if (touch != null) hidden.Add(touch);
            Transform invectorUi = playerGo.transform.Find("Invector Components/UI");
            if (invectorUi != null) hidden.Add(invectorUi.gameObject);
            Transform aimCanvas = playerGo.transform.Find("Invector Components/AimCanvas");
            if (aimCanvas != null) hidden.Add(aimCanvas.gameObject);
            var hiddenProp = uiSo.FindProperty("hiddenDuringMenus");
            hiddenProp.arraySize = hidden.Count;
            for (int i = 0; i < hidden.Count; i++) hiddenProp.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];
            uiSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("  managers, HUD, wheel (8 groups), prompt, fader, shop menu, range HUD and map blips set up. Save the scene.");
            return log.ToString();
        }

        private const string CameraStatesPath = "Assets/_Project/Data/Camera/OWC_ShooterMelee_CameraStates.asset";

        /// <summary>Adds the closer "Interior" camera state (a copy of Default) used indoors when not aiming.</summary>
        public static string EnsureInteriorCameraState()
        {
            var list = AssetDatabase.LoadAssetAtPath<vThirdPersonCameraListData>(CameraStatesPath);
            if (list == null) return "  camera states asset missing";
            vThirdPersonCameraState source = list.tpCameraStates.Find(s => s.Name == "Default");
            if (source == null) return "  no Default camera state";
            vThirdPersonCameraState state = list.tpCameraStates.Find(s => s.Name == InteriorTravelManager.InteriorCameraState);
            if (state == null)
            {
                state = JsonUtility.FromJson<vThirdPersonCameraState>(JsonUtility.ToJson(source));
                list.tpCameraStates.Add(state);
            }
            // Rooms are 2.7 m (corridor) to 3.6 m (range) high: near the Default height, kept close for the 1.75 m corridor.
            state.Name = InteriorTravelManager.InteriorCameraState;
            state.height = 1.55f;
            state.right = 0.3f;
            state.defaultDistance = 1.9f;
            state.minDistance = 0.5f;
            state.maxDistance = 2.4f;
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return "  camera state 'Interior' (h 1.55, dist 1.9) in OWC_ShooterMelee_CameraStates";
        }

        // ---------------------------------------------------------------- player

        private static void SetupPlayerPrefab(WeaponCatalog catalog, StringBuilder log)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var arsenal = root.GetComponent<PlayerArsenal>();
                if (arsenal == null) arsenal = root.AddComponent<PlayerArsenal>();
                SetRef(arsenal, "catalog", catalog);
                SetRef(arsenal, "itemManager", root.GetComponent<vItemManager>());
                SetRef(arsenal, "shooterManager", root.GetComponent<vShooterManager>());

                var weapons = root.GetComponent<PlayerWeaponController>();
                var so = new SerializedObject(weapons);
                so.FindProperty("arsenal").objectReferenceValue = arsenal;
                SerializedProperty keys = so.FindProperty("slotKeys");
                keys.arraySize = 8;
                for (int i = 0; i < 8; i++)
                {
                    SerializedProperty key = keys.GetArrayElementAtIndex(i);
                    key.FindPropertyRelative("keyboard").stringValue = "Alpha" + (i + 1);
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                // Interact: E on keyboard, the on-screen prompt (M_Interact) on touch.
                var interaction = root.GetComponent<PlayerInteractionController>();
                var iso = new SerializedObject(interaction);
                iso.FindProperty("interactInput").FindPropertyRelative("mobile").stringValue = "M_Interact";
                iso.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                log.AppendLine("  Player_Jason prefab: PlayerArsenal, catalog wheel (1-8 keys), interact on M_Interact");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------------------------------------------------------------- wheel

        /// <summary>Wheel groups as PlayerWeaponController builds them: Unarmed plus every category the catalog sells.</summary>
        private static int WheelGroupCount(WeaponCatalog catalog)
        {
            int count = 0;
            var buffer = new List<WeaponDefinition>();
            foreach (WeaponCategory category in System.Enum.GetValues(typeof(WeaponCategory)))
            {
                catalog.GetCategory(category, buffer);
                if (category == WeaponCategory.Unarmed || buffer.Count > 0) count++;
            }
            return count;
        }

        private static WeaponWheelView RebuildWheel(Transform wheelRoot, int count)
        {
            var view = wheelRoot.GetComponent<WeaponWheelView>();
            for (int i = wheelRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = wheelRoot.GetChild(i);
                if (child.name.StartsWith("Slot")) Object.DestroyImmediate(child.gameObject);
            }
            ((RectTransform)wheelRoot).sizeDelta = new Vector2(860, 860);
            var so = new SerializedObject(view);
            SerializedProperty slots = so.FindProperty("slots");
            slots.arraySize = count;
            const float radius = 300f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 360f / count * Mathf.Deg2Rad;
                Image slot = NewImage("Slot" + i, wheelRoot, discSprite, new Color(0, 0, 0, 0.6f));
                Place(slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius, new Vector2(210, 210));
                slot.transform.SetSiblingIndex(i);
                Image icon = NewImage("Icon", slot.transform, null, Color.white);
                Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(170, 85));
                icon.preserveAspect = true;
                icon.enabled = false;
                Text label = NewText("Label", slot.transform, "", 22, TextAnchor.LowerCenter, Color.white);
                Place(label.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 26), new Vector2(-30, 70));
                label.rectTransform.pivot = new Vector2(0.5f, 0);
                SerializedProperty entry = slots.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("background").objectReferenceValue = slot;
                entry.FindPropertyRelative("label").objectReferenceValue = label;
                entry.FindPropertyRelative("icon").objectReferenceValue = icon;
            }
            so.FindProperty("markerRadius").floatValue = 110f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        // ---------------------------------------------------------------- fader

        private static ScreenFader BuildFader(Transform uiRoot)
        {
            var go = new GameObject("ScreenFader", typeof(RectTransform));
            go.transform.SetParent(uiRoot, false);
            Stretch((RectTransform)go.transform);
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2000;
            Image black = NewImage("Black", go.transform, null, Color.black);
            Stretch(black.rectTransform);
            black.raycastTarget = true;
            Text caption = NewText("Caption", black.transform, "", 64, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            Stretch(caption.rectTransform);
            black.gameObject.SetActive(false);
            var fader = go.AddComponent<ScreenFader>();
            SetRef(fader, "image", black);
            SetRef(fader, "caption", caption);
            return fader;
        }

        // ---------------------------------------------------------------- shop menu

        private const float ShopPanelWidth = 600f;
        private const float ShopHeaderHeight = 104f;
        private const float ShopCategoryHeight = 72f;
        private const float ShopRowHeight = 52f;
        private const float ShopDetailsHeight = 304f;

        /// <summary>
        /// Rebuilds only the gun shop side panel, the range HUD and the sniper scope overlay in the open city scene and wires
        /// them (UIManager, WeaponShopController framing area, SniperScopeView), leaving the rest of the city setup alone.
        /// </summary>
        [MenuItem("Tools/OpenworldCrime/Gun Shop/Rebuild Shop, Range, Scope And Wheel UI (city scene)")]
        public static void RebuildUiMenu() => Debug.Log(RebuildUi());

        public static string RebuildUi()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            discSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managerRoot = FindRoot(scene, ManagerRootName);
            GameObject playerGo = FindRoot(scene, "Player_Jason");
            GameObject gameplayUi = FindRoot(scene, "Gameplay UI");
            if (managerRoot == null || playerGo == null || gameplayUi == null) return "[GunShopCitySetup] Scene needs ///Manager///, Player_Jason and Gameplay UI.";
            WeaponShopController shopController = managerRoot.GetComponentInChildren<WeaponShopController>(true);
            UIManager uiManager = managerRoot.GetComponentInChildren<UIManager>(true);
            if (shopController == null || uiManager == null) return "[GunShopCitySetup] Run the full city setup first (no WeaponShopController / UIManager).";

            Transform uiRoot = gameplayUi.transform;
            Remove(uiRoot, "WeaponShop");
            Remove(uiRoot, "RangeHud");
            Remove(uiRoot, "SniperScope");
            GameObject shopPanel = BuildShop(uiRoot, shopController);
            BuildRangeHud(uiRoot);
            BuildScopeOverlay(uiRoot, playerGo);
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponBuilder.CatalogPath);
            Transform wheelRoot = uiRoot.Find("WeaponWheel");
            if (catalog != null && wheelRoot != null) RebuildWheel(wheelRoot, WheelGroupCount(catalog));
            SetRef(uiManager, "weaponShopPanel", shopPanel);
            // Invector's throwable HUD stayed on screen over the shop: hide its canvases with the rest of the HUD.
            foreach (string path in new[] { "Invector Components/ThrowManager/ThrowManager-Standalone/Canvas", "Invector Components/ThrowManager/ThrowManager-Inventory_Item/Canvas" })
            {
                Transform canvasTransform = playerGo.transform.Find(path);
                if (canvasTransform != null) AddHiddenDuringMenus(uiManager, canvasTransform.gameObject);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return "[GunShopCitySetup] shop side panel, range HUD (Quit button), sniper scope overlay and weapon wheel rebuilt. Save the scene.";
        }

        private static GameObject BuildShop(Transform uiRoot, WeaponShopController controller)
        {
            var root = new GameObject("WeaponShop", typeof(RectTransform));
            root.transform.SetParent(uiRoot, false);
            Stretch((RectTransform)root.transform);
            var canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 900;
            root.AddComponent<GraphicRaycaster>();

            // Free screen area the selected weapon is framed in (no graphic: presses there browse the wall).
            var framing = new GameObject("FramingArea", typeof(RectTransform));
            framing.transform.SetParent(root.transform, false);
            var framingRect = (RectTransform)framing.transform;
            framingRect.anchorMin = Vector2.zero;
            framingRect.anchorMax = Vector2.one;
            framingRect.offsetMin = new Vector2(48f + ShopPanelWidth + 40f, 120f);
            framingRect.offsetMax = new Vector2(-150f, -150f);

            // Side panel (top-left, GTA style); its height follows the category's row count.
            Image panel = NewImage("Panel", root.transform, null, new Color(0.02f, 0.02f, 0.03f, 0.8f));
            Place(panel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -40), new Vector2(ShopPanelWidth, 600));
            panel.rectTransform.pivot = new Vector2(0, 1);
            panel.raycastTarget = true; // presses on the panel never browse the wall behind it

            Image header = NewImage("Header", panel.transform, null, new Color(0.07f, 0.07f, 0.08f, 0.97f));
            TopBlock(header.rectTransform, 0f, ShopHeaderHeight);
            Image strip = NewImage("AccentStrip", header.transform, null, Accent);
            Place(strip.rectTransform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 6));
            strip.rectTransform.pivot = new Vector2(0.5f, 0);
            Text title = NewText("Title", header.transform, "GANGFIRE", 50, TextAnchor.MiddleLeft, Color.white);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -8), new Vector2(360, 62));
            title.rectTransform.pivot = new Vector2(0, 1);
            title.fontStyle = FontStyle.BoldAndItalic;
            Text subtitle = NewText("Subtitle", header.transform, "GUNS  &  AMMO", 20, TextAnchor.MiddleLeft, new Color(1f, 0.45f, 0.45f));
            Place(subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -66), new Vector2(360, 28));
            subtitle.rectTransform.pivot = new Vector2(0, 1);
            subtitle.fontStyle = FontStyle.Bold;
            Text money = NewText("Money", header.transform, "$0", 38, TextAnchor.MiddleRight, new Color(0.45f, 1f, 0.45f));
            Place(money.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 2), new Vector2(260, 60));
            money.rectTransform.pivot = new Vector2(1, 0.5f);
            money.fontStyle = FontStyle.Bold;

            // Category bar: < CATEGORY  n / m >
            Image bar = NewImage("CategoryBar", panel.transform, null, new Color(0f, 0f, 0f, 0.85f));
            TopBlock(bar.rectTransform, ShopHeaderHeight, ShopCategoryHeight);
            Button previous = NewButton("Previous", bar.transform, "<", 38, new Color(1f, 1f, 1f, 0.12f));
            Place((RectTransform)previous.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(64, 60));
            ((RectTransform)previous.transform).pivot = new Vector2(0, 0.5f);
            Button next = NewButton("Next", bar.transform, ">", 38, new Color(1f, 1f, 1f, 0.12f));
            Place((RectTransform)next.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(64, 60));
            ((RectTransform)next.transform).pivot = new Vector2(1, 0.5f);
            Text category = NewText("Category", bar.transform, "", 30, TextAnchor.MiddleCenter, Color.white);
            Place(category.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(-30, 0), new Vector2(-200, 0));
            category.fontStyle = FontStyle.Bold;
            Text count = NewText("Count", bar.transform, "", 22, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.55f));
            Place(count.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-84, 0), new Vector2(90, 40));
            count.rectTransform.pivot = new Vector2(1, 0.5f);

            // Weapon rows
            var content = new GameObject("Rows", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            var contentRect = (RectTransform)content.transform;
            TopBlock(contentRect, ShopHeaderHeight + ShopCategoryHeight, 0f);
            Button row = NewButton("RowTemplate", content.transform, "", 26, new Color(0, 0, 0, 0.55f));
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0, 1);
            rowRect.anchorMax = new Vector2(1, 1);
            rowRect.pivot = new Vector2(0.5f, 1);
            rowRect.sizeDelta = new Vector2(0, ShopRowHeight - 2f);
            row.transition = Selectable.Transition.None; // the view colours the selected row
            Object.DestroyImmediate(row.transform.Find("Label").gameObject);
            Text rowName = NewText("Name", row.transform, "", 26, TextAnchor.MiddleLeft, Color.white);
            Place(rowName.rectTransform, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            rowName.rectTransform.offsetMin = new Vector2(22, 0);
            rowName.rectTransform.offsetMax = new Vector2(-180, 0);
            rowName.fontStyle = FontStyle.Bold;
            Text rowPrice = NewText("Price", row.transform, "", 24, TextAnchor.MiddleRight, new Color(0.45f, 1f, 0.45f));
            Place(rowPrice.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-22, 0), new Vector2(170, 0));
            rowPrice.rectTransform.pivot = new Vector2(1, 0.5f);
            rowPrice.fontStyle = FontStyle.Bold;
            row.gameObject.SetActive(false);

            // Details: stats, ammo line, Buy / Ammo / Fill (moved under the last row by the view)
            var detailsGo = new GameObject("Details", typeof(RectTransform));
            detailsGo.transform.SetParent(panel.transform, false);
            var details = (RectTransform)detailsGo.transform;
            TopBlock(details, ShopHeaderHeight + ShopCategoryHeight, ShopDetailsHeight);
            string[] statNames = { "DAMAGE", "FIRE RATE", "ACCURACY", "RANGE" };
            var bars = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                float y = -12 - i * 36;
                Text label = NewText("Stat" + i, details, statNames[i], 21, TextAnchor.MiddleLeft, new Color(0.82f, 0.82f, 0.82f));
                Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, y), new Vector2(170, 30));
                label.rectTransform.pivot = new Vector2(0, 1);
                label.fontStyle = FontStyle.Bold;
                Image back = NewImage("Bar" + i, details, null, new Color(1, 1, 1, 0.14f));
                back.rectTransform.anchorMin = new Vector2(0, 1);
                back.rectTransform.anchorMax = new Vector2(1, 1);
                back.rectTransform.pivot = new Vector2(0, 1);
                back.rectTransform.offsetMin = new Vector2(196, y - 23);
                back.rectTransform.offsetMax = new Vector2(-22, y - 9);
                Image fill = NewImage("Fill", back.transform, null, new Color(0.95f, 0.95f, 0.95f, 0.95f));
                Stretch(fill.rectTransform);
                fill.rectTransform.pivot = new Vector2(0, 0.5f);
                bars[i] = fill.rectTransform;
            }
            Text ammoText = NewText("Ammo", details, "", 22, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.35f));
            Place(ammoText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -160), new Vector2(-44, 36));
            ammoText.rectTransform.pivot = new Vector2(0, 1);
            ammoText.fontStyle = FontStyle.Bold;
            Button buy = NewButton("Buy", details, "BUY", 28, new Color(0.13f, 0.55f, 0.2f, 0.97f));
            Place((RectTransform)buy.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 16), new Vector2(294, 84));
            ((RectTransform)buy.transform).pivot = Vector2.zero;
            Button ammo = NewButton("BuyAmmo", details, "AMMO", 22, new Color(0.22f, 0.22f, 0.27f, 0.97f));
            Place((RectTransform)ammo.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(318, 16), new Vector2(129, 84));
            ((RectTransform)ammo.transform).pivot = Vector2.zero;
            Button fillAmmo = NewButton("FillAmmo", details, "FILL", 22, new Color(0.22f, 0.22f, 0.27f, 0.97f));
            Place((RectTransform)fillAmmo.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(455, 16), new Vector2(129, 84));
            ((RectTransform)fillAmmo.transform).pivot = Vector2.zero;
            foreach (Button b in new[] { ammo, fillAmmo }) b.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;

            // Close (top-right), purchase message (under the framed weapon), controls hint (bottom-right).
            Button close = NewButton("Close", root.transform, "X", 46, Accent);
            Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -36), new Vector2(100, 100));
            ((RectTransform)close.transform).pivot = new Vector2(1, 1);
            Text message = NewText("Message", framing.transform, "", 38, TextAnchor.MiddleCenter, Color.white);
            Place(message.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, -20), new Vector2(900, 64));
            message.rectTransform.pivot = new Vector2(0.5f, 0);
            message.fontStyle = FontStyle.Bold;
            message.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            Text hint = NewText("Hint", root.transform, "A / D  category      W / S  weapon      Enter  buy      Esc  leave", 22, TextAnchor.LowerRight, new Color(1, 1, 1, 0.6f));
            Place(hint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 30), new Vector2(900, 36));
            hint.rectTransform.pivot = new Vector2(1, 0);
            hint.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            var view = root.AddComponent<WeaponShopView>();
            var so = new SerializedObject(view);
            so.FindProperty("controller").objectReferenceValue = controller;
            so.FindProperty("panel").objectReferenceValue = panel.rectTransform;
            so.FindProperty("details").objectReferenceValue = details;
            so.FindProperty("listTop").floatValue = ShopHeaderHeight + ShopCategoryHeight;
            so.FindProperty("categoryText").objectReferenceValue = category;
            so.FindProperty("categoryCountText").objectReferenceValue = count;
            so.FindProperty("previousCategoryButton").objectReferenceValue = previous;
            so.FindProperty("nextCategoryButton").objectReferenceValue = next;
            so.FindProperty("rowTemplate").objectReferenceValue = row;
            so.FindProperty("rowContainer").objectReferenceValue = contentRect;
            so.FindProperty("rowHeight").floatValue = ShopRowHeight;
            so.FindProperty("ammoText").objectReferenceValue = ammoText;
            so.FindProperty("messageText").objectReferenceValue = message;
            so.FindProperty("moneyText").objectReferenceValue = money;
            SerializedProperty statBars = so.FindProperty("statBars");
            statBars.arraySize = 4;
            for (int i = 0; i < 4; i++) statBars.GetArrayElementAtIndex(i).objectReferenceValue = bars[i];
            so.FindProperty("buyButton").objectReferenceValue = buy;
            so.FindProperty("buyLabel").objectReferenceValue = buy.GetComponentInChildren<Text>();
            so.FindProperty("ammoButton").objectReferenceValue = ammo;
            so.FindProperty("ammoLabel").objectReferenceValue = ammo.GetComponentInChildren<Text>();
            so.FindProperty("fillButton").objectReferenceValue = fillAmmo;
            so.FindProperty("fillLabel").objectReferenceValue = fillAmmo.GetComponentInChildren<Text>();
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetRef(controller, "framingArea", framingRect);
            root.SetActive(false);
            return root;
        }

        private static void AddHiddenDuringMenus(UIManager uiManager, GameObject target)
        {
            var so = new SerializedObject(uiManager);
            SerializedProperty list = so.FindProperty("hiddenDuringMenus");
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == target) return;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Full-width block of the side panel, <paramref name="top"/> units below its top edge.
        private static void TopBlock(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(0, height);
        }

        // ---------------------------------------------------------------- sniper scope

        private const string ScopeMaskPath = "Assets/_Project/Art/UI/SniperScopeMask.png";

        /// <summary>
        /// Full-screen GTA-style scope overlay (first child of the gameplay UI so the HUD stays on top): a square black mask
        /// with a soft round lens, black side bars for any aspect ratio, and a duplex reticle (thin centre lines, thick outer
        /// posts) centred on the screen - the scope camera's bore axis.
        /// </summary>
        private static void BuildScopeOverlay(Transform uiRoot, GameObject playerGo)
        {
            var root = new GameObject("SniperScope", typeof(RectTransform));
            root.transform.SetParent(uiRoot, false);
            root.transform.SetSiblingIndex(0);
            Stretch((RectTransform)root.transform);

            var overlay = new GameObject("Overlay", typeof(RectTransform));
            overlay.transform.SetParent(root.transform, false);
            Stretch((RectTransform)overlay.transform);
            Image mask = NewImage("Mask", overlay.transform, GetScopeMaskSprite(), Color.white);
            mask.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            mask.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            mask.rectTransform.sizeDelta = Vector2.zero;
            var fitter = mask.gameObject.AddComponent<AspectRatioFitter>(); // keeps the lens round on every screen shape
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = 1f;
            Image left = NewImage("LeftBar", mask.transform, null, Color.black);
            Place(left.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(4000, 0));
            left.rectTransform.pivot = new Vector2(1, 0.5f);
            Image right = NewImage("RightBar", mask.transform, null, Color.black);
            Place(right.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(4000, 0));
            right.rectTransform.pivot = new Vector2(0, 0.5f);

            // Reticle in lens units (fractions of the mask): thin full lines + thick posts on the outer part of each arm.
            var line = new Color(0f, 0f, 0f, 0.9f);
            ReticleBar(mask.transform, "Horizontal", new Vector2(0.03f, 0.5f), new Vector2(0.97f, 0.5f), 3f, line, true);
            ReticleBar(mask.transform, "Vertical", new Vector2(0.5f, 0.03f), new Vector2(0.5f, 0.97f), 3f, line, false);
            ReticleBar(mask.transform, "PostLeft", new Vector2(0.03f, 0.5f), new Vector2(0.27f, 0.5f), 12f, line, true);
            ReticleBar(mask.transform, "PostRight", new Vector2(0.73f, 0.5f), new Vector2(0.97f, 0.5f), 12f, line, true);
            ReticleBar(mask.transform, "PostBottom", new Vector2(0.5f, 0.03f), new Vector2(0.5f, 0.27f), 12f, line, false);
            ReticleBar(mask.transform, "PostTop", new Vector2(0.5f, 0.73f), new Vector2(0.5f, 0.97f), 12f, line, false);
            Image dot = NewImage("CentreDot", mask.transform, null, new Color(0.9f, 0.1f, 0.1f, 0.9f));
            Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 5));

            var view = root.AddComponent<SniperScopeView>();
            var so = new SerializedObject(view);
            so.FindProperty("shooterManager").objectReferenceValue = playerGo.GetComponent<vShooterManager>();
            so.FindProperty("shooterInput").objectReferenceValue = playerGo.GetComponent<vShooterMeleeInput>();
            var aimCanvas = playerGo.GetComponentInChildren<vControlAimCanvas>(true);
            so.FindProperty("scopeCamera").objectReferenceValue = aimCanvas != null ? aimCanvas.scopeBackgroundCamera : null;
            so.FindProperty("overlay").objectReferenceValue = overlay;
            so.ApplyModifiedPropertiesWithoutUndo();
            overlay.SetActive(false);
        }

        private static void ReticleBar(Transform parent, string name, Vector2 from, Vector2 to, float thickness, Color color, bool horizontal)
        {
            Image bar = NewImage(name, parent, null, color);
            bar.rectTransform.anchorMin = from;
            bar.rectTransform.anchorMax = to;
            bar.rectTransform.sizeDelta = horizontal ? new Vector2(0f, thickness) : new Vector2(thickness, 0f);
            bar.rectTransform.anchoredPosition = Vector2.zero;
        }

        // Black square with a soft-edged transparent round lens (generated once, 1024 px).
        private static Sprite GetScopeMaskSprite()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ScopeMaskPath);
            if (sprite != null) return sprite;
            if (System.IO.File.Exists(ScopeMaskPath)) return ImportScopeMaskSprite();
            const int size = 1024;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = r < 0.80f ? 0f
                        : r < 0.955f ? Mathf.SmoothStep(0f, 0.6f, (r - 0.80f) / 0.155f)
                        : r < 0.975f ? Mathf.Lerp(0.6f, 1f, (r - 0.955f) / 0.02f)
                        : 1f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/UI")) AssetDatabase.CreateFolder("Assets/_Project/Art", "UI");
            System.IO.File.WriteAllBytes(ScopeMaskPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ScopeMaskPath);
            return ImportScopeMaskSprite();
        }

        // Single sprite (the project's default sprite mode is Multiple, which yields no sprite for a plain texture).
        private static Sprite ImportScopeMaskSprite()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ScopeMaskPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ScopeMaskPath);
            if (sprite == null) Debug.LogError("[GunShopCitySetup] Could not import the sniper scope mask sprite at " + ScopeMaskPath);
            return sprite;
        }

        // ---------------------------------------------------------------- range HUD

        private static void BuildRangeHud(Transform uiRoot)
        {
            var root = new GameObject("RangeHud", typeof(RectTransform));
            root.transform.SetParent(uiRoot, false);
            Stretch((RectTransform)root.transform);

            Image running = NewImage("Running", root.transform, panelSprite, new Color(0, 0, 0, 0.65f));
            Place(running.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(620, 80));
            running.rectTransform.pivot = new Vector2(0.5f, 1);
            Text timer = NewText("Timer", running.transform, "", 36, TextAnchor.MiddleLeft, Color.white);
            Place(timer.rectTransform, new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(30, 0), Vector2.zero);
            timer.rectTransform.pivot = new Vector2(0, 0.5f);
            Text score = NewText("Score", running.transform, "", 36, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.35f));
            Place(score.rectTransform, new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(-30, 0), Vector2.zero);
            score.rectTransform.pivot = new Vector2(1, 0.5f);

            Button quit = NewButton("Quit", running.transform, "QUIT", 28, Accent);
            Place((RectTransform)quit.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(16, 0), new Vector2(150, 70));
            ((RectTransform)quit.transform).pivot = new Vector2(0, 0.5f);

            Text countdown = NewText("Countdown", root.transform, "", 170, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            Place(countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(600, 260));
            countdown.gameObject.AddComponent<Outline>().effectColor = Color.black;

            Image result = NewImage("Result", root.transform, panelSprite, new Color(0.04f, 0.04f, 0.06f, 0.9f));
            Place(result.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(700, 400));
            Text resultTitle = NewText("Title", result.transform, "", 64, TextAnchor.UpperCenter, new Color(1f, 0.8f, 0.2f));
            Place(resultTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, 90));
            resultTitle.rectTransform.pivot = new Vector2(0.5f, 1);
            resultTitle.fontStyle = FontStyle.Bold;
            Text resultBody = NewText("Body", result.transform, "", 34, TextAnchor.UpperCenter, Color.white);
            Place(resultBody.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -60), new Vector2(-60, -150));
            resultBody.lineSpacing = 1.15f;

            var view = root.AddComponent<RangeHudView>();
            var so = new SerializedObject(view);
            so.FindProperty("runningPanel").objectReferenceValue = running.gameObject;
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("scoreText").objectReferenceValue = score;
            so.FindProperty("countdownText").objectReferenceValue = countdown;
            so.FindProperty("quitButton").objectReferenceValue = quit;
            so.FindProperty("resultPanel").objectReferenceValue = result.gameObject;
            so.FindProperty("resultTitle").objectReferenceValue = resultTitle;
            so.FindProperty("resultBody").objectReferenceValue = resultBody;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- helpers

        private static T GetOrAddManager<T>(GameObject managerRoot, string name) where T : Component
        {
            Transform t = managerRoot.transform.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(managerRoot.transform, false);
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static Light FindSun(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var controller = root.GetComponentInChildren<CityDayNightController>(true);
                if (controller != null && controller.sun != null) return controller.sun;
            }
            return null;
        }

        private static GameObject FindDayNightControls(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var controller = root.GetComponentInChildren<CityDayNightController>(true);
                if (controller != null && controller.controls != null) return controller.controls;
            }
            return null;
        }

        private static AudioClip FindClip(params string[] names)
        {
            foreach (string n in names)
                foreach (string guid in AssetDatabase.FindAssets(n + " t:AudioClip"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Invector") || path.Contains("_Project") || path.Contains("RealisticCarController")) return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            return null;
        }

        private static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }

        private static void Remove(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static Text NewText(string name, Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static Button NewButton(string name, Transform parent, string label, int size, Color color)
        {
            Image image = NewImage(name, parent, panelSprite, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = colors;
            Text text = NewText("Label", image.transform, label, size, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            text.fontStyle = FontStyle.Bold;
            return button;
        }

        internal static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
