using OpenworldCrime.Police;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Sets up the police car crews without re-running the Police System builder (which would overwrite the tuned prefabs):
    /// - PoliceOfficerCombat.controller: ExitCar / EnterCar states (the project's Mixamo car exit / enter clips) plus mirrored
    ///   copies for the passenger side;
    /// - PoliceCruiser.prefab: a passenger seat with its seated mesh next to the driver's, and <see cref="PoliceCarCrew"/> (roster and
    ///   seat fits copied from the traffic police car, the model's front doors, exit points beside them);
    /// - PoliceSettings: fewer officers on foot (crews included), most of them arriving by car.
    /// Safe to run again (updates in place).
    /// </summary>
    public static class PoliceCrewSetup
    {
        private const string OfficerController = "Assets/_Project/Animations/Police/PoliceOfficerCombat.controller";
        private const string CruiserPath = "Assets/_Project/Prefabs/Police/PoliceCruiser.prefab";
        private const string TrafficPolicePath = "Assets/_Project/Prefabs/Vehicles/Traffic/Traffic_Police.prefab";
        private const string SettingsPath = "Assets/_Project/Data/Police/PoliceSettings.asset";
        private const string ExitClipPath = "Assets/CustomAnimations/Vehicle/Exiting Car.fbx";
        private const string EnterClipPath = "Assets/CustomAnimations/Vehicle/Entering Car.fbx";
        private const string GetUpClipsPath = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_Actions.fbx";
        private static readonly string[] OfficerPrefabs =
        {
            "Assets/_Project/Prefabs/Police/PoliceOfficer_2.prefab",
            "Assets/_Project/Prefabs/Police/PoliceOfficer_3.prefab"
        };

        [MenuItem("Tools/OpenworldCrime/Police/Setup Car Crews")]
        public static void Run()
        {
            SetupController();
            SetupCruiser();
            SetupSettings();
            SetupOfficerGetUp();
            AssetDatabase.SaveAssets();
            Debug.Log("[PoliceCrewSetup] Officer car clips, cruiser crew (driver + passenger, doors) and on-foot limits set up.");
        }

        private static void SetupController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(OfficerController);
            AnimationClip exit = LoadClip(ExitClipPath), enter = LoadClip(EnterClipPath);
            if (controller == null || exit == null || enter == null)
            {
                Debug.LogError("[PoliceCrewSetup] Officer controller or car clips missing.");
                return;
            }
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AddState(machine, "ExitCar", exit, false, new Vector3(300f, 180f));
            AddState(machine, "ExitCarMirror", exit, true, new Vector3(300f, 240f));
            AddState(machine, "EnterCar", enter, false, new Vector3(300f, 300f));
            AddState(machine, "EnterCarMirror", enter, true, new Vector3(300f, 360f));
            AddState(machine, "GetUpFromBack", LoadClip(GetUpClipsPath, "GetUpFromBack"), false, new Vector3(560f, 180f));
            AddState(machine, "GetUpFromBelly", LoadClip(GetUpClipsPath, "GetUpFromBelly"), false, new Vector3(560f, 240f));
            EditorUtility.SetDirty(controller);
        }

        private static void AddState(AnimatorStateMachine machine, string name, AnimationClip clip, bool mirror, Vector3 position)
        {
            AnimatorState state = null;
            foreach (ChildAnimatorState child in machine.states) if (child.state.name == name) state = child.state;
            if (state == null) state = machine.AddState(name, position);
            state.motion = clip;
            state.mirror = mirror;
            state.speed = 1f;
            state.writeDefaultValues = true;
        }

        private static AnimationClip LoadClip(string path, string name)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && clip.name == name) return clip;
            Debug.LogError("[PoliceCrewSetup] Clip " + name + " not found in " + path);
            return null;
        }

        // Shot officers get up with Invector's get-up clips (sit up, stand) after a longer time on the ground, instead of popping up.
        private static void SetupOfficerGetUp()
        {
            foreach (string path in OfficerPrefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var ragdoll = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianRagdoll>();
                    var damage = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
                    if (ragdoll == null || damage == null) continue;
                    var so = new SerializedObject(ragdoll);
                    so.FindProperty("getUpFromBackState").stringValue = "GetUpFromBack";
                    so.FindProperty("getUpFromBellyState").stringValue = "GetUpFromBelly";
                    so.FindProperty("getUpClipSeconds").floatValue = 1.9f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var dso = new SerializedObject(damage);
                    dso.FindProperty("shotGroundTime").floatValue = 4.5f;
                    dso.FindProperty("minimumGroundTime").floatValue = 3.5f;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static AnimationClip LoadClip(string path)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) return clip;
            return null;
        }

        private static void SetupCruiser()
        {
            var trafficPolice = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPolicePath);
            TrafficVehicleAdapter trafficAdapter = trafficPolice != null ? trafficPolice.GetComponent<TrafficVehicleAdapter>() : null;
            if (trafficAdapter == null)
            {
                Debug.LogError("[PoliceCrewSetup] " + TrafficPolicePath + " has no TrafficVehicleAdapter (roster and seat fits come from it).");
                return;
            }
            var trafficSo = new SerializedObject(trafficAdapter);
            var roster = (PedDriverRoster)trafficSo.FindProperty("driverRoster").objectReferenceValue;
            SerializedProperty fitsProperty = trafficSo.FindProperty("driverFits");
            var fits = new PedDriverRoster.SeatFit[fitsProperty.arraySize];
            for (int i = 0; i < fits.Length; i++)
            {
                SerializedProperty fit = fitsProperty.GetArrayElementAtIndex(i);
                fits[i] = new PedDriverRoster.SeatFit { drop = fit.FindPropertyRelative("drop").floatValue, recline = fit.FindPropertyRelative("recline").floatValue };
            }

            GameObject root = PrefabUtility.LoadPrefabContents(CruiserPath);
            try
            {
                Transform driverSeat = root.transform.Find("DriverSeat");
                Transform driverVisual = root.transform.Find("DriverSeat/DriverVisual");
                if (driverSeat == null || driverVisual == null)
                {
                    Debug.LogError("[PoliceCrewSetup] PoliceCruiser has no DriverSeat/DriverVisual.");
                    return;
                }
                Transform passengerSeat = root.transform.Find("PassengerSeat");
                if (passengerSeat == null)
                {
                    passengerSeat = new GameObject("PassengerSeat").transform;
                    passengerSeat.SetParent(root.transform, false);
                }
                Vector3 seatLocal = driverSeat.localPosition;
                passengerSeat.localPosition = new Vector3(-seatLocal.x, seatLocal.y, seatLocal.z);
                passengerSeat.localRotation = driverSeat.localRotation;
                passengerSeat.gameObject.layer = driverSeat.gameObject.layer;
                Transform passengerVisual = passengerSeat.Find("PassengerVisual");
                if (passengerVisual == null)
                {
                    passengerVisual = Object.Instantiate(driverVisual.gameObject, passengerSeat, false).transform;
                    passengerVisual.name = "PassengerVisual";
                }
                driverVisual.gameObject.SetActive(false);
                passengerVisual.gameObject.SetActive(false);

                float exitZ = seatLocal.z + 0.15f;
                var seats = new[]
                {
                    new PoliceCarCrew.Seat
                    {
                        anchor = driverSeat, visual = driverVisual.gameObject, door = root.transform.Find("Body/Door_left"),
                        exitPoint = new Vector3(-1.55f, 0f, exitZ), mirrored = false
                    },
                    new PoliceCarCrew.Seat
                    {
                        anchor = passengerSeat, visual = passengerVisual.gameObject, door = root.transform.Find("Body/Door_right"),
                        exitPoint = new Vector3(1.55f, 0f, exitZ), mirrored = true
                    }
                };
                var crew = root.GetComponent<PoliceCarCrew>();
                if (crew == null) crew = root.AddComponent<PoliceCarCrew>();
                crew.EditorSetup(seats, roster, fits);
                EditorUtility.SetDirty(crew);

                // Officers on foot walk round the car instead of through it (carved into the NavMesh while it stands still).
                var obstacle = root.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = root.AddComponent<NavMeshObstacle>();
                ConfigureObstacle(obstacle, root);

                var unit = root.GetComponent<PoliceCarUnit>();
                var unitSo = new SerializedObject(unit);
                unitSo.FindProperty("crew").objectReferenceValue = crew;
                SerializedProperty seated = unitSo.FindProperty("seatedCrew");
                seated.arraySize = 2;
                seated.GetArrayElementAtIndex(0).objectReferenceValue = driverVisual.gameObject;
                seated.GetArrayElementAtIndex(1).objectReferenceValue = passengerVisual.gameObject;
                unitSo.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, CruiserPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>A car-sized box obstacle carved only while the car stands still (moving cars only push agents aside).</summary>
        public static void ConfigureObstacle(NavMeshObstacle obstacle, GameObject car) => VehicleNavMeshObstacle.Configure(obstacle, car);

        private static void SetupSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PoliceSettings>(SettingsPath);
            if (settings == null) return;
            Undo.RecordObject(settings, "Police crews");
            settings.footUnitsPerStar = new[] { 0, 2, 2, 3, 4, 5 };
            settings.walkingReinforcementsPerStar = new[] { 0, 0, 0, 1, 1, 2 };
            settings.carUnitsPerStar = new[] { 0, 1, 2, 2, 3, 4 };
            settings.officersPerCar = 2;
            EditorUtility.SetDirty(settings);
        }
    }
}
