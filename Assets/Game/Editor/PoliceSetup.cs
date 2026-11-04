using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts police on the streets: officers join the footpath walkers and a patrol car joins the traffic.
// Safe to run again; it rebuilds its own assets and leaves the source models alone, apart from
// giving the officers a Humanoid rig.
public static class PoliceSetup
{
    const string Root = "Assets/Game/Police";
    const string ControllerPath = Root + "/PoliceOfficer.controller";
    const string PeopleFolder = "Assets/PopulationSystem/Prefabs/People Prefabs";
    const string ModelsFolder = "Assets/PopulationSystem/Prefabs/";
    const string ClipRoot = "Assets/iTS/Demo/Locomotion Setup/Locomotion/Animations/DefaultAvatar@";
    const string CarModelPath = "Assets/New Folder/PoliceCar_Preview.fbm/POlice car.fbx";
    const string CarPrefabPath = "Assets/Game/Prefabs/PoliceCar.prefab";
    const string DonorCarPath = "Assets/iTS/Vehicles/Car17.prefab";
    const string WheelPartName = "Wheel_police__2_";

    static readonly string[] Officers = { "Police 2", "Police 3" };

    // PedestrianWalker plays its clips as if they move at 1.3 and 4 m/s; these clips cover more ground.
    const float WalkStateSpeed = 1.3f / 1.556f, RunStateSpeed = 4f / 5.668f;

    // At this size the model matches the donor car's physics, which is about 5.9 m long.
    const float CarScale = 0.73f;
    // Measured on the model: where its wheels sit.
    const float WheelSideX = 1.30f, FrontWheelZ = 2.41f, RearWheelZ = -2.23f, WheelCentreY = 0.03f;
    const float DonorWheelY = 0.41f;
    // Measured on the model: the roof bar, once scaled and placed on the car.
    const float BarPanelX = 0.36f, BarPanelY = 2.05f;
    static readonly Vector3 BarPanelSize = new Vector3(0.68f, 0.22f, 0.66f);
    // How many of the 30 chances in the traffic mix go to the police car: about one car in ten.
    const int PatrolCarFrequency = 3;

    [MenuItem("Game/Set Up Police")]
    public static void Run()
    {
        Debug.Log(BuildOfficers());
        Debug.Log(BuildPatrolCar());
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    static string BuildOfficers()
    {
        Directory.CreateDirectory(Root);
        var controller = BuildController();
        var prefabs = new List<GameObject>();
        foreach (var name in Officers)
        {
            string path = ModelsFolder + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.SaveAndReimport();
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var animator = instance.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            prefabs.Add(PrefabUtility.SaveAsPrefabAsset(instance, PeopleFolder + "/" + name.Replace(' ', '_') + ".prefab"));
            Object.DestroyImmediate(instance);
        }

        var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
        if (spawner == null) return "No pedestrian spawner in the scene, so the officers were not added";
        var names = prefabs.Select(p => p.name).ToList();
        spawner.peoplePrefabs = spawner.peoplePrefabs.Where(p => p != null && !names.Contains(p.name)).Concat(prefabs).ToArray();
        EditorUtility.SetDirty(spawner);
        return "Police officers: " + prefabs.Count + " added to " + spawner.peoplePrefabs.Length + " people types";
    }

    // State names are the ones PedestrianWalker plays.
    static AnimatorController BuildController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        foreach (var old in machine.states.ToList()) machine.RemoveState(old.state);

        var walk = machine.AddState("walk");
        walk.motion = LoadClip(ClipRoot + "WalkForward_NtrlFaceFwd.fbx");
        walk.speed = WalkStateSpeed;
        var idle = machine.AddState("idle1");
        idle.motion = LoadClip(ClipRoot + "Idle_Neutral.fbx");
        var run = machine.AddState("run");
        run.motion = LoadClip(ClipRoot + "RunForward_NtrlFaceFwd.fbx");
        run.speed = RunStateSpeed;
        machine.defaultState = walk;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimationClip LoadClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
    }

    static string BuildPatrolCar()
    {
        var car = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DonorCarPath));
        car.name = "PoliceCar";
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CarModelPath));
        var materials = new Dictionary<Material, Material>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m => ConvertedMaterial(m, materials)).ToArray();
        }

        var donorBody = car.transform.Find("Center");
        var donorCollider = donorBody.GetComponentInChildren<BoxCollider>(true);
        int bodyLayer = donorCollider.gameObject.layer;
        Object.DestroyImmediate(donorBody.gameObject);

        // The file holds only one wheel, so the four are made from it.
        var wheelPart = model.transform.Find(WheelPartName);
        var wheelMesh = wheelPart.GetComponent<MeshFilter>().sharedMesh;
        var wheelMaterials = wheelPart.GetComponent<MeshRenderer>().sharedMaterials;
        wheelPart.SetParent(null);
        Object.DestroyImmediate(wheelPart.gameObject);

        model.name = "Body";
        model.transform.SetParent(car.transform, false);
        model.transform.localPosition = new Vector3(0f, DonorWheelY - WheelCentreY * CarScale, 0f);
        model.transform.localScale = Vector3.one * CarScale;
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = bodyLayer;

        var collider = new GameObject("Body Collider").AddComponent<BoxCollider>();
        collider.gameObject.layer = bodyLayer;
        collider.transform.SetParent(car.transform, false);
        collider.center = new Vector3(0f, 1f, 0f);
        collider.size = new Vector3(2.2f, 1.55f, 5.8f);

        PlaceWheel(car.transform, "WheelFL1", -1f, FrontWheelZ, wheelMesh, wheelMaterials, bodyLayer);
        PlaceWheel(car.transform, "WheelFR4", 1f, FrontWheelZ, wheelMesh, wheelMaterials, bodyLayer);
        PlaceWheel(car.transform, "WheelRL3", -1f, RearWheelZ, wheelMesh, wheelMaterials, bodyLayer);
        PlaceWheel(car.transform, "WheelRR2", 1f, RearWheelZ, wheelMesh, wheelMaterials, bodyLayer);

        // The donor's lights sit at the back of a slightly different car.
        foreach (var name in new[] { "BrakeLight1", "BrakeLight2", "HaloLight-Left", "HaloLight-Right" })
        {
            var light = car.transform.Find(name);
            light.localPosition = new Vector3(light.localPosition.x, 1.06f, -2.96f);
        }

        AddLightBar(car, bodyLayer);

        var prefab = PrefabUtility.SaveAsPrefabAsset(car, CarPrefabPath);
        Object.DestroyImmediate(car);

        var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
        if (spawner == null) return "No traffic spawner in the scene, so the patrol car was not added";
        var entries = spawner.cars.Where(c => c.cars != null && c.cars.name != prefab.name).ToList();
        entries.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = prefab, frequency = PatrolCarFrequency });
        spawner.cars = entries.ToArray();
        EditorUtility.SetDirty(spawner);
        return "Patrol car: " + materials.Count + " materials converted, " + spawner.cars.Length + " vehicle types in traffic";
    }

    // The model's bar is one mesh, so a bright panel over each half does the flashing.
    static void AddLightBar(GameObject car, int layer)
    {
        var bar = car.AddComponent<PoliceLightBar>();
        var red = new Color(1f, 0.04f, 0.04f, 1f);
        var blue = new Color(0.05f, 0.2f, 1f, 1f);
        bar.redPanel = MakeFlashPanel(car.transform, "Flash Red", -BarPanelX, red, layer);
        bar.bluePanel = MakeFlashPanel(car.transform, "Flash Blue", BarPanelX, blue, layer);
        bar.redLight = MakeFlashLight(bar.redPanel.transform, red);
        bar.blueLight = MakeFlashLight(bar.bluePanel.transform, blue);
    }

    static Renderer MakeFlashPanel(Transform car, string name, float x, Color color, int layer)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.layer = layer;
        panel.transform.SetParent(car, false);
        panel.transform.localPosition = new Vector3(x, BarPanelY, 0f);
        panel.transform.localScale = BarPanelSize;
        var renderer = panel.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = FlashMaterial(name, color);
        return renderer;
    }

    static Light MakeFlashLight(Transform panel, Color color)
    {
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(panel.parent, false);
        light.transform.position = panel.position;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 6f;
        light.range = 14f;
        light.shadows = LightShadows.None;
        return light;
    }

    static Material FlashMaterial(string name, Color color)
    {
        string folder = Root + "/Car Materials";
        Directory.CreateDirectory(folder);
        string path = folder + "/" + name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", color * 3f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void PlaceWheel(Transform car, string name, float side, float modelZ, Mesh mesh, Material[] materials, int layer)
    {
        var wheel = car.Find(name);
        wheel.localPosition = new Vector3(side * WheelSideX * CarScale, DonorWheelY, modelZ * CarScale);
        var tire = wheel.Find("Tire");
        foreach (Transform old in tire.Cast<Transform>().ToList()) Object.DestroyImmediate(old.gameObject);

        var visual = new GameObject("Wheel");
        visual.layer = layer;
        visual.transform.SetParent(tire, false);
        // The mesh is the car's rear left wheel, so its vertices are off centre. Mirroring it makes the right side.
        float mirror = side < 0f ? 1f : -1f;
        visual.transform.localScale = new Vector3(mirror * CarScale, CarScale, CarScale);
        Vector3 centre = mesh.bounds.center;
        visual.transform.localPosition = -new Vector3(centre.x * mirror * CarScale, centre.y * CarScale, centre.z * CarScale);
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        visual.AddComponent<MeshRenderer>().sharedMaterials = materials;
    }

    // The model's materials use built-in shaders, which draw bright pink in URP. Copies keep the originals as they are.
    static Material ConvertedMaterial(Material source, Dictionary<Material, Material> done)
    {
        if (done.TryGetValue(source, out Material converted)) return converted;
        string folder = Root + "/Car Materials";
        Directory.CreateDirectory(folder);
        string path = folder + "/" + source.name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        converted = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (converted == null)
        {
            converted = new Material(shader);
            AssetDatabase.CreateAsset(converted, path);
        }
        converted.shader = shader;
        converted.shaderKeywords = new string[0];
        converted.SetColor("_BaseColor", source.GetColor("_Color"));
        converted.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
        converted.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);
        converted.SetFloat("_Smoothness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.5f);

        Color emission = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
        if (emission.maxColorComponent > 0f)
        {
            converted.SetColor("_EmissionColor", emission);
            converted.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
            converted.EnableKeyword("_EMISSION");
            converted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        float mode = source.HasProperty("_Mode") ? source.GetFloat("_Mode") : 0f;
        if (mode == 1f) MakeCutout(converted);
        else if (mode >= 2f) MakeTransparent(converted);
        else converted.renderQueue = -1;

        EditorUtility.SetDirty(converted);
        done[source] = converted;
        return converted;
    }

    static void MakeCutout(Material material)
    {
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}
