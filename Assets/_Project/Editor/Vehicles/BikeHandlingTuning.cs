using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Arcade handling for our two drivable bikes (values on our prefabs only; RCC and Hamza's BikeRig sources unchanged), from
    /// the BikeHandlingProbe measurements: speed-sensitive steering and yaw damping (BikeHandlingAssist), a firmer upright hold
    /// so the physics body (and the camera following it) stops rolling 13-15 deg in every turn, a quicker visual lean, more
    /// yaw inertia against the snap, a slightly lower centre of mass and RCC's steering helper turned up.
    /// Menu: Tools > OpenworldCrime > Vehicles > Tune Drivable Bikes. Also run at the end of Build Drivable Bikes.
    /// </summary>
    public static class BikeHandlingTuning
    {
        // Sport bike: sharp; chopper: lazier (longer wheelbase, less grip shaped in).
        private static readonly (string path, float maxLean, float yawInertia, float lowSpeedSteer, float grip)[] Bikes =
        {
            ("Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Bike_Sport.prefab", 38f, 420f, 26f, 22f),
            ("Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Bike_Chopper.prefab", 32f, 480f, 24f, 15f),
        };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Tune Drivable Bikes")]
        public static void ApplyMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var log = new StringBuilder("[BikeHandlingTuning]\n");
            foreach (var spec in Bikes)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(spec.path);
                if (root == null) { log.AppendLine("  missing " + spec.path); continue; }
                var car = root.GetComponent<RCC_CarControllerV3>();
                var body = root.GetComponent<Rigidbody>();
                var rig = root.GetComponent<BikeRig>();
                if (car == null || body == null || rig == null) { PrefabUtility.UnloadPrefabContents(root); log.AppendLine("  not a bike: " + spec.path); continue; }

                var assist = GetOrAdd<BikeHandlingAssist>(root.gameObject);
                var so = new SerializedObject(assist);
                so.FindProperty("car").objectReferenceValue = car;
                so.FindProperty("body").objectReferenceValue = body;
                so.FindProperty("lowSpeedSteer").floatValue = spec.lowSpeedSteer;
                // Shaped for 22 m/s²: the tyres' slip makes the real circle about twice the geometric one, so this lands near
                // 1 g in practice (measured: about 17 m at 50 km/h, 60 m at 90 km/h).
                so.FindProperty("lateralGrip").floatValue = spec.grip;
                so.ApplyModifiedPropertiesWithoutUndo();
                car.steerAngle = spec.lowSpeedSteer;

                rig.uprightStrength = 650f;
                rig.uprightDamping = 60f;
                rig.leanResponse = 7f;
                rig.maxLean = spec.maxLean;
                rig.yawInertia = spec.yawInertia;

                car.steeringHelper = true;
                car.steerHelperAngularVelStrength = 0.35f;
                car.steerHelperLinearVelStrength = 0.2f;
                car.angularDragHelperStrength = 0.25f;
                if (car.COM != null) car.COM.localPosition = new Vector3(0f, 0.38f, car.COM.localPosition.z);

                PrefabUtility.SaveAsPrefabAsset(root, spec.path);
                PrefabUtility.UnloadPrefabContents(root);
                log.AppendLine($"  {System.IO.Path.GetFileNameWithoutExtension(spec.path)}: assist, upright 650/60, lean {spec.maxLean} @7, yaw inertia {spec.yawInertia}, COM 0.38");
            }
            return log.ToString();
        }

        // Explicit null check: in the editor GetComponent returns a "fake null" for missing built-in components, which ?? misses.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
