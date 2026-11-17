using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Game/Set Up Car Showroom: builds the car showroom's own scene from the Show Room package (with display cars on its
// bays), and the way in from the city: a door at the Parking Plaza with a map icon. Safe to run again.
public static class ShowroomSetup
{
    const string ScenePath = "Assets/Scenes/Car Showroom Interior.unity";
    const string ShowroomPrefabPath = "Assets/Show Room.prefab";
    const string Folder = "Assets/Game/Showroom";
    const string MapIconPath = Folder + "/Car Showroom Map Icon.png";
    const string TriggerLayer = "Triggers";
    const string Name = "Car Showroom";
    static readonly Vector3 InteriorOrigin = new Vector3(3400f, 30f, 3400f);

    // The cars on show: one per display bay, ground floor first, then the mezzanine.
    const string DrivableFolder = "Assets/_Project/Prefabs/Vehicles/Drivable/";
    // prefab, name on the card, class on the card
    static readonly string[][] DisplayCars =
    {
        new[] { "Drivable_GWagon",    "Ironclad G4",     "Luxury SUV" },
        new[] { "Drivable_Car1",      "Volta GT-E",      "Sports" },
        new[] { "Drivable_Talenter",  "Talenter",        "Sedan" },
        new[] { "Drivable_Khodge2",   "Khodge Vandal",   "Muscle" },
        new[] { "Drivable_WMB",       "WMB Executive",   "Luxury Sedan" },
        new[] { "Drivable_Car3",      "Vortex R",        "Super" },
        new[] { "Drivable_LandSover", "Land Sover",      "Off-road SUV" },
    };

    [MenuItem("Game/Set Up Car Showroom")]
    static void Run()
    {
        Debug.Log(SetUp(DefaultDoor, DefaultFacing));
    }

    // In front of the Parking Plaza, on its pavement.
    public static Vector3 DefaultDoor = new Vector3(838f, 27.7f, -317f);
    public static Vector3 DefaultFacing = Vector3.left;

    public static string SetUp(Vector3 door, Vector3 facing)
    {
        var city = SceneManager.GetActiveScene();
        if (!city.name.Contains("Gameplay")) return "Open the gameplay scene first (active: " + city.name + ")";
        Directory.CreateDirectory(Folder);
        int cars = BuildScene();
        var icon = MakeMapIcon();
        WireCity(city, door, facing, icon);
        EditorSceneManager.SaveScene(city);
        AssetDatabase.SaveAssets();
        return "[ShowroomSetup] scene " + ScenePath + " with " + cars + " display cars; door at " + door + " facing " + facing;
    }

    // ---------- The showroom's own scene ----------

    static int BuildScene()
    {
        var city = SceneManager.GetActiveScene();
        var existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        var root = new GameObject(Name + " Interior");
        root.transform.position = InteriorOrigin;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShowroomPrefabPath);
        var showroom = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        showroom.transform.SetParent(root.transform, false);

        // The showroom was made as a scene of its own: its sun and global look must not take over the city.
        foreach (var camera in showroom.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
        foreach (var light in showroom.GetComponentsInChildren<Light>(true)) if (light.type == LightType.Directional) light.gameObject.SetActive(false);
        foreach (var volume in showroom.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true))
        {
            volume.isGlobal = false;
            volume.blendDistance = 1f;
            var box = volume.gameObject.GetComponent<BoxCollider>();
            if (box == null) box = volume.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = volume.transform.InverseTransformPoint(root.transform.position + new Vector3(0f, 5f, -1f));
            box.size = new Vector3(30f, 11f, 28f);
            volume.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        AddColliders(root.transform, showroom.transform);
        int cars = PlaceCars(root.transform, showroom.transform);

        AddStaff(root.transform);
        root.AddComponent<ShowroomCarInfo>();
        var interior = root.AddComponent<ShowroomInterior>();
        interior.arrival = Marker(root.transform, "Arrival", new Vector3(0f, 0f, 9.8f), 180f);

        var exit = new GameObject("Showroom Exit");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 1f, 11.2f);
        int layer = LayerMask.NameToLayer(TriggerLayer);
        if (layer >= 0) exit.layer = layer;
        var trigger = exit.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(5f, 2f, 1.8f);
        exit.AddComponent<ShowroomExit>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        SceneManager.SetActiveScene(city);
        EditorSceneManager.CloseScene(scene, true);

        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        return cars;
    }

    // The package has no collision: its own meshes for the building and furniture, plus a floor and walls to be safe.
    static void AddColliders(Transform root, Transform showroom)
    {
        var holder = new GameObject("Showroom Collision").transform;
        holder.SetParent(root, false);
        foreach (var renderer in showroom.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer.GetComponentInParent<LODGroup>() != null && !IsFirstLod(renderer)) continue;
            string path = PathOf(renderer.transform, showroom);
            // The city painted outside the windows, signs overhead, the ceiling and little things on desks stay out of the way.
            if (path.StartsWith("Mirage City Exterior") || path.StartsWith("Signage") || path.StartsWith("Ceiling")) continue;
            var b = renderer.bounds;
            if (Mathf.Max(b.size.x, b.size.y, b.size.z) < 0.35f) continue;
            var go = new GameObject(renderer.name);
            go.transform.SetParent(holder, false);
            go.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            go.transform.localScale = renderer.transform.lossyScale;
            go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }
        Box(holder, "Floor", new Vector3(0f, -0.1f, -1f), new Vector3(28.5f, 0.2f, 26.5f));
        Box(holder, "Back Wall", new Vector3(0f, 4.5f, -14.2f), new Vector3(28.5f, 9f, 0.3f));
        Box(holder, "Front Wall Left", new Vector3(8.6f, 4.5f, 12.15f), new Vector3(11.3f, 9f, 0.3f));
        Box(holder, "Front Wall Right", new Vector3(-8.6f, 4.5f, 12.15f), new Vector3(11.3f, 9f, 0.3f));
        Box(holder, "Front Doors", new Vector3(0f, 4.5f, 12.4f), new Vector3(6f, 9f, 0.3f));
        Box(holder, "Left Wall", new Vector3(-14.2f, 4.5f, -1f), new Vector3(0.3f, 9f, 26.5f));
        Box(holder, "Right Wall", new Vector3(14.2f, 4.5f, -1f), new Vector3(0.3f, 9f, 26.5f));

        // The staircase's steps snag the character: a smooth slope lies over them, from the ground floor up to the mezzanine.
        Vector3 bottom = new Vector3(10.5f, 0f, -0.6f), top = new Vector3(10.5f, 4.1f, -7.45f);
        Vector3 along = top - bottom;
        var slope = new GameObject("Stair Slope");
        slope.transform.SetParent(holder, false);
        var rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
        slope.transform.localRotation = rotation;
        slope.transform.localPosition = (bottom + top) * 0.5f - rotation * Vector3.up * 0.06f;
        slope.AddComponent<BoxCollider>().size = new Vector3(3f, 0.12f, along.magnitude + 0.3f);
    }

    static bool IsFirstLod(Renderer renderer)
    {
        var group = renderer.GetComponentInParent<LODGroup>();
        var lods = group.GetLODs();
        return lods.Length == 0 || lods[0].renderers.Contains(renderer);
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var p = t; p != null && p != root; p = p.parent) parts.Insert(0, p.name);
        return string.Join("/", parts);
    }

    // A still copy of a car (just its look) standing on each display bay.
    static int PlaceCars(Transform root, Transform showroom)
    {
        var bays = new List<Transform>();
        foreach (Transform child in showroom)
            if (child.name.StartsWith("Ground Display Bay")) bays.Add(child);
        foreach (Transform child in showroom)
            if (child.name.StartsWith("Upper Display Bay")) bays.Add(child);

        var holder = new GameObject("Display Cars").transform;
        holder.SetParent(root, false);
        int placed = 0;
        for (int i = 0; i < bays.Count && i < DisplayCars.Length; i++)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DrivableFolder + DisplayCars[i][0] + ".prefab");
            if (source == null) continue;
            var car = StillCopy(source, holder);
            var bay = bays[i];
            var top = Bounds(bay).max.y;
            // Angled on the turntable like a showroom display, nose towards the entrance side.
            float yaw = bay.name.StartsWith("Upper") ? 160f + i * 10f : 145f + i * 25f;
            car.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var carBounds = Bounds(car.transform);
            Vector3 offset = car.transform.position - carBounds.center;
            Vector3 centre = Bounds(bay).center;
            car.transform.position = new Vector3(centre.x + offset.x, top + (car.transform.position.y - carBounds.min.y) + 0.01f, centre.z + offset.z);

            var info = car.AddComponent<ShowroomCar>();
            info.displayName = DisplayCars[i][1];
            info.carClass = DisplayCars[i][2];
            Rate(info, source);
            info.bayCentre = holder.InverseTransformPoint(new Vector3(centre.x, top, centre.z));
            placed++;
        }
        return placed;
    }

    // ---------- Staff ----------

    const string PeopleFolder = "Assets/PopulationSystem/Prefabs/People Prefabs/";
    const string ManAnimator = "Assets/_Project/Animations/Pedestrians/PedMan_Ambient.controller";
    const string WomanAnimator = "Assets/_Project/Animations/Pedestrians/PedGirl_Ambient.controller";
    const float SeatHeight = 0.57f;

    // Employees: at the customer-service desks, on reception, walking the floor and by the cars upstairs.
    static void AddStaff(Transform root)
    {
        var holder = new GameObject("Staff").transform;
        holder.SetParent(root, false);
        Physics.SyncTransforms();

        // Customer services: seated behind each desk, facing the customers.
        Staff(holder, "Service Desk 01", "Girl_33", ShowroomStaff.Duty.Sit, 0f, new Vector3(10.13f, SeatHeight, -11.77f));
        Staff(holder, "Service Desk 02", "Man_44", ShowroomStaff.Duty.Sit, 0f, new Vector3(3.53f, SeatHeight, -11.77f));
        Staff(holder, "Service Desk 03", "Girl_55", ShowroomStaff.Duty.Sit, 0f, new Vector3(-3.17f, SeatHeight, -11.77f));
        // Reception: one at the desk, one standing beside them.
        Staff(holder, "Receptionist", "Girl_11", ShowroomStaff.Duty.Sit, 90f, new Vector3(-8.22f, SeatHeight, 0.29f));
        Staff(holder, "Reception Host", "Man_66", ShowroomStaff.Duty.Stand, 90f, Ground(root, new Vector3(-7.75f, 0f, -2.7f)));
        // Sales: one walks between the cars, stopping at each, one waits by a car near the door.
        Staff(holder, "Salesperson Floor", "Man_22", ShowroomStaff.Duty.Patrol, 0f,
            Ground(root, new Vector3(2.35f, 0f, 8.5f)), Ground(root, new Vector3(2.35f, 0f, 1.8f)), Ground(root, new Vector3(2.35f, 0f, -3.6f)),
            Ground(root, new Vector3(9.4f, 0f, 1.5f)), Ground(root, new Vector3(8.8f, 0f, 9.6f)));
        Staff(holder, "Salesperson Door", "Girl_77", ShowroomStaff.Duty.Stand, 125f, Ground(root, new Vector3(-4.3f, 0f, 7.2f)));
        // Upstairs, between the two cars on the mezzanine.
        Staff(holder, "Salesperson Upstairs", "Man_99", ShowroomStaff.Duty.Stand, 0f, Ground(root, new Vector3(-0.6f, 4.6f, -11.2f)));
    }

    public static Vector3 Ground(Transform root, Vector3 local)
    {
        Vector3 world = root.TransformPoint(local + Vector3.up * 1.5f);
        if (Physics.Raycast(world, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return root.InverseTransformPoint(hit.point);
        return local;
    }

    public static void Staff(Transform holder, string role, string person, ShowroomStaff.Duty duty, float yaw, params Vector3[] localPoints)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PeopleFolder + person + ".prefab");
        if (prefab == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.gameObject.scene);
        go.transform.SetParent(holder, false);
        go.name = role + " (" + person + ")";
        go.transform.localPosition = localPoints[0];
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(person.StartsWith("Girl") ? WomanAnimator : ManAnimator);
            animator.applyRootMotion = false;
        }

        var staff = go.AddComponent<ShowroomStaff>();
        staff.duty = duty;
        staff.facingYaw = yaw;
        staff.points = localPoints.Select(p => holder.TransformPoint(p)).ToArray();

        // A body the player bumps into (a seated employee is behind their desk and chair already).
        if (duty != ShowroomStaff.Duty.Sit)
        {
            var body = go.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.28f;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
    }

    // The card's numbers come from how the drivable car is actually set up (RCC): top speed, torque against weight for the
    // sprint, weight and drive for handling, brake torque against weight for braking. The price follows the performance.
    static void Rate(ShowroomCar info, GameObject source)
    {
        var rcc = source.GetComponent<RCC_CarControllerV3>();
        var body = source.GetComponent<Rigidbody>();
        float mass = body != null ? body.mass : 1800f;
        float speed = rcc != null ? rcc.maxspeed : 220f;
        float torque = rcc != null ? rcc.maxEngineTorque : 450f;
        float brake = rcc != null ? rcc.brakeTorque : 2800f;
        var wheels = rcc != null ? rcc.wheelTypeChoise : RCC_CarControllerV3.WheelType.RWD;

        info.drive = wheels.ToString();
        info.topSpeed = speed;
        info.zeroToHundred = Mathf.Round((0.6f + 1.1f * mass / Mathf.Max(100f, torque)) * 10f) / 10f;
        info.speedRating = Mathf.Clamp(Mathf.InverseLerp(170f, 320f, speed) * 100f, 25f, 100f);
        info.acceleration = Mathf.Clamp(Mathf.InverseLerp(6.5f, 2.8f, info.zeroToHundred) * 100f, 25f, 100f);
        float handling = 100f - (mass - 1500f) / 22f + (wheels == RCC_CarControllerV3.WheelType.AWD ? 6f : 0f);
        if (info.carClass.Contains("Super")) handling += 8f;
        info.handling = Mathf.Clamp(handling, 30f, 98f);
        info.braking = Mathf.Clamp(brake / mass * 55f, 30f, 98f);

        float score = info.speedRating * 0.4f + info.acceleration * 0.35f + info.handling * 0.15f + info.braking * 0.1f;
        float luxury = info.carClass.Contains("Luxury") ? 1.35f : info.carClass.Contains("Super") ? 1.5f : 1f;
        info.price = Mathf.RoundToInt((18000f + score * score * 22f) * luxury / 500f) * 500;
    }

    static GameObject StillCopy(GameObject source, Transform parent)
    {
        var copy = new GameObject(source.name.Replace("Drivable_", "") + " (Display)");
        copy.transform.SetParent(parent, false);
        Matrix4x4 toLocal = source.transform.worldToLocalMatrix;
        foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(false))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (!renderer.enabled || filter == null || filter.sharedMesh == null) continue;
            if (renderer.GetComponentInParent<LODGroup>() != null && !IsFirstLod(renderer)) continue;
            var part = new GameObject(renderer.name);
            part.transform.SetParent(copy.transform, false);
            Matrix4x4 m = toLocal * renderer.transform.localToWorldMatrix;
            part.transform.localPosition = m.GetColumn(3);
            part.transform.localRotation = m.rotation;
            part.transform.localScale = m.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = renderer.sharedMaterials;
            // Solid, so the player walks round the car rather than through it.
            part.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }
        copy.transform.localScale = source.transform.localScale;
        return copy;
    }

    static Bounds Bounds(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(t.position, Vector3.zero);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static void Box(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.AddComponent<BoxCollider>().size = size;
    }

    static Transform Marker(Transform parent, string name, Vector3 local, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    // ---------- The way in from the city ----------

    static void WireCity(Scene city, Vector3 door, Vector3 facing, Sprite mapIcon)
    {
        var gameplay = city.GetRootGameObjects().FirstOrDefault(g => g.name == "Gameplay");
        if (gameplay == null) gameplay = new GameObject("Gameplay");
        var travel = gameplay.GetComponent<ShowroomTravel>();
        if (travel == null) travel = gameplay.AddComponent<ShowroomTravel>();
        travel.showroomScene = Path.GetFileNameWithoutExtension(ScenePath);
        if (gameplay.GetComponent<InteractionPromptUI>() == null) gameplay.AddComponent<InteractionPromptUI>();
        if (gameplay.GetComponent<ScreenBlink>() == null) gameplay.AddComponent<ScreenBlink>();

        var old = city.GetRootGameObjects().FirstOrDefault(g => g.name == "Shops");
        var shops = old != null ? old.transform : new GameObject("Shops").transform;
        var oldEntrance = shops.Find(Name + " Entrance");
        if (oldEntrance != null) Object.DestroyImmediate(oldEntrance.gameObject);

        facing.y = 0f;
        facing.Normalize();
        var entrance = new GameObject(Name + " Entrance");
        entrance.transform.SetParent(shops, false);
        entrance.transform.SetPositionAndRotation(door + Vector3.up * 1.2f, Quaternion.LookRotation(facing));
        int layer = LayerMask.NameToLayer(TriggerLayer);
        if (layer >= 0) entrance.layer = layer;
        var trigger = entrance.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(6f, 2.4f, 3f);
        var interact = entrance.AddComponent<ShowroomEntrance>();
        interact.prompt = "Enter the " + Name;

        // Coming back out: on the pavement in front of the door, facing the street.
        var exitPoint = new GameObject("Exit Point").transform;
        exitPoint.SetParent(entrance.transform, false);
        Vector3 exitAt = door + facing * 1.6f;
        if (Physics.Raycast(exitAt + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) exitAt.y = hit.point.y;
        exitPoint.SetPositionAndRotation(exitAt, Quaternion.LookRotation(facing));
        interact.exitPoint = exitPoint;

        var icon = entrance.AddComponent<MapIcon>();
        icon.icon = mapIcon;
        icon.label = Name;
        icon.size = 30f;
        EditorSceneManager.MarkSceneDirty(city);
    }

    // ---------- Map icon: a white car on a dark badge with a blue rim ----------

    static Sprite MakeMapIcon()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var badge = new Color(0.07f, 0.1f, 0.18f, 1f);
        var rim = new Color(0.25f, 0.7f, 1f, 1f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color sum = Color.clear;
                for (int s = 0; s < 4; s++)
                {
                    float u = (x + (s % 2 == 0 ? 0.25f : 0.75f)) / size, v = (y + (s < 2 ? 0.25f : 0.75f)) / size;
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    Color c = Color.clear;
                    if (d < 0.49f) c = d > 0.43f ? rim : badge;
                    if (d < 0.43f && Car(u, v)) c = Color.white;
                    if (d < 0.43f && Wheel(u, v)) c = badge;
                    sum += c;
                }
                tex.SetPixel(x, y, sum / 4f);
            }
        }
        tex.Apply();
        File.WriteAllBytes(MapIconPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(MapIconPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(MapIconPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(MapIconPath);
    }

    // A side-on car: body, cabin with sloped windows, and two wheels.
    static bool Car(float u, float v)
    {
        bool body = u > 0.2f && u < 0.8f && v > 0.38f && v < 0.52f;
        float cabinHalf = 0.17f - (v - 0.52f) * 0.9f;
        bool cabin = v >= 0.52f && v < 0.64f && Mathf.Abs(u - 0.47f) < cabinHalf;
        bool wheels = Vector2.Distance(new Vector2(u, v), new Vector2(0.33f, 0.37f)) < 0.07f || Vector2.Distance(new Vector2(u, v), new Vector2(0.67f, 0.37f)) < 0.07f;
        return body || cabin || wheels;
    }

    static bool Wheel(float u, float v)
    {
        return Vector2.Distance(new Vector2(u, v), new Vector2(0.33f, 0.37f)) < 0.03f || Vector2.Distance(new Vector2(u, v), new Vector2(0.67f, 0.37f)) < 0.03f;
    }
}
