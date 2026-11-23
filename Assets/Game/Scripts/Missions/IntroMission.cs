using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Player;
using OpenworldCrime.Police;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The opening mission. The game starts on a cutscene: Jason drives a supercar with Mia beside him, police cars right behind,
// the camera cutting between angles (letterbox, subtitles, SKIP). A blink, and the player has the wheel at speed:
// 1. lose the cops (wanted level back to zero);
// 2. a route on the road and a marker lead to the drop-off (a Miami Clothing Store) - "Drop Mia at ...";
// 3. stopped there, Mia gets out and walks into the store: MISSION COMPLETE and a cash reward.
// Plays once per install (PlayerPrefs; Game/Reset Intro Mission plays it again) and only when Jason is the one playing.
public class IntroMission : MonoBehaviour
{
    const string DoneKey = "IntroMissionDone";

    [Header("Setup")]
    public GameObject carPrefab;
    [Tooltip("Play it every time the game scene starts (testing). Off: once per install.")]
    public bool playEveryTime;
    [Tooltip("Name of the ClothingStoreEntrance Mia is dropped at (its exit point, on the kerb nearest it).")]
    public string dropOffEntrance = "Miami Clothing Store Entrance (Miami Clothing Store (1))";
    public string dropOffName = "the Miami Clothing Store";
    public int reward = 1500;

    [Header("Chase")]
    [Tooltip("The chase starts on the road nearest this point (a long straight road with nothing parked on it).")]
    public Vector3 chaseStart = new Vector3(389f, 28f, 327f);
    public int wantedStars = 3;
    public int policeCars = 3;
    [Tooltip("Car speed through the cutscene (m/s).")]
    public float cutsceneSpeed = 21f;
    public float shotSeconds = 2.4f;

    [Header("Mia in the passenger seat")]
    [Tooltip("Added to the mirrored driver seat (car space).")]
    public Vector3 passengerOffset = new Vector3(0f, 0f, 0f);

    static readonly string[] Subtitles =
    {
        "MIA:  Jason... they're right on us!",
        "JASON:  I see them. Hold on.",
        "MIA:  You're crazy, you know that?",
        "JASON:  That's one down.",
        "MIA:  Lose the rest, then get me to the store!",
    };

    enum Phase { None, Cutscene, LoseCops, Drive, Stop, Done }
    Phase phase;

    PlayerVehicleInteraction driver;
    IVehicleController vehicle;
    GameObject car;
    Rigidbody carBody;
    GameObject mia;
    PlayableGraph miaGraph;
    Vector3 dropPoint, storeDoor;
    readonly List<Vector3> path = new List<Vector3>();
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    readonly List<Camera> hiddenCameras = new List<Camera>();
    bool skip;

    // HUD
    Canvas hud;
    Text objective, subtitle, banner, bannerSub;
    Image objectivePanel;
    RectTransform barTop, barBottom;
    GameObject skipButton;
    Font font;

    void Start()
    {
        if (!playEveryTime && PlayerPrefs.GetInt(DoneKey, 0) == 1) return;
        if (carPrefab == null) return;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // Hidden while the city wakes up.
        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0f);
        HideGameView();
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { HideGameView(); yield return null; }   // HUD built in Start too

        var player = GamePlayer.Current;
        if (player == null || PlayableCharacter.CurrentId != "jason") { RestoreGameView(); if (blink != null) yield return blink.Open(0.4f); yield break; }
        driver = player.transform.GetComponent<PlayerVehicleInteraction>();
        if (driver == null || driver.State != VehicleInteractionState.OnFoot || !FindDropOff()) { RestoreGameView(); if (blink != null) yield return blink.Open(0.4f); yield break; }

        BuildHud();
        if (!SetUpChase(chaseStart)) { Cleanup(); if (blink != null) yield return blink.Open(0.4f); yield break; }
        CharacterSwitcher.BlockedReason = "Mia is riding with you";

        // 1. Cutscene.
        phase = Phase.Cutscene;
        GamePlayer.MenuOpen = true;
        HideGameView();
        driver.SetRealDriverVisible(true);       // the close-ups show Jason himself, not the car's driver dummy
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) { AlignMiaWithJason(); yield return null; }
        var cam = MakeCamera();
        var carHealth = car.GetComponent<VehicleHealth>();
        if (carHealth != null) carHealth.Invulnerable = true;   // the blast is staged right behind it
        StartCoroutine(AutoDrive());
        StartCoroutine(Letterbox(true));
        if (blink != null) StartCoroutine(blink.Open(0.5f));
        yield return Shots(cam);
        EndSlowMotion();
        driver.SetRealDriverVisible(false);

        // The player takes over at speed.
        if (blink != null) yield return blink.Close(0.25f);
        phase = Phase.LoseCops;
        Destroy(cam.gameObject);
        StartCoroutine(Letterbox(false));
        subtitle.text = "";
        skipButton.SetActive(false);
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        if (blink != null) yield return blink.Open(0.35f);
        SetObjective("Lose the cops");
        StartCoroutine(EndInvulnerability(carHealth, 4f));

        // 2. Lose the cops, then the drive.
        var wanted = WantedManager.Instance;
        while (wanted != null && wanted.Stars > 0)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            yield return null;
        }
        phase = Phase.Drive;
        Banner("COPS LOST", "", 2.2f);
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.SetMarker(dropPoint);

        while (true)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            bool inCar = driver.State == VehicleInteractionState.InVehicle && driver.CurrentVehicle == vehicle;
            float distance = Flat(car.transform.position - dropPoint).magnitude;
            if (!inCar) SetObjective("Get back in the car - Mia is waiting");
            else if (wanted != null && wanted.Stars > 0) SetObjective("Lose the cops");
            else if (distance > 14f) SetObjective("Drop Mia at " + dropOffName);
            else
            {
                SetObjective("Stop the car");
                if (carBody.linearVelocity.magnitude < 1.5f) break;
            }
            if (inCar && WaypointSystem.Instance != null && !WaypointSystem.Instance.HasMarker) WaypointSystem.Instance.SetMarker(dropPoint);
            yield return null;
        }

        // 3. Drop-off.
        phase = Phase.Stop;
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
        SetObjective("");
        yield return DropOff();
        phase = Phase.Done;
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        var money = OpenworldCrime.Economy.MoneyManager.Instance;
        if (money != null) money.Add(reward);
        Banner("MISSION COMPLETE", "+$" + reward.ToString("N0"), 4.5f);
        CharacterSwitcher.BlockedReason = null;
        yield return new WaitForSeconds(5f);
        Cleanup();
    }

    // ---------- Setup ----------

    bool FindDropOff()
    {
        foreach (var e in FindObjectsByType<ClothingStoreEntrance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (e.name != dropOffEntrance) continue;
            storeDoor = e.transform.position;
            Vector3 near = e.exitPoint != null ? e.exitPoint.position : e.transform.position;
            dropPoint = RoadNetwork.Instance != null && RoadNetwork.Instance.TryFindCurbStop(near, out RoadNetwork.RoadPoint stop) ? stop.position : near;
            return true;
        }
        return false;
    }

    bool SetUpChase(Vector3 around)
    {
        var network = RoadNetwork.Instance;
        if (network == null || !network.TryFindNearest(around, out RoadNetwork.RoadPoint start)) return false;
        path.Clear();
        path.AddRange(network.BuildDepartPath(start, 760f, new List<RoadNetwork.Link>()));
        if (path.Count < 4) return false;

        Vector3 forward = Flat(path[3] - path[0]).normalized;
        car = Instantiate(carPrefab, path[0] + Vector3.up * 0.4f, Quaternion.LookRotation(forward));
        car.name = "Intro Car";
        carBody = car.GetComponent<Rigidbody>();
        vehicle = car.GetComponent<IVehicleController>();
        if (vehicle == null || carBody == null || !driver.EnterInstant(vehicle)) { Destroy(car); return false; }
        carBody.linearVelocity = forward * cutsceneSpeed;

        mia = MakeMiaPuppet();

        var wanted = WantedManager.Instance;
        if (wanted != null) wanted.SetWanted(wantedStars, false);
        var dispatcher = FindFirstObjectByType<PoliceDispatcher>();
        if (dispatcher != null)
        {
            // Cruisers on the car's tail, along the road it came from.
            var behind = network.BuildApproachPath(start, 90f, new List<RoadNetwork.Link>(), out _);
            float[] gaps = { 16f, 30f, 44f, 58f };
            for (int i = 0; i < Mathf.Min(policeCars, gaps.Length); i++)
            {
                if (!SampleBack(behind, gaps[i], out Vector3 at, out Vector3 heading)) break;
                Vector3 side = Vector3.Cross(Vector3.up, heading) * (i % 2 == 0 ? 0.8f : -0.8f);
                dispatcher.SpawnChaseCar(at + side + Vector3.up * 0.3f, Quaternion.LookRotation(heading), heading * cutsceneSpeed * 1.05f);
            }
        }
        return true;
    }

    // A point the given distance back from the end of a path, and the direction of travel there.
    static bool SampleBack(List<Vector3> p, float distance, out Vector3 at, out Vector3 heading)
    {
        at = heading = Vector3.zero;
        float travelled = 0f;
        for (int i = p.Count - 1; i > 0; i--)
        {
            float segment = Vector3.Distance(p[i], p[i - 1]);
            if (travelled + segment >= distance)
            {
                float k = (distance - travelled) / Mathf.Max(segment, 0.001f);
                at = Vector3.Lerp(p[i], p[i - 1], k);
                heading = Flat(p[i] - p[i - 1]).normalized;
                return heading.sqrMagnitude > 0.5f;
            }
            travelled += segment;
        }
        return false;
    }

    // Mia: a copy of her character with only the body (no controller, physics or UI), sitting beside Jason.
    GameObject MakeMiaPuppet()
    {
        PlayableCharacter source = null;
        foreach (var c in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            if (c != null && c.gameObject.scene.IsValid() && c.id == "girl") source = c;
        if (source == null) return null;

        bool wasActive = source.gameObject.activeSelf;
        source.gameObject.SetActive(false);              // the copy starts inactive: none of its scripts wake up
        var puppet = Instantiate(source.gameObject);
        source.gameObject.SetActive(wasActive);
        puppet.name = "Mia (intro passenger)";
        var invector = puppet.transform.Find("Invector Components");
        if (invector != null) DestroyImmediate(invector.gameObject);
        StripToBody(puppet);

        var seat = vehicle.GetDriverSeat();
        Vector3 local = car.transform.InverseTransformPoint(seat.position);
        local.x = -local.x;
        puppet.transform.SetParent(car.transform, false);
        puppet.transform.localPosition = local + passengerOffset;
        puppet.transform.localRotation = Quaternion.Inverse(car.transform.rotation) * seat.rotation;
        puppet.SetActive(true);

        var animator = puppet.GetComponent<Animator>();
        if (animator != null && source.sitClip != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            miaGraph = PlayableGraph.Create("Mia seated");
            var output = AnimationPlayableOutput.Create(miaGraph, "Seat", animator);
            output.SetSourcePlayable(AnimationClipPlayable.Create(miaGraph, source.sitClip));
            miaGraph.Play();
        }
        foreach (var r in puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
        return puppet;
    }

    // Everything but the body (transforms, animator, renderers) goes, in an order Unity accepts: a component only once nothing
    // left on its object still needs it (RequireComponent, a joint's rigidbody), so no "can't remove" errors are logged.
    static void StripToBody(GameObject root)
    {
        var left = new List<Component>();
        foreach (var c in root.GetComponentsInChildren<Component>(true))
            if (c != null && !(c is Transform) && !(c is Animator) && !(c is Renderer) && !(c is MeshFilter) && !(c is LODGroup)) left.Add(c);
        for (int pass = 0; pass < 12 && left.Count > 0; pass++)
        {
            for (int i = left.Count - 1; i >= 0; i--)
            {
                Component c = left[i];
                if (c == null) { left.RemoveAt(i); continue; }
                if (NeededByOthers(c, left)) continue;
                DestroyImmediate(c);
                left.RemoveAt(i);
            }
        }
    }

    static bool NeededByOthers(Component target, List<Component> left)
    {
        var type = target.GetType();
        foreach (var other in left)
        {
            if (other == null || other == target || other.gameObject != target.gameObject) continue;
            if (target is Rigidbody && other is Joint) return true;
            foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (require.m_Type0 != null && require.m_Type0.IsAssignableFrom(type)) return true;
                if (require.m_Type1 != null && require.m_Type1.IsAssignableFrom(type)) return true;
                if (require.m_Type2 != null && require.m_Type2.IsAssignableFrom(type)) return true;
            }
        }
        return false;
    }

    // ---------- Cutscene ----------

    // The car follows the road path at cutscene speed (physics velocity, so the wheels roll) until the player takes over.
    IEnumerator AutoDrive()
    {
        float travelled = 0f;
        Vector3 last = Flat(car.transform.position);
        var wait = new WaitForFixedUpdate();
        while (phase == Phase.Cutscene && car != null)
        {
            Vector3 now = Flat(car.transform.position);
            travelled += Vector3.Distance(now, last);
            last = now;
            Vector3 aim = Flat(Sample(travelled + 9f) - car.transform.position);
            Vector3 heading = Flat(Sample(travelled + 5f) - Sample(travelled)).normalized;
            if (aim.sqrMagnitude > 0.01f)
            {
                Vector3 v = aim.normalized * cutsceneSpeed;
                v.y = carBody.linearVelocity.y;
                carBody.linearVelocity = v;
            }
            if (heading.sqrMagnitude > 0.5f)
            {
                // Yaw toward the road, kept level: a knock from a cruiser must not roll the car mid-cutscene.
                Vector3 now2 = Flat(carBody.rotation * Vector3.forward);
                Vector3 yaw = Vector3.Slerp(now2.sqrMagnitude > 0.01f ? now2.normalized : heading, heading, Time.fixedDeltaTime * 5f);
                carBody.MoveRotation(Quaternion.LookRotation(yaw, Vector3.up));
            }
            carBody.angularVelocity = Vector3.zero;
            yield return wait;
        }
    }

    Vector3 Sample(float distance)
    {
        for (int i = 1; i < path.Count; i++)
        {
            float segment = Vector3.Distance(path[i - 1], path[i]);
            if (distance <= segment) return Vector3.Lerp(path[i - 1], path[i], distance / Mathf.Max(segment, 0.001f));
            distance -= segment;
        }
        return path[path.Count - 1];
    }

    // ---------- The shots ----------
    // Each shot is a camera move: positions and a look target along it (car space: x toward Mia's side, y up, z forward), a lens,
    // a length in real seconds, handheld shake, an optional subtitle and an optional event (the cruiser blast).

    class Shot
    {
        public float seconds = 2.4f, fovFrom = 45f, fovTo = 45f, shake = 0.5f;
        public int subtitle = -1;
        public System.Func<float, Vector3> position;   // k (0..1, eased) -> world position
        public System.Func<float, Vector3> look;       // k -> world look-at point
        public System.Action onStart;
        public float eventAt = -1f;                     // seconds into the shot
        public System.Action onEvent;
    }

    Vector3 Car(float x, float y, float z) => car.transform.TransformPoint(new Vector3(x, y, z));
    Vector3 Car(Vector3 local) => car.transform.TransformPoint(local);

    // Their heads, from the bones (Jason's real model is shown during the cutscene; Mia is the puppet).
    Vector3 JasonHead => Bone(driver != null ? driver.GetComponent<Animator>() : null, HumanBodyBones.Head, Car(-0.4f, 1.0f, 0.1f));
    Vector3 MiaHead => Bone(mia != null ? mia.GetComponent<Animator>() : null, HumanBodyBones.Head, Car(0.4f, 1.0f, 0.1f));
    Vector3 Heads => Vector3.Lerp(JasonHead, MiaHead, 0.5f);

    static Vector3 Bone(Animator animator, HumanBodyBones bone, Vector3 fallback)
    {
        if (animator == null || !animator.isHuman) return fallback;
        Transform t = animator.GetBoneTransform(bone);
        return t != null ? t.position : fallback;
    }

    // Mia's sitting pose (a bench clip) sits lower than Jason's driving pose: her hips go where his are, mirrored across the car.
    void AlignMiaWithJason()
    {
        var jason = driver.GetComponent<Animator>();
        var her = mia != null ? mia.GetComponent<Animator>() : null;
        if (jason == null || her == null || !jason.isHuman || !her.isHuman) return;
        Transform jh = jason.GetBoneTransform(HumanBodyBones.Hips), mh = her.GetBoneTransform(HumanBodyBones.Hips);
        if (jh == null || mh == null) return;
        Vector3 jl = car.transform.InverseTransformPoint(jh.position);
        Vector3 ml = car.transform.InverseTransformPoint(mh.position);
        Vector3 target = new Vector3(-jl.x, jl.y, jl.z) + passengerOffset;
        mia.transform.localPosition += target - ml;
    }

    Vector3 SeatLocal(float side)
    {
        Vector3 local = car.transform.InverseTransformPoint(vehicle.GetDriverSeat().position);
        local.x = Mathf.Abs(local.x) * side;
        return local;
    }

    List<Shot> BuildShots()
    {
        var shots = new List<Shot>();
        PoliceCarUnit victim = null;
        Vector3 fixedSpot = Vector3.zero;

        // 1. Establishing: a crane coming down onto the chase, the light bars streaming behind.
        shots.Add(new Shot { seconds = 3.4f, fovFrom = 55f, fovTo = 48f, shake = 0.15f,
            position = k => Car(Vector3.Lerp(new Vector3(-16f, 24f, 34f), new Vector3(-8f, 5f, 20f), k)),
            look = k => Car(0f, 0f, Mathf.Lerp(-18f, -6f, k)) });
        // 2. Through the windscreen: both of them.
        shots.Add(new Shot { seconds = 3.0f, fovFrom = 34f, fovTo = 30f, shake = 0.35f, subtitle = 0,
            position = k => Heads + car.transform.forward * Mathf.Lerp(2.3f, 1.85f, k) + car.transform.right * 0.12f + Vector3.up * 0.12f,
            look = k => Heads + Vector3.down * 0.08f });
        // 3. Low along the wheels, tracking back from the front arch.
        shots.Add(new Shot { seconds = 2.2f, fovFrom = 50f, fovTo = 50f, shake = 0.6f,
            position = k => Car(Vector3.Lerp(new Vector3(2.2f, 0.35f, 3.0f), new Vector3(2.1f, 0.35f, -2.4f), k)),
            look = k => Car(0f, 0.35f, Mathf.Lerp(0.8f, -1.2f, k)) });
        // 4. Jason through his window.
        shots.Add(new Shot { seconds = 2.4f, fovFrom = 30f, fovTo = 26f, shake = 0.5f, subtitle = 1,
            position = k => JasonHead + car.transform.forward * Mathf.Lerp(1.8f, 1.5f, k) - car.transform.right * 0.2f + Vector3.up * 0.14f,
            look = k => JasonHead + Vector3.down * 0.05f });
        // 5. Low on the rear bumper, looking back: the cruisers closing in, lights flashing.
        shots.Add(new Shot { seconds = 2.2f, fovFrom = 40f, fovTo = 34f, shake = 0.9f,
            position = k => Car(Vector3.Lerp(new Vector3(0.7f, 0.75f, -3.0f), new Vector3(0.5f, 0.65f, -3.4f), k)),
            look = k => { var cop = NearestCruiser(null); return cop != null ? cop.transform.position + Vector3.up * 0.9f : Car(0f, 0.7f, -25f); } });
        // 6. Mia, looking back at them.
        shots.Add(new Shot { seconds = 2.4f, fovFrom = 30f, fovTo = 27f, shake = 0.5f, subtitle = 2,
            position = k => MiaHead + car.transform.forward * Mathf.Lerp(1.75f, 1.45f, k) + car.transform.right * 0.55f + Vector3.up * 0.12f,
            look = k => MiaHead + Vector3.down * 0.05f });
        // 7. The blast: low behind the car, the last cruiser clips the kerb, spins and goes up in slow motion.
        shots.Add(new Shot { seconds = 4.2f, fovFrom = 32f, fovTo = 26f, shake = 0.4f, subtitle = 3,
            onStart = () => victim = FarthestCruiser(),
            position = k => Car(Vector3.Lerp(new Vector3(-1.9f, 0.9f, -5.2f), new Vector3(-2.8f, 1.4f, -8.0f), k)),
            look = k => victim != null ? victim.transform.position + Vector3.up * 0.8f : Car(0f, 0.5f, -25f),
            eventAt = 0.6f, onEvent = () => StartCoroutine(BlowUp(victim)) });
        // 8. Head-on, low: the supercar coming out of the smoke toward the lens.
        shots.Add(new Shot { seconds = 2.6f, fovFrom = 38f, fovTo = 44f, shake = 0.3f, subtitle = 4,
            position = k => Car(Vector3.Lerp(new Vector3(1.6f, 0.45f, 20f), new Vector3(2.4f, 0.45f, 9f), k)),
            look = k => Car(0f, 0.6f, 0f) });
        // 9. A locked-off camera at the roadside: the car tears past it.
        shots.Add(new Shot { seconds = 2.4f, fovFrom = 46f, fovTo = 46f, shake = 0.1f,
            onStart = () => fixedSpot = Car(4.5f, 0.8f, 24f),
            position = k => fixedSpot,
            look = k => Car(0f, 0.6f, 0f) });
        return shots;
    }

    IEnumerator Shots(Camera cam)
    {
        var shots = BuildShots();
        float seed = Random.value * 100f;
        foreach (var shot in shots)
        {
            if (skip) break;
            shot.onStart?.Invoke();
            if (shot.subtitle >= 0 && shot.subtitle < Subtitles.Length) subtitle.text = Subtitles[shot.subtitle];
            bool fired = false;
            for (float t = 0f; t < shot.seconds && !skip; t += Time.unscaledDeltaTime)
            {
                HideGameView();   // the car's touch pedals and dashboard switch on a moment after he is seated
                if (GameInput.CancelDown || GameInput.EnterDown) skip = true;
                if (!fired && shot.onEvent != null && t >= shot.eventAt) { fired = true; shot.onEvent(); }
                float k = Mathf.SmoothStep(0f, 1f, t / shot.seconds);
                Vector3 position = shot.position(k);
                Vector3 look = shot.look(k);
                // Handheld: a slow drift, plus the kick of a blast.
                float time = Time.unscaledTime + seed;
                float shake = shot.shake + impulse;
                Vector3 drift = new Vector3(Mathf.PerlinNoise(time * 1.3f, 0f) - 0.5f, Mathf.PerlinNoise(0f, time * 1.1f) - 0.5f, 0f) * 0.08f * shake;
                cam.transform.position = position;
                cam.transform.rotation = Quaternion.LookRotation(look - position);
                cam.transform.position += cam.transform.TransformVector(drift);
                cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(time * 2.3f, 3f) - 0.5f) * 1.6f * shake,
                    (Mathf.PerlinNoise(5f, time * 2.1f) - 0.5f) * 1.6f * shake, (Mathf.PerlinNoise(time * 0.7f, 9f) - 0.5f) * 1.2f * shake);
                cam.fieldOfView = Mathf.Lerp(shot.fovFrom, shot.fovTo, k);
                impulse = Mathf.MoveTowards(impulse, 0f, Time.unscaledDeltaTime * 2.5f);
                yield return null;
            }
        }
    }

    float impulse;

    IEnumerator EndInvulnerability(VehicleHealth health, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (health != null) health.Invulnerable = false;
    }

    // The cruiser clips the kerb, slews sideways and goes up; the moment plays at a third of the speed.
    IEnumerator BlowUp(PoliceCarUnit cruiser)
    {
        if (cruiser == null) yield break;
        var body = cruiser.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.AddForce(cruiser.transform.right * 7f + Vector3.up * 1.5f, ForceMode.VelocityChange);
            body.AddTorque(Vector3.up * 2.6f + cruiser.transform.forward * 1.2f, ForceMode.VelocityChange);
        }
        yield return new WaitForSecondsRealtime(0.35f);
        if (cruiser == null) yield break;
        StartSlowMotion(0.3f);
        var health = cruiser.GetComponent<VehicleHealth>();
        if (health != null) health.ApplyDamage(100000f, car.transform);
        impulse = 2.5f;
        if (body != null)
        {
            body.AddForce(Vector3.up * 7f + cruiser.transform.right * 3f, ForceMode.VelocityChange);
            body.AddTorque(cruiser.transform.forward * 4f, ForceMode.VelocityChange);
        }
        yield return new WaitForSecondsRealtime(1.8f);
        EndSlowMotion();
    }

    float normalFixedStep = -1f;

    void StartSlowMotion(float scale)
    {
        if (normalFixedStep < 0f) normalFixedStep = Time.fixedDeltaTime;
        Time.timeScale = scale;
        Time.fixedDeltaTime = normalFixedStep * scale;   // smooth physics at the slower rate
    }

    void EndSlowMotion()
    {
        Time.timeScale = 1f;
        if (normalFixedStep > 0f) Time.fixedDeltaTime = normalFixedStep;
    }

    PoliceCarUnit NearestCruiser(PoliceCarUnit not)
    {
        PoliceCarUnit best = null;
        float bestDistance = float.MaxValue;
        foreach (var unit in PoliceCarUnit.Active)
        {
            if (unit == null || unit == not) continue;
            float d = (unit.transform.position - car.transform.position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = unit; }
        }
        return best;
    }

    // The closest cruiser that is still far enough back for the blast not to reach the supercar.
    PoliceCarUnit FarthestCruiser()
    {
        PoliceCarUnit best = null;
        float bestDistance = 0f;
        foreach (var unit in PoliceCarUnit.Active)
        {
            if (unit == null) continue;
            float d = Vector3.Distance(unit.transform.position, car.transform.position);
            if (d > 22f && d < 55f && (best == null || d < bestDistance)) { bestDistance = d; best = unit; }
        }
        return best != null ? best : NearestCruiser(null);
    }

    Camera MakeCamera()
    {
        Camera main = Camera.main;
        var go = new GameObject("Intro Camera");
        var cam = go.AddComponent<Camera>();
        if (main != null)
        {
            cam.CopyFrom(main);
            var from = main.GetUniversalAdditionalCameraData();
            var to = cam.GetUniversalAdditionalCameraData();
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.renderShadows = from.renderShadows;
        }
        cam.fieldOfView = 52f;
        cam.nearClipPlane = 0.05f;   // the windscreen shot sits on the bonnet
        cam.depth = 50f;
        cam.targetTexture = null;
        // Only this camera draws while the cutscene runs.
        foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (other != cam && other.enabled && other.targetTexture == null) { other.enabled = false; hiddenCameras.Add(other); }
        return cam;
    }

    void HideGameView()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.enabled || canvas == hud || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (canvas.name == "Blink Canvas" || canvas.GetComponentInParent<OpenworldCrime.UI.Menu.LoadingScreen>() != null) continue;
            canvas.enabled = false;
            hiddenCanvases.Add(canvas);
        }
    }

    void RestoreGameView()
    {
        foreach (var canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
        hiddenCanvases.Clear();
        foreach (var cam in hiddenCameras) if (cam != null) cam.enabled = true;
        hiddenCameras.Clear();
    }

    IEnumerator Letterbox(bool show)
    {
        float from = show ? 0f : 1f, to = show ? 1f : 0f;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(from, to, t / 0.5f);
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f * k);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f * to);
    }

    // ---------- Drop-off, failure ----------

    IEnumerator DropOff()
    {
        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0.3f);
        Vector3 door = car.transform.position + car.transform.right * 2.3f;
        if (Physics.Raycast(door + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, ~(1 << 2), QueryTriggerInteraction.Ignore)) door = hit.point;
        Vector3 toStore = Flat(storeDoor - door);
        if (mia != null)
        {
            if (miaGraph.IsValid()) miaGraph.Destroy();
            mia.transform.SetParent(null, true);
            mia.transform.SetPositionAndRotation(door, Quaternion.LookRotation(toStore.sqrMagnitude > 0.01f ? toStore.normalized : car.transform.right));
            var animator = mia.GetComponent<Animator>();
            if (animator != null)
            {
                // Her own locomotion controller, walking (no controller script drives it: the parameters are set here).
                animator.Rebind();
                SetFloat(animator, "InputMagnitude", 0.5f);
                SetFloat(animator, "InputVertical", 0.5f);
                SetBool(animator, "IsGrounded", true);
            }
        }
        // A short shot of her getting out and walking into the store: from the street, past the car, toward the door.
        phase = Phase.Stop;
        GamePlayer.MenuOpen = true;
        HideGameView();
        var cam = MakeCamera();
        Vector3 across = Flat(storeDoor - car.transform.position).normalized;
        if (across.sqrMagnitude < 0.5f) across = car.transform.right;
        Vector3 shotFrom = car.transform.position - across * 5.5f + car.transform.forward * 4.5f + Vector3.up * 1.9f;
        cam.transform.SetPositionAndRotation(shotFrom, Quaternion.LookRotation((mia != null ? mia.transform.position : storeDoor) + Vector3.up - shotFrom));
        StartCoroutine(Letterbox(true));
        if (blink != null) yield return blink.Open(0.35f);
        for (float t = 0f; t < 6f && mia != null; t += Time.deltaTime)
        {
            HideGameView();
            Vector3 to = Flat(storeDoor - mia.transform.position);
            if (to.magnitude < 1.2f) break;
            mia.transform.rotation = Quaternion.Slerp(mia.transform.rotation, Quaternion.LookRotation(to.normalized), Time.deltaTime * 6f);
            mia.transform.position += mia.transform.forward * 1.35f * Time.deltaTime;
            if (Physics.Raycast(mia.transform.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                mia.transform.position = new Vector3(mia.transform.position.x, ground.point.y, mia.transform.position.z);
            Vector3 look = mia.transform.position + Vector3.up * 1.1f;
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - cam.transform.position), 1f - Mathf.Exp(-5f * Time.deltaTime));
            yield return null;
        }
        if (blink != null) yield return blink.Close(0.3f);
        if (mia != null) Destroy(mia);
        Destroy(cam.gameObject);
        StartCoroutine(Letterbox(false));
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        if (blink != null) yield return blink.Open(0.35f);
    }

    static void SetFloat(Animator a, string name, float v) { foreach (var p in a.parameters) if (p.name == name && p.type == AnimatorControllerParameterType.Float) a.SetFloat(name, v); }
    static void SetBool(Animator a, string name, bool v) { foreach (var p in a.parameters) if (p.name == name && p.type == AnimatorControllerParameterType.Bool) a.SetBool(name, v); }

    bool Failed(out string why)
    {
        why = null;
        var manager = driver != null ? driver.GetComponent<OpenworldCrime.Player.PlayerManager>() : null;
        if (manager != null && manager.CurrentState == OpenworldCrime.Player.PlayerState.Dead) why = "You died";
        else if (car == null) why = "The car is gone";
        else
        {
            var health = car.GetComponent<VehicleHealth>();
            if (health != null && health.IsDestroyed) why = "The car was wrecked";
        }
        return why != null;
    }

    IEnumerator Fail(string why)
    {
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
        SetObjective("");
        Banner("MISSION FAILED", why, 4f);
        if (mia != null) Destroy(mia);
        CharacterSwitcher.BlockedReason = null;
        PlayerPrefs.SetInt(DoneKey, 1);   // no second forced intro; it can be replayed from the menu item
        yield return new WaitForSeconds(4.5f);
        Cleanup();
    }

    void Cleanup()
    {
        EndSlowMotion();
        if (miaGraph.IsValid()) miaGraph.Destroy();
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        CharacterSwitcher.BlockedReason = null;
        if (hud != null) Destroy(hud.gameObject);
    }

    void OnDestroy()
    {
        if (phase == Phase.Cutscene) EndSlowMotion();
        if (miaGraph.IsValid()) miaGraph.Destroy();
        if (phase != Phase.None && phase != Phase.Done) CharacterSwitcher.BlockedReason = null;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---------- HUD ----------

    void SetObjective(string text)
    {
        if (objective == null || objective.text == text) return;
        objective.text = text;
        objectivePanel.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    void Banner(string title, string sub, float seconds)
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        banner.text = title;
        bannerSub.text = sub;
        bannerRoutine = StartCoroutine(BannerRoutine(seconds));
    }

    Coroutine bannerRoutine;

    IEnumerator BannerRoutine(float seconds)
    {
        banner.gameObject.SetActive(true);
        bannerSub.gameObject.SetActive(true);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float a = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((seconds - t) / 0.5f);
            banner.color = new Color(1f, 0.82f, 0.25f, a);
            bannerSub.color = new Color(0.45f, 1f, 0.55f, a);
            yield return null;
        }
        banner.gameObject.SetActive(false);
        bannerSub.gameObject.SetActive(false);
    }

    void BuildHud()
    {
        font = GameButtonStyle.Font != null ? GameButtonStyle.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("Intro Mission HUD");
        go.transform.SetParent(transform, false);
        hud = go.AddComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 140;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        barTop = Bar("Letterbox Top", new Vector2(0f, 1f));
        barBottom = Bar("Letterbox Bottom", new Vector2(0f, 0f));

        subtitle = NewText("Subtitle", go.transform, 34, new Vector2(0.5f, 0f), new Vector2(0f, 175f), new Vector2(1400f, 60f), Color.white);

        var panel = new GameObject("Objective", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(go.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -150f);
        panel.sizeDelta = new Vector2(760f, 74f);
        objectivePanel = panel.gameObject.AddComponent<Image>();
        objectivePanel.color = new Color(0.05f, 0.03f, 0.08f, 0.8f);
        GameButtonStyle.Panel(objectivePanel);
        objectivePanel.raycastTarget = false;
        objective = NewText("Text", panel, 32, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 70f), Color.white);
        panel.gameObject.SetActive(false);

        banner = NewText("Banner", go.transform, 96, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1600f, 130f), new Color(1f, 0.82f, 0.25f, 1f));
        bannerSub = NewText("Banner Sub", go.transform, 54, new Vector2(0.5f, 0.5f), new Vector2(0f, -5f), new Vector2(1200f, 80f), new Color(0.45f, 1f, 0.55f, 1f));
        banner.gameObject.SetActive(false);
        bannerSub.gameObject.SetActive(false);

        // SKIP (bottom right, over the letterbox).
        var skipRect = new GameObject("Skip", typeof(RectTransform)).GetComponent<RectTransform>();
        skipRect.SetParent(go.transform, false);
        skipRect.anchorMin = skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-40f, 30f);
        skipRect.sizeDelta = new Vector2(230f, 70f);
        var skipImage = skipRect.gameObject.AddComponent<Image>();
        skipImage.color = new Color(0.2f, 0.2f, 0.2f, 0.85f);
        GameButtonStyle.Button(skipImage);
        var skipBtn = skipRect.gameObject.AddComponent<Button>();
        skipBtn.onClick.AddListener(() => skip = true);
        NewText("Label", skipRect, 32, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 66f), Color.white).text = "SKIP  >>";
        skipButton = skipRect.gameObject;
    }

    RectTransform Bar(string name, Vector2 anchor)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(hud.transform, false);
        rect.anchorMin = new Vector2(0f, anchor.y);
        rect.anchorMax = new Vector2(1f, anchor.y);
        rect.pivot = new Vector2(0.5f, anchor.y);
        rect.sizeDelta = new Vector2(0f, 0f);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return rect;
    }

    Text NewText(string name, Transform parent, int size, Vector2 anchor, Vector2 position, Vector2 box, Color colour)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = box;
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = colour;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }
}
