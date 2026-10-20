using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds every car of the game from its <see cref="VehicleSpec"/>:
    /// - Drivable_[name] (RCC, player can enter/exit/steal) from Template_Drivable, and
    /// - Traffic_[name] (iTS AI, swaps to the drivable when stolen) from Template_Traffic.
    /// The templates hold the full game setup (adapters, seats with the player / NPC dummies, hitboxes, audio, damage,
    /// lights); the factory swaps in the new model, scales it to the spec's real length, and re-fits wheels, colliders,
    /// seat and entry points, door hinge, steering wheel, LODs and the seated dummies to that model.
    /// Menu: Tools > OpenworldCrime > Vehicles > Create Vehicle Specs / Build Vehicles From Specs.
    /// </summary>
    public static class VehicleFactory
    {
        private const string ModelsFolder = "Assets/_Project/Models/Vehicles";
        private const string SpecsFolder = "Assets/_Project/Data/Vehicles/Specs";
        private const string DrivableFolder = "Assets/_Project/Prefabs/Vehicles/Drivable";
        private const string TrafficFolder = "Assets/_Project/Prefabs/Vehicles/Traffic";
        private const string TemplateDrivable = "Assets/_Project/Prefabs/Vehicles/_Templates/Template_Drivable.prefab";
        private const string TemplateTraffic = "Assets/_Project/Prefabs/Vehicles/_Templates/Template_Traffic.prefab";

        // Cars are built this much over their real length (see PlaceModel). 1.0 = real size.
        private const float CarSizeFactor = 1.2f;

        // Highest roof (m above the ground) per class for the 1.64 m player: a sedan's roof at his shoulder, a supercar's at his
        // chest, an SUV's above his head.
        private static float MaxRoofHeight(VehicleClass cls)
        {
            switch (cls)
            {
                case VehicleClass.Compact: return 1.45f;
                case VehicleClass.Sedan: return 1.50f;
                case VehicleClass.Sports: return 1.35f;
                case VehicleClass.Super: return 1.25f;
                case VehicleClass.SUV: return 1.90f;
                case VehicleClass.Emergency: return 1.65f;
                case VehicleClass.Van: return 2.10f;
                default: return 3.6f;
            }
        }

        // Top of the car's roof (root space) without roof accessories: a taxi sign, light bars, racks or antennas would otherwise
        // count as the roof and shrink the whole car. Only the main body shell(s) are measured, over their middle 60% of length
        // (spoilers, open-top windscreen frames and roll hoops at the ends are left out).
        private static float RoofTop(ModelInfo info, Transform root)
        {
            float top = float.NegativeInfinity;
            var parts = new List<Renderer>(info.Lod0); parts.AddRange(info.Shared);
            float zMin = info.Body.center.z - info.Body.extents.z * 0.6f, zMax = info.Body.center.z + info.Body.extents.z * 0.6f;
            foreach (var r in parts)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("logo") || n.Contains("sign") || n.Contains("light") || n.Contains("lamp") || n.Contains("siren")
                    || n.Contains("serena") || n.Contains("antenna") || n.Contains("rack") || n.Contains("glass")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                Matrix4x4 m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    if (p.z >= zMin && p.z <= zMax && p.y > top) top = p.y;
                }
            }
            return float.IsNegativeInfinity(top) ? info.Body.max.y : top;
        }
        // Root scale of the player prefab (Player_Jason). The seated dummy, the seated clip's hip offset and the head height
        // all follow it, so the dummy keeps matching the real player's seated pose.
        // 0.94 = 1.68 m with the FinalPlayer model (Jason's skeleton, smaller head): the size of the street pedestrians' men,
        // about a head over a sedan roof.
        internal const float PlayerScale = 0.94f;

        // Hips of the posed seated player dummy relative to DriverSeat (calibrated on the previous car set).
        private static readonly Vector3 SeatToHips = new Vector3(0.01f, 0.69f, 0.11f);

        private struct Preset
        {
            public string File, Name; public VehicleClass Cls; public float Length; public bool Traffic; public int Freq;
            public float Mass, Speed, Torque, Brake, Steer, HighSteer, Down; public RCC_CarControllerV3.WheelType Drive;
            public Preset(string file, string name, VehicleClass cls, float length, bool traffic, int freq, float mass, float speed,
                float torque, float brake, float steer, float highSteer, float down, RCC_CarControllerV3.WheelType drive)
            {
                File = file; Name = name; Cls = cls; Length = length; Traffic = traffic; Freq = freq; Mass = mass; Speed = speed;
                Torque = torque; Brake = brake; Steer = steer; HighSteer = highSteer; Down = down; Drive = drive;
            }
        }

        private static readonly Preset[] Presets =
        {
            //          file                      name              class                 len   traffic freq mass   km/h torque brake steer hs  down drive
            new Preset("StandAsideCar1",        "Compact White",  VehicleClass.Compact, 4.40f, true,  9, 1250f, 185f, 260f, 2200f, 40f, 8f, 20f, RCC_CarControllerV3.WheelType.FWD),
            new Preset("StandAsideCar30",       "Compact Pink",   VehicleClass.Compact, 4.50f, true,  8, 1280f, 185f, 260f, 2200f, 40f, 8f, 20f, RCC_CarControllerV3.WheelType.FWD),
            new Preset("Cervo_Preview",         "Cervo",          VehicleClass.Compact, 4.30f, true,  7, 1200f, 180f, 250f, 2100f, 40f, 8f, 20f, RCC_CarControllerV3.WheelType.FWD),
            new Preset("StandAsideCar_Preview", "Sedan Orange",   VehicleClass.Sedan,   4.80f, true,  8, 1500f, 210f, 320f, 2500f, 38f, 7f, 22f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Taxi_Preview",          "Taxi",           VehicleClass.Sedan,   4.90f, true,  7, 1600f, 200f, 320f, 2500f, 38f, 7f, 22f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("TeslaCar",              "Tesla",          VehicleClass.Sedan,   4.75f, true,  6, 2100f, 240f, 480f, 2800f, 38f, 7f, 25f, RCC_CarControllerV3.WheelType.AWD),
            new Preset("PoliceCar_Preview",     "Police Car",     VehicleClass.Emergency,5.00f, true, 3, 1800f, 230f, 430f, 2800f, 38f, 7f, 25f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Jeep_Preview",          "Jeep",           VehicleClass.SUV,     4.60f, true,  5, 2000f, 175f, 380f, 2800f, 36f, 8f, 18f, RCC_CarControllerV3.WheelType.AWD),
            new Preset("Hummer",                "Hummer",         VehicleClass.SUV,     4.70f, true,  3, 3200f, 160f, 520f, 3200f, 33f, 8f, 18f, RCC_CarControllerV3.WheelType.AWD),
            new Preset("PoliceJeep",            "Police Jeep",    VehicleClass.Emergency,4.90f, true, 2, 2300f, 190f, 430f, 3000f, 35f, 8f, 20f, RCC_CarControllerV3.WheelType.AWD),
            new Preset("Ambulance",             "Ambulance",      VehicleClass.Emergency,6.00f, true, 2, 3400f, 150f, 520f, 3400f, 33f, 8f, 18f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Limusine",              "Limousine",      VehicleClass.Van,     8.20f, true,  1, 3200f, 170f, 480f, 3400f, 30f, 7f, 20f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Bus",                   "City Bus",       VehicleClass.Bus,    12.00f, true,  1, 11000f, 100f, 1400f, 9000f, 30f, 10f, 15f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("BNW i8",                "i8",             VehicleClass.Sports,  4.69f, true,  3, 1550f, 260f, 480f, 3000f, 36f, 6f, 35f, RCC_CarControllerV3.WheelType.AWD),
            new Preset("Ferrari",               "Ferrari",        VehicleClass.Super,   4.60f, true,  2, 1475f, 320f, 640f, 3400f, 35f, 5f, 45f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Ferraro En",            "Enzo",           VehicleClass.Super,   4.70f, true,  2, 1365f, 330f, 660f, 3400f, 35f, 5f, 45f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Roenigsegg",            "Koenig",         VehicleClass.Super,   4.30f, true,  1, 1420f, 350f, 720f, 3600f, 35f, 5f, 50f, RCC_CarControllerV3.WheelType.RWD),
            new Preset("Bugetta C",             "Bugatti",        VehicleClass.Super,   4.54f, true,  1, 1995f, 380f, 880f, 3800f, 34f, 4f, 55f, RCC_CarControllerV3.WheelType.AWD),
        };

        // Final car pack (Assets/FinalCars): preset file -> model. Presets without an entry (police, ambulance, bus, Tesla,
        // limousine) are not built; their specs are kept but have no model.
        private static readonly Dictionary<string, string> FinalModels = new Dictionary<string, string>
        {
            { "StandAsideCar1", "Assets/FinalCars/StandAsideCar1.fbm/Stand car01.fbx" },
            { "StandAsideCar30", "Assets/FinalCars/StandAsideCar30.fbm/Stand Car 03.fbx" },
            { "Cervo_Preview", "Assets/FinalCars/Cervo_Preview.fbm/Cervo.fbx" },
            { "StandAsideCar_Preview", "Assets/FinalCars/StandAsideCar_Preview.fbm/Stand Aside.fbx" },
            { "Taxi_Preview", "Assets/FinalCars/Taxi_Preview.fbm/Taxi.fbx" },
            { "Jeep_Preview", "Assets/FinalCars/Jeep_Preview.fbm/Jeep.fbx" },
            { "Hummer", "Assets/FinalCars/Hummer.fbm/Hummer.fbx" },
            { "BNW i8", "Assets/FinalCars/BNW i8.fbm/BMW.fbx" },
            { "Ferrari", "Assets/FinalCars/Ferrari.fbm/Ferrari.fbx" },
            { "Ferraro En", "Assets/FinalCars/Ferraro En.fbm/Ferraro.fbx" },
            { "Roenigsegg", "Assets/FinalCars/Roenigsegg.fbm/Roenigegg.fbx" },
            { "Bugetta C", "Assets/FinalCars/Bugetta C.fbm/Burgetta.fbx" },
        };

        // Source (Built-in) material -> converted URP material, filled at the start of a build.
        private static Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
        // Cabin fit of the drivable just built: the traffic twin uses the same seat, so the dummies match on a steal.
        private static CabinFit lastCabin;
        // Triangles (root space) of the model being fitted, for the cabin probes.
        private static List<(Vector3 a, Vector3 b, Vector3 c, string n)> probeTriangles;
        private static Transform probeModel;

        // Steering wheel position as a share of the body (x of the half width from the centre, y of the height above the
        // ground, z of the length from the rear), averaged over the pack's models that have a separate steering wheel; used
        // for models whose wheel is part of the dashboard mesh.
        private static readonly Vector3 SteeringShare = new Vector3(-0.36f, 0.66f, 0.58f);

        // ---------------------------------------------------------------------------------------------------------------
        [MenuItem("Tools/OpenworldCrime/Vehicles/Create Vehicle Specs")]
        public static void CreateSpecsMenu() => Debug.Log(CreateSpecs());

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Vehicles From Specs")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        /// <summary>Creates a spec asset for every known model that has none yet (existing specs are never overwritten).</summary>
        public static string CreateSpecs()
        {
            EnsureFolder(SpecsFolder);
            var sb = new StringBuilder("[VehicleFactory] Specs:\n");
            foreach (Preset p in Presets)
            {
                string path = $"{SpecsFolder}/Spec_{p.File.Replace(" ", "")}.asset";
                var spec = AssetDatabase.LoadAssetAtPath<VehicleSpec>(path);
                GameObject model = FinalModels.TryGetValue(p.File, out string modelPath) ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
                if (spec != null)
                {
                    // Re-point existing specs (tuned values kept) at the final pack; specs left out of the pack lose their model.
                    if (spec.Model != model) { spec.EditorSetModel(model); EditorUtility.SetDirty(spec); }
                    sb.AppendLine((model != null ? "  kept " : "  not in final pack: ") + path);
                    continue;
                }
                if (model == null) { sb.AppendLine("  model missing: " + p.File); continue; }
                spec = ScriptableObject.CreateInstance<VehicleSpec>();
                spec.EditorSetup(p.Name, model, p.Cls, p.Length, p.Traffic, p.Freq, p.Mass, p.Speed, p.Torque, p.Brake, p.Steer, p.HighSteer, p.Down, p.Drive);
                AssetDatabase.CreateAsset(spec, path);
                sb.AppendLine("  created " + path);
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// <summary>Builds both prefabs for every spec, then wires them into the scene's traffic spawner and takeover pool.</summary>
        public static string BuildAll()
        {
            CreateSpecs();
            EnsureFolder(DrivableFolder);
            EnsureFolder(TrafficFolder);
            var sb = new StringBuilder("[VehicleFactory] Build:\n");
            var built = new List<KeyValuePair<VehicleSpec, (GameObject traffic, RCCVehicleAdapter drivable)>>();
            var modelFolders = new List<string>();
            foreach (string modelPath in FinalModels.Values) modelFolders.Add(System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/'));
            materialMap = FinalCarMaterials.ConvertAll(modelFolders);
            foreach (string guid in AssetDatabase.FindAssets("t:VehicleSpec", new[] { SpecsFolder }))
            {
                var spec = AssetDatabase.LoadAssetAtPath<VehicleSpec>(AssetDatabase.GUIDToAssetPath(guid));
                if (spec == null || spec.Model == null) continue;
                MakeReadable(spec.Model);
                try
                {
                    lastCabin = null;
                    string dPath = $"{DrivableFolder}/Drivable_{Safe(spec)}.prefab";
                    string dLog = BuildDrivable(spec, dPath);
                    var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(dPath);
                    string tPath = $"{TrafficFolder}/Traffic_{Safe(spec)}.prefab";
                    string tLog = BuildTraffic(spec, tPath, drivable);
                    var traffic = AssetDatabase.LoadAssetAtPath<GameObject>(tPath);
                    built.Add(new KeyValuePair<VehicleSpec, (GameObject, RCCVehicleAdapter)>(spec, (traffic, drivable)));
                    sb.AppendLine($"  {spec.DisplayName}: {dLog} | {tLog}");
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"  {spec.DisplayName}: FAILED {e.Message}\n{e.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine(CarPaintSetup.ApplyAll());
            sb.AppendLine(VehicleLightsSetup.SetupAllReport());
            sb.AppendLine(PedDriverBuilder.RepatchPrefabs());
            sb.AppendLine(WireScene(built));
            return sb.ToString();
        }

        // Prefab names follow the spec file (stable across model re-exports), e.g. Spec_BNWi8 -> Drivable_BNWi8.
        private static string Safe(VehicleSpec spec) => spec.name.Replace("Spec_", "").Replace("_Preview", "");

        // ---------------------------------------------------------------------------------------------------------------
        private class ModelInfo
        {
            public Transform Instance;
            public readonly List<Renderer>[] WheelRenderers = new List<Renderer>[4]; // FL, FR, RL, RR
            public readonly Vector3[] WheelCenters = new Vector3[4];                  // root space
            public readonly float[] WheelRadius = new float[4];
            public Bounds Body;                                                        // root space, wheels excluded
            public readonly Dictionary<string, Vector3> Points = new Dictionary<string, Vector3>();
            public readonly List<Renderer> Lod0 = new List<Renderer>(), Lod1 = new List<Renderer>(), Lod2 = new List<Renderer>(), Shared = new List<Renderer>();
            public Renderer Steering;
            public readonly List<Renderer> DriverDoor = new List<Renderer>();
            public readonly List<MeshFilter> BodyMeshes = new List<MeshFilter>();
            public float Ground => Mathf.Min(Mathf.Min(WheelCenters[0].y - WheelRadius[0], WheelCenters[1].y - WheelRadius[1]),
                                             Mathf.Min(WheelCenters[2].y - WheelRadius[2], WheelCenters[3].y - WheelRadius[3]));
        }

        private static readonly string[][] WheelNames =
        {
            new[] { "WheelColliderFL", "WheelFL", "Wheel_L", "Wheel_FL" },
            new[] { "WheelColliderFR", "WheelFR", "Wheel_R", "Wheel_FR" },
            new[] { "WheelColliderBL", "WheelColliderRL", "WheelRL", "Wheel_Back_L", "Wheel_RL" },
            new[] { "WheelColliderBR", "WheelColliderRR", "WheelRR", "Wheel_Back_R", "Wheel_RR" },
        };

        private static readonly string[] HelperNames = { "Collider", "SmokePoint", "Source", "WaterSensor", "Points", "Armature", "Armature_001" };

        /// <summary>Instantiates the model under parent, scales it to the spec length, centres it and reads its layout.</summary>
        private static ModelInfo PlaceModel(VehicleSpec spec, Transform parent, Transform root)
        {
            var inst = (GameObject)Object.Instantiate(spec.Model, parent);
            inst.name = spec.Model.name;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = spec.Model.transform.localScale;
            RemapMaterials(inst.transform);
            CompleteWheels(inst.transform);
            var info = Analyze(inst.transform, root);

            // Face +Z (front wheels ahead of the rear ones).
            if ((info.WheelCenters[0].z + info.WheelCenters[1].z) < (info.WheelCenters[2].z + info.WheelCenters[3].z))
            {
                inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                info = Analyze(inst.transform, root);
            }
            // Real-world length times the game's car size factor. The pack's models are long and wide but flat for their
            // length (a 4.3 m compact is only 1.12 m tall against 1.48 real), so at true size their roofs reach a 1.76 m
            // character's chest and they read as toy-sized next to him. Like GTA, cars are built over real size - all by the
            // same factor, so they stay proportional to each other - which also gives the seated driver real headroom.
            float scale = spec.TargetLength * CarSizeFactor / Mathf.Max(0.1f, info.Body.size.z);
            inst.transform.localScale *= scale;
            info = Analyze(inst.transform, root);
            // The pack's models differ in proportion (the taxi is tall for its length), so length alone can make a car tower
            // over the player. Its roof is capped at a class height set against the player (real cars next to a person, a
            // little over size like the rest); a taller car is scaled down as a whole.
            float roof = RoofTop(info, root) - info.Ground;
            float cap = MaxRoofHeight(spec.Class);
            if (roof > cap)
            {
                inst.transform.localScale *= cap / roof;
                info = Analyze(inst.transform, root);
            }
            // Wheels on the ground plane (root y = 0), body centred on x and between the axles' midpoint on z.
            Vector3 shift = new Vector3(-info.Body.center.x, -info.Ground, -info.Body.center.z);
            inst.transform.position += root.TransformVector(shift);
            info = Analyze(inst.transform, root);
            return info;
        }

        private static ModelInfo Analyze(Transform inst, Transform root)
        {
            var info = new ModelInfo { Instance = inst };
            var wheelSet = new HashSet<Renderer>();
            for (int w = 0; w < 4; w++)
            {
                Transform t = FindAny(inst, WheelNames[w]);
                if (t == null) throw new System.Exception($"wheel {w} not found in {inst.name}");
                var list = new List<Renderer>(t.GetComponentsInChildren<MeshRenderer>(true));
                if (list.Count == 0) throw new System.Exception($"wheel {t.name} has no mesh");
                info.WheelRenderers[w] = list;
                Bounds b = RootBounds(list, root);
                info.WheelCenters[w] = b.center;
                info.WheelRadius[w] = Mathf.Max(b.extents.y, b.extents.z);
                foreach (var r in list) wheelSet.Add(r);
            }

            var all = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true)) if (!wheelSet.Contains(r)) all.Add(r);
            foreach (var r in all)
            {
                int lod = LodOf(r.transform, inst);
                if (lod == 0) info.Lod0.Add(r); else if (lod == 1) info.Lod1.Add(r); else if (lod >= 2) info.Lod2.Add(r); else info.Shared.Add(r);
            }
            var primary = new List<Renderer>(info.Lod0); primary.AddRange(info.Shared);
            if (primary.Count == 0) primary.AddRange(all);
            info.Body = RootBounds(primary, root);

            Transform points = FindAny(inst, new[] { "Points" });
            if (points != null) foreach (Transform p in points) info.Points[p.name] = root.InverseTransformPoint(p.position);

            foreach (var r in primary)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("steer") || n == "helm") info.Steering = r;
            }
            // Driver door: the largest door panel on the left (-X) side.
            float best = 0f; Renderer door = null;
            foreach (var r in primary)
            {
                string n = r.name.ToLowerInvariant();
                if (!n.Contains("door") || n.Contains("bus")) continue;
                Bounds b = RootBounds(new List<Renderer> { r }, root);
                if (b.center.x >= -0.05f || b.size.z < 0.5f) continue;
                float v = b.size.y * b.size.z;
                if (v > best) { best = v; door = r; }
            }
            if (door != null) info.DriverDoor.Add(door);

            // Deformable body: renderers named body (LOD0 / shared), else the biggest non-glass, non-interior part.
            foreach (var r in primary)
            {
                string n = r.name.ToLowerInvariant();
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && n.Contains("body") && !n.Contains("lod")) info.BodyMeshes.Add(mf);
            }
            if (info.BodyMeshes.Count == 0)
            {
                MeshFilter biggest = null; float vol = 0f;
                foreach (var r in primary)
                {
                    string n = r.name.ToLowerInvariant();
                    if (n.Contains("glass") || n.Contains("interior") || n.Contains("salon")) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    Vector3 s = r.bounds.size; float v2 = s.x * s.y * s.z;
                    if (v2 > vol) { vol = v2; biggest = mf; }
                }
                if (biggest != null) info.BodyMeshes.Add(biggest);
            }
            return info;
        }

        private static void RemapMaterials(Transform inst)
        {
            if (materialMap == null || materialMap.Count == 0) return;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && materialMap.TryGetValue(mats[i], out Material urp)) { mats[i] = urp; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // The final pack's models were exported with one to four wheels. Missing wheels are copies of an existing one: mirrored
        // to the other side (rim facing out) and/or moved to the other axle, whose position is read from the body's wheel arch
        // (the gap in the outer side surface at wheel height), falling back to the overhang ratio. Wheels are then named
        // WheelFL / WheelFR / WheelRL / WheelRR for Analyze. Models face +Z (the pack's "Back" wheels sit at -Z).
        private static void CompleteWheels(Transform inst)
        {
            var wheels = new List<Renderer>();
            var rest = new List<MeshFilter>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("wheel") && !n.Contains("steer") && !n.Contains("collider")) wheels.Add(r);
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) rest.Add(mf); }
            }
            if (wheels.Count == 0 || rest.Count == 0) return;
            // Already complete and named (e.g. an RCC-ready model).
            if (FindAny(inst, new[] { "WheelColliderFL", "WheelFL" }) != null) return;

            Bounds body = rest[0].GetComponent<Renderer>().bounds;
            foreach (var mf in rest) body.Encapsulate(mf.GetComponent<Renderer>().bounds);
            float cx = body.center.x;

            // slot: 0 FL, 1 FR, 2 RL, 3 RR
            var slot = new Renderer[4];
            foreach (var w in wheels)
            {
                Vector3 c = w.bounds.center;
                int s = (c.z >= body.center.z ? 0 : 2) + (c.x < cx ? 0 : 1);
                if (slot[s] == null) slot[s] = w;
            }

            float frontZ = float.NaN, rearZ = float.NaN;
            for (int s = 0; s < 4; s++)
            {
                if (slot[s] == null) continue;
                if (s < 2) frontZ = slot[s].bounds.center.z; else rearZ = slot[s].bounds.center.z;
            }
            Renderer any = wheels[0];
            float radius = any.bounds.extents.y;
            float ground = any.bounds.center.y - radius;
            if (float.IsNaN(frontZ))
                frontZ = FindArch(rest, body, any.bounds.center.y, radius, ground, true) ?? body.max.z - (rearZ - body.min.z) * 1.15f;
            if (float.IsNaN(rearZ))
                rearZ = FindArch(rest, body, any.bounds.center.y, radius, ground, false) ?? body.min.z + (body.max.z - frontZ) * 0.9f;

            for (int s = 0; s < 4; s++)
            {
                if (slot[s] != null) continue;
                bool front = s < 2, left = (s & 1) == 0;
                // Source: same axle other side (mirror) > same side other axle (move) > any wheel.
                Renderer src = slot[front ? (left ? 1 : 0) : (left ? 3 : 2)] ?? slot[front ? (left ? 2 : 3) : (left ? 0 : 1)] ?? any;
                Vector3 sc = src.bounds.center;
                bool srcLeft = sc.x < cx;
                var clone = Object.Instantiate(src.gameObject, src.transform.parent);
                clone.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                clone.transform.localScale = src.transform.localScale;
                if (srcLeft != left) clone.transform.RotateAround(sc, Vector3.up, 180f);
                Vector3 target = new Vector3(srcLeft != left ? 2f * cx - sc.x : sc.x, sc.y, front ? frontZ : rearZ);
                clone.transform.position += target - clone.GetComponent<Renderer>().bounds.center;
                slot[s] = clone.GetComponent<Renderer>();
            }

            string[] names = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
            for (int s = 0; s < 4; s++) slot[s].gameObject.name = names[s];
        }

        // Centre (z) of the wheel arch on the front or rear half of the body, from the gap in the outer side surface.
        private static float? FindArch(List<MeshFilter> parts, Bounds body, float wheelY, float radius, float ground, bool front)
        {
            float y0 = ground + radius * 0.3f, y1 = wheelY;
            foreach (float share in new[] { 0.82f, 0.9f, 0.7f })
            {
                const int bins = 400;
                var covered = new bool[bins];
                float xLimit = body.extents.x * share;
                foreach (var mf in parts)
                {
                    Vector3[] v = mf.sharedMesh.vertices; int[] tris = mf.sharedMesh.triangles; Matrix4x4 m = mf.transform.localToWorldMatrix;
                    var wv = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) wv[i] = m.MultiplyPoint3x4(v[i]);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 a = wv[tris[i]], b = wv[tris[i + 1]], c = wv[tris[i + 2]];
                        if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - body.center.x > -xLimit) continue; // left outer surface only
                        if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < y0 || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > y1) continue;
                        int i0 = Mathf.Clamp((int)((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - body.min.z) / body.size.z * bins), 0, bins - 1);
                        int i1 = Mathf.Clamp((int)((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - body.min.z) / body.size.z * bins), 0, bins - 1);
                        for (int k = i0; k <= i1; k++) covered[k] = true;
                    }
                }
                float best = -1f, bestZ = 0f; int start = -1;
                for (int k = 0; k <= bins; k++)
                {
                    bool c = k < bins && covered[k];
                    if (!c && start < 0) start = k;
                    if ((c || k == bins) && start >= 0)
                    {
                        float z0 = body.min.z + start * body.size.z / bins, z1 = body.min.z + k * body.size.z / bins;
                        float mid = (z0 + z1) * 0.5f;
                        bool inHalf = front ? mid > body.center.z : mid < body.center.z;
                        if (inHalf && z1 - z0 > radius * 0.6f && z0 > body.min.z + 0.2f && z1 < body.max.z - 0.2f && z1 - z0 > best) { best = z1 - z0; bestZ = mid; }
                        start = -1;
                    }
                }
                if (best > 0f) return bestZ;
            }
            return null;
        }

        // 0/1/2 = LOD level from its own or an ancestor's name, -1 = not part of a LOD chain (shown at every level).
        private static int LodOf(Transform t, Transform stop)
        {
            for (Transform p = t; p != null && p != stop; p = p.parent)
            {
                string n = p.name.ToLowerInvariant();
                if (n.Contains("lod_03") || n.Contains("lod_3") || n.Contains("lod3")) return 3;
                if (n.Contains("lod_02") || n.Contains("lod_2") || n.Contains("lod2") || n == "low" || n.EndsWith("_low")) return 2;
                if (n.Contains("lod_01") || n.Contains("lod_1") || n.Contains("lod1") || n == "medium" || n.EndsWith("_medium")) return 1;
                if (n.Contains("lod_0") || n.Contains("lod0") || n == "hight" || n == "high" || n.EndsWith("_hight")) return 0;
            }
            return -1;
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static string BuildDrivable(VehicleSpec spec, string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TemplateDrivable);
            try
            {
                root.name = Safe(spec);
                Transform rt = root.transform;
                Object.DestroyImmediate(rt.Find("Model").gameObject);
                var model = NewChild("Model", rt, root.layer);
                ModelInfo info = PlaceModel(spec, model, rt);
                SetLayer(info.Instance, root.layer);
                Vector3 seatOld = rt.Find("DriverSeat").localPosition;

                // Wheels: visuals under RCC's wheel model holders, colliders fitted to them.
                string[] holders = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
                Transform modelsRoot = rt.Find("Wheel Models"), collidersRoot = rt.Find("Wheel Colliders");
                for (int w = 0; w < 4; w++)
                {
                    Transform holder = modelsRoot.Find(holders[w]);
                    for (int c = holder.childCount - 1; c >= 0; c--) Object.DestroyImmediate(holder.GetChild(c).gameObject);
                    holder.position = rt.TransformPoint(info.WheelCenters[w]);
                    holder.rotation = rt.rotation;
                    foreach (var r in info.WheelRenderers[w]) r.transform.SetParent(holder, true);
                    var wc = collidersRoot.Find(holders[w]).GetComponent<WheelCollider>();
                    FitWheelCollider(wc, info.WheelCenters[w], info.WheelRadius[w], rt);
                }
                CleanHelpers(info.Instance);

                Bounds body = info.Body; float g = 0f;
                FitBodyColliders(model, body, g, rt);

                // Seat from the car's own cabin (steering wheel, floor, roof); the seated dummy is fitted at once, since the roof
                // check uses its real head. The seated clip then defines the DriverSeat transform under the hips.
                CabinFit cabin = FitCabin(info, body, g, rt, model);
                Transform seatT = rt.Find("DriverSeat");
                seatT.localRotation = Quaternion.identity;
                seatT.localPosition = cabin.Seat;
                string seatLog = FitSeatedDummies(rt, seatT, info, cabin, seatOld);
                lastCabin = cabin;
                Vector3 seat = cabin.Seat;
                // Driver hit zones follow the hips (template zones were authored around the template's hips).
                Vector3 seatDelta = cabin.Hips - (seatOld + SeatToHips);
                DoorGeometry door0 = DoorGeometryOf(info, body, seat, rt);
                SetPoint(rt, "EnterPoint", new Vector3(door0.OuterX - 0.45f, g + 0.02f, door0.HandleZ - 0.05f), 90f);
                SetPoint(rt, "ExitPoint", new Vector3(door0.OuterX - 0.60f, g + 0.02f, seat.z + 0.05f), 270f);
                SetPoint(rt, "AlternateExitPoint", new Vector3(body.max.x + 0.60f, g + 0.02f, seat.z + 0.05f), 90f);
                SetPoint(rt, "InteractionPoint", new Vector3(door0.OuterX - 0.10f, g + 0.52f, door0.CenterZ), 0f);
                SetPoint(rt, "CabinFloor", new Vector3(door0.OuterX + 0.12f, cabin.FloorY, cabin.Hips.z + 0.2f), 0f);
                SetPoint(rt, "EjectPoint", new Vector3(door0.OuterX - 0.75f, g + 0.02f, seat.z + 0.30f), 284f);
                SetPoint(rt, "CarjackStandPoint", new Vector3(door0.OuterX - 0.32f, g + 0.02f, door0.HandleZ - 0.25f), 69f);
                rt.Find("HoodCamera").localPosition = new Vector3(cabin.Hips.x, g + body.size.y * 0.85f, cabin.Hips.z + 0.02f);
                FitTriggerBox(rt.Find("InteractionZone"), new Vector3(0f, g + 1.04f, body.center.z), new Vector3(body.size.x + 3.9f, Mathf.Max(2.4f, body.size.y + 0.6f), body.size.z + 2.8f), rt);
                FitTriggerBox(rt.Find("TrafficSensorTarget"), body.center, new Vector3(body.size.x * 1.02f, body.size.y, body.size.z * 0.98f), rt);
                rt.Find("COM").localPosition = new Vector3(0f, (info.WheelCenters[0].y + info.WheelCenters[2].y) * 0.5f + spec.CenterOfMassHeight,
                    (info.WheelCenters[0].z + info.WheelCenters[2].z) * 0.5f);

                FitLights(rt, body, g);
                FitHitboxes(rt.Find("BulletHitbox"), body, g, seatDelta, rt);

                // Steering wheel pivot (RCC turns it), driver door hinge, LODs.
                var rcc = root.GetComponent<RCC_CarControllerV3>();
                rcc.SteeringWheel = info.Steering != null ? MakePivot(info.Steering, "SteeringWheel", rt, -18f) : null;
                VehicleDoor door = MakeDoor(info, model, rt, g);
                SetupLods(model.gameObject, info);

                // Systems.
                var adapter = root.GetComponent<RCCVehicleAdapter>();
                var so = new SerializedObject(adapter);
                so.FindProperty("driverDoor").objectReferenceValue = door;
                so.ApplyModifiedPropertiesWithoutUndo();
                SetMeshes(root.GetComponent<VehicleDamageDeformer>(), info.BodyMeshes, spec.DamageMultiplier);
                ClearLightSlots(root.GetComponent<VehicleLights>());
                var body2 = root.GetComponent<Rigidbody>();
                body2.mass = spec.Mass;
                ApplyHandling(rcc, spec);
                var applier = root.GetComponent<VehicleSpecApplier>(); if (applier == null) applier = root.AddComponent<VehicleSpecApplier>();
                applier.EditorAssign(spec, rcc, body2);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"drivable ok (len {body.size.z:F2} x {body.size.x:F2} x {body.size.y:F2}, door {(door != null ? "yes" : "no")}, wheel {(info.Steering != null ? "yes" : "no")}, LOD {info.Lod1.Count + info.Lod2.Count > 0}, {seatLog})";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static string BuildTraffic(VehicleSpec spec, string path, RCCVehicleAdapter drivable)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TemplateTraffic);
            try
            {
                root.name = Safe(spec);
                Transform rt = root.transform;
                Transform center = rt.Find("Center");
                Transform oldModel = center.Find("Model");
                var model = NewChild("Model", center, oldModel.gameObject.layer);
                Object.DestroyImmediate(oldModel.gameObject);
                ModelInfo info = PlaceModel(spec, model, rt);
                Vector3 seatOld = rt.Find("DriverSeat").localPosition;

                string[] wheels = { "WheelFL1", "WheelFR4", "WheelRL3", "WheelRR2" };
                for (int w = 0; w < 4; w++)
                {
                    Transform wt = rt.Find(wheels[w]);
                    var wc = wt.GetComponent<WheelCollider>();
                    FitWheelCollider(wc, info.WheelCenters[w], info.WheelRadius[w], rt);
                    Transform tire = wt.Find("Tire");
                    for (int c = tire.childCount - 1; c >= 0; c--) Object.DestroyImmediate(tire.GetChild(c).gameObject);
                    tire.position = rt.TransformPoint(info.WheelCenters[w]);
                    tire.rotation = rt.rotation;
                    foreach (var r in info.WheelRenderers[w]) { r.transform.SetParent(tire, true); r.gameObject.layer = 0; }
                }
                CleanHelpers(info.Instance);

                Bounds body = info.Body; float g = 0f;
                FitBodyColliders(model, body, g, rt);
                // Same cabin fit as the drivable twin, so the NPC / player dummies line up when the car is stolen.
                CabinFit cabin = lastCabin ?? FitCabin(info, body, g, rt, model);
                Vector3 seat = cabin.Seat;
                Transform seatT = rt.Find("DriverSeat");
                seatT.localRotation = Quaternion.identity;
                seatT.localPosition = seat;
                DoorGeometry door0 = DoorGeometryOf(info, body, seat, rt);
                SetPoint(rt, "InteractionPoint", new Vector3(door0.OuterX - 0.10f, g + 0.52f, door0.CenterZ), 0f);
                SetPoint(rt, "EnterPoint", new Vector3(door0.OuterX - 0.45f, g + 0.02f, door0.HandleZ - 0.05f), 90f);
                SetPoint(rt, "StealPoint", new Vector3(door0.OuterX - 0.45f, g + 0.02f, door0.HandleZ - 0.15f), 72f);
                FitTriggerBox(rt.Find("InteractionZone"), new Vector3(0f, g + 1.04f, body.center.z), new Vector3(body.size.x + 3.9f, Mathf.Max(2.4f, body.size.y + 0.6f), body.size.z + 2.8f), rt);
                rt.Find("CoG").localPosition = new Vector3(0f, (info.WheelCenters[0].y + info.WheelCenters[2].y) * 0.5f - 0.2f, (info.WheelCenters[0].z + info.WheelCenters[2].z) * 0.5f);
                FitHitboxes(rt.Find("BulletHitbox"), body, g, cabin.Hips - (seatOld + SeatToHips), rt);
                FitNpcDriver(seatT.Find("NpcSeated") ?? seatT.Find("Driver"), rt, cabin, seatOld);

                // iTS brake renderers are not used (VehicleLights drives the light materials).
                var simple = root.GetComponent<TSSimpleCar>();
                simple.brakeLigths = new Renderer[0];
                simple.maxSpeed = spec.Class == VehicleClass.Bus ? 35f : spec.Class == VehicleClass.Super || spec.Class == VehicleClass.Sports ? 55f : 50f;
                var paint = root.GetComponent<TrafficCarPaint>();
                if (paint != null) Object.DestroyImmediate(paint);

                SetupLods(center.gameObject, info);
                var adapter = root.GetComponent<TrafficVehicleAdapter>();
                var so = new SerializedObject(adapter);
                so.FindProperty("drivablePrefab").objectReferenceValue = drivable;
                so.ApplyModifiedPropertiesWithoutUndo();
                SetMeshes(root.GetComponent<VehicleDamageDeformer>(), info.BodyMeshes, spec.DamageMultiplier);
                ClearLightSlots(root.GetComponent<VehicleLights>());
                var body2 = root.GetComponent<Rigidbody>();
                body2.mass = spec.Mass;
                var applier = root.GetComponent<VehicleSpecApplier>(); if (applier == null) applier = root.AddComponent<VehicleSpecApplier>();
                applier.EditorAssign(spec, null, body2);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return "traffic ok";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Cabin fit (root space = metres, y 0 = ground under the wheels).
        private class CabinFit
        {
            public Vector3 Hips;           // driver's hip joint
            public Vector3 Seat;           // DriverSeat local position: the seated clip puts the hips ClipHipsOffset above it
            public Vector3 Wheel;          // steering wheel centre
            public float FloorY;           // cabin floor under the feet
            public float RoofY;            // roof underside above the head (+inf for an open car)
            public float Headroom;         // roof - top of the head (m)
            public bool WheelEstimated;
        }

        private class DoorGeometry { public float OuterX, HandleZ, CenterZ; public bool Found; }

        // Hips relative to the character root in the seated pose (Vehicle_Seated = Vehicle_Exit frame 0), i.e. where the real
        // player's hips land once parented to DriverSeat at local zero. The seated dummy is placed by the same rule.
        private static readonly Vector3 ClipHipsOffset = new Vector3(0.01f, 0.98f, -0.13f) * PlayerScale;
        private const float WheelAboveHips = 0.42f;     // steering wheel hub above the hip joint (real cars 0.38-0.48)
        private const float HipsBehindWheel = 0.45f;    // hip joint behind the hub (shoulders then ~0.55 m from it, elbows bent)
        private const float HipsAboveFloorMin = 0.10f;  // lowest the hips may sit above the cabin floor (low bucket seat)
        private const float RoofClearanceMin = 0.04f;
        private const float HeadBoneToCrown = 0.17f * PlayerScale;    // mixamorig:Head bone to the top of the head
        private const float SeatedLeanDegrees = 23f;    // torso rotated back from the clip's pose (8 deg forward -> 15 deg reclined)

        private static CabinFit FitCabin(ModelInfo info, Bounds body, float g, Transform root, Transform model)
        {
            var fit = new CabinFit { WheelEstimated = info.Steering == null };
            fit.Wheel = info.Steering != null ? RootBounds(new List<Renderer> { info.Steering }, root).center
                : new Vector3(body.center.x + SteeringShare.x * body.extents.x, g + SteeringShare.y * (body.max.y - g), body.min.z + SteeringShare.z * body.size.z);
            float hipsX = fit.Wheel.x, hipsZ = fit.Wheel.z - HipsBehindWheel;

            // Floor: the floor pan is the first surface above the underbody (at least 8 cm above it, so the two faces of the
            // underbody panel are skipped), well below the wheel; consoles and seat bases above it are ignored. Median over six
            // columns through the footwell.
            var floors = new List<float>();
            foreach (float dz in new[] { -0.15f, 0.05f })
                foreach (float dx in new[] { -0.12f, 0f, 0.12f })
                {
                    var column = ColumnCrossings(model, root, hipsX + dx, fit.Wheel.z + dz, false);
                    if (column.Count == 0) continue;
                    float lowest = column[column.Count - 1].y;
                    float best = float.PositiveInfinity;
                    foreach (var c in column) if (c.y >= lowest + 0.08f && c.y < fit.Wheel.y - 0.25f && c.y < best) best = c.y;
                    if (float.IsPositiveInfinity(best))
                    {
                        float? f = HighestBelow(column, fit.Wheel.y - 0.25f, g + 0.05f);
                        if (!f.HasValue) continue;
                        best = f.Value;
                    }
                    floors.Add(best);
                }
            // Lowest column wins: a column through the console or a seat base reads high, the true floor pan is the lowest.
            floors.Sort();
            fit.FloorY = floors.Count > 0 ? floors[0] : g + 0.25f;

            float hipsY = Mathf.Max(fit.Wheel.y - WheelAboveHips, fit.FloorY + HipsAboveFloorMin);
            fit.RoofY = RoofAbove(model, root, hipsX, hipsZ - 0.10f, hipsY + 0.55f);
            fit.Hips = new Vector3(hipsX, hipsY, hipsZ);
            fit.Seat = fit.Hips - ClipHipsOffset;
            return fit;
        }

        // Roof underside in the head column: the highest exterior surface above minY, down to its liner (within 8 cm).
        private static float RoofAbove(Transform model, Transform root, float x, float z, float minY)
        {
            var column = ColumnCrossings(model, root, x, z, true);
            float top = float.NegativeInfinity;
            foreach (var c in column) if (c.y > minY && c.y > top) top = c.y;
            if (float.IsNegativeInfinity(top)) return float.PositiveInfinity;
            float under = top;
            foreach (var c in column) if (c.y >= top - 0.08f && c.y < under) under = c.y;
            return under;
        }

        private static float? HighestBelow(List<(float y, string n)> column, float maxY, float minY)
        {
            float best = float.NegativeInfinity;
            foreach (var c in column) if (c.y < maxY && c.y > minY && c.y > best) best = c.y;
            return float.IsNegativeInfinity(best) ? (float?)null : best;
        }

        // All mesh surfaces crossed by a vertical line at root-space (x, z): (height, mesh name), highest first.
        // exteriorOnly skips interior parts (seats, dashboard) unless the whole car is one mesh.
        private static List<(float y, string n)> ColumnCrossings(Transform model, Transform root, float x, float z, bool exteriorOnly)
        {
            if (probeModel != model || probeTriangles == null)
            {
                probeModel = model;
                probeTriangles = new List<(Vector3, Vector3, Vector3, string)>();
                string body = null; int most = 0;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.sharedMesh.vertexCount > most && !IsWheelName(mf.name)) { most = mf.sharedMesh.vertexCount; body = mf.name; }
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m == null || !m.isReadable || IsWheelName(mf.name)) continue;
                    string tag = (mf.name == body ? "#" : "") + mf.name;
                    Vector3[] v = m.vertices; int[] t = m.triangles;
                    var w = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) w[i] = root.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                    for (int i = 0; i < t.Length; i += 3) probeTriangles.Add((w[t[i]], w[t[i + 1]], w[t[i + 2]], tag));
                }
            }
            var hits = new List<(float y, string n)>();
            foreach (var tr in probeTriangles)
            {
                if (exteriorOnly && !tr.n.StartsWith("#") && IsInteriorName(tr.n)) continue;
                float d1 = (x - tr.b.x) * (tr.a.z - tr.b.z) - (tr.a.x - tr.b.x) * (z - tr.b.z);
                float d2 = (x - tr.c.x) * (tr.b.z - tr.c.z) - (tr.b.x - tr.c.x) * (z - tr.c.z);
                float d3 = (x - tr.a.x) * (tr.c.z - tr.a.z) - (tr.c.x - tr.a.x) * (z - tr.a.z);
                if ((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0)) continue;
                float den = (tr.b.z - tr.c.z) * (tr.a.x - tr.c.x) + (tr.c.x - tr.b.x) * (tr.a.z - tr.c.z);
                if (Mathf.Abs(den) < 1e-6f) continue;
                float l1 = ((tr.b.z - tr.c.z) * (x - tr.c.x) + (tr.c.x - tr.b.x) * (z - tr.c.z)) / den;
                float l2 = ((tr.c.z - tr.a.z) * (x - tr.c.x) + (tr.a.x - tr.c.x) * (z - tr.c.z)) / den;
                hits.Add((l1 * tr.a.y + l2 * tr.b.y + (1f - l1 - l2) * tr.c.y, tr.n));
            }
            hits.Sort((p, q) => q.y.CompareTo(p.y));
            return hits;
        }

        private static bool IsWheelName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("wheel") && !n.Contains("steer");
        }

        private static bool IsInteriorName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("interior") || n.Contains("box") || n.Contains("object") || n.Contains("salon") || n.Contains("dashboard")
                || n.Contains("seat") || n.Contains("helm") || n.Contains("steer") || n.Contains("car_40") || n.Contains("car_30");
        }

        // Driver door outer surface and handle (root space); falls back to the body side when no door was found.
        private static DoorGeometry DoorGeometryOf(ModelInfo info, Bounds body, Vector3 seat, Transform root)
        {
            var d = new DoorGeometry { Found = info.DriverDoor.Count > 0 };
            if (d.Found)
            {
                Bounds b = RootBounds(info.DriverDoor, root);
                d.OuterX = b.min.x; d.HandleZ = b.min.z + 0.15f; d.CenterZ = b.center.z;
            }
            else { d.OuterX = body.min.x + 0.05f; d.HandleZ = seat.z - 0.25f; d.CenterZ = seat.z; }
            return d;
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static void FitWheelCollider(WheelCollider wc, Vector3 wheelCenter, float radius, Transform root)
        {
            wc.radius = radius;
            wc.center = Vector3.zero;
            float rest = wc.suspensionDistance * (1f - wc.suspensionSpring.targetPosition);
            wc.transform.position = root.TransformPoint(wheelCenter + Vector3.up * rest);
            wc.transform.rotation = root.rotation;
        }

        private static void FitBodyColliders(Transform holder, Bounds body, float ground, Transform root)
        {
            foreach (var bc in holder.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
            Transform cols = holder.Find("Colliders"); if (cols == null) cols = NewChild("Colliders", holder, holder.gameObject.layer);
            cols.localPosition = Vector3.zero; cols.localRotation = Quaternion.identity; cols.localScale = Vector3.one;
            foreach (var bc in cols.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
            float bottom = ground + Mathf.Min(0.3f, body.size.y * 0.15f);
            float belt = body.min.y + body.size.y * 0.55f;
            var lower = cols.gameObject.AddComponent<BoxCollider>();
            SetBox(lower, new Vector3(body.center.x, (bottom + belt) * 0.5f, body.center.z), new Vector3(body.size.x * 0.96f, belt - bottom, body.size.z * 0.98f), cols, root);
            var cabin = cols.gameObject.AddComponent<BoxCollider>();
            float top = body.max.y - 0.03f;
            SetBox(cabin, new Vector3(body.center.x, (belt + top) * 0.5f, body.center.z - body.size.z * 0.04f), new Vector3(body.size.x * 0.8f, top - belt, body.size.z * 0.55f), cols, root);
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
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
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

        private static void FitLights(Transform root, Bounds body, float g)
        {
            float frontZ = body.max.z - 0.12f, rearZ = body.min.z + 0.10f;
            float lampY = g + Mathf.Clamp(body.size.y * 0.42f, 0.55f, 1.1f);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                string n = light.name.ToLowerInvariant();
                if (light.transform.parent != null && light.transform.parent.name.StartsWith("RCCExhaust")) continue;
                float side = light.transform.localPosition.x >= 0f ? 1f : -1f;
                bool front = n.Contains("head") || (n.Contains("indicator") && light.transform.localPosition.z > 0f);
                light.transform.localPosition = new Vector3(side * body.extents.x * 0.7f, lampY, front ? frontZ : rearZ);
            }
            Transform exhausts = root.Find("Exhausts");
            if (exhausts != null) foreach (Transform e in exhausts) e.localPosition = new Vector3(-body.extents.x * 0.45f, g + 0.3f, body.min.z + 0.05f);
        }

        private static void FitHitboxes(Transform bullet, Bounds body, float g, Vector3 seatDelta, Transform root)
        {
            if (bullet == null) return;
            foreach (var mc in bullet.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
            foreach (var bc in bullet.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
            float belt = body.min.y + body.size.y * 0.55f;
            var lower = bullet.gameObject.AddComponent<BoxCollider>();
            SetBox(lower, new Vector3(body.center.x, (g + 0.25f + belt) * 0.5f, body.center.z), new Vector3(body.size.x, belt - g - 0.25f, body.size.z), bullet, root);
            Transform glass = bullet.Find("GlassHitbox");
            if (glass != null)
            {
                foreach (var mc in glass.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
                var box = glass.GetComponent<BoxCollider>(); if (box == null) box = glass.gameObject.AddComponent<BoxCollider>();
                glass.localPosition = Vector3.zero; glass.localRotation = Quaternion.identity;
                SetBox(box, new Vector3(body.center.x, (belt + body.max.y) * 0.5f, body.center.z - body.size.z * 0.04f), new Vector3(body.size.x * 0.85f, body.max.y - belt, body.size.z * 0.58f), glass, root);
            }
            Transform zones = bullet.Find("DriverHitZones");
            if (zones != null) zones.position += root.TransformVector(seatDelta);
        }

        private static Transform MakePivot(Renderer r, string name, Transform root, float tilt)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(r.transform.parent, false);
            pivot.position = r.bounds.center;
            pivot.rotation = root.rotation * Quaternion.Euler(tilt, 0f, 0f);
            r.transform.SetParent(pivot, true);
            return pivot;
        }

        private static VehicleDoor MakeDoor(ModelInfo info, Transform model, Transform root, float g)
        {
            if (info.DriverDoor.Count == 0) return null;
            Bounds b = RootBounds(info.DriverDoor, root);
            var hinge = new GameObject("DriverDoor").transform;
            hinge.SetParent(model, false);
            hinge.position = root.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z - 0.03f));
            hinge.rotation = root.rotation;
            hinge.gameObject.layer = model.gameObject.layer;
            foreach (var r in info.DriverDoor) r.transform.SetParent(hinge, true);
            var handle = new GameObject("Handle").transform;
            handle.SetParent(hinge, false);
            handle.position = root.TransformPoint(new Vector3(b.min.x - 0.06f, g + Mathf.Clamp(b.center.y + b.extents.y * 0.35f, 0.75f, 1.3f), b.min.z + 0.15f));
            var door = hinge.gameObject.AddComponent<VehicleDoor>();
            var so = new SerializedObject(door);
            so.FindProperty("handle").objectReferenceValue = handle;
            so.FindProperty("hingeAxis").vector3Value = Vector3.up;
            so.FindProperty("doorOpenAngle").floatValue = 70f;
            so.FindProperty("edgeDirection").vector3Value = Vector3.back;
            so.ApplyModifiedPropertiesWithoutUndo();
            return door;
        }

        private static void SetupLods(GameObject holder, ModelInfo info)
        {
            var group = holder.GetComponent<LODGroup>(); if (group == null) group = holder.AddComponent<LODGroup>();
            var l0 = new List<Renderer>(info.Lod0); l0.AddRange(info.Shared);
            var lods = new List<LOD>();
            Transform h = holder.transform;
            if (info.Lod1.Count == 0 && info.Lod2.Count == 0)
            {
                AddWheels(l0, info, h);
                lods.Add(new LOD(0.02f, l0.ToArray()));
            }
            else
            {
                AddWheels(l0, info, h);
                lods.Add(new LOD(0.35f, l0.ToArray()));
                if (info.Lod1.Count > 0)
                {
                    var l1 = new List<Renderer>(info.Lod1); l1.AddRange(info.Shared); AddWheels(l1, info, h);
                    lods.Add(new LOD(info.Lod2.Count > 0 ? 0.12f : 0.02f, l1.ToArray()));
                }
                if (info.Lod2.Count > 0)
                {
                    var l2 = new List<Renderer>(info.Lod2); AddWheels(l2, info, h);
                    lods.Add(new LOD(0.02f, l2.ToArray()));
                }
            }
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        // Wheels join the LOD levels only where they sit inside the LOD group's hierarchy (traffic / drivable keep them apart).
        private static void AddWheels(List<Renderer> list, ModelInfo info, Transform holder)
        {
            foreach (var w in info.WheelRenderers) foreach (var r in w) if (r.transform.IsChildOf(holder) && !list.Contains(r)) list.Add(r);
        }

        // Player dummy: the same posed rig re-fitted to this car. Its hips sit exactly where the real player's seated pose puts
        // them, the torso is reclined onto the seat back, hands rest on the wheel rim and the feet on the cabin floor. If the head
        // would touch the roof, hips and seat move down together (never below the floor), so player and dummy keep matching.
        private static string FitSeatedDummies(Transform root, Transform seat, ModelInfo info, CabinFit cabin, Vector3 seatOld)
        {
            Transform carPlayer = seat.Find("CarPlayer");
            Transform rig = carPlayer != null ? carPlayer.Find("JasonSeated") : null;
            if (rig == null) return "no player dummy";
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            Transform hips = bones["mixamorig:Hips"], head = bones["mixamorig:Head"];
            rig.localScale = Vector3.one * PlayerScale;

            // 1. Hips on the cabin fit, torso reclined about the hips, head levelled.
            rig.position += root.TransformPoint(cabin.Hips) - hips.position;
            ApplyLean(bones, root, SeatedLeanDegrees);

            // 2. Roof over the whole head after the recline (it moves the head back, where sloping roofs come down): lower hips and
            //    seat together until the crown and the head's edges clear the roof; if the floor stops that, slide the seat
            //    forward under the higher part of the roof. The real player sits on the same seat, so both stay inside.
            float drop = 0f, slide = 0f;
            float headroom = 1f;
            Transform model = root.Find("Model");
            float roofTop = RoofTop(info, root);
            for (int pass = 0; pass < 6; pass++)
            {
                Vector3 crownRoot = root.InverseTransformPoint(head.position) + Vector3.up * HeadBoneToCrown;
                headroom = model != null ? RoofGapOverHead(model, root, crownRoot, roofTop) : 1f;
                float need = RoofClearanceMin - headroom;
                if (need <= 0.002f) break;
                float canDrop = Mathf.Max(0f, cabin.Hips.y - (cabin.FloorY + 0.07f));
                Vector3 move;
                if (canDrop > 0.005f)
                {
                    float d = Mathf.Min(need, canDrop);
                    drop += d;
                    move = Vector3.down * d;
                }
                else if (slide < 0.15f)
                {
                    float s = Mathf.Min(0.05f, 0.15f - slide);
                    slide += s;
                    move = Vector3.forward * s;
                }
                else break;
                cabin.Hips += move;
                cabin.Seat = cabin.Hips - ClipHipsOffset;
                seat.localPosition = cabin.Seat;
                rig.position += root.TransformPoint(cabin.Hips) - hips.position;
            }
            cabin.Headroom = headroom;

            // 3. Hands on the wheel rim (quarter to three), elbows bent outward.
            Vector3 wheel = root.TransformPoint(cabin.Wheel);
            foreach (int side in new[] { -1, 1 })
            {
                string sd = side < 0 ? "Left" : "Right";
                Vector3 target = wheel + root.right * (0.16f * side) + root.up * 0.05f - root.forward * 0.05f;
                TwoBoneIK(bones["mixamorig:" + sd + "Arm"], bones["mixamorig:" + sd + "ForeArm"], bones["mixamorig:" + sd + "Hand"], target, root.right * (0.6f * side) - root.up * 0.8f);
            }

            // 4. Feet flat on the cabin floor in front of the hips (knees come up in a low car).
            float chain = Vector3.Distance(bones["mixamorig:LeftUpLeg"].position, bones["mixamorig:LeftLeg"].position)
                        + Vector3.Distance(bones["mixamorig:LeftLeg"].position, bones["mixamorig:LeftFoot"].position);
            float ankleY = cabin.FloorY + 0.09f;
            float dy = Mathf.Max(0f, cabin.Hips.y - ankleY);
            float dz = Mathf.Min(0.62f, Mathf.Sqrt(Mathf.Max(0.04f, 0.92f * chain * 0.92f * chain - dy * dy)));
            foreach (int side in new[] { -1, 1 })
            {
                string sd = side < 0 ? "Left" : "Right";
                Vector3 target = root.TransformPoint(new Vector3(cabin.Hips.x + 0.14f * side, ankleY, cabin.Hips.z + dz));
                TwoBoneIK(bones["mixamorig:" + sd + "UpLeg"], bones["mixamorig:" + sd + "Leg"], bones["mixamorig:" + sd + "Foot"], target, root.up * 0.8f + root.forward * 0.5f + root.right * (0.15f * side));
            }

            string npcLog = FitNpcDriver(seat.Find("NpcSeated") ?? seat.Find("Driver"), root, cabin, seatOld);
            return $"hips {cabin.Hips.y:F2} floor {cabin.FloorY:F2} roof {(float.IsInfinity(cabin.RoofY) ? "open" : cabin.RoofY.ToString("F2"))} headroom {cabin.Headroom:F2}{(drop > 0f ? $" (seat lowered {drop:F2})" : "")}{(slide > 0f ? $" (seat forward {slide:F2})" : "")}{(cabin.WheelEstimated ? " wheel est." : "")}{npcLog}";
        }

        // Smallest gap (m) between the head and the roof, over the crown and a ring 9 cm around it (the head's width): the lowest
        // car surface above crown - 12 cm within the roof band (a sloping roof's rear edge counts, headrests well below the roof
        // do not). Negative = the head pokes through. 1 = nothing above (open top).
        private static float RoofGapOverHead(Transform model, Transform root, Vector3 crown, float roofTop)
        {
            float best = 1f;
            float bandBottom = Mathf.Max(crown.y - 0.12f, roofTop - 0.25f);
            for (int k = 0; k < 9; k++)
            {
                Vector3 p = crown + (k == 0 ? Vector3.zero : Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * 0.09f);
                foreach (var c in ColumnCrossings(model, root, p.x, p.z, false))
                {
                    if (c.y < bandBottom) continue;
                    float gap = c.y - crown.y;
                    if (gap < best) best = gap;
                }
            }
            return best;
        }

        // Rotates the torso back about the hips (spine chain) and levels the head again.
        private static void ApplyLean(Dictionary<string, Transform> bones, Transform root, float degrees)
        {
            string[] spine = { "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2" };
            foreach (string b in spine)
                if (bones.TryGetValue(b, out Transform t)) t.rotation = Quaternion.AngleAxis(-degrees / spine.Length, root.right) * t.rotation;
            if (bones.TryGetValue("mixamorig:Head", out Transform h)) h.rotation = Quaternion.AngleAxis(degrees * 0.55f, root.right) * h.rotation;
        }

        // NPC driver (static posed mesh): keeps the template's offset from the hips (the template hips were SeatToHips above its
        // seat; the real hips are now ClipHipsOffset above it), then stays under the roof.
        // The NPC driver visual is a real pedestrian's seated mesh baked in driver seat space (PedDriverRoster), so it sits on the
        // seat unmoved and unscaled; PedDriverBuilder.RepatchPrefabs puts the roster's mesh on it after the build.
        private static string FitNpcDriver(Transform npcRoot, Transform root, CabinFit cabin, Vector3 seatOld)
        {
            if (npcRoot == null) return "";
            npcRoot.localPosition = Vector3.zero;
            npcRoot.localRotation = Quaternion.identity;
            npcRoot.localScale = Vector3.one;
            float dropped = 0f;
            foreach (var mf in npcRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Vector3 top = mf.transform.TransformPoint(new Vector3(mf.sharedMesh.bounds.center.x, mf.sharedMesh.bounds.max.y, mf.sharedMesh.bounds.center.z));
                float topY = root.InverseTransformPoint(top).y;
                if (!float.IsInfinity(cabin.RoofY) && topY > cabin.RoofY - RoofClearanceMin)
                {
                    float d = Mathf.Min(topY - (cabin.RoofY - RoofClearanceMin), 0.25f);
                    mf.transform.position += root.up * -d;
                    dropped = Mathf.Max(dropped, d);
                }
            }
            return dropped > 0f ? $", npc lowered {dropped:F2}" : "";
        }

        // Gap (m) between a head-top point and the underside of the roof right above it (large if nothing is above).
        private static float RoofClearance(Transform root, ModelInfo info, Vector3 headTop, Transform ignore)
        {
            float best = 1f;
            var list = new List<Renderer>(info.Lod0); list.AddRange(info.Shared);
            foreach (var r in list)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || r.transform.IsChildOf(ignore)) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("steer")) continue;
                Matrix4x4 m = r.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w = m.MultiplyPoint3x4(v);
                    Vector3 d = w - headTop;
                    if (new Vector2(Vector3.Dot(d, root.right), Vector3.Dot(d, root.forward)).magnitude > 0.15f) continue;
                    float h = Vector3.Dot(d, root.up);
                    if (h > -0.10f && h < best) best = h;
                }
            }
            return best;
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
            Vector3 kneeWanted = a.position + dir * (Mathf.Cos(angA) * l1) + bend * (Mathf.Sin(angA) * l1);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, kneeWanted - a.position) * a.rotation;
            Vector3 endWanted = a.position + dir * d;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, endWanted - b.position) * b.rotation;
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static void ApplyHandling(RCC_CarControllerV3 rcc, VehicleSpec spec)
        {
            rcc.maxspeed = spec.MaxSpeed;
            rcc.maxEngineTorque = spec.EngineTorque;
            rcc.brakeTorque = spec.BrakeTorque;
            rcc.steerAngle = spec.SteerAngle;
            rcc.highspeedsteerAngle = spec.HighSpeedSteerAngle;
            rcc.highspeedsteerAngleAtspeed = spec.MaxSpeed * 0.8f;
            rcc.downForce = spec.DownForce;
            rcc.wheelTypeChoise = spec.DriveType;
        }

        private static void SetMeshes(VehicleDamageDeformer deformer, List<MeshFilter> meshes, float damageMultiplier)
        {
            if (deformer == null) return;
            var so = new SerializedObject(deformer);
            var prop = so.FindProperty("deformableMeshes");
            prop.arraySize = meshes.Count;
            for (int i = 0; i < meshes.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = meshes[i];
            // Template values (calibrated on the previous cars) scaled by the spec; built from the template every time.
            so.FindProperty("depthPerSpeed").floatValue *= damageMultiplier;
            so.FindProperty("maxDentPerImpact").floatValue *= damageMultiplier;
            so.FindProperty("maxTotalDisplacement").floatValue *= damageMultiplier;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ClearLightSlots(VehicleLights lights)
        {
            if (lights == null) return;
            var so = new SerializedObject(lights);
            var prop = so.FindProperty("slots");
            if (prop != null) prop.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CleanHelpers(Transform inst)
        {
            foreach (string n in HelperNames)
            {
                Transform t = FindAny(inst, new[] { n });
                if (t != null && t.GetComponentsInChildren<Renderer>(true).Length == 0) Object.DestroyImmediate(t.gameObject);
            }
            Transform wheels = FindAny(inst, new[] { "Wheels" });
            if (wheels != null && wheels.GetComponentsInChildren<Renderer>(true).Length == 0) Object.DestroyImmediate(wheels.gameObject);
        }

        private static string WireScene(List<KeyValuePair<VehicleSpec, (GameObject traffic, RCCVehicleAdapter drivable)>> built)
        {
            var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            var takeover = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            int traffic = 0;
            if (spawner != null)
            {
                var list = new List<TSTrafficSpawner.TSSpawnVehicles>();
                foreach (var kv in built)
                {
                    if (!kv.Key.UseInTraffic || kv.Value.traffic == null) continue;
                    list.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = kv.Value.traffic, frequency = kv.Key.TrafficFrequency });
                    traffic++;
                }
                Undo.RecordObject(spawner, "Assign traffic cars");
                spawner.cars = list.ToArray();
                EditorUtility.SetDirty(spawner);
            }
            if (takeover != null)
            {
                var so = new SerializedObject(takeover);
                var prop = so.FindProperty("prewarmedPrefabs");
                var drivables = new List<RCCVehicleAdapter>();
                foreach (var kv in built) if (kv.Value.drivable != null && kv.Key.UseInTraffic) drivables.Add(kv.Value.drivable);
                prop.arraySize = drivables.Count;
                for (int i = 0; i < drivables.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = drivables[i];
                so.ApplyModifiedProperties();
            }
            return $"  scene: traffic spawner {traffic} cars, takeover pool {(takeover != null ? "set" : "missing")}";
        }

        // ---------------------------------------------------------------------------------------------------------------
        private static void MakeReadable(GameObject model)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(model)) as ModelImporter;
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true; // dents need CPU access to the body mesh
            importer.SaveAndReimport();
        }

        // Names are tried in priority order (a model may also contain a lower-priority name elsewhere, e.g. a wheel mesh
        // called Wheel_R sitting under WheelColliderFL).
        private static Transform FindAny(Transform root, string[] names)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (string n in names)
                foreach (Transform t in all) if (t.name == n) return t;
            return null;
        }

        private static Bounds RootBounds(List<Renderer> renderers, Transform root)
        {
            bool init = false; Bounds b = new Bounds();
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Transform NewChild(string name, Transform parent, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void SetLayer(Transform t, int layer)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
