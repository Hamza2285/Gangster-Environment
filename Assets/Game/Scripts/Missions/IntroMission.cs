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

// The opening mission. The game starts on a cutscene: Jason drives a car with Mia beside him, police cars right behind,
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
    [Tooltip("The chase route: the long straight avenue beside the canal (flat, no bridge). Fixed, so the cutscene is the same " +
             "every time; the player takes over further along it.")]
    public Vector3 routeFrom = new Vector3(95f, 28f, 330f);
    public Vector3 routeFromDirection = Vector3.back;
    public Vector3 routeTo = new Vector3(93f, 28f, -410f);
    public Vector3 routeToDirection = Vector3.back;
    [Tooltip("Fallback when the route cannot be built: the chase starts on the road nearest this point.")]
    public Vector3 chaseStart = new Vector3(389f, 28f, 327f);
    public int wantedStars = 3;
    public int policeCars = 3;
    [Tooltip("Car speed through the cutscene (m/s).")]
    public float cutsceneSpeed = 24f;
    [Tooltip("Paint variant of the car (index in its VehiclePaintVariants; 12 = race red on the Challenger). -1: leave it random.")]
    public int carPaint = 12;

    [Header("Mia in the passenger seat")]
    [Tooltip("Added to the mirrored driver seat (car space).")]
    public Vector3 passengerOffset = new Vector3(0f, 0f, 0f);

    static readonly string[] Subtitles =
    {
        "MIA:  Jason... they're right on us!",
        "JASON:  Hold on!",
    };

    enum Phase { None, Cutscene, LoseCops, Drive, Done }
    Phase phase;
    const float TakeoverGrace = 4f;   // seconds after the player gets the wheel: the car is invulnerable and he can't drop out
    float takeoverTime = -100f;

    PlayerVehicleInteraction driver;
    IVehicleController vehicle;
    GameObject car;
    Rigidbody carBody;
    GameObject mia;
    PlayableGraph miaGraph;
    Vector3 dropPoint, storeDoor;
    readonly List<Vector3> path = new List<Vector3>();   // the road behind the start (where the cruisers come from) + the route
    float heroDistance;                                   // how far along the path the car is
    Light cabinLight;

    // Cruisers held in formation behind the car through the cutscene (they always stay in shot), then handed to their AI.
    class ChaseCop
    {
        public PoliceCarUnit unit;
        public Rigidbody body;
        public float gap, side, phase, distance;
        public bool scripted = true;
    }
    readonly List<ChaseCop> cops = new List<ChaseCop>();

    // Wheels in the cutscene: at chase speed the real spin (10 turns a second, or 500 for a cruiser's spinning rear wheels)
    // strobes on screen - a frozen or backward-turning rim. Each wheel model gets a spin the eye reads as fast forward rolling.
    class SpinWheel
    {
        public Transform model;
        public WheelCollider collider;
        public Rigidbody body;
        public float angle;
    }
    readonly List<SpinWheel> spinWheels = new List<SpinWheel>();
    const float MaxSpinPerFrame = 24f;   // degrees: under a third of a spoke gap, so the rim always reads as turning forward

    void CollectWheels()
    {
        spinWheels.Clear();
        var cars = new List<Rigidbody> { carBody };
        foreach (var cop in cops) if (cop.body != null) cars.Add(cop.body);
        foreach (Rigidbody body in cars)
        {
            foreach (var w in body.GetComponentsInChildren<RCC_WheelCollider>())
                if (w.wheelModel != null && w.wheelCollider != null)
                    spinWheels.Add(new SpinWheel { model = w.wheelModel, collider = w.wheelCollider, body = body });
            foreach (var w in body.GetComponentsInChildren<TSSimpleCar_Wheel>())
                if (w.CorrespondingCollider != null)
                    spinWheels.Add(new SpinWheel { model = w.transform, collider = w.CorrespondingCollider, body = body });
        }
    }

    // After the wheel scripts (Update): the spin angle of each model is replaced, steering, camber and suspension kept.
    void LateUpdate()
    {
        SeatMiaLegs();
        if (phase != Phase.Cutscene || spinWheels.Count == 0) return;
        float dt = Time.deltaTime;
        foreach (var w in spinWheels)
        {
            if (w.model == null || w.collider == null || w.body == null) continue;
            Transform axle = w.collider.transform;
            float radius = Mathf.Max(0.1f, w.collider.radius * axle.lossyScale.y);
            float speed = Vector3.Dot(w.body.linearVelocity, w.body.transform.forward);   // road speed, not a slipping wheel
            float step = speed / (2f * Mathf.PI * radius) * 360f * dt;
            w.angle = Mathf.Repeat(w.angle + Mathf.Clamp(step, -MaxSpinPerFrame, MaxSpinPerFrame), 360f);

            Quaternion basis = axle.rotation * Quaternion.Euler(0f, w.collider.steerAngle, 0f);
            Quaternion local = Quaternion.Inverse(basis) * w.model.rotation;
            Vector3 f = local * Vector3.forward;
            float current = Mathf.Atan2(-f.y, f.z) * Mathf.Rad2Deg;   // the spin the wheel script gave it
            w.model.rotation = basis * Quaternion.Euler(w.angle, 0f, 0f) * Quaternion.Inverse(Quaternion.Euler(current, 0f, 0f)) * local;
        }
    }
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

    static IntroMission active;
    bool restarting;                   // from a failure until the player is back behind the wheel
    string failure;                    // set from outside (an arrest)

    /// <summary>The mission is on with the player in charge: dying or being arrested restarts it at the chase. No hospital, no
    /// bill, no fine (HospitalRespawn and BustedSequence ask).</summary>
    public static bool HandlesDeath => active != null && (active.restarting || active.phase == Phase.LoseCops || active.phase == Phase.Drive);
    public static bool Restarting => active != null && active.restarting;

    /// <summary>An arrest during the mission: true when the mission deals with it (it restarts).</summary>
    public static bool TakeArrest()
    {
        if (!HandlesDeath) return false;
        active.failure = "You were busted";
        return true;
    }

    void Start()
    {
        active = this;
        if (!playEveryTime && PlayerPrefs.GetInt(DoneKey, 0) == 1) return;
        if (carPrefab == null) return;
        StartCoroutine(Run());
    }

    IEnumerator Run(bool cutscene = true)
    {
        // Hidden while the city wakes up.
        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0f);
        HideGameView();
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { HideGameView(); yield return null; }   // HUD built in Start too
        // The loading screen stays up until the city runs smoothly: the chase waits for it, or it would play behind it.
        while (OpenworldCrime.UI.Menu.LoadingScreen.CoversGame) { HideGameView(); yield return null; }

        var player = GamePlayer.Current;
        if (player == null || PlayableCharacter.CurrentId != "jason") { RestoreGameView(); if (blink != null) yield return blink.Open(0.4f); yield break; }
        driver = player.transform.GetComponent<PlayerVehicleInteraction>();
        if (driver == null || driver.State != VehicleInteractionState.OnFoot || !FindDropOff()) { RestoreGameView(); if (blink != null) yield return blink.Open(0.4f); yield break; }

        BuildHud();
        if (!SetUpChase(chaseStart)) { Cleanup(); if (blink != null) yield return blink.Open(0.4f); yield break; }
        CharacterSwitcher.BlockedReason = "Mia is riding with you";

        // 1. Cutscene (not again after a death or an arrest: straight back into the chase).
        failure = null;
        phase = Phase.Cutscene;
        GamePlayer.MenuOpen = true;
        HideGameView();
        var carHealth = car.GetComponent<VehicleHealth>();
        if (carHealth != null) carHealth.Invulnerable = true;
        if (cutscene)
        {
            driver.SetRealDriverVisible(true);       // the close-ups show Jason himself, not the car's driver dummy
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) { AlignMiaWithJason(); yield return null; }
            var cam = MakeCamera();
            AddCabinLight();
            CollectWheels();
            StartCoroutine(AutoDrive());
            StartCoroutine(KeepRouteClear());
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f);   // short cutscene: the bars are there from the first frame
            if (blink != null) StartCoroutine(blink.Open(0.2f));
            yield return Shots(cam);
            if (blink != null) yield return blink.Close(0.25f);
            RemoveCabinLight();
            driver.SetRealDriverVisible(false);
            Destroy(cam.gameObject);
            StartCoroutine(Letterbox(false));
        }
        else
        {
            // Behind the closed screen: the chase gets up to speed and Mia into her seat.
            StartCoroutine(AutoDrive());
            StartCoroutine(KeepRouteClear());
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { AlignMiaWithJason(); HideGameView(); yield return null; }
        }
        foreach (var cop in cops) cop.scripted = false;   // their own AI takes the chase from here

        // The player takes over at speed.
        phase = Phase.LoseCops;
        restarting = false;
        subtitle.text = "";
        skipButton.SetActive(false);
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        if (blink != null) yield return blink.Open(0.35f);
        SetObjective("Lose the cops");
        takeoverTime = Time.time;
        StartCoroutine(EndInvulnerability(carHealth, TakeoverGrace));
        StartCoroutine(EndProtection(TakeoverGrace));

        // 2. Lose the cops, then the drive.
        var wanted = WantedManager.Instance;
        while (wanted != null && wanted.Stars > 0)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            yield return null;
        }
        // (Dying clears the wanted level too: that is not losing the cops.)
        if (Failed(out string early)) { yield return Fail(early); yield break; }
        phase = Phase.Drive;
        Banner("COPS LOST", "", 2.2f);
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.SetMarker(dropPoint);

        while (true)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            float distance = Flat(car.transform.position - dropPoint).magnitude;
            // Complete only after the chase is over: if the cops are back on him, they have to be lost again first.
            if (wanted != null && wanted.Stars > 0) SetObjective("Lose the cops");
            else if (distance > StopDistance) SetObjective("Drop Mia at " + dropOffName);
            else
            {
                SetObjective("Stop the car");
                if (carBody.linearVelocity.magnitude < 1.5f) break;
            }
            if (SteppedOutAtStore()) break;
            if (WaypointSystem.Instance != null && !WaypointSystem.Instance.HasMarker) WaypointSystem.Instance.SetMarker(dropPoint);
            yield return null;
        }

        // 3. Drop-off.
        // From here the mission is won: saved straight away, so nothing that happens during the drop-off shot (the app closed,
        // the car hit, the scene reloaded) can bring the mission back.
        phase = Phase.Done;
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
        SetObjective("");
        if (carHealth != null) carHealth.Invulnerable = true;    // the shot needs the car whole
        yield return DropOff();
        if (carHealth != null) carHealth.Invulnerable = false;
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
        if (network == null) return false;
        var links = new List<RoadNetwork.Link>();
        var route = new List<Vector3>();
        RoadNetwork.RoadPoint start;
        bool fixedRoute = network.TryFindLaneAhead(routeFrom, routeFromDirection, 0f, out start)
                          && network.TryFindLaneAhead(routeTo, routeToDirection, 0f, out RoadNetwork.RoadPoint end)
                          && network.TryFindRoute(start, end, route, links, 4f) && route.Count > 20;
        if (!fixedRoute)
        {
            if (!network.TryFindNearest(around, out start)) return false;
            route.Clear();
            route.AddRange(network.BuildDepartPath(start, 760f, new List<RoadNetwork.Link>()));
            if (route.Count < 4) return false;
        }

        // The road behind the start, for the cruisers.
        var behind = network.BuildApproachPath(start, 90f, new List<RoadNetwork.Link>(), out _);
        path.Clear();
        path.AddRange(behind);
        if (path.Count > 0 && route.Count > 0 && (path[path.Count - 1] - route[0]).sqrMagnitude < 1f) path.RemoveAt(path.Count - 1);
        heroDistance = 0f;
        for (int i = 1; i < path.Count; i++) heroDistance += Vector3.Distance(path[i - 1], path[i]);
        path.AddRange(route);
        if (path.Count < 4) return false;

        ClearTraffic(0f, heroDistance + 700f, 16f);

        Vector3 forward = Flat(Sample(heroDistance + 6f) - Sample(heroDistance)).normalized;
        car = Instantiate(carPrefab, Sample(heroDistance) + Vector3.up * 0.4f, Quaternion.LookRotation(forward));
        car.name = "Intro Car";
        carBody = car.GetComponent<Rigidbody>();
        vehicle = car.GetComponent<IVehicleController>();
        if (vehicle == null || carBody == null || !driver.EnterInstant(vehicle)) { Destroy(car); return false; }
        carBody.linearVelocity = forward * cutsceneSpeed;
        var paint = car.GetComponent<VehiclePaintVariants>();
        if (paint != null && carPaint >= 0 && carPaint < paint.Count) paint.ApplyVariant(carPaint);
        travel = forward;
        ProtectCar();

        mia = MakeMiaPuppet();

        var wanted = WantedManager.Instance;
        if (wanted != null) wanted.SetWanted(wantedStars, false);
        var dispatcher = FindFirstObjectByType<PoliceDispatcher>();
        cops.Clear();
        if (dispatcher != null)
        {
            float[] gaps = { 12f, 15f, 23f, 27f };
            for (int i = 0; i < Mathf.Min(policeCars, gaps.Length); i++)
            {
                float d = Mathf.Max(0f, heroDistance - gaps[i]);
                Vector3 heading = Flat(Sample(d + 5f) - Sample(d)).normalized;
                if (heading.sqrMagnitude < 0.5f) heading = forward;
                float side = i % 2 == 0 ? 1.7f : -1.7f;
                Vector3 at = Sample(d) + Vector3.Cross(Vector3.up, heading) * side;
                PoliceCarUnit unit = dispatcher.SpawnChaseCarUnit(at + Vector3.up * 0.3f, Quaternion.LookRotation(heading), heading * cutsceneSpeed);
                if (unit == null) continue;
                cops.Add(new ChaseCop { unit = unit, body = unit.GetComponent<Rigidbody>(), gap = gaps[i], side = side, phase = i * 1.7f, distance = d });
                IgnoreCop(unit.gameObject, true);
            }
        }
        return true;
    }

    // Traffic cars and bikes on the stretch of route between the two distances go back to the iTS pool (they respawn elsewhere).
    void ClearTraffic(float fromDistance, float toDistance, float radius)
    {
        var samples = new List<Vector3>();
        for (float d = Mathf.Max(0f, fromDistance); d <= toDistance; d += 8f) samples.Add(Flat(Sample(d)));
        float sqr = radius * radius;
        foreach (var ai in FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None))
        {
            if (ai == null || !ai.gameObject.activeInHierarchy) continue;
            Vector3 at = Flat(ai.transform.position);
            foreach (Vector3 sample in samples)
            {
                if ((sample - at).sqrMagnitude > sqr) continue;
                ai.Disable(true, true);
                break;
            }
        }
    }

    // Through the cutscene, the road ahead of the chase (out of shot) and far behind it is kept empty.
    IEnumerator KeepRouteClear()
    {
        while (phase == Phase.Cutscene && car != null)
        {
            ClearTraffic(heroDistance + 45f, heroDistance + 420f, 14f);
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    // So both of them read behind the glass: a soft fill that lights only the two characters (they go on a layer of their own for
    // the cutscene, so it cannot flare on the trim), and clearer windows. RCC's exhaust flame carries an old-style lens flare
    // drawn over everything; off on the intro car for the close-ups.
    const int ActorLayer = 30;
    readonly Dictionary<GameObject, int> actorLayers = new Dictionary<GameObject, int>();
    readonly List<Renderer> hiddenShoes = new List<Renderer>();
    readonly List<KeyValuePair<Renderer, int>> clearedGlass = new List<KeyValuePair<Renderer, int>>();

    void AddCabinLight()
    {
        foreach (var flare in car.GetComponentsInChildren<LensFlare>(true)) flare.enabled = false;

        foreach (var root in new[] { driver != null ? driver.gameObject : null, mia })
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (actorLayers.ContainsKey(r.gameObject)) continue;
                // His legs and shoes reach through the floor of a low car (nothing below the windows is in shot otherwise): not drawn.
                if (root != mia && r.enabled && (r.name.Contains("Sneaker") || r.name.Contains("Shoe") || r.name.Contains("Bottoms"))) { r.enabled = false; hiddenShoes.Add(r); }
                actorLayers.Add(r.gameObject, r.gameObject.layer);
                r.gameObject.layer = ActorLayer;
            }
        }
        var go = new GameObject("Intro Cabin Light");
        go.transform.SetParent(car.transform, false);
        go.transform.position = Heads + car.transform.forward * 0.95f + Vector3.up * 0.2f;
        cabinLight = go.AddComponent<Light>();
        cabinLight.type = LightType.Point;
        cabinLight.range = 3f;
        cabinLight.intensity = 1.6f;
        cabinLight.color = new Color(1f, 0.95f, 0.88f);
        cabinLight.shadows = LightShadows.None;
        cabinLight.cullingMask = 1 << ActorLayer;

        var block = new MaterialPropertyBlock();
        foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
        {
            var materials = r.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null || !materials[m].name.Contains("Glass") || !materials[m].HasProperty("_BaseColor")) continue;
                Color c = materials[m].GetColor("_BaseColor");
                c.a *= 0.22f;
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block, m);
                clearedGlass.Add(new KeyValuePair<Renderer, int>(r, m));
            }
        }
    }

    void RemoveCabinLight()
    {
        if (cabinLight != null) Destroy(cabinLight.gameObject);
        foreach (var pair in actorLayers) if (pair.Key != null) pair.Key.layer = pair.Value;
        actorLayers.Clear();
        foreach (var r in hiddenShoes) if (r != null) r.enabled = true;
        hiddenShoes.Clear();
        foreach (var pair in clearedGlass) if (pair.Key != null) pair.Key.SetPropertyBlock(null, pair.Value);
        clearedGlass.Clear();
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

    // Her legs in the footwell: her own clip lets them hang straight down, through the floor of a low car. Thighs forward, knees
    // bent, set over the animation every frame while she rides.
    void SeatMiaLegs()
    {
        if (mia == null || car == null || !mia.transform.IsChildOf(car.transform)) return;
        var animator = mia.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return;
        for (int side = 0; side < 2; side++)
        {
            Transform thigh = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform shin = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Transform foot = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (thigh == null || shin == null || foot == null) continue;
            float spread = side == 0 ? -0.08f : 0.08f;
            Point(thigh, shin, car.transform.TransformDirection(new Vector3(spread, 0.1f, 1f)));
            Point(shin, foot, car.transform.TransformDirection(new Vector3(0f, -0.62f, 0.78f)));
        }
    }

    static void Point(Transform bone, Transform next, Vector3 direction) =>
        bone.rotation = Quaternion.FromToRotation(next.position - bone.position, direction) * bone.rotation;

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

    // ---------- The drive ----------

    Vector3 travel = Vector3.forward;             // smoothed direction of travel (the cameras hang on this)

    // Not a scratch on the car: no crash deformation and no contact with the cruisers until the player has settled in.
    RCC_CarControllerV3 carRcc;
    bool carDamage;
    readonly List<RCC_Exhaust> quietExhausts = new List<RCC_Exhaust>();

    void ProtectCar()
    {
        carRcc = car.GetComponent<RCC_CarControllerV3>();
        if (carRcc != null) { carDamage = carRcc.useDamage; carRcc.useDamage = false; }
        // No backfire flames flashing through the shots.
        quietExhausts.Clear();
        foreach (var exhaust in car.GetComponentsInChildren<RCC_Exhaust>())
        {
            if (!exhaust.enabled) continue;
            exhaust.enabled = false;
            foreach (var particles in exhaust.GetComponentsInChildren<ParticleSystem>()) particles.Clear();
            foreach (var light in exhaust.GetComponentsInChildren<Light>()) light.intensity = 0f;
            quietExhausts.Add(exhaust);
        }
    }

    void IgnoreCop(GameObject cop, bool ignore)
    {
        if (cop == null || car == null) return;
        var mine = car.GetComponentsInChildren<Collider>();
        foreach (var theirs in cop.GetComponentsInChildren<Collider>())
        {
            foreach (var c in mine) if (c != null && theirs != null) Physics.IgnoreCollision(c, theirs, ignore);
            foreach (var other in cops)
                if (other.unit != null && other.unit.gameObject != cop)
                    foreach (var c in other.unit.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, theirs, ignore);
        }
    }

    // The player has had the wheel a moment: everything is solid again (a cruiser on top of the car right now waits).
    IEnumerator EndProtection(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        foreach (var exhaust in quietExhausts) if (exhaust != null) exhaust.enabled = true;
        quietExhausts.Clear();
        if (carRcc != null) carRcc.useDamage = carDamage;
        foreach (var cop in cops.ToArray())
        {
            while (cop.unit != null && car != null && (cop.unit.transform.position - car.transform.position).sqrMagnitude < 30f) yield return null;
            if (cop.unit != null) IgnoreCop(cop.unit.gameObject, false);
        }
    }

    // The car runs straight down the road at cutscene speed (physics velocity, so the wheels roll) until the player takes over;
    // the cruisers hold two abreast behind it, drifting gently in their lanes.
    IEnumerator AutoDrive()
    {
        Vector3 last = Flat(car.transform.position);
        var lastCop = new Dictionary<ChaseCop, Vector3>();
        var wait = new WaitForFixedUpdate();
        while (phase == Phase.Cutscene && car != null)
        {
            Vector3 now = Flat(car.transform.position);
            heroDistance += Vector3.Distance(now, last);
            last = now;
            Vector3 roadDir = Flat(Sample(heroDistance + 5f) - Sample(heroDistance)).normalized;
            if (roadDir.sqrMagnitude > 0.5f) travel = Vector3.Slerp(travel, roadDir, 1f - Mathf.Exp(-4f * Time.fixedDeltaTime)).normalized;
            Steer(carBody, heroDistance, 0f, cutsceneSpeed);

            float time = Time.time;
            foreach (var cop in cops)
            {
                if (!cop.scripted || cop.unit == null || cop.body == null || !cop.unit.isActiveAndEnabled) continue;
                Vector3 copNow = Flat(cop.body.position);
                if (lastCop.TryGetValue(cop, out Vector3 copLast)) cop.distance += Vector3.Distance(copNow, copLast);
                lastCop[cop] = copNow;
                float wanted = heroDistance - (cop.gap + 1.2f * Mathf.Sin(time * 0.7f + cop.phase));
                float speed = cutsceneSpeed + Mathf.Clamp((wanted - cop.distance) * 0.9f, -7f, 8f);
                Steer(cop.body, cop.distance, cop.side * (1f + 0.15f * Mathf.Sin(time * 0.8f + cop.phase)), speed);
            }
            yield return wait;
        }
    }

    // Physics velocity toward a point ahead on the road (the wheels roll), yaw along the road, kept level: a knock must not roll it.
    void Steer(Rigidbody body, float distance, float lateral, float speed)
    {
        Vector3 heading = Flat(Sample(distance + 12f) - Sample(distance + 6f)).normalized;
        if (heading.sqrMagnitude < 0.5f) heading = Flat(body.rotation * Vector3.forward).normalized;
        Vector3 aim = Flat(Sample(distance + 9f) + Vector3.Cross(Vector3.up, heading) * lateral - body.position);
        if (aim.sqrMagnitude > 0.01f)
        {
            Vector3 v = aim.normalized * speed;
            v.y = body.linearVelocity.y;
            body.linearVelocity = v;
        }
        Vector3 road = Flat(Sample(distance + 5f) - Sample(distance)).normalized;
        if (road.sqrMagnitude > 0.5f)
        {
            Vector3 now = Flat(body.rotation * Vector3.forward);
            Vector3 yaw = Vector3.Slerp(now.sqrMagnitude > 0.01f ? now.normalized : road, road, Time.fixedDeltaTime * 5f);
            body.MoveRotation(Quaternion.LookRotation(yaw, Vector3.up));
        }
        body.angularVelocity = Vector3.zero;
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
    // a length in real seconds, handheld shake, and an optional subtitle.
    // Every shot is in front of or beside the cabin, so Jason and Mia are both in it.

    class Shot
    {
        public float seconds = 2.4f, fovFrom = 45f, fovTo = 45f, shake = 0.5f;
        public int subtitle = -1;
        public System.Func<float, Vector3> position;   // k (0..1, eased) -> world position
        public System.Func<float, Vector3> look;       // k -> world look-at point
        public System.Action onStart;
    }

    // A point around the car in the direction of travel (smoother than the car's own axes).
    Vector3 Rig(float x, float y, float z) =>
        car.transform.position + Vector3.Cross(Vector3.up, travel) * x + Vector3.up * y + travel * z;

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

    // A camera outside the car never sits inside a wall, a railing or a parked car: pulled in toward what it looks at.
    readonly RaycastHit[] hits = new RaycastHit[16];
    Vector3 Clear(Vector3 look, Vector3 wanted)
    {
        Vector3 to = wanted - look;
        float length = to.magnitude;
        if (length < 0.01f) return wanted;
        int n = Physics.SphereCastNonAlloc(look, 0.25f, to / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        float best = length;
        for (int i = 0; i < n; i++)
        {
            Rigidbody owner = hits[i].rigidbody;
            if (owner != null && (owner == carBody || IsCop(owner))) continue;
            if (hits[i].collider.transform.IsChildOf(car.transform)) continue;
            if (hits[i].distance > 0f && hits[i].distance < best) best = hits[i].distance;
        }
        return best < length ? look + to / length * Mathf.Max(1.2f, best - 0.3f) : wanted;
    }

    bool IsCop(Rigidbody body)
    {
        foreach (var cop in cops) if (cop.body == body) return true;
        return false;
    }

    // Three quick shots, 4.8 seconds: from high above, down at the tyres, and the two of them from the side.
    List<Shot> BuildShots()
    {
        return new List<Shot>
        {
            // 1. From the top: high over the road, the whole chase below.
            new Shot { seconds = 1.6f, fovFrom = 42f, fovTo = 40f, shake = 0.06f,
                position = k => Rig(Mathf.Lerp(5f, 4.2f, k), Mathf.Lerp(13f, 11.5f, k), Mathf.Lerp(13f, 12f, k)),
                look = k => Rig(0.4f, 0.3f, -3f) },
            // 2. At the tyres: just off the road beside the wheels, the cruisers coming up behind.
            new Shot { seconds = 1.5f, fovFrom = 40f, fovTo = 38f, shake = 0.3f, subtitle = 0,
                position = k => Clear(Rig(0f, 0.6f, 0f), Rig(-Mathf.Lerp(3.3f, 3.1f, k), 0.38f, Mathf.Lerp(5.6f, 5.2f, k))),
                look = k => Rig(-0.2f, 0.6f, -0.6f) },
            // 3. From the side, through the door window: Jason, and Mia beyond.
            new Shot { seconds = 1.7f, fovFrom = 34f, fovTo = 31f, shake = 0.12f, subtitle = 1,
                position = k => Clear(Heads, Rig(-6.6f, 1.5f, Mathf.Lerp(2.3f, 1.9f, k))),
                look = k => Vector3.Lerp(Heads, Rig(0f, 0.8f, 0f), Mathf.Lerp(0.5f, 0.25f, k)) },
        };
    }

    IEnumerator Shots(Camera cam)
    {
        var shots = BuildShots();
        float seed = Random.value * 100f;
        foreach (var shot in shots)
        {
            if (skip) break;
            shot.onStart?.Invoke();
            subtitle.text = shot.subtitle >= 0 && shot.subtitle < Subtitles.Length ? Subtitles[shot.subtitle] : "";
            for (float t = 0f; t < shot.seconds && !skip; t += Time.unscaledDeltaTime)
            {
                HideGameView();   // the car's touch pedals and dashboard switch on a moment after he is seated
                if (GameInput.CancelDown || GameInput.EnterDown) skip = true;
                float k = Mathf.SmoothStep(0f, 1f, t / shot.seconds);
                Vector3 position = shot.position(k);
                Vector3 look = shot.look(k);
                // Handheld: a slow drift.
                float time = Time.unscaledTime + seed;
                float shake = shot.shake;
                Vector3 drift = new Vector3(Mathf.PerlinNoise(time * 1.3f, 0f) - 0.5f, Mathf.PerlinNoise(0f, time * 1.1f) - 0.5f, 0f) * 0.08f * shake;
                cam.transform.position = position;
                cam.transform.rotation = Quaternion.LookRotation(look - position);
                cam.transform.position += cam.transform.TransformVector(drift);
                cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(time * 2.3f, 3f) - 0.5f) * 1.6f * shake,
                    (Mathf.PerlinNoise(5f, time * 2.1f) - 0.5f) * 1.6f * shake, (Mathf.PerlinNoise(time * 0.7f, 9f) - 0.5f) * 1.2f * shake);
                cam.fieldOfView = Mathf.Lerp(shot.fovFrom, shot.fovTo, k);
                yield return null;
            }
        }
        if (shots.Count > 0 && !skip)   // where the last shot has got to, so the hand-over to the car does not jump
        {
            Vector3 end = shots[shots.Count - 1].position(1f);
            cam.transform.SetPositionAndRotation(end, Quaternion.LookRotation(shots[shots.Count - 1].look(1f) - end));
        }
        if (car != null) cam.transform.SetParent(car.transform, true);   // rides along while the screen blinks shut
    }

    IEnumerator EndInvulnerability(VehicleHealth health, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (health != null) health.Invulnerable = false;
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
        cam.cullingMask |= 1 << ActorLayer;
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
        Vector3 door = FootpathSideExit();
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

    // Mia gets out on the footpath side: the side of the car the store is on (the kerb), not into the traffic lane. The spot is
    // stepped out until it is clear of the car and of street furniture, so she never appears inside a pole or the bodywork.
    Vector3 FootpathSideExit()
    {
        Vector3 side = car.transform.right;
        if (Vector3.Dot(Flat(storeDoor - car.transform.position), side) < 0f) side = -side;
        Vector3 best = car.transform.position + side * 2.3f;
        for (float out_ = 2.0f; out_ <= 4.0f; out_ += 0.4f)
        {
            Vector3 at = car.transform.position + side * out_;
            if (!Physics.Raycast(at + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
            bool blocked = Physics.CheckCapsule(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.5f, 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            best = hit.point;
            if (!blocked) break;
        }
        return best;
    }

    const float StopDistance = 18f;       // "Stop the car" this close to the drop-off
    const float StepOutDistance = 35f;    // getting out this close to it, cops lost, is dropping Mia off too

    // Jason parked by the store and got out himself instead of waiting for the stop: that is the drop-off, not leaving her.
    bool SteppedOutAtStore()
    {
        if (phase != Phase.Drive || car == null || driver == null) return false;
        if (driver.State == VehicleInteractionState.InVehicle && driver.CurrentVehicle == vehicle) return false;
        var wanted = WantedManager.Instance;
        if (wanted != null && wanted.Stars > 0) return false;
        return Mathf.Min(Flat(car.transform.position - dropPoint).magnitude, Flat(car.transform.position - storeDoor).magnitude) <= StepOutDistance;
    }

    bool Failed(out string why)
    {
        why = failure;
        if (why != null) return true;
        var manager = driver != null ? driver.GetComponent<OpenworldCrime.Player.PlayerManager>() : null;
        if (manager != null && manager.CurrentState == OpenworldCrime.Player.PlayerState.Dead) why = "You died";
        else if (car == null) why = "The car is gone";
        else
        {
            var health = car.GetComponent<VehicleHealth>();
            if (health != null && health.IsDestroyed) why = "The car was wrecked";
            // Getting out (or being thrown out) of the car with Mia in it ends the mission.
            // (At the store with the cops gone it is the drop-off instead: see SteppedOutAtStore.)
            else if ((phase == Phase.LoseCops || phase == Phase.Drive) && driver != null && !SteppedOutAtStore()
                     && (driver.State != VehicleInteractionState.InVehicle || driver.CurrentVehicle != vehicle))
            {
                // Right after the takeover (the car is still invulnerable then) a hit from a cruiser could throw him out
                // before he has even touched the controls: he is put straight back behind the wheel instead.
                if (Time.time - takeoverTime < TakeoverGrace)
                {
                    if (driver.State == VehicleInteractionState.OnFoot) driver.EnterInstant(vehicle);
                    return false;
                }
                why = LeftMia;
            }
        }
        return why != null;
    }

    // Failed: the banner, then everything is put back and the mission starts again from the cutscene.
    IEnumerator Fail(string why)
    {
        // Killed, wrecked or arrested: straight back into the chase. Walking out on Mia: the whole mission again, cutscene too.
        bool direct = why != LeftMia;
        restarting = direct;
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
        SetObjective("");
        Banner("MISSION FAILED", why, direct ? 1.2f : 3.5f);
        CharacterSwitcher.BlockedReason = null;
        yield return new WaitForSecondsRealtime(direct ? 1.3f : 3.5f);

        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0.4f);
        TearDown();
        failure = null;
        // A dead player respawns first; the restart waits for him to be standing again (on foot).
        var manager = driver != null ? driver.GetComponent<OpenworldCrime.Player.PlayerManager>() : null;
        for (float t = 0f; t < 20f; t += Time.unscaledDeltaTime)
        {
            bool dead = manager != null && manager.CurrentState == OpenworldCrime.Player.PlayerState.Dead;
            if (!dead && driver != null && driver.State == VehicleInteractionState.OnFoot) break;
            yield return null;
        }
        var health = driver != null ? driver.GetComponent<Invector.vCharacterController.vThirdPersonController>() : null;
        if (direct && health != null && !health.isDead) health.ResetHealth();
        StartCoroutine(Run(!direct));
    }

    const string LeftMia = "You left Mia in the car";

    // Removes the mission's car, Mia, the chasing cruisers and the wanted level, and gets Jason out of the car if he is in it.
    // (No StopAllCoroutines: the restart itself runs as one. The chase helpers end on their own once the phase is reset.)
    void TearDown()
    {
        if (miaGraph.IsValid()) miaGraph.Destroy();
        if (mia != null) Destroy(mia);
        mia = null;
        if (driver != null && driver.State == VehicleInteractionState.InVehicle && car != null)
            driver.ForceExitAt(car.transform.position + car.transform.right * 2.5f, car.transform.rotation);
        var dispatcher = FindFirstObjectByType<PoliceDispatcher>();
        foreach (var cop in cops)
        {
            if (cop.unit == null) continue;
            if (dispatcher != null) dispatcher.Release(cop.unit);
            else Destroy(cop.unit.gameObject);
        }
        cops.Clear();
        spinWheels.Clear();
        var wanted = WantedManager.Instance;
        if (wanted != null) wanted.ClearWanted();
        if (car != null) Destroy(car, 0.5f);   // a moment for the forced exit to let go of it
        car = null;
        carBody = null;
        vehicle = null;
        skip = false;
        phase = Phase.None;
        Cleanup();
    }

    void Cleanup()
    {
        if (miaGraph.IsValid()) miaGraph.Destroy();
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        CharacterSwitcher.BlockedReason = null;
        RemoveCabinLight();
        if (hud != null) Destroy(hud.gameObject);
    }

    void OnDestroy()
    {
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
