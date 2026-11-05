using System.Collections.Generic;
using System.Text;
using Invector;
using Invector.vCamera;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vShooter;
using OpenworldCrime.Controls;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Sets up combat on the player of the open scene. Re-runnable: what it made earlier is replaced.
    /// - Aim: locomotion FreeWithStrafe (OnlyFree makes Invector skip all aim input), free movement until the gun is raised, over-the-shoulder Aiming camera state.
    /// - Keys: frees Tab (weapon wheel), the scroll wheel and 1/2/3 (quick switch) from Invector's defaults.
    /// - UIManager with the HUD (weapon, ammo), weapon wheel and vehicle prompt, under ///Manager///.
    /// - ControlsModeManager, the touch controls canvas (MobileCombatButtons) and the touch aim assist.
    /// Menu: Tools > OpenworldCrime > Player > Setup Combat UI In Open Scene.
    /// </summary>
    public static class GameplayUIBuilder
    {
        private const string ManagerRootName = "///Manager///";
        private const string GameplayCanvasName = "Gameplay UI";
        private const string TouchCanvasName = "Touch Controls";
        private const string RccCanvasName = "RCC Mobile Controls";
        private const string RccCanvasPrefabPath = "Assets/RealisticCarControllerV3/Prefabs/UI/RCCCanvas.prefab";
        private const string CameraStatesPath = "Assets/_Project/Data/Camera/OWC_ShooterMelee_CameraStates.asset";
        private const string SourceBulletPath = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Weapon/_bullets/vDefaultBullet.prefab";
        private const string PlayerBulletPath = "Assets/_Project/Prefabs/Weapons/PlayerBullet.prefab";
        private const string SparkEffectPath = "Assets/Invector-3rdPersonController/Shooter/Scripts/ImpactEffect/ImpactEffects/ImpactEffect@Default.asset";
        private static readonly string[] WeaponPrefabPaths =
        {
            "Assets/_Project/Prefabs/Weapons/Pistol_Inventory.prefab",
            "Assets/_Project/Prefabs/Weapons/AssaultRifle_Inventory.prefab",
        };
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        private static Font uiFont;
        private static Sprite discSprite;
        private static Sprite panelSprite;

        [MenuItem("Tools/OpenworldCrime/Player/Setup Combat UI In Open Scene")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var playerManager = Object.FindFirstObjectByType<OpenworldCrime.Player.PlayerManager>(FindObjectsInactive.Include);
            GameObject managerRoot = playerManager != null ? FindRoot(playerManager.gameObject.scene, ManagerRootName) : null;
            if (playerManager == null || managerRoot == null) return "[GameplayUIBuilder] The open scene needs a player with PlayerManager and a ///Manager/// object.";

            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            discSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject player = playerManager.gameObject;
            RemoveOld(managerRoot);

            var sb = new StringBuilder("[GameplayUIBuilder]\n");
            EnsureVirtualInputNames(sb);
            ConfigurePlayer(player, sb);
            TuneAimCamera(sb);
            UseStraightBullets(sb);
            UIManager ui = BuildGameplayUI(managerRoot.transform, player, playerManager);
            ControlsModeManager controls = BuildControls(managerRoot.transform, player, playerManager);
            AddAimAssist(player, controls);
            WireWeaponController(player, playerManager);

            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(player.scene);
            sb.AppendLine("  UIManager, ControlsModeManager, Gameplay UI, Touch Controls and the aim assist are set up. Save the scene.");
            return sb.ToString();
        }

        // GameObject.Find treats the slashes of ///Manager/// as a path and skips inactive objects, so roots are matched by name.
        private static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        // Invector's GenericInput first asks the legacy Input Manager whether a button exists, even for touch input, so the
        // on-screen buttons' virtual names must be defined there or they never register as pressed.
        private static void EnsureVirtualInputNames(StringBuilder log)
        {
            string[] names = { "M_Aim", "M_Shoot", "M_Reload", "M_Jump", "M_Sprint", "M_Crouch", "M_Roll", "M_Interact", "M_Holster", "M_Vehicle" };
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
            SerializedProperty axes = manager.FindProperty("m_Axes");
            var existing = new HashSet<string>();
            for (int i = 0; i < axes.arraySize; i++) existing.Add(axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue);
            int added = 0;
            foreach (string name in names)
            {
                if (existing.Contains(name)) continue;
                axes.arraySize++;
                SerializedProperty axis = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                axis.FindPropertyRelative("m_Name").stringValue = name;
                foreach (string field in new[] { "descriptiveName", "descriptiveNegativeName", "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton" })
                    axis.FindPropertyRelative(field).stringValue = string.Empty;
                axis.FindPropertyRelative("gravity").floatValue = 1000f;
                axis.FindPropertyRelative("dead").floatValue = 0.001f;
                axis.FindPropertyRelative("sensitivity").floatValue = 1000f;
                axis.FindPropertyRelative("type").intValue = 0;
                axis.FindPropertyRelative("axis").intValue = 0;
                axis.FindPropertyRelative("joyNum").intValue = 0;
                added++;
            }
            manager.ApplyModifiedPropertiesWithoutUndo();
            if (added > 0) AssetDatabase.SaveAssets();
            log.AppendLine($"  Input Manager: {added} virtual button names added");
        }

        private static void RemoveOld(GameObject managerRoot)
        {
            foreach (string name in new[] { "UIManager", "ControlsModeManager" })
            {
                Transform old = managerRoot.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            foreach (string name in new[] { GameplayCanvasName, TouchCanvasName, RccCanvasName })
            {
                GameObject old = FindRoot(managerRoot.scene, name);
                if (old != null) Object.DestroyImmediate(old);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Player: aim and key bindings
        // ---------------------------------------------------------------------------------------------------------------
        private static void ConfigurePlayer(GameObject player, StringBuilder log)
        {
            var controller = player.GetComponent<vThirdPersonController>();
            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("_locomotionType").enumValueIndex = (int)vThirdPersonMotor.LocomotionType.FreeWithStrafe;
            // GTA-style movement: walk by default, run only while the sprint input is held, with no stamina limit.
            controllerSo.FindProperty("freeSpeed").FindPropertyRelative("walkByDefault").boolValue = true;
            // Matches the run animation: the Sprint clip at time scale 1 in OWC_Shooter_Josh covers about 6.5 m/s on this avatar.
            controllerSo.FindProperty("freeSpeed").FindPropertyRelative("runningSpeed").floatValue = 6.5f;
            controllerSo.FindProperty("useContinuousSprint").boolValue = false;
            controllerSo.FindProperty("_sprintStamina").floatValue = 0f;
            controllerSo.ApplyModifiedProperties();

            var combatSo = new SerializedObject(player.GetComponent<PlayerCombatController>());
            combatSo.FindProperty("strafeWhileArmed").boolValue = false;
            combatSo.ApplyModifiedProperties();

            var input = player.GetComponent<vShooterMeleeInput>();
            var inputSo = new SerializedObject(input);
            ClearKeyboard(inputSo.FindProperty("strafeInput"));                               // Tab: strafe comes from aiming only
            SetKeyboard(inputSo.FindProperty("switchCameraSideInput"), "LeftAlt");            // was Tab
            ClearKeyboard(inputSo.FindProperty("cameraZoomInput"));                           // scroll wheel switches weapons
            SetKeyboard(inputSo.FindProperty("strongAttackInput"), "Mouse2");                 // was 1
            inputSo.ApplyModifiedProperties();

            var lockOn = player.GetComponent<vLockOnShooter>();
            if (lockOn != null)
            {
                var lockOnSo = new SerializedObject(lockOn);
                ClearKeyboard(lockOnSo.FindProperty("lockOnInput"));                          // was Tab
                lockOnSo.ApplyModifiedProperties();
            }

            // The HUD shows weapon and ammo in one place, where the touch buttons don't cover it.
            var shooterSo = new SerializedObject(player.GetComponent<vShooterManager>());
            shooterSo.FindProperty("useAmmoDisplay").boolValue = false;
            // Firing without Aim lowers the gun almost at once: the aim posture belongs to the Aim input.
            shooterSo.FindProperty("hipfireAimTime").floatValue = 0.15f;
            shooterSo.ApplyModifiedProperties();
            var ammoDisplay = player.GetComponentInChildren<vAmmoDisplay>(true);
            if (ammoDisplay != null) ammoDisplay.gameObject.SetActive(false);

            log.AppendLine("  player: FreeWithStrafe, strafe only while aiming; Tab = wheel, Left Alt = camera side, middle mouse = strong melee, scroll and 1/2/3 = weapons");
        }

        private static void SetKeyboard(SerializedProperty genericInput, string key) => genericInput.FindPropertyRelative("keyboard").stringValue = key;

        private static void ClearKeyboard(SerializedProperty genericInput) => SetKeyboard(genericInput, string.Empty);

        private static void TuneAimCamera(StringBuilder log)
        {
            var states = AssetDatabase.LoadAssetAtPath<vThirdPersonCameraListData>(CameraStatesPath);
            if (states == null) { log.AppendLine("  camera states asset missing"); return; }
            foreach (vThirdPersonCameraState state in states.tpCameraStates)
            {
                if (state.Name != "Aiming") continue;
                state.right = 0.55f;
                state.height = 1.62f;
                state.defaultDistance = 2.2f;
                state.minDistance = Mathf.Min(state.minDistance, 2.2f);
                state.maxDistance = Mathf.Max(state.maxDistance, 2.2f);
                state.fov = 48f;
            }
            EditorUtility.SetDirty(states);
            AssetDatabase.SaveAssets();
        }

        // Invector's bullet has gravity, so shots land below the crosshair: this one flies straight to the aim point. Its decal
        // manager is the project's, which only leaves marks on exact surfaces (a full copy: a variant cannot swap the component's script).
        private static void UseStraightBullets(StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBulletPath);
            if (source == null) { log.AppendLine("  bullet prefab missing"); return; }
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject bullet;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "PlayerBullet";
                root.GetComponent<Rigidbody>().useGravity = false;
                SwapScript<vDecalManager, OpenworldCrime.Combat.SurfaceDecalManager>(root);
                SwapScript<vProjectileControl, OpenworldCrime.Combat.PlayerProjectile>(root);
                var sparkSource = AssetDatabase.LoadAssetAtPath<vImpactEffect>(SparkEffectPath);
                GameObject spark = sparkSource != null && sparkSource.hitEffects.Count > 0 ? sparkSource.hitEffects[0] : null;
                var sparkSo = new SerializedObject(root.GetComponent<OpenworldCrime.Combat.SurfaceDecalManager>());
                sparkSo.FindProperty("sparkEffect").objectReferenceValue = spark;
                sparkSo.ApplyModifiedPropertiesWithoutUndo();
                bullet = PrefabUtility.SaveAsPrefabAsset(root, PlayerBulletPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            foreach (string path in WeaponPrefabPaths)
            {
                GameObject weaponRoot = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    weaponRoot.GetComponentInChildren<vShooterWeapon>(true).projectile = bullet;
                    PrefabUtility.SaveAsPrefabAsset(weaponRoot, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(weaponRoot); }
            }
            log.AppendLine("  weapons fire PlayerBullet (no gravity)");
        }

        // Turns the component into its project subclass in place: its values and the events pointing at it stay.
        private static void SwapScript<TBase, TProject>(GameObject root) where TBase : MonoBehaviour where TProject : TBase
        {
            TProject probe = root.AddComponent<TProject>();
            MonoScript script = MonoScript.FromMonoBehaviour(probe);
            Object.DestroyImmediate(probe);
            var so = new SerializedObject(root.GetComponent<TBase>());
            so.FindProperty("m_Script").objectReferenceValue = script;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireWeaponController(GameObject player, OpenworldCrime.Player.PlayerManager playerManager)
        {
            var so = new SerializedObject(player.GetComponent<PlayerWeaponController>());
            so.FindProperty("playerManager").objectReferenceValue = playerManager;
            so.ApplyModifiedProperties();
        }

        private static void AddAimAssist(GameObject player, ControlsModeManager controls)
        {
            var assist = player.GetComponent<PlayerAimAssist>();
            if (assist == null) assist = player.AddComponent<PlayerAimAssist>();
            var camera = player.GetComponentInChildren<vThirdPersonCamera>(true);
            var so = new SerializedObject(assist);
            so.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombatController>();
            so.FindProperty("thirdPersonCamera").objectReferenceValue = camera;
            so.FindProperty("playerCamera").objectReferenceValue = camera != null ? camera.GetComponentInChildren<Camera>(true) : null;
            so.FindProperty("controlsMode").objectReferenceValue = controls;
            so.FindProperty("snapTime").floatValue = 0.35f;
            so.FindProperty("targetLayers").intValue = (1 << LayerMask.NameToLayer("BodyPart")) | (1 << LayerMask.NameToLayer("Enemy"));
            so.FindProperty("lineOfSightLayers").intValue = (1 << 0) | (1 << LayerMask.NameToLayer("Vehicle"));
            so.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Gameplay UI: HUD, weapon wheel, vehicle prompt
        // ---------------------------------------------------------------------------------------------------------------
        private static UIManager BuildGameplayUI(Transform managerRoot, GameObject player, OpenworldCrime.Player.PlayerManager playerManager)
        {
            Canvas canvas = NewCanvas(GameplayCanvasName, 40);
            Transform root = canvas.transform;

            // HUD, top right under Hamza's day / night buttons.
            RectTransform hud = NewPanel("HUD", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform info = NewPanel("WeaponInfo", hud, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -120f), new Vector2(360f, 86f), new Vector2(1f, 1f));
            AddImage(info.gameObject, panelSprite, new Color(0.05f, 0.05f, 0.08f, 0.55f));
            Text weaponName = NewText("WeaponName", info, 28, TextAnchor.MiddleRight, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(-16f, 0f), Color.white);
            Text ammo = NewText("Ammo", info, 34, TextAnchor.MiddleRight, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Color(1f, 0.85f, 0.35f, 1f));

            // Vehicle prompt and message, above Hamza's hint line.
            RectTransform prompt = NewPanel("VehiclePrompt", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(420f, 58f));
            AddImage(prompt.gameObject, panelSprite, new Color(0f, 0f, 0f, 0.6f));
            Text promptText = NewText("Text", prompt, 28, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Color.white);
            prompt.gameObject.SetActive(false);
            RectTransform message = NewPanel("VehicleMessage", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 180f), new Vector2(560f, 58f));
            AddImage(message.gameObject, panelSprite, new Color(0.35f, 0.05f, 0.05f, 0.7f));
            Text messageText = NewText("Text", message, 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Color.white);
            message.gameObject.SetActive(false);

            // Weapon wheel.
            RectTransform wheel = NewPanel("WeaponWheel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f));
            AddImage(wheel.gameObject, discSprite, new Color(0f, 0f, 0f, 0.45f));
            var view = wheel.gameObject.AddComponent<WeaponWheelView>();
            const int SlotCount = 3;
            const float SlotRadius = 240f;
            var slotViews = new List<(Image, Text)>();
            for (int i = 0; i < SlotCount; i++)
            {
                float angle = i * 360f / SlotCount * Mathf.Deg2Rad;
                RectTransform slot = NewPanel("Slot" + i, wheel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * SlotRadius, new Vector2(240f, 240f));
                Image background = AddImage(slot.gameObject, discSprite, new Color(0f, 0f, 0f, 0.6f));
                Text label = NewText("Label", slot, 30, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(-24f, 0f), Color.white);
                slotViews.Add((background, label));
            }
            Text selected = NewText("SelectedName", wheel, 36, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f), Vector2.zero, new Color(1f, 0.85f, 0.35f, 1f));
            RectTransform marker = NewPanel("DirectionMarker", wheel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            AddImage(marker.gameObject, discSprite, new Color(1f, 0.75f, 0.1f, 0.95f));

            var viewSo = new SerializedObject(view);
            SerializedProperty slots = viewSo.FindProperty("slots");
            slots.arraySize = SlotCount;
            for (int i = 0; i < SlotCount; i++)
            {
                SerializedProperty entry = slots.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("background").objectReferenceValue = slotViews[i].Item1;
                entry.FindPropertyRelative("label").objectReferenceValue = slotViews[i].Item2;
            }
            viewSo.FindProperty("directionMarker").objectReferenceValue = marker;
            viewSo.FindProperty("markerRadius").floatValue = SlotRadius * 0.55f;
            viewSo.FindProperty("selectedNameText").objectReferenceValue = selected;
            viewSo.ApplyModifiedProperties();
            wheel.gameObject.SetActive(false);

            var go = new GameObject("UIManager");
            go.transform.SetParent(managerRoot, false);
            var ui = go.AddComponent<UIManager>();
            var so = new SerializedObject(ui);
            so.FindProperty("playerManager").objectReferenceValue = playerManager;
            so.FindProperty("weaponController").objectReferenceValue = player.GetComponent<PlayerWeaponController>();
            so.FindProperty("hudPanel").objectReferenceValue = hud.gameObject;
            so.FindProperty("weaponInfoGroup").objectReferenceValue = info.gameObject;
            so.FindProperty("weaponNameText").objectReferenceValue = weaponName;
            so.FindProperty("ammoText").objectReferenceValue = ammo;
            so.FindProperty("weaponWheelPanel").objectReferenceValue = wheel.gameObject;
            so.FindProperty("weaponWheelView").objectReferenceValue = view;
            so.FindProperty("vehicleInteraction").objectReferenceValue = player.GetComponent<PlayerVehicleInteraction>();
            so.FindProperty("vehiclePromptPanel").objectReferenceValue = prompt.gameObject;
            so.FindProperty("vehiclePromptText").objectReferenceValue = promptText;
            so.FindProperty("vehicleMessagePanel").objectReferenceValue = message.gameObject;
            so.FindProperty("vehicleMessageText").objectReferenceValue = messageText;
            so.ApplyModifiedProperties();
            return ui;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Touch controls
        // ---------------------------------------------------------------------------------------------------------------
        private static ControlsModeManager BuildControls(Transform managerRoot, GameObject player, OpenworldCrime.Player.PlayerManager playerManager)
        {
            Canvas canvas = NewCanvas(TouchCanvasName, 30);
            Transform root = canvas.transform;

            RectTransform onFoot = NewPanel("OnFootControls", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Look area first: the buttons sit on top of it.
            RectTransform look = NewPanel("LookArea", onFoot, new Vector2(0.4f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(look.gameObject, null, new Color(1f, 1f, 1f, 0f));
            look.gameObject.AddComponent<VirtualTouchLook>();

            // Movement stick.
            RectTransform stickArea = NewPanel("MoveStick", onFoot, Vector2.zero, new Vector2(0.4f, 0.7f), Vector2.zero, Vector2.zero);
            AddImage(stickArea.gameObject, null, new Color(1f, 1f, 1f, 0f));
            RectTransform stickBackground = NewPanel("Background", stickArea, Vector2.zero, Vector2.zero, new Vector2(480f, 210f), new Vector2(240f, 240f));
            AddImage(stickBackground.gameObject, discSprite, new Color(1f, 1f, 1f, 0.2f));
            RectTransform stickHandle = NewPanel("Handle", stickBackground, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f));
            AddImage(stickHandle.gameObject, discSprite, new Color(1f, 1f, 1f, 0.55f));
            RectTransform sprintMarker = NewPanel("SprintMarker", stickArea, Vector2.zero, Vector2.zero, new Vector2(480f, 370f), new Vector2(120f, 44f));
            Text sprintText = NewText("Text", sprintMarker, 24, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, new Color(1f, 1f, 1f, 0.35f));
            sprintText.text = "SPRINT";
            var stick = stickArea.gameObject.AddComponent<VirtualJoystick>();
            var stickSo = new SerializedObject(stick);
            stickSo.FindProperty("background").objectReferenceValue = stickBackground;
            stickSo.FindProperty("handle").objectReferenceValue = stickHandle;
            stickSo.FindProperty("controller").objectReferenceValue = player.GetComponent<vThirdPersonController>();
            stickSo.FindProperty("sprintIndicator").objectReferenceValue = sprintText;
            stickSo.ApplyModifiedProperties();

            // Right thumb cluster; the buttons are anchored to the bottom right corner.
            GameObject fireRight = NewButton("Fire", onFoot, Corner.BottomRight, new Vector2(-170f, 170f), 190f, "FIRE", "M_Shoot", false, new Color(0.8f, 0.15f, 0.1f, 0.65f));
            GameObject punch = NewButton("Punch", onFoot, Corner.BottomRight, new Vector2(-170f, 170f), 190f, "HIT", "M_Shoot", false, new Color(0.8f, 0.45f, 0.1f, 0.65f));
            GameObject aim = NewButton("Aim", onFoot, Corner.BottomRight, new Vector2(-390f, 130f), 140f, "AIM", "M_Aim", true, new Color(0.1f, 0.35f, 0.8f, 0.65f));
            GameObject reload = NewButton("Reload", onFoot, Corner.BottomRight, new Vector2(-130f, 390f), 120f, "RELOAD", "M_Reload", false, new Color(0.2f, 0.2f, 0.2f, 0.65f));
            GameObject jump = NewButton("Jump", onFoot, Corner.BottomRight, new Vector2(-330f, 310f), 110f, "JUMP", "M_Jump", false, new Color(0.2f, 0.2f, 0.2f, 0.65f));
            GameObject vehicle = NewButton("Vehicle", onFoot, Corner.BottomRight, new Vector2(-130f, 550f), 110f, "CAR", "M_Vehicle", false, new Color(0.15f, 0.5f, 0.2f, 0.65f));
            GameObject weaponSwitch = NewButton("WeaponSwitch", onFoot, Corner.BottomRight, new Vector2(-300f, 490f), 120f, "WEAPON", string.Empty, false, new Color(0.45f, 0.2f, 0.6f, 0.65f));
            // Left thumb fire, shown while aiming.
            GameObject fireLeft = NewButton("FireLeft", onFoot, Corner.BottomLeft, new Vector2(170f, 560f), 150f, "FIRE", "M_Shoot", false, new Color(0.8f, 0.15f, 0.1f, 0.65f));

            var weaponController = player.GetComponent<PlayerWeaponController>();
            UnityEventTools.AddPersistentListener(GetOnPressed(weaponSwitch), weaponController.EquipNextSlot);

            var buttons = onFoot.gameObject.AddComponent<MobileCombatButtons>();
            var buttonsSo = new SerializedObject(buttons);
            buttonsSo.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombatController>();
            buttonsSo.FindProperty("fireRight").objectReferenceValue = fireRight;
            buttonsSo.FindProperty("fireLeft").objectReferenceValue = fireLeft;
            buttonsSo.FindProperty("aim").objectReferenceValue = aim;
            buttonsSo.FindProperty("reload").objectReferenceValue = reload;
            buttonsSo.FindProperty("punch").objectReferenceValue = punch;
            buttonsSo.ApplyModifiedProperties();
            onFoot.gameObject.SetActive(false);

            // In-vehicle exit button and RCC's own mobile driving canvas.
            RectTransform inVehicle = NewPanel("VehicleControls", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            NewButton("Exit", inVehicle, Corner.TopRight, new Vector2(-130f, -420f), 110f, "EXIT", "M_Vehicle", false, new Color(0.15f, 0.5f, 0.2f, 0.65f));
            inVehicle.gameObject.SetActive(false);

            var rccPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RccCanvasPrefabPath);
            GameObject rccCanvas = rccPrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(rccPrefab, player.scene) : null;
            if (rccCanvas != null)
            {
                rccCanvas.name = RccCanvasName;
                rccCanvas.SetActive(false);
            }

            var go = new GameObject("ControlsModeManager");
            go.transform.SetParent(managerRoot, false);
            var controls = go.AddComponent<ControlsModeManager>();
            var so = new SerializedObject(controls);
            so.FindProperty("useMobileControls").boolValue = false;
            so.FindProperty("playerManager").objectReferenceValue = playerManager;
            so.FindProperty("playerInput").objectReferenceValue = player.GetComponent<vThirdPersonInput>();
            so.FindProperty("onFootControls").objectReferenceValue = onFoot.gameObject;
            so.FindProperty("vehicleControls").objectReferenceValue = inVehicle.gameObject;
            so.FindProperty("rccCanvas").objectReferenceValue = rccCanvas;

            // Invector's slot display and the throw canvases carry key hints and sit under the thumbs.
            var keyboardOnly = new List<GameObject>();
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == "EquipmentDisplayWindow") keyboardOnly.Add(t.gameObject);
            foreach (var ui in player.GetComponentsInChildren<Invector.Throw.vThrowUI>(true))
                keyboardOnly.Add(ui.GetComponentInParent<Canvas>(true).gameObject);
            SerializedProperty keyboardObjects = so.FindProperty("keyboardOnlyObjects");
            keyboardObjects.arraySize = keyboardOnly.Count;
            for (int i = 0; i < keyboardOnly.Count; i++) keyboardObjects.GetArrayElementAtIndex(i).objectReferenceValue = keyboardOnly[i];
            so.ApplyModifiedProperties();
            return controls;
        }

        private enum Corner { BottomLeft, BottomRight, TopRight }

        private static GameObject NewButton(string name, Transform parent, Corner corner, Vector2 position, float size, string caption, string virtualButton, bool toggle, Color color)
        {
            Vector2 anchor = corner == Corner.BottomLeft ? Vector2.zero : corner == Corner.BottomRight ? new Vector2(1f, 0f) : Vector2.one;
            RectTransform rect = NewPanel(name, parent, anchor, anchor, position, new Vector2(size, size));
            Image image = AddImage(rect.gameObject, discSprite, color);
            Text label = NewText("Label", rect, Mathf.RoundToInt(size * 0.18f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Color.white);
            label.text = caption;
            label.raycastTarget = false;
            var button = rect.gameObject.AddComponent<VirtualButton>();
            var so = new SerializedObject(button);
            so.FindProperty("buttonName").stringValue = virtualButton;
            so.FindProperty("toggle").boolValue = toggle;
            so.FindProperty("targetGraphic").objectReferenceValue = image;
            so.ApplyModifiedProperties();
            return rect.gameObject;
        }

        private static UnityEngine.Events.UnityEvent GetOnPressed(GameObject button)
        {
            var field = typeof(VirtualButton).GetField("onPressed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (UnityEngine.Events.UnityEvent)field.GetValue(button.GetComponent<VirtualButton>());
        }

        // ---------------------------------------------------------------------------------------------------------------
        // UI helpers
        // ---------------------------------------------------------------------------------------------------------------
        private static Canvas NewCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static RectTransform NewPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 sizeDelta, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static Image AddImage(GameObject go, Sprite sprite, Color color)
        {
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            if (sprite == panelSprite) image.type = Image.Type.Sliced;
            return image;
        }

        private static Text NewText(string name, Transform parent, int size, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Color color)
        {
            RectTransform rect = NewPanel(name, parent, anchorMin, anchorMax, Vector2.zero, sizeDelta);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = uiFont;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
