using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The ambulance, built like the final cars (Drivable_Ambulance + Traffic_Ambulance, damage, wreck, seated driver, lamps)
    /// but on its own, so the other cars are not rebuilt. It keeps its white livery (no paint variants) and is rare in traffic.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Ambulance.
    /// </summary>
    public static partial class FinalCarBuilder
    {
        private const string AmbulanceFbx = "Assets/New Folder/Ambulance.fbm/Ambulance.fbx";
        private const string AmbulanceName = "Ambulance";
        private const float AmbulanceLength = 5.6f;
        private const int AmbulanceTrafficFrequency = 1;

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Ambulance")]
        public static void BuildAmbulanceMenu() => Debug.Log(BuildAmbulance());

        public static string BuildAmbulance()
        {
            var sb = new StringBuilder("[FinalCarBuilder] Ambulance:\n");
            EnsureFolder(DrivableFolder);
            EnsureFolder(TrafficFolder);
            EnsureFolder(MaterialFolder);
            materialCache.Clear();

            // Source prefab next to the other exported cars.
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(AmbulanceFbx);
            if (fbx == null) return sb.Append("  missing " + AmbulanceFbx).ToString();
            string sourcePath = $"{SourceFolder}/{AmbulanceName}.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            float length = 0f;
            try
            {
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
            float scale = length > 0.01f ? AmbulanceLength / length : 1f;
            sb.AppendLine($"  model {length:F2} m long -> scale {scale:F3}");

            //                    source          name           class          mass   km/h  torque brake  drive                                   freq  scale
            var def = new CarDef(AmbulanceName, AmbulanceName, CarClass.SUV, 3200f, 170f, 620f, 3600f, RCC_CarControllerV3.WheelType.RWD, AmbulanceTrafficFrequency, scale);
            string dPath = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
            string tPath = $"{TrafficFolder}/Traffic_{def.Name}.prefab";
            try
            {
                sb.AppendLine("  " + BuildDrivable(def, dPath));
                var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(dPath);
                sb.AppendLine("  " + BuildTraffic(def, tPath, drivable));
                AssetDatabase.SaveAssets();

                // Damage, wreck and seated drivers, for this car only.
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
            sb.AppendLine("  " + PedDriverBuilder.RepatchPrefabs());
            sb.AppendLine("  " + VehicleLampBuilder.BuildAll());
            sb.AppendLine("  " + AddAmbulanceToOpenScene(tPath, dPath));
            return sb.ToString();
        }

        // Rare in traffic (weight 1 of the mix), and pre-warmed for the takeover like the other drivables.
        private static string AddAmbulanceToOpenScene(string trafficPath, string drivablePath)
        {
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>(trafficPath);
            var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(drivablePath);
            var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            if (spawner == null || traffic == null) return "no traffic spawner in the open scene";
            var list = new List<TSTrafficSpawner.TSSpawnVehicles>();
            foreach (var entry in spawner.cars) if (entry.cars != null && entry.cars != traffic) list.Add(entry);
            list.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = traffic, frequency = AmbulanceTrafficFrequency });
            Undo.RecordObject(spawner, "Add ambulance to traffic");
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
            return $"traffic: {list.Count} entries (ambulance weight {AmbulanceTrafficFrequency})";
        }
    }
}
