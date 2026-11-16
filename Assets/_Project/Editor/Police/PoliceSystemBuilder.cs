using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Invector.vCamera;
using Invector.vCharacterController;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using OpenworldCrime.Police;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the police system and wires it into the open gameplay scene. Safe to run again: generated assets are updated in place
    /// (GUIDs kept), scene objects are found by name and re-wired.
    /// - Data: PoliceSettings (created once, never overwritten), the officer Animator Controller (locomotion blend tree of the street
    ///   officers' clips + an upper-body pistol aim / shot layer of Invector's shooter clips), star / flash textures, tracer material.
    /// - Prefabs: PoliceOfficer_2 / _3, variants of the damageable street officer variants (Ped_Police_2 / _3) with a NavMeshAgent,
    ///   root capsule, pistol and <see cref="PoliceOfficerUnit"/>; PoliceCruiser, a variant of the police traffic car with the iTS
    ///   traffic AI and traffic adapter removed and a <see cref="PoliceCarUnit"/> (driven by Hamza's ChaseDriver at runtime).
    ///   None of the source prefabs (Hamza's or ours) are modified.
    /// - Scene: WantedManager / CrimeReporter / PoliceDispatcher / BustedSequence under ///Manager///, a "Police" root with the search
    ///   area blip and the release points, and the Wanted HUD under Gameplay UI/HUD.
    /// </summary>
    public static class PoliceSystemBuilder
    {
        private const string DataFolder = "Assets/_Project/Data/Police";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Police";
        private const string AnimationFolder = "Assets/_Project/Animations/Police";
        private const string ArtFolder = "Assets/_Project/Art/Police";

        private const string SettingsPath = DataFolder + "/PoliceSettings.asset";
        private const string ControllerPath = AnimationFolder + "/PoliceOfficerCombat.controller";
        private const string StarPath = ArtFolder + "/WantedStar.png";
        private const string StarOutlinePath = ArtFolder + "/WantedStarOutline.png";
        private const string FlashTexturePath = ArtFolder + "/MuzzleFlash.png";
        private const string FlashMaterialPath = ArtFolder + "/PoliceMuzzleFlash.mat";
        private const string TracerMaterialPath = ArtFolder + "/PoliceTracer.mat";
        private const string CruiserPath = PrefabFolder + "/PoliceCruiser.prefab";

        private const string StreetOfficerController = "Assets/Game/Police/PoliceOfficer.controller";
        private const string UpperBodyMask = "Assets/Invector-3rdPersonController/Basic Locomotion/Animator/AvatarMasks/UpperBody.mask";
        private const string PoseClips = "Assets/Invector-3rdPersonController/Shooter/3DModels/Animations/Shooter_UpperBodyPoses.fbx";
        private const string ShotClips = "Assets/Invector-3rdPersonController/Shooter/3DModels/Animations/Shooter_Shot&Reload.fbx";
        private const string HandgunPrefab = "Assets/Invector-3rdPersonController/Shooter/3DModels/Weapons/vWeaponsNew/prefabs/vHandgun.prefab";
        private const string HandgunSound = "Assets/Invector-3rdPersonController/Shooter/Audio/Weapons/Handgun@Fire.wav";
        private const string PoliceTrafficCar = "Assets/_Project/Prefabs/Vehicles/Traffic/Traffic_Police.prefab";
        private static readonly string[] StreetOfficerVariants =
        {
            "Assets/_Project/Prefabs/Pedestrians/Ped_Police_2.prefab",
            "Assets/_Project/Prefabs/Pedestrians/Ped_Police_3.prefab",
        };

        // Street officers' clips play as if they move at these speeds (PoliceSetup's measurements).
        private const float WalkClipSpeed = 1.556f;
        private const float RunClipSpeed = 5.668f;

        private static readonly Color PoliceRed = new Color(0.9f, 0.12f, 0.12f, 0.95f);
        private static readonly Color PoliceBlue = new Color(0.12f, 0.35f, 1f, 0.95f);

        [MenuItem("Tools/OpenworldCrime/Police/Build Police System")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("Police system:\n");
            EnsureFolders();
            PoliceSettings settings = EnsureSettings(log);
            Sprite star = EnsureStarSprite(StarPath, false);
            Sprite starOutline = EnsureStarSprite(StarOutlinePath, true);
            Material flash = EnsureFlashMaterial();
            Material tracer = EnsureTracerMaterial();
            AnimatorController controller = BuildController(log);

            var officers = new List<PoliceOfficerUnit>();
            foreach (string source in StreetOfficerVariants)
            {
                PoliceOfficerUnit officer = BuildOfficer(source, controller, flash, tracer, log);
                if (officer != null) officers.Add(officer);
            }
            PoliceCarUnit cruiser = BuildCruiser(flash, tracer, log);

            WireScene(settings, officers.ToArray(), cruiser, star, starOutline, log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Assets
        // ---------------------------------------------------------------------------------------------------------------

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { DataFolder, PrefabFolder, AnimationFolder, ArtFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        private static PoliceSettings EnsureSettings(StringBuilder log)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PoliceSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PoliceSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            log.AppendLine("- created " + SettingsPath);
            return settings;
        }

        // Filled: white star with a dark rim. Outline: the rim only, white (tinted dim on the HUD), hollow inside.
        private static Sprite EnsureStarSprite(string path, bool outline)
        {
            if (!File.Exists(path))
            {
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                Vector2[] outer = StarPolygon(size * 0.5f, size * 0.47f, size * 0.2f);
                Vector2[] inner = StarPolygon(size * 0.5f, size * 0.39f, size * 0.165f);
                const int samples = 4;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int outerHits = 0, innerHits = 0;
                        for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                            if (Inside(outer, p)) outerHits++;
                            if (Inside(inner, p)) innerHits++;
                        }
                        float outerA = outerHits / (float)(samples * samples);
                        float innerA = innerHits / (float)(samples * samples);
                        // White star with a dark rim: readable on bright sky and dark streets alike.
                        if (outline)
                        {
                            // Dark rim and a faint dark fill: an empty slot that reads on bright sky and dark streets alike.
                            float rim = Mathf.Clamp01(outerA - innerA);
                            pixels[y * size + x] = new Color(0f, 0f, 0f, rim * 0.85f + innerA * 0.3f);
                            continue;
                        }
                        float grey = Mathf.Lerp(0.08f, 1f, outerA > 0f ? innerA / outerA : 0f);
                        pixels[y * size + x] = new Color(grey, grey, grey, outerA);
                    }
                }
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Vector2[] StarPolygon(float centre, float outerRadius, float innerRadius)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                points[i] = new Vector2(centre + Mathf.Cos(angle) * radius, centre + Mathf.Sin(angle) * radius - 3f);
            }
            return points;
        }

        private static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static Material EnsureFlashMaterial()
        {
            if (!File.Exists(FlashTexturePath))
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    pixels[y * size + x] = new Color(1f, Mathf.Lerp(0.55f, 1f, a), Mathf.Lerp(0.2f, 0.9f, a * a), a * a);
                }
                texture.SetPixels(pixels);
                File.WriteAllBytes(FlashTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(FlashTexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(FlashTexturePath);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            // Legacy unlit alpha-blended shader: renders in URP's transparent queue, no lighting cost.
            Material material = LoadOrCreateMaterial(FlashMaterialPath, "Sprites/Default");
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FlashTexturePath);
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureTracerMaterial()
        {
            Material material = LoadOrCreateMaterial(TracerMaterialPath, "Sprites/Default");
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AnimatorController BuildController(StringBuilder log)
        {
            var street = AssetDatabase.LoadAssetAtPath<AnimatorController>(StreetOfficerController);
            AnimationClip idle = null, walk = null, run = null;
            foreach (ChildAnimatorState child in street.layers[0].stateMachine.states)
            {
                var clip = child.state.motion as AnimationClip;
                if (child.state.name == "idle1") idle = clip;
                else if (child.state.name == "walk") walk = clip;
                else if (child.state.name == "run") run = clip;
            }
            AnimationClip aim = LoadClip(PoseClips, "Aiming@Pistol");
            AnimationClip shot = LoadClip(ShotClips, "Shot_Pistol");
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null) AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            // Rebuilt from scratch each run (only generated content lives here).
            while (controller.layers.Length > 0) controller.RemoveLayer(0);
            foreach (AnimatorControllerParameter p in controller.parameters.ToArray()) controller.RemoveParameter(p);
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub is BlendTree || sub is AnimatorState || sub is AnimatorStateMachine || sub is AnimatorStateTransition) Object.DestroyImmediate(sub, true);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            controller.AddLayer("Base");
            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkClipSpeed);
            tree.AddChild(run, RunClipSpeed);
            baseMachine.defaultState = locomotion;

            var armsMachine = new AnimatorStateMachine { name = "Arms", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(armsMachine, controller);
            var armsLayer = new AnimatorControllerLayer
            {
                name = "Arms",
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 0f,
                iKPass = true, // PoliceOfficerUnit.OnAnimatorIK points the pistol at the target
                stateMachine = armsMachine
            };
            controller.AddLayer(armsLayer);
            AnimatorState aimState = armsMachine.AddState("Aim");
            aimState.motion = aim;
            AnimatorState shootState = armsMachine.AddState("Shoot");
            shootState.motion = shot;
            AnimatorStateTransition back = shootState.AddTransition(aimState);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.08f;
            armsMachine.defaultState = aimState;

            EditorUtility.SetDirty(controller);
            log.AppendLine("- animator " + ControllerPath + " (idle/walk/run blend + pistol aim/shot upper body)");
            return controller;
        }

        private static AnimationClip LoadClip(string path, string clipName)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == clipName);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Officer prefabs
        // ---------------------------------------------------------------------------------------------------------------

        private static PoliceOfficerUnit BuildOfficer(string sourcePath, AnimatorController controller, Material flash, Material tracer, StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                log.AppendLine("- MISSING " + sourcePath);
                return null;
            }
            string number = source.name.Substring(source.name.LastIndexOf('_') + 1);
            string path = PrefabFolder + "/PoliceOfficer_" + number + ".prefab";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "PoliceOfficer_" + number;
                // The bridge ties the person to the street crowd (walker); a police unit drives its own body.
                var bridge = instance.GetComponent<PedestrianDamageBridge>();
                if (bridge != null) Object.DestroyImmediate(bridge);

                var animator = instance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var capsule = GetOrAdd<CapsuleCollider>(instance);
                capsule.height = 1.8f;
                capsule.radius = 0.3f;
                capsule.center = new Vector3(0f, 0.9f, 0f);
                var body = GetOrAdd<Rigidbody>(instance);
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;

                var agent = GetOrAdd<NavMeshAgent>(instance);
                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.baseOffset = 0f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                agent.avoidancePriority = 40;
                agent.enabled = false;

                var damage = instance.GetComponent<PedestrianDamage>();
                var damageSo = new SerializedObject(damage);
                damageSo.FindProperty("aliveCollider").objectReferenceValue = capsule;
                damageSo.ApplyModifiedPropertiesWithoutUndo();

                PoliceSidearm sidearm = BuildPistol(instance, animator, flash, tracer, out GameObject weaponModel);

                var blip = GetOrAdd<MapBlip>(instance);
                SetBlip(blip, 13f, PoliceRed, PoliceBlue, 0f);
                blip.enabled = false;

                var unit = GetOrAdd<PoliceOfficerUnit>(instance);
                var so = new SerializedObject(unit);
                so.FindProperty("agent").objectReferenceValue = agent;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("damage").objectReferenceValue = damage;
                so.FindProperty("sidearm").objectReferenceValue = sidearm;
                so.FindProperty("weaponModel").objectReferenceValue = weaponModel;
                so.FindProperty("mapBlip").objectReferenceValue = blip;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                log.AppendLine("- officer " + path + " (variant of " + source.name + ")");
                return saved.GetComponent<PoliceOfficerUnit>();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // The pistol is placed from the aim pose itself: grip in the palm, barrel along the forearm, so it lines up whatever the rig.
        private static PoliceSidearm BuildPistol(GameObject officer, Animator animator, Material flash, Material tracer, out GameObject weaponModel)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            Transform old = hand.Find("Police Pistol");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            AnimationClip aim = LoadClip(PoseClips, "Aiming@Pistol");
            var pose = new Dictionary<Transform, (Vector3, Quaternion)>();
            foreach (Transform t in officer.GetComponentsInChildren<Transform>()) pose[t] = (t.localPosition, t.localRotation);
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(officer, aim, 0f);
            AnimationMode.EndSampling();

            Vector3 palm = middle != null ? Vector3.Lerp(hand.position, middle.position, 0.55f) : hand.position;
            Vector3 barrel = (hand.position - forearm.position).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, barrel).normalized;
            Quaternion gunRotation = Quaternion.LookRotation(barrel, up);
            // Handgun mesh: centred, barrel along +Z, grip at the back bottom.
            Vector3 gripLocal = new Vector3(0f, -0.045f, -0.065f);
            Vector3 gunPosition = palm - gunRotation * gripLocal;
            Vector3 localPosition = hand.InverseTransformPoint(gunPosition);
            Quaternion localRotation = Quaternion.Inverse(hand.rotation) * gunRotation;

            AnimationMode.StopAnimationMode();
            foreach (var entry in pose) entry.Key.SetLocalPositionAndRotation(entry.Value.Item1, entry.Value.Item2);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HandgunPrefab);
            weaponModel = new GameObject("Police Pistol");
            weaponModel.transform.SetParent(hand, false);
            weaponModel.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            weaponModel.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = weaponModel.AddComponent<MeshRenderer>();
            // The handgun's second material is a bright orange accent that reads as a toy at a distance: all gunmetal.
            Material gunmetal = source.GetComponent<MeshRenderer>().sharedMaterials[0];
            var materials = new Material[source.GetComponent<MeshRenderer>().sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = gunmetal;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            weaponModel.SetActive(false);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(weaponModel.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.035f, 0.14f);
            return BuildGun(weaponModel, muzzle, flash, tracer, 0.18f);
        }

        // Muzzle flash (two crossed unlit quads), tracer line and shot sound on a gun object.
        private static PoliceSidearm BuildGun(GameObject gunObject, Transform muzzle, Material flash, Material tracer, float flashSize)
        {
            var flashRoot = new GameObject("Muzzle Flash");
            flashRoot.transform.SetParent(muzzle, false);
            for (int i = 0; i < 2; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = "Flash " + i;
                quad.transform.SetParent(flashRoot.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, flashSize * 0.4f);
                quad.transform.localRotation = Quaternion.Euler(0f, 90f, i * 90f);
                quad.transform.localScale = new Vector3(flashSize * 2f, flashSize, 1f);
                var quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = flash;
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            }
            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(front.GetComponent<Collider>());
            front.name = "Flash Front";
            front.transform.SetParent(flashRoot.transform, false);
            front.transform.localScale = Vector3.one * flashSize * 1.2f;
            var frontRenderer = front.GetComponent<MeshRenderer>();
            frontRenderer.sharedMaterial = flash;
            frontRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashRoot.SetActive(false);

            var tracerObject = new GameObject("Tracer");
            tracerObject.transform.SetParent(gunObject.transform, false);
            var line = tracerObject.AddComponent<LineRenderer>();
            line.sharedMaterial = tracer;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.015f;
            line.startColor = new Color(1f, 0.9f, 0.6f, 0.9f);
            line.endColor = new Color(1f, 0.8f, 0.4f, 0.15f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            var audio = gunObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 4f;
            audio.maxDistance = 90f;
            audio.volume = 0.8f;

            var sidearm = gunObject.AddComponent<PoliceSidearm>();
            var so = new SerializedObject(sidearm);
            so.FindProperty("muzzle").objectReferenceValue = muzzle;
            so.FindProperty("muzzleFlash").objectReferenceValue = flashRoot;
            so.FindProperty("tracer").objectReferenceValue = line;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            SerializedProperty clips = so.FindProperty("shotClips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(HandgunSound);
            so.ApplyModifiedPropertiesWithoutUndo();
            return sidearm;
        }

        private static void SetBlip(MapBlip blip, float size, Color background, Color flashColour, float worldRadius)
        {
            var so = new SerializedObject(blip);
            so.FindProperty("icon").objectReferenceValue = null;
            so.FindProperty("backgroundColour").colorValue = background;
            so.FindProperty("size").floatValue = size;
            so.FindProperty("tracksMovement").boolValue = true;
            so.FindProperty("flashes").boolValue = true;
            so.FindProperty("flashColour").colorValue = flashColour;
            so.FindProperty("flashInterval").floatValue = 0.35f;
            so.FindProperty("worldRadius").floatValue = worldRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Pursuit car prefab
        // ---------------------------------------------------------------------------------------------------------------

        private static PoliceCarUnit BuildCruiser(Material flash, Material tracer, StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceTrafficCar);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "PoliceCruiser";
                // Out of the iTS traffic for good: ChaseDriver drives the TSSimpleCar physics directly (as the panic flee does).
                RemoveComponent<TrafficVehicleAdapter>(instance);
                RemoveComponent<TSTrafficAI>(instance);
                Component navigation = instance.GetComponent("TSNavigation"); // obsolete iTS type, removed by name
                if (navigation != null) Object.DestroyImmediate(navigation);
                Transform driverZones = instance.transform.Find("BulletHitbox/DriverHitZones");
                if (driverZones != null) driverZones.gameObject.SetActive(false);

                // The root already has iTS' engine AudioSource; the siren gets its own child above the light bar.
                Transform sirenChild = instance.transform.Find("Siren");
                if (sirenChild == null)
                {
                    sirenChild = new GameObject("Siren").transform;
                    sirenChild.SetParent(instance.transform, false);
                    sirenChild.localPosition = new Vector3(0f, 1.8f, 0f);
                }
                var siren = GetOrAdd<AudioSource>(sirenChild.gameObject);
                siren.playOnAwake = false;
                siren.loop = true;
                siren.spatialBlend = 1f;
                siren.rolloffMode = AudioRolloffMode.Linear;
                siren.minDistance = 10f;
                siren.maxDistance = 160f;
                siren.dopplerLevel = 0.4f;
                siren.priority = 100;

                Transform gunPoint = instance.transform.Find("Gun Point");
                if (gunPoint != null) Object.DestroyImmediate(gunPoint.gameObject);
                gunPoint = new GameObject("Gun Point").transform;
                gunPoint.SetParent(instance.transform, false);
                gunPoint.localPosition = new Vector3(0.95f, 1.35f, 0.3f);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(gunPoint, false);
                PoliceSidearm gun = BuildGun(gunPoint.gameObject, muzzle, flash, tracer, 0.22f);

                var blip = GetOrAdd<MapBlip>(instance);
                SetBlip(blip, 17f, PoliceRed, PoliceBlue, 0f);
                blip.enabled = false;

                var unit = GetOrAdd<PoliceCarUnit>(instance);
                var so = new SerializedObject(unit);
                so.FindProperty("body").objectReferenceValue = instance.GetComponent<Rigidbody>();
                so.FindProperty("simpleCar").objectReferenceValue = instance.GetComponent<TSSimpleCar>();
                so.FindProperty("health").objectReferenceValue = instance.GetComponent<VehicleHealth>();
                so.FindProperty("gun").objectReferenceValue = gun;
                so.FindProperty("lightBar").objectReferenceValue = instance.GetComponent<PoliceLightBar>();
                so.FindProperty("mapBlip").objectReferenceValue = blip;
                so.FindProperty("siren").objectReferenceValue = siren;
                Transform driver = instance.transform.Find("DriverSeat/DriverVisual");
                SerializedProperty crew = so.FindProperty("seatedCrew");
                crew.arraySize = driver != null ? 1 : 0;
                if (driver != null) crew.GetArrayElementAtIndex(0).objectReferenceValue = driver.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, CruiserPath);
                log.AppendLine("- pursuit car " + CruiserPath + " (variant of Traffic_Police without iTS AI / adapter)");
                return saved.GetComponent<PoliceCarUnit>();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void RemoveComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            if (component != null) Object.DestroyImmediate(component);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Scene
        // ---------------------------------------------------------------------------------------------------------------

        private static void WireScene(PoliceSettings settings, PoliceOfficerUnit[] officers, PoliceCarUnit cruiser, Sprite star, Sprite starOutline, StringBuilder log)
        {
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managerRoot = FindRoot(scene, "///Manager///");
            GameObject player = Object.FindFirstObjectByType<OpenworldCrime.Player.PlayerManager>(FindObjectsInactive.Include)?.gameObject;
            var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            GameObject ui = FindRoot(scene, "Gameplay UI");
            if (managerRoot == null || player == null || spawner == null || ui == null)
            {
                log.AppendLine("- scene not wired: needs ///Manager///, the player, the PedestrianSpawner and Gameplay UI");
                return;
            }
            var playerManager = player.GetComponent<OpenworldCrime.Player.PlayerManager>();
            var vehicleInteraction = player.GetComponentInChildren<PlayerVehicleInteraction>(true);
            var combat = player.GetComponentInChildren<PlayerCombatController>(true);
            var shooter = player.GetComponentInChildren<vShooterMeleeInput>(true);
            var gamePlayer = player.GetComponentInChildren<InvectorGamePlayer>(true);
            var camera = Object.FindFirstObjectByType<vThirdPersonCamera>(FindObjectsInactive.Include);
            var fader = ui.GetComponentInChildren<ScreenFader>(true);

            // World side: search area and release points (not managers).
            GameObject policeRoot = FindRoot(scene, "Police") ?? new GameObject("Police");
            Transform searchArea = Child(policeRoot.transform, "Search Area");
            var areaBlip = GetOrAdd<MapBlip>(searchArea.gameObject);
            SetBlip(areaBlip, 26f, new Color(0.95f, 0.15f, 0.15f, 0.28f), new Color(0.15f, 0.35f, 1f, 0.28f), 80f);
            var areaSo = new SerializedObject(areaBlip);
            areaSo.FindProperty("flashInterval").floatValue = 0.6f;
            areaSo.ApplyModifiedPropertiesWithoutUndo();
            searchArea.gameObject.SetActive(false);
            Transform releaseRoot = Child(policeRoot.transform, "Release Points");
            var releasePoints = new List<Transform>();
            foreach (var entrance in Object.FindObjectsByType<OpenworldCrime.Interiors.ShopEntrance>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Transform exit = entrance.ExitPoint;
                string pointName = "Release - " + entrance.EntranceId;
                Transform point = releaseRoot.Find(pointName);
                if (point == null)
                {
                    point = Child(releaseRoot, pointName);
                    // A few steps out on the pavement from the shop door, facing the street.
                    Vector3 position = exit.position + exit.forward * 4f;
                    if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas)) position = hit.position;
                    point.SetPositionAndRotation(position, exit.rotation);
                }
                releasePoints.Add(point);
            }

            // Managers.
            var wanted = GetOrAdd<WantedManager>(Child(managerRoot.transform, "WantedManager").gameObject);
            var reporter = GetOrAdd<CrimeReporter>(Child(managerRoot.transform, "CrimeReporter").gameObject);
            var dispatcher = GetOrAdd<PoliceDispatcher>(Child(managerRoot.transform, "PoliceDispatcher").gameObject);
            var busted = GetOrAdd<BustedSequence>(Child(managerRoot.transform, "BustedSequence").gameObject);

            Assign(wanted, ("settings", settings), ("dispatcher", dispatcher), ("playerManager", playerManager),
                ("vehicleInteraction", vehicleInteraction), ("playerBody", player.GetComponent<Rigidbody>()), ("searchAreaBlip", areaBlip));
            Assign(reporter, ("settings", settings), ("player", player.transform), ("combat", combat), ("shooterInput", shooter),
                ("vehicleInteraction", vehicleInteraction), ("pedestrians", spawner));
            Assign(dispatcher, ("settings", settings), ("pedestrians", spawner), ("carPrefab", cruiser));
            AssignArray(dispatcher, "officerPrefabs", officers);
            Assign(busted, ("settings", settings), ("dispatcher", dispatcher), ("playerManager", playerManager), ("gamePlayer", gamePlayer),
                ("vehicleInteraction", vehicleInteraction), ("thirdPersonCamera", camera), ("fader", fader));
            AssignArray(busted, "releasePoints", releasePoints.ToArray());

            BuildHud(ui.transform.Find("HUD"), star, starOutline);
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("- scene: managers under ///Manager///, 'Police' root (" + releasePoints.Count + " release points), Wanted HUD");
        }

        private static void BuildHud(Transform hud, Sprite star, Sprite starOutline)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite panel = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Transform old = hud.Find("Wanted");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            RectTransform root = Rect("Wanted", hud, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Stars, top right under the cash (GTA).
            const float starSize = 50f, gap = 4f;
            RectTransform starsRow = Rect("Stars", root, Vector2.one, Vector2.one, new Vector2(-30f, -280f), new Vector2(5 * starSize + 4 * gap, starSize), new Vector2(1f, 1f));
            var starImages = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                RectTransform s = Rect("Star " + (i + 1), starsRow, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * (starSize + gap), 0f), Vector2.one * starSize, new Vector2(0f, 0.5f));
                starImages[i] = s.gameObject.AddComponent<Image>();
                starImages[i].sprite = star;
                starImages[i].raycastTarget = false;
            }

            // Arrest bar, bottom centre.
            RectTransform bar = Rect("Arrest Bar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(440f, 54f), new Vector2(0.5f, 0f));
            Text label = NewText("Label", bar, font, 24, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.95f));
            label.text = "ARREST";
            label.fontStyle = FontStyle.Bold;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, 26f);
            RectTransform back = Rect("Background", bar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 20f), new Vector2(0.5f, 0f));
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = panel;
            backImage.type = Image.Type.Sliced;
            backImage.color = new Color(0f, 0f, 0f, 0.65f);
            backImage.raycastTarget = false;
            RectTransform fill = Rect("Fill", back, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-6f, -6f), new Vector2(0f, 0.5f));
            fill.anchoredPosition = new Vector2(3f, 0f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f, 1f);
            fillImage.raycastTarget = false;
            fill.localScale = new Vector3(0f, 1f, 1f);

            // BUSTED screen.
            RectTransform busted = Rect("Busted Screen", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform band = Rect("Band", busted, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 190f));
            var bandImage = band.gameObject.AddComponent<Image>();
            bandImage.color = new Color(0f, 0f, 0f, 0.55f);
            bandImage.raycastTarget = false;
            Text bustedText = NewText("Text", band, font, 130, TextAnchor.MiddleCenter, new Color(0.55f, 0.75f, 1f, 1f));
            bustedText.text = "BUSTED";
            bustedText.fontStyle = FontStyle.Bold;
            Stretch(bustedText.rectTransform);
            var shadow = bustedText.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(3f, -3f);

            // Fine message after the arrest.
            RectTransform fine = Rect("Fine Message", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(720f, 64f), new Vector2(0.5f, 1f));
            var fineBack = fine.gameObject.AddComponent<Image>();
            fineBack.sprite = panel;
            fineBack.type = Image.Type.Sliced;
            fineBack.color = new Color(0f, 0f, 0f, 0.6f);
            fineBack.raycastTarget = false;
            Text fineText = NewText("Text", fine, font, 32, TextAnchor.MiddleCenter, Color.white);
            Stretch(fineText.rectTransform);

            var view = root.gameObject.AddComponent<WantedHudView>();
            var so = new SerializedObject(view);
            so.FindProperty("starsRoot").objectReferenceValue = starsRow.gameObject;
            so.FindProperty("activeStarSprite").objectReferenceValue = star;
            so.FindProperty("inactiveStarSprite").objectReferenceValue = starOutline;
            SerializedProperty starsProperty = so.FindProperty("stars");
            starsProperty.arraySize = starImages.Length;
            for (int i = 0; i < starImages.Length; i++) starsProperty.GetArrayElementAtIndex(i).objectReferenceValue = starImages[i];
            so.FindProperty("bustBarRoot").objectReferenceValue = bar.gameObject;
            so.FindProperty("bustBarFill").objectReferenceValue = fill;
            so.FindProperty("bustedScreen").objectReferenceValue = busted.gameObject;
            so.FindProperty("fineMessage").objectReferenceValue = fine.gameObject;
            so.FindProperty("fineText").objectReferenceValue = fineText;
            so.ApplyModifiedPropertiesWithoutUndo();

            starsRow.gameObject.SetActive(false);
            bar.gameObject.SetActive(false);
            busted.gameObject.SetActive(false);
            fine.gameObject.SetActive(false);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color)
        {
            RectTransform rect = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 50f));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Assign(Object target, params (string name, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (propertyName, value) in values)
            {
                SerializedProperty property = so.FindProperty(propertyName);
                if (property == null) Debug.LogError($"[{nameof(PoliceSystemBuilder)}] No field '{propertyName}' on {target.GetType().Name}.");
                else property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignArray(Object target, string propertyName, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
