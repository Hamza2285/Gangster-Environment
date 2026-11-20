using System.IO;
using System.Text;
using OpenworldCrime.Club;
using OpenworldCrime.Interiors;
using OpenworldCrime.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds the club activities to the saved club scene without touching its bake (all objects are dynamic or unlit):
    /// two sit spots facing the catwalk, cash throwing, the paid VIP-booth private dance and the club HUD (buttons + fade).
    /// Also generates the player's Humanoid sit loops from the pedestrian sit poses (Generic Biped clips sampled onto a
    /// Humanoid model of the same skeleton and stored as muscle curves, so they retarget to Jason and Mia).
    /// Menu: Tools > OpenworldCrime > Club > 5 Build Club Activities.
    /// </summary>
    public static class ClubActivitiesBuilder
    {
        private const string MaleSitPath = ClubInteriorBuilder.AnimationFolder + "/ClubSit_Male.anim";
        private const string FemaleSitPath = ClubInteriorBuilder.AnimationFolder + "/ClubSit_Female.anim";
        private const string ManSitSource = "Assets/_Project/Animations/Pedestrians/PedMan_sit_low_a.anim";
        private const string GirlSitSource = "Assets/_Project/Animations/Pedestrians/PedGirl_sit_low_a.anim";
        private const string SamplingModel = "casual07_f"; // Humanoid, same Bip01 skeleton as the pedestrians
        private const string ThrowClipFile = "Assets/Invector-3rdPersonController/Shooter/Scripts/ThrowSystem/Animations/Throw_Animations.fbx";
        private const string ThrowClipName = "Throwing@Forward";
        private const string ThrowSoundPath = "Assets/Invector-3rdPersonController/Basic Locomotion/Audio/Others/pickup.wav";
        private const string BanknoteTexturePath = ClubInteriorBuilder.ArtFolder + "/Banknote.png";
        private const string BanknoteMaterialPath = ClubInteriorBuilder.MaterialFolder + "/Club_Banknote.mat";
        private const string BoothDancerModel = "nude04_f"; // yellow bikini: reads clearly as swimwear under the warm booth light
        private const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // Layout (Club.fbx space, metres). Seats: the free lounge chairs facing the catwalk (cushion 0.36 m).
        private struct SeatSpot
        {
            public string Name;
            public Vector3 ChairPivot;
            public float Yaw;
        }

        private static readonly SeatSpot[] Seats =
        {
            new SeatSpot { Name = "Catwalk Seat A", ChairPivot = new Vector3(0.94f, -1.69f, 2.86f), Yaw = 180f },
            new SeatSpot { Name = "Catwalk Seat B", ChairPivot = new Vector3(6.28f, -1.71f, -6.2f), Yaw = 0f },
        };
        private const float ChairCushion = 0.36f;
        private const float HipsAboveSeat = 0.1f;
        private static readonly Vector3 CatwalkFocus = new Vector3(4.5f, 0.3f, -1.6f);

        // VIP booth on the upper gallery (RoomSofa (2), cushion 0.41 m on the 4.4 m floor), bought at a lounge marker.
        private static readonly Vector3 PrivateMarker = new Vector3(-12f, 0f, 10.5f);
        private const float PrivateStandYaw = 90f;
        private static readonly Vector3 BoothHips = new Vector3(3.4f, 4.4f + 0.41f + HipsAboveSeat, -16.55f);
        private static readonly Vector3 BoothDancer = new Vector3(3.25f, 4.4f, -15.0f);
        private static readonly Vector3 BoothCamera = new Vector3(3.95f, 6.05f, -17.0f);
        private static readonly Vector3 BoothLight = new Vector3(3.3f, 6.9f, -15.4f);
        private static readonly Bounds BoothProps = new Bounds(new Vector3(3.1f, 5.2f, -14.9f), new Vector3(1.6f, 1.6f, 1.6f));

        [MenuItem("Tools/OpenworldCrime/Club/5 Build Club Activities (sit, cash, private dance; keeps the bake)")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[ClubActivitiesBuilder]\n");
            AnimationClip maleSit = BuildHumanoidSitClip(ManSitSource, MaleSitPath, log);
            AnimationClip femaleSit = BuildHumanoidSitClip(GirlSitSource, FemaleSitPath, log);
            Material banknote = BanknoteMaterial();

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ClubInteriorBuilder.ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ClubInteriorBuilder.ScenePath, OpenSceneMode.Additive);
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects()) if (r.GetComponent<InteriorScene>() != null) root = r;
            if (root == null) return "[ClubActivitiesBuilder] club root not found";
            SceneManager.SetActiveScene(scene);

            Transform old = root.transform.Find("Activities");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var activities = new GameObject("Activities").transform;
            activities.SetParent(root.transform, false);

            // Seating controller.
            var seatingGo = new GameObject("Club Seating");
            seatingGo.transform.SetParent(activities, false);
            var seating = seatingGo.AddComponent<ClubPlayerSeating>();
            var seatingSo = new SerializedObject(seating);
            seatingSo.FindProperty("maleSitClip").objectReferenceValue = maleSit;
            seatingSo.FindProperty("femaleSitClip").objectReferenceValue = femaleSit;
            seatingSo.FindProperty("throwGestureClip").objectReferenceValue = FindClip(ThrowClipFile, ThrowClipName);
            seatingSo.ApplyModifiedPropertiesWithoutUndo();

            // Cash thrower with its pooled banknote particles.
            var cashGo = new GameObject("Cash Thrower");
            cashGo.transform.SetParent(activities, false);
            var thrower = cashGo.AddComponent<ClubCashThrower>();
            ParticleSystem notes = BuildBanknoteParticles(cashGo.transform, banknote);
            var audio = cashGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.6f;
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ThrowSoundPath);
            var throwerSo = new SerializedObject(thrower);
            throwerSo.FindProperty("seating").objectReferenceValue = seating;
            throwerSo.FindProperty("cashBurst").objectReferenceValue = notes;
            throwerSo.FindProperty("throwAudio").objectReferenceValue = audio;
            throwerSo.ApplyModifiedPropertiesWithoutUndo();

            // Sit spots.
            Material markerMaterial = ClubInteriorBuilder.AdditiveMaterial("Club_Marker", null, new Color(0.75f, 0.2f, 1f, 0.6f));
            foreach (SeatSpot spot in Seats) BuildSeat(activities, seating, spot, markerMaterial);

            // HUD + fade (club scene canvas).
            BuildHud(activities, seating, thrower, out ClubHudView hud, out ScreenFader fader);

            // Private dance.
            ClubPrivateDance dance = BuildPrivateDance(root.transform, activities, seating, fader, markerMaterial, log);
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("privateDance").objectReferenceValue = dance;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (previousActive.IsValid() && previousActive != scene) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            log.AppendLine($"  {Seats.Length} seats, cash thrower, private dance, HUD; saved {ClubInteriorBuilder.ScenePath} (bake kept)");
            return log.ToString();
        }

        // ---------------------------------------------------------------- seats

        private static void BuildSeat(Transform parent, ClubPlayerSeating seating, SeatSpot spot, Material markerMaterial)
        {
            Quaternion facing = Quaternion.Euler(0f, spot.Yaw, 0f);
            Vector3 forward = facing * Vector3.forward;
            var go = new GameObject(spot.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spot.ChairPivot + forward * 0.85f;
            go.transform.localRotation = facing;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(1.2f, 2f, 1.1f);

            Transform seatPoint = NewPoint("Seat Point", parent, spot.ChairPivot + Vector3.up * (ChairCushion + HipsAboveSeat) + forward * 0.05f, facing);
            seatPoint.SetParent(go.transform, true);
            Vector3 cameraPosition = spot.ChairPivot - forward * 1.25f + Vector3.up * 1.85f;
            Transform cameraPoint = NewPoint("Camera Point", parent, cameraPosition, Quaternion.LookRotation(CatwalkFocus - cameraPosition));
            cameraPoint.SetParent(go.transform, true);
            GameObject marker = NewMarker(go.transform, markerMaterial, 0.9f);

            var seat = go.AddComponent<ClubSeat>();
            var so = new SerializedObject(seat);
            so.FindProperty("seating").objectReferenceValue = seating;
            so.FindProperty("seatPoint").objectReferenceValue = seatPoint;
            so.FindProperty("cameraPoint").objectReferenceValue = cameraPoint;
            so.FindProperty("marker").objectReferenceValue = marker;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- private dance

        private static ClubPrivateDance BuildPrivateDance(Transform root, Transform parent, ClubPlayerSeating seating, ScreenFader fader, Material markerMaterial, StringBuilder log)
        {
            var go = new GameObject("Private Dance");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = PrivateMarker;
            go.transform.localRotation = Quaternion.Euler(0f, PrivateStandYaw, 0f);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(1.6f, 2f, 1.6f);

            var markerRoot = new GameObject("Marker");
            markerRoot.transform.SetParent(go.transform, false);
            NewMarker(markerRoot.transform, markerMaterial, 1.2f);
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "Marker Column";
            Object.DestroyImmediate(column.GetComponent<Collider>());
            column.transform.SetParent(markerRoot.transform, false);
            column.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            column.transform.localScale = new Vector3(1.0f, 0.6f, 1.0f);
            var columnRenderer = column.GetComponent<MeshRenderer>();
            columnRenderer.sharedMaterial = ClubInteriorBuilder.AdditiveMaterial("Club_EntranceColumn", null, new Color(0.7f, 0.15f, 1f, 0.35f));
            columnRenderer.shadowCastingMode = ShadowCastingMode.Off;
            AddFloatingText(markerRoot.transform, "PRIVATE DANCE", new Vector3(0f, 2.3f, 0f), 2.6f, new Color(1f, 0.5f, 0.95f));

            Transform standPoint = NewPoint("Stand Point", go.transform, Vector3.zero, Quaternion.identity);

            var booth = new GameObject("VIP Booth").transform;
            booth.SetParent(parent, false);
            Transform seat = NewPoint("Booth Seat", booth, BoothHips, Quaternion.identity);
            Transform view = NewPoint("Booth Camera", booth, BoothCamera, Quaternion.LookRotation(BoothDancer + Vector3.up * 1.05f - BoothCamera));

            var lightGo = new GameObject("Booth Light");
            lightGo.transform.SetParent(booth, false);
            lightGo.transform.localPosition = BoothLight;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.color = new Color(1f, 0.62f, 0.55f); // warm amber-pink, not so pink that a pink outfit blends into skin
            light.range = 4.5f;
            light.intensity = 2.4f;
            light.shadows = LightShadows.None;

            GameObject dancer = BuildBoothDancer(booth);

            // Table and bottles in front of the sofa make room for the dancer.
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(parent)) continue;
                if (r.name.StartsWith("Table") || r.name.StartsWith("Milk") || r.name.StartsWith("Whiskey") || r.name.StartsWith("Wine") || r.name.StartsWith("Beer") || r.name.StartsWith("Vang"))
                    if (BoothProps.Contains(root.InverseTransformPoint(r.bounds.center))) hidden.Add(r.gameObject);
            }

            var dance = go.AddComponent<ClubPrivateDance>();
            var so = new SerializedObject(dance);
            so.FindProperty("seating").objectReferenceValue = seating;
            so.FindProperty("fader").objectReferenceValue = fader;
            so.FindProperty("boothSeat").objectReferenceValue = seat;
            so.FindProperty("boothCamera").objectReferenceValue = view;
            so.FindProperty("standPoint").objectReferenceValue = standPoint;
            so.FindProperty("dancer").objectReferenceValue = dancer;
            so.FindProperty("boothLight").objectReferenceValue = light;
            so.FindProperty("marker").objectReferenceValue = markerRoot;
            SerializedProperty list = so.FindProperty("hideDuringDance");
            list.arraySize = hidden.Count;
            for (int i = 0; i < hidden.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  private dance marker at {PrivateMarker}, booth props hidden during a dance: {hidden.Count}");
            return dance;
        }

        private static GameObject BuildBoothDancer(Transform booth)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ClubInteriorBuilder.FemaleModelPath(BoothDancerModel));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, booth.gameObject.scene);
            go.name = "Booth Dancer (" + BoothDancerModel + ")";
            go.transform.SetParent(booth, false);
            go.transform.localPosition = BoothDancer;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var animator = go.GetComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ClubInteriorBuilder.DancerController);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.quality = SkinQuality.Bone2;
                smr.lightProbeUsage = LightProbeUsage.BlendProbes;
                Material[] mats = smr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) mats[m] = ClubInteriorBuilder.CharacterMaterial(mats[m]);
                smr.sharedMaterials = mats;
            }
            var member = go.AddComponent<ClubCrowdMember>();
            var so = new SerializedObject(member);
            so.FindProperty("animator").objectReferenceValue = animator;
            SerializedProperty states = so.FindProperty("states");
            string[] dances = { "Arms Hip Hop Dance", "Salsa Dancing", "Snake Hip Hop Dance" };
            states.arraySize = dances.Length;
            for (int i = 0; i < dances.Length; i++) states.GetArrayElementAtIndex(i).stringValue = dances[i];
            so.FindProperty("switchInterval").vector2Value = new Vector2(10f, 15f);
            so.FindProperty("isDancer").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return go;
        }

        // ---------------------------------------------------------------- HUD

        private static void BuildHud(Transform parent, ClubPlayerSeating seating, ClubCashThrower thrower, out ClubHudView hud, out ScreenFader fader)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("Club HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45; // above the gameplay HUD (40) and touch controls (30)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var hudGo = NewRect("Club HUD", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hud = hudGo.AddComponent<ClubHudView>();

            // Actions (bottom centre: clear of the joystick and the combat buttons).
            GameObject actions = NewRect("Actions Panel", hudGo.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(850f, 120f));
            Button throwButton = NewButton("Throw Cash Button", actions.transform, new Vector2(-215f, 0f), new Vector2(410f, 112f), new Color(0.12f, 0.55f, 0.28f, 0.92f), font, out Text throwLabel);
            Button leaveButton = NewButton("Leave Button", actions.transform, new Vector2(215f, 0f), new Vector2(410f, 112f), new Color(0.45f, 0.1f, 0.55f, 0.92f), font, out Text leaveLabel);
            GameObject timerGo = NewRect("Timer", actions.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 60f), new Vector2(780f, 60f));
            Text timer = AddText(timerGo, font, 36, new Color(1f, 0.7f, 1f), "PRIVATE DANCE  0:40");

            GameObject message = NewRect("Message Panel", hudGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(900f, 90f));
            message.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            GameObject messageTextGo = NewRect("Text", message.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Text messageText = AddText(messageTextGo, font, 36, new Color(1f, 0.85f, 0.4f), "Not enough cash");

            // Black fade with caption (ScreenFader toggles the image object).
            var faderGo = NewRect("Club Fader", canvasGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            GameObject black = NewRect("Black", faderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var blackImage = black.AddComponent<Image>();
            blackImage.color = Color.black;
            blackImage.raycastTarget = true;
            GameObject captionGo = NewRect("Caption", black.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Text caption = AddText(captionGo, font, 64, new Color(1f, 0.55f, 1f), "");
            fader = faderGo.AddComponent<ScreenFader>();
            var faderSo = new SerializedObject(fader);
            faderSo.FindProperty("image").objectReferenceValue = blackImage;
            faderSo.FindProperty("caption").objectReferenceValue = caption;
            faderSo.ApplyModifiedPropertiesWithoutUndo();
            black.SetActive(false);

            var so = new SerializedObject(hud);
            so.FindProperty("seating").objectReferenceValue = seating;
            so.FindProperty("cashThrower").objectReferenceValue = thrower;
            so.FindProperty("actionsPanel").objectReferenceValue = actions;
            so.FindProperty("throwButton").objectReferenceValue = throwButton;
            so.FindProperty("throwLabel").objectReferenceValue = throwLabel;
            so.FindProperty("leaveButton").objectReferenceValue = leaveButton;
            so.FindProperty("leaveLabel").objectReferenceValue = leaveLabel;
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("messagePanel").objectReferenceValue = message;
            so.FindProperty("messageText").objectReferenceValue = messageText;
            so.ApplyModifiedPropertiesWithoutUndo();
            actions.SetActive(false);
            message.SetActive(false);
        }

        private static Button NewButton(string name, Transform parent, Vector2 position, Vector2 size, Color color, Font font, out Text label)
        {
            GameObject go = NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            GameObject labelGo = NewRect("Label", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label = AddText(labelGo, font, 30, Color.white, name);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            return button;
        }

        private static GameObject NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return go;
        }

        private static Text AddText(GameObject go, Font font, int size, Color color, string value)
        {
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject NewMarker(Transform parent, Material material, float diameter)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Marker";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            ring.transform.localScale = new Vector3(diameter, 0.01f, diameter);
            var r = ring.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ring;
        }

        private static void AddFloatingText(Transform parent, string value, Vector3 localPosition, float fontSize, Color color)
        {
            var go = new GameObject("Label " + value);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            go.AddComponent<OpenworldCrime.Interiors.MarkerBob>();
        }

        private static Transform NewPoint(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            return t;
        }

        private static AnimationClip FindClip(string file, string clipName)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(file))
                if (o is AnimationClip c && c.name == clipName) return c;
            Debug.LogWarning("[ClubActivitiesBuilder] clip not found: " + clipName);
            return null;
        }

        // ---------------------------------------------------------------- banknotes

        private static ParticleSystem BuildBanknoteParticles(Transform parent, Material material)
        {
            var go = new GameObject("Cash Burst");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 5.5f);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = 0.17f;
            main.startSizeY = 0.08f;
            main.startSizeZ = 1f;
            main.startRotation3D = true;
            main.gravityModifier = 0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 700;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            // The notes land on the stage / floor and lie there until they fade (they used to fall through it at once).
            CashCollision(ps);
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rotation.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 50f;
            drag.drag = 0.5f; // light: the notes reach the dancer on the stage before they flutter down
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static Material BanknoteMaterial()
        {
            if (!File.Exists(BanknoteTexturePath)) BuildBanknoteTexture();
            var material = AssetDatabase.LoadAssetAtPath<Material>(BanknoteMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Club_Banknote" };
                AssetDatabase.CreateAsset(material, BanknoteMaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BanknoteTexturePath));
            material.SetColor("_BaseColor", new Color(0.9f, 0.95f, 0.9f, 1f));
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // 128x64 green banknote: border, inner frame and a centre medallion.
        private static void BuildBanknoteTexture()
        {
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var paper = new Color(0.55f, 0.74f, 0.5f);
            var ink = new Color(0.16f, 0.36f, 0.2f);
            var light = new Color(0.78f, 0.88f, 0.72f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = paper;
                    bool border = x < 4 || y < 4 || x >= w - 4 || y >= h - 4;
                    bool frame = (x == 9 || x == w - 10 || y == 9 || y == h - 10) && x >= 9 && x <= w - 10 && y >= 9 && y <= h - 10;
                    float d = new Vector2(x - w * 0.5f, (y - h * 0.5f) * 1.2f).magnitude;
                    if (d < 17f) c = light;
                    if (d > 15f && d < 17.5f) c = ink;
                    if (border || frame) c = ink;
                    if ((x > 16 && x < 30 && y > 18 && y < 46) || (x > w - 31 && x < w - 17 && y > 18 && y < 46)) c = Color.Lerp(paper, ink, 0.35f);
                    tex.SetPixel(x, y, c);
                }
            File.WriteAllBytes(BanknoteTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(BanknoteTexturePath);
        }

        // ---------------------------------------------------------------- humanoid sit clips

        /// <summary>
        /// Samples a Generic Biped sit clip onto a Humanoid model of the same skeleton and stores the result as Humanoid
        /// muscle curves (loops; retargets to any Humanoid player).
        /// </summary>
        private static AnimationClip BuildHumanoidSitClip(string sourcePath, string outPath, StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ClubInteriorBuilder.FemaleModelPath(SamplingModel));
            if (source == null || model == null) { log.AppendLine("  missing sit source or sampling model"); return null; }
            // The copy keeps the model's name (the avatar maps bones from the root by name) and loses its Animator
            // (with an Animator present, SampleAnimation does not write the Generic curves to the transforms).
            Avatar avatar = model.GetComponent<Animator>().avatar;
            var go = Object.Instantiate(model, Vector3.zero, Quaternion.identity);
            go.name = model.name;
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DestroyImmediate(go.GetComponent<Animator>());
            try
            {
                var handler = new HumanPoseHandler(avatar, go.transform);
                var pose = new HumanPose();
                int muscles = HumanTrait.MuscleCount;
                var curves = new AnimationCurve[muscles];
                for (int i = 0; i < muscles; i++) curves[i] = new AnimationCurve();
                var root = new AnimationCurve[7];
                for (int i = 0; i < 7; i++) root[i] = new AnimationCurve();
                const float step = 0.25f;
                for (float t = 0f; t <= source.length + 0.001f; t += step)
                {
                    float time = Mathf.Min(t, source.length);
                    source.SampleAnimation(go, time);
                    handler.GetHumanPose(ref pose);
                    for (int i = 0; i < muscles; i++) curves[i].AddKey(time, pose.muscles[i]);
                    root[0].AddKey(time, 0f);
                    root[1].AddKey(time, pose.bodyPosition.y);
                    root[2].AddKey(time, 0f);
                    // Facing is set by the seat: keep the body's pitch / roll, drop its yaw.
                    Quaternion q = Quaternion.Euler(pose.bodyRotation.eulerAngles.x, 0f, pose.bodyRotation.eulerAngles.z);
                    root[3].AddKey(time, q.x);
                    root[4].AddKey(time, q.y);
                    root[5].AddKey(time, q.z);
                    root[6].AddKey(time, q.w);
                }
                handler.Dispose();

                var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outPath) };
                for (int i = 0; i < muscles; i++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleBinding(HumanTrait.MuscleName[i])), curves[i]);
                string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
                for (int i = 0; i < 7; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), rootNames[i]), root[i]);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                ClubInteriorBuilder.EnsureFolder(Path.GetDirectoryName(outPath).Replace('\\', '/'));
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    Object.DestroyImmediate(clip);
                    clip = existing;
                }
                else AssetDatabase.CreateAsset(clip, outPath);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                log.AppendLine($"  {outPath}: Humanoid={clip.humanMotion} from {Path.GetFileName(sourcePath)}");
                return clip;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // HumanTrait finger muscle names ("Left Index 1 Stretched") use a different binding name ("LeftHand.Index.1 Stretched").
        private static string MuscleBinding(string muscle)
        {
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in fingers)
                {
                    string prefix = side + " " + finger + " ";
                    if (muscle.StartsWith(prefix)) return side + "Hand." + finger + "." + muscle.Substring(prefix.Length);
                }
            return muscle;
        }

        public static void CashCollision(ParticleSystem ps)
        {
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.85f;
            collision.bounce = 0.05f;
            collision.lifetimeLoss = 0f;
            collision.radiusScale = 0.6f;
            collision.collidesWith = ~(1 << 2);
            collision.enableDynamicColliders = false;
        }
    }
}
