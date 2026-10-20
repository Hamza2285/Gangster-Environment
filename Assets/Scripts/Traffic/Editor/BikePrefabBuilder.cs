using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds traffic motorbikes with posed NPC riders from the two bike models.
// Each bike starts as a copy of a working iTS car prefab (so the traffic AI, lights, horns and all the
// traffic helpers treat it like any other vehicle), with the car body swapped for the bike model, the four
// wheel colliders pulled in close under the two tyres, physics scaled to a motorbike's weight, a BikeRig
// that keeps it upright and leans it into turns, and a pedestrian character posed in the riding position.
public static class BikePrefabBuilder
{
    const string BaseCar = "Assets/iTS/Vehicles/Car16.prefab";
    const string OutDir = "Assets/Prefabs/Bikes";
    const string PeopleDir = "Assets/PopulationSystem/Prefabs/People Prefabs/";

    // Seat, handlebar grip and footpeg positions on each model (model space, before scaling), read off
    // side and top renders of the models.
    class BikeModel
    {
        public string name, fbx, material;
        public float scale;
        public Vector3 seat, grip, peg, tail, indicator;
        public float lean;            // rider's forward lean in degrees
        public Vector3 kneeHint, elbowHint;
    }

    static readonly BikeModel Sport = new BikeModel
    {
        name = "SportBike",
        fbx = "Assets/PopulationSystem/Models/Bike 1/Bike_1.fbx",
        material = "Assets/PopulationSystem/Models/Bike 1/materials/Material #24.mat",
        scale = 0.76f,
        seat = new Vector3(0f, 1.04f, -0.47f),
        grip = new Vector3(0.32f, 1.13f, 0.66f),
        peg = new Vector3(0.27f, 0.58f, -0.45f),
        tail = new Vector3(0f, 1.01f, -1.44f),
        indicator = new Vector3(0.43f, 0.95f, 0.99f),
        lean = 55f,
        kneeHint = new Vector3(0.35f, 0.35f, 1f),
        elbowHint = new Vector3(1f, -0.8f, -0.2f),
    };

    static readonly BikeModel Chopper = new BikeModel
    {
        name = "Chopper",
        fbx = "Assets/PopulationSystem/Models/Bike 2/Bike_2.fbx",
        material = "Assets/PopulationSystem/Models/Bike 2/materials/02 - Default.mat",
        scale = 0.65f,
        seat = new Vector3(0f, 0.92f, -0.78f),
        grip = new Vector3(0.62f, 1.38f, 0.15f),
        peg = new Vector3(0.45f, 0.42f, -0.10f),
        tail = new Vector3(0f, 0.71f, -1.89f),
        indicator = new Vector3(0.28f, 1.25f, 1.09f),
        lean = 2f,
        kneeHint = new Vector3(0.25f, 0.7f, 0.6f),
        elbowHint = new Vector3(1f, -0.4f, -0.3f),
    };

    class Variant
    {
        public BikeModel model;
        public string paint, rider, suffix;
    }

    static readonly Variant[] Variants =
    {
        new Variant { model = Sport, paint = "Assets/PopulationSystem/Models/Bike 1/textures/crb97-t2.png", rider = "Man_22", suffix = "A" },
        new Variant { model = Sport, paint = "Assets/PopulationSystem/Models/Bike 1/crb97-t1.png", rider = "Girl_33", suffix = "B" },
        new Variant { model = Chopper, paint = "Assets/PopulationSystem/Models/Bike 2/textures/bike_t3.png", rider = "Man_55", suffix = "A" },
        new Variant { model = Chopper, paint = "Assets/PopulationSystem/Models/Bike 2/bike_t1.png", rider = "Man_88", suffix = "B" },
        new Variant { model = Chopper, paint = "Assets/PopulationSystem/Models/Bike 2/bike_t5.png", rider = "Girl_66", suffix = "C" },
    };

    // Bikes keep the base car's proven weight, suspension and engine: a light body gets shoved off its lane
    // by 2.5 t cars and is less stable on car-tuned wheels. Weight isn't visible; the model and lean are.
    const float BikeMass = 2500f;

    [MenuItem("Tools/Traffic/Build Bike Prefabs")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(OutDir + "/Materials");
        AssetDatabase.Refresh();
        var built = new List<string>();
        foreach (var v in Variants) built.Add(Build(v));
        Debug.Log("[Bikes] Built " + built.Count + " bike prefabs: " + string.Join(", ", built.ToArray()));
    }

    [MenuItem("Tools/Traffic/Add Bikes To Traffic")]
    public static void AddToTraffic()
    {
        var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>();
        if (spawner == null) { Debug.LogError("[Bikes] No TSTrafficSpawner in the open scene."); return; }
        var entryType = spawner.cars.GetType().GetElementType();
        var list = new List<object>();
        foreach (var e in spawner.cars)
        {
            var go = entryType.GetField("cars").GetValue(e) as GameObject;
            // Re-running replaces the old bike entries; empty/broken entries would crash the spawner.
            if (go == null || go.name.StartsWith("Bike_")) continue;
            list.Add(e);
        }
        int added = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab Bike_", new[] { OutDir }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.GetComponent<BikeRig>() == null) continue;
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("cars").SetValue(entry, prefab);
            entryType.GetField("frequency").SetValue(entry, 0);
            list.Add(entry);
            added++;
        }
        var arr = System.Array.CreateInstance(entryType, list.Count);
        for (int i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
        Undo.RecordObject(spawner, "Add bikes to traffic");
        typeof(TSTrafficSpawner).GetField("cars").SetValue(spawner, arr);
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        Debug.Log("[Bikes] Traffic spawner now has " + list.Count + " vehicle entries, " + added + " of them bikes.");
    }

    static string Build(Variant v)
    {
        BikeModel m = v.model;
        string name = "Bike_" + m.name + "_" + v.suffix;
        string path = OutDir + "/" + name + ".prefab";
        // Built from a fresh copy of the base car and saved over any existing bike prefab, which keeps its
        // asset ID, so the traffic spawner's references survive a rebuild.
        var root = PrefabUtility.LoadPrefabContents(BaseCar);
        try
        {
            root.name = name;
            var car = root.GetComponent<TSSimpleCar>();
            var body = root.GetComponent<Rigidbody>();

            // Strip the car body, lights and tyre meshes; keep the wheel colliders and their Tire holders.
            foreach (var n in new[] { "Center", "HaloLight-Right", "HaloLight-Left", "BrakeLight1", "BrakeLight2" })
            {
                var t = root.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            foreach (var wc in root.GetComponentsInChildren<WheelCollider>(true))
            {
                var tire = wc.transform.Find("Tire");
                if (tire == null) continue;
                for (int i = tire.childCount - 1; i >= 0; i--) Object.DestroyImmediate(tire.GetChild(i).gameObject);
                // iTS's wheel script (which the anti-roll bars read) expects a renderer under each wheel;
                // the bike's real tyres are spun by BikeRig, so this one stays invisible.
                var proxy = new GameObject("WheelProxy");
                proxy.transform.SetParent(tire, false);
                proxy.AddComponent<MeshFilter>();
                proxy.AddComponent<MeshRenderer>().enabled = false;
            }

            // Bike model, scaled, under a lean pivot on the ground.
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(m.fbx);
            var probe = (GameObject)Object.Instantiate(modelAsset);
            Transform frontTyre = probe.transform.Find("Tyre_F"), rearTyre = probe.transform.Find("Tyre_B");
            Bounds fb = frontTyre.GetComponent<Renderer>().bounds, rb = rearTyre.GetComponent<Renderer>().bounds;
            float s = m.scale;
            Vector3 frontCentre = fb.center * s, rearCentre = rb.center * s;
            float frontRadius = fb.extents.y * s, rearRadius = rb.extents.y * s;
            float groundY = Mathf.Min(frontCentre.y - frontRadius, rearCentre.y - rearRadius);
            Object.DestroyImmediate(probe);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, groundY, 0f);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, visual);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.localPosition = new Vector3(0f, -groundY, 0f);
            model.transform.localRotation = Quaternion.identity;
            // Keep the file's own unit scale (the sport bike is exported in centimetres, i.e. scale 100).
            model.transform.localScale = modelAsset.transform.localScale * s;

            // Paint.
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(m.material);
            var paintTex = AssetDatabase.LoadAssetAtPath<Texture2D>(v.paint);
                string matPath = OutDir + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(baseMat); AssetDatabase.CreateAsset(mat, matPath); }
            else mat.CopyPropertiesFromMaterial(baseMat);
            if (paintTex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", paintTex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", paintTex);
            }
            EditorUtility.SetDirty(mat);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            // Wheel spin pivots at the true tyre centres (the sport bike's tyres pivot at the model origin).
            Transform frontPivot = MakeWheelPivot(model.transform.Find("Tyre_F"), "FrontWheelPivot");
            Transform rearPivot = MakeWheelPivot(model.transform.Find("Tyre_B"), "RearWheelPivot");

            // Physics: four colliders pulled in close under the two tyres.
            const float halfTrack = 0.2f;
            const float suspension = 0.2f;   // the base car's travel; less bottoms out under the car's weight
            float spring = BikeMass / body.mass;
            foreach (var wc in root.GetComponentsInChildren<WheelCollider>(true))
            {
                bool front = wc.name.Contains("F");
                bool left = wc.name.Contains("L");
                Vector3 c = front ? frontCentre : rearCentre;
                wc.radius = front ? frontRadius : rearRadius;
                wc.suspensionDistance = suspension;
                var js = wc.suspensionSpring;
                js.spring *= spring;
                js.damper *= spring;
                wc.suspensionSpring = js;
                // Where the wheel settles under load (a quarter of the weight on each spring), so the tyre
                // meshes sit exactly on the road instead of floating or sinking.
                float sag = BikeMass * 9.81f * 0.25f / js.spring;
                float restExtension = Mathf.Max(0.01f, suspension * (1f - js.targetPosition) - sag);
                wc.transform.localPosition = new Vector3(left ? -halfTrack : halfTrack, c.y + restExtension, c.z);

            }
            foreach (var bar in root.GetComponents<TSAntiRollBar>()) bar.coefficient *= spring;
            car.EngineTorque *= spring;
            car.brakeTorque *= spring;
            body.mass = BikeMass;

            var cog = root.transform.Find("CoG");
            if (cog != null) cog.localPosition = new Vector3(0f, groundY + 0.25f, (frontCentre.z + rearCentre.z) * 0.5f);

            // Body collider around bike and rider.
            float length = (frontCentre.z + frontRadius) - (rearCentre.z - rearRadius);
            var box = root.GetComponent<BoxCollider>();
            // Starts 30 cm above the road like the car's body collider: any lower and a small bounce pushes
            // it into the road surface, which launches the bike into the air.
            box.size = new Vector3(0.8f, 1.4f, length);
            box.center = new Vector3(0f, groundY + 0.3f + 0.7f, (frontCentre.z + frontRadius + rearCentre.z - rearRadius) * 0.5f);

            // Tail light (lit while braking) and front indicators (blink before turns).
            car.brakeLigths = new Renderer[] { Lamp(visual, "BrakeLight", Local(m.tail, s, groundY) + new Vector3(0f, 0f, -0.03f), new Color(1f, 0.05f, 0.03f), false) };
            car.turnRightLight = Lamp(visual, "TurnRight", Local(new Vector3(m.indicator.x, m.indicator.y, m.indicator.z), s, groundY), new Color(1f, 0.6f, 0.05f), true).gameObject;
            car.turnLeftLight = Lamp(visual, "TurnLeft", Local(new Vector3(-m.indicator.x, m.indicator.y, m.indicator.z), s, groundY), new Color(1f, 0.6f, 0.05f), true).gameObject;

            // Rider.
            var riderAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PeopleDir + v.rider + ".prefab");
            var rider = (GameObject)PrefabUtility.InstantiatePrefab(riderAsset, visual);
            PrefabUtility.UnpackPrefabInstance(rider, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rider.name = "Rider";
            var animator = rider.GetComponent<Animator>();
            if (animator != null) Object.DestroyImmediate(animator);
            PoseRider(rider.transform, m, s, groundY, visual);

            // Lean, upright and wheel spin.
            var rig = root.AddComponent<BikeRig>();
            rig.visual = visual;
            rig.frontWheelVisual = frontPivot;
            rig.rearWheelVisual = rearPivot;
            foreach (var wc in root.GetComponentsInChildren<WheelCollider>(true))
            {
                if (wc.name.Contains("FL")) rig.frontWheel = wc;
                if (wc.name.Contains("RL")) rig.rearWheel = wc;
            }
            root.AddComponent<BikeCrash>();   // knocked over, rider ragdolled, when the player's car hits it

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return name;
    }

    static Vector3 Local(Vector3 modelPoint, float scale, float groundY)
    {
        // Model space -> Visual space (Visual origin is on the ground, model is lifted back by -groundY).
        return modelPoint * scale - new Vector3(0f, groundY, 0f);
    }

    static Transform MakeWheelPivot(Transform tyre, string name)
    {
        // From the mesh itself: renderer bounds aren't reliable inside the prefab editing scene.
        var mesh = tyre.GetComponent<MeshFilter>().sharedMesh;
        var centre = tyre.TransformPoint(mesh.bounds.center);
        var pivot = new GameObject(name).transform;
        pivot.SetParent(tyre.parent, false);
        pivot.position = centre;
        pivot.rotation = tyre.parent.rotation;
        tyre.SetParent(pivot, true);
        return pivot;
    }

    static Renderer Lamp(Transform parent, string name, Vector3 localPos, Color colour, bool startOff)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(0.09f, 0.05f, 0.03f);
        string matPath = OutDir + "/Materials/Lamp_" + ColorUtility.ToHtmlStringRGB(colour) + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", colour);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (name == "BrakeLight") r.enabled = false;
        if (startOff) go.SetActive(false);
        return r;
    }

    // ---------- rider pose ----------

    static void PoseRider(Transform rider, BikeModel m, float s, float groundY, Transform visual)
    {
        rider.localPosition = Vector3.zero;
        rider.localRotation = Quaternion.identity;
        Transform Bone(string n)
        {
            foreach (var t in rider.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }
        Transform pelvis = Bone("Bip01 Pelvis");
        Transform spine = Bone("Bip01 Spine"), spine1 = Bone("Bip01 Spine1"), spine2 = Bone("Bip01 Spine2");
        Transform neck = Bone("Bip01 Neck"), head = Bone("Bip01 Head");

        // World-space targets (the prefab root is at the origin facing +Z, Visual has no rotation yet).
        Vector3 W(Vector3 modelPoint) { return visual.TransformPoint(Local(modelPoint, s, groundY)); }
        Vector3 hip = W(m.seat) + Vector3.up * 0.09f;
        rider.position += hip - pelvis.position;

        // Lean the torso forward over the tank, head up to look down the road.
        foreach (var b in new[] { spine, spine1, spine2 })
            if (b != null) b.rotation = Quaternion.AngleAxis(m.lean / 3f, Vector3.right) * b.rotation;
        if (neck != null) neck.rotation = Quaternion.AngleAxis(-m.lean * 0.45f, Vector3.right) * neck.rotation;
        if (head != null) head.rotation = Quaternion.AngleAxis(-m.lean * 0.35f, Vector3.right) * head.rotation;

        foreach (int side in new[] { -1, 1 })
        {
            string lr = side < 0 ? "L" : "R";
            Vector3 mirror = new Vector3(side, 1f, 1f);

            // Legs: feet on the pegs, knees forward and slightly out.
            Transform thigh = Bone("Bip01 " + lr + " Thigh"), calf = Bone("Bip01 " + lr + " Calf"), foot = Bone("Bip01 " + lr + " Foot"), toe = Bone("Bip01 " + lr + " Toe0");
            Vector3 peg = W(Vector3.Scale(m.peg, mirror));
            TwoBoneIK(thigh, calf, foot, peg + Vector3.up * 0.06f, Vector3.Scale(m.kneeHint, mirror));
            if (toe != null) Aim(foot, toe.position - foot.position, new Vector3(side * 0.15f, -0.35f, 1f));

            // Arms: hands on the grips, elbows out and down.
            Transform upper = Bone("Bip01 " + lr + " UpperArm"), fore = Bone("Bip01 " + lr + " Forearm"), hand = Bone("Bip01 " + lr + " Hand"), finger = Bone("Bip01 " + lr + " Finger2");
            Vector3 grip = W(Vector3.Scale(m.grip, mirror));
            TwoBoneIK(upper, fore, hand, grip, Vector3.Scale(m.elbowHint, mirror));
            if (finger != null) Aim(hand, finger.position - hand.position, new Vector3(side * 0.35f, -0.25f, 1f));
        }
    }

    // Turns bone so that its current pointing direction becomes 'wanted'.
    static void Aim(Transform bone, Vector3 current, Vector3 wanted)
    {
        if (bone == null || current.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.FromToRotation(current, wanted.normalized) * bone.rotation;
    }

    // Analytic two-bone IK in world space: a (hip/shoulder) -> b (knee/elbow) -> c (ankle/wrist) reaching
    // target, bending toward 'pole'. Works with any bone axis convention.
    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
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
}
