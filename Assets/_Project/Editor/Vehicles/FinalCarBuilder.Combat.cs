using System.Collections.Generic;
using System.Text;
using Invector;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Damage and drivers on the final cars (runs at the end of Build Final Cars, and on its own on the existing prefabs). Re-runnable:
    /// everything it adds is replaced, nothing else on the prefabs changes.
    /// - Hit zones on the VehicleHitbox layer (triggers; that layer collides with nothing, only bullets' line casts find it): metal body,
    ///   window glass, driver head and torso.
    /// - VehicleHealth + the shared VehicleDamageSettings asset, effects, VehicleCharacterImpact (cars hit pedestrians).
    /// - Wreck prefabs for the cars that have a destroyed model in Assets/GameObject.
    /// - Seated driver: a static baked mesh (PedDriverRoster), no Animator, no shadow, culled by its own LOD group.
    /// - Traffic_Police: a prefab variant of Assets/Game/Prefabs/PoliceCar.prefab with a seated officer (it cannot be stolen).
    /// Menu: Tools > OpenworldCrime > Vehicles > Add Car Damage And Drivers. Pedestrian prefabs and the scene wiring: Setup Combat In Open Scene.
    /// </summary>
    public static partial class FinalCarBuilder
    {
        private const string SettingsPath = "Assets/_Project/Data/Vehicles/VehicleDamageSettings.asset";
        private const string WreckFolder = "Assets/_Project/Prefabs/Vehicles/Wrecks";
        private const string EffectFolder = "Assets/_Project/Prefabs/Vehicles/Effects";
        private const string PoliceSourcePath = "Assets/Game/Prefabs/PoliceCar.prefab";
        private const string TrafficPolicePath = TrafficFolder + "/Traffic_Police.prefab";
        private const string TaxiSourcePath = "Assets/Game/Prefabs/Taxi.prefab";
        private const string TaxiVariantPath = "Assets/_Project/Prefabs/Vehicles/Taxi_CityScale.prefab";
        // Hamza's police car and taxi are modelled about 6 m long and 2.15 m tall; these bring them to the final cars' size. The source
        // prefabs stay untouched: the factor lives on the root of a variant (neither has an RCC rig that would need refitting). The
        // police car's driver seat cancels it, so the officer stays full size.
        private const float PoliceScale = 0.82f;
        private const float TaxiScale = 0.80f;
        private const string BloodSourcePath = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Other/SimpleEnemy_TargetPractice.prefab";
        private const string ExplosionEffectPath = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Particles/Explosion/Explosion_Air.prefab";
        private const string ExplosionSoundPath = "Assets/AudioClip/fire_explosion_huge.ogg";
        private const string ParticleMaterialFolder = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Particles/EffectExamples/FireExplosionEffects/Materials/";
        // Screen height share under which a seated driver is culled: about 40 m on a phone's field of view.
        private const float DriverCullHeight = 0.025f;

        private static readonly Dictionary<string, string> DestroyedModels = new Dictionary<string, string>
        {
            { "Khodge2", "Khodge 2 destroyed" },
            { "LandSover", "Land Sover destroyed" },
        };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Add Car Damage And Drivers")]
        public static void BuildCombatMenu() => Debug.Log(BuildCombat());

        [MenuItem("Tools/OpenworldCrime/Vehicles/Setup Combat In Open Scene")]
        public static void SetupSceneMenu() => Debug.Log(SetupCombatInOpenScene());

        /// <summary>Everything, in order, for the open scene: pedestrians, roster, cars, scene references.</summary>
        public static string SetupCombatInOpenScene()
        {
            var sb = new StringBuilder();
            sb.AppendLine(PedestrianVariantBuilder.BuildForOpenScene());
            sb.AppendLine(PedDriverBuilder.Build());
            sb.AppendLine(BuildCombat());
            sb.AppendLine(PedDriverBuilder.RepatchPrefabs());
            sb.AppendLine(WireScene());
            return sb.ToString();
        }

        /// <summary>Adds the damage setup and seated drivers to all 12 drivable and traffic prefabs and the police car.</summary>
        public static string BuildCombat()
        {
            EnsureFolder(WreckFolder);
            EnsureFolder(EffectFolder);
            EnsureFolder("Assets/_Project/Data/Vehicles");
            materialCache.Clear();
            MakeHitboxLayerIgnoreCollisions();
            VehicleDamageSettings settings = EnsureSettings();
            var sb = new StringBuilder("[FinalCarBuilder] Damage and drivers:\n");
            foreach (CarDef def in Cars)
            {
                try
                {
                    GameObject wreck = BuildWreck(def);
                    string dPath = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
                    RigDrivable(dPath, settings, wreck);
                    RigTraffic($"{TrafficFolder}/Traffic_{def.Name}.prefab", dPath, settings, wreck);
                    sb.AppendLine($"  {def.Name}: ok{(wreck != null ? " (destroyed model)" : "")}");
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"  {def.Name}: FAILED {e.Message}\n{e.StackTrace}");
                }
            }
            try { sb.AppendLine("  " + BuildPoliceVariant(settings)); }
            catch (System.Exception e) { sb.AppendLine($"  Police: FAILED {e.Message}\n{e.StackTrace}"); }
            try { sb.AppendLine("  " + BuildTaxiVariant()); }
            catch (System.Exception e) { sb.AppendLine($"  Taxi: FAILED {e.Message}\n{e.StackTrace}"); }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Only line casts need this layer: no physics pair, no trigger callback (the player's capsule walks through it unnoticed).
        private static void MakeHitboxLayerIgnoreCollisions()
        {
            int layer = LayerMask.NameToLayer("VehicleHitbox");
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(layer, i, true);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Settings and effects
        // ---------------------------------------------------------------------------------------------------------------
        private static VehicleDamageSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<VehicleDamageSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<VehicleDamageSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.smokeEffect = BuildLoopingEffect("VFX_CarSmoke", "SmokeDarkParticle", 7f, 2.6f, 1.1f, 2.6f, 1.6f, new Color(0.25f, 0.25f, 0.25f, 0.65f), 0.2f, 0.5f);
            settings.fireEffect = BuildLoopingEffect("VFX_CarFire", "FlameParticle", 18f, 0.7f, 0.9f, 1.4f, 1.4f, new Color(1f, 0.75f, 0.4f, 1f), 0.45f, 0.25f);
            settings.explosionEffect = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionEffectPath);
            settings.explosionSound = AssetDatabase.LoadAssetAtPath<AudioClip>(ExplosionSoundPath);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static GameObject BuildLoopingEffect(string name, string materialName, float rate, float life, float startSize, float endSize, float riseSpeed, Color color, float radius, float sizeRandom)
        {
            var go = new GameObject(name);
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
                main.duration = 5f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
                main.startSpeed = riseSpeed;
                main.startSize = new ParticleSystem.MinMaxCurve(startSize * (1f - sizeRandom), startSize);
                main.startColor = color;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = Mathf.CeilToInt(rate * life * 1.3f);
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTime = rate;
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 12f;
                shape.radius = radius;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize / startSize));
                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                 new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialFolder + materialName + ".mat");
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return PrefabUtility.SaveAsPrefabAsset(go, $"{EffectFolder}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Wrecks
        // ---------------------------------------------------------------------------------------------------------------
        // The destroyed model, stripped of its scripts, physics pieces and legacy-shader particles, on the wheels' ground, centred
        // and facing like the live model.
        private static GameObject BuildWreck(CarDef def)
        {
            if (!DestroyedModels.TryGetValue(def.Name, out string sourceName)) return null;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourceFolder}/{sourceName}.prefab");
            if (source == null) throw new System.Exception($"{sourceName} not found");

            GameObject live = PrefabUtility.LoadPrefabContents($"{DrivableFolder}/Drivable_{def.Name}.prefab");
            Quaternion facing;
            try
            {
                Transform liveModel = live.transform.Find($"Model/{def.Source}");
                facing = liveModel != null ? liveModel.localRotation : Quaternion.identity;
            }
            finally { PrefabUtility.UnloadPrefabContents(live); }

            var root = new GameObject("Wreck_" + def.Name);
            try
            {
                var inst = (GameObject)Object.Instantiate(source, root.transform);
                inst.name = sourceName;
                Transform t = inst.transform;
                t.localPosition = Vector3.zero;
                t.localRotation = facing;
                t.localScale = source.transform.localScale * def.Scale;
                Clean(t, 0);
                RemapMaterials(t);
                bool init = false;
                Bounds bounds = new Bounds();
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    Bounds b = RootBounds(r, root.transform);
                    if (!init) { bounds = b; init = true; } else bounds.Encapsulate(b);
                }
                t.position += root.transform.TransformVector(new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z));
                return PrefabUtility.SaveAsPrefabAsset(root, $"{WreckFolder}/Wreck_{def.Name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Rigging
        // ---------------------------------------------------------------------------------------------------------------
        private sealed class Rig
        {
            public VehicleHealth Health;
            public GameObject DriverVisual;
            public SeatedDriverHealth DriverHealth;
            public Collider Glass;
        }

        private static void RigDrivable(string path, VehicleDamageSettings settings, GameObject wreck)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform rt = root.transform;
                Transform seat = rt.Find("DriverSeat");
                Rig rig = AddRig(root, BodyOf(rt), seat, settings, wreck, new[] { rt.Find("Lights"), rt.Find("Exhausts") });

                // The door side, read from the existing enter point (see BuildDrivable).
                Transform enterPoint = rt.Find("EnterPoint");
                float outerX = enterPoint.localPosition.x + 0.45f, handleZ = enterPoint.localPosition.z + 0.05f;
                Transform stand = SetOrCreatePoint(rt, "CarjackStandPoint", new Vector3(outerX - 0.32f, 0.02f, handleZ - 0.25f), 69f);
                Transform eject = SetOrCreatePoint(rt, "EjectPoint", new Vector3(outerX - 0.75f, 0.02f, seat.localPosition.z + 0.30f), 284f);

                var driver = GetOrAdd<TrafficVehicleDriver>(root);
                var driverSo = new SerializedObject(driver);
                driverSo.FindProperty("npcSeatedVisual").objectReferenceValue = rig.DriverVisual;
                driverSo.FindProperty("seatedHealth").objectReferenceValue = rig.DriverHealth;
                driverSo.FindProperty("carjackStandPoint").objectReferenceValue = stand;
                driverSo.FindProperty("ejectPoint").objectReferenceValue = eject;
                driverSo.FindProperty("groundLayers").intValue = 1;
                driverSo.ApplyModifiedPropertiesWithoutUndo();

                var adapter = root.GetComponent<RCCVehicleAdapter>();
                var adapterSo = new SerializedObject(adapter);
                adapterSo.FindProperty("npcDriver").objectReferenceValue = driver;
                adapterSo.FindProperty("health").objectReferenceValue = rig.Health;
                adapterSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RigTraffic(string path, string drivablePath, VehicleDamageSettings settings, GameObject wreck)
        {
            var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(drivablePath);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform rt = root.transform;
                Transform seat = SetOrCreatePoint(rt, "DriverSeat", drivable.GetDriverSeat().localPosition, 0f);
                seat.gameObject.layer = 0;
                Rig rig = AddRig(root, BodyOf(rt), seat, settings, wreck,
                    new[] { rt.Find("HaloLight-Left"), rt.Find("HaloLight-Right"), rt.Find("BrakeLight1"), rt.Find("BrakeLight2") });
                WireTrafficAdapter(root.GetComponent<TrafficVehicleAdapter>(), rig, true, false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void WireTrafficAdapter(TrafficVehicleAdapter adapter, Rig rig, bool stealable, bool police)
        {
            var so = new SerializedObject(adapter);
            so.FindProperty("driverVisual").objectReferenceValue = rig.DriverVisual;
            so.FindProperty("driverHealth").objectReferenceValue = rig.DriverHealth;
            so.FindProperty("glassCollider").objectReferenceValue = rig.Glass;
            so.FindProperty("health").objectReferenceValue = rig.Health;
            so.FindProperty("stealable").boolValue = stealable;
            so.FindProperty("policeDriver").boolValue = police;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // A variant, so the police car itself stays Hamza's: whatever he changes on it comes through.
        private static string BuildPoliceVariant(VehicleDamageSettings settings)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceSourcePath);
            if (source == null) return "Police: no police car prefab";
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                root.name = "Traffic_Police";
                Transform rt = root.transform;
                rt.localScale = Vector3.one * PoliceScale;
                Transform model = rt.Find("Body");
                Transform helm = model != null ? model.Find("Helm") : null;
                if (helm == null) return "Police: no Body/Helm in the police car";
                Bounds body = new Bounds();
                bool init = false;
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Bounds b = RootBounds(r, rt);
                    if (!init) { body = b; init = true; } else body.Encapsulate(b);
                }
                Bounds wheel = RootBounds(helm.GetComponent<MeshRenderer>(), rt);
                // Root space here is shrunk by the variant's scale; the seat is in metres, so its offsets are divided by it.
                Vector3 hips = new Vector3(wheel.center.x, wheel.center.y - WheelAboveHips / PoliceScale, wheel.center.z - HipsBehindWheel / PoliceScale);
                Transform seat = SetOrCreatePoint(rt, "DriverSeat", hips - PedDriverBuilder.PlayerSeatedHips / PoliceScale, 0f);
                seat.localScale = Vector3.one / PoliceScale;
                seat.gameObject.layer = 0;

                var hitBody = new CarBody
                {
                    Lower = new Bounds(new Vector3(body.center.x, body.min.y + body.size.y * 0.3f, body.center.z), new Vector3(body.size.x, body.size.y * 0.5f, body.size.z)),
                    Cabin = new Bounds(new Vector3(body.center.x, body.min.y + body.size.y * 0.78f, body.center.z), new Vector3(body.size.x * 0.85f, body.size.y * 0.46f, body.size.z * 0.6f)),
                    Length = body.size.z,
                };
                FitPoliceBody(rt, hitBody);
                Rig rig = AddRig(root, hitBody, seat, settings, null, new[] { rt.Find("HaloLight-Left"), rt.Find("HaloLight-Right"), rt.Find("BrakeLight1"), rt.Find("BrakeLight2") });

                var adapter = GetOrAdd<TrafficVehicleAdapter>(root);
                var so = new SerializedObject(adapter);
                so.FindProperty("trafficAI").objectReferenceValue = root.GetComponent<TSTrafficAI>();
                so.FindProperty("body").objectReferenceValue = root.GetComponent<Rigidbody>();
                so.FindProperty("simpleCar").objectReferenceValue = root.GetComponent<TSSimpleCar>();
                so.ApplyModifiedPropertiesWithoutUndo();
                WireTrafficAdapter(adapter, rig, false, true);
                PrefabUtility.SaveAsPrefabAsset(root, TrafficPolicePath);
                return "Police: variant ok";
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        // The source car has one solid box as tall as the cabin and as long as the car. Bullets stopped on it in the air above the hood
        // and trunk, so they never reached the windows: the variant splits it into the same two boxes as the other cars.
        private static void FitPoliceBody(Transform rt, CarBody body)
        {
            Transform solid = rt.Find("Body Collider");
            if (solid == null || !solid.TryGetComponent(out BoxCollider lower)) return;
            // The bodies are in root space and this box is a plain child of the scaled root, so sizes carry over unchanged.
            lower.center = solid.InverseTransformPoint(rt.TransformPoint(body.Lower.center));
            lower.size = body.Lower.size;
            BoxCollider cabin = solid.gameObject.AddComponent<BoxCollider>();
            cabin.center = solid.InverseTransformPoint(rt.TransformPoint(body.Cabin.center));
            cabin.size = body.Cabin.size;
        }

        // A variant, so the taxi itself stays Hamza's: only the root scale differs.
        private static string BuildTaxiVariant()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(TaxiSourcePath);
            if (source == null) return "Taxi: no taxi prefab";
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                root.name = "Taxi_CityScale";
                root.transform.localScale = Vector3.one * TaxiScale;
                PrefabUtility.SaveAsPrefabAsset(root, TaxiVariantPath);
                return "Taxi: variant ok";
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private sealed class CarBody
        {
            public Bounds Lower;
            public Bounds Cabin;
            public float Length;
        }

        // The two solid body boxes of FitBodyColliders.
        private static CarBody BodyOf(Transform rt)
        {
            Transform cols = rt.Find("Model/Colliders");
            if (cols == null)
            {
                Transform center = rt.Find("Center/Model/Colliders");
                cols = center;
            }
            if (cols == null) throw new System.Exception($"no Model/Colliders on {rt.name}");
            BoxCollider[] boxes = cols.GetComponents<BoxCollider>();
            if (boxes.Length < 2) throw new System.Exception($"expected two body boxes on {rt.name}");
            return new CarBody { Lower = RootBox(boxes[0], rt), Cabin = RootBox(boxes[1], rt), Length = RootBox(boxes[0], rt).size.z };
        }

        private static Bounds RootBox(BoxCollider box, Transform rt)
        {
            Vector3 center = rt.InverseTransformPoint(box.transform.TransformPoint(box.center));
            Vector3 scale = box.transform.lossyScale;
            return new Bounds(center, new Vector3(box.size.x * scale.x, box.size.y * scale.y, box.size.z * scale.z));
        }

        // Bullet hitboxes just outside the solid boxes (the solid ones are on Default on traffic cars, which bullets also hit), health,
        // car-hits-people, the seated driver and his hit zones. Replaces whatever an earlier run made.
        private static Rig AddRig(GameObject root, CarBody body, Transform seat, VehicleDamageSettings settings, GameObject wreck, Transform[] wreckOff)
        {
            Transform rt = root.transform;
            int hitLayer = LayerMask.NameToLayer("VehicleHitbox");
            int glassTag = System.Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, "Glass");

            Transform old = rt.Find("BulletHitbox");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform oldVisual = seat.Find("DriverVisual");
            if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);
            Transform oldEngine = rt.Find("EngineAnchor");
            if (oldEngine != null) Object.DestroyImmediate(oldEngine.gameObject);

            var rig = new Rig();

            // Health, on the root next to the Rigidbody.
            rig.Health = GetOrAdd<VehicleHealth>(root);
            Transform engine = NewChild("EngineAnchor", rt, root.layer);
            engine.localPosition = new Vector3(body.Lower.center.x, body.Lower.max.y, body.Lower.center.z + body.Length * 0.3f);
            var offList = new List<GameObject>();
            foreach (Transform t in wreckOff) if (t != null) offList.Add(t.gameObject);
            var healthSo = new SerializedObject(rig.Health);
            healthSo.FindProperty("settings").objectReferenceValue = settings;
            healthSo.FindProperty("destroyedModelPrefab").objectReferenceValue = wreck;
            healthSo.FindProperty("effectAnchor").objectReferenceValue = engine;
            SerializedProperty off = healthSo.FindProperty("disableWhenWrecked");
            off.arraySize = offList.Count;
            for (int i = 0; i < offList.Count; i++) off.GetArrayElementAtIndex(i).objectReferenceValue = offList[i];
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            var impact = GetOrAdd<VehicleCharacterImpact>(root);
            var impactSo = new SerializedObject(impact);
            impactSo.FindProperty("receiverLayers").intValue = (1 << 0) | (1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("BodyPart"));
            impactSo.ApplyModifiedPropertiesWithoutUndo();

            // Metal.
            Transform hitbox = NewChild("BulletHitbox", rt, hitLayer);
            var metal = hitbox.gameObject.AddComponent<BoxCollider>();
            metal.isTrigger = true;
            metal.center = body.Lower.center;
            metal.size = body.Lower.size + Vector3.one * 0.04f;
            var zone = hitbox.gameObject.AddComponent<VehicleHitZone>();
            var zoneSo = new SerializedObject(zone);
            zoneSo.FindProperty("health").objectReferenceValue = rig.Health;
            zoneSo.ApplyModifiedPropertiesWithoutUndo();

            // Cabin: bullets stop here and are continued into the driver at once (VehicleGlassHit).
            Transform glass = NewChild("GlassHitbox", hitbox, hitLayer);
            if (glassTag >= 0) glass.gameObject.tag = "Glass";
            var glassBox = glass.gameObject.AddComponent<BoxCollider>();
            glassBox.isTrigger = true;
            glassBox.center = body.Cabin.center;
            glassBox.size = body.Cabin.size + Vector3.one * 0.06f;
            var glassHit = glass.gameObject.AddComponent<VehicleGlassHit>();
            var glassSo = new SerializedObject(glassHit);
            glassSo.FindProperty("hitboxLayers").intValue = 1 << hitLayer;
            glassSo.FindProperty("health").objectReferenceValue = rig.Health;
            glassSo.ApplyModifiedPropertiesWithoutUndo();
            rig.Glass = glassBox;

            // Seated driver: a static mesh on the seat, without shadow, culled early by its own LOD group.
            var visual = new GameObject("DriverVisual") { layer = seat.gameObject.layer };
            visual.transform.SetParent(seat, false);
            visual.AddComponent<MeshFilter>();
            var meshRenderer = visual.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var lod = visual.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(DriverCullHeight, new Renderer[] { meshRenderer }) });
            rig.DriverVisual = visual;

            // Driver hit zones: head and torso over the seated hips.
            Vector3 hips = rt.InverseTransformPoint(seat.TransformPoint(PedDriverBuilder.PlayerSeatedHips));
            Transform zones = NewChild("DriverHitZones", hitbox, hitLayer);
            rig.DriverHealth = zones.gameObject.AddComponent<SeatedDriverHealth>();
            var blood = zones.gameObject.AddComponent<vHitDamageParticle>();
            var bloodSource = AssetDatabase.LoadAssetAtPath<GameObject>(BloodSourcePath);
            var bloodFrom = bloodSource != null ? bloodSource.GetComponent<vHitDamageParticle>() : null;
            if (bloodFrom != null) EditorUtility.CopySerialized(bloodFrom, blood);
            var driverSo = new SerializedObject(rig.DriverHealth);
            driverSo.FindProperty("hitParticle").objectReferenceValue = blood;
            driverSo.FindProperty("flinchTarget").objectReferenceValue = visual.transform;
            driverSo.FindProperty("maxHealth").floatValue = 1f;
            driverSo.FindProperty("headZone").objectReferenceValue = AddDriverZone(zones, "Head", hitLayer, rig.DriverHealth, 4f, hips + new Vector3(0f, 0.72f, -0.13f), 0.15f, 0f);
            driverSo.FindProperty("torsoZone").objectReferenceValue = AddDriverZone(zones, "Torso", hitLayer, rig.DriverHealth, 1f, hips + new Vector3(0f, 0.3f, -0.05f), 0.22f, 0.7f);
            driverSo.ApplyModifiedPropertiesWithoutUndo();
            return rig;
        }

        private static Transform AddDriverZone(Transform parent, string name, int layer, SeatedDriverHealth health, float multiplier, Vector3 rootPosition, float radius, float height)
        {
            Transform t = NewChild(name, parent, layer);
            t.position = parent.root.TransformPoint(rootPosition);
            // Sizes in metres, whatever the car's root scale.
            float toLocal = 1f / Mathf.Max(1e-4f, t.lossyScale.y);
            radius *= toLocal;
            height *= toLocal;
            if (height <= 0f)
            {
                var sphere = t.gameObject.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = radius;
            }
            else
            {
                var capsule = t.gameObject.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = radius;
                capsule.height = height;
            }
            var hit = t.gameObject.AddComponent<SeatedDriverHitZone>();
            var so = new SerializedObject(hit);
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("damageMultiplier").floatValue = multiplier;
            so.ApplyModifiedPropertiesWithoutUndo();
            return t;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component => go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

        // ---------------------------------------------------------------------------------------------------------------
        // Scene
        // ---------------------------------------------------------------------------------------------------------------
        private static string WireScene()
        {
            var sb = new StringBuilder("[FinalCarBuilder] Scene:\n");
            var traffic = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            var police = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPolicePath);
            var policeSource = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceSourcePath);
            if (traffic != null && police != null)
            {
                foreach (TSTrafficSpawner.TSSpawnVehicles entry in traffic.cars)
                {
                    if (entry.cars == policeSource) { entry.cars = police; sb.AppendLine("  traffic spawner: police car uses Traffic_Police"); }
                }
                EditorUtility.SetDirty(traffic);
            }

            var taxiService = Object.FindFirstObjectByType<TaxiService>(FindObjectsInactive.Include);
            var taxiVariant = AssetDatabase.LoadAssetAtPath<GameObject>(TaxiVariantPath);
            if (taxiService != null && taxiVariant != null)
            {
                taxiService.taxiPrefab = taxiVariant;
                EditorUtility.SetDirty(taxiService);
                sb.AppendLine("  taxi service: uses Taxi_CityScale");
            }

            var takeover = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            var people = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            if (takeover != null)
            {
                var so = new SerializedObject(takeover);
                so.FindProperty("pedestrianSpawner").objectReferenceValue = people;
                so.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine($"  takeover manager: pedestrian spawner {(people != null ? "set" : "MISSING")}");
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return sb.ToString();
        }
    }
}
