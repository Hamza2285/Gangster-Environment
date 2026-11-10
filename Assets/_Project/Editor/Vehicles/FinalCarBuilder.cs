using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Turns the exported car prefabs in Assets/GameObject (left untouched) into game vehicles:
    /// - Drivable_[name] (RCC + RCCVehicleAdapter, enter / exit / carjack) built from Drivable_RCC_Jeep, and
    /// - Traffic_[name] (iTS TSTrafficAI + TSSimpleCar, swaps to its drivable twin when the player takes it) built from iTS' Car1.
    /// For each car the source model is copied, stripped to its visible meshes (missing scripts, colliders, rigidbodies, lights,
    /// flare / shadow sprites and the source game's hidden template parts are removed), its materials are converted to URP Lit
    /// copies under Assets/_Project/Materials/Vehicles/FinalCars, and the vehicle is re-fitted to it: wheels (found by shape),
    /// wheel colliders, body colliders, centre of mass, driver seat (from the steering wheel, floor and roof of the cabin),
    /// seated driver dummy, door hinge, enter / exit / interaction points, cameras, lights and handling per car class.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Final Cars / Assign Final Cars To Traffic.
    /// </summary>
    public static partial class FinalCarBuilder
    {
        private const string SourceFolder = "Assets/GameObject";
        private const string DrivableFolder = "Assets/_Project/Prefabs/Vehicles/Drivable";
        private const string TrafficFolder = "Assets/_Project/Prefabs/Vehicles/Traffic";
        private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/FinalCars";
        private const string DrivableTemplatePath = "Assets/_Project/Prefabs/Vehicles/Drivable_RCC_Jeep.prefab";
        private const string TrafficTemplatePath = "Assets/iTS/Vehicles/Car1.prefab";

        private enum CarClass { Sedan, Sports, Super, SUV }

        private sealed class CarDef
        {
            public readonly string Source;
            public readonly string Name;
            public readonly CarClass Class;
            public readonly float Mass;
            public readonly float MaxSpeed;
            public readonly float Torque;
            public readonly float Brake;
            public readonly RCC_CarControllerV3.WheelType Drive;
            public readonly int TrafficFrequency;
            public readonly float Scale;

            public CarDef(string source, string name, CarClass cls, float mass, float maxSpeed, float torque, float brake,
                RCC_CarControllerV3.WheelType drive, int trafficFrequency, float scale)
            {
                Source = source; Name = name; Class = cls; Mass = mass; MaxSpeed = maxSpeed; Torque = torque; Brake = brake;
                Drive = drive; TrafficFrequency = trafficFrequency; Scale = scale;
            }
        }

        private static readonly CarDef[] Cars =
        {
            // Scale: uniform factor on the source model only (the vehicle root stays at 1), so every wheel, collider, seat, door and
            // entry point below is fitted to the resized model. Chosen against a 1.75 m player: sedans and sports 4.4-4.9 m long and
            // 1.8-1.95 m wide without mirrors, SUVs a little bigger.
            //          source         name          class            mass   km/h  torque brake  drive                                    traffic freq  scale
            new CarDef("Car 1",      "Car1",       CarClass.Sports, 2100f, 250f, 540f, 3000f, RCC_CarControllerV3.WheelType.AWD, 2, 0.93f),
            new CarDef("Car 2",      "Car2",       CarClass.Sports, 1900f, 250f, 560f, 2900f, RCC_CarControllerV3.WheelType.RWD, 2, 1f),
            new CarDef("Car 3",      "Car3",       CarClass.Super,  1950f, 310f, 760f, 3600f, RCC_CarControllerV3.WheelType.AWD, 1, 0.97f),
            new CarDef("Car 4",      "Car4",       CarClass.Sedan,  2000f, 230f, 460f, 2800f, RCC_CarControllerV3.WheelType.RWD, 3, 0.96f),
            new CarDef("Car 5",      "Car5",       CarClass.SUV,    2300f, 215f, 520f, 3000f, RCC_CarControllerV3.WheelType.AWD, 2, 0.96f),
            new CarDef("G Wagon",    "GWagon",     CarClass.SUV,    2600f, 195f, 560f, 3300f, RCC_CarControllerV3.WheelType.AWD, 2, 1f),
            new CarDef("Khodge 2",   "Khodge2",    CarClass.Sports, 1600f, 280f, 600f, 3200f, RCC_CarControllerV3.WheelType.RWD, 2, 0.86f),
            new CarDef("Land Sover", "LandSover",  CarClass.SUV,    2500f, 200f, 520f, 3200f, RCC_CarControllerV3.WheelType.AWD, 2, 0.94f),
            new CarDef("New Car 1",  "NewCar1",    CarClass.Sports, 2100f, 250f, 540f, 3000f, RCC_CarControllerV3.WheelType.AWD, 2, 0.84f),
            new CarDef("New Car 3",  "NewCar3",    CarClass.Sports, 1900f, 250f, 560f, 2900f, RCC_CarControllerV3.WheelType.RWD, 2, 0.88f),
            new CarDef("Talenter",   "Talenter",   CarClass.Sedan,  1800f, 230f, 460f, 2700f, RCC_CarControllerV3.WheelType.RWD, 3, 0.96f),
            new CarDef("WMB",        "WMB",        CarClass.Sedan,  1950f, 240f, 480f, 2800f, RCC_CarControllerV3.WheelType.RWD, 3, 0.86f),
        };

        // Per class: suspension travel, centre of mass above the wheel centres, steering, high-speed steering, downforce.
        private static float Suspension(CarClass c) => c == CarClass.SUV ? 0.25f : c == CarClass.Super ? 0.12f : c == CarClass.Sports ? 0.16f : 0.2f;
        private static float ComHeight(CarClass c) => c == CarClass.SUV ? 0.12f : c == CarClass.Super ? -0.05f : c == CarClass.Sports ? 0f : 0.05f;
        private static float SteerAngle(CarClass c) => c == CarClass.Super ? 34f : c == CarClass.SUV ? 35f : c == CarClass.Sports ? 36f : 38f;
        private static float HighSpeedSteer(CarClass c) => c == CarClass.Super ? 5f : c == CarClass.Sports ? 6f : c == CarClass.SUV ? 8f : 7f;
        private static float DownForce(CarClass c) => c == CarClass.Super ? 45f : c == CarClass.Sports ? 30f : c == CarClass.SUV ? 18f : 22f;

        // Cabin fit (root space, metres, y 0 = ground under the wheels).
        private const float WheelAboveHips = 0.32f;     // steering wheel hub above the driver's hip joint
        private const float HipsBehindWheel = 0.47f;    // hip joint behind the hub
        private const float HipsAboveFloorMin = 0.08f;  // lowest the hips may sit above the cabin floor
        private const float RoofClearance = 0.04f;      // gap kept between the seated dummy's crown and the roof
        private const float CrownAboveHeadBone = 0.17f; // mixamorig:Head bone to the top of the head (unscaled)

        private static readonly Dictionary<Material, Material> materialCache = new Dictionary<Material, Material>();
        private static List<(Vector3 a, Vector3 b, Vector3 c)> probeTriangles;

        // ---------------------------------------------------------------------------------------------------------------
        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Final Cars")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        [MenuItem("Tools/OpenworldCrime/Vehicles/Assign Final Cars To Traffic")]
        public static void AssignTrafficMenu() => Debug.Log(AssignToOpenScene());

        [MenuItem("Tools/OpenworldCrime/Vehicles/Assign Handling Profiles To Drivable Cars")]
        public static void AssignHandlingMenu() => Debug.Log(AssignHandlingProfiles());

        // Driving feel per class (VehicleHandlingProfile assets, applied by RCCVehicleAdapter at runtime): tune these, no rebuild.
        private const string HandlingFolder = "Assets/_Project/Data/Vehicles/Handling";
        private static string HandlingProfilePath(CarClass c) =>
            $"{HandlingFolder}/VehicleHandling_{(c == CarClass.SUV ? "SUV" : c == CarClass.Sedan ? "Sedan" : "Sports")}.asset";

        private static VehicleHandlingProfile LoadHandlingProfile(CarClass c)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VehicleHandlingProfile>(HandlingProfilePath(c));
            if (profile == null) Debug.LogWarning($"[FinalCarBuilder] Handling profile missing: {HandlingProfilePath(c)}; the car keeps its RCC values.");
            return profile;
        }

        /// <summary>Puts the class handling profile on every existing drivable final car (no rebuild).</summary>
        public static string AssignHandlingProfiles()
        {
            var sb = new StringBuilder("[FinalCarBuilder] Handling profiles:\n");
            foreach (CarDef def in Cars)
            {
                string path = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { sb.AppendLine($"  {path}: missing"); continue; }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var adapter = root.GetComponent<RCCVehicleAdapter>();
                    VehicleHandlingProfile profile = LoadHandlingProfile(def.Class);
                    if (adapter == null || profile == null) { sb.AppendLine($"  {path}: skipped"); continue; }
                    adapter.EditorSetHandlingProfile(profile);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    sb.AppendLine($"  Drivable_{def.Name}: {profile.name}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return sb.ToString();
        }

        /// <summary>Builds the drivable and traffic prefab of every car. Returns a per-car report.</summary>
        public static string BuildAll()
        {
            EnsureFolder(DrivableFolder);
            EnsureFolder(TrafficFolder);
            EnsureFolder(MaterialFolder);
            materialCache.Clear();
            var sb = new StringBuilder("[FinalCarBuilder] Build:\n");
            foreach (CarDef def in Cars)
            {
                try
                {
                    string dPath = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
                    string dLog = BuildDrivable(def, dPath);
                    var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(dPath);
                    string tLog = BuildTraffic(def, $"{TrafficFolder}/Traffic_{def.Name}.prefab", drivable);
                    sb.AppendLine($"  {def.Source}: {dLog} | {tLog}");
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"  {def.Source}: FAILED {e.Message}\n{e.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            sb.Append(BuildCombat());
            sb.AppendLine(PedDriverBuilder.RepatchPrefabs());
            // Last, on the finished prefabs: the colour variant slots.
            sb.AppendLine(FinalCarPaint.WirePrefabs());
            // The lamps (emission per car, front / rear split meshes, VehicleLights) on the finished prefabs.
            sb.AppendLine(VehicleLampBuilder.BuildAll());
            return sb.ToString();
        }

        /// <summary>
        /// Puts the traffic prefabs (mixed by their frequency) on the open scene's iTS spawner and the drivable twins on the
        /// takeover manager's pre-warm list. The spawner's car count is not changed.
        /// </summary>
        public static string AssignToOpenScene()
        {
            var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            var takeover = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            var list = new List<TSTrafficSpawner.TSSpawnVehicles>();
            var drivables = new List<RCCVehicleAdapter>();
            foreach (CarDef def in Cars)
            {
                var traffic = AssetDatabase.LoadAssetAtPath<GameObject>($"{TrafficFolder}/Traffic_{def.Name}.prefab");
                var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>($"{DrivableFolder}/Drivable_{def.Name}.prefab");
                if (traffic == null || drivable == null) continue;
                list.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = traffic, frequency = def.TrafficFrequency });
                drivables.Add(drivable);
            }
            if (spawner != null)
            {
                // Only the stock iTS models are replaced; project vehicles on the spawner (the police patrol car) stay.
                foreach (TSTrafficSpawner.TSSpawnVehicles existing in spawner.cars)
                {
                    if (existing.cars == null) continue;
                    string path = AssetDatabase.GetAssetPath(existing.cars);
                    if (!path.StartsWith("Assets/iTS/") && !path.StartsWith(TrafficFolder + "/")) list.Add(existing);
                }
                Undo.RecordObject(spawner, "Assign final traffic cars");
                spawner.cars = list.ToArray();
                EditorUtility.SetDirty(spawner);
            }
            if (takeover != null)
            {
                var so = new SerializedObject(takeover);
                var prop = so.FindProperty("prewarmedPrefabs");
                prop.arraySize = drivables.Count;
                for (int i = 0; i < drivables.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = drivables[i];
                so.ApplyModifiedProperties();
            }
            return $"[FinalCarBuilder] Traffic spawner: {(spawner != null ? list.Count + " cars" : "missing")}, takeover pre-warm: {(takeover != null ? drivables.Count + " drivables" : "no TrafficTakeoverManager")}";
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Model
        // ---------------------------------------------------------------------------------------------------------------
        private sealed class ModelInfo
        {
            public Transform Mesh;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly Renderer[] Wheels = new Renderer[4];          // FL, FR, RL, RR
            public readonly Vector3[] WheelCenters = new Vector3[4];      // root space
            public readonly float[] WheelRadius = new float[4];
            public readonly float[] WheelWidth = new float[4];
            public Bounds Body;                                           // root space, wheels excluded
            public Renderer Steering;
            public Vector3 SteeringCenter;
            public string SteeringSource;
            public Renderer DriverDoor;
            public MeshFilter MainBody;
            public float AxleY => (WheelCenters[0].y + WheelCenters[2].y) * 0.5f;
            public float AxleMidZ => (WheelCenters[0].z + WheelCenters[2].z) * 0.5f;
        }

        /// <summary>Copies the source car under parent, strips it to its visible meshes, converts its materials and centres it
        /// on the root (wheels on y = 0, body centred on x / z, facing +Z).</summary>
        private static ModelInfo PlaceModel(CarDef def, Transform parent, Transform root, int layer)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourceFolder}/{def.Source}.prefab");
            if (src == null) throw new System.Exception($"source prefab {SourceFolder}/{def.Source}.prefab not found");
            var inst = (GameObject)Object.Instantiate(src, parent);
            inst.name = def.Source;
            Transform t = inst.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = src.transform.localScale * def.Scale;

            // The source game kept a hidden steering wheel per car (for its driver IK) when the wheel is part of the body mesh.
            Vector3? steeringHint = FindHiddenSteering(t);
            Clean(t, layer);
            RemapMaterials(t);

            ModelInfo info = Analyze(t, root, steeringHint);
            if (info.SteeringCenter.z < info.Body.center.z && info.SteeringSource != "estimated")
            {
                // Faces -Z: turn it around.
                t.localRotation = Quaternion.Euler(0f, 180f, 0f);
                info = Analyze(t, root, steeringHint);
            }
            Vector3 shift = new Vector3(-info.Body.center.x, -Ground(info), -info.Body.center.z);
            t.position += root.TransformVector(shift);
            return Analyze(t, root, steeringHint);
        }

        private static float Ground(ModelInfo info)
        {
            float g = float.PositiveInfinity;
            for (int w = 0; w < 4; w++) g = Mathf.Min(g, info.WheelCenters[w].y - info.WheelRadius[w]);
            return g;
        }

        // Steering hint in the instance's local space (it survives the model being moved), from a disabled "SteeringWheel" mesh.
        private static Vector3? FindHiddenSteering(Transform inst)
        {
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name != "SteeringWheel" || IsVisible(r, inst)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                return inst.InverseTransformPoint(r.transform.TransformPoint(mf.sharedMesh.bounds.center));
            }
            return null;
        }

        private static bool IsVisible(Renderer r, Transform stop)
        {
            if (!r.enabled) return false;
            for (Transform p = r.transform; p != null; p = p.parent)
            {
                if (!p.gameObject.activeSelf) return false;
                if (p == stop) break;
            }
            return true;
        }

        private static bool IsJunk(Renderer r)
        {
            string n = r.name.ToLowerInvariant();
            if (n.Contains("flare") || n.Contains("shadow") || n.Contains("headlights") || n.Contains("taillights") || n == "cube") return true;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name.Contains("Particles")) return true;
            return false;
        }

        // Keeps only visible mesh renderers (and the transforms above them) with nothing but Transform / MeshFilter / MeshRenderer.
        private static void Clean(Transform inst, int layer)
        {
            var keep = new HashSet<Transform>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !IsVisible(r, inst) || IsJunk(r)) continue;
                keep.Add(r.transform);
            }
            var needed = new HashSet<Transform>();
            foreach (Transform k in keep)
                for (Transform p = k; p != null; p = p.parent) { needed.Add(p); if (p == inst) break; }

            Transform[] all = inst.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                if (all[i] == null || all[i] == inst) continue;
                if (!needed.Contains(all[i])) Object.DestroyImmediate(all[i].gameObject);
            }

            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                // Dependants first (scripts, joints), rigidbodies last.
                for (int pass = 0; pass < 3; pass++)
                {
                    foreach (var c in go.GetComponents<Component>())
                    {
                        if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                        if (pass == 0 && !(c is MonoBehaviour)) continue;
                        if (pass == 1 && !(c is Joint)) continue;
                        Object.DestroyImmediate(c);
                    }
                }
                if (!keep.Contains(t))
                {
                    var mr = go.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr);
                    var mf = go.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf);
                }
                go.SetActive(true);
                go.layer = layer;
                go.tag = "Untagged";
            }
        }

        private static ModelInfo Analyze(Transform inst, Transform root, Vector3? steeringHint)
        {
            var info = new ModelInfo { Mesh = inst };
            info.Renderers.AddRange(inst.GetComponentsInChildren<MeshRenderer>(true));

            // Wheels by shape: a disc (height ~ length, narrow), 45-110 cm across; the four lowest ones.
            var candidates = new List<(Renderer r, Bounds b)>();
            foreach (var r in info.Renderers)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("steer") || n.Contains("sterring")) continue;
                Bounds b = RootBounds(r, root);
                Vector3 s = b.size;
                if (s.y < 0.45f || s.y > 1.1f || Mathf.Abs(s.y - s.z) > 0.15f * s.y || s.x > 0.65f * s.y) continue;
                candidates.Add((r, b));
            }
            if (candidates.Count < 4) throw new System.Exception($"only {candidates.Count} wheel meshes found in {inst.name}");
            candidates.Sort((p, q) => p.b.center.y.CompareTo(q.b.center.y));
            if (candidates.Count > 4) candidates.RemoveRange(4, candidates.Count - 4);
            float cx = 0f, cz = 0f;
            foreach (var c in candidates) { cx += c.b.center.x * 0.25f; cz += c.b.center.z * 0.25f; }
            foreach (var c in candidates)
            {
                int slot = (c.b.center.z >= cz ? 0 : 2) + (c.b.center.x < cx ? 0 : 1);
                if (info.Wheels[slot] != null) throw new System.Exception($"wheel layout of {inst.name} not recognised");
                info.Wheels[slot] = c.r;
                info.WheelCenters[slot] = c.b.center;
                info.WheelRadius[slot] = Mathf.Max(c.b.extents.y, c.b.extents.z);
                info.WheelWidth[slot] = c.b.size.x;
            }

            var wheelSet = new HashSet<Renderer>(info.Wheels);
            bool init = false;
            foreach (var r in info.Renderers)
            {
                if (wheelSet.Contains(r)) continue;
                Bounds b = RootBounds(r, root);
                if (!init) { info.Body = b; init = true; } else info.Body.Encapsulate(b);
            }

            // Steering wheel: its own mesh, else the source game's hidden one, else estimated from the body.
            foreach (var r in info.Renderers)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("steer") || n.Contains("sterring")) { info.Steering = r; break; }
            }
            Bounds body = info.Body;
            if (info.Steering != null) { info.SteeringCenter = RootBounds(info.Steering, root).center; info.SteeringSource = "mesh"; }
            else if (steeringHint.HasValue) { info.SteeringCenter = root.InverseTransformPoint(inst.TransformPoint(steeringHint.Value)); info.SteeringSource = "hint"; }
            else
            {
                info.SteeringCenter = new Vector3(body.center.x - body.extents.x * 0.36f, body.min.y + body.size.y * 0.62f, body.min.z + body.size.z * 0.58f);
                info.SteeringSource = "estimated";
            }

            // Driver door: the front-most door panel on the left (-X) side.
            float bestZ = float.NegativeInfinity;
            float bestVolume = 0f;
            foreach (var r in info.Renderers)
            {
                if (wheelSet.Contains(r) || !r.name.ToLowerInvariant().Contains("door")) continue;
                Bounds b = RootBounds(r, root);
                if (b.center.x > body.center.x - body.extents.x * 0.4f || b.size.x > body.size.x * 0.4f) continue;
                if (b.size.z < 0.6f || b.size.z > 2f || b.size.y < 0.5f) continue;
                if (b.center.z > bestZ) { bestZ = b.center.z; info.DriverDoor = r; }
            }

            // Main body: biggest painted part (dented by RCC damage).
            foreach (var r in info.Renderers)
            {
                if (wheelSet.Contains(r) || r == info.DriverDoor || r == info.Steering) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("glass") || n.Contains("light") || n.Contains("interior")) continue;
                Vector3 s = RootBounds(r, root).size;
                float v = s.x * s.y * s.z;
                if (v > bestVolume) { bestVolume = v; info.MainBody = r.GetComponent<MeshFilter>(); }
            }
            return info;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Materials
        // ---------------------------------------------------------------------------------------------------------------
        private static void RemapMaterials(Transform inst)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material urp = ToUrp(mats[i]);
                    if (urp != mats[i]) { mats[i] = urp; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Built-in / legacy / exported custom shaders -> URP Lit copy (same name) in the material folder. Re-runnable.
        private static Material ToUrp(Material src)
        {
            if (src == null || src.shader == null) return src;
            if (src.shader.name.StartsWith("Universal Render Pipeline")) return src;
            if (materialCache.TryGetValue(src, out Material cached)) return cached;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            string path = $"{MaterialFolder}/{Sanitize(src.name)}.mat";
            var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (dst == null)
            {
                dst = new Material(lit) { name = src.name };
                AssetDatabase.CreateAsset(dst, path);
            }
            dst.shader = lit;

            string shaderName = src.shader.name;
            string n = src.name.ToLowerInvariant();
            Texture albedo = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            Color color = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            Texture normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            float mode = src.HasProperty("_Mode") ? src.GetFloat("_Mode") : 0f;
            bool glass = n.Contains("glass") || n.Contains("wind") || n.Contains("window") || shaderName.Contains("Transparent") || mode >= 2f;
            bool cutout = !glass && (shaderName.Contains("Cutout") || Mathf.Approximately(mode, 1f));
            bool reflective = shaderName.Contains("Reflective") || shaderName.Contains("OG_Car");

            dst.SetTexture("_BaseMap", albedo);
            dst.SetTexture("_BumpMap", normal);
            if (normal != null) dst.EnableKeyword("_NORMALMAP"); else dst.DisableKeyword("_NORMALMAP");
            float metallic = src.HasProperty("_Metallic") ? Mathf.Min(src.GetFloat("_Metallic"), 0.7f) : reflective ? 0.35f : 0f;
            float smooth = src.HasProperty("_Glossiness") ? Mathf.Clamp(src.GetFloat("_Glossiness"), 0.15f, 0.85f) : reflective ? 0.72f : 0.3f;

            // Lamps keep the source's emission mask but start unlit: VehicleLights (set up by VehicleLampBuilder) switches the
            // emission per car (headlights, tail / brake lights). Nothing else glows.
            bool lamp = n.Contains("light") && src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap") != null;
            dst.SetTexture("_EmissionMap", lamp ? src.GetTexture("_EmissionMap") : null);
            dst.SetColor("_EmissionColor", Color.black);
            dst.DisableKeyword("_EMISSION");
            dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (glass)
            {
                color.a = Mathf.Clamp(color.a < 0.99f ? color.a : 0.45f, 0.3f, 0.7f);
                dst.SetColor("_BaseColor", color);
                dst.SetFloat("_Metallic", 0f);
                dst.SetFloat("_Smoothness", 0.95f);
                dst.SetFloat("_Surface", 1f);
                dst.SetFloat("_Blend", 0f);
                dst.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                dst.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                dst.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                dst.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                dst.SetFloat("_ZWrite", 0f);
                dst.SetFloat("_AlphaClip", 0f);
                dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                dst.DisableKeyword("_ALPHATEST_ON");
                dst.SetOverrideTag("RenderType", "Transparent");
                dst.renderQueue = (int)RenderQueue.Transparent;
                dst.SetShaderPassEnabled("ShadowCaster", false);
                dst.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                color.a = 1f;
                dst.SetColor("_BaseColor", color);
                dst.SetFloat("_Metallic", metallic);
                dst.SetFloat("_Smoothness", smooth);
                dst.SetFloat("_Surface", 0f);
                dst.SetFloat("_SrcBlend", (float)BlendMode.One);
                dst.SetFloat("_DstBlend", (float)BlendMode.Zero);
                dst.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                dst.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                dst.SetFloat("_ZWrite", 1f);
                dst.SetFloat("_AlphaClip", cutout ? 1f : 0f);
                dst.SetFloat("_Cutoff", 0.5f);
                dst.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (cutout) dst.EnableKeyword("_ALPHATEST_ON"); else dst.DisableKeyword("_ALPHATEST_ON");
                dst.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                dst.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
                dst.SetShaderPassEnabled("ShadowCaster", true);
                dst.SetShaderPassEnabled("DepthOnly", true);
            }
            dst.enableInstancing = true;
            FinalCarPaint.ApplyIfBody(dst);
            EditorUtility.SetDirty(dst);
            materialCache[src] = dst;
            return dst;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Drivable
        // ---------------------------------------------------------------------------------------------------------------
        private static string BuildDrivable(CarDef def, string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DrivableTemplatePath);
            try
            {
                root.name = "Drivable_" + def.Name;
                Transform rt = root.transform;
                int layer = root.layer;

                // Template seat geometry (the CarPlayer dummy is calibrated to the real player's seated pose).
                Transform seatT = rt.Find("DriverSeat");
                Transform dummy = seatT.Find("CarPlayer");
                var bones = Bones(dummy);
                Transform hips = bones["mixamorig:Hips"], head = bones["mixamorig:Head"];
                Vector3 hipsOffset = rt.InverseTransformPoint(hips.position) - seatT.localPosition;
                float crownAboveHips = rt.InverseTransformPoint(head.position).y + CrownAboveHeadBone * dummy.lossyScale.y
                                       - rt.InverseTransformPoint(hips.position).y;

                // The Jeep's RCC lights live under its body mesh: keep them, drop the Jeep model.
                Transform jeep = rt.Find("Jeep");
                var lights = NewChild("Lights", rt, layer);
                foreach (var light in jeep.GetComponentsInChildren<Light>(true))
                {
                    if (light.GetComponentInParent<RCC_Exhaust>() != null) continue;
                    light.transform.SetParent(lights, true);
                    foreach (var emission in light.GetComponents<RCC_LightEmission>()) Object.DestroyImmediate(emission);
                }
                Object.DestroyImmediate(jeep.gameObject);

                Transform model = NewChild("Model", rt, layer);
                ModelInfo info = PlaceModel(def, model, rt, layer);
                Bounds body = info.Body;

                // Wheels: visuals under RCC's wheel model holders, colliders fitted to them.
                string[] holders = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
                Transform modelsRoot = rt.Find("Wheel Models"), collidersRoot = rt.Find("Wheel Colliders");
                float susp = Suspension(def.Class);
                for (int w = 0; w < 4; w++)
                {
                    Transform holder = modelsRoot.Find(holders[w]);
                    var oldRenderer = holder.GetComponent<MeshRenderer>(); if (oldRenderer != null) Object.DestroyImmediate(oldRenderer);
                    var oldFilter = holder.GetComponent<MeshFilter>(); if (oldFilter != null) Object.DestroyImmediate(oldFilter);
                    for (int c = holder.childCount - 1; c >= 0; c--) Object.DestroyImmediate(holder.GetChild(c).gameObject);
                    holder.position = rt.TransformPoint(info.WheelCenters[w]);
                    holder.rotation = rt.rotation;
                    holder.localScale = Vector3.one;
                    info.Wheels[w].transform.SetParent(holder, true);

                    var wc = collidersRoot.Find(holders[w]).GetComponent<WheelCollider>();
                    FitWheelCollider(wc, info.WheelCenters[w], info.WheelRadius[w], susp, def.Mass, rt);
                    var rccWheel = wc.GetComponent<RCC_WheelCollider>();
                    rccWheel.width = Mathf.Clamp(info.WheelWidth[w], 0.15f, 0.45f);
                    rccWheel.offset = 0f;
                }
                RemoveEmpty(info.Mesh);

                FitBodyColliders(model, body, rt, layer);

                // Seat from the cabin (steering wheel, floor, roof), dummy refitted (hands on the wheel, feet on the floor).
                CabinFit cabin = FitCabin(info, model, rt, crownAboveHips);
                seatT.localRotation = Quaternion.identity;
                seatT.localPosition = cabin.Hips - hipsOffset;
                Vector3 seat = seatT.localPosition;
                FitDummy(bones, rt, cabin);

                DoorGeometry door0 = DoorGeometryOf(info, body, seat, rt);
                SetPoint(rt, "EnterPoint", new Vector3(door0.OuterX - 0.45f, 0.02f, door0.HandleZ - 0.05f), 90f);
                SetPoint(rt, "ExitPoint", new Vector3(door0.OuterX - 0.60f, 0.02f, seat.z + 0.05f), 270f);
                SetPoint(rt, "AlternateExitPoint", new Vector3(body.max.x + 0.60f, 0.02f, seat.z + 0.05f), 90f);
                SetPoint(rt, "InteractionPoint", new Vector3(door0.OuterX - 0.10f, 0.52f, door0.CenterZ), 0f);
                SetPoint(rt, "CabinFloor", new Vector3(door0.OuterX + 0.12f, cabin.FloorY, cabin.Hips.z + 0.2f), 0f);
                FitTriggerBox(rt.Find("InteractionZone"), new Vector3(0f, 1.04f, body.center.z),
                    new Vector3(body.size.x + 3.9f, Mathf.Max(2.4f, body.size.y + 0.6f), body.size.z + 2.8f), rt);

                rt.Find("COM").localPosition = new Vector3(0f, info.AxleY + ComHeight(def.Class), info.AxleMidZ);
                Transform hood = rt.Find("HoodCamera");
                if (hood != null) hood.localPosition = new Vector3(cabin.Hips.x, Mathf.Min(cabin.Wheel.y + 0.42f, cabin.RoofY - 0.12f), cabin.Wheel.z + 0.05f);
                Transform wheelCam = rt.Find("WheelCamera");
                if (wheelCam != null) wheelCam.localPosition = new Vector3(body.max.x + 0.7f, 0.55f, body.min.z - 0.6f);
                Transform exhausts = rt.Find("Exhausts");
                if (exhausts != null) foreach (Transform e in exhausts) e.localPosition = new Vector3(-body.extents.x * 0.45f, 0.3f, body.min.z + 0.05f);
                FitLights(lights, body);

                // Steering wheel pivot (RCC turns it), driver door hinge, culling.
                var rcc = root.GetComponent<RCC_CarControllerV3>();
                if (info.Steering != null)
                {
                    rcc.SteeringWheel = MakeSteeringPivot(info.Steering, rt);
                    rcc.steeringWheelRotateAround = RCC_CarControllerV3.SteeringWheelRotateAround.ZAxis;
                }
                else rcc.SteeringWheel = null;
                VehicleDoor door = MakeDoor(info, rt);
                SetupCulling(root, info);

                var adapter = root.GetComponent<RCCVehicleAdapter>();
                var so = new SerializedObject(adapter);
                so.FindProperty("driverDoor").objectReferenceValue = door;
                so.ApplyModifiedPropertiesWithoutUndo();
                adapter.EditorSetHandlingProfile(LoadHandlingProfile(def.Class));

                root.GetComponent<Rigidbody>().mass = def.Mass;
                ApplyHandling(rcc, def, body);
                // RCC dents only the main body (its mesh must be readable in builds); without a list it would copy every mesh.
                if (info.MainBody != null)
                {
                    MakeReadable(info.MainBody.sharedMesh);
                    rcc.deformableMeshFilters = new[] { info.MainBody };
                }
                else rcc.useDamage = false;

                SetTagRecursive(rt, "Untagged");
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"drivable ok (L {body.size.z:F2} W {body.size.x:F2} H {body.size.y:F2}, wheel r {info.WheelRadius[0]:F2}, " +
                       $"steering {info.SteeringSource}, door {(door != null ? info.DriverDoor.name : "none")}, hips {cabin.Hips.y:F2} floor {cabin.FloorY:F2} " +
                       $"roof {(float.IsInfinity(cabin.RoofY) ? "open" : cabin.RoofY.ToString("F2"))} headroom {cabin.Headroom:F2}{cabin.Note})";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void FitWheelCollider(WheelCollider wc, Vector3 wheelCenter, float radius, float suspension, float mass, Transform root)
        {
            wc.radius = radius;
            wc.center = Vector3.zero;
            wc.suspensionDistance = suspension;
            JointSpring spring = wc.suspensionSpring;
            spring.spring = mass * 25f;
            spring.damper = mass * 1.2f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            float rest = wc.suspensionDistance * (1f - spring.targetPosition);
            wc.transform.position = root.TransformPoint(wheelCenter + Vector3.up * rest);
            wc.transform.rotation = root.rotation;
        }

        // Two boxes: the lower body (full footprint) and the cabin (glasshouse). Solid, on the vehicle layer: they stop bullets,
        // break props and carry the car on its roof.
        private static void FitBodyColliders(Transform holder, Bounds body, Transform root, int layer)
        {
            Transform cols = NewChild("Colliders", holder, layer);
            float bottom = Mathf.Min(0.3f, body.size.y * 0.15f);
            float belt = body.min.y + body.size.y * 0.55f;
            var lower = cols.gameObject.AddComponent<BoxCollider>();
            SetBox(lower, new Vector3(body.center.x, (bottom + belt) * 0.5f, body.center.z), new Vector3(body.size.x * 0.96f, belt - bottom, body.size.z * 0.98f), cols, root);
            var cabin = cols.gameObject.AddComponent<BoxCollider>();
            float top = body.max.y - 0.03f;
            SetBox(cabin, new Vector3(body.center.x, (belt + top) * 0.5f, body.center.z - body.size.z * 0.04f), new Vector3(body.size.x * 0.82f, top - belt, body.size.z * 0.55f), cols, root);
        }

        private static void ApplyHandling(RCC_CarControllerV3 rcc, CarDef def, Bounds body)
        {
            rcc.maxspeed = def.MaxSpeed;
            rcc.maxEngineTorque = def.Torque;
            rcc.brakeTorque = def.Brake;
            rcc.steerAngle = SteerAngle(def.Class);
            rcc.highspeedsteerAngle = HighSpeedSteer(def.Class);
            rcc.highspeedsteerAngleAtspeed = def.MaxSpeed * 0.8f;
            rcc.downForce = DownForce(def.Class);
            rcc.wheelTypeChoise = def.Drive;
            rcc.engineSoundPosition = new Vector3(0f, 0.6f, body.max.z - 0.9f);
            rcc.turboSoundPosition = rcc.engineSoundPosition;
            rcc.exhaustSoundPosition = new Vector3(0f, 0.3f, body.min.z + 0.2f);
            rcc.windSoundPosition = new Vector3(0f, body.max.y, body.max.z - 0.5f);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Traffic
        // ---------------------------------------------------------------------------------------------------------------
        private static string BuildTraffic(CarDef def, string path, RCCVehicleAdapter drivable)
        {
            if (drivable == null) throw new System.Exception("drivable prefab missing");
            GameObject root = PrefabUtility.LoadPrefabContents(TrafficTemplatePath);
            try
            {
                root.name = "Traffic_" + def.Name;
                Transform rt = root.transform;
                Transform center = rt.Find("Center");
                int modelLayer = center.gameObject.layer;
                for (int c = center.childCount - 1; c >= 0; c--) Object.DestroyImmediate(center.GetChild(c).gameObject);
                center.localPosition = Vector3.zero;
                center.localRotation = Quaternion.identity;
                center.localScale = Vector3.one;
                Transform model = NewChild("Model", center, modelLayer);
                ModelInfo info = PlaceModel(def, model, rt, modelLayer);
                Bounds body = info.Body;

                // iTS wheels: the WheelCollider transform carries the collider, its Tire child (TSSimpleCar_Wheel) spins and steers.
                string[] wheels = { "WheelFL1", "WheelFR4", "WheelRL3", "WheelRR2" };
                for (int w = 0; w < 4; w++)
                {
                    Transform wt = rt.Find(wheels[w]);
                    var wc = wt.GetComponent<WheelCollider>();
                    wc.radius = info.WheelRadius[w];
                    wc.center = Vector3.zero;
                    float rest = wc.suspensionDistance * (1f - wc.suspensionSpring.targetPosition);
                    wt.position = rt.TransformPoint(info.WheelCenters[w] + Vector3.up * rest);
                    wt.rotation = rt.rotation;
                    Transform tire = wt.Find("Tire");
                    for (int c = tire.childCount - 1; c >= 0; c--) Object.DestroyImmediate(tire.GetChild(c).gameObject);
                    tire.position = rt.TransformPoint(info.WheelCenters[w]);
                    tire.rotation = rt.rotation;
                    tire.localScale = Vector3.one;
                    info.Wheels[w].transform.SetParent(tire, true);
                }
                RemoveEmpty(info.Mesh);
                FitBodyColliders(model, body, rt, modelLayer);

                rt.Find("CoG").localPosition = new Vector3(0f, info.AxleY - 0.1f, info.AxleMidZ);
                // iTS brake lamps and blinkers at the car's rear corners.
                float lampY = Mathf.Clamp(body.size.y * 0.5f, 0.6f, 1.1f);
                SetLocal(rt.Find("BrakeLight1"), new Vector3(-body.extents.x * 0.7f, lampY, body.min.z - 0.01f));
                SetLocal(rt.Find("BrakeLight2"), new Vector3(body.extents.x * 0.7f, lampY, body.min.z - 0.01f));
                SetLocal(rt.Find("HaloLight-Left"), new Vector3(-body.extents.x * 0.85f, lampY, body.min.z));
                SetLocal(rt.Find("HaloLight-Right"), new Vector3(body.extents.x * 0.85f, lampY, body.min.z));

                var simple = root.GetComponent<TSSimpleCar>();
                simple.maxSpeed = def.Class == CarClass.Super || def.Class == CarClass.Sports ? 55f : 50f;

                // Player-facing points: the same places as on the drivable twin, so the swap is seamless.
                Transform seat = drivable.GetDriverSeat();
                DoorGeometry door0 = DoorGeometryOf(info, body, seat.localPosition, rt);
                Transform interaction = SetOrCreatePoint(rt, "InteractionPoint", new Vector3(door0.OuterX - 0.10f, 0.52f, door0.CenterZ), 0f);
                Transform enter = SetOrCreatePoint(rt, "EnterPoint", drivable.GetEnterPoint().localPosition, drivable.GetEnterPoint().localEulerAngles.y);
                Transform steal = SetOrCreatePoint(rt, "StealPoint", new Vector3(door0.OuterX - 0.45f, 0.02f, door0.HandleZ - 0.15f), 72f);
                Transform anchor = SetOrCreatePoint(rt, "DrivableAnchor", Vector3.zero, 0f);
                Transform zone = rt.Find("InteractionZone");
                if (zone == null)
                {
                    zone = NewChild("InteractionZone", rt, LayerMask.NameToLayer("Triggers"));
                    zone.gameObject.AddComponent<BoxCollider>().isTrigger = true;
                }
                FitTriggerBox(zone, new Vector3(0f, 1.04f, body.center.z), new Vector3(body.size.x + 3.9f, Mathf.Max(2.4f, body.size.y + 0.6f), body.size.z + 2.8f), rt);

                var adapter = root.GetComponent<TrafficVehicleAdapter>();
                if (adapter == null) adapter = root.AddComponent<TrafficVehicleAdapter>();
                var so = new SerializedObject(adapter);
                so.FindProperty("trafficAI").objectReferenceValue = root.GetComponent<TSTrafficAI>();
                so.FindProperty("body").objectReferenceValue = root.GetComponent<Rigidbody>();
                so.FindProperty("simpleCar").objectReferenceValue = simple;
                so.FindProperty("drivablePrefab").objectReferenceValue = drivable;
                so.FindProperty("drivableAnchor").objectReferenceValue = anchor;
                so.FindProperty("interactionPoint").objectReferenceValue = interaction;
                so.FindProperty("enterPoint").objectReferenceValue = enter;
                so.FindProperty("stealPoint").objectReferenceValue = steal;
                so.ApplyModifiedPropertiesWithoutUndo();

                SetupCulling(root, info);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return "traffic ok";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Cabin
        // ---------------------------------------------------------------------------------------------------------------
        private sealed class CabinFit
        {
            public Vector3 Hips;
            public Vector3 Wheel;
            public float FloorY;
            public float RoofY;
            public float Headroom;
            public string Note = "";
        }

        private sealed class DoorGeometry { public float OuterX, HandleZ, CenterZ; }

        private static CabinFit FitCabin(ModelInfo info, Transform model, Transform root, float crownAboveHips)
        {
            BuildProbe(model, root, info);
            var fit = new CabinFit { Wheel = info.SteeringCenter };
            float hipsX = fit.Wheel.x, hipsZ = fit.Wheel.z - HipsBehindWheel;

            // Floor: lowest surface crossed in the footwell above the underbody, well below the steering wheel.
            var floors = new List<float>();
            foreach (float dz in new[] { -0.15f, 0.05f })
                foreach (float dx in new[] { -0.12f, 0f, 0.12f })
                {
                    List<float> column = ColumnCrossings(hipsX + dx, fit.Wheel.z + dz);
                    if (column.Count == 0) continue;
                    float lowest = column[column.Count - 1];
                    float best = float.PositiveInfinity;
                    foreach (float y in column) if (y >= lowest + 0.08f && y < fit.Wheel.y - 0.25f && y < best) best = y;
                    if (!float.IsPositiveInfinity(best)) floors.Add(best);
                }
            floors.Sort();
            fit.FloorY = floors.Count > 0 ? floors[0] : Mathf.Max(0.25f, fit.Wheel.y - 0.6f);

            float hipsY = Mathf.Max(fit.Wheel.y - WheelAboveHips, fit.FloorY + HipsAboveFloorMin);
            fit.RoofY = RoofAbove(hipsX, hipsZ - 0.05f, hipsY + 0.5f);
            fit.Headroom = fit.RoofY - (hipsY + crownAboveHips);
            if (fit.Headroom < RoofClearance)
            {
                // Lower the seat towards the floor until the head clears the roof.
                float drop = Mathf.Min(RoofClearance - fit.Headroom, Mathf.Max(0f, hipsY - (fit.FloorY + 0.04f)));
                hipsY -= drop;
                fit.Headroom += drop;
                if (drop > 0.005f) fit.Note += $", seat lowered {drop:F2}";
                if (fit.Headroom < RoofClearance) fit.Note += ", HEAD TOUCHES ROOF";
            }
            fit.Hips = new Vector3(hipsX, hipsY, hipsZ);
            return fit;
        }

        // Roof underside in a column: the highest exterior surface above minY, down to its liner (within 8 cm).
        private static float RoofAbove(float x, float z, float minY)
        {
            List<float> column = ColumnCrossings(x, z);
            float top = float.NegativeInfinity;
            foreach (float y in column) if (y > minY && y > top) top = y;
            if (float.IsNegativeInfinity(top)) return float.PositiveInfinity;
            float under = top;
            foreach (float y in column) if (y >= top - 0.08f && y > minY && y < under) under = y;
            return under;
        }

        // Model triangles in root space (wheels left out), for vertical probes.
        private static void BuildProbe(Transform model, Transform root, ModelInfo info)
        {
            probeTriangles = new List<(Vector3, Vector3, Vector3)>();
            var wheels = new HashSet<Renderer>(info.Wheels);
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                var r = mf.GetComponent<Renderer>();
                if (m == null || (r != null && wheels.Contains(r))) continue;
                Vector3[] v = m.vertices;
                int[] t = m.triangles;
                var w = new Vector3[v.Length];
                Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++) w[i] = toRoot.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < t.Length; i += 3) probeTriangles.Add((w[t[i]], w[t[i + 1]], w[t[i + 2]]));
            }
        }

        // Heights of all surfaces crossed by a vertical line at root-space (x, z), highest first.
        private static List<float> ColumnCrossings(float x, float z)
        {
            var hits = new List<float>();
            foreach (var tr in probeTriangles)
            {
                float d1 = (x - tr.b.x) * (tr.a.z - tr.b.z) - (tr.a.x - tr.b.x) * (z - tr.b.z);
                float d2 = (x - tr.c.x) * (tr.b.z - tr.c.z) - (tr.b.x - tr.c.x) * (z - tr.c.z);
                float d3 = (x - tr.a.x) * (tr.c.z - tr.a.z) - (tr.c.x - tr.a.x) * (z - tr.a.z);
                if ((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0)) continue;
                float den = (tr.b.z - tr.c.z) * (tr.a.x - tr.c.x) + (tr.c.x - tr.b.x) * (tr.a.z - tr.c.z);
                if (Mathf.Abs(den) < 1e-6f) continue;
                float l1 = ((tr.b.z - tr.c.z) * (x - tr.c.x) + (tr.c.x - tr.b.x) * (z - tr.c.z)) / den;
                float l2 = ((tr.c.z - tr.a.z) * (x - tr.c.x) + (tr.a.x - tr.c.x) * (z - tr.c.z)) / den;
                hits.Add(l1 * tr.a.y + l2 * tr.b.y + (1f - l1 - l2) * tr.c.y);
            }
            hits.Sort((p, q) => q.CompareTo(p));
            return hits;
        }

        // Seated dummy: hands on the steering wheel rim, feet on the cabin floor in front of the hips.
        private static void FitDummy(Dictionary<string, Transform> bones, Transform root, CabinFit cabin)
        {
            Vector3 wheel = root.TransformPoint(cabin.Wheel);
            foreach (int side in new[] { -1, 1 })
            {
                string sd = side < 0 ? "Left" : "Right";
                Vector3 target = wheel + root.right * (0.17f * side) + root.up * 0.04f - root.forward * 0.04f;
                TwoBoneIK(Bone(bones, sd + "Arm"), Bone(bones, sd + "ForeArm"), Bone(bones, sd + "Hand"), target, root.right * (0.6f * side) - root.up * 0.8f);
            }
            Transform upLeg = Bone(bones, "LeftUpLeg"), leg = Bone(bones, "LeftLeg"), foot = Bone(bones, "LeftFoot");
            if (upLeg == null || leg == null || foot == null) return;
            float chain = Vector3.Distance(upLeg.position, leg.position) + Vector3.Distance(leg.position, foot.position);
            float ankleY = cabin.FloorY + 0.09f;
            float dy = Mathf.Max(0f, cabin.Hips.y - ankleY);
            float dz = Mathf.Min(0.62f, Mathf.Sqrt(Mathf.Max(0.04f, 0.92f * chain * 0.92f * chain - dy * dy)));
            foreach (int side in new[] { -1, 1 })
            {
                string sd = side < 0 ? "Left" : "Right";
                Vector3 target = root.TransformPoint(new Vector3(cabin.Hips.x + 0.14f * side, ankleY, cabin.Hips.z + dz));
                TwoBoneIK(Bone(bones, sd + "UpLeg"), Bone(bones, sd + "Leg"), Bone(bones, sd + "Foot"), target, root.up * 0.8f + root.forward * 0.5f + root.right * (0.15f * side));
            }
        }

        private static Transform Bone(Dictionary<string, Transform> bones, string name) =>
            bones.TryGetValue("mixamorig:" + name, out Transform t) ? t : null;

        private static Dictionary<string, Transform> Bones(Transform rig)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            return bones;
        }

        private static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            if (a == null || b == null || c == null) return;
            float l1 = Vector3.Distance(a.position, b.position), l2 = Vector3.Distance(b.position, c.position);
            Vector3 toTarget = target - a.position;
            float d = Mathf.Clamp(toTarget.magnitude, 0.01f, (l1 + l2) * 0.999f);
            Vector3 dir = toTarget.normalized;
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float angA = Mathf.Acos(cosA);
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            bend.Normalize();
            Vector3 jointWanted = a.position + dir * (Mathf.Cos(angA) * l1) + bend * (Mathf.Sin(angA) * l1);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, jointWanted - a.position) * a.rotation;
            Vector3 endWanted = a.position + dir * d;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, endWanted - b.position) * b.rotation;
        }

        // Driver door outer surface and handle (root space); falls back to the body side when the car has no door mesh.
        private static DoorGeometry DoorGeometryOf(ModelInfo info, Bounds body, Vector3 seat, Transform root)
        {
            var d = new DoorGeometry();
            if (info.DriverDoor != null)
            {
                Bounds b = RootBounds(info.DriverDoor, root);
                d.OuterX = b.min.x; d.HandleZ = b.min.z + 0.15f; d.CenterZ = b.center.z;
            }
            else { d.OuterX = body.min.x + 0.05f; d.HandleZ = seat.z - 0.25f; d.CenterZ = seat.z; }
            return d;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Parts
        // ---------------------------------------------------------------------------------------------------------------
        private static VehicleDoor MakeDoor(ModelInfo info, Transform root)
        {
            if (info.DriverDoor == null) return null;
            Bounds b = RootBounds(info.DriverDoor, root);
            Transform doorT = info.DriverDoor.transform;
            var hinge = new GameObject("DriverDoorHinge").transform;
            hinge.SetParent(doorT.parent, false);
            hinge.gameObject.layer = doorT.gameObject.layer;
            hinge.position = root.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z - 0.03f));
            hinge.rotation = root.rotation;
            doorT.SetParent(hinge, true);
            var handle = new GameObject("Handle").transform;
            handle.SetParent(hinge, false);
            handle.position = root.TransformPoint(new Vector3(b.min.x - 0.06f, Mathf.Clamp(b.center.y + b.extents.y * 0.35f, 0.75f, 1.3f), b.min.z + 0.15f));
            var door = hinge.gameObject.AddComponent<VehicleDoor>();
            var so = new SerializedObject(door);
            so.FindProperty("handle").objectReferenceValue = handle;
            so.FindProperty("hingeAxis").vector3Value = Vector3.up;
            so.FindProperty("doorOpenAngle").floatValue = 70f;
            so.FindProperty("edgeDirection").vector3Value = Vector3.back;
            so.ApplyModifiedPropertiesWithoutUndo();
            return door;
        }

        // Pivot at the steering wheel's centre, its Z along the steering column (tilt read from the wheel's bounds).
        private static Transform MakeSteeringPivot(Renderer steering, Transform root)
        {
            Bounds b = RootBounds(steering, root);
            float diameter = Mathf.Max(0.2f, b.size.x);
            float tilt = Mathf.Acos(Mathf.Clamp01((b.size.y - 0.04f) / diameter)) * Mathf.Rad2Deg;
            var pivot = new GameObject("SteeringWheelPivot").transform;
            pivot.SetParent(steering.transform.parent, false);
            pivot.gameObject.layer = steering.gameObject.layer;
            pivot.position = root.TransformPoint(b.center);
            pivot.rotation = root.rotation * Quaternion.Euler(tilt, 0f, 0f);
            steering.transform.SetParent(pivot, true);
            return pivot;
        }

        private static void FitLights(Transform lights, Bounds body)
        {
            float frontZ = body.max.z - 0.12f, rearZ = body.min.z + 0.10f;
            float lampY = Mathf.Clamp(body.size.y * 0.42f, 0.55f, 1.1f);
            foreach (Transform l in lights)
            {
                string n = l.name.ToLowerInvariant();
                float side = l.localPosition.x >= 0f ? 1f : -1f;
                bool front = n.Contains("head");
                l.localPosition = new Vector3(side * body.extents.x * 0.7f, lampY, front ? frontZ : rearZ);
            }
        }

        // One LOD level culled when the car is a few pixels tall (traffic far down the street, parked cars across the map).
        private static void SetupCulling(GameObject root, ModelInfo info)
        {
            var group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            var renderers = new List<Renderer>();
            foreach (var r in info.Renderers) if (r != null) renderers.Add(r);
            group.SetLODs(new[] { new LOD(0.015f, renderers.ToArray()) });
            group.RecalculateBounds();
        }

        private static void MakeReadable(Mesh mesh)
        {
            if (mesh == null || mesh.isReadable) return;
            var so = new SerializedObject(mesh);
            var prop = so.FindProperty("m_IsReadable");
            if (prop == null) return;
            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mesh);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------------
        private static void RemoveEmpty(Transform inst)
        {
            Transform[] all = inst.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform t = all[i];
                if (t == null || t == inst) continue;
                if (t.childCount == 0 && t.GetComponent<Renderer>() == null && t.GetComponents<Component>().Length == 1) Object.DestroyImmediate(t.gameObject);
            }
        }

        private static void SetBox(BoxCollider box, Vector3 rootCenter, Vector3 rootSize, Transform on, Transform root)
        {
            box.center = on.InverseTransformPoint(root.TransformPoint(rootCenter));
            Vector3 s = on.lossyScale;
            box.size = new Vector3(rootSize.x / Mathf.Max(1e-4f, s.x), rootSize.y / Mathf.Max(1e-4f, s.y), rootSize.z / Mathf.Max(1e-4f, s.z));
        }

        private static void FitTriggerBox(Transform t, Vector3 rootCenter, Vector3 rootSize, Transform root)
        {
            if (t == null) return;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            var box = t.GetComponent<BoxCollider>();
            if (box != null) SetBox(box, rootCenter, rootSize, t, root);
        }

        private static void SetPoint(Transform root, string name, Vector3 local, float yaw)
        {
            Transform t = root.Find(name);
            if (t == null) return;
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static Transform SetOrCreatePoint(Transform root, string name, Vector3 local, float yaw)
        {
            Transform t = root.Find(name);
            if (t == null) t = NewChild(name, root, root.gameObject.layer);
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static void SetLocal(Transform t, Vector3 local)
        {
            if (t != null) t.localPosition = local;
        }

        private static void SetTagRecursive(Transform root, string tag)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = tag;
        }

        private static Bounds RootBounds(Renderer r, Transform root)
        {
            var mf = r.GetComponent<MeshFilter>();
            Bounds mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds();
            Bounds b = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                if (i == 0) b = new Bounds(p, Vector3.zero); else b.Encapsulate(p);
            }
            return b;
        }

        private static Transform NewChild(string name, Transform parent, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
