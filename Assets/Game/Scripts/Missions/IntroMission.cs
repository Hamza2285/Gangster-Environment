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

// The opening mission. The game starts on a cutscene, cut like a film: Jason and Mia each drive a car, side by side down the
// avenue, police cars right behind them (letterbox, subtitles, SKIP). A blink, and the player has Jason's wheel at speed:
// 1. the drive tutorial (accelerator, then steering, then brake, then everything; DriveTutorial), Mia's car level with his;
// 2. Mia peels off at a junction and pulls part of the police after her; Jason loses the rest (wanted level back to zero):
//    MISSION COMPLETE and a cash reward;
// 3. Mia phones him (PhoneUI): answered with Enter (or a tap), the two of them talk in subtitles, the call ends.
// Dying or being arrested restarts it at the chase (no hospital, no cutscene again).
// Plays once per install (PlayerPrefs; Game/Reset Intro Mission plays it again) and only when Jason is the one playing.
public class IntroMission : MonoBehaviour
{
    const string DoneKey = "IntroMissionDone";

    [Header("Setup")]
    public GameObject carPrefab;
    [Tooltip("Mia's car (her own, driven beside Jason's). Empty: the same model as his.")]
    public GameObject miaCarPrefab;
    [Tooltip("Play it every time the game scene starts (testing). Off: once per install.")]
    public bool playEveryTime;
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
    [Tooltip("Wanted level of the chase. 2: police chase and ram but do not shoot (they shoot from 3).")]
    public int wantedStars = 2;
    public int policeCars = 4;
    [Tooltip("Car speed through the cutscene (m/s).")]
    public float cutsceneSpeed = 24f;
    [Tooltip("Paint variant of Jason's car (index in its VehiclePaintVariants; 12 = race red on the Challenger). -1: leave it random.")]
    public int carPaint = 12;
    [Tooltip("Paint variant of Mia's car. -1: leave it random.")]
    public int miaCarPaint = -1;
    [Tooltip("The first drive teaches the controls one at a time (accelerator, steering, brake, then all of them).")]
    public bool driveTutorial = true;

    [Header("Mia")]
    [Tooltip("Seated driver pose (the vehicle exit clip, held on its first frame: the same pose Jason drives in).")]
    public AnimationClip driveClip;
    [Tooltip("Added to her seat position (car space).")]
    public Vector3 passengerOffset = new Vector3(0f, 0f, 0f);

    static readonly string[] Subtitles =
    {
        "MIA:  They're right on us, Jason!",
        "JASON:  Stay with me, Mia!",
        "MIA:  Keep up... I'll pull them off you!",
        "MIA:  Split up! I'll lead them away - lose the rest!",
    };

    // Mia's call once the cops are lost.
    static readonly string[] CallLines =
    {
        "MIA:  Jason! Tell me you got away...",
        "JASON:  Lost every last one of them. You?",
        "MIA:  Ha! They never stood a chance. That was fun!",
        "JASON:  You drive like a maniac, you know that?",
        "MIA:  Says the guy who nearly took out a bus stop.",
        "JASON:  Thanks for pulling them off me back there.",
        "MIA:  Anytime. I'll lay low for a while - we'll catch up later.",
        "JASON:  Later, Mia. Stay safe.",
    };

    enum Phase { None, Cutscene, LoseCops, Done }
    Phase phase;
    const float TakeoverGrace = 4f;   // seconds after the player gets the wheel: the car is invulnerable and he can't drop out
    float takeoverTime = -100f;

    PlayerVehicleInteraction driver;
    IVehicleController vehicle;
    GameObject car;
    Rigidbody carBody;
    RCC_CarControllerV3 carRcc;
    DriveTutorial tutorial;

    // ---------- Roads ----------

    // A road to drive along: points and their distances, sampled by distance, and the distance of the point nearest a position.
    class Track
    {
        public readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> along = new List<float>();
        public float Length => along.Count > 0 ? along[along.Count - 1] : 0f;
        public int Count => points.Count;

        public void Rebuild()
        {
            along.Clear();
            float d = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) d += Vector3.Distance(points[i - 1], points[i]);
                along.Add(d);
            }
        }

        public float DistanceAt(int index) => along[Mathf.Clamp(index, 0, along.Count - 1)];

        int IndexAt(float distance)
        {
            int lo = 1, hi = along.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (along[mid] < distance) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        public Vector3 Sample(float distance)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1 || distance <= 0f) return points[0];
            if (distance >= Length)
            {
                // Past the end: straight on, so nothing driving it ever stops dead on the last point.
                Vector3 last = points[points.Count - 1];
                Vector3 dir = Flat(last - points[Mathf.Max(0, points.Count - 3)]).normalized;
                return last + dir * (distance - Length);
            }
            int i = IndexAt(distance);
            float span = along[i] - along[i - 1];
            return Vector3.Lerp(points[i - 1], points[i], span > 0.001f ? (distance - along[i - 1]) / span : 0f);
        }

        public Vector3 Heading(float distance) => Flat(Sample(distance + 2.5f) - Sample(Mathf.Max(0f, distance - 2.5f))).normalized;

        public float LastSqrDistance { get; private set; }

        // Searched near the hint only: the road can pass the same spot twice (a loop, a bridge over it).
        public float Project(Vector3 position, float hint, float back = 30f, float ahead = 120f)
        {
            if (points.Count < 2) return 0f;
            Vector3 p = Flat(position);
            float best = float.MaxValue, result = hint;
            int from = Mathf.Max(1, IndexAt(hint - back) - 1), to = Mathf.Min(points.Count - 1, IndexAt(hint + ahead) + 1);
            for (int i = from; i <= to; i++)
            {
                Vector3 a = Flat(points[i - 1]), ab = Flat(points[i]) - a;
                float length2 = ab.sqrMagnitude;
                float k = length2 > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / length2) : 0f;
                float d = (a + ab * k - p).sqrMagnitude;
                if (d < best) { best = d; result = along[i - 1] + (along[i] - along[i - 1]) * k; }
            }
            LastSqrDistance = best;
            return result;
        }
    }

    readonly Track road = new Track();       // Jason's: the road behind the start (where the cruisers come from) + the route
    float heroDistance;                      // how far along it his car is
    float heroStart;                         // ... where he started
    float leaderSpeed;                       // his speed along it
    bool heroOnRoad = true;

    // ---------- Mia ----------

    GameObject miaCar;
    Rigidbody miaBody;
    RCC_CarControllerV3 miaRcc;
    IVehicleController miaVehicle;
    GameObject mia;
    PlayableGraph miaGraph;
    readonly Track miaRoad = new Track();    // her own lane beside his, then her turn-off
    class Junction { public float distance; public int lane; public int index; }
    readonly List<Junction> miaJunctions = new List<Junction>();
    float miaDistance, miaSpeed, miaOffset, miaSide, miaLateral, miaLateralWanted, miaSteer, nextAvoid;
    bool miaSplit, miaGone;
    float splitDistance, splitTime;

    // Cruisers held in formation behind the cars through the cutscene and the tutorial (they always stay in shot), then
    // handed to their AI - all but Mia's, which follow her off down her road and are released out of sight.
    class ChaseCop
    {
        public PoliceCarUnit unit;
        public Rigidbody body;
        public float gap, side, phase, distance;
        public bool scripted = true;
        public bool followsMia;      // in formation behind her car instead of his
        public bool onMiaRoad;       // ... and after the split, on her road
    }
    readonly List<ChaseCop> cops = new List<ChaseCop>();

    // Wheels: at chase speed the real spin (10 turns a second, or 500 for a cruiser's spinning rear wheels) strobes on screen - a
    // frozen or backward-turning rim. Each wheel model of a scripted car gets a spin the eye reads as fast forward rolling.
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
        if (miaBody != null) cars.Add(miaBody);
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

    bool Scripted(Rigidbody body)
    {
        if (body == null) return false;
        if (body == carBody) return phase == Phase.Cutscene;
        if (body == miaBody) return !miaGone;
        foreach (var cop in cops) if (cop.body == body) return cop.scripted;
        return false;
    }

    // After the wheel scripts (Update): the spin angle of each model is replaced, steering, camber and suspension kept.
    void LateUpdate()
    {
        if (driveClip == null) SeatMiaLegs();
        if (spinWheels.Count == 0) return;
        float dt = Time.deltaTime;
        foreach (var w in spinWheels)
        {
            if (w.model == null || w.collider == null || w.body == null || !Scripted(w.body)) continue;
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
    public static bool HandlesDeath => active != null && (active.restarting || active.phase == Phase.LoseCops);
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
        if (driver == null || driver.State != VehicleInteractionState.OnFoot) { RestoreGameView(); if (blink != null) yield return blink.Open(0.4f); yield break; }

        BuildHud();
        if (!SetUpChase(chaseStart)) { TearDown(); if (blink != null) yield return blink.Open(0.4f); yield break; }
        CharacterSwitcher.BlockedReason = "Mia needs you";

        // 1. Cutscene (not again after a death or an arrest: straight back into the chase).
        failure = null;
        phase = Phase.Cutscene;
        GamePlayer.MenuOpen = true;
        HideGameView();
        var carHealth = car.GetComponent<VehicleHealth>();
        if (carHealth != null) carHealth.Invulnerable = true;
        // Both cars start their engines and settle a frame before anything is filmed.
        yield return null;
        WakeMiaCar();
        CollectWheels();
        if (cutscene)
        {
            driver.SetRealDriverVisible(true);       // the close-ups show Jason himself, not the car's driver dummy
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) { SeatMia(); yield return null; }
            var cam = MakeCamera();
            AddCabinLights();
            StartCoroutine(KeepRouteClear());
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f);   // the bars are there from the first frame
            if (blink != null) StartCoroutine(blink.Open(0.2f));
            yield return Shots(cam);
            if (blink != null) yield return blink.Close(0.25f);
            RemoveCabinLights();
            driver.SetRealDriverVisible(false);
            Destroy(cam.gameObject);
            StartCoroutine(Letterbox(false));
        }
        else
        {
            // Behind the closed screen: the chase gets up to speed and Mia into her seat.
            StartCoroutine(KeepRouteClear());
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { SeatMia(); HideGameView(); yield return null; }
        }

        // The player takes over at speed. The first time, the controls come in one at a time (the cruisers keep their
        // formation behind him until he has them all); after a restart he gets them all at once.
        bool teach = cutscene && driveTutorial;
        phase = Phase.LoseCops;
        restarting = false;
        subtitle.text = "";
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 400f);   // in play: above the touch pedals
        skipButton.SetActive(false);
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        if (teach && carRcc != null) tutorial = DriveTutorial.Begin(gameObject, carRcc, AutoSteerInput, hud, font);
        else ReleaseCopsBehindJason();
        if (carRcc != null && tutorial == null) carRcc.externalController = false;
        takeoverTime = Time.time;
        if (blink != null) yield return blink.Open(0.35f);
        if (tutorial == null) SetObjective("Lose the cops");
        StartCoroutine(EndInvulnerability(carHealth, TakeoverGrace));
        StartCoroutine(EndProtection(TakeoverGrace));
        StartCoroutine(MiaSplitsOff());

        // 2. Lose the cops, then the drive.
        var wanted = WantedManager.Instance;
        while (tutorial != null && !tutorial.Finished)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            yield return null;
        }
        if (tutorial != null)
        {
            ReleaseCopsBehindJason();
            SetObjective("Lose the cops");
        }
        while (wanted != null && wanted.Stars > 0)
        {
            if (Failed(out string why)) { yield return Fail(why); yield break; }
            yield return null;
        }
        // (Dying clears the wanted level too: that is not losing the cops.)
        if (Failed(out string early)) { yield return Fail(early); yield break; }

        // 3. Cops lost: the mission is won. Saved straight away, so nothing that happens next (the app closed, a crash, the
        // scene reloaded) can bring it back.
        phase = Phase.Done;
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        SetObjective("");
        var money = OpenworldCrime.Economy.MoneyManager.Instance;
        if (money != null) money.Add(reward);
        Banner("MISSION COMPLETE", "+$" + reward.ToString("N0"), 4.5f);
        CharacterSwitcher.BlockedReason = null;
        yield return new WaitForSeconds(5f);

        // Mia calls.
        bool callOver = false;
        var phone = PhoneUI.Ensure();
        phone.IncomingCall(phone.Mia, CallLines, () => callOver = true);
        while (!callOver) yield return null;
        Cleanup();
    }

    // ---------- Setup ----------

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
        road.points.Clear();
        road.points.AddRange(behind);
        if (road.Count > 0 && route.Count > 0 && (road.points[road.Count - 1] - route[0]).sqrMagnitude < 1f) road.points.RemoveAt(road.Count - 1);
        road.Rebuild();
        heroDistance = heroStart = road.Length;
        road.points.AddRange(route);
        road.Rebuild();
        if (road.Count < 4) return false;
        leaderSpeed = cutsceneSpeed;

        Vector3 forward = road.Heading(heroDistance + 3f);
        if (forward.sqrMagnitude < 0.5f) forward = Flat(routeFromDirection).normalized;
        if (!BuildMiaRoad(forward)) miaRoad.points.Clear();
        ClearTraffic(road, 0f, heroDistance + 700f, 16f);
        if (miaRoad.Count > 1) ClearTraffic(miaRoad, 0f, 700f, 12f);

        car = Instantiate(carPrefab, Sample(heroDistance) + Vector3.up * 0.4f, Quaternion.LookRotation(forward));
        car.name = "Intro Car";
        carBody = car.GetComponent<Rigidbody>();
        carRcc = car.GetComponent<RCC_CarControllerV3>();
        vehicle = car.GetComponent<IVehicleController>();
        if (vehicle == null || carBody == null || !driver.EnterInstant(vehicle)) { Destroy(car); car = null; return false; }
        carBody.linearVelocity = forward * cutsceneSpeed;
        var paint = car.GetComponent<VehiclePaintVariants>();
        if (paint != null && carPaint >= 0 && carPaint < paint.Count) paint.ApplyVariant(carPaint);
        travel = forward;
        ProtectCar(car, true);

        SpawnMiaCar();

        var wanted = WantedManager.Instance;
        if (wanted != null) wanted.SetWanted(wantedStars, false);
        var dispatcher = FindFirstObjectByType<PoliceDispatcher>();
        cops.Clear();
        if (dispatcher != null)
        {
            // Two abreast behind each car, staggered: Jason's first, then Mia's.
            float[] gaps = { 12f, 14f, 22f, 25f };
            bool hasMia = miaCar != null;
            for (int i = 0; i < Mathf.Min(policeCars, gaps.Length); i++)
            {
                bool behindMia = hasMia && i % 2 == 1;
                float d = Mathf.Max(0f, heroDistance - gaps[i]);
                Vector3 heading = road.Heading(d);
                if (heading.sqrMagnitude < 0.5f) heading = forward;
                float away = hasMia ? -Mathf.Sign(miaSide) : (i % 2 == 0 ? 1f : -1f);
                float side = behindMia ? miaSide + Mathf.Sign(miaSide) * (i < 2 ? 0.9f : -0.9f) : away * (i < 2 ? 1.1f : -0.6f) * (hasMia ? 1f : 1.5f);
                Vector3 at = Sample(d) + Vector3.Cross(Vector3.up, heading) * side;
                PoliceCarUnit unit = dispatcher.SpawnChaseCarUnit(at + Vector3.up * 0.3f, Quaternion.LookRotation(heading), heading * cutsceneSpeed);
                if (unit == null) continue;
                var cop = new ChaseCop { unit = unit, body = unit.GetComponent<Rigidbody>(), gap = gaps[i], side = side, phase = i * 1.7f, distance = d, followsMia = behindMia };
                cops.Add(cop);
                IgnoreCop(unit.gameObject, true);
                IgnoreBetween(miaCar, unit.gameObject, true);
            }
        }
        return true;
    }

    // ---------- Mia's car ----------

    // Her lane: the one beside his going the same way (the kerb lane first: traffic keeps left). Followed straight on at every
    // junction for now; where she turns off is picked later (PlanSplit).
    bool BuildMiaRoad(Vector3 forward)
    {
        var network = RoadNetwork.Instance;
        miaRoad.points.Clear();
        miaJunctions.Clear();
        Vector3 at = Sample(heroDistance);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        bool found = false;
        RoadNetwork.RoadPoint start = default;
        foreach (float side in new[] { -6f, 6f, -5f, 5f, -7f, 7f, -4f, 4f })
        {
            Vector3 want = at + right * side;
            if (!network.TryFindLaneAhead(want, forward, 0f, out RoadNetwork.RoadPoint p)) continue;
            float lateral = Vector3.Dot(Flat(p.position - at), right);
            if (Mathf.Abs(lateral) < 3f || Mathf.Abs(lateral - side) > 2.5f) continue;
            start = p;
            found = true;
            break;
        }
        // No lane beside his: she drives ahead of him in his own.
        if (!found && !network.TryFindLaneAhead(at, forward, 14f, out start)) return false;

        FollowLanes(start.lane, start.index, 1400f, -1);
        miaRoad.Rebuild();
        if (miaRoad.Count < 4) return false;
        Vector3 offset = Flat(miaRoad.points[0] - at);
        miaOffset = Vector3.Dot(offset, forward);
        miaSide = Vector3.Dot(offset, right);
        if (Mathf.Abs(miaSide) < 0.5f) miaSide = -0.5f;
        miaDistance = Mathf.Max(0f, -miaOffset);
        return true;
    }

    // Appends lanes to her road: straight on at each junction (or the given link first), noting each junction passed.
    void FollowLanes(int lane, int index, float length, int firstLink)
    {
        var network = RoadNetwork.Instance;
        int guard = 0;
        float travelled = miaRoad.Count > 0 ? EndDistance() : 0f;
        while (travelled < length && guard++ < 64)
        {
            var pts = network.lanes[lane].points;
            for (int i = Mathf.Max(0, index); i < pts.Length; i++) travelled += Append(pts[i]);
            var outgoing = network.lanes[lane].outgoing;
            if (outgoing.Count == 0) break;
            miaJunctions.Add(new Junction { distance = travelled, lane = lane, index = miaRoad.Count - 1 });
            RoadNetwork.Link link = firstLink >= 0 && firstLink < outgoing.Count ? outgoing[firstLink] : Straightest(lane);
            firstLink = -1;
            foreach (Vector3 p in link.points) travelled += Append(p);
            lane = link.to;
            index = 0;
        }
    }

    float EndDistance()
    {
        float d = 0f;
        for (int i = 1; i < miaRoad.Count; i++) d += Vector3.Distance(miaRoad.points[i - 1], miaRoad.points[i]);
        return d;
    }

    float Append(Vector3 p)
    {
        if (miaRoad.Count > 0)
        {
            float step = Vector3.Distance(miaRoad.points[miaRoad.Count - 1], p);
            if (step < 0.05f) return 0f;      // the joins of lanes and links repeat a point
            miaRoad.points.Add(p);
            return step;
        }
        miaRoad.points.Add(p);
        return 0f;
    }

    static float TurnAngle(int lane, RoadNetwork.Link link)
    {
        var network = RoadNetwork.Instance;
        var pts = network.lanes[lane].points;
        Vector3 into = Flat(pts[pts.Length - 1] - pts[Mathf.Max(0, pts.Length - 2)]).normalized;
        var next = network.lanes[link.to].points;
        Vector3 outOf = next.Length > 1 ? Flat(next[1] - next[0]).normalized : into;
        return Vector3.SignedAngle(into, outOf, Vector3.up);
    }

    static RoadNetwork.Link Straightest(int lane)
    {
        RoadNetwork.Link best = null;
        float bestAngle = float.MaxValue;
        foreach (var link in RoadNetwork.Instance.lanes[lane].outgoing)
        {
            float a = Mathf.Abs(TurnAngle(lane, link));
            if (a < bestAngle) { bestAngle = a; best = link; }
        }
        return best;
    }

    // Her turn-off: the first junction at least the given distance ahead of her with a real turn - toward her own side first
    // (away from Jason). Her road is rebuilt from there: the turn, then straight on.
    bool PlanSplit(float minAhead)
    {
        var network = RoadNetwork.Instance;
        for (int j = 0; j < miaJunctions.Count; j++)
        {
            Junction junction = miaJunctions[j];
            if (junction.distance < miaDistance + minAhead) continue;
            var outgoing = network.lanes[junction.lane].outgoing;
            int pick = -1;
            float score = 0f;
            for (int i = 0; i < outgoing.Count; i++)
            {
                float a = TurnAngle(junction.lane, outgoing[i]);
                if (Mathf.Abs(a) < 35f) continue;
                float s = Mathf.Abs(a) + (Mathf.Sign(a) == Mathf.Sign(miaSide) ? 100f : 0f);   // her side's turn first
                if (s > score) { score = s; pick = i; }
            }
            if (pick < 0) continue;
            miaRoad.points.RemoveRange(junction.index + 1, miaRoad.Count - junction.index - 1);
            miaJunctions.RemoveRange(j, miaJunctions.Count - j);
            FollowLanes(junction.lane, network.lanes[junction.lane].points.Length, junction.distance + 600f, pick);
            miaRoad.Rebuild();
            splitDistance = junction.distance;
            return true;
        }
        return false;
    }

    void SpawnMiaCar()
    {
        miaGone = true;
        if (miaRoad.Count < 4) return;
        GameObject prefab = miaCarPrefab != null ? miaCarPrefab : carPrefab;
        Vector3 heading = miaRoad.Heading(miaDistance + 3f);
        miaCar = Instantiate(prefab, miaRoad.Sample(miaDistance) + Vector3.up * 0.4f, Quaternion.LookRotation(heading));
        miaCar.name = "Mia's Car";
        miaBody = miaCar.GetComponent<Rigidbody>();
        miaRcc = miaCar.GetComponent<RCC_CarControllerV3>();
        miaVehicle = miaCar.GetComponent<IVehicleController>();
        if (miaBody == null) { Destroy(miaCar); miaCar = null; return; }
        miaBody.linearVelocity = heading * cutsceneSpeed;
        miaSpeed = cutsceneSpeed;
        var paint = miaCar.GetComponent<VehiclePaintVariants>();
        if (paint != null && miaCarPaint >= 0 && miaCarPaint < paint.Count) paint.ApplyVariant(miaCarPaint);
        var health = miaCar.GetComponent<VehicleHealth>();
        if (health != null) health.Invulnerable = true;
        ProtectCar(miaCar, false);
        IgnoreBetween(car, miaCar, true);
        miaGone = false;
        miaSplit = false;
        miaLateral = miaLateralWanted = 0f;
        mia = MakeMiaPuppet();
    }

    // After the car's own Start (which parks it and switches its engine off): driven from here, engine running.
    void WakeMiaCar()
    {
        if (miaRcc == null) return;
        miaRcc.externalController = true;
        miaRcc.handbrakeInput = 0f;
        miaRcc.brakeInput = 0f;
        if (!miaRcc.engineRunning) miaRcc.StartEngine(true);
    }

    void DriveMia(float dt)
    {
        miaDistance = miaRoad.Project(miaBody.position, miaDistance);
        float target;
        if (!miaSplit && heroOnRoad)
        {
            // Level with Jason: her place on his road (the two lanes run side by side up to her turn-off).
            float error = heroDistance + 0.6f - (heroStart + miaOffset + miaDistance);
            target = leaderSpeed + Mathf.Clamp(error * 0.9f, -6f, 6f);
        }
        else target = cutsceneSpeed + 2f;
        target = Mathf.Clamp(Mathf.Min(target, CornerSpeed(miaRoad, miaDistance)), 0f, 36f);
        miaSpeed = Mathf.MoveTowards(miaSpeed, target, (target > miaSpeed ? 7f : 10f) * dt);

        // Anything left in her lane: she swings round it.
        if (Time.time >= nextAvoid)
        {
            nextAvoid = Time.time + 0.15f;
            miaLateralWanted = AvoidLateral(miaBody, miaRoad, miaDistance, miaSpeed, miaLateralWanted);
        }
        miaLateral = Mathf.MoveTowards(miaLateral, miaLateralWanted, 3f * dt);

        float steer = Drive(miaBody, miaRoad, miaDistance, miaLateral, miaSpeed);
        if (miaRcc != null)
        {
            miaRcc.externalController = true;
            miaSteer = Mathf.Lerp(miaSteer, Mathf.Clamp(steer / Mathf.Max(5f, miaRcc.steerAngle), -1f, 1f), 1f - Mathf.Exp(-10f * dt));
            miaRcc.steerInput = miaSteer;
            miaRcc.throttleInput = Mathf.Clamp01(0.35f + (target - miaSpeed) * 0.25f);
            miaRcc.brakeInput = 0f;
            miaRcc.handbrakeInput = 0f;
        }
    }

    // Slower into a bend: the sharpest turn of the road over the next stretch sets the speed.
    float CornerSpeed(Track t, float distance)
    {
        Vector3 now = t.Heading(distance + 2f);
        float sharpest = 0f;
        for (float ahead = 8f; ahead <= 44f; ahead += 6f)
            sharpest = Mathf.Max(sharpest, Vector3.Angle(now, t.Heading(distance + ahead)) * Mathf.Lerp(1f, 0.6f, (ahead - 8f) / 36f));
        return Mathf.Lerp(cutsceneSpeed + 8f, 9f, Mathf.InverseLerp(10f, 80f, sharpest));
    }

    // Her lane, or a little to one side of it when something stands in it.
    readonly RaycastHit[] avoidHits = new RaycastHit[12];
    float AvoidLateral(Rigidbody body, Track t, float distance, float speed, float current)
    {
        float look = 8f + speed * 1.1f;
        float[] options = { current, 0f, -2.4f, 2.4f, -4f, 4f };
        foreach (float lateral in options)
            if (LaneClear(body, t, distance, lateral, look)) return lateral;
        return current;
    }

    bool LaneClear(Rigidbody body, Track t, float distance, float lateral, float look)
    {
        Vector3 from = body.position + Vector3.up * 0.9f;
        Vector3 heading = t.Heading(distance + look);
        Vector3 to = t.Sample(distance + look) + Vector3.Cross(Vector3.up, heading) * lateral + Vector3.up * 0.9f;
        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.1f) return true;
        int n = Physics.SphereCastNonAlloc(from, 1.0f, dir / length, avoidHits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = avoidHits[i];
            if (hit.distance <= 0f || hit.normal.y > 0.6f) continue;                // the road itself
            if (hit.point.y < body.position.y + 0.25f) continue;                    // a kerb
            Rigidbody owner = hit.rigidbody;
            if (owner != null && (owner == body || owner == carBody || owner == miaBody || IsCop(owner))) continue;
            if (car != null && hit.collider.transform.IsChildOf(car.transform)) continue;
            if (miaCar != null && hit.collider.transform.IsChildOf(miaCar.transform)) continue;
            return false;
        }
        return true;
    }

    // She drives off and pulls her cruisers after her. The turn-off is picked once the player has all the controls; she and
    // they are gone once far enough away (or out of sight).
    IEnumerator MiaSplitsOff()
    {
        if (miaCar == null) yield break;
        float since = 0f;
        while (!miaSplit)
        {
            if (miaCar == null || phase == Phase.None) yield break;
            bool ready = tutorial == null || tutorial.Finished;
            since = ready ? since + Time.deltaTime : 0f;
            if (since > 1.2f || !heroOnRoad && Time.time - takeoverTime > 3f)
            {
                if (PlanSplit(35f + miaSpeed * 1.4f))
                {
                    miaSplit = true;
                    splitTime = Time.time;
                    foreach (var cop in cops)
                    {
                        if (!cop.followsMia || cop.unit == null) continue;
                        cop.scripted = true;
                        cop.onMiaRoad = true;
                        cop.distance = miaRoad.Project(cop.body.position, Mathf.Max(0f, miaDistance - cop.gap));
                    }
                    StartCoroutine(Say(3, 4.5f));
                }
                else yield return new WaitForSeconds(1f);
            }
            yield return null;
        }
        while (miaCar != null)
        {
            if (phase == Phase.None) yield break;
            float apart = Flat(miaCar.transform.position - car.transform.position).magnitude;
            bool past = miaDistance > splitDistance + 30f;
            if (past && (apart > 160f || apart > 70f && !Visible(miaCar) || Time.time - splitTime > 30f)) break;
            yield return new WaitForSeconds(0.25f);
        }
        RemoveMiaCar();
    }

    bool Visible(GameObject go)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            if (r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds)) return true;
        return false;
    }

    void RemoveMiaCar()
    {
        if (miaGraph.IsValid()) miaGraph.Destroy();
        if (mia != null) Destroy(mia);
        mia = null;
        var dispatcher = FindFirstObjectByType<PoliceDispatcher>();
        for (int i = cops.Count - 1; i >= 0; i--)
        {
            var cop = cops[i];
            if (!cop.onMiaRoad) continue;
            if (cop.unit != null)
            {
                if (dispatcher != null) dispatcher.Release(cop.unit);
                else Destroy(cop.unit.gameObject);
            }
            cops.RemoveAt(i);
        }
        spinWheels.RemoveAll(w => w.body == null || w.body == miaBody);
        if (miaCar != null) Destroy(miaCar);
        miaCar = null;
        miaBody = null;
        miaRcc = null;
        miaGone = true;
    }

    IEnumerator Say(int line, float seconds)
    {
        if (subtitle == null) yield break;
        string text = Subtitles[line];
        subtitle.text = text;
        yield return new WaitForSeconds(seconds);
        if (subtitle != null && subtitle.text == text) subtitle.text = "";
    }

    // ---------- Driving ----------

    Vector3 travel = Vector3.forward;             // smoothed direction of travel (the cameras hang on this)

    void FixedUpdate()
    {
        if (car == null || carBody == null || road.Count < 2) return;
        if (phase != Phase.Cutscene && phase != Phase.LoseCops && phase != Phase.Done) return;
        float dt = Time.fixedDeltaTime;

        if (phase == Phase.Cutscene)
        {
            heroDistance = road.Project(carBody.position, heroDistance);
            heroOnRoad = true;
            leaderSpeed = cutsceneSpeed;
            float steer = Drive(carBody, road, heroDistance, 0f, cutsceneSpeed);
            if (carRcc != null)
            {
                // The game drives: his wheel and the front wheels turn with the road, the engine pulls.
                carRcc.externalController = true;
                carRcc.steerInput = Mathf.Clamp(steer / Mathf.Max(5f, carRcc.steerAngle), -1f, 1f);
                carRcc.throttleInput = 0.6f;
                carRcc.brakeInput = 0f;
                carRcc.handbrakeInput = 0f;
            }
        }
        else
        {
            heroDistance = road.Project(carBody.position, heroDistance);
            heroOnRoad = road.LastSqrDistance < 14f * 14f && driver != null && driver.State == VehicleInteractionState.InVehicle;
            leaderSpeed = Mathf.Max(0f, Vector3.Dot(carBody.linearVelocity, road.Heading(heroDistance)));
        }
        Vector3 roadDir = road.Heading(heroDistance + 3f);
        Vector3 carDir = Flat(car.transform.forward).normalized;
        Vector3 want = phase == Phase.Cutscene && roadDir.sqrMagnitude > 0.5f ? roadDir : carDir;
        if (want.sqrMagnitude > 0.5f) travel = Vector3.Slerp(travel, want, 1f - Mathf.Exp(-4f * dt)).normalized;

        if (miaCar != null && miaBody != null && !miaGone) DriveMia(dt);

        float time = Time.time;
        foreach (var cop in cops)
        {
            if (!cop.scripted || cop.unit == null || cop.body == null || !cop.unit.isActiveAndEnabled) continue;
            Track t = cop.onMiaRoad ? miaRoad : road;
            cop.distance = t.Project(cop.body.position, cop.distance);
            float leader, speed0;
            if (cop.onMiaRoad) { leader = miaDistance; speed0 = miaSpeed; }
            else if (cop.followsMia && miaCar != null) { leader = heroStart + miaOffset + miaDistance; speed0 = miaSpeed; }
            else { leader = heroDistance; speed0 = leaderSpeed; }
            float wanted = leader - (cop.gap + 1.2f * Mathf.Sin(time * 0.7f + cop.phase));
            float speed = Mathf.Max(0f, speed0 + Mathf.Clamp((wanted - cop.distance) * 0.9f, -7f, 8f));
            if (cop.onMiaRoad) speed = Mathf.Min(speed, CornerSpeed(miaRoad, cop.distance) + 1f);
            float lateral = cop.onMiaRoad ? 0.8f * Mathf.Sin(time * 0.8f + cop.phase) : cop.side * (1f + 0.15f * Mathf.Sin(time * 0.8f + cop.phase));
            Drive(cop.body, t, cop.distance, lateral, speed);
        }
    }

    // Physics velocity toward a point ahead on the road (the wheels roll), yaw turned along it (pitch and roll left to the
    // suspension), never rolled over by a knock. Returns the steering angle a real car would need for it (pure pursuit).
    float Drive(Rigidbody body, Track t, float distance, float lateral, float speed)
    {
        Vector3 heading = t.Heading(distance + 8f);
        Vector3 bodyForward = Flat(body.rotation * Vector3.forward);
        if (heading.sqrMagnitude < 0.5f) heading = bodyForward.normalized;
        float look = Mathf.Clamp(speed * 0.4f, 6f, 11f);
        Vector3 aim = Flat(t.Sample(distance + look) + Vector3.Cross(Vector3.up, heading) * lateral - body.position);
        if (aim.sqrMagnitude < 0.01f) aim = heading;
        Vector3 v = aim.normalized * speed;
        v.y = body.linearVelocity.y;
        body.linearVelocity = v;

        float steer = 0f;
        if (bodyForward.sqrMagnitude > 0.01f)
        {
            float alpha = Vector3.SignedAngle(bodyForward, aim, Vector3.up);
            const float wheelbase = 2.7f;
            steer = Mathf.Atan2(2f * wheelbase * Mathf.Sin(alpha * Mathf.Deg2Rad), aim.magnitude) * Mathf.Rad2Deg;
            // Yaw only, toward where it is going: it never crabs, and the suspension keeps its own pitch and roll.
            float yaw = alpha * (1f - Mathf.Exp(-8f * Time.fixedDeltaTime));
            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * body.rotation;
            Vector3 up = rotation * Vector3.up;
            if (Vector3.Angle(up, Vector3.up) > 10f)
                rotation = Quaternion.Slerp(rotation, Quaternion.FromToRotation(up, Vector3.up) * rotation, 0.2f);
            body.MoveRotation(rotation);
        }
        Vector3 spin = body.angularVelocity;
        body.angularVelocity = new Vector3(spin.x * 0.8f, 0f, spin.z * 0.8f);
        return steer;
    }

    // The game's wheel for the player's car while he only has the accelerator (DriveTutorial): along his lane.
    float AutoSteerInput()
    {
        if (car == null || carRcc == null) return 0f;
        float speed = carBody.linearVelocity.magnitude;
        Vector3 aim = Flat(Sample(heroDistance + Mathf.Clamp(speed * 0.55f, 7f, 18f)) - car.transform.position);
        Vector3 forward = Flat(car.transform.forward);
        if (aim.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f) return 0f;
        float alpha = Vector3.SignedAngle(forward, aim, Vector3.up);
        float steer = Mathf.Atan2(2f * 2.7f * Mathf.Sin(alpha * Mathf.Deg2Rad), aim.magnitude) * Mathf.Rad2Deg;
        return Mathf.Clamp(steer * 1.3f / Mathf.Max(5f, carRcc.steerAngle), -1f, 1f);
    }

    void ReleaseCopsBehindJason()
    {
        foreach (var cop in cops)
            if (!cop.followsMia || miaCar == null) cop.scripted = false;   // their own AI takes the chase from here
    }

    Vector3 Sample(float distance) => road.Sample(distance);

    // Traffic cars and bikes on a stretch of road between the two distances go back to the iTS pool (they respawn elsewhere).
    void ClearTraffic(Track t, float fromDistance, float toDistance, float radius)
    {
        if (t.Count < 2) return;
        var samples = new List<Vector3>();
        for (float d = Mathf.Max(0f, fromDistance); d <= toDistance; d += 8f) samples.Add(Flat(t.Sample(d)));
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

    // Through the cutscene and the tutorial the road ahead of the chase (out of shot) is kept empty, and Mia's until she's gone.
    IEnumerator KeepRouteClear()
    {
        while (car != null && (phase == Phase.Cutscene || phase == Phase.LoseCops || phase == Phase.Done))
        {
            bool teaching = phase == Phase.Cutscene || tutorial != null && !tutorial.Finished;
            if (teaching) ClearTraffic(road, heroDistance + 45f, heroDistance + 380f, 14f);
            if (miaCar != null && !miaGone) ClearTraffic(miaRoad, miaDistance + 40f, miaDistance + 260f, 10f);
            if (!teaching && (miaCar == null || miaGone)) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    // Not a scratch on the cars: no crash deformation, no backfire flames flashing through the shots, no old-style lens flare.
    bool carDamage;
    readonly List<RCC_Exhaust> quietExhausts = new List<RCC_Exhaust>();

    void ProtectCar(GameObject target, bool player)
    {
        var rcc = target.GetComponent<RCC_CarControllerV3>();
        if (rcc != null)
        {
            if (player) carDamage = rcc.useDamage;
            rcc.useDamage = false;
        }
        foreach (var flare in target.GetComponentsInChildren<LensFlare>(true)) flare.enabled = false;
        if (player) quietExhausts.Clear();
        foreach (var exhaust in target.GetComponentsInChildren<RCC_Exhaust>())
        {
            if (!exhaust.enabled) continue;
            exhaust.enabled = false;
            foreach (var particles in exhaust.GetComponentsInChildren<ParticleSystem>()) particles.Clear();
            foreach (var light in exhaust.GetComponentsInChildren<Light>()) light.intensity = 0f;
            if (player) quietExhausts.Add(exhaust);
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

    static void IgnoreBetween(GameObject a, GameObject b, bool ignore)
    {
        if (a == null || b == null) return;
        var theirs = b.GetComponentsInChildren<Collider>();
        foreach (var x in a.GetComponentsInChildren<Collider>())
            foreach (var y in theirs)
                if (x != null && y != null) Physics.IgnoreCollision(x, y, ignore);
    }

    // The player has had the wheel a moment: everything is solid again (a cruiser on top of the car right now waits).
    // Mia's car stays out of reach: it is on rails, a knock would only look wrong.
    IEnumerator EndProtection(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        foreach (var exhaust in quietExhausts) if (exhaust != null) exhaust.enabled = true;
        quietExhausts.Clear();
        if (carRcc != null) carRcc.useDamage = carDamage;
        foreach (var cop in cops.ToArray())
        {
            if (cop.followsMia && miaCar != null) continue;
            while (cop.unit != null && car != null && (cop.unit.transform.position - car.transform.position).sqrMagnitude < 30f) yield return null;
            if (cop.unit != null) IgnoreCop(cop.unit.gameObject, false);
        }
    }

    // ---------- Mia ----------

    // Mia: a copy of her character with only the body (no controller, physics or UI), at the wheel of her car.
    GameObject MakeMiaPuppet()
    {
        PlayableCharacter source = null;
        foreach (var c in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            if (c != null && c.gameObject.scene.IsValid() && c.id == "girl") source = c;
        if (source == null || miaCar == null) return null;

        bool wasActive = source.gameObject.activeSelf;
        source.gameObject.SetActive(false);              // the copy starts inactive: none of its scripts wake up
        var puppet = Instantiate(source.gameObject);
        source.gameObject.SetActive(wasActive);
        puppet.name = "Mia (intro)";
        var invector = puppet.transform.Find("Invector Components");
        if (invector != null) DestroyImmediate(invector.gameObject);
        StripToBody(puppet);

        Transform seat = miaVehicle != null ? miaVehicle.GetDriverSeat() : null;
        puppet.transform.SetParent(miaCar.transform, false);
        if (seat != null) puppet.transform.SetPositionAndRotation(seat.position, seat.rotation);
        puppet.SetActive(true);

        var animator = puppet.GetComponent<Animator>();
        AnimationClip pose = driveClip != null ? driveClip : source.sitClip;
        if (animator != null && pose != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            miaGraph = PlayableGraph.Create("Mia driving");
            var output = AnimationPlayableOutput.Create(miaGraph, "Seat", animator);
            var clip = AnimationClipPlayable.Create(miaGraph, pose);
            if (pose == driveClip) { clip.SetTime(0.0); clip.SetSpeed(0.0); }   // the exit clip held on its first frame: seated
            output.SetSourcePlayable(clip);
            miaGraph.Play();
        }
        foreach (var r in puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
        return puppet;
    }

    // Her hips where Jason's are in his seat, measured in each car's own seat space (his pose is the real driving pose).
    void SeatMia()
    {
        if (mia == null || miaCar == null || miaVehicle == null || !mia.activeSelf) return;
        var jason = driver != null ? driver.GetComponent<Animator>() : null;
        var her = mia.GetComponent<Animator>();
        Transform jSeat = vehicle != null ? vehicle.GetDriverSeat() : null, mSeat = miaVehicle.GetDriverSeat();
        if (jason == null || her == null || !jason.isHuman || !her.isHuman || jSeat == null || mSeat == null) return;
        Transform jh = jason.GetBoneTransform(HumanBodyBones.Hips), mh = her.GetBoneTransform(HumanBodyBones.Hips);
        if (jh == null || mh == null) return;
        mia.transform.rotation = mSeat.rotation * (Quaternion.Inverse(jSeat.rotation) * driver.transform.rotation);
        Vector3 target = mSeat.TransformPoint(jSeat.InverseTransformPoint(jh.position)) + miaCar.transform.TransformVector(passengerOffset);
        mia.transform.position += target - mh.position;
    }

    // Without the driving pose (no clip set): her bench-sitting clip lets the legs hang straight down, through the floor of a
    // low car. Thighs forward, knees bent, set over the animation every frame while she drives.
    void SeatMiaLegs()
    {
        if (mia == null || miaCar == null || !mia.transform.IsChildOf(miaCar.transform)) return;
        var animator = mia.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return;
        for (int side = 0; side < 2; side++)
        {
            Transform thigh = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform shin = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Transform foot = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (thigh == null || shin == null || foot == null) continue;
            float spread = side == 0 ? -0.08f : 0.08f;
            Point(thigh, shin, miaCar.transform.TransformDirection(new Vector3(spread, 0.1f, 1f)));
            Point(shin, foot, miaCar.transform.TransformDirection(new Vector3(0f, -0.62f, 0.78f)));
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

    // ---------- Close-up lighting ----------

    // So both of them read behind the glass: clearer windows, and Jason's legs (through the floor of a low car) not drawn. The
    // characters go on a layer of their own for the cutscene. (No cabin light: a point light in the cabin showed as a white
    // blob on the bonnet in the close-ups.)
    const int ActorLayer = 30;
    readonly Dictionary<GameObject, int> actorLayers = new Dictionary<GameObject, int>();
    readonly List<Renderer> hiddenShoes = new List<Renderer>();
    readonly List<KeyValuePair<Renderer, int>> clearedGlass = new List<KeyValuePair<Renderer, int>>();

    void AddCabinLights()
    {
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
        ClearGlass(car);
        ClearGlass(miaCar);
    }

    void ClearGlass(GameObject target)
    {
        if (target == null) return;
        var block = new MaterialPropertyBlock();
        foreach (var r in target.GetComponentsInChildren<MeshRenderer>())
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

    void RemoveCabinLights()
    {
        foreach (var pair in actorLayers) if (pair.Key != null) pair.Key.layer = pair.Value;
        actorLayers.Clear();
        foreach (var r in hiddenShoes) if (r != null) r.enabled = true;
        hiddenShoes.Clear();
        foreach (var pair in clearedGlass) if (pair.Key != null) pair.Key.SetPropertyBlock(null, pair.Value);
        clearedGlass.Clear();
    }

    // ---------- The shots ----------
    // Each shot is a camera move: a position and a look target over it, a lens, a length in real seconds, handheld shake and an
    // optional subtitle. Hard cuts between them, like a film: aerial, a low shot the cars blast past, the two of them at the
    // wheel, the cops' view, a tyre, a long lens head-on, and down behind Jason's car for the hand-over.

    class Shot
    {
        public float seconds = 2.4f, fovFrom = 45f, fovTo = 45f, shake = 0.5f;
        public int subtitle = -1;
        public System.Func<float, Vector3> position;   // k (0..1, eased) -> world position
        public System.Func<float, Vector3> look;       // k -> world look-at point
        public System.Action onStart;
        public bool linear;                            // k runs evenly (a fixed camera the cars race past)
    }

    // A point around Jason's car in the direction of travel (smoother than the car's own axes).
    Vector3 Rig(float x, float y, float z) =>
        car.transform.position + Vector3.Cross(Vector3.up, travel) * x + Vector3.up * y + travel * z;

    Vector3 MiaPosition => miaCar != null ? miaCar.transform.position : car.transform.position;

    // Across the road from Jason's car toward Mia's.
    Vector3 Across
    {
        get
        {
            Vector3 a = Flat(MiaPosition - car.transform.position);
            a -= travel * Vector3.Dot(a, travel);
            if (a.sqrMagnitude > 0.25f) return a.normalized;
            return Vector3.Cross(Vector3.up, travel) * (miaSide >= 0f ? 1f : -1f);
        }
    }

    float Spread => Mathf.Max(1f, Mathf.Abs(Vector3.Dot(Flat(MiaPosition - car.transform.position), Across)));

    // A point around the pair: x across toward Mia, y up, z ahead.
    Vector3 Pair(float x, float y, float z) =>
        Vector3.Lerp(car.transform.position, MiaPosition, 0.5f) + Across * x + Vector3.up * y + travel * z;

    // Their heads, from the bones (Jason's real model is shown during the cutscene; Mia is the puppet).
    Vector3 JasonHead => Bone(driver != null ? driver.GetComponent<Animator>() : null, HumanBodyBones.Head, car.transform.TransformPoint(-0.4f, 1.0f, 0.1f));
    Vector3 MiaHead => Bone(mia != null ? mia.GetComponent<Animator>() : null, HumanBodyBones.Head,
        miaCar != null ? miaCar.transform.TransformPoint(-0.4f, 1.0f, 0.1f) : car.transform.TransformPoint(0.4f, 1.0f, 0.1f));

    static Vector3 Bone(Animator animator, HumanBodyBones bone, Vector3 fallback)
    {
        if (animator == null || !animator.isHuman) return fallback;
        Transform t = animator.GetBoneTransform(bone);
        return t != null ? t.position : fallback;
    }

    // Toward the middle of a car from its driver's seat (the side a close-up sits on, so the face is seen past the pillar).
    static Vector3 Inward(GameObject target, IVehicleController v)
    {
        Transform seat = v != null ? v.GetDriverSeat() : null;
        if (target == null || seat == null) return Vector3.zero;
        float x = target.transform.InverseTransformPoint(seat.position).x;
        return target.transform.right * (x > 0f ? -1f : 1f);
    }

    Vector3 FrontWheelOutside()
    {
        if (miaCar == null) return Rig(0f, 0.35f, 1.5f);
        Vector3 best = miaCar.transform.TransformPoint(0f, 0.35f, 1.4f);
        float bestScore = float.MinValue;
        foreach (var w in miaCar.GetComponentsInChildren<WheelCollider>())
        {
            Vector3 local = miaCar.transform.InverseTransformPoint(w.transform.position);
            float outward = Vector3.Dot(w.transform.position - miaCar.transform.position, Across);
            float score = local.z * 2f + outward;
            if (score > bestScore) { bestScore = score; best = w.transform.position; }
        }
        return best;
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
            if (owner != null && (owner == carBody || owner == miaBody || IsCop(owner))) continue;
            if (hits[i].collider.transform.IsChildOf(car.transform)) continue;
            if (miaCar != null && hits[i].collider.transform.IsChildOf(miaCar.transform)) continue;
            if (hits[i].distance > 0f && hits[i].distance < best) best = hits[i].distance;
        }
        return best < length ? look + to / length * Mathf.Max(1.2f, best - 0.3f) : wanted;
    }

    bool IsCop(Rigidbody body)
    {
        foreach (var cop in cops) if (cop.body == body) return true;
        return false;
    }

    List<Shot> BuildShots()
    {
        var shots = new List<Shot>();
        bool two = miaCar != null;

        // 1. Aerial: high and ahead over the far side of the road, craning down as the whole chase rushes toward the lens.
        shots.Add(new Shot { seconds = 1.7f, fovFrom = 50f, fovTo = 44f, shake = 0.05f,
            position = k => Pair(-Spread * 0.5f - 9f, Mathf.Lerp(24f, 15f, k), Mathf.Lerp(42f, 30f, k)),
            look = k => Pair(0f, 0f, Mathf.Lerp(-8f, -2f, k)) });

        // 2. On the tarmac between the two lanes, a few metres ahead: the cars come straight at it and blast past either side.
        Vector3 groundSpot = Vector3.zero;
        shots.Add(new Shot { seconds = 1.15f, fovFrom = 58f, fovTo = 58f, shake = 0.25f, linear = true,
            onStart = () =>
            {
                float ahead = cutsceneSpeed * 1.25f;
                Vector3 heading = road.Heading(heroDistance + ahead);
                Vector3 across = two ? Across : Vector3.Cross(Vector3.up, heading);
                groundSpot = Sample(heroDistance + ahead) + across * (two ? Spread * 0.5f : 2.6f) + Vector3.up * 0.45f;
            },
            position = k => groundSpot,
            look = k => Pair(0f, 0.7f, 0f) });

        // 3. Mia at the wheel, through her side window, the road streaming past behind her. "They're right on us!"
        if (two && mia != null)
            shots.Add(new Shot { seconds = 1.7f, fovFrom = 40f, fovTo = 35f, shake = 0.18f, subtitle = 0,
                position = k => Clear(MiaHead, MiaHead + Across * Mathf.Lerp(1.7f, 1.45f, k) + travel * Mathf.Lerp(0.55f, 0.4f, k) + Vector3.up * 0.28f),
                look = k => MiaHead + Vector3.down * 0.12f });

        // 4. Jason at his, the same way. "Stay with me!"
        shots.Add(new Shot { seconds = 1.6f, fovFrom = 40f, fovTo = 35f, shake = 0.18f, subtitle = two ? 1 : 0,
            position = k => JasonHead + car.transform.forward * Mathf.Lerp(1.6f, 1.4f, k) + Inward(car, vehicle) * 0.4f + Vector3.up * 0.04f,
            look = k => JasonHead + Vector3.down * 0.05f });

        // 5. The cops' view: off the front of the lead cruiser, both cars just ahead.
        ChaseCop lead = null;
        foreach (var cop in cops) if (cop.unit != null && (lead == null || cop.gap < lead.gap)) lead = cop;
        if (lead != null)
            shots.Add(new Shot { seconds = 1.4f, fovFrom = 48f, fovTo = 44f, shake = 0.7f,
                position = k => lead.unit != null ? lead.unit.transform.TransformPoint(new Vector3(0f, 1.05f, 2.9f)) : Pair(0f, 1.2f, -9f),
                look = k => Pair(0f, 0.8f, 2f) });

        // 6. A tyre: low beside Mia's front wheel on the outside, the road streaming past.
        if (two)
            shots.Add(new Shot { seconds = 1.2f, fovFrom = 46f, fovTo = 42f, shake = 0.35f,
                position = k => { Vector3 w = FrontWheelOutside(); return Clear(w + Vector3.up * 0.2f, w + Across * 1.6f + travel * Mathf.Lerp(1.9f, 1.5f, k) + Vector3.up * 0.05f); },
                look = k => FrontWheelOutside() - travel * 0.3f });

        // 7. Long lens, low and far ahead: the two cars side by side, the cruisers stacked up behind. "I'll pull them off you!"
        Vector3 headOn = Vector3.zero;
        shots.Add(new Shot { seconds = 1.8f, fovFrom = 26f, fovTo = 19f, shake = 0.12f, subtitle = two ? 2 : 1,
            onStart = () =>
            {
                float ahead = cutsceneSpeed * 1.8f + 10f;     // they end the shot about 10 m off the lens
                Vector3 heading = road.Heading(heroDistance + ahead);
                Vector3 across = two ? Across : Vector3.Cross(Vector3.up, heading);
                headOn = Sample(heroDistance + ahead) + across * (two ? Spread * 0.5f : 0f) + Vector3.up * 1.3f;
            },
            position = k => headOn,
            look = k => Pair(0f, 0.7f, -1f) });

        // 8. Down behind Jason's car, rising into the game's own camera.
        shots.Add(new Shot { seconds = 1.0f, fovFrom = 52f, fovTo = 56f, shake = 0.1f,
            position = k => Rig(Mathf.Lerp(-1.6f, 0f, k), Mathf.Lerp(1.1f, 2.4f, k), Mathf.Lerp(-5.5f, -7f, k)),
            look = k => Rig(0f, 1f, 8f) });
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
            subtitle.text = shot.subtitle >= 0 && shot.subtitle < Subtitles.Length ? Subtitles[shot.subtitle] : "";
            for (float t = 0f; t < shot.seconds && !skip; t += Time.unscaledDeltaTime)
            {
                HideGameView();   // the car's touch pedals and dashboard switch on a moment after he is seated
                SeatMia();
                if (GameInput.CancelDown || GameInput.EnterDown) skip = true;
                float k = shot.linear ? t / shot.seconds : Mathf.SmoothStep(0f, 1f, t / shot.seconds);
                Vector3 position = shot.position(k);
                Vector3 look = shot.look(k);
                // Handheld: a slow drift.
                float time = Time.unscaledTime + seed;
                float shake = shot.shake;
                Vector3 drift = new Vector3(Mathf.PerlinNoise(time * 1.3f, 0f) - 0.5f, Mathf.PerlinNoise(0f, time * 1.1f) - 0.5f, 0f) * 0.08f * shake;
                cam.transform.position = position;
                if ((look - position).sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(look - position);
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
        cam.nearClipPlane = 0.05f;   // the close-ups sit on the bonnet
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);
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
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(from, to, t / 0.5f);
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f * k);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, 130f * to);
    }

    // ---------- At the store, failure ----------

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
            // Right after the takeover (the car is still invulnerable then) a hit from a cruiser could throw him out before he
            // has even touched the controls: he is put straight back behind the wheel. (Later, getting out is his call.)
            else if (phase == Phase.LoseCops && driver != null && Time.time - takeoverTime < TakeoverGrace
                     && driver.State == VehicleInteractionState.OnFoot)
                driver.EnterInstant(vehicle);
        }
        return why != null;
    }

    // Failed: the banner, then everything is put back and the chase starts again (no cutscene).
    IEnumerator Fail(string why)
    {
        restarting = true;
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
        SetObjective("");
        Banner("MISSION FAILED", why, 1.2f);
        CharacterSwitcher.BlockedReason = null;
        if (tutorial != null) Destroy(tutorial);
        tutorial = null;
        yield return new WaitForSecondsRealtime(1.3f);

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
        if (health != null && !health.isDead) health.ResetHealth();
        StartCoroutine(Run(false));
    }

    // Removes the mission's cars, Mia, the chasing cruisers and the wanted level, and gets Jason out of the car if he is in it.
    // (No StopAllCoroutines: the restart itself runs as one. The chase helpers end on their own once the phase is reset.)
    void TearDown()
    {
        phase = Phase.None;
        if (tutorial != null) Destroy(tutorial);
        tutorial = null;
        if (miaGraph.IsValid()) miaGraph.Destroy();
        if (mia != null) Destroy(mia);
        mia = null;
        if (miaCar != null) Destroy(miaCar);
        miaCar = null;
        miaBody = null;
        miaRcc = null;
        miaGone = true;
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
        if (carRcc != null) carRcc.externalController = false;
        if (car != null) Destroy(car, 0.5f);   // a moment for the forced exit to let go of it
        car = null;
        carBody = null;
        carRcc = null;
        vehicle = null;
        skip = false;
        if (Time.timeScale > 0f) Time.timeScale = 1f;
        Cleanup();
    }

    void Cleanup()
    {
        if (miaGraph.IsValid()) miaGraph.Destroy();
        RestoreGameView();
        GamePlayer.MenuOpen = false;
        CharacterSwitcher.BlockedReason = null;
        RemoveCabinLights();
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
        if (hud != null) Destroy(hud.gameObject);
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
