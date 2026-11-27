using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Controls;
using OpenworldCrime.Environment;
using OpenworldCrime.Player;
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
    /// The flyable jet (re-runnable; menu Tools > OpenworldCrime > Plane > Set Up Plane):
    /// - takes the scene's own "Embraer Phenom 100 private jet airplane" object (the imported model, kept as it is) and puts it
    ///   as "Model" under a new plane root "Planes / Jet - Phenom 100" on the airport apron: levelled, nose to +z, scaled to a
    ///   13.5 m fuselage (the real one is 12.8 m), the pivot at the main wheels on the ground;
    /// - Rigidbody, box colliders (fuselage, wings, tail, engines; Vehicle layer), the interaction trigger at the airstair door,
    ///   seat / enter / exit points, a seated pilot copied from the car driver figure, its own chase camera
    ///   (<see cref="HelicopterCamera"/>, pitch follow), the engine loop (Assets/planeloop.mp3), <see cref="VehicleHealth"/>
    ///   (shared damage settings), the airstair door + far-side boarding view (<see cref="HelicopterBoarding"/>), a map blip and
    ///   <see cref="PlaneController"/>;
    /// - touch controls (Touch Controls / PlaneControls: stick + THR + / THR -), the keyboard hint, <see cref="PlaneHudView"/>
    ///   and the flight instruments panel (<see cref="FlightInstrumentsView"/>, also used by the helicopter).
    /// Nothing of the imported model or its materials is changed.
    /// </summary>
    public static class PlaneSetup
    {
        private const string JetName = "Embraer Phenom 100 private jet airplane";
        private const string RootGroupName = "Planes";
        private const string PlaneName = "Jet - Phenom 100";
        private const string EngineClipPath = "Assets/planeloop.mp3";
        private const string DoorCloseClipPath = "Assets/_Project/Audio/VehicleFoley/Door/Heavy Door Close.wav";
        private const string DamageSettingsPath = "Assets/_Project/Data/Vehicles/VehicleDamageSettings.asset";
        private const string DriverSourcePrefab = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car1.prefab";
        private const string HelicopterPrefab = "Assets/_Project/Prefabs/Vehicles/Helicopter/Helicopter.prefab";
        private const string IconPath = "Assets/_Project/Art/Airport/AirportIcon.png";
        private const string ArtFolder = "Assets/_Project/Art/Plane";
        private const string ButtonSheet = "Assets/Game_Play buttons final_2.png";

        // Model parts (mesh names inside the imported model).
        private const string FuselagePart = "fuselage_01_mainmat_0";
        private const string WingPart = "wing_01_mainmat_0";
        private const string TailFinPart = "tail_01_mainmat_0";
        private const string StabiliserPart = "tail_stabilizer001_01_mainmat_0";
        private const string EnginePart = "engine_01_mainmat_0";
        private const string DoorPart = "door_01_mainmat_0";
        private const string CockpitGlassPart = "wind_cocpt_fr_07 - gwindows_0";
        private const string MainGearPart = "mid_chass_tyre_01_mainmat_0";

        /// <summary>Fuselage length (m) the model is scaled to.</summary>
        private const float FuselageLength = 13.5f;
        /// <summary>Parking spot: on the northern stand of the apron, nose west towards the taxiway.</summary>
        public static readonly Vector3 StandPosition = new Vector3(-592f, 0f, -15f);
        public const float StandYaw = -90f;
        private const int VehicleLayer = 19;

        [MenuItem("Tools/OpenworldCrime/Plane/Set Up Plane")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[PlaneSetup]\n");
            Scene scene = SceneManager.GetActiveScene();
            ProceduralArt.EnsureFolder(ArtFolder);

            GameObject jet = FindJet(scene);
            if (jet == null) return "[PlaneSetup] No '" + JetName + "' in the scene.";

            // --- root (reused on a re-run: everything but the model is rebuilt)
            GameObject group = ParachuteSetup.FindRoot(scene, RootGroupName);
            if (group == null)
            {
                group = new GameObject(RootGroupName);
                Undo.RegisterCreatedObjectUndo(group, RootGroupName);
            }
            Transform oldPlane = group.transform.Find(PlaneName);
            if (oldPlane != null && jet.transform.IsChildOf(oldPlane))
            {
                Undo.SetTransformParent(jet.transform, null, "Plane model");
            }
            if (oldPlane != null) Undo.DestroyObjectImmediate(oldPlane.gameObject);

            var root = new GameObject(PlaneName) { layer = VehicleLayer };
            Undo.RegisterCreatedObjectUndo(root, PlaneName);
            root.transform.SetParent(group.transform, false);
            float groundY = GroundHeight(StandPosition);
            root.transform.SetPositionAndRotation(new Vector3(StandPosition.x, groundY, StandPosition.z), Quaternion.Euler(0f, StandYaw, 0f));

            // --- model: nose (+x of the model) to +z, scaled, pivot at the main wheels on the ground
            Undo.SetTransformParent(jet.transform, root.transform, "Plane model");
            Undo.RecordObject(jet.transform, "Plane model");
            jet.name = JetName; // keeps its name: it is the scene's original object
            jet.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            jet.transform.localPosition = Vector3.zero;
            jet.transform.localScale = Vector3.one;
            Bounds fuselage = LocalBounds(Part(jet, FuselagePart), root.transform);
            float scale = FuselageLength / Mathf.Max(0.01f, fuselage.size.z);
            jet.transform.localScale = Vector3.one * scale;
            Bounds gear = LocalBounds(Part(jet, MainGearPart), root.transform);
            Bounds all = ModelBounds(jet, root.transform);
            jet.transform.localPosition = new Vector3(-all.center.x, -gear.min.y, -gear.center.z);
            SetLayer(jet.transform, VehicleLayer);
            foreach (Renderer r in jet.GetComponentsInChildren<Renderer>(true))
            {
                Undo.RecordObject(r, "Plane renderers");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }
            // Old colliders on the model (none expected) would fight the boxes below.
            foreach (Collider c in jet.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);

            fuselage = LocalBounds(Part(jet, FuselagePart), root.transform);
            Bounds wing = LocalBounds(Part(jet, WingPart), root.transform);
            Bounds fin = LocalBounds(Part(jet, TailFinPart), root.transform);
            Bounds stabiliser = LocalBounds(Part(jet, StabiliserPart), root.transform);
            Bounds engines = LocalBounds(Part(jet, EnginePart), root.transform);
            Bounds door = LocalBounds(Part(jet, DoorPart), root.transform);
            Bounds cockpit = LocalBounds(Part(jet, CockpitGlassPart), root.transform);
            all = ModelBounds(jet, root.transform);
            log.AppendLine($"  model scale {scale:F3} (fuselage {FuselageLength} m), plane bounds {all.size}, door at {door.center}, ground y {groundY:F2}");

            // --- physics
            var body = root.AddComponent<Rigidbody>();
            body.mass = 4800f;
            body.linearDamping = 0f;
            body.angularDamping = 2f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Transform colliders = Child(root.transform, "Colliders", Vector3.zero, Quaternion.identity);
            colliders.gameObject.layer = VehicleLayer;
            AddBox(colliders, "Fuselage", Shrink(fuselage, 0.92f, 0.95f, 1f));
            // Wing: a thin slab through the wing's middle (its bounds include the dihedral).
            AddBox(colliders, "Wing", new Bounds(new Vector3(wing.center.x, wing.min.y + wing.size.y * 0.45f, wing.center.z),
                new Vector3(wing.size.x * 0.97f, Mathf.Min(0.5f, wing.size.y), wing.size.z * 0.6f)));
            AddBox(colliders, "Tail Fin", Shrink(fin, 0.6f, 0.95f, 0.8f));
            AddBox(colliders, "Stabiliser", new Bounds(stabiliser.center, new Vector3(stabiliser.size.x * 0.97f, Mathf.Min(0.35f, stabiliser.size.y), stabiliser.size.z * 0.7f)));
            AddBox(colliders, "Engines", Shrink(engines, 0.95f, 0.9f, 0.95f));

            // --- interaction trigger: the front left (the airstair door)
            var trigger = new GameObject("Interaction Trigger") { layer = 2 };
            trigger.transform.SetParent(root.transform, false);
            var tc = trigger.AddComponent<BoxCollider>();
            tc.isTrigger = true;
            tc.center = new Vector3(door.min.x - 1.2f, 1.6f, door.center.z + 0.5f);
            tc.size = new Vector3(4.5f, 3.6f, 7f);

            // --- points (door on the left, -x)
            float doorZ = door.center.z;
            Transform enter = Child(root.transform, "EnterPoint", new Vector3(door.min.x - 1.1f, 0f, doorZ), Quaternion.Euler(0f, 90f, 0f));
            Transform exit = Child(root.transform, "ExitPoint", new Vector3(door.min.x - 1.6f, 0f, doorZ - 0.3f), Quaternion.Euler(0f, -90f, 0f));
            Transform altExit = Child(root.transform, "AlternateExitPoint", new Vector3(door.min.x - 1.6f, 0f, doorZ + 2.2f), Quaternion.Euler(0f, -90f, 0f));
            Transform interaction = Child(root.transform, "InteractionPoint", new Vector3(door.min.x - 0.4f, 1.1f, doorZ), Quaternion.identity);
            // Left pilot seat: behind the windscreen, floor at the door sill.
            Transform seat = Child(root.transform, "DriverSeat", new Vector3(-0.34f, door.min.y + 0.08f, cockpit.min.z - 0.55f), Quaternion.identity);
            Transform effects = Child(root.transform, "Effect Anchor", new Vector3(0f, engines.center.y, engines.center.z), Quaternion.identity);

            // --- seated pilot (copy of the cars' seated driver figure)
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
            else log.AppendLine("  WARNING: no seated driver figure in " + DriverSourcePrefab);

            // --- engine sound
            AudioClip engineClip = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineClipPath);
            if (engineClip == null) log.AppendLine("  WARNING: no engine sound at " + EngineClipPath);
            Transform engineAudioT = Child(root.transform, "Engine Audio", new Vector3(0f, engines.center.y, engines.center.z), Quaternion.identity);
            var audio = engineAudioT.gameObject.AddComponent<AudioSource>();
            audio.clip = engineClip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0.2f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = 15f;
            audio.maxDistance = 500f;
            audio.volume = 0f;

            // --- chase camera (inactive until flying), set up like the main camera
            var camGo = new GameObject("Plane Camera") { tag = "MainCamera" };
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
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1500f);
            cam.targetTexture = null;
            camGo.AddComponent<AudioListener>();
            var chase = camGo.AddComponent<HelicopterCamera>();
            chase.EditorSetup(root.transform);
            chase.EditorSetupFraming(24f, 6.5f, 10f, 85f, 14f, 2.5f, 4.5f, 2.6f, 0.55f);
            camGo.SetActive(false);

            // --- health / crash damage / explosion
            var health = root.AddComponent<VehicleHealth>();
            var hso = new SerializedObject(health);
            hso.FindProperty("settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DamageSettingsPath);
            hso.FindProperty("effectAnchor").objectReferenceValue = effects;
            hso.ApplyModifiedPropertiesWithoutUndo();

            // --- map blip
            var blip = root.AddComponent<MapBlip>();
            var bso = new SerializedObject(blip);
            bso.FindProperty("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            bso.FindProperty("iconColour").colorValue = Color.white;
            bso.FindProperty("backgroundColour").colorValue = new Color(0.1f, 0.3f, 0.6f, 0.92f);
            bso.FindProperty("size").floatValue = 26f;
            bso.FindProperty("tracksMovement").boolValue = true;
            bso.ApplyModifiedPropertiesWithoutUndo();

            // --- controller
            var controller = root.AddComponent<PlaneController>();
            controller.EditorSetup(body, health, Object.FindFirstObjectByType<WaterManager>(FindObjectsInactive.Include), seat, enter, exit, altExit,
                interaction, dummy, chase, audio, GroundLayers());
            // The map's limit (Tools > OpenworldCrime > World > Set Up World Boundary), when it is there.
            controller.EditorSetBoundary(Object.FindAnyObjectByType<WorldBoundary>(FindObjectsInactive.Include));

            // --- airstair door (hinged at its lower outer edge along the fuselage, folds outwards and down) + boarding view
            Transform doorPart = Part(jet, DoorPart);
            var hinge = new Vector3(door.min.x + 0.05f, door.min.y, doorZ);
            Transform doorAudioT = Child(root.transform, "Door Audio", hinge, Quaternion.identity);
            var doorAudio = doorAudioT.gameObject.AddComponent<AudioSource>();
            doorAudio.playOnAwake = false;
            doorAudio.spatialBlend = 1f;
            doorAudio.minDistance = 3f;
            doorAudio.maxDistance = 40f;
            doorAudio.volume = 0.8f;
            var boarding = root.AddComponent<HelicopterBoarding>();
            boarding.EditorSetup(doorPart != null ? new[] { doorPart } : new Transform[0], hinge, chase, doorAudio, null,
                AssetDatabase.LoadAssetAtPath<AudioClip>(DoorCloseClipPath));
            var boSo = new SerializedObject(boarding);
            boSo.FindProperty("hingeAxis").vector3Value = Vector3.forward;
            boSo.FindProperty("openAngle").floatValue = 100f;
            boSo.FindProperty("doorSeconds").floatValue = 0.8f;
            // Far (right) side, ahead of the wing: the fuselage hides the move from the door onto the seat.
            boSo.FindProperty("viewPosition").vector3Value = new Vector3(9f, 3.2f, doorZ + 7f);
            boSo.FindProperty("viewLookAt").vector3Value = new Vector3(0f, 1.8f, doorZ - 1f);
            boSo.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine($"  plane '{PlaneName}' on the apron at {root.transform.position}, yaw {StandYaw}; seat {seat.localPosition}, enter {enter.localPosition}");
            log.AppendLine("  " + BuildUi(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        // ---------------------------------------------------------------- scene helpers

        private static GameObject FindJet(Scene scene)
        {
            foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (!EditorUtility.IsPersistent(g) && g.scene == scene && g.name == JetName) return g;
            return null;
        }

        private static float GroundHeight(Vector3 at)
        {
            var hits = Physics.RaycastAll(new Vector3(at.x, 200f, at.z), Vector3.down, 300f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponentInParent<PlaneController>() == null && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? 27.75f : best;
        }

        // The helicopter's ground layers (the same world surfaces), else Default.
        private static LayerMask GroundLayers()
        {
            var heli = AssetDatabase.LoadAssetAtPath<GameObject>(HelicopterPrefab);
            var hc = heli != null ? heli.GetComponent<HelicopterController>() : null;
            if (hc == null) return 1;
            return new SerializedObject(hc).FindProperty("groundLayers").intValue;
        }

        private static Transform Part(GameObject model, string name)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Debug.LogWarning("[PlaneSetup] Model part not found: " + name);
            return null;
        }

        // Bounds of a part's mesh in the plane root's space (exact box of its 8 corners, not the world AABB).
        private static Bounds LocalBounds(Transform part, Transform root)
        {
            if (part == null || !part.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) return new Bounds();
            Bounds b = mf.sharedMesh.bounds;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = root.InverseTransformPoint(part.TransformPoint(corner));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        private static Bounds ModelBounds(GameObject model, Transform root)
        {
            bool first = true;
            var all = new Bounds();
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds b = LocalBounds(mf.transform, root);
                if (first) { all = b; first = false; }
                else all.Encapsulate(b);
            }
            return all;
        }

        private static Bounds Shrink(Bounds b, float x, float y, float z) => new Bounds(b.center, Vector3.Scale(b.size, new Vector3(x, y, z)));

        private static void AddBox(Transform parent, string name, Bounds local)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }

        private static Transform Child(Transform parent, string name, Vector3 local, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rotation;
            return go.transform;
        }

        private static void SetLayer(Transform t, int layer)
        {
            Undo.RecordObject(t.gameObject, "Plane layer");
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        // ---------------------------------------------------------------- UI

        private static string BuildUi(Scene scene)
        {
            GameObject touch = ParachuteSetup.FindRoot(scene, "Touch Controls");
            GameObject gameplayUi = ParachuteSetup.FindRoot(scene, "Gameplay UI");
            GameObject rcc = ParachuteSetup.FindRoot(scene, "RCC Mobile Controls");
            var controls = Object.FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
            if (touch == null || gameplayUi == null) return "WARNING: Touch Controls / Gameplay UI not found: no plane controls";
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Transform interact = gameplayUi.transform.Find("InteractPrompt");
            Text interactText = interact != null ? interact.GetComponentInChildren<Text>(true) : null;
            Font hudFont = interactText != null ? interactText.font : font;

            // --- touch controls: stick (bottom left) + THR + / THR - (bottom right), like the helicopter's
            Transform old = touch.transform.Find("PlaneControls");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var group = new GameObject("PlaneControls", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(group, "Plane Controls");
            group.layer = touch.layer;
            var groupRect = (RectTransform)group.transform;
            groupRect.SetParent(touch.transform, false);
            groupRect.anchorMin = Vector2.zero;
            groupRect.anchorMax = Vector2.one;
            groupRect.offsetMin = groupRect.offsetMax = Vector2.zero;

            var stickArea = UiImage(groupRect, "Stick", null, new Color(1f, 1f, 1f, 0f), Vector2.zero, new Vector2(260f, 260f), new Vector2(460f, 460f));
            stickArea.pivot = new Vector2(0.5f, 0.5f);
            stickArea.anchoredPosition = new Vector2(260f, 260f);
            var background = UiImage(stickArea, "Background", LoadSprite(ButtonSheet, "MC_Joystick"), new Color(1f, 1f, 1f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 280f));
            var handle = UiImage(background, "Handle", LoadSprite(ButtonSheet, "MC_JoystickKnob"), Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            handle.GetComponent<Image>().raycastTarget = false;
            var stick = stickArea.gameObject.AddComponent<VirtualJoystick>();
            var sso = new SerializedObject(stick);
            sso.FindProperty("horizontalAxis").stringValue = "PlaneStickX";
            sso.FindProperty("verticalAxis").stringValue = "PlaneStickY";
            sso.FindProperty("background").objectReferenceValue = background;
            sso.FindProperty("handle").objectReferenceValue = handle;
            sso.FindProperty("sprintButtonName").stringValue = "";
            sso.ApplyModifiedPropertiesWithoutUndo();

            ProceduralArt.Icon(ArtFolder + "/ThrottleUp.png", (x, y) => Sign(x, y, true), new Color(0.06f, 0.2f, 0.45f, 0.85f));
            ProceduralArt.Icon(ArtFolder + "/ThrottleDown.png", (x, y) => Sign(x, y, false), new Color(0.06f, 0.2f, 0.45f, 0.85f));
            Button(groupRect, "Throttle Up", AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/ThrottleUp.png"), new Vector2(-180f, 420f), "M_PlaneThrottleUp", "THR +", font);
            Button(groupRect, "Throttle Down", AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/ThrottleDown.png"), new Vector2(-180f, 200f), "M_PlaneThrottleDown", "THR - / BRAKE", font);
            group.SetActive(false);

            // --- keyboard hint
            Transform oldHint = gameplayUi.transform.Find("Plane Keys");
            if (oldHint != null) Undo.DestroyObjectImmediate(oldHint.gameObject);
            var hint = UiImage((RectTransform)gameplayUi.transform, "Plane Keys", null, new Color(0f, 0f, 0f, 0.45f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1500f, 52f));
            Undo.RegisterCreatedObjectUndo(hint.gameObject, "Plane Keys");
            hint.GetComponent<Image>().raycastTarget = false;
            Text hintText = UiText(hint, "Text", hudFont, 24, TextAnchor.MiddleCenter, Color.white,
                "W / S  THROTTLE (S AT IDLE = BRAKE)     A / D  ROLL / STEER     UP / DOWN  NOSE DOWN / UP     Q / E  RUDDER     F  EXIT (STOPPED)");
            Stretch((RectTransform)hintText.transform);
            hint.gameObject.SetActive(false);

            // --- view
            Transform oldView = gameplayUi.transform.Find("Plane HUD View");
            if (oldView != null) Undo.DestroyObjectImmediate(oldView.gameObject);
            var viewGo = new GameObject("Plane HUD View", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(viewGo, "Plane HUD View");
            viewGo.transform.SetParent(gameplayUi.transform, false);
            viewGo.AddComponent<PlaneHudView>().EditorSetup(group, hint.gameObject, rcc, controls);

            return "UI: Touch Controls/PlaneControls (stick PlaneStickX/Y, M_PlaneThrottleUp/Down), hint 'Plane Keys', view 'Plane HUD View'; "
                   + BuildInstruments(scene, gameplayUi, hudFont);
        }

        // Speed / altitude / throttle panel (top centre), shared by the helicopter and the plane.
        private static string BuildInstruments(Scene scene, GameObject gameplayUi, Font font)
        {
            Transform old = gameplayUi.transform.Find("Flight Instruments");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var panel = UiImage((RectTransform)gameplayUi.transform, "Flight Instruments", null, new Color(0f, 0f, 0f, 0.5f),
                new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(480f, 96f));
            Undo.RegisterCreatedObjectUndo(panel.gameObject, "Flight Instruments");
            panel.pivot = new Vector2(0.5f, 1f);
            // Under the car radio station bar (top centre, shown in every vehicle).
            panel.anchoredPosition = new Vector2(0f, -200f);
            panel.GetComponent<Image>().raycastTarget = false;
            Text speed = Cell(panel, "Speed", "KM/H", font, -160f);
            Text altitude = Cell(panel, "Altitude", "ALT M", font, 0f);
            Text throttle = Cell(panel, "Throttle", "THR %", font, 160f);
            var stall = UiText(panel, "Stall", font, 30, TextAnchor.MiddleCenter, new Color(1f, 0.25f, 0.2f, 1f), "STALL");
            var stallRect = (RectTransform)stall.transform;
            stallRect.anchorMin = stallRect.anchorMax = new Vector2(0.5f, 0f);
            stallRect.pivot = new Vector2(0.5f, 1f);
            stallRect.sizeDelta = new Vector2(200f, 40f);
            stallRect.anchoredPosition = new Vector2(0f, -4f);
            stall.fontStyle = FontStyle.Bold;
            stall.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);

            Transform oldView = gameplayUi.transform.Find("Flight Instruments View");
            if (oldView != null) Undo.DestroyObjectImmediate(oldView.gameObject);
            var viewGo = new GameObject("Flight Instruments View", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(viewGo, "Flight Instruments View");
            viewGo.transform.SetParent(gameplayUi.transform, false);
            var players = new List<PlayerVehicleInteraction>();
            foreach (PlayerVehicleInteraction p in Resources.FindObjectsOfTypeAll<PlayerVehicleInteraction>())
                if (!EditorUtility.IsPersistent(p) && p.gameObject.scene == scene) players.Add(p);
            viewGo.AddComponent<FlightInstrumentsView>().EditorSetup(players.ToArray(), panel.gameObject, speed, altitude, throttle,
                throttle.transform.parent.gameObject, stall.gameObject);
            return $"instruments 'Flight Instruments' + view ({players.Count} players)";
        }

        private static Text Cell(RectTransform panel, string name, string label, Font font, float x)
        {
            var cell = new GameObject(name, typeof(RectTransform));
            cell.layer = panel.gameObject.layer;
            var rect = (RectTransform)cell.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150f, 90f);
            rect.anchoredPosition = new Vector2(x, 0f);
            Text value = UiText(rect, "Value", font, 44, TextAnchor.MiddleCenter, Color.white, "0");
            var vr = (RectTransform)value.transform;
            vr.anchorMin = new Vector2(0f, 0.3f);
            vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = vr.offsetMax = Vector2.zero;
            value.fontStyle = FontStyle.Bold;
            Text caption = UiText(rect, "Label", font, 20, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 0.9f, 1f), label);
            var cr = (RectTransform)caption.transform;
            cr.anchorMin = Vector2.zero;
            cr.anchorMax = new Vector2(1f, 0.32f);
            cr.offsetMin = cr.offsetMax = Vector2.zero;
            return value;
        }

        private static float Sign(float x, float y, bool plus)
        {
            float bar = ProceduralArt.Segment(x, y, new Vector2(-0.45f, 0f), new Vector2(0.45f, 0f), 0.12f);
            return plus ? Mathf.Max(bar, ProceduralArt.Segment(x, y, new Vector2(0f, -0.45f), new Vector2(0f, 0.45f), 0.12f)) : bar;
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
            Text text = UiText(rect, "Label", font, 22, TextAnchor.UpperCenter, Color.white, label);
            var tr = (RectTransform)text.transform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(80f, 30f);
            text.fontStyle = FontStyle.Bold;
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

        private static Text UiText(RectTransform parent, string name, Font font, int size, TextAnchor alignment, Color colour, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Sprite LoadSprite(string path, string name = null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite s && (name == null || s.name == name)) return s;
            return null;
        }
    }
}
