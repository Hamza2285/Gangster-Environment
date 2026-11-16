using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Pedestrians;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Switching between the playable characters. Hold the switch key for the character wheel, point at someone and let go:
// the camera leaves the current character in steps up into the sky, flies over the city and comes down in steps on the
// other one, who is somewhere else doing something of their own (standing, walking, running, on a bench or getting into a
// car). Only the played character is in the world; the one left behind waits switched off.
public class CharacterSwitcher : MonoBehaviour
{
    public static CharacterSwitcher Instance { get; private set; }

    public PlayableCharacter[] characters = new PlayableCharacter[0];
    [Tooltip("Where people stand and sit in the city; the characters are found at these spots.")]
    public AmbientSpotData spots;

    [Header("Where the other character turns up")]
    public float minDistance = 120f;
    public float maxDistance = 900f;

    [Header("Camera flight")]
    [Tooltip("Flight over the city.")]
    public float highHeight = 240f;
    [Range(20f, 89f)] public float highPitch = 78f;
    [Tooltip("Degrees the view turns while climbing, for a bit of life.")]
    public float climbTwist = 18f;

    [Header("Blur while moving")]
    public bool blurWhileMoving = true;
    [Tooltip("Camera speed (m/s) at which the blur is full.")]
    public float fullBlurSpeed = 140f;
    [Range(0f, 1f)] public float motionBlur = 0.85f;
    [Tooltip("Soft blur of the far city while the camera moves.")]
    [Range(0f, 1.5f)] public float distanceBlur = 1.1f;
    [Tooltip("Colour the picture takes on while the camera moves.")]
    public Color blurTint = new Color(1f, 0.78f, 0.9f, 1f);
    public float climbSeconds = 1.3f;
    [Tooltip("Short stop at the top and before landing.")]
    public float pauseSeconds = 0.12f;
    [Tooltip("The dive from the sky into the game's camera.")]
    public float landSeconds = 1.6f;
    [Tooltip("White flash on each move up and down.")]
    public bool flashes = false;
    [Range(0.05f, 1f)] public float wheelTimeScale = 0.25f;

    CharacterWheelUI wheel;
    Image flash;
    Camera skyCamera;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    int current;
    bool wheelOpen;

    public bool Busy { get; private set; }
    public PlayableCharacter Current => current >= 0 && current < characters.Length ? characters[current] : null;

    // Settings saved in the scene before a camera retune are brought up to date once.
    [SerializeField, HideInInspector] int cameraVersion;
    const int CameraVersion = 3;

    void UpgradeCamera()
    {
        if (cameraVersion >= CameraVersion) return;
        highHeight = 240f; highPitch = 78f; climbTwist = 18f;
        climbSeconds = 1.3f; landSeconds = 1.6f; pauseSeconds = 0.25f;
        blurWhileMoving = true; fullBlurSpeed = 140f; motionBlur = 0.85f; distanceBlur = 1.1f;
        cameraVersion = CameraVersion;
    }

    void OnValidate() => UpgradeCamera();

    void Awake()
    {
        UpgradeCamera();
        Instance = this;
        wheel = new GameObject("Character Wheel").AddComponent<CharacterWheelUI>();
        wheel.transform.SetParent(transform, false);
        BuildFlash();
    }

    void Start()
    {
        // Whoever is active in the scene is played; the rest wait.
        current = 0;
        for (int i = 0; i < characters.Length; i++)
            if (characters[i] != null && characters[i].gameObject.activeInHierarchy) { current = i; break; }
        for (int i = 0; i < characters.Length; i++)
            if (i != current && characters[i] != null) characters[i].gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (wheelOpen) Time.timeScale = 1f;
    }

    void Update()
    {
        if (Busy || characters.Length < 2) return;

        if (!wheelOpen)
        {
            if (!GameInput.SwitchDown) return;
            string why = WhyNot();
            if (why != null) { wheel.Flash(why); return; }
            wheelOpen = true;
            GamePlayer.Current.SetControl(false);
            Time.timeScale = wheelTimeScale;
            wheel.Open(characters, current);
            return;
        }

        if (GameInput.SwitchHeld && !GameInput.CancelDown) return;
        wheelOpen = false;
        Time.timeScale = 1f;
        int chosen = GameInput.CancelDown ? current : wheel.Selected;
        wheel.Close();
        if (chosen < 0 || chosen == current || characters[chosen] == null) { GamePlayer.Current.SetControl(true); return; }
        StartCoroutine(SwitchTo(chosen));
    }

    // Null when switching is fine right now; otherwise the reason, shown to the player.
    string WhyNot()
    {
        if (GamePlayer.MenuOpen || ClothingStoreMenu.IsOpen) return "Close the menu to switch character";
        if (GamePlayer.InVehicle) return "Get out of the vehicle to switch character";
        var travel = ClothingStoreTravel.Instance;
        if (travel != null && (travel.Inside || travel.Busy)) return "Leave the store to switch character";
        var manager = Current != null ? Current.GetComponent<OpenworldCrime.Player.PlayerManager>() : null;
        if (manager != null && manager.CurrentState == OpenworldCrime.Player.PlayerState.Dead) return "Can't switch now";
        if (GamePlayer.Current == null || !GamePlayer.Current.ControlEnabled) return "Can't switch now";
        return null;
    }

    IEnumerator SwitchTo(int index)
    {
        Busy = true;
        var from = characters[current];
        var to = characters[index];
        var player = GamePlayer.Current;
        player.SetControl(false);
        GamePlayer.MenuOpen = true;

        var main = Camera.main;
        skyCamera = MakeSkyCamera(main);
        if (blurWhileMoving) StartCoroutine(DriveBlur());
        HideScreens();

        Vector3 anchor = from.transform.position;
        float yaw = main != null ? main.transform.eulerAngles.y : from.transform.eulerAngles.y;
        var plan = CharacterArrival.Choose(spots, anchor, minDistance, maxDistance);

        // Up from the character in one go, straight to the view over the city.
        yield return Move(() => Aerial(anchor, highHeight, highPitch, yaw + climbTwist), climbSeconds);

        // Stop 1, at the top: the other character takes over the world, somewhere else. Their scripts start up over
        // the next two frames (Awake, then Start) before anything is asked of them.
        bool swapped = Safe(() => Swap(from, to, plan), "swap");
        if (swapped) current = index;
        yield return null;
        yield return null;
        CharacterArrival arrival = null;
        if (swapped) Safe(() => arrival = CharacterArrival.Begin(to, plan), "arrival");
        // Wearing what they last bought.
        if (swapped && ClothingStoreTravel.Instance != null) Safe(() => ClothingStoreTravel.Instance.DressPlayer(), "dress");
        Safe(() => GamePlayer.Current.SetControl(false), "lock");
        HideScreens();
        if (!swapped) to = from;
        yield return Hold(pauseSeconds);

        // Across the city, turning on the way to look the way the new character's camera looks, so the dive has
        // little left to turn.
        float distance = Vector3.Distance(Flat(anchor), Flat(to.transform.position));
        yield return Travel(Aerial(to.transform.position, highHeight, highPitch, LandingYaw(to)), Mathf.Clamp(0.6f + distance / 700f, 0.9f, 2.2f), Mathf.Min(distance * 0.12f, 110f));

        // Stop 2, over them (longer while they get into a car), then one dive into the game's camera.
        float waited = 0f;
        while (waited < pauseSeconds || (arrival != null && !arrival.Ready && waited < 10f))
        {
            waited += Time.unscaledDeltaTime;
            Pose hover = Aerial(to.transform.position, highHeight, highPitch, LandingYaw(to));
            var t = skyCamera.transform;
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, hover.position, k), Quaternion.Slerp(t.rotation, hover.rotation, k));
            yield return null;
        }
        yield return Land();

        Destroy(skyCamera.gameObject);
        ShowScreens();
        GamePlayer.MenuOpen = false;
        Safe(() => { GamePlayer.Current.SetControl(true); GamePlayer.Current.LockCursor(true); }, "unlock");
        if (arrival != null) Safe(arrival.Release, "release");
        Busy = false;
    }

    // One step of the switch; an error is logged instead of leaving the camera stuck in the sky.
    static bool Safe(System.Action action, string what)
    {
        try { action(); return true; }
        catch (System.Exception e) { Debug.LogException(e); Debug.LogError("[CharacterSwitcher] " + what + " failed"); return false; }
    }

    void Swap(PlayableCharacter from, PlayableCharacter to, ArrivalPlan plan)
    {
        // The city's traffic spawner rides along with whoever is played.
        var spawner = from.transform.Find("iTS Traffic Spawner");
        Vector3 spawnerPosition = Vector3.zero;
        Quaternion spawnerRotation = Quaternion.identity;
        if (spawner != null)
        {
            spawnerPosition = spawner.localPosition;
            spawnerRotation = spawner.localRotation;
            spawner.SetParent(null, true);
        }

        to.transform.SetPositionAndRotation(plan.position, Quaternion.Euler(0f, plan.yaw, 0f));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int changed = PlayerRetargeter.Retarget(from.gameObject, to.gameObject);
        long retargetMs = watch.ElapsedMilliseconds;
        to.gameObject.SetActive(true);
        from.gameObject.SetActive(false);

        if (spawner != null)
        {
            spawner.SetParent(to.transform, false);
            spawner.localPosition = spawnerPosition;
            spawner.localRotation = spawnerRotation;
        }
        // The one player camera now follows the new character.
        var follow = FindFirstObjectByType<Invector.vCamera.vThirdPersonCamera>();
        if (follow != null) Safe(() => follow.SetMainTarget(to.transform), "camera target");

        // The new character's own touch / keyboard screens follow the current controls mode.
        var apply = typeof(OpenworldCrime.Controls.ControlsModeManager).GetMethod("Apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (apply != null)
            foreach (var mode in FindObjectsByType<OpenworldCrime.Controls.ControlsModeManager>(FindObjectsSortMode.None))
                Safe(() => apply.Invoke(mode, null), "controls mode");
        Debug.Log("[CharacterSwitcher] " + from.displayName + " -> " + to.displayName + ": " + plan + ", " + changed + " references moved over in " + retargetMs + " ms");
    }

    // ------------------------------------------------------------------ camera

    Camera MakeSkyCamera(Camera like)
    {
        var go = new GameObject("Switch Sky Camera");
        var cam = go.AddComponent<Camera>();
        if (like != null)
        {
            cam.CopyFrom(like);
            go.transform.SetPositionAndRotation(like.transform.position, like.transform.rotation);
            var from = like.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var to = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (to == null) to = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (from != null) to.renderPostProcessing = from.renderPostProcessing;
            // The blur is a post effect.
            to.renderPostProcessing = true;
        }
        cam.targetTexture = null;
        cam.depth = 100f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2500f);
        go.tag = "Untagged";
        return cam;
    }

    // ------------------------------------------------------------------ blur

    // A volume over everything while the switch camera exists, as strong as the camera is fast: motion blur, the far city
    // softened, darker edges and a little less colour. Gone when the camera lands.
    IEnumerator DriveBlur()
    {
        var holder = new GameObject("Switch Blur");
        holder.transform.SetParent(transform, false);
        var volume = holder.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        volume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.profile = profile;

        var motion = profile.Add<MotionBlur>(true);
        motion.mode.Override(MotionBlurMode.CameraOnly);
        motion.quality.Override(MotionBlurQuality.High);
        motion.intensity.Override(motionBlur);
        motion.clamp.Override(0.12f);
        var depth = profile.Add<DepthOfField>(true);
        depth.mode.Override(DepthOfFieldMode.Gaussian);
        depth.gaussianStart.Override(8f);
        depth.gaussianEnd.Override(90f);
        depth.gaussianMaxRadius.Override(distanceBlur);
        depth.highQualitySampling.Override(true);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.32f);
        vignette.color.Override(new Color(0.45f, 0.08f, 0.3f, 1f));
        vignette.smoothness.Override(0.5f);
        var colour = profile.Add<ColorAdjustments>(true);
        colour.saturation.Override(-22f);
        colour.colorFilter.Override(blurTint);

        Vector3 last = skyCamera != null ? skyCamera.transform.position : Vector3.zero;
        float weight = 0f;
        // Runs on after landing until the blur has faded out.
        while (skyCamera != null || weight > 0.01f)
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector3 now = skyCamera != null ? skyCamera.transform.position : last;
            float speed = (now - last).magnitude / dt;
            last = now;
            float wanted = Mathf.Clamp01(speed / Mathf.Max(1f, fullBlurSpeed));
            // Quick to come in, a little slower to clear.
            weight = Mathf.MoveTowards(weight, wanted, dt * (wanted > weight ? 6f : 2.5f));
            volume.weight = weight;
            yield return null;
        }
        Destroy(holder);
        Destroy(profile);
    }

    // Looking down at a point from a height and an angle, from the side the yaw faces away from.
    static Pose Aerial(Vector3 point, float height, float pitch, float yaw)
    {
        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = -(rotation * Vector3.forward) * (height / Mathf.Sin(pitch * Mathf.Deg2Rad));
        return new Pose(point + Vector3.up * 1.4f + back, rotation);
    }

    // The way the camera that takes over at the end is looking (behind the character, or the car's camera).
    float LandingYaw(PlayableCharacter character)
    {
        var live = LiveCamera();
        return live != null ? live.transform.eulerAngles.y : character.transform.eulerAngles.y;
    }

    static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }

    // A smooth move to a pose that may itself be moving.
    IEnumerator Move(System.Func<Pose> target, float seconds)
    {
        var t = skyCamera.transform;
        Vector3 startPosition = t.position;
        Quaternion startRotation = t.rotation;
        if (flashes) StartCoroutine(Flash(0.14f));
        for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime)
        {
            float k = Ease(time / seconds);
            var goal = target();
            t.SetPositionAndRotation(Vector3.LerpUnclamped(startPosition, goal.position, k), Quaternion.Slerp(startRotation, goal.rotation, k));
            yield return null;
        }
        var end = target();
        t.SetPositionAndRotation(end.position, end.rotation);
    }

    // Across the city on a gentle arc.
    IEnumerator Travel(Pose to, float seconds, float arc)
    {
        var t = skyCamera.transform;
        Vector3 startPosition = t.position;
        Quaternion startRotation = t.rotation;
        for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime)
        {
            float k = Ease(time / seconds);
            Vector3 position = Vector3.Lerp(startPosition, to.position, k) + Vector3.up * arc * Mathf.Sin(k * Mathf.PI);
            t.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, to.rotation, k));
            yield return null;
        }
        t.SetPositionAndRotation(to.position, to.rotation);
    }

    IEnumerator Hold(float seconds)
    {
        for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime) yield return null;
    }

    // The last move glides into the game's own camera, wherever it is by then.
    IEnumerator Land()
    {
        var t = skyCamera.transform;
        Vector3 startPosition = t.position;
        Quaternion startRotation = t.rotation;
        float startFov = skyCamera.fieldOfView;
        if (flashes) StartCoroutine(Flash(0.1f));
        for (float time = 0f; time < landSeconds; time += Time.unscaledDeltaTime)
        {
            var live = LiveCamera();
            if (live == null) break;
            float k = Ease(time / landSeconds);
            t.SetPositionAndRotation(Vector3.Lerp(startPosition, live.transform.position, k), Quaternion.Slerp(startRotation, live.transform.rotation, k));
            skyCamera.fieldOfView = Mathf.Lerp(startFov, live.fieldOfView, k);
            yield return null;
        }
    }

    Camera LiveCamera()
    {
        Camera best = null;
        foreach (var cam in Camera.allCameras)
        {
            if (cam == skyCamera || cam.targetTexture != null) continue;
            if (best == null || (cam.CompareTag("MainCamera") && !best.CompareTag("MainCamera")) || (cam.CompareTag("MainCamera") == best.CompareTag("MainCamera") && cam.depth > best.depth)) best = cam;
        }
        return best;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ------------------------------------------------------------------ screen

    void BuildFlash()
    {
        var go = new GameObject("Switch Flash Canvas");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 390;
        flash = new GameObject("Flash", typeof(RectTransform)).AddComponent<Image>();
        flash.transform.SetParent(go.transform, false);
        var rect = flash.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        flash.raycastTarget = false;
        flash.color = new Color(1f, 1f, 1f, 0f);
    }

    IEnumerator Flash(float seconds)
    {
        for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime)
        {
            flash.color = new Color(1f, 1f, 1f, 0.55f * (1f - time / seconds));
            yield return null;
        }
        flash.color = new Color(1f, 1f, 1f, 0f);
    }

    // The game's HUD is out of the way during the flight.
    void HideScreens()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.transform.IsChildOf(transform)) continue;
            if (ScreenBlink.Instance != null && canvas.transform.IsChildOf(ScreenBlink.Instance.transform)) continue;
            canvas.enabled = false;
            hiddenCanvases.Add(canvas);
        }
    }

    void ShowScreens()
    {
        foreach (var canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
        hiddenCanvases.Clear();
    }
}
