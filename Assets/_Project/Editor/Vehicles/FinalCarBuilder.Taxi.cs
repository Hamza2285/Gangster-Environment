using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The yellow taxi from Assets/New Folder/Taxi_Preview.fbm, built like the ambulance (Drivable_Taxi + Traffic_Taxi, damage,
    /// wreck, seated driver, lamps) on its own, so the other cars are not rebuilt. It keeps its taxi livery (no paint variants).
    /// The model ships with only its rear-left wheel, so the source prefab gets the other three as mirrored copies first.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Taxi.
    /// </summary>
    public static partial class FinalCarBuilder
    {
        private const string TaxiFbx = "Assets/New Folder/Taxi_Preview.fbm/Taxi.fbx";
        private const string TaxiName = "Taxi";
        private const float TaxiLength = 5.0f;
        private const int TaxiTrafficFrequency = 3;
        // Front axle of the model in its own space (found from the body's front wheel arch); the rear axle is the wheel's own z.
        private const float TaxiFrontAxleZ = 2.4f;

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Taxi")]
        public static void BuildTaxiMenu() => Debug.Log(BuildTaxi());

        public static string BuildTaxi()
        {
            var sb = new StringBuilder("[FinalCarBuilder] Taxi:\n");
            EnsureFolder(DrivableFolder);
            EnsureFolder(TrafficFolder);
            EnsureFolder(MaterialFolder);
            materialCache.Clear();

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(TaxiFbx);
            if (fbx == null) return sb.Append("  missing " + TaxiFbx).ToString();
            string sourcePath = $"{SourceFolder}/{TaxiName}.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            float length = 0f;
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = TaxiName;
                sb.AppendLine("  " + AddMissingTaxiWheels(instance.transform));
                var bounds = new Bounds();
                bool first = true;
                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
                length = Mathf.Max(bounds.size.x, bounds.size.z);
                PrefabUtility.SaveAsPrefabAsset(instance, sourcePath);
            }
            finally { Object.DestroyImmediate(instance); }
            float scale = length > 0.01f ? TaxiLength / length : 1f;
            sb.AppendLine($"  model {length:F2} long -> scale {scale:F3}");

            //                    source    name      class            mass   km/h  torque brake  drive                                   freq                  scale
            var def = new CarDef(TaxiName, TaxiName, CarClass.Sedan, 1900f, 200f, 440f, 2800f, RCC_CarControllerV3.WheelType.RWD, TaxiTrafficFrequency, scale);
            string dPath = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
            string tPath = $"{TrafficFolder}/Traffic_{def.Name}.prefab";
            try
            {
                sb.AppendLine("  " + BuildDrivable(def, dPath));
                var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(dPath);
                sb.AppendLine("  " + BuildTraffic(def, tPath, drivable));
                AssetDatabase.SaveAssets();

                VehicleDamageSettings settings = EnsureSettings();
                GameObject wreck = BuildWreck(def);
                RigDrivable(dPath, settings, wreck);
                RigTraffic(tPath, dPath, settings, wreck);
                AssetDatabase.SaveAssets();
                sb.AppendLine("  damage and drivers ok");
            }
            catch (System.Exception e)
            {
                return sb.AppendLine($"  FAILED {e.Message}\n{e.StackTrace}").ToString();
            }
            sb.AppendLine("  " + PedDriverBuilder.RepatchPrefabs(TaxiName));
            sb.AppendLine("  " + VehicleLampBuilder.BuildAll(TaxiName));
            sb.AppendLine("  " + AddTaxiToOpenScene(tPath, dPath));
            return sb.ToString();
        }

        // The rear-left wheel copied to rear-right, front-left and front-right. Right-side copies are turned 180 degrees so the
        // rim faces outwards.
        private static string AddMissingTaxiWheels(Transform root)
        {
            Transform rearLeft = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Wheel_Back_L") { rearLeft = t; break; }
            if (rearLeft == null) return "no Wheel_Back_L found; wheels left as they are";

            Vector3 p = rearLeft.localPosition;
            int added = 0;
            added += CopyWheel(rearLeft, root, "Wheel_Back_R", new Vector3(-p.x, p.y, p.z), true);
            added += CopyWheel(rearLeft, root, "Wheel_Front_L", new Vector3(p.x, p.y, TaxiFrontAxleZ), false);
            added += CopyWheel(rearLeft, root, "Wheel_Front_R", new Vector3(-p.x, p.y, TaxiFrontAxleZ), true);
            return $"added {added} wheels (rear axle z {p.z:F2}, front {TaxiFrontAxleZ:F2})";
        }

        private static int CopyWheel(Transform source, Transform root, string name, Vector3 localPosition, bool mirror)
        {
            foreach (Transform t in root) if (t.name == name) return 0;
            var copy = Object.Instantiate(source.gameObject, root);
            copy.name = name;
            copy.transform.localPosition = localPosition;
            copy.transform.localRotation = mirror ? Quaternion.Euler(0f, 180f, 0f) * source.localRotation : source.localRotation;
            copy.transform.localScale = source.localScale;
            return 1;
        }

        // Common in traffic (weight 3 of the mix), and pre-warmed for the takeover like the other drivables.
        private static string AddTaxiToOpenScene(string trafficPath, string drivablePath)
        {
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>(trafficPath);
            var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(drivablePath);
            var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            if (spawner == null || traffic == null) return "no traffic spawner in the open scene";
            var list = new List<TSTrafficSpawner.TSSpawnVehicles>();
            foreach (var entry in spawner.cars) if (entry.cars != null && entry.cars != traffic) list.Add(entry);
            list.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = traffic, frequency = TaxiTrafficFrequency });
            Undo.RecordObject(spawner, "Add taxi to traffic");
            spawner.cars = list.ToArray();
            EditorUtility.SetDirty(spawner);

            var takeover = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            if (takeover != null && drivable != null)
            {
                var so = new SerializedObject(takeover);
                var prop = so.FindProperty("prewarmedPrefabs");
                bool present = false;
                for (int i = 0; i < prop.arraySize; i++) present |= prop.GetArrayElementAtIndex(i).objectReferenceValue == drivable;
                if (!present)
                {
                    prop.arraySize++;
                    prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = drivable;
                    so.ApplyModifiedProperties();
                }
            }
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            return $"traffic: {list.Count} entries (taxi weight {TaxiTrafficFrequency})";
        }
    }
}
