using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes the player into the car showroom and back, like the clothing store: the showroom is its own scene, loaded on top
// of the city far away from it, so the city keeps going and coming back out puts the player at the door they used.
public class ShowroomTravel : MonoBehaviour
{
    public static ShowroomTravel Instance { get; private set; }

    public string showroomScene = "Car Showroom Interior";
    public float blinkCloseSeconds = 0.35f, blinkOpenSeconds = 0.45f;

    ShowroomEntrance usedEntrance;
    Scene loaded;

    public bool Busy { get; private set; }
    public bool Inside { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Enter(ShowroomEntrance entrance)
    {
        if (!Busy && !Inside) StartCoroutine(EnterRoutine(entrance));
    }

    public void Exit()
    {
        if (!Busy && Inside) StartCoroutine(ExitRoutine());
    }

    IEnumerator EnterRoutine(ShowroomEntrance entrance)
    {
        Busy = true;
        usedEntrance = entrance;
        var player = GamePlayer.Current;
        player.SetControl(false);
        yield return Blink(true);

        var loading = SceneManager.LoadSceneAsync(showroomScene, LoadSceneMode.Additive);
        while (loading != null && !loading.isDone) yield return null;
        loaded = SceneManager.GetSceneByName(showroomScene);
        var interior = ShowroomInterior.Instance;
        if (interior == null)
        {
            Debug.LogError("[ShowroomTravel] The showroom scene '" + showroomScene + "' did not load. Is it in the build settings?");
            player.SetControl(true);
            yield return Blink(false);
            Busy = false;
            yield break;
        }

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
        if (loaded.IsValid() && loaded.isLoaded)
        {
            var unloading = SceneManager.UnloadSceneAsync(loaded);
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
