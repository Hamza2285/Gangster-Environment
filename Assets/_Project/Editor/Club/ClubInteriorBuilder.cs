using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Club;
using OpenworldCrime.Interiors;
using OpenworldCrime.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the night club interior (Club.fbx) as an additive interior scene, following the gun shop pattern: the scene is
    /// authored ~4 km from the city (beyond the 800 m far plane), has an <see cref="InteriorScene"/> root, an exit door and
    /// its own baked lighting, and is entered through a <see cref="ShopEntrance"/> on a city storefront.
    /// The user's source assets (Club.fbx, the character pack, the dance clips) only get import settings and material remaps.
    /// Menu: Tools > OpenworldCrime > Club (run 1 - 4 in order; re-running rebuilds the generated objects).
    /// </summary>
    public static class ClubInteriorBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Club_Interior.unity";
        public const string SceneName = "Club_Interior";
        private const string CityScenePath = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";
        private const string ClubModelPath = "Assets/Club.fbx";
        internal const string ArtFolder = "Assets/_Project/Art/Club";
        internal const string MaterialFolder = ArtFolder + "/Materials";
        private const string CharacterMaterialFolder = ArtFolder + "/Characters";
        internal const string AnimationFolder = "Assets/_Project/Animations/Club";
        private const string EntrancePrefabPath = "Assets/_Project/Prefabs/Interiors/ClubEntrance.prefab";
        private const string LightingSettingsPath = ArtFolder + "/ClubInterior.lighting";
        private const string GradeProfilePath = ArtFolder + "/ClubInteriorGrade.asset";
        private const string MapIconPath = ArtFolder + "/ClubMapIcon.png";
        private const string FemaleFolder = "Assets/Player/Models/Female";
        private const string ManFolder = "Assets/PopulationSystem/Models/man";
        private const string ManClips = "Assets/PopulationSystem/Animations/man";
        private const string GirlClips = "Assets/PopulationSystem/Animations/girl";
        private const string PedClips = "Assets/_Project/Animations/Pedestrians";

        internal const string DancerController = AnimationFolder + "/ClubDancer.controller";
        private const string GirlController = AnimationFolder + "/ClubPatronGirl.controller";
        private const string ManController = AnimationFolder + "/ClubPatronMan.controller";

        /// <summary>~4 km from the city on the opposite side to the gun shop (-4000, 200, -4000).</summary>
        public static readonly Vector3 InteriorOrigin = new Vector3(-4000f, 200f, 4000f);

        // City entrance: the "PLAY NEXT" storefront east of the start (facade at x 756.7, facing -X onto the sidewalk).
        public static readonly Vector3 EntrancePosition = new Vector3(752.5f, 27.74f, 189f);
        public const float EntranceYaw = 270f;
        private const float FacadeDistance = 3.5f;

        // Club layout (Club.fbx space, metres; measured from its geometry).
        private static readonly Vector3 SpawnPoint = new Vector3(17.6f, 0.02f, 17.6f);
        private static readonly Vector3 ExitDoorCenter = new Vector3(19.1f, 1.1f, 17.6f);
        private static readonly Vector3 ExitDoorSize = new Vector3(1.2f, 2.2f, 3.8f);

        private static readonly string[] DanceStates = { "Arms Hip Hop Dance", "Salsa Dancing", "Snake Hip Hop Dance" };
        private static readonly string[] DanceClipFiles = { "Assets/Arms Hip Hop Dance.fbx", "Assets/Salsa Dancing.fbx", "Assets/Snake Hip Hop Dance.fbx" };
        private static readonly string[] HumanoidModels = { "nude01_f", "nude02_f", "nude03_f", "nude04_f", "casual07_f", "casual10_f" };

        private enum Kind { Dancer, Girl, Man }

        private struct CrowdSpot
        {
            public string Name;
            public string Model;
            public Kind Kind;
            public Vector3 Position;
            public float Yaw;
            public string[] States;
            public Vector2 Switch;
            public bool Seated;

            public CrowdSpot(string name, string model, Kind kind, Vector3 position, float yaw, string[] states, Vector2 switchInterval, bool seated = false)
            {
                Name = name; Model = model; Kind = kind; Position = position; Yaw = yaw; States = states; Switch = switchInterval; Seated = seated;
            }
        }

        private static readonly string[] Cheer = { "girl_cheer", "girl_claphands" };
        private static readonly string[] Hype = { "Hands_up", "man_claphands" };
        private static readonly string[] GirlChat = { "girl_talk1", "girl_talk2", "girl_listen" };
        private static readonly string[] ManChat = { "man_talk1", "man_talk2", "man_listen" };

        // 18 characters: 6 dancers, 4 cheering by the catwalk, 3 seated facing it, 4 in the bar lounge, the DJ.
        private static readonly CrowdSpot[] Crowd =
        {
            // Dancers: swimwear models on the catwalk, two more in the pit.
            new CrowdSpot("Dancer Pole", "nude02_f", Kind.Dancer, new Vector3(2.75f, -0.9f, -1.67f), 270f, DanceStates, new Vector2(18f, 30f)),
            new CrowdSpot("Dancer Catwalk A", "nude04_f", Kind.Dancer, new Vector3(5.6f, -0.9f, -0.4f), 215f, DanceStates, new Vector2(18f, 30f)),
            new CrowdSpot("Dancer Catwalk B", "nude01_f", Kind.Dancer, new Vector3(5.6f, -0.9f, -3.0f), 320f, DanceStates, new Vector2(18f, 30f)),
            new CrowdSpot("Dancer Catwalk End", "nude03_f", Kind.Dancer, new Vector3(9.5f, -0.9f, -1.7f), 270f, DanceStates, new Vector2(18f, 30f)),
            new CrowdSpot("Dancer Floor A", "casual07_f", Kind.Dancer, new Vector3(-1.0f, -1.7f, -8.8f), 20f, DanceStates, new Vector2(20f, 34f)),
            new CrowdSpot("Dancer Floor B", "casual10_f", Kind.Dancer, new Vector3(8.0f, -1.7f, 5.6f), 200f, DanceStates, new Vector2(20f, 34f)),
            // Crowd cheering at the catwalk.
            new CrowdSpot("Patron Cheer A", "Man_22", Kind.Man, new Vector3(2.4f, -1.7f, -8.6f), 10f, Hype, new Vector2(8f, 16f)),
            new CrowdSpot("Patron Cheer B", "casual01_f", Kind.Girl, new Vector3(4.9f, -1.7f, -8.4f), 345f, Cheer, new Vector2(8f, 16f)),
            new CrowdSpot("Patron Cheer C", "Man_33", Kind.Man, new Vector3(10.5f, -1.7f, 5.0f), 190f, Hype, new Vector2(8f, 16f)),
            new CrowdSpot("Patron Cheer D", "casual05_f", Kind.Girl, new Vector3(11.6f, -1.7f, -7.8f), 330f, Cheer, new Vector2(8f, 16f)),
            // Seated on the pit sofas facing the catwalk (sofa pivot + facing).
            new CrowdSpot("Patron Seated A", "Man_66", Kind.Man, new Vector3(3.73f, -1.69f, 2.86f), 180f, new[] { "sit_low_a" }, Vector2.zero, true),
            new CrowdSpot("Patron Seated B", "casual09_f", Kind.Girl, new Vector3(6.28f, -1.69f, 2.86f), 180f, new[] { "sit_low_b" }, Vector2.zero, true),
            new CrowdSpot("Patron Seated C", "casual19_f", Kind.Girl, new Vector3(3.73f, -1.71f, -6.2f), 0f, new[] { "sit_low_c" }, Vector2.zero, true),
            // Bar lounge: two pairs chatting.
            new CrowdSpot("Bar Chat A", "Man_44", Kind.Man, new Vector3(-11.6f, 0f, -6.8f), 0f, ManChat, new Vector2(10f, 18f)),
            new CrowdSpot("Bar Chat B", "business01_f", Kind.Girl, new Vector3(-11.6f, 0f, -5.7f), 180f, GirlChat, new Vector2(10f, 18f)),
            new CrowdSpot("Bar Chat C", "Man_55", Kind.Man, new Vector3(-11.5f, 0f, 2.6f), 70f, ManChat, new Vector2(10f, 18f)),
            new CrowdSpot("Bar Chat D", "casual08_f", Kind.Girl, new Vector3(-10.7f, 0f, 3.4f), 230f, GirlChat, new Vector2(10f, 18f)),
            // DJ behind the decks on the upper stage, facing the room.
            new CrowdSpot("DJ", "Man_11", Kind.Man, new Vector3(12.4f, 6.09f, -1.75f), 270f, new[] { "Hands_up", "man_claphands", "man_listen" }, new Vector2(6f, 12f)),
        };

        // ---------------------------------------------------------------- menus

        [MenuItem("Tools/OpenworldCrime/Club/1 Setup Imports, Materials And Controllers")]
        public static void SetupMenu() => Debug.Log(SetupAssets());

        [MenuItem("Tools/OpenworldCrime/Club/2 Build Club Interior Scene")]
        public static void BuildMenu() => Debug.Log(BuildInterior());

        [MenuItem("Tools/OpenworldCrime/Club/3 Bake Club Lighting (opens the club alone)")]
        public static void BakeMenu() => Debug.Log(StartBake());

        [MenuItem("Tools/OpenworldCrime/Club/4 Place City Entrance In Open Scene")]
        public static void PlaceEntranceMenu() => Debug.Log(PlaceEntrance());

        [MenuItem("Tools/OpenworldCrime/Club/3b Finish Bake (save the club, reopen the city)")]
        public static void FinishBakeMenu() => Debug.Log(FinishBake());

        [MenuItem("Tools/OpenworldCrime/Club/Update Crowd (keeps the bake)")]
        public static void UpdateCrowdMenu() => Debug.Log(UpdateCrowd());

        [MenuItem("Tools/OpenworldCrime/Club/Update Room Collision (keeps the bake)")]
        public static void UpdateCollisionMenu() => Debug.Log(UpdateCollision());

        // ---------------------------------------------------------------- 1. assets

        public static string SetupAssets()
        {
            var log = new StringBuilder("[ClubInteriorBuilder] assets\n");
            EnsureFolder(MaterialFolder);
            EnsureFolder(CharacterMaterialFolder);
            EnsureFolder(AnimationFolder);

            // Club model: our URP materials (remap) and lightmap UVs for the props.
            var importer = (ModelImporter)AssetImporter.GetAtPath(ClubModelPath);
            Material interior = LitMaterial("Club_Interior", Tex("Assets/Interior.png"), Color.white, 0.35f);
            Material djSet = LitMaterial("Club_DJSet", Tex("Assets/DJSet.png"), Color.white, 0.3f);
            var remaps = new Dictionary<string, Material>
            {
                { "Interior", interior },
                { "Interior Baked", interior },
                { "DJSet", djSet },
                { "DJSet Baked", djSet },
                { "Array", LitMaterial("Club_Walls", null, new Color(0.62f, 0.58f, 0.7f), 0.25f) },
                { "Tile", LitMaterial("Club_BottleShelves", Tex("Assets/Tile.png"), Color.white, 0.45f) },
                { "neon", NeonMaterial() },
                { "TVScreen", ScreenMaterial() },
                { "Glass", GlassMaterial() },
                { "Disco", DiscoBallMaterial() },
                { "DiscoAura", AdditiveMaterial("Club_DiscoAura", null, new Color(1f, 0.3f, 0.9f, 0.3f)) },
                { "SpotLight", AdditiveMaterial("Club_LightBeam_A", Tex("Assets/Spotlight.png"), new Color(1f, 0.2f, 0.85f, 0.55f)) },
            };
            AdditiveMaterial("Club_LightBeam_B", Tex("Assets/Spotlight.png"), new Color(0.2f, 0.8f, 1f, 0.55f));
            foreach (KeyValuePair<string, Material> pair in remaps)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.generateSecondaryUV = true;
            importer.SaveAndReimport();
            log.AppendLine($"  {ClubModelPath}: {remaps.Count} material remaps, lightmap UVs");

            // Dancers need Humanoid avatars for the Mixamo dance clips; the other characters stay Generic (same Biped
            // skeleton as the PopulationSystem people, so their clips play without retargeting).
            foreach (string model in HumanoidModels)
            {
                string path = FemaleModelPath(model);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                if (mi == null) { log.AppendLine("  missing " + path); continue; }
                if (mi.animationType != ModelImporterAnimationType.Human)
                {
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.SaveAndReimport();
                }
                Avatar avatar = null;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Avatar a) avatar = a;
                log.AppendLine($"  {model}: Humanoid ({(avatar != null && avatar.isHuman ? "valid" : "INVALID")})");
            }

            // Controllers.
            var danceClips = new List<AnimationClip>();
            for (int i = 0; i < DanceClipFiles.Length; i++) danceClips.Add(FbxClip(DanceClipFiles[i]));
            BuildController(DancerController, DanceStates, danceClips);
            BuildPatronController(GirlController, GirlClips, new[] { "girl_cheer", "girl_claphands", "girl_idle1", "girl_idle2", "girl_talk1", "girl_talk2", "girl_listen" }, "PedGirl");
            BuildPatronController(ManController, ManClips, new[] { "Hands_up", "man_claphands", "man_idle1", "man_idle2", "man_talk1", "man_talk2", "man_listen" }, "PedMan");
            log.AppendLine("  controllers: " + DancerController + ", " + GirlController + ", " + ManController);

            BuildMapIcon();
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static void BuildController(string path, string[] states, List<AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState s in sm.states) sm.RemoveState(s.state);
            for (int i = 0; i < states.Length; i++)
            {
                if (clips[i] == null) { Debug.LogWarning("[ClubInteriorBuilder] missing clip for state " + states[i]); continue; }
                AnimatorState state = sm.AddState(states[i], new Vector3(250f, 60f * i, 0f));
                state.motion = clips[i];
                state.writeDefaultValues = true;
                if (i == 0) sm.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
        }

        private static void BuildPatronController(string path, string folder, string[] clipNames, string pedPrefix)
        {
            var states = new List<string>();
            var clips = new List<AnimationClip>();
            foreach (string clipName in clipNames)
            {
                AnimationClip clip = null;
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                    foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                        if (o is AnimationClip c && c.name == clipName) clip = c;
                states.Add(clipName);
                clips.Add(clip);
            }
            // Seated poses generated by the pedestrian builder for this skeleton. The low poses (hips 0.46 m above the
            // feet) fit the club's lounge chairs: 0.36 m cushion + 0.1 m, the same rule the city's sitters use.
            foreach (string sit in new[] { "sit_low_a", "sit_low_b", "sit_low_c" })
            {
                states.Add(sit);
                clips.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{PedClips}/{pedPrefix}_{sit}.anim"));
            }
            BuildController(path, states.ToArray(), clips);
        }

        // ---------------------------------------------------------------- 2. scene

        public static string BuildInterior()
        {
            var log = new StringBuilder("[ClubInteriorBuilder] interior\n");
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (File.Exists(ScenePath)) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                else
                {
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                openedHere = true;
            }
            SceneManager.SetActiveScene(scene);
            foreach (GameObject old in scene.GetRootGameObjects()) Object.DestroyImmediate(old);

            // Scene lighting (applies while the club is the active scene at runtime): dark violet ambience, violet haze.
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.075f, 0.04f, 0.11f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.07f, 0.025f, 0.1f);
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 70f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.6f;
            Lightmapping.lightingSettings = GetLightingSettings();

            var root = new GameObject("ClubInterior");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = InteriorOrigin;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ClubModelPath);
            var club = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            club.transform.SetParent(root.transform, false);
            var movingFixtures = new List<Renderer>();
            Transform discoBall = null;
            Renderer baseRoom = null;
            int colliders = 0;
            foreach (Renderer r in club.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                r.shadowCastingMode = ShadowCastingMode.Off; // no realtime shadows indoors (the bake has its own)
                if (n.Contains("SpotLight"))
                {
                    movingFixtures.Add(r);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
                    SetReceiveGI(r, ReceiveGI.LightProbes);
                    continue;
                }
                if (n == "Disco")
                {
                    discoBall = r.transform;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
                    SetReceiveGI(r, ReceiveGI.LightProbes);
                    continue;
                }
                if (IsBottle(n))
                {
                    // Tiny props: batched, lit by probes (no lightmap space).
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    SetReceiveGI(r, ReceiveGI.LightProbes);
                    continue;
                }
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                SetReceiveGI(r, ReceiveGI.Lightmaps);
                if (n == "BaseRoom")
                {
                    baseRoom = r; // collision: BuildRoomCollision below (metre-scale copy)
                    colliders++;
                }
                else
                {
                    if (r is MeshRenderer mr) mr.scaleInLightmap = 0.5f;
                    if (!n.StartsWith("Pillow")) { r.gameObject.AddComponent<BoxCollider>(); colliders++; }
                }
            }
            // Alternate the beam colours (A on even fixtures, B on odd).
            Material beamA = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Club_LightBeam_A.mat");
            Material beamB = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Club_LightBeam_B.mat");
            var beamsA = new List<Renderer>();
            var beamsB = new List<Renderer>();
            for (int i = 0; i < movingFixtures.Count; i++)
            {
                Renderer r = movingFixtures[i];
                if (i % 2 == 1)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) if (mats[m] == beamA) mats[m] = beamB;
                    r.sharedMaterials = mats;
                    beamsB.Add(r);
                }
                else beamsA.Add(r);
            }
            log.AppendLine($"  Club.fbx at {InteriorOrigin}: {colliders} colliders, {movingFixtures.Count} moving fixtures");

            var gameplay = new GameObject("Gameplay").transform;
            gameplay.SetParent(root.transform, false);
            var interior = root.AddComponent<InteriorScene>();
            Transform spawn = NewPoint("Spawn Point", gameplay, SpawnPoint, 270f);
            var interiorSo = new SerializedObject(interior);
            interiorSo.FindProperty("spawnPoint").objectReferenceValue = spawn;
            interiorSo.FindProperty("displayName").stringValue = "Club";
            interiorSo.ApplyModifiedPropertiesWithoutUndo();

            var door = new GameObject("Exit Door");
            door.transform.SetParent(gameplay, false);
            door.transform.localPosition = ExitDoorCenter;
            door.layer = LayerMask.NameToLayer("Ignore Raycast");
            var doorBox = door.AddComponent<BoxCollider>();
            doorBox.isTrigger = true;
            doorBox.size = ExitDoorSize;
            door.AddComponent<InteriorExitDoor>();
            AddExitMarker(gameplay);
            if (baseRoom != null) BuildRoomCollision(gameplay, baseRoom.GetComponent<MeshFilter>());

            Physics.SyncTransforms();
            Transform lighting = BuildLighting(root.transform, out Light[] movingLights);
            AddLightProbes(root.transform, log);
            AddReflectionProbe(root.transform);
            AddGradeVolume(root.transform);

            // Light show rig.
            var showGo = new GameObject("Disco Light Show");
            showGo.transform.SetParent(lighting, false);
            var show = showGo.AddComponent<DiscoLightShow>();
            var showSo = new SerializedObject(show);
            SerializedProperty fixtures = showSo.FindProperty("fixtures");
            fixtures.arraySize = movingFixtures.Count + movingLights.Length;
            for (int i = 0; i < movingFixtures.Count; i++)
            {
                bool floor = movingFixtures[i].name.StartsWith("SpotLight");
                SetFixture(fixtures.GetArrayElementAtIndex(i), movingFixtures[i].transform, floor ? 35f : 22f, 0.08f + 0.03f * (i % 4), i * 0.173f, floor ? 0f : 8f);
            }
            for (int i = 0; i < movingLights.Length; i++)
                SetFixture(fixtures.GetArrayElementAtIndex(movingFixtures.Count + i), movingLights[i].transform, 30f, 0.07f + 0.02f * i, i * 0.33f, 14f);
            SetArray(showSo.FindProperty("colouredLights"), movingLights);
            SetArray(showSo.FindProperty("spinners"), discoBall != null ? new Object[] { discoBall } : new Object[0]);
            SerializedProperty mats2 = showSo.FindProperty("materials");
            mats2.arraySize = 4;
            SetCycled(mats2.GetArrayElementAtIndex(0), beamA, beamsA.ToArray(), true, false, 0f);
            SetCycled(mats2.GetArrayElementAtIndex(1), beamB, beamsB.ToArray(), true, false, 0.5f);
            SetCycled(mats2.GetArrayElementAtIndex(2), AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Club_Neon.mat"), baseRoom != null ? new[] { baseRoom } : new Renderer[0], false, true, 0f);
            SetCycled(mats2.GetArrayElementAtIndex(3), AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Club_DiscoAura.mat"), discoBall != null ? new[] { discoBall.GetComponent<Renderer>() } : new Renderer[0], true, false, 0.25f);
            showSo.ApplyModifiedPropertiesWithoutUndo();

            BuildCrowd(root.transform, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            if (previousActive.IsValid() && previousActive != scene) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            log.AppendLine($"  saved {ScenePath}; in Build Settings. Bake with menu 3.");
            return log.ToString();
        }

        /// <summary>Rebuilds only the characters (dynamic, probe-lit) in the saved scene, so the bake stays valid.</summary>
        public static string UpdateCrowd()
        {
            var log = new StringBuilder("[ClubInteriorBuilder] crowd updated (bake kept)\n");
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects()) if (r.GetComponent<InteriorScene>() != null) root = r;
            if (root == null) return "[ClubInteriorBuilder] club root not found";
            Transform old = root.transform.Find("Crowd");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            BuildCrowd(root.transform, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            return log.ToString();
        }

        private const string RoomCollisionMeshPath = ArtFolder + "/ClubRoomCollision.asset";

        /// <summary>Replaces a BaseRoom mesh collider with the metre-scale collision copy in the saved scene (bake untouched).</summary>
        public static string UpdateCollision()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects()) if (r.GetComponent<InteriorScene>() != null) root = r;
            if (root == null) return "[ClubInteriorBuilder] club root not found";
            Transform baseRoom = null;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == "BaseRoom") baseRoom = mf.transform;
            if (baseRoom == null) return "[ClubInteriorBuilder] BaseRoom not found";
            MeshCollider old = baseRoom.GetComponent<MeshCollider>();
            if (old != null) Object.DestroyImmediate(old);
            Transform gameplay = root.transform.Find("Gameplay");
            Transform oldCollision = gameplay.Find("Room Collision");
            if (oldCollision != null) Object.DestroyImmediate(oldCollision.gameObject);
            BuildRoomCollision(gameplay, baseRoom.GetComponent<MeshFilter>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            return "[ClubInteriorBuilder] room collision updated (bake kept)";
        }

        // The FBX room mesh is authored in centimetres under a 0.01 scale; PhysX warns about its (>500 unit) triangles.
        // The collider uses a copy baked into the club root's space in metres instead.
        private static void BuildRoomCollision(Transform gameplay, MeshFilter source)
        {
            Mesh sourceMesh = source.sharedMesh;
            Transform root = gameplay.parent;
            Vector3[] vertices = sourceMesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = root.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
            var triangles = new List<int>();
            for (int sub = 0; sub < sourceMesh.subMeshCount; sub++) triangles.AddRange(sourceMesh.GetTriangles(sub));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RoomCollisionMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "ClubRoomCollision" };
                AssetDatabase.CreateAsset(mesh, RoomCollisionMeshPath);
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            var go = new GameObject("Room Collision");
            go.transform.SetParent(gameplay, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
        }

        private static bool IsBottle(string n) => n.StartsWith("Beer") || n.StartsWith("Milk") || n.StartsWith("Whiskey") || n.StartsWith("WineRed") || n.StartsWith("Winexanh") || n.StartsWith("Vang");

        // Baked lights give the room its colour (zero runtime cost); three realtime moving heads sweep the dance pit.
        private static Transform BuildLighting(Transform root, out Light[] movingLights)
        {
            var lighting = new GameObject("Lighting").transform;
            lighting.SetParent(root, false);
            Color magenta = new Color(1f, 0.18f, 0.8f);
            Color violet = new Color(0.55f, 0.25f, 1f);
            Color cyan = new Color(0.15f, 0.75f, 1f);
            Color warm = new Color(1f, 0.45f, 0.55f);

            // Dance pit and catwalk.
            BakedPoint(lighting, "Pit Light 1", new Vector3(-4f, 2.5f, -3f), magenta, 9f, 2.2f);
            BakedPoint(lighting, "Pit Light 2", new Vector3(4f, 2.8f, 6f), violet, 9f, 2.2f);
            BakedPoint(lighting, "Pit Light 3", new Vector3(5f, 2.8f, -9f), cyan, 9f, 1.8f);
            BakedPoint(lighting, "Pit Light 4", new Vector3(14f, 2.5f, 4f), magenta, 9f, 2f);
            BakedPoint(lighting, "Pit Light 5", new Vector3(14f, 2.5f, -8f), violet, 9f, 2f);
            BakedSpot(lighting, "Catwalk Spot A", new Vector3(4f, 7.5f, -9f), new Vector3(5f, -0.9f, -1.5f), magenta, 16f, 50f, 9f);
            BakedSpot(lighting, "Catwalk Spot B", new Vector3(4f, 7.5f, 6f), new Vector3(6f, -0.9f, -1.5f), cyan, 16f, 50f, 8f);
            BakedSpot(lighting, "Pole Spot", new Vector3(2.2f, 8.5f, -1.7f), new Vector3(2.2f, -0.9f, -1.7f), new Color(1f, 0.55f, 0.9f), 12f, 35f, 10f);
            // Booth rows either side of the pit.
            BakedPoint(lighting, "Booths North 1", new Vector3(-2f, 2.6f, 12f), violet, 7f, 1.6f);
            BakedPoint(lighting, "Booths North 2", new Vector3(8f, 2.6f, 12f), magenta, 7f, 1.6f);
            BakedPoint(lighting, "Booths South 1", new Vector3(-2f, 2.6f, -14.5f), magenta, 7f, 1.6f);
            BakedPoint(lighting, "Booths South 2", new Vector3(8f, 2.6f, -14.5f), violet, 7f, 1.6f);
            // Bar lounge (warmer) and the neon corridor.
            BakedPoint(lighting, "Bar Light 1", new Vector3(-12f, 2.8f, -5f), warm, 7f, 2f);
            BakedPoint(lighting, "Bar Light 2", new Vector3(-12f, 2.8f, 3f), warm, 7f, 2f);
            BakedPoint(lighting, "Bar Light 3", new Vector3(-15.5f, 2.8f, 12f), violet, 7f, 1.6f);
            BakedPoint(lighting, "Corridor 1", new Vector3(-10f, 3f, 17.6f), magenta, 8f, 1.6f);
            BakedPoint(lighting, "Corridor 2", new Vector3(2f, 3f, 17.6f), violet, 8f, 1.6f);
            BakedPoint(lighting, "Corridor 3", new Vector3(14f, 3f, 17.6f), magenta, 8f, 1.6f);
            // Upper gallery and DJ.
            BakedPoint(lighting, "Gallery 1", new Vector3(-8f, 7f, -13f), violet, 9f, 1.6f);
            BakedPoint(lighting, "Gallery 2", new Vector3(-8f, 7f, 10f), violet, 9f, 1.6f);
            BakedSpot(lighting, "DJ Spot", new Vector3(10f, 9.5f, -1.7f), new Vector3(12.4f, 6.1f, -1.7f), cyan, 10f, 45f, 6f);

            // Realtime moving heads from the gallery ring, aimed into the pit (no shadows).
            var aims = new[] { new Vector3(0f, -1.7f, -2f), new Vector3(6f, -1.7f, 4f), new Vector3(8f, -1.7f, -7f) };
            var origins = new[] { new Vector3(-6f, 8.2f, -8f), new Vector3(8f, 8.2f, 8f), new Vector3(12f, 8.2f, -10f) };
            movingLights = new Light[aims.Length];
            for (int i = 0; i < aims.Length; i++)
            {
                var go = new GameObject("Moving Head " + (i + 1));
                go.transform.SetParent(lighting, false);
                go.transform.localPosition = origins[i];
                go.transform.rotation = Quaternion.LookRotation(aims[i] - origins[i]);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.shadows = LightShadows.None;
                l.range = 20f;
                l.spotAngle = 32f;
                l.innerSpotAngle = 18f;
                l.intensity = 14f;
                l.color = i == 0 ? magenta : i == 1 ? cyan : violet;
                movingLights[i] = l;
            }
            return lighting;
        }

        private static Light BakedPoint(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            return l;
        }

        private static void BakedSpot(Transform parent, string name, Vector3 localPosition, Vector3 localTarget, Color color, float range, float angle, float intensity)
        {
            Light l = BakedPoint(parent, name, localPosition, color, range, intensity);
            l.type = LightType.Spot;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            l.transform.rotation = Quaternion.LookRotation(localTarget - localPosition);
        }

        // Probes on a 3 m grid at foot and head height of every floor, kept only where they are inside the room and not
        // inside geometry, so the dancers and the player pick up the coloured baked light.
        private static void AddLightProbes(Transform root, StringBuilder log)
        {
            var positions = new List<Vector3>();
            float[] heightsAboveFloor = { 0.4f, 1.9f };
            for (float x = -17f; x <= 19.5f; x += 3f)
                for (float z = -16f; z <= 19f; z += 3f)
                {
                    // Every floor under this column: cast down repeatedly from the top.
                    Vector3 from = root.TransformPoint(new Vector3(x, 12f, z));
                    for (int guard = 0; guard < 4; guard++)
                    {
                        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) break;
                        if (hit.normal.y > 0.7f && Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            foreach (float h in heightsAboveFloor)
                            {
                                Vector3 p = hit.point + Vector3.up * h;
                                if (!Physics.CheckSphere(p, 0.15f, ~0, QueryTriggerInteraction.Ignore)) positions.Add(root.InverseTransformPoint(p));
                            }
                        }
                        from = hit.point + Vector3.down * 0.6f;
                    }
                }
            var go = new GameObject("Light Probes");
            go.transform.SetParent(root, false);
            go.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
            log.AppendLine($"  {positions.Count} light probes");
        }

        private static void AddReflectionProbe(Transform root)
        {
            var go = new GameObject("Reflection Probe");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(1.5f, 1.5f, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 128;
            probe.boxProjection = true;
            probe.size = new Vector3(40f, 13f, 38f);
            probe.center = new Vector3(0f, 3f, 1f);
        }

        // Club grade: bloom on the neon and beams, a violet vignette, a little more contrast and colour (LDR pipeline, so
        // no tonemapping). Global volume above the city's, only present while the club is loaded.
        private static void AddGradeVolume(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, GradeProfilePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.value = 0.8f;
                bloom.intensity.value = 1.1f;
                bloom.scatter.value = 0.65f;
                bloom.tint.value = new Color(1f, 0.85f, 1f);
                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.value = 0.32f;
                vignette.smoothness.value = 0.45f;
                vignette.color.value = new Color(0.08f, 0f, 0.1f);
                var color = profile.Add<ColorAdjustments>(true);
                color.contrast.value = 10f;
                color.saturation.value = 15f;
                color.postExposure.value = 0.15f;
                foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("Club Grade");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 90f;
            volume.sharedProfile = profile;
        }

        private static void AddExitMarker(Transform gameplay)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Exit Marker";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(gameplay, false);
            ring.transform.localPosition = new Vector3(18.7f, 0.03f, 17.6f);
            ring.transform.localScale = new Vector3(1.4f, 0.01f, 1.4f);
            var r = ring.GetComponent<MeshRenderer>();
            r.sharedMaterial = AdditiveMaterial("Club_Marker", null, new Color(0.75f, 0.2f, 1f, 0.6f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            SetReceiveGI(r, ReceiveGI.LightProbes);
        }

        // ---------------------------------------------------------------- crowd

        private static void BuildCrowd(Transform root, StringBuilder log)
        {
            var crowd = new GameObject("Crowd").transform;
            crowd.SetParent(root, false);
            var dancer = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DancerController);
            var girl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GirlController);
            var man = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ManController);
            int built = 0;
            foreach (CrowdSpot spot in Crowd)
            {
                string path = spot.Kind == Kind.Man ? $"{ManFolder}/{spot.Model}.FBX" : FemaleModelPath(spot.Model);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) { log.AppendLine("  missing model " + path); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source, root.gameObject.scene);
                go.name = spot.Name + " (" + spot.Model + ")";
                go.transform.SetParent(crowd, false);
                Vector3 position = spot.Position;
                Quaternion facing = Quaternion.Euler(0f, spot.Yaw, 0f);
                if (spot.Seated) position += facing * Vector3.forward * 0.05f; // hips just in front of the chair centre
                go.transform.localPosition = position;
                go.transform.localRotation = facing;

                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = spot.Kind == Kind.Dancer ? dancer : spot.Kind == Kind.Girl ? girl : man;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.updateMode = AnimatorUpdateMode.Normal;

                foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                    smr.receiveShadows = false;
                    smr.quality = SkinQuality.Bone2;
                    smr.updateWhenOffscreen = false;
                    smr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    smr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    Material[] mats = smr.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) mats[m] = CharacterMaterial(mats[m]);
                    smr.sharedMaterials = mats;
                }

                // Solid to the player (no rigidbody: static colliders, no physics cost).
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = 0.28f;
                capsule.height = spot.Seated ? 1.2f : 1.75f;
                capsule.center = new Vector3(0f, capsule.height * 0.5f, spot.Seated ? -0.15f : 0f);

                var member = go.AddComponent<ClubCrowdMember>();
                var so = new SerializedObject(member);
                so.FindProperty("animator").objectReferenceValue = animator;
                SerializedProperty states = so.FindProperty("states");
                states.arraySize = spot.States.Length;
                for (int i = 0; i < spot.States.Length; i++) states.GetArrayElementAtIndex(i).stringValue = spot.States[i];
                so.FindProperty("switchInterval").vector2Value = spot.Switch;
                so.FindProperty("speedRange").vector2Value = spot.Kind == Kind.Dancer ? new Vector2(0.9f, 1.1f) : new Vector2(0.85f, 1.1f);
                so.FindProperty("isDancer").boolValue = spot.Kind == Kind.Dancer;
                so.ApplyModifiedPropertiesWithoutUndo();

                if (!spot.Seated) ValidateStandingSpot(go.transform, spot, log);
                built++;
            }
            log.AppendLine($"  crowd: {built} characters");
        }

        // Warns when a standing character is not on a floor or overlaps the club's geometry.
        private static void ValidateStandingSpot(Transform character, CrowdSpot spot, StringBuilder log)
        {
            Physics.SyncTransforms();
            Vector3 feet = character.position;
            if (!Physics.Raycast(feet + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - feet.y) > 0.12f)
                log.AppendLine($"  WARNING {spot.Name}: floor {(hit.collider != null ? (hit.point.y - InteriorOrigin.y).ToString("F2") : "none")} vs {spot.Position.y:F2}");
            Collider own = character.GetComponent<Collider>();
            foreach (Collider c in Physics.OverlapCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.6f, 0.25f, ~0, QueryTriggerInteraction.Ignore))
                if (c != own && c.GetComponentInParent<ClubCrowdMember>() == null)
                    log.AppendLine($"  WARNING {spot.Name} overlaps {c.name}");
        }

        // ---------------------------------------------------------------- 3. bake

        /// <summary>
        /// Bakes the club's lightmaps, probes and reflection probe with only the club open (a multi-scene bake would bake the
        /// city too and replace its day/night sets). The city scene must be saved first; it is reopened afterwards.
        /// </summary>
        public static string StartBake()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "[ClubInteriorBuilder] Save or discard changes in '" + SceneManager.GetSceneAt(i).name + "' first.";
            cityPathBeforeBake = SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Lightmapping.lightingSettings = GetLightingSettings();
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            return Lightmapping.BakeAsync() ? "[ClubInteriorBuilder] club bake started" : "[ClubInteriorBuilder] bake could not start";
        }

        private static string cityPathBeforeBake;

        private static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorApplication.delayCall += () => Debug.Log(FinishBake());
        }

        public static string FinishBake()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene);
            string city = string.IsNullOrEmpty(cityPathBeforeBake) || cityPathBeforeBake == ScenePath ? CityScenePath : cityPathBeforeBake;
            if (File.Exists(city)) EditorSceneManager.OpenScene(city, OpenSceneMode.Single);
            return "[ClubInteriorBuilder] club bake saved; reopened " + city;
        }

        private static LightingSettings GetLightingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings != null) return settings;
            EnsureFolder(ArtFolder);
            settings = new LightingSettings { name = "ClubInterior" };
            AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = 6f;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 4;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.ao = true;
            settings.aoMaxDistance = 0.5f;
            settings.maxBounces = 2;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 32;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        // ---------------------------------------------------------------- 4. city entrance

        public static string PlaceEntrance()
        {
            Scene city = SceneManager.GetActiveScene();
            if (city.path != CityScenePath) return "[ClubInteriorBuilder] Open " + CityScenePath + " as the active scene first.";
            GameObject prefab = BuildEntrancePrefab();
            GameObject container = null;
            foreach (GameObject r in city.GetRootGameObjects()) if (r.name == "Club Entrances") container = r;
            if (container != null) Object.DestroyImmediate(container);
            container = new GameObject("Club Entrances");
            SceneManager.MoveGameObjectToScene(container, city);

            Vector3 position = EntrancePosition;
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) position.y = hit.point.y;
            var entrance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, city);
            entrance.transform.SetParent(container.transform, false);
            entrance.name = "Club Entrance - club_playnext";
            entrance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, EntranceYaw, 0f));
            EditorSceneManager.MarkSceneDirty(city);
            return $"[ClubInteriorBuilder] club entrance at {position} (yaw {EntranceYaw}); save the city scene";
        }

        private static GameObject BuildEntrancePrefab()
        {
            EnsureFolder(Path.GetDirectoryName(EntrancePrefabPath).Replace('\\', '/'));
            var root = new GameObject("ClubEntrance");
            root.layer = LayerMask.NameToLayer("Ignore Raycast"); // aim / bullet rays pass through the trigger
            try
            {
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 1f, 0f);
                trigger.size = new Vector3(2.2f, 2f, 2.2f);
                var entrance = root.AddComponent<ShopEntrance>();
                var exit = new GameObject("Exit Point").transform;
                exit.SetParent(root.transform, false);
                exit.localPosition = new Vector3(0f, 0.05f, 2.4f);
                var so = new SerializedObject(entrance);
                so.FindProperty("entranceId").stringValue = "club_playnext";
                so.FindProperty("interiorSceneName").stringValue = SceneName;
                so.FindProperty("prompt").stringValue = "Enter Club";
                so.FindProperty("loadingCaption").stringValue = "CLUB";
                so.FindProperty("exitPoint").objectReferenceValue = exit;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Purple GTA marker: soft column and a bobbing ring.
                var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                column.name = "Marker Column";
                Object.DestroyImmediate(column.GetComponent<Collider>());
                column.transform.SetParent(root.transform, false);
                column.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                column.transform.localScale = new Vector3(1.1f, 0.75f, 1.1f);
                var columnRenderer = column.GetComponent<MeshRenderer>();
                columnRenderer.sharedMaterial = AdditiveMaterial("Club_EntranceColumn", null, new Color(0.7f, 0.15f, 1f, 0.35f));
                columnRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Marker Ring";
                Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.transform.SetParent(root.transform, false);
                ring.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                ring.transform.localScale = new Vector3(0.45f, 0.03f, 0.45f);
                var ringRenderer = ring.GetComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = AdditiveMaterial("Club_EntranceRing", null, new Color(1f, 0.3f, 0.95f, 0.9f));
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ring.AddComponent<MarkerBob>();

                AddNeonSign(root.transform);

                var blip = root.AddComponent<MapBlip>();
                var blipSo = new SerializedObject(blip);
                blipSo.FindProperty("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(MapIconPath);
                blipSo.FindProperty("backgroundColour").colorValue = new Color(0.62f, 0.1f, 0.85f, 0.95f);
                blipSo.FindProperty("size").floatValue = 28f;
                blipSo.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, EntrancePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // "CLUB" neon on the facade above the door: a glowing magenta panel behind bright text (both unlit, so the city's
        // bloom picks them up at night without a light or a rebake of the city).
        private static void AddNeonSign(Transform entrance)
        {
            var sign = new GameObject("Club Neon Sign");
            sign.transform.SetParent(entrance, false);
            sign.transform.localPosition = new Vector3(0f, 4.6f, -FacadeDistance);
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Glow";
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.transform.SetParent(sign.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            glow.transform.localScale = new Vector3(4.4f, 1.3f, 1f);
            var glowRenderer = glow.GetComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = AdditiveMaterial("Club_SignGlow", null, new Color(0.85f, 0.1f, 1f, 0.35f));
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(sign.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = textGo.AddComponent<TextMeshPro>();
            text.text = "CLUB";
            text.fontSize = 10f;
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.characterSpacing = 12f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.55f, 1f);
            text.rectTransform.sizeDelta = new Vector2(4.4f, 1.3f);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            // The glow quad faces the street (its visible side points away from the facade).
            glow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // 64 px cocktail-glass icon for the map blip (white on transparent; the blip draws the purple background).
        private static void BuildMapIcon()
        {
            if (File.Exists(MapIconPath)) return;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool bowl = y >= 30 && y <= 50 && Mathf.Abs(x - 32) <= (y - 30) * 0.9f + 1f;
                    bool rim = y >= 49 && y <= 51 && Mathf.Abs(x - 32) <= 19;
                    bool stem = y >= 14 && y < 30 && Mathf.Abs(x - 32) <= 1;
                    bool foot = y >= 11 && y < 14 && Mathf.Abs(x - 32) <= 10;
                    bool olive = (new Vector2(x, y) - new Vector2(38f, 42f)).sqrMagnitude <= 9f;
                    tex.SetPixel(x, y, (bowl || rim || stem || foot) && !olive ? Color.white : clear);
                }
            File.WriteAllBytes(MapIconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(MapIconPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(MapIconPath);
            ti.textureType = TextureImporterType.Sprite;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        // ---------------------------------------------------------------- materials

        private static Material LitMaterial(string name, Texture2D texture, Color color, float smoothness)
        {
            Material m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material NeonMaterial()
        {
            Texture2D neon = Tex("Assets/neon.png");
            Material m = LitMaterial("Club_Neon", neon, Color.white, 0.6f);
            m.SetTexture("_EmissionMap", neon);
            m.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material ScreenMaterial()
        {
            Material m = LitMaterial("Club_Screen", null, new Color(0.05f, 0.03f, 0.08f), 0.9f);
            m.SetColor("_EmissionColor", new Color(0.35f, 0.1f, 0.55f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GlassMaterial()
        {
            Material m = LitMaterial("Club_Glass", null, new Color(0.08f, 0.1f, 0.4f, 0.45f), 0.95f);
            MakeTransparent(m, false);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material DiscoBallMaterial()
        {
            Material m = LitMaterial("Club_DiscoBall", null, new Color(0.85f, 0.75f, 0.95f), 0.85f);
            m.SetFloat("_Metallic", 0.9f);
            m.SetColor("_EmissionColor", new Color(0.3f, 0.08f, 0.35f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Unlit additive (beams, glows, markers): no depth write, both faces, no shadows.</summary>
        internal static Material AdditiveMaterial(string name, Texture2D texture, Color color)
        {
            Material m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            MakeTransparent(m, true);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        /// <summary>URP copies of the character pack's Built-in (Standard) materials; the pack's own materials stay untouched.</summary>
        internal static Material CharacterMaterial(Material source)
        {
            if (source == null || source.shader.name.StartsWith("Universal Render Pipeline")) return source;
            string path = $"{CharacterMaterialFolder}/{source.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { name = source.name };
            m.SetTexture("_BaseMap", source.mainTexture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.25f);
            bool hair = source.name.Contains("hair") || source.IsKeywordEnabled("_ALPHATEST_ON");
            if (hair)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material GetOrCreate(string name, string shader)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader)) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader.name != shader) m.shader = Shader.Find(shader);
            return m;
        }

        // ---------------------------------------------------------------- helpers

        private static void SetReceiveGI(Renderer r, ReceiveGI gi)
        {
            if (r is MeshRenderer mr) mr.receiveGI = gi;
        }

        internal static string FemaleModelPath(string model) => $"{FemaleFolder}/{model}/{model}.FBX";

        private static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        private static AnimationClip FbxClip(string path)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            return null;
        }

        private static void SetFixture(SerializedProperty p, Transform target, float amplitude, float frequency, float phase, float tilt)
        {
            p.FindPropertyRelative("target").objectReferenceValue = target;
            p.FindPropertyRelative("amplitude").floatValue = amplitude;
            p.FindPropertyRelative("frequency").floatValue = frequency;
            p.FindPropertyRelative("phase").floatValue = phase;
            p.FindPropertyRelative("tiltAmplitude").floatValue = tilt;
        }

        private static void SetCycled(SerializedProperty p, Material source, Renderer[] renderers, bool cycle, bool pulse, float offset)
        {
            p.FindPropertyRelative("source").objectReferenceValue = source;
            SetArray(p.FindPropertyRelative("renderers"), renderers);
            p.FindPropertyRelative("cycleColour").boolValue = cycle;
            p.FindPropertyRelative("pulseEmission").boolValue = pulse;
            p.FindPropertyRelative("paletteOffset").floatValue = offset;
        }

        private static void SetArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static Transform NewPoint(string name, Transform parent, Vector3 localPosition, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
