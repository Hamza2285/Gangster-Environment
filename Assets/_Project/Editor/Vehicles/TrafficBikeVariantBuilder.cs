using System.Collections.Generic;
using System.Text;
using Invector;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Makes Hamza's traffic motorbikes (Assets/Prefabs/Bikes, built by his BikePrefabBuilder) stealable and shootable without
    /// touching them: one prefab variant per bike under _Project/Prefabs/Vehicles/TrafficBikes adds
    /// - <see cref="TrafficBikeAdapter"/> (steal -> drivable twin, crash judgement, shot rider),
    /// - the rider's <see cref="SeatedDriverHealth"/> with head and chest hit zones on his bones (VehicleHitbox layer, like the
    ///   seated drivers of the traffic cars),
    /// - interaction trigger, interaction and enter points (left side).
    /// Then, in the open gameplay scene, the iTS Traffic Spawner's bike entries are pointed at the variants and the drivable bikes
    /// are added to the TrafficTakeoverManager's pre-warmed prefabs. Re-running updates the variants in place (GUIDs kept).
    /// Needs Build Drivable Bikes first.
    /// </summary>
    public static class TrafficBikeVariantBuilder
    {
        private const string SourceDir = "Assets/Prefabs/Bikes";
        private const string OutDir = "Assets/_Project/Prefabs/Vehicles/TrafficBikes";
        private const string SportDrivable = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Bike_Sport.prefab";
        private const string ChopperDrivable = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Bike_Chopper.prefab";
        private const string CrashSettingsPath = "Assets/_Project/Data/Vehicles/BikeCrashSettings.asset";

        /// <summary>
        /// Hamza's sport bikes are about 2.25 m long and 1.3 m tall, oversized next to the player (a real sport bike is ~2.0 m long,
        /// ~1.15 m tall with ~0.8 m seat height). The variants scale the bike (model, lamps, wheel colliders) by this about its ground
        /// point; the rider keeps his size and is re-posed onto the smaller seat, bars and pegs. The choppers stay as they are.
        /// </summary>
        public const float SportScale = 0.9f;
        private const string SportFbx = "Assets/Bike 1/Bike_1.fbx";
        // Sport bike grip / peg points (model space before scaling, as in BikePrefabBuilder) and the rider's pole hints.
        private static readonly Vector3 SportGrip = new Vector3(0.32f, 1.13f, 0.66f);
        private static readonly Vector3 SportPeg = new Vector3(0.27f, 0.58f, -0.45f);
        private static readonly Vector3 SportKneeHint = new Vector3(0.35f, 0.35f, 1f);
        private static readonly Vector3 SportElbowHint = new Vector3(1f, -0.8f, -0.2f);

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Traffic Bike Variants")]
        public static void Build()
        {
            var log = new StringBuilder("[TrafficBikeVariantBuilder]\n");
            var crash = AssetDatabase.LoadAssetAtPath<BikeCrashSettings>(CrashSettingsPath);
            var sport = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(SportDrivable);
            var chopper = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(ChopperDrivable);
            if (crash == null || sport == null || chopper == null)
            {
                Debug.LogError("[TrafficBikeVariantBuilder] Needs " + CrashSettingsPath + " and the drivable bikes (Build Drivable Bikes).");
                return;
            }
            var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Vehicles", "TrafficBikes");

            var variants = new Dictionary<GameObject, GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SourceDir }))
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (source.GetComponent<BikeCrash>() == null) continue;
                RCCVehicleAdapter drivable = source.name.Contains("Chopper") ? chopper : sport;
                GameObject variant = BuildVariant(source, drivable, crash, spawner, log);
                if (variant != null) variants[source] = variant;
            }
            WireScene(variants, new[] { sport, chopper }, log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        private static GameObject BuildVariant(GameObject source, RCCVehicleAdapter drivable, BikeCrashSettings crash, PedestrianSpawner spawner, StringBuilder log)
        {
            string path = OutDir + "/TrafficBike_" + (source.name.StartsWith("Bike_") ? source.name.Substring(5) : source.name) + ".prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                Transform root = instance.transform;
                Transform visual = root.Find("Visual");
                Transform rider = root.Find("Visual/Rider");
                if (visual == null || rider == null) return null;
                if (!source.name.Contains("Chopper")) ScaleSportBike(root, visual, rider);
                int hitLayer = LayerMask.NameToLayer("VehicleHitbox");
                int triggerLayer = LayerMask.NameToLayer("Triggers");

                // Rider health and hit zones (on his bones, so they lean with him).
                var health = GetOrAdd<SeatedDriverHealth>(rider.gameObject);
                GetOrAdd<vHitDamageParticle>(rider.gameObject);
                Transform head = FindBone(rider, "Bip01 Head");
                Transform pelvis = FindBone(rider, "Bip01 Pelvis");
                Transform neck = FindBone(rider, "Bip01 Neck");
                if (head != null)
                {
                    Transform zone = Child(head, "RiderHeadZone", hitLayer);
                    var sphere = GetOrAdd<SphereCollider>(zone.gameObject);
                    sphere.isTrigger = true;
                    sphere.radius = 0.16f / Mathf.Max(1e-4f, zone.lossyScale.x);
                    zone.position = head.position + Vector3.up * 0.07f;
                    AddHitZone(zone, health, 4f);
                }
                if (pelvis != null && neck != null)
                {
                    Transform zone = Child(pelvis, "RiderTorsoZone", hitLayer);
                    Vector3 from = pelvis.position, to = neck.position;
                    zone.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, to - from));
                    var capsule = GetOrAdd<CapsuleCollider>(zone.gameObject);
                    capsule.isTrigger = true;
                    capsule.direction = 1;
                    float scale = Mathf.Max(1e-4f, zone.lossyScale.y);
                    capsule.radius = 0.24f / scale;
                    capsule.height = (Vector3.Distance(from, to) + 0.3f) / scale;
                    AddHitZone(zone, health, 1f);
                }

                // Interaction (left side, like the drivable twin's enter point).
                Transform interactionZone = Child(root, "InteractionZone", triggerLayer);
                var box = GetOrAdd<BoxCollider>(interactionZone.gameObject);
                box.isTrigger = true;
                box.center = new Vector3(0f, 1f, 0f);
                box.size = new Vector3(3.4f, 2f, 3.4f);
                Transform interactionPoint = Child(root, "InteractionPoint", root.gameObject.layer);
                interactionPoint.SetLocalPositionAndRotation(new Vector3(-0.6f, 0.6f, -0.3f), Quaternion.LookRotation(Vector3.right));
                Transform enterPoint = Child(root, "EnterPoint", root.gameObject.layer);
                Transform drivableEnter = drivable.transform.Find("EnterPoint");
                Vector3 enterLocal = drivableEnter != null ? drivableEnter.localPosition + visual.localPosition : new Vector3(-0.85f, 0f, -0.3f);
                enterPoint.SetLocalPositionAndRotation(enterLocal, Quaternion.LookRotation(Vector3.right));
                Transform alternateEnter = Child(root, "AlternateEnterPoint", root.gameObject.layer);
                alternateEnter.SetLocalPositionAndRotation(new Vector3(-enterLocal.x, enterLocal.y, enterLocal.z), Quaternion.LookRotation(Vector3.left));

                var adapter = GetOrAdd<TrafficBikeAdapter>(instance);
                var so = new SerializedObject(adapter);
                so.FindProperty("trafficAI").objectReferenceValue = instance.GetComponent<TSTrafficAI>();
                so.FindProperty("simpleCar").objectReferenceValue = instance.GetComponent<TSSimpleCar>();
                so.FindProperty("body").objectReferenceValue = instance.GetComponent<Rigidbody>();
                so.FindProperty("bikeCrash").objectReferenceValue = instance.GetComponent<BikeCrash>();
                so.FindProperty("crashSettings").objectReferenceValue = crash;
                so.FindProperty("riderVisual").objectReferenceValue = rider.gameObject;
                so.FindProperty("riderHealth").objectReferenceValue = health;
                so.FindProperty("riderPedPrefab").objectReferenceValue = FindPed(spawner, rider);
                so.FindProperty("drivablePrefab").objectReferenceValue = drivable;
                so.FindProperty("drivableAnchor").objectReferenceValue = visual;
                Transform body = root.Find("Visual/Model/Body");
                so.FindProperty("paintRenderer").objectReferenceValue = body != null ? body.GetComponent<Renderer>() : null;
                so.FindProperty("interactionPoint").objectReferenceValue = interactionPoint;
                so.FindProperty("enterPoint").objectReferenceValue = enterPoint;
                so.FindProperty("alternateEnterPoint").objectReferenceValue = alternateEnter;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                log.AppendLine("- " + path + " (rider ped: " + (so.FindProperty("riderPedPrefab").objectReferenceValue != null ? so.FindProperty("riderPedPrefab").objectReferenceValue.name : "none") + ")");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // The bike shrinks about its ground point (Visual's origin); wheel colliders keep their contact with the road; the rider keeps
        // his size, sits on the lowered seat and has his arms and legs re-fitted to the bars and pegs (two-bone IK, as when built).
        private static void ScaleSportBike(Transform root, Transform visual, Transform rider)
        {
            float f = SportScale;
            Transform model = visual.Find("Model");
            Transform pelvis = FindBone(rider, "Bip01 Pelvis");
            Vector3 pelvisLocal = visual.InverseTransformPoint(pelvis.position);
            foreach (Transform child in visual)
            {
                if (child == rider) continue;
                child.localPosition *= f;
                child.localScale *= f;
            }
            float groundY = visual.localPosition.y;
            foreach (WheelCollider wc in root.GetComponentsInChildren<WheelCollider>(true))
            {
                Vector3 p = wc.transform.localPosition;
                wc.transform.localPosition = new Vector3(p.x, groundY + (p.y - groundY) * f, p.z * f);
                wc.radius *= f;
            }
            Transform cog = root.Find("CoG");
            if (cog != null) cog.localPosition = new Vector3(cog.localPosition.x, groundY + (cog.localPosition.y - groundY) * f, cog.localPosition.z * f);

            // Rider: hips onto the scaled seat, then the limbs onto the scaled bars and pegs.
            rider.position += visual.TransformPoint(pelvisLocal * f) - pelvis.position;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SportFbx);
            float assetScale = asset != null ? asset.transform.localScale.x : model.localScale.x / (0.76f * f);
            System.Func<Vector3, Vector3> W = p => model.TransformPoint(p / assetScale);
            foreach (int side in new[] { -1, 1 })
            {
                string s = side < 0 ? "L" : "R";
                Vector3 mirror = new Vector3(side, 1f, 1f);
                Vector3 bikeSpace(Vector3 v) => visual.TransformDirection(Vector3.Scale(v, mirror));
                DrivableBikeBuilder.TwoBoneIK(FindBone(rider, "Bip01 " + s + " Thigh"), FindBone(rider, "Bip01 " + s + " Calf"), FindBone(rider, "Bip01 " + s + " Foot"),
                    W(Vector3.Scale(SportPeg, mirror)) + visual.up * 0.07f, bikeSpace(SportKneeHint));
                DrivableBikeBuilder.TwoBoneIK(FindBone(rider, "Bip01 " + s + " UpperArm"), FindBone(rider, "Bip01 " + s + " Forearm"), FindBone(rider, "Bip01 " + s + " Hand"),
                    W(Vector3.Scale(SportGrip, mirror)), bikeSpace(SportElbowHint));
            }
        }

        // iTS spawner entries -> variants; drivable bikes pre-warmed by the takeover manager.
        private static void WireScene(Dictionary<GameObject, GameObject> variants, RCCVehicleAdapter[] drivables, StringBuilder log)
        {
            int swapped = 0;
            foreach (TSTrafficSpawner spawner in Object.FindObjectsByType<TSTrafficSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(spawner, "Traffic bike variants");
                for (int i = 0; i < spawner.cars.Length; i++)
                {
                    GameObject car = spawner.cars[i].cars;
                    if (car != null && variants.TryGetValue(car, out GameObject variant))
                    {
                        spawner.cars[i].cars = variant;
                        swapped++;
                    }
                }
                EditorUtility.SetDirty(spawner);
            }
            log.AppendLine("- iTS spawner bike entries pointed at the variants: " + swapped);

            var manager = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                var so = new SerializedObject(manager);
                SerializedProperty list = so.FindProperty("prewarmedPrefabs");
                foreach (RCCVehicleAdapter drivable in drivables)
                {
                    bool present = false;
                    for (int i = 0; i < list.arraySize; i++) present |= list.GetArrayElementAtIndex(i).objectReferenceValue == drivable;
                    if (present) continue;
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = drivable;
                }
                so.ApplyModifiedProperties();
                log.AppendLine("- drivable bikes pre-warmed by " + manager.name);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // The rider mesh is named after the pedestrian (Man55 / Man_22 / Girl_33 -> Ped_Man_55 / Ped_Man_22 / Ped_Girl_33).
        private static GameObject FindPed(PedestrianSpawner spawner, Transform rider)
        {
            if (spawner == null) return null;
            var smr = rider.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return null;
            string key = smr.name.Replace("_", "").ToLowerInvariant();
            foreach (GameObject ped in spawner.peoplePrefabs)
            {
                if (ped != null && ped.name.Replace("Ped_", "").Replace("_", "").ToLowerInvariant() == key) return ped;
            }
            return null;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // After the zone's collider exists (the hit zone requires one).
        private static void AddHitZone(Transform zone, SeatedDriverHealth health, float multiplier)
        {
            var hit = GetOrAdd<SeatedDriverHitZone>(zone.gameObject);
            var so = new SerializedObject(hit);
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("damageMultiplier").floatValue = multiplier;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Child(Transform parent, string name, int layer)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            t.gameObject.layer = layer;
            return t;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
