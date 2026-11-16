using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Game/Set Up Clothing Store: builds Jason's clothing catalog with rendered thumbnails, the store's own scene from the
// City Mirage cloth shop, and the way in from Miami Clothing Store in the open city scene. Safe to run again.
public static partial class ClothingStoreSetup
{
    const string ShopFolder = "Assets/Game/Shop";
    const string IconFolder = ShopFolder + "/Icons";
    const string CatalogPath = ShopFolder + "/Jason Clothing Catalog.asset";
    const string MapIconPath = ShopFolder + "/Clothing Store Map Icon.png";
    const string RingPath = ShopFolder + "/Fitting Spot Ring.png";
    const string ScenePath = "Assets/Scenes/Clothing Store Interior.unity";
    const string ShopPrefabPath = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string PlayerPrefabPath = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";
    const string StoreName = "Miami Clothing Store";
    const string TriggerLayer = "Triggers";

    // Far from the city, so the store's lights and the city never meet.
    static readonly Vector3 InteriorOrigin = new Vector3(2600f, 30f, 2600f);
    static readonly string[] BodyParts = { "Face", "hand", "Neck", "Eyes" };

    [MenuItem("Game/Set Up Clothing Store")]
    public static void Run()
    {
        Debug.Log(SetUp());
    }

    public static string SetUp()
    {
        var city = SceneManager.GetActiveScene();
        var store = city.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == StoreName);
        if (store == null) return "Open the city scene with '" + StoreName + "' first.";

        Directory.CreateDirectory(IconFolder);
        var catalog = BuildCatalog();
        int icons = RenderIcons(catalog);
        var mapIcon = MakeMapIcon();
        BuildStoreScene(catalog);
        WireCity(city, store, catalog, mapIcon);
        EditorSceneManager.SaveScene(city);
        return "Clothing store ready: " + catalog.categories.Sum(c => c.items.Length) + " items in " + catalog.categories.Length
            + " categories, " + icons + " thumbnails, store scene " + ScenePath;
    }

    // ---------- Catalog ----------

    static ClothingCatalog BuildCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ClothingCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ClothingCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.storeName = StoreName;
        catalog.armsPart = "Object001";
        catalog.legsPart = "legs";
        catalog.categories = new[]
        {
            Category("tops", "Tops", ClothingCatalog.Focus.Torso, "top_white_polo",
                Top("top_white_polo", "White Striped Polo", 0, "Crisp white piqué polo with navy and gold trim.", true, "Tops004"),
                Top("top_racing_polo", "Black Racing Polo", 650, "Slim black polo with red racing stripes.", true, "Tops003"),
                Top("top_camo_tee", "Camo Skull Tee", 450, "Washed camo tee with a small skull crest.", true, "Tops002"),
                Top("top_lion_tank", "Lion Crown Tank", 380, "Black and gold tank with the crowned lion.", true, "Tops"),
                Top("top_skull_tank", "Red Skull Tank", 380, "Loud red tank with a flaming skull print.", true, "Tops005"),
                Top("top_track_jacket", "Red Track Jacket", 1200, "Zip-up track jacket in red and white.", false, "Tops001"),
                Top("top_palm_overshirt", "Yellow Palm Overshirt", 1100, "Open floral overshirt over a white tee. Pure Miami.", false, "Jecket001"),
                Top("top_denim_jacket", "Blue Denim Jacket", 1500, "Light wash denim jacket over a white tee.", false, "Jecket002"),
                Top("top_leather_jacket", "Black Leather Jacket", 2400, "Worn-in leather jacket over a white tee.", false, "Jecket")),
            Category("bottoms", "Bottoms", ClothingCatalog.Focus.Legs, "bottom_ripped_jeans",
                Bottom("bottom_ripped_jeans", "Ripped Blue Jeans", 0, "Faded straight-leg jeans with torn knees.", false, "Bottoms"),
                Bottom("bottom_cargo_pants", "Desert Cargo Pants", 900, "Loose sand cargo pants with deep pockets.", false, "Bottom01"),
                Bottom("bottom_grey_shorts", "Grey Distressed Shorts", 420, "Faded grey shorts with a striped belt.", true, "Bottoms001"),
                Bottom("bottom_ash_shorts", "Ash Biker Shorts", 420, "Washed ash shorts with a white belt.", true, "Bottoms004"),
                Bottom("bottom_claw_shorts", "Brown Claw Shorts", 450, "Brown cargo shorts with gold claw marks.", true, "Bottoms002"),
                Bottom("bottom_red_shorts", "Red Cargo Shorts", 480, "Bright red cargo shorts with a dragon print.", true, "Bottoms003")),
            Category("shoes", "Shoes", ClothingCatalog.Focus.Feet, "shoes_white",
                Item("shoes_white", "White Low Sneakers", 0, "Clean white everyday sneakers.", "Ch42_Sneakers"),
                Item("shoes_red_runners", "Red Street Runners", 750, "Red and white runners with chunky soles.", "Shoes"),
                Item("shoes_crimson", "Crimson Sneakers", 680, "All-red sneakers with black laces.", "Shoes003"),
                Item("shoes_black", "Black Laced Runners", 720, "Black runners on bright white soles.", "Shoes004"),
                Item("shoes_yellow", "Yellow Court Shoes", 520, "Low yellow court shoes.", "Shoes002"),
                Item("shoes_boots", "Brown Work Boots", 950, "Tough leather work boots.", "Shoes001")),
            Category("glasses", "Glasses", ClothingCatalog.Focus.Head, "glasses_none",
                Item("glasses_none", "No Glasses", 0, "Let them see your eyes."),
                Item("glasses_black", "Black Frame Glasses", 300, "Classic black frames.", "Eyewear", "glass"),
                Item("glasses_round", "Round Gold Shades", 450, "Thin round gold frames with tinted lenses.", "Eyewear001", "GLASS001"),
                Item("glasses_aviator", "Gold Aviators", 520, "Gold aviators with smoked lenses.", "Eyewear002", "glass1"),
                Item("glasses_sport", "Sport Wraparounds", 600, "Silver sport shades that wrap around.", "Eyewear003", "glass2")),
            Category("hair", "Hair", ClothingCatalog.Focus.Head, "hair_shaved",
                Item("hair_shaved", "Clean Shave", 0, "Short and sharp."),
                Item("hair_messy", "Messy Brown Cut", 250, "Long, loose and a little wild."),
                Item("hair_quiff", "Silver Quiff", 300, "Swept-up silver quiff with short sides.", "Hair01"),
                Item("hair_twists", "Dark Twists", 400, "Neat dark twists.", "Mesh_0014.rip"),
                Item("hair_rainbow", "Rainbow Twists", 450, "Twists with colourful tips.", "Mesh_0014.rip001")),
            Category("chains", "Chains", ClothingCatalog.Focus.Neck, "chain_none",
                Item("chain_none", "No Chain", 0, "Nothing round the neck."),
                Item("chain_spike", "Gold Spike Pendant", 1800, "Gold rope chain with a spike pendant.", "Chain"),
                Item("chain_tag", "Gold Tag Pendant", 1600, "Gold rope chain with a dog tag.", "Chain001"),
                Item("chain_arrow", "Gold Arrow Pendant", 1700, "Gold rope chain with an arrowhead.", "Chain002"),
                Item("chain_cuban", "Silver Cuban Chain", 2500, "Heavy silver Cuban link.", "Mesh_0018.rip")),
            Category("extras", "Extras", ClothingCatalog.Focus.Neck, "extra_none",
                Item("extra_none", "No Extras", 0, "Keep it simple."),
                Item("extra_earring", "Diamond Stud", 1200, "A single diamond stud.", "Mesh_0127.rip"),
                Item("extra_ring", "Silver Ring", 900, "A plain silver ring.", "Hand ring"),
                Item("extra_both", "Stud and Ring", 1900, "The diamond stud and the silver ring.", "Mesh_0127.rip", "Hand ring")),
        };
        // The messy cut is the model's own Hair part.
        catalog.categories[4].items[1].parts = new[] { "Hair" };
        foreach (var category in catalog.categories)
            foreach (var item in category.items)
                item.starter = item.price == 0;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    static ClothingCatalog.Category Category(string id, string name, ClothingCatalog.Focus focus, string defaultItem, params ClothingCatalog.Item[] items)
    {
        return new ClothingCatalog.Category { id = id, displayName = name, focus = focus, defaultItem = defaultItem, items = items };
    }

    static ClothingCatalog.Item Item(string id, string name, int price, string description, params string[] parts)
    {
        return new ClothingCatalog.Item { id = id, displayName = name, price = price, description = description, parts = parts };
    }

    static ClothingCatalog.Item Top(string id, string name, int price, string description, bool arms, params string[] parts)
    {
        var item = Item(id, name, price, description, parts);
        item.showsArms = arms;
        return item;
    }

    static ClothingCatalog.Item Bottom(string id, string name, int price, string description, bool legs, params string[] parts)
    {
        var item = Item(id, name, price, description, parts);
        item.showsLegs = legs;
        return item;
    }

    // ---------- Thumbnails ----------

    static int RenderIcons(ClothingCatalog catalog)
    {
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var modelSource = playerPrefab != null ? playerPrefab.transform.Find("3D Model/Jason") : null;
        return RenderIcons(catalog, modelSource, BodyParts);
    }

    static int RenderIcons(ClothingCatalog catalog, Transform modelSource, string[] bodyParts)
    {
        if (modelSource == null) return 0;

        var model = Object.Instantiate(modelSource.gameObject);
        model.hideFlags = HideFlags.HideAndDontSave;
        model.transform.SetPositionAndRotation(new Vector3(5000f, -500f, 5000f), Quaternion.Euler(0f, 180f, 0f));
        foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
        var parts = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());

        var cameraObject = new GameObject("Icon Camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.fieldOfView = 22f;
        camera.nearClipPlane = 0.02f;
        camera.farClipPlane = 20f;
        var key = MakeLight("Icon Key", Quaternion.Euler(25f, 150f, 0f), 1.8f);
        var fill = MakeLight("Icon Fill", Quaternion.Euler(10f, 230f, 0f), 0.7f);

        const int size = 256;
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = target;
        var pixels = new Texture2D(size, size, TextureFormat.RGBA32, false);
        int made = 0;
        try
        {
            foreach (var category in catalog.categories)
            {
                foreach (var item in category.items)
                {
                    var shown = new HashSet<string>(bodyParts);
                    foreach (var p in item.parts) shown.Add(p);
                    if (item.showsArms) shown.Add(catalog.armsPart);
                    if (item.showsLegs) shown.Add(catalog.legsPart);
                    foreach (var pair in parts) pair.Value.gameObject.SetActive(shown.Contains(pair.Key));

                    Bounds frame = Frame(category, item, parts, model.transform);
                    float radius = Mathf.Max(frame.extents.x, frame.extents.y, 0.08f) * 1.15f;
                    float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    camera.transform.position = frame.center + new Vector3(0.18f, 0.08f, -1f).normalized * distance;
                    camera.transform.LookAt(frame.center);
                    camera.Render();

                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = null;
                    string path = IconFolder + "/" + item.id + ".png";
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    AssetDatabase.ImportAsset(path);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                    item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    made++;
                }
            }
        }
        finally
        {
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(key);
            Object.DestroyImmediate(fill);
            Object.DestroyImmediate(model);
        }
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return made;
    }

    // What to frame: the item itself, or for "none" items the part of the body it is about.
    static Bounds Frame(ClothingCatalog.Category category, ClothingCatalog.Item item, Dictionary<string, SkinnedMeshRenderer> parts, Transform model)
    {
        var names = item.parts.Length > 0 ? item.parts : new[] { category.focus == ClothingCatalog.Focus.Neck ? "Neck" : "Face" };
        // An earring sold with something else is still shown by the ear: the other piece is far away on the hand.
        if (names.Contains("Mesh_0127.rip")) names = new[] { "Mesh_0127.rip" };
        Bounds bounds = default;
        bool first = true;
        foreach (var name in names)
        {
            if (!parts.TryGetValue(name, out var part)) continue;
            if (first) { bounds = part.bounds; first = false; }
            else bounds.Encapsulate(part.bounds);
        }
        if (first) bounds = new Bounds(model.position + Vector3.up * 1.5f, Vector3.one * 0.3f);
        // Tiny pieces (a ring, an earring) are shown close up with a little of the hand or ear round them.
        if (bounds.size.magnitude < 0.12f)
        {
            bounds.Expand(0.14f);
            return bounds;
        }
        // Jackets stand in a T-pose: frame the body, not the outstretched sleeves.
        if (category.focus == ClothingCatalog.Focus.Torso && bounds.extents.x > 0.4f)
            bounds = new Bounds(new Vector3(model.position.x, bounds.center.y, bounds.center.z), new Vector3(0.8f, bounds.size.y, bounds.size.z));
        // Heads and necks: include enough of the face.
        if (category.focus == ClothingCatalog.Focus.Head || category.focus == ClothingCatalog.Focus.Neck)
            bounds.Expand(new Vector3(0.06f, 0.08f, 0f));
        return bounds;
    }

    static GameObject MakeLight(string name, Quaternion rotation, float intensity)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        go.transform.rotation = rotation;
        return go;
    }

    // ---------- Map icon and fitting ring ----------

    static Sprite MakeMapIcon()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var badge = new Color(0.12f, 0.08f, 0.18f, 1f);
        var rim = new Color(0.93f, 0.33f, 0.98f, 1f);
        var shirt = Color.white;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Four samples per pixel for smooth edges.
                Color sum = Color.clear;
                for (int s = 0; s < 4; s++)
                {
                    float u = (x + (s % 2 == 0 ? 0.25f : 0.75f)) / size, v = (y + (s < 2 ? 0.25f : 0.75f)) / size;
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    Color c = Color.clear;
                    if (d < 0.49f) c = d > 0.43f ? rim : badge;
                    if (d < 0.43f && TShirt(u, v)) c = shirt;
                    sum += c;
                }
                tex.SetPixel(x, y, sum / 4f);
            }
        }
        tex.Apply();
        return SaveSprite(tex, MapIconPath);
    }

    // A T-shirt shape in the unit square.
    static bool TShirt(float u, float v)
    {
        float x = u - 0.5f;
        bool body = Mathf.Abs(x) < 0.17f && v > 0.24f && v < 0.68f;
        bool sleeves = v > 0.5f && v < 0.68f && Mathf.Abs(x) < 0.29f && (v - 0.5f) > (Mathf.Abs(x) - 0.17f) * 0.9f;
        bool neck = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.72f)) < 0.075f;
        return (body || sleeves) && !neck;
    }

    static Sprite MakeRing()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f) / size, new Vector2(0.5f, 0.5f));
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.44f) / 0.035f);
                float glow = Mathf.Clamp01(1f - d / 0.46f) * 0.22f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(ring, glow)));
            }
        }
        tex.Apply();
        return SaveSprite(tex, RingPath);
    }

    static Sprite SaveSprite(Texture2D tex, string path)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---------- The store's own scene ----------

    static void BuildStoreScene(ClothingCatalog catalog)
    {
        var city = SceneManager.GetActiveScene();
        var existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        var root = new GameObject("Clothing Store Interior");
        root.transform.position = InteriorOrigin;
        var shopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefabPath);
        var shop = (GameObject)PrefabUtility.InstantiatePrefab(shopPrefab, scene);
        shop.transform.SetParent(root.transform, false);

        // The shop was made as a scene of its own: its camera, sun and global look must not take over the city.
        foreach (var camera in shop.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
        foreach (var light in shop.GetComponentsInChildren<Light>(true)) if (light.type == LightType.Directional) light.gameObject.SetActive(false);
        foreach (var volume in shop.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true))
        {
            volume.isGlobal = false;
            volume.blendDistance = 1f;
            var box = volume.gameObject.GetComponent<BoxCollider>();
            if (box == null) box = volume.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = volume.transform.InverseTransformPoint(root.transform.position + Vector3.up * 2f);
            box.size = new Vector3(21f, 5f, 15f);
            volume.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        AddColliders(root.transform, shop.transform);

        var interior = root.AddComponent<ClothingStoreInterior>();
        interior.arrival = Marker(root.transform, "Arrival", new Vector3(0f, 0f, 4.6f), 180f);
        interior.fittingSpot = Marker(root.transform, "Fitting Spot", new Vector3(0f, 0f, -5.1f), 0f);
        interior.fittingCamera = Marker(root.transform, "Fitting Camera Rest", new Vector3(0f, 1.35f, -2.4f), 180f);

        var menu = root.AddComponent<ClothingStoreMenu>();
        menu.catalog = catalog;

        var fitting = Trigger(root.transform, "Fitting Area", new Vector3(0f, 1f, -5.1f), new Vector3(2.4f, 2f, 2.4f));
        fitting.AddComponent<ClothingStoreFittingSpot>();
        var ring = new GameObject("Fitting Spot Ring");
        ring.transform.SetParent(fitting.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.98f, 0f);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = Vector3.one * 1.5f;
        var sprite = ring.AddComponent<SpriteRenderer>();
        sprite.sprite = MakeRing();
        sprite.color = new Color(1f, 0.78f, 0.24f, 0.9f);

        var exit = Trigger(root.transform, "Store Exit", new Vector3(0f, 1f, 5.7f), new Vector3(4.2f, 2f, 1.6f));
        exit.AddComponent<ClothingStoreExit>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        SceneManager.SetActiveScene(city);
        EditorSceneManager.CloseScene(scene, true);

        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    // The shop came without collision: its own meshes for everything you could bump into, plus walls and a floor to be safe.
    static void AddColliders(Transform root, Transform shop)
    {
        var holder = new GameObject("Store Collision").transform;
        holder.SetParent(root, false);
        foreach (var renderer in shop.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !renderer.gameObject.activeInHierarchy) continue;
            var b = renderer.bounds;
            // Ceiling details and small things on shelves and tables stay out of the way.
            if (b.min.y - root.position.y > 2.3f || Mathf.Max(b.size.x, b.size.y, b.size.z) < 0.35f) continue;
            var go = new GameObject(renderer.name);
            go.transform.SetParent(holder, false);
            go.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            go.transform.localScale = renderer.transform.lossyScale;
            go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }
        Box(holder, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(20.5f, 0.2f, 14.5f));
        Box(holder, "Back Wall", new Vector3(0f, 2f, -7.15f), new Vector3(20.5f, 4f, 0.3f));
        Box(holder, "Front Wall", new Vector3(0f, 2f, 7.15f), new Vector3(20.5f, 4f, 0.3f));
        Box(holder, "Left Wall", new Vector3(-10.15f, 2f, 0f), new Vector3(0.3f, 4f, 14.5f));
        Box(holder, "Right Wall", new Vector3(10.15f, 2f, 0f), new Vector3(0.3f, 4f, 14.5f));
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

    static GameObject Trigger(Transform parent, string name, Vector3 local, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        int layer = LayerMask.NameToLayer(TriggerLayer);
        if (layer >= 0) go.layer = layer;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return go;
    }

    // ---------- The city side ----------

    static void WireCity(Scene city, Transform store, ClothingCatalog catalog, Sprite mapIcon)
    {
        var gameplay = city.GetRootGameObjects().FirstOrDefault(g => g.name == "Gameplay");
        if (gameplay == null) gameplay = new GameObject("Gameplay");
        var travel = gameplay.GetComponent<ClothingStoreTravel>();
        if (travel == null) travel = gameplay.AddComponent<ClothingStoreTravel>();
        travel.catalog = catalog;
        travel.storeScene = Path.GetFileNameWithoutExtension(ScenePath);
        if (gameplay.GetComponent<InteractionPromptUI>() == null) gameplay.AddComponent<InteractionPromptUI>();
        if (gameplay.GetComponent<ScreenBlink>() == null) gameplay.AddComponent<ScreenBlink>();

        // The shop front faces along the store model's forward axis, onto the pavement.
        var renderers = store.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        Vector3 front = store.forward;
        float depth = Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(front.x), Mathf.Abs(front.y), Mathf.Abs(front.z))));
        Vector3 door = new Vector3(bounds.center.x, store.position.y, bounds.center.z) + front * (depth + 1.2f);

        var old = city.GetRootGameObjects().FirstOrDefault(g => g.name == "Shops");
        var shops = old != null ? old.transform : new GameObject("Shops").transform;
        var oldEntrance = shops.Find(StoreName + " Entrance");
        if (oldEntrance != null) Object.DestroyImmediate(oldEntrance.gameObject);

        var entrance = new GameObject(StoreName + " Entrance");
        entrance.transform.SetParent(shops, false);
        entrance.transform.SetPositionAndRotation(door + Vector3.up * 1.2f, Quaternion.LookRotation(front));
        int layer = LayerMask.NameToLayer(TriggerLayer);
        if (layer >= 0) entrance.layer = layer;
        var trigger = entrance.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(7f, 2.4f, 2.6f);
        var interact = entrance.AddComponent<ClothingStoreEntrance>();
        interact.prompt = "Enter " + StoreName;

        // Coming back out: on the pavement beside the door, facing the street, clear of the lamp post in the middle.
        var exitPoint = new GameObject("Exit Point").transform;
        exitPoint.SetParent(entrance.transform, false);
        Vector3 exitAt = door + front * 0.8f + Vector3.Cross(Vector3.up, front) * 2.2f;
        if (Physics.Raycast(exitAt + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) exitAt.y = hit.point.y;
        exitPoint.SetPositionAndRotation(exitAt, Quaternion.LookRotation(front));
        interact.exitPoint = exitPoint;

        var icon = entrance.AddComponent<MapIcon>();
        icon.icon = mapIcon;
        icon.label = "Clothing Store";
        icon.size = 30f;
        EditorSceneManager.MarkSceneDirty(city);
    }
}
