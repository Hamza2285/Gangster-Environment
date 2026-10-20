using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the two drivable motorbikes (Drivable_Bike_Sport, red sport bike; Drivable_Bike_Chopper, blue chopper) from the bike
    /// models already in the project (PopulationSystem Bike 1 / Bike 2, same models and paint textures as the traffic bikes).
    /// RCC V3 has no bike controller, so - like the iTS traffic bikes (BikePrefabBuilder) - each bike is an RCC car with its four
    /// wheel colliders pulled in close under the two tyres:
    /// - starts from a working drivable prefab (Drivable_Challenger) so RCC, RCCVehicleAdapter, VehicleCharacterImpact, collision
    ///   audio and the enter/exit integration are the proven ones; car body, door, glass, hitboxes, NPC driver and extra lights go;
    /// - BikeRig (shared with the traffic bikes) keeps the body upright, leans model and rider into turns, spins the real tyres and
    ///   moves them with the suspension (the RCC wheel models are hidden helper transforms);
    /// - bike weight, engine, brakes, springs and inertia are scaled for ~420 kg (bike + rider);
    /// - the rider shown while driving is the player model posed onto seat, grips and pegs (two-bone IK), scaled like the player;
    /// - mounting uses the quick mount path of PlayerVehicleInteraction (RCCVehicleAdapter.quickMount).
    /// Re-running overwrites the two prefabs (asset GUIDs kept).
    /// </summary>
    public static class DrivableBikeBuilder
    {
        private const string BaseDrivable = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Challenger.prefab";
        private const string OutDir = "Assets/_Project/Prefabs/Vehicles/Drivable";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";
        private const float PlayerModelScale = 1.15f; // matches Player_Jason/3D Model
        private const float BaseMass = 1650f;

        private class Spec
        {
            public string name, fbx, material;
            public float scale;
            public Vector3 seat, grip, peg, headlight, tail;
            public float lean;
            public Vector3 kneeHint, elbowHint;
            public float mass, torque, maxSpeed, steer;
        }

        // Seat / grip / peg / lamp points in model space (before scaling), as measured for BikePrefabBuilder.
        private static readonly Spec Sport = new Spec
        {
            name = "Drivable_Bike_Sport",
            fbx = "Assets/PopulationSystem/Models/Bike 1/Bike_1.fbx",
            material = "Assets/Prefabs/Bikes/Materials/Bike_SportBike_B.mat", // red
            scale = 0.76f,
            seat = new Vector3(0f, 1.04f, -0.47f),
            grip = new Vector3(0.32f, 1.13f, 0.66f),
            peg = new Vector3(0.27f, 0.58f, -0.45f),
            headlight = new Vector3(0f, 1.05f, 1.05f),
            tail = new Vector3(0f, 1.01f, -1.44f),
            lean = 55f,
            kneeHint = new Vector3(0.35f, 0.35f, 1f),
            elbowHint = new Vector3(1f, -0.8f, -0.2f),
            mass = 400f, torque = 140f, maxSpeed = 190f, steer = 32f
        };

        private static readonly Spec Chopper = new Spec
        {
            name = "Drivable_Bike_Chopper",
            fbx = "Assets/PopulationSystem/Models/Bike 2/Bike_2.fbx",
            material = "Assets/Prefabs/Bikes/Materials/Bike_Chopper_B.mat", // blue
            scale = 0.65f,
            seat = new Vector3(0f, 0.92f, -0.78f),
            grip = new Vector3(0.62f, 1.38f, 0.15f),
            peg = new Vector3(0.45f, 0.42f, -0.10f),
            headlight = new Vector3(0f, 1.25f, 1.2f),
            tail = new Vector3(0f, 0.71f, -1.89f),
            lean = 2f,
            kneeHint = new Vector3(0.25f, 0.7f, 0.6f),
            elbowHint = new Vector3(1f, -0.4f, -0.3f),
            mass = 450f, torque = 120f, maxSpeed = 155f, steer = 30f
        };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Drivable Bikes")]
        public static void BuildAll()
        {
            var built = new List<string> { Build(Sport), Build(Chopper) };
            Debug.Log("[DrivableBikeBuilder] Built " + string.Join(", ", built) + ". Run Tools > OpenworldCrime > Vehicles > Setup Vehicle Lights afterwards.");
        }

        private static string Build(Spec spec)
        {
            string path = OutDir + "/" + spec.name + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(BaseDrivable);
            try
            {
                root.name = spec.name;
                var car = root.GetComponent<RCC_CarControllerV3>();
                var body = root.GetComponent<Rigidbody>();
                var adapter = root.GetComponent<RCCVehicleAdapter>();

                // --- strip the car -------------------------------------------------------------------------------------------
                foreach (string n in new[] { "Model", "HoodCamera", "BulletHitbox", "CarjackStandPoint", "EjectPoint", "CabinFloor" })
                    DestroyChild(root.transform, n);
                Transform seatT = root.transform.Find("DriverSeat");
                for (int i = seatT.childCount - 1; i >= 0; i--) Object.DestroyImmediate(seatT.GetChild(i).gameObject);
                DestroyComponent<TrafficVehicleDriver>(root);
                DestroyComponent<VehicleDamageDeformer>(root);
                DestroyComponent<VehicleLights>(root);
                car.SteeringWheel = null;

                // Wheel models become hidden helpers (RCC aligns them); a disabled renderer gives RCC a bounds centre.
                foreach (Transform w in root.transform.Find("Wheel Models"))
                {
                    for (int i = w.childCount - 1; i >= 0; i--) Object.DestroyImmediate(w.GetChild(i).gameObject);
                    var proxy = new GameObject("WheelProxy");
                    proxy.transform.SetParent(w, false);
                    proxy.AddComponent<MeshFilter>();
                    proxy.AddComponent<MeshRenderer>().enabled = false;
                }

                // Lights: one headlight spot and one tail light; the rest of the car's lamp lights go.
                Light keepHead = null, keepTail = null;
                foreach (RCC_Light l in root.GetComponentsInChildren<RCC_Light>(true))
                {
                    if (keepHead == null && l.lightType == RCC_Light.LightType.HeadLight) { keepHead = l.GetComponent<Light>(); continue; }
                    if (keepTail == null && l.lightType == RCC_Light.LightType.BrakeLight) { keepTail = l.GetComponent<Light>(); continue; }
                    Object.DestroyImmediate(l.gameObject);
                }
                foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true)) Object.DestroyImmediate(trail.gameObject);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "Lights" && t.childCount == 0) Object.DestroyImmediate(t.gameObject);

                // --- bike model under a lean pivot ------------------------------------------------------------------------------
                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbx);
                var probe = (GameObject)Object.Instantiate(modelAsset);
                Bounds fb = probe.transform.Find("Tyre_F").GetComponent<Renderer>().bounds;
                Bounds rb = probe.transform.Find("Tyre_B").GetComponent<Renderer>().bounds;
                Object.DestroyImmediate(probe);
                float s = spec.scale;
                Vector3 frontCentre = fb.center * s, rearCentre = rb.center * s;
                float frontRadius = fb.extents.y * s, rearRadius = rb.extents.y * s;
                float groundY = Mathf.Min(frontCentre.y - frontRadius, rearCentre.y - rearRadius);
                // Root origin on the ground under the bike.
                Vector3 lift = new Vector3(0f, -groundY, 0f);
                frontCentre += lift;
                rearCentre += lift;

                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, visual);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model";
                model.transform.localPosition = lift;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = modelAsset.transform.localScale * s;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(spec.material);
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                Transform frontPivot = MakeWheelPivot(model.transform.Find("Tyre_F"), "FrontWheelPivot");
                Transform rearPivot = MakeWheelPivot(model.transform.Find("Tyre_B"), "RearWheelPivot");

                // --- physics ----------------------------------------------------------------------------------------------------
                float massScale = spec.mass / BaseMass;
                body.mass = spec.mass;
                const float halfTrack = 0.16f;
                foreach (RCC_WheelCollider rwc in root.GetComponentsInChildren<RCC_WheelCollider>(true))
                {
                    WheelCollider wc = rwc.GetComponent<WheelCollider>();
                    bool front = rwc.name.Contains("_F");
                    bool left = rwc.name.EndsWith("L");
                    Vector3 c = front ? frontCentre : rearCentre;
                    wc.radius = front ? frontRadius : rearRadius;
                    wc.mass = 20f;
                    wc.suspensionDistance = 0.18f;
                    JointSpring js = wc.suspensionSpring;
                    js.spring = 40000f * massScale * 1.1f;
                    js.damper = 1500f * massScale * 1.2f;
                    js.targetPosition = 0.5f;
                    wc.suspensionSpring = js;
                    float sag = spec.mass * 9.81f * 0.25f / js.spring;
                    float restExtension = Mathf.Max(0.01f, wc.suspensionDistance * (1f - js.targetPosition) - sag);
                    wc.transform.localPosition = new Vector3(left ? -halfTrack : halfTrack, c.y + restExtension, c.z);
                }
                foreach (Transform w in root.transform.Find("Wheel Models"))
                {
                    bool front = w.name.Contains("_F");
                    bool left = w.name.EndsWith("L");
                    Vector3 c = front ? frontCentre : rearCentre;
                    w.localPosition = new Vector3(left ? -halfTrack : halfTrack, c.y, c.z);
                }

                car.maxEngineTorque = spec.torque;
                car.maxspeed = spec.maxSpeed;
                car.brakeTorque = 2000f * massScale * 1.3f;
                car.steerAngle = spec.steer;
                car.highspeedsteerAngle = 4f;
                // BikeRig owns roll stability; RCC's anti-roll bars would fight it on a 32 cm track.
                car.antiRollFrontHorizontal = 0f;
                car.antiRollRearHorizontal = 0f;

                Transform com = root.transform.Find("COM");
                com.localPosition = new Vector3(0f, 0.5f, (frontCentre.z + rearCentre.z) * 0.5f);

                float length = (frontCentre.z + frontRadius) - (rearCentre.z - rearRadius);
                float midZ = (frontCentre.z + frontRadius + rearCentre.z - rearRadius) * 0.5f;
                // Solid body for bike and rider; starts above the tyres' contact so a bounce never pushes it into the road.
                var bodyCollider = new GameObject("BodyCollider") { layer = root.layer };
                bodyCollider.transform.SetParent(root.transform, false);
                var box = bodyCollider.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.95f, midZ);
                box.size = new Vector3(0.7f, 1.2f, length);

                SetBox(root.transform.Find("InteractionZone"), new Vector3(0f, 1f, midZ), new Vector3(3.2f, 2f, length + 1.2f));
                SetBox(root.transform.Find("TrafficSensorTarget"), new Vector3(0f, 0.9f, midZ), new Vector3(0.9f, 1.3f, length));

                Transform exhaust = root.transform.Find("Exhausts");
                if (exhaust != null && exhaust.childCount > 0) exhaust.GetChild(0).localPosition = new Vector3(0.2f, 0.4f, rearCentre.z - 0.2f);

                // --- rider (player model posed on the bike) -----------------------------------------------------------------
                Vector3 W(Vector3 modelPoint) => visual.TransformPoint(modelPoint * s + lift);
                GameObject rider = CreateRider();
                rider.transform.SetParent(visual, false);
                PoseRider(rider, spec, W);
                // The seat anchor is the rider's root; the rider hangs under it (inactive until driven).
                seatT.SetParent(visual, true);
                seatT.SetPositionAndRotation(rider.transform.position, visual.rotation);
                rider.transform.SetParent(seatT, true);
                rider.name = "BikeRider";
                rider.SetActive(false);

                // --- enter / exit points (left side, like getting on a real bike) -------------------------------------------------
                float seatZ = spec.seat.z * s;
                SetPoint(root.transform, "EnterPoint", new Vector3(-0.85f, 0.02f, seatZ), Vector3.right);
                SetPoint(root.transform, "ExitPoint", new Vector3(-1.15f, 0.02f, seatZ), Vector3.left);
                SetPoint(root.transform, "AlternateExitPoint", new Vector3(1.15f, 0.02f, seatZ), Vector3.right);
                SetPoint(root.transform, "InteractionPoint", new Vector3(-0.6f, 0.6f, seatZ), Vector3.right);

                // Lamps at the model's own headlight / tail light.
                if (keepHead != null)
                {
                    keepHead.transform.SetParent(visual, true);
                    keepHead.transform.position = W(spec.headlight);
                    keepHead.transform.rotation = Quaternion.LookRotation(visual.forward + Vector3.down * 0.08f);
                }
                if (keepTail != null)
                {
                    keepTail.transform.SetParent(visual, true);
                    keepTail.transform.position = W(spec.tail);
                }

                // --- lean / upright / wheels ------------------------------------------------------------------------------
                var rig = root.GetComponent<BikeRig>();
                if (rig == null) rig = root.AddComponent<BikeRig>();
                rig.visual = visual;
                rig.frontWheelVisual = frontPivot;
                rig.rearWheelVisual = rearPivot;
                foreach (WheelCollider wc in root.GetComponentsInChildren<WheelCollider>(true))
                {
                    if (wc.name == "Wheel_FL") rig.frontWheel = wc;
                    if (wc.name == "Wheel_RL") rig.rearWheel = wc;
                }
                float inertiaScale = spec.mass / 2500f;
                rig.pitchInertia = 1400f * inertiaScale;
                rig.yawInertia = 1200f * inertiaScale;
                rig.rollInertia = 430f * inertiaScale;
                rig.maxLean = 30f;
                // A player bike is thrown into corners much harder than the traffic AI drives: stiffer, well damped upright.
                rig.uprightStrength = 260f;
                rig.uprightDamping = 32f;
                rig.followSuspension = true;

                // --- adapter -------------------------------------------------------------------------------------------------
                var so = new SerializedObject(adapter);
                so.FindProperty("quickMount").boolValue = true;
                so.FindProperty("driverDoor").objectReferenceValue = null;
                so.FindProperty("cabinFloor").objectReferenceValue = null;
                so.FindProperty("npcDriver").objectReferenceValue = null;
                so.FindProperty("driverDummy").objectReferenceValue = rider;
                so.FindProperty("driverSeat").objectReferenceValue = seatT;
                so.FindProperty("maxEnterSpeed").floatValue = 1.5f;
                so.FindProperty("maxExitSpeed").floatValue = 1.5f;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return spec.name;
        }

        // The player's model with the player's materials, scaled like Player_Jason/3D Model; the Animator is only kept for the IK
        // bone lookup and removed after posing.
        private static GameObject CreateRider()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var playerAnimator = player.GetComponent<Animator>();
            string fbxPath = AssetDatabase.GetAssetPath(playerAnimator.avatar);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var rider = (GameObject)Object.Instantiate(fbx);
            var animator = rider.GetComponent<Animator>();
            if (animator == null) animator = rider.AddComponent<Animator>();
            animator.avatar = playerAnimator.avatar;
            rider.transform.localScale = Vector3.one * PlayerModelScale;

            var playerMats = new Dictionary<string, Material[]>();
            foreach (SkinnedMeshRenderer r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) playerMats[r.sharedMesh != null ? r.sharedMesh.name : r.name] = r.sharedMaterials;
            foreach (SkinnedMeshRenderer r in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh != null && playerMats.TryGetValue(r.sharedMesh.name, out Material[] mats)) r.sharedMaterials = mats;
                r.updateWhenOffscreen = false;
            }
            return rider;
        }

        private static void PoseRider(GameObject rider, Spec m, System.Func<Vector3, Vector3> W)
        {
            var a = rider.GetComponent<Animator>();
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            Transform hips = B(HumanBodyBones.Hips);
            Vector3 hipTarget = W(m.seat) + Vector3.up * 0.1f;
            rider.transform.position += hipTarget - hips.position;

            foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                Transform t = B(b);
                if (t != null) t.rotation = Quaternion.AngleAxis(m.lean / 3f, Vector3.right) * t.rotation;
            }
            Transform neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            if (neck != null) neck.rotation = Quaternion.AngleAxis(-m.lean * 0.45f, Vector3.right) * neck.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(-m.lean * 0.35f, Vector3.right) * head.rotation;

            foreach (int side in new[] { -1, 1 })
            {
                bool left = side < 0;
                Vector3 mirror = new Vector3(side, 1f, 1f);
                Transform thigh = B(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform calf = B(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform foot = B(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform toe = B(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                TwoBoneIK(thigh, calf, foot, W(Vector3.Scale(m.peg, mirror)) + Vector3.up * 0.07f, Vector3.Scale(m.kneeHint, mirror));
                if (toe != null) Aim(foot, toe.position - foot.position, new Vector3(side * 0.15f, -0.35f, 1f));

                Transform upper = B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform fore = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Transform finger = B(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                TwoBoneIK(upper, fore, hand, W(Vector3.Scale(m.grip, mirror)), Vector3.Scale(m.elbowHint, mirror));
                if (finger != null) Aim(hand, finger.position - hand.position, new Vector3(side * 0.35f, -0.25f, 1f));
            }
            Object.DestroyImmediate(a);
        }

        private static void Aim(Transform bone, Vector3 current, Vector3 wanted)
        {
            if (bone == null || current.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(current, wanted.normalized) * bone.rotation;
        }

        // Analytic two-bone IK (same as BikePrefabBuilder).
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

        private static Transform MakeWheelPivot(Transform tyre, string name)
        {
            var mesh = tyre.GetComponent<MeshFilter>().sharedMesh;
            Vector3 centre = tyre.TransformPoint(mesh.bounds.center);
            var pivot = new GameObject(name).transform;
            pivot.SetParent(tyre.parent, false);
            pivot.position = centre;
            pivot.rotation = tyre.parent.rotation;
            tyre.SetParent(pivot, true);
            return pivot;
        }

        private static void SetPoint(Transform root, string name, Vector3 local, Vector3 forward)
        {
            Transform t = root.Find(name);
            if (t == null) return;
            t.localPosition = local;
            t.localRotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void SetBox(Transform t, Vector3 centre, Vector3 size)
        {
            if (t == null || !t.TryGetComponent(out BoxCollider box)) return;
            box.center = centre;
            box.size = size;
        }

        private static void DestroyChild(Transform root, string name)
        {
            Transform t = root.Find(name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static void DestroyComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Object.DestroyImmediate(c);
        }
    }
}
