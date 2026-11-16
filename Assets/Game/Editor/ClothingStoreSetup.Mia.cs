using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Mia's half of Game/Set Up Clothing Store. Stays on this machine with Mia herself (see .git/info/exclude).
public static partial class ClothingStoreSetup
{
    const string MiaCatalogPath = ShopFolder + "/Mia Clothing Catalog.asset";
    const string MiaPlayerPath = "Assets/_Project/Prefabs/Characters/Player_Girl.prefab";
    static readonly string[] MiaBodyParts = { "arm body", "Body", "Eyes", "Head" };

    [MenuItem("Game/Build Mia Clothing Catalog")]
    public static void RunMia()
    {
        Debug.Log(BuildMiaCatalog());
    }

    // Mia's own outfit pieces as a catalog, with thumbnails, and the store selling to her too.
    public static string BuildMiaCatalog()
    {
        Directory.CreateDirectory(IconFolder);
        var catalog = AssetDatabase.LoadAssetAtPath<ClothingCatalog>(MiaCatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ClothingCatalog>();
            AssetDatabase.CreateAsset(catalog, MiaCatalogPath);
        }
        catalog.characterId = "girl";
        catalog.storeName = StoreName;
        catalog.armsPart = "";
        catalog.legsPart = "Legs";
        catalog.categories = new[]
        {
            Category("tops", "Tops", ClothingCatalog.Focus.Torso, "mia_top_red_bomber",
                Item("mia_top_red_bomber", "Red Bomber Jacket", 0, "Red satin bomber with cherry-blossom print.", "Shirt"),
                Item("mia_top_camo_bomber", "Camo Bomber Jacket", 1300, "Green camo bomber with ribbed cuffs.", "Shirt1"),
                Item("mia_top_pink_bomber", "Pink Bomber Jacket", 1300, "Candy-pink bomber, oversized fit.", "Shirt2"),
                Item("mia_top_cream_corset", "Cream Corset Top", 900, "Laced cream corset top.", "Tops"),
                Item("mia_top_red_corset", "Red Corset Top", 950, "Bold red corset with boning detail.", "Tops001"),
                Item("mia_top_purple_crop", "Purple Crop Top", 600, "Fitted purple crop top.", "Tops004")),
            Category("bottoms", "Bottoms", ClothingCatalog.Focus.Legs, "mia_bottom_black_shorts",
                Bottom("mia_bottom_black_shorts", "Black Shorts", 0, "High-waisted black shorts.", true, "Bottoms007"),
                Bottom("mia_bottom_camo_shorts", "Camo Shorts", 450, "Cargo camo shorts.", true, "Bottoms008"),
                Bottom("mia_bottom_pink_shorts", "Pink Shorts", 450, "Pink denim cut-offs.", true, "Bottoms010"),
                Bottom("mia_bottom_black_skirt", "Black Skirt", 520, "Pleated black mini skirt.", true, "Bottoms"),
                Bottom("mia_bottom_denim_skirt", "Denim Skirt", 560, "Washed denim mini skirt.", true, "Bottoms009"),
                Bottom("mia_bottom_purple_jeans", "Purple Jeans", 850, "Skinny purple jeans.", false, "Bottoms011")),
            Category("shoes", "Shoes", ClothingCatalog.Focus.Feet, "mia_shoes_red_black",
                Item("mia_shoes_red_black", "Red & Black Sneakers", 0, "Chunky red and black sneakers.", "Shoes006"),
                Item("mia_shoes_camo", "Camo Sneakers", 500, "Camo-panel sneakers.", "Shoes002"),
                Item("mia_shoes_pink", "Pink Sneakers", 520, "Pastel pink sneakers.", "Shoes0061"),
                Item("mia_shoes_blue", "Blue Sneakers", 520, "Electric blue sneakers.", "Shoes008"),
                Item("mia_shoes_gold", "Gold High-Tops", 900, "Metallic gold high-tops.", "Shoes001"),
                Item("mia_shoes_brown", "Brown Boots", 750, "Brown leather ankle boots.", "Shoes 003")),
            Category("hair", "Hair", ClothingCatalog.Focus.Head, "mia_hair_black_bob",
                Item("mia_hair_black_bob", "Black Bob", 0, "Sleek black bob with a fringe.", "Hair002"),
                Item("mia_hair_long_blonde", "Long Blonde", 300, "Long, loose blonde hair.", "Hair"),
                Item("mia_hair_short_auburn", "Short Auburn", 280, "Short auburn cut.", "Hair001"),
                Item("mia_hair_braids", "Braids", 400, "Long braids.", "Hair004"),
                Item("mia_hair_brown_waves", "Brown Waves", 300, "Soft brown waves.", "Hair005")),
        };
        foreach (var category in catalog.categories)
            foreach (var item in category.items) item.starter = item.price == 0;
        EditorUtility.SetDirty(catalog);

        var player = AssetDatabase.LoadAssetAtPath<GameObject>(MiaPlayerPath);
        var model = player != null ? player.transform.Find("3D Model/Girl") : null;
        int icons = RenderIcons(catalog, model, MiaBodyParts);

        // The store sells to her too.
        var travel = Object.FindFirstObjectByType<ClothingStoreTravel>(FindObjectsInactive.Include);
        if (travel != null)
        {
            var jason = AssetDatabase.LoadAssetAtPath<ClothingCatalog>(CatalogPath);
            if (jason != null) jason.characterId = "jason";
            travel.catalogs = new[] { jason, catalog };
            EditorUtility.SetDirty(travel);
            EditorSceneManager.MarkSceneDirty(travel.gameObject.scene);
            EditorSceneManager.SaveScene(travel.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        return "Mia's catalog: " + catalog.categories.Sum(c => c.items.Length) + " items, " + icons + " thumbnails, store wired=" + (travel != null);
    }
}
