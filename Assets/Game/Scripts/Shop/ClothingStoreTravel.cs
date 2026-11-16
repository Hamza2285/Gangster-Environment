using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes the player into the clothing store and back. The store is its own scene, loaded on top of the city far away from
// it, so the city keeps going (traffic, time of day) and coming back out puts the player at the door they went in by.
public class ClothingStoreTravel : MonoBehaviour
{
    public static ClothingStoreTravel Instance { get; private set; }

    public string storeScene = "Clothing Store Interior";
    [Tooltip("Jason's clothes.")]
    public ClothingCatalog catalog;
    [Tooltip("Every character's clothes; the store sells to whoever walks in.")]
    public ClothingCatalog[] catalogs = new ClothingCatalog[0];
    public float blinkCloseSeconds = 0.35f, blinkOpenSeconds = 0.45f;

    // Who is being played, as a catalog character id. A character-switching system can set this; without one it is Jason.
    public static System.Func<string> CurrentCharacterId;

    ClothingStoreEntrance usedEntrance;
    Scene loadedStore;

    // The clothes of whoever is being played (each character's catalog is made of their own model's pieces).
    public ClothingCatalog CurrentCatalog
    {
        get
        {
            string id = CurrentCharacterId != null ? CurrentCharacterId() : "jason";
            foreach (var c in catalogs) if (c != null && c.characterId == id) return c;
            return catalog != null && catalog.characterId == id ? catalog : null;
        }
    }

    public static bool ForCurrentCharacter => Instance != null && Instance.CurrentCatalog != null;

    public bool Busy { get; private set; }
    public bool Inside { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // The player wears what they bought last time from the start, not only after a visit.
        DressPlayer();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Puts on what the current character bought last time.
    public PlayerWardrobe DressPlayer()
    {
        var player = GamePlayer.Current;
        var clothes = CurrentCatalog;
        if (player == null || clothes == null) return null;
        return PlayerWardrobe.Attach(player.transform.gameObject, clothes);
    }

    public void Enter(ClothingStoreEntrance entrance)
    {
        if (!Busy && !Inside) StartCoroutine(EnterRoutine(entrance));
    }

    public void Exit()
    {
        if (!Busy && Inside) StartCoroutine(ExitRoutine());
    }

    IEnumerator EnterRoutine(ClothingStoreEntrance entrance)
    {
        Busy = true;
        usedEntrance = entrance;
        var player = GamePlayer.Current;
        player.SetControl(false);
        yield return Blink(true);

        var loading = SceneManager.LoadSceneAsync(storeScene, LoadSceneMode.Additive);
        while (loading != null && !loading.isDone) yield return null;
        loadedStore = SceneManager.GetSceneByName(storeScene);
        var interior = ClothingStoreInterior.Instance;
        if (interior == null)
        {
            Debug.LogError("[ClothingStoreTravel] The store scene '" + storeScene + "' did not load. Is it in the build settings?");
            player.SetControl(true);
            yield return Blink(false);
            Busy = false;
            yield break;
        }

        DressPlayer();
        Inside = true;
        // Hides the minimap and keeps the taxi away while inside.
        GamePlayer.MenuOpen = true;
        player.Teleport(interior.arrival.position, interior.arrival.eulerAngles.y);
        player.ResetLook();
        yield return null;
        player.SetControl(true);
        yield return Blink(false);
        Busy = false;
    }

    IEnumerator ExitRoutine()
    {
        Busy = true;
        var player = GamePlayer.Current;
        player.SetControl(false);
        yield return Blink(true);

        var exit = usedEntrance != null && usedEntrance.exitPoint != null ? usedEntrance.exitPoint : null;
        if (exit != null)
        {
            player.Teleport(exit.position, exit.eulerAngles.y);
            player.ResetLook();
        }
        if (loadedStore.IsValid() && loadedStore.isLoaded)
        {
            var unloading = SceneManager.UnloadSceneAsync(loadedStore);
            while (unloading != null && !unloading.isDone) yield return null;
        }
        Inside = false;
        GamePlayer.MenuOpen = false;
        yield return null;
        player.SetControl(true);
        yield return Blink(false);
        Busy = false;
    }

    IEnumerator Blink(bool closing)
    {
        var blink = ScreenBlink.Instance;
        if (blink == null) yield break;
        yield return closing ? blink.Close(blinkCloseSeconds) : blink.Open(blinkOpenSeconds);
    }
}
