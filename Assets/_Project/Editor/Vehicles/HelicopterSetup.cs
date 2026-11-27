using System.Text;
using OpenworldCrime.Controls;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The flyable helicopter (re-runnable; menu Tools > OpenworldCrime > Helicopter > Set Up Helicopter):
    /// - prefab Assets/_Project/Prefabs/Vehicles/Helicopter/Helicopter.prefab around the imported Helicopter_2.fbx (unchanged):
    ///   Rigidbody, box colliders (fuselage, tail boom, skids), interaction trigger, <see cref="HelicopterController"/>,
    ///   <see cref="VehicleHealth"/> (shared damage settings), seat / enter / exit points, a seated pilot copied from the car
    ///   driver figure, its own chase camera (<see cref="HelicopterCamera"/>, a copy of the main camera's settings), the
    ///   rotor loop (Assets/helicopterLoop.mp3), the boarding door + far-side view (<see cref="HelicopterBoarding"/>) and a map blip;
    /// - one placed on the Yatch_Port helipad ("Helicopters" in the scene);
    /// - touch controls (Touch Controls / HelicopterControls: stick + UP / DOWN), the keyboard hint and
    ///   <see cref="HelicopterHudView"/>.
    /// Menu "Update Helicopter Prefab" re-applies only the sound, rotor speeds, door / boarding view and enter / exit points to
    /// the existing prefab (every placed helicopter follows), without rebuilding it or touching the scene.
    /// </summary>
    public static class HelicopterSetup
    {
        private const string ModelPath = "Assets/Helicopter_2.fbx";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Vehicles/Helicopter";
        private const string PrefabPath = PrefabFolder + "/Helicopter.prefab";
        private const string ArtFolder = "Assets/_Project/Art/Helicopter";
        private const string RotorClipPath = "Assets/helicopterLoop.mp3";
        private const string DoorCloseClipPath = "Assets/_Project/Audio/VehicleFoley/Door/Heavy Door Close.wav";
        // Cabin door on the pilot's (right) side: the panel and its window, hinged at the front edge.
        private const string DoorPanelName = "_gltfNode_0_001";
        private const string DoorWindowName = "_gltfNode_1_003";
        // Pilot gets in / out through that door.
        private static readonly Vector3 EnterLocal = new Vector3(1.75f, 0f, 2.2f);
        private static readonly Vector3 ExitLocal = new Vector3(2.05f, 0f, 2.1f);
        private const string DamageSettingsPath = "Assets/_Project/Data/Vehicles/VehicleDamageSettings.asset";
        private const string DriverSourcePrefab = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car1.prefab";
        private const string HeliIconPath = "Assets/Game/UI/MapIcons/MapIcon_Helipad.png";
        private const string ButtonSheet = "Assets/Game_Play buttons final_2.png";
        private const string SceneRootName = "Helicopters";

        /// <summary>Centre of the H on the Yatch_Port helipad (measured from a top-down render of the marina).</summary>
        public static readonly Vector3 HelipadCentre = new Vector3(-254.6f, 0f, -764.0f);
        /// <summary>Heading on the pad: along the pier (Yatch_Port is turned 112 deg).</summary>
        public const float HelipadYaw = 22f;

        [MenuItem("Tools/OpenworldCrime/Helicopter/Set Up Helicopter")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[HelicopterSetup]\n");
            Scene scene = SceneManager.GetActiveScene();
            ProceduralArt.EnsureFolder(PrefabFolder);
            ProceduralArt.EnsureFolder(ArtFolder);

            AudioClip rotor = AssetDatabase.LoadAssetAtPath<AudioClip>(RotorClipPath);
            if (rotor == null) log.AppendLine("  WARNING: no rotor sound at " + RotorClipPath);
            GameObject prefab = BuildPrefab(rotor, log);
            log.AppendLine("  prefab: " + PrefabPath);

            // Scene: one on the marina helipad.
            GameObject root = ParachuteSetup.FindRoot(scene, SceneRootName);
            if (root != null) Undo.DestroyObjectImmediate(root);
            root = new GameObject(SceneRootName);
            Undo.RegisterCreatedObjectUndo(root, SceneRootName);
            float padY = PadHeight();
            var heli = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            heli.name = "Helicopter - Yatch_Port";
            heli.transform.SetParent(root.transform, false);
            heli.transform.SetPositionAndRotation(new Vector3(HelipadCentre.x, padY + 0.05f, HelipadCentre.z), Quaternion.Euler(0f, HelipadYaw, 0f));
            log.AppendLine($"  placed on the Yatch_Port helipad at ({HelipadCentre.x:F1}, {padY:F2}, {HelipadCentre.z:F1}), yaw {HelipadYaw}");

            log.AppendLine("  " + BuildUi(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        private static float PadHeight()
        {
            var hits = Physics.RaycastAll(new Vector3(HelipadCentre.x, 200f, HelipadCentre.z), Vector3.down, 300f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponentInParent<HelicopterController>() == null && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? 27.7f : best;
        }

        // ---------------------------------------------------------------- prefab

        private static GameObject BuildPrefab(AudioClip rotorClip, StringBuilder log)
        {
            var root = new GameObject("Helicopter") { layer = 19 }; // Vehicle
            var body = root.AddComponent<Rigidbody>();
            body.mass = 2200f;
            body.linearDamping = 0.05f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Model, levelled and centred: nose to +z, skids at y 0.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            Transform camTarget = FindDeep(model.transform, "camTarget");
            if (camTarget != null) camTarget.gameObject.SetActive(false); // a helper cube in the FBX
            // The FBX carries its own camera (FPSCamera) and maybe lights: off, they would render over the game camera.
            foreach (Camera fbxCamera in model.GetComponentsInChildren<Camera>(true)) fbxCamera.gameObject.SetActive(false);
            foreach (Light fbxLight in model.GetComponentsInChildren<Light>(true)) fbxLight.gameObject.SetActive(false);
            Bounds b = Bounds(model);
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = Bounds(model);
            SetLayer(model.transform, 19);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }
            Transform mainRotor = FindDeep(model.transform, "Main_Rotor");
            Transform tailRotor = FindDeep(model.transform, "Tail_Rotor");
            log.AppendLine($"  model bounds {b.size}, rotors: main {(mainRotor != null)}, tail {(tailRotor != null)}");

            // Colliders (Vehicle layer): fuselage, tail boom, skids.
            float length = b.size.z;
            Transform colliders = new GameObject("Colliders") { layer = 19 }.transform;
            colliders.SetParent(root.transform, false);
            AddBox(colliders, "Fuselage", new Vector3(0f, 1.75f, 1.7f), new Vector3(2.6f, 2.3f, 6.2f));
            AddBox(colliders, "Tail Boom", new Vector3(0f, 2.1f, -4.6f), new Vector3(0.8f, 1.2f, 6.6f));
            AddBox(colliders, "Skids", new Vector3(0f, 0.25f, 1.6f), new Vector3(2.8f, 0.5f, 4.6f));

            // Interaction trigger (the player's vehicle sensor).
            var trigger = new GameObject("Interaction Trigger") { layer = 2 };
            trigger.transform.SetParent(root.transform, false);
            var tc = trigger.AddComponent<BoxCollider>();
            tc.isTrigger = true;
            tc.center = new Vector3(0f, 1.5f, 1.2f);
            tc.size = new Vector3(7f, 3.5f, Mathf.Max(9f, length * 0.6f));

            // Points: the pilot sits on the right, gets in / out on the right.
            Transform seat = Point(root.transform, "DriverSeat", new Vector3(0.42f, 0.62f, 3.1f), Quaternion.identity);
            Transform enter = Point(root.transform, "EnterPoint", EnterLocal, Quaternion.Euler(0f, -90f, 0f));
            Transform exit = Point(root.transform, "ExitPoint", ExitLocal, Quaternion.Euler(0f, 90f, 0f));
            Transform altExit = Point(root.transform, "AlternateExitPoint", new Vector3(-2.3f, 0f, 3.1f), Quaternion.Euler(0f, -90f, 0f));
            Transform interaction = Point(root.transform, "InteractionPoint", new Vector3(1.4f, 1f, 3.1f), Quaternion.identity);

            // Seated pilot: a copy of the cars' seated driver figure.
            GameObject dummy = null;
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(DriverSourcePrefab);
            Transform carDriver = car != null ? car.transform.Find("DriverSeat/CarPlayer") : null;
            if (carDriver != null)
            {
                dummy = Object.Instantiate(carDriver.gameObject, seat);
                dummy.name = "PilotVisual";
                dummy.transform.localPosition = carDriver.localPosition;
                dummy.transform.localRotation = carDriver.localRotation;
                dummy.transform.localScale = carDriver.localScale;
                foreach (Collider c in dummy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                dummy.SetActive(false);
            }
            else log.AppendLine("  WARNING: no seated driver figure found in " + DriverSourcePrefab);

            // Audio.
            var audio = root.AddComponent<AudioSource>();
            audio.clip = rotorClip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0.3f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = 12f;
            audio.maxDistance = 300f;
            audio.volume = 0f;

            // Chase camera (inactive until flying), set up like the main camera.
            var camGo = new GameObject("Helicopter Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null)
            {
                cam.CopyFrom(main);
                var mainData = main.GetComponent<UniversalAdditionalCameraData>();
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                if (mainData != null) EditorUtility.CopySerialized(mainData, data);
            }
            cam.fieldOfView = 60f;
            cam.targetTexture = null;
            camGo.AddComponent<AudioListener>();
            var chase = camGo.AddComponent<HelicopterCamera>();
            chase.EditorSetup(root.transform);
            camGo.SetActive(false);

            // Health / crash damage.
            var health = root.AddComponent<VehicleHealth>();
            var damage = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DamageSettingsPath);
            var hso = new SerializedObject(health);
            hso.FindProperty("settings").objectReferenceValue = damage;
            hso.ApplyModifiedPropertiesWithoutUndo();
            if (damage == null) log.AppendLine("  WARNING: no VehicleDamageSettings at " + DamageSettingsPath);

            // Map blip (moves with it).
            var blip = root.AddComponent<MapBlip>();
            var bso = new SerializedObject(blip);
            bso.FindProperty("icon").objectReferenceValue = LoadSprite(HeliIconPath);
            bso.FindProperty("iconColour").colorValue = Color.white;
            bso.FindProperty("backgroundColour").colorValue = new Color(0.1f, 0.3f, 0.6f, 0.92f);
            bso.FindProperty("size").floatValue = 26f;
            bso.FindProperty("tracksMovement").boolValue = true;
            bso.ApplyModifiedPropertiesWithoutUndo();

            var controller = root.AddComponent<HelicopterController>();
            controller.EditorSetup(body, mainRotor, Vector3.forward, tailRotor, Vector3.right, health, seat, enter, exit, altExit, interaction,
                dummy, chase, audio);

            ConfigureBoardingAndSound(root, log);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        [MenuItem("Tools/OpenworldCrime/Helicopter/Update Helicopter Prefab (Sound, Rotor, Door)")]
        public static void UpdatePrefabMenu() => Debug.Log(UpdatePrefab());

        /// <summary>Re-applies sound, rotor speeds, door / boarding view and enter / exit points to the existing prefab.</summary>
        public static string UpdatePrefab()
        {
            var log = new StringBuilder("[HelicopterSetup] update prefab\n");
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform enter = root.transform.Find("EnterPoint");
                Transform exit = root.transform.Find("ExitPoint");
                if (enter != null) enter.localPosition = EnterLocal;
                if (exit != null) exit.localPosition = ExitLocal;
                var audio = root.GetComponent<AudioSource>();
                AudioClip rotor = AssetDatabase.LoadAssetAtPath<AudioClip>(RotorClipPath);
                if (audio != null && rotor != null) audio.clip = rotor;
                else log.AppendLine("  WARNING: rotor sound not set (" + RotorClipPath + ")");
                ConfigureBoardingAndSound(root, log);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return log.ToString();
        }

        // Rotor speeds / sound response on the controller, and the boarding door (panel + window round the front hinge), its
        // sound and the far-side boarding view (the chase camera).
        private static void ConfigureBoardingAndSound(GameObject root, StringBuilder log)
        {
            var controller = root.GetComponent<HelicopterController>();
            var so = new SerializedObject(controller);
            so.FindProperty("mainRotorSpeed").floatValue = 2600f;
            so.FindProperty("tailRotorSpeed").floatValue = 4200f;
            so.FindProperty("maxMainRotorStep").floatValue = 38f;
            so.FindProperty("maxTailRotorStep").floatValue = 40f;
            so.FindProperty("maxVolume").floatValue = 0.9f;
            so.FindProperty("idlePitch").floatValue = 0.6f;
            so.FindProperty("fullPitch").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Transform panel = FindDeep(root.transform, DoorPanelName);
            Transform window = FindDeep(root.transform, DoorWindowName);
            if (panel == null) log.AppendLine("  WARNING: door panel '" + DoorPanelName + "' not found: no door animation");
            // Hinge: the panel's outer front edge, in the helicopter's space.
            Vector3 hinge = new Vector3(1.1f, 2f, 2.92f);
            if (panel != null && panel.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = root.transform.InverseTransformPoint(panel.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
                hinge = new Vector3(max.x, (min.y + max.y) * 0.5f, max.z);
            }

            Transform doorAudioT = root.transform.Find("Door Audio");
            if (doorAudioT == null)
            {
                doorAudioT = new GameObject("Door Audio").transform;
                doorAudioT.SetParent(root.transform, false);
            }
            doorAudioT.localPosition = hinge;
            var doorAudio = doorAudioT.GetComponent<AudioSource>();
            if (doorAudio == null) doorAudio = doorAudioT.gameObject.AddComponent<AudioSource>();
            doorAudio.playOnAwake = false;
            doorAudio.spatialBlend = 1f;
            doorAudio.minDistance = 3f;
            doorAudio.maxDistance = 40f;
            doorAudio.volume = 0.8f;

            var boarding = root.GetComponent<HelicopterBoarding>();
            if (boarding == null) boarding = root.AddComponent<HelicopterBoarding>();
            var parts = window != null ? new[] { panel, window } : panel != null ? new[] { panel } : new Transform[0];
            boarding.EditorSetup(parts, hinge, root.GetComponentInChildren<HelicopterCamera>(true), doorAudio, null,
                AssetDatabase.LoadAssetAtPath<AudioClip>(DoorCloseClipPath));
            log.AppendLine($"  boarding door: {parts.Length} part(s), hinge {hinge}; rotor sound {RotorClipPath}");
        }

        // ---------------------------------------------------------------- UI

        private static string BuildUi(Scene scene)
        {
            GameObject touch = ParachuteSetup.FindRoot(scene, "Touch Controls");
            GameObject gameplayUi = ParachuteSetup.FindRoot(scene, "Gameplay UI");
            GameObject rcc = ParachuteSetup.FindRoot(scene, "RCC Mobile Controls");
            var controls = Object.FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
            if (touch == null || gameplayUi == null) return "WARNING: Touch Controls / Gameplay UI not found: no helicopter controls";

            Transform old = touch.transform.Find("HelicopterControls");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var group = new GameObject("HelicopterControls", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(group, "Helicopter Controls");
            group.layer = touch.layer;
            var groupRect = (RectTransform)group.transform;
            groupRect.SetParent(touch.transform, false);
            groupRect.anchorMin = Vector2.zero;
            groupRect.anchorMax = Vector2.one;
            groupRect.offsetMin = groupRect.offsetMax = Vector2.zero;

            // Stick (bottom left), like the on-foot move stick.
            var stickArea = UiImage(groupRect, "Stick", null, new Color(1f, 1f, 1f, 0f), new Vector2(0f, 0f), new Vector2(170f, 170f), new Vector2(460f, 460f));
            stickArea.pivot = new Vector2(0.5f, 0.5f);
            var background = UiImage(stickArea, "Background", LoadSprite(ButtonSheet, "MC_Joystick"), new Color(1f, 1f, 1f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 280f));
            var handle = UiImage(background, "Handle", LoadSprite(ButtonSheet, "MC_JoystickKnob"), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            handle.GetComponent<Image>().raycastTarget = false;
            stickArea.anchoredPosition = new Vector2(260f, 260f);
            var stick = stickArea.gameObject.AddComponent<VirtualJoystick>();
            var sso = new SerializedObject(stick);
            sso.FindProperty("horizontalAxis").stringValue = "HeliStickX";
            sso.FindProperty("verticalAxis").stringValue = "HeliStickY";
            sso.FindProperty("background").objectReferenceValue = background;
            sso.FindProperty("handle").objectReferenceValue = handle;
            sso.FindProperty("sprintButtonName").stringValue = "";
            sso.ApplyModifiedPropertiesWithoutUndo();

            // UP / DOWN (bottom right).
            Texture2D up = ProceduralArt.Icon(ArtFolder + "/HeliUp.png", (x, y) => Arrow(x, y, true), new Color(0.06f, 0.2f, 0.45f, 0.85f));
            Texture2D down = ProceduralArt.Icon(ArtFolder + "/HeliDown.png", (x, y) => Arrow(x, y, false), new Color(0.06f, 0.2f, 0.45f, 0.85f));
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Button(groupRect, "Up", AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/HeliUp.png"), new Vector2(-180f, 420f), "M_HeliUp", "UP", font);
            Button(groupRect, "Down", AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/HeliDown.png"), new Vector2(-180f, 200f), "M_HeliDown", "DOWN", font);
            group.SetActive(false);

            // Keyboard hint.
            Transform oldHint = gameplayUi.transform.Find("Helicopter Keys");
            if (oldHint != null) Undo.DestroyObjectImmediate(oldHint.gameObject);
            var hint = UiImage((RectTransform)gameplayUi.transform, "Helicopter Keys", null, new Color(0f, 0f, 0f, 0.45f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1180f, 52f));
            Undo.RegisterCreatedObjectUndo(hint.gameObject, "Helicopter Keys");
            hint.GetComponent<Image>().raycastTarget = false;
            var hintText = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var htr = (RectTransform)hintText.transform;
            htr.SetParent(hint, false);
            htr.anchorMin = Vector2.zero;
            htr.anchorMax = Vector2.one;
            htr.offsetMin = htr.offsetMax = Vector2.zero;
            var text = hintText.GetComponent<Text>();
            Transform interact = gameplayUi.transform.Find("InteractPrompt");
            Text interactText = interact != null ? interact.GetComponentInChildren<Text>(true) : null;
            text.font = interactText != null ? interactText.font : font;
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = "W / S  FLY     A / D  TURN     Q / E  STRAFE     SPACE  UP     CTRL  DOWN     F  EXIT (LANDED)";
            hint.gameObject.SetActive(false);

            Transform oldView = gameplayUi.transform.Find("Helicopter HUD View");
            if (oldView != null) Undo.DestroyObjectImmediate(oldView.gameObject);
            var viewGo = new GameObject("Helicopter HUD View", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(viewGo, "Helicopter HUD View");
            viewGo.transform.SetParent(gameplayUi.transform, false);
            var view = viewGo.AddComponent<HelicopterHudView>();
            var vso = new SerializedObject(view);
            vso.FindProperty("touchControls").objectReferenceValue = group;
            vso.FindProperty("keyHint").objectReferenceValue = hint.gameObject;
            vso.FindProperty("rccCanvas").objectReferenceValue = rcc;
            vso.FindProperty("controlsMode").objectReferenceValue = controls;
            vso.ApplyModifiedPropertiesWithoutUndo();
            return "UI: Touch Controls/HelicopterControls (stick HeliStickX/Y, M_HeliUp, M_HeliDown), hint 'Helicopter Keys', view 'Helicopter HUD View'";
        }

        private static float Arrow(float x, float y, bool up)
        {
            float s = up ? 1f : -1f;
            return Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, -0.45f * s), new Vector2(0f, 0.45f * s), 0.1f),
                Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, 0.5f * s), new Vector2(-0.38f, 0.1f * s), 0.1f),
                          ProceduralArt.Segment(x, y, new Vector2(0f, 0.5f * s), new Vector2(0.38f, 0.1f * s), 0.1f)));
        }

        private static void Button(RectTransform parent, string name, Sprite sprite, Vector2 position, string buttonName, string label, Font font)
        {
            var rect = UiImage(parent, name, sprite, new Color(1f, 1f, 1f, 0.95f), new Vector2(1f, 0f), position, new Vector2(170f, 170f));
            var vb = rect.gameObject.AddComponent<VirtualButton>();
            var so = new SerializedObject(vb);
            so.FindProperty("buttonName").stringValue = buttonName;
            so.FindProperty("targetGraphic").objectReferenceValue = rect.GetComponent<Image>();
            so.FindProperty("pressedTint").colorValue = new Color(0.75f, 0.9f, 1f, 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            var t = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var tr = (RectTransform)t.transform;
            tr.SetParent(rect, false);
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(0f, 30f);
            var text = t.GetComponent<Text>();
            text.font = font;
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;
        }

        private static RectTransform UiImage(RectTransform parent, string name, Sprite sprite, Color colour, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            return rect;
        }

        // ---------------------------------------------------------------- helpers

        private static void AddBox(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        private static Transform Point(Transform parent, string name, Vector3 local, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rotation;
            return go.transform;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private static Sprite LoadSprite(string path, string name = null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite s && (name == null || s.name == name)) return s;
            return null;
        }
    }
}
