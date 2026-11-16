using System.Collections.Generic;
using System.Text;
using UnityEngine;

// What the player wears, owns and can spend. Dresses the character by switching the model's clothing parts, and keeps
// all of it between sessions. Clothes only change in the clothing store; everywhere else this just wears what was bought.
public class PlayerWardrobe : MonoBehaviour
{
    // Jason keeps the keys he always had; anyone else has their own.
    string Prefix => catalog == null || catalog.characterId == "jason" ? "Wardrobe." : "Wardrobe." + catalog.characterId + ".";
    string EquippedKey => Prefix + "Equipped";
    string OwnedKey => Prefix + "Owned";
    string CashKey => Prefix + "Cash";

    public ClothingCatalog catalog;
    public int startingCash = 25000;

    readonly Dictionary<string, GameObject> parts = new Dictionary<string, GameObject>();
    readonly Dictionary<string, string> equipped = new Dictionary<string, string>();
    readonly Dictionary<string, string> showing = new Dictionary<string, string>();
    readonly HashSet<string> owned = new HashSet<string>();
    bool ready;

    public static PlayerWardrobe Current { get; private set; }
    public int Cash { get; private set; }
    public event System.Action Changed;

    // Puts a wardrobe on the player if it has none yet.
    public static PlayerWardrobe Attach(GameObject player, ClothingCatalog catalog)
    {
        var wardrobe = player.GetComponent<PlayerWardrobe>();
        if (wardrobe != null && wardrobe.catalog != catalog)
        {
            DestroyImmediate(wardrobe);
            wardrobe = null;
        }
        if (wardrobe == null)
        {
            wardrobe = player.AddComponent<PlayerWardrobe>();
            wardrobe.catalog = catalog;
        }
        wardrobe.Init();
        return wardrobe;
    }

    void Start()
    {
        Init();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    void Init()
    {
        Current = this;
        if (ready || catalog == null) return;
        ready = true;

        // Only meshes: the skeleton has bones with the same names as some parts (such as Neck).
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (!parts.ContainsKey(r.name)) parts[r.name] = r.gameObject;
        Load();
        foreach (var category in catalog.categories)
        {
            if (!equipped.ContainsKey(category.id) || catalog.FindItem(category, equipped[category.id]) == null)
                equipped[category.id] = WornNow(category);
            foreach (var item in category.items) if (item.starter || item.price == 0) owned.Add(item.id);
            owned.Add(equipped[category.id]);
        }
        Show(equipped);
    }

    // Before anything is saved: whatever the model already shows.
    string WornNow(ClothingCatalog.Category category)
    {
        foreach (var item in category.items)
        {
            if (item.parts.Length == 0) continue;
            bool all = true;
            foreach (var part in item.parts) all &= parts.TryGetValue(part, out var go) && go.activeSelf;
            if (all) return item.id;
        }
        return category.defaultItem;
    }

    public string EquippedId(string category) => equipped.TryGetValue(category, out var id) ? id : null;
    public string ShowingId(string category) => showing.TryGetValue(category, out var id) ? id : null;
    public bool Owns(ClothingCatalog.Item item) => item != null && owned.Contains(item.id);

    // Try something on without buying it.
    public void Preview(ClothingCatalog.Category category, ClothingCatalog.Item item)
    {
        var look = new Dictionary<string, string>(showing);
        look[category.id] = item.id;
        Show(look);
    }

    // Back to what is actually worn.
    public void CancelPreview()
    {
        Show(equipped);
    }

    public enum Result { Bought, Worn, AlreadyWearing, NotEnoughCash }

    public Result BuyOrWear(ClothingCatalog.Category category, ClothingCatalog.Item item)
    {
        if (EquippedId(category.id) == item.id && owned.Contains(item.id)) return Result.AlreadyWearing;
        var result = Result.Worn;
        if (!owned.Contains(item.id))
        {
            if (Cash < item.price) return Result.NotEnoughCash;
            Cash -= item.price;
            owned.Add(item.id);
            result = Result.Bought;
        }
        equipped[category.id] = item.id;
        // Anything else being tried on is dropped; only this purchase sticks.
        Show(equipped);
        Save();
        Changed?.Invoke();
        return result;
    }

    public void AddCash(int amount)
    {
        Cash = Mathf.Max(0, Cash + amount);
        Save();
        Changed?.Invoke();
    }

    void Show(Dictionary<string, string> look)
    {
        // Hide every part the catalog knows about, then show the chosen ones, so parts shared between items stay on.
        foreach (var category in catalog.categories)
            foreach (var item in category.items)
                foreach (var part in item.parts) SetPart(part, false);

        bool arms = false, legs = false;
        foreach (var category in catalog.categories)
        {
            if (!look.TryGetValue(category.id, out var id)) continue;
            var item = catalog.FindItem(category, id);
            if (item == null) continue;
            foreach (var part in item.parts) SetPart(part, true);
            arms |= item.showsArms;
            legs |= item.showsLegs;
        }
        SetPart(catalog.armsPart, arms);
        SetPart(catalog.legsPart, legs);

        showing.Clear();
        foreach (var pair in look) showing[pair.Key] = pair.Value;
    }

    void SetPart(string name, bool on)
    {
        if (!string.IsNullOrEmpty(name) && parts.TryGetValue(name, out var go) && go.activeSelf != on) go.SetActive(on);
    }

    void Load()
    {
        Cash = PlayerPrefs.GetInt(CashKey, startingCash);
        foreach (var pair in PlayerPrefs.GetString(EquippedKey, "").Split(';'))
        {
            int split = pair.IndexOf('=');
            if (split > 0) equipped[pair.Substring(0, split)] = pair.Substring(split + 1);
        }
        foreach (var id in PlayerPrefs.GetString(OwnedKey, "").Split(';')) if (id.Length > 0) owned.Add(id);
    }

    void Save()
    {
        var text = new StringBuilder();
        foreach (var pair in equipped) text.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
        PlayerPrefs.SetString(EquippedKey, text.ToString());
        PlayerPrefs.SetString(OwnedKey, string.Join(";", owned));
        PlayerPrefs.SetInt(CashKey, Cash);
        PlayerPrefs.Save();
    }
}
