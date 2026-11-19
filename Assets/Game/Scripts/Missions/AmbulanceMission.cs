using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.UI;

// Paramedic missions. Get into an ambulance and press E: a patient somewhere in the city needs picking up before time runs
// out, then taking to the hospital while they still have health. Waiting costs the patient health slowly; crashing on the
// way costs a lot of it. Delivered alive, the patient pays (more for a quick, careful run). One mission at a time.
public class AmbulanceMission : MonoBehaviour
{
    public static AmbulanceMission Instance { get; private set; }

    [Header("Where")]
    [Tooltip("Pavement spots in the city (the pedestrians' spot data); patients are found at these.")]
    public AmbientSpotData spots;
    public float minDistance = 220f, maxDistance = 650f;
    [Tooltip("Where patients are dropped off. Empty: found from the hospital building at start.")]
    public Transform hospitalDropOff;
    [Tooltip("Where the patient walks in after being dropped off. Empty: away from the ambulance.")]
    public Transform hospitalEntrance;

    [Header("Patients")]
    public GameObject[] patientPrefabs = new GameObject[0];
    public RuntimeAnimatorController manAnimator, womanAnimator;

    [Header("Rules")]
    [Tooltip("Drivable vehicles whose name contains this are ambulances.")]
    public string ambulanceName = "Ambulance";
    [Tooltip("Seconds given per metre to the patient / to the hospital, on top of a fixed allowance.")]
    public float secondsPerMetre = 0.11f, baseSeconds = 25f;
    [Tooltip("Health the patient loses per second while waiting / while on board.")]
    public float waitingBleed = 0.6f, onBoardBleed = 0.25f;
    [Tooltip("Health lost per km/h of a crash above the threshold.")]
    public float crashHurt = 0.6f, crashThresholdKmh = 12f;
    [Tooltip("Most health one hit can take: a crash hurts, but only running out of health loses the patient.")]
    public float maxHurtPerHit = 30f;
    [Tooltip("Health lost per second once the time is up.")]
    public float overtimeBleed = 4f;
    public int baseReward = 600, maxTimeBonus = 500, maxCareBonus = 500;
    [Tooltip("Seconds the player may spend out of the ambulance before the mission is lost.")]
    public float outOfVehicleSeconds = 25f;

    enum Stage { Idle, ToPatient, Boarding, ToHospital, Unloading }

    Stage stage;
    float timeLeft, timeGiven, health, outTimer, messageUntil;
    Vector3 patientSpot, dropOff;
    bool hasDropOff;
    GameObject patient;
    Animator patientAnimator;
    Transform ambulance;
    AmbulanceCrashSensor sensor;
    Coroutine sequence;
    bool fadingShown;

    // HUD
    GameObject hudPanel, promptBox, messageBox;
    Text objective, timeText, healthLabel, promptText, messageText;
    Image timeFill, healthFill;
    Font font;

    public bool Running => stage != Stage.Idle;

    void Awake()
    {
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHud();
    }

    void Start()
    {
        if (hospitalDropOff != null) { dropOff = hospitalDropOff.position; hasDropOff = true; }
        else hasDropOff = FindHospital(out dropOff);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ flow

    void Update()
    {
        var vehicle = PlayerVehicle(out var interaction);
        bool inAmbulance = vehicle != null && vehicle.transform.name.IndexOf(ambulanceName, System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool hidden = GamePlayer.MenuOpen;

        if (stage == Stage.Idle)
        {
            bool offer = inAmbulance && !hidden && hasDropOff;
            if (offer && !promptBox.activeSelf) PlacePrompt(MobileControls());
            if (offer && !promptBox.activeSelf) promptText.text = MobileControls()
                ? "Tap <color=#FFC83D><b>START MISSION</b></color> for a paramedic mission"
                : "Press <color=#FFC83D><b> E </b></color> to start a paramedic mission";
            promptBox.SetActive(offer);
            if (offer && GameInput.MissionDown) Begin(vehicle.transform);
            hudPanel.SetActive(false);
            UpdateMessage();
            return;
        }

        promptBox.SetActive(false);
        hudPanel.SetActive(!hidden);
        float dt = Time.deltaTime;
        timeLeft -= dt;

        // Out of the ambulance: a short grace period.
        if (!inAmbulance || vehicle.transform != ambulance)
        {
            outTimer += dt;
            objective.text = "Get back in the ambulance  " + Mathf.CeilToInt(Mathf.Max(0f, outOfVehicleSeconds - outTimer)) + "s";
            if (outTimer > outOfVehicleSeconds) { Fail("You left the ambulance"); return; }
        }
        else outTimer = 0f;

        float speed = vehicle != null ? vehicle.CurrentSpeed : 0f;
        switch (stage)
        {
            case Stage.ToPatient:
                health -= waitingBleed * dt;
                if (outTimer == 0f) objective.text = "Pick up the patient";
                if (ambulance != null && Flat(ambulance.position - patientSpot) < 11f && speed < 2.5f && outTimer == 0f) StartBoarding();
                break;
            case Stage.Boarding:
                health -= waitingBleed * 0.5f * dt;
                if (outTimer == 0f) objective.text = "The patient is getting in";
                break;
            case Stage.ToHospital:
                health -= onBoardBleed * dt;
                if (outTimer == 0f) objective.text = "Take the patient to the hospital - drive carefully";
                if (ambulance != null && Flat(ambulance.position - dropOff) < 16f && speed < 2f && outTimer == 0f) StartUnloading();
                break;
            case Stage.Unloading:
                objective.text = "The patient is getting out";
                break;
        }

        // Out of time the patient fades fast; only running out of health loses them.
        if (timeLeft <= 0f && stage != Stage.Unloading)
        {
            health -= overtimeBleed * dt;
            if (!fadingShown) { Show("Hurry! The patient is fading", new Color(1f, 0.4f, 0.3f), 2.5f); fadingShown = true; }
        }
        if (health <= 0f) { Fail("The patient died"); return; }
        UpdateHud();
        UpdateMessage();
    }

    void Begin(Transform vehicle)
    {
        if (!FindPatientSpot(vehicle.position, out patientSpot)) { Show("No patients right now", new Color(1f, 0.6f, 0.4f)); return; }
        ambulance = vehicle;
        health = 100f;
        float distance = Vector3.Distance(vehicle.position, patientSpot);
        timeGiven = timeLeft = baseSeconds + distance * secondsPerMetre;
        outTimer = 0f;
        fadingShown = false;
        SpawnPatient(patientSpot);
        AttachSensor();
        SetMarker(patientSpot);
        stage = Stage.ToPatient;
        Show("PARAMEDIC  -  a patient needs you", new Color(1f, 0.35f, 0.35f));
    }

    void StartBoarding()
    {
        stage = Stage.Boarding;
        ClearMarker();
        sequence = StartCoroutine(BoardRoutine());
    }

    // The patient walks to the back of the ambulance; then a quick cut (the screen blinks shut) and they are on board, the
    // doors shut, rather than showing them climb in.
    IEnumerator BoardRoutine()
    {
        var doors = Doors();
        PlayPatient("walk");
        yield return WalkTo(() => Ground(doors != null ? doors.BehindPoint : RearOf(ambulance)), 1.5f, 15f, true);
        PlayPatient("idle1", "idle2");
        yield return Cut(() =>
        {
            if (patient != null) patient.SetActive(false);
        });
        sequence = null;
        Boarded();
    }

    // At the hospital: a cut, and the patient is standing behind the ambulance, then walks into the hospital.
    IEnumerator UnloadRoutine()
    {
        var doors = Doors();
        yield return Cut(() =>
        {
            if (patient == null) return;
            Vector3 behind = Ground(doors != null ? doors.BehindPoint : RearOf(ambulance));
            Vector3 entranceAt = hospitalEntrance != null ? hospitalEntrance.position : dropOff;
            Vector3 face = entranceAt - behind;
            face.y = 0f;
            patient.transform.SetPositionAndRotation(behind, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.LookRotation(-ambulance.forward));
            patient.SetActive(true);
            PlayPatient("idle1", "idle2");
        });
        // Handed over: the mission is done now. The patient walks into the hospital on their own and is gone at the doors.
        Vector3 entrance = hospitalEntrance != null ? hospitalEntrance.position : dropOff + (dropOff - RearOf(ambulance)).normalized * 6f;
        var walker = patient;
        var walkerAnimator = patientAnimator;
        patient = null;
        sequence = null;
        Delivered();
        if (walker != null) StartCoroutine(WalkIntoHospital(walker, walkerAnimator, entrance));
    }

    IEnumerator WalkIntoHospital(GameObject walker, Animator animator, Vector3 entrance)
    {
        yield return new WaitForSeconds(0.4f);
        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, Animator.StringToHash("walk")))
            animator.CrossFadeInFixedTime("walk", 0.2f, 0);
        float started = Time.time;
        while (walker != null && Time.time - started < 25f)
        {
            Vector3 goal = Ground(entrance);
            Vector3 to = goal - walker.transform.position;
            to.y = 0f;
            if (to.magnitude < 0.4f) break;
            walker.transform.rotation = Quaternion.Slerp(walker.transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-8f * Time.deltaTime));
            walker.transform.position = Ground(Vector3.MoveTowards(walker.transform.position, goal, 1.5f * Time.deltaTime));
            yield return null;
        }
        if (walker != null) Destroy(walker);
    }

    // The screen blinks shut, the change happens out of sight, and it opens again.
    IEnumerator Cut(System.Action duringBlack)
    {
        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0.35f);
        var doors = Doors();
        if (doors != null) doors.Close();
        duringBlack();
        yield return new WaitForSeconds(0.35f);
        if (blink != null) yield return blink.Open(0.45f);
    }

    // ---------- stooping through the rear doors ----------

    Transform patientHead, patientPelvis;
    Transform[] patientSpine = new Transform[0];
    float stoop;

    // A standing patient's head is higher than the door opening: in the doorway and inside they bend forward (and dip a
    // little at the knees if bending is not enough), easing in and out, so the head stays under the roof.
    void LateUpdate()
    {
        if (patient == null || !patient.activeInHierarchy || ambulance == null) { stoop = 0f; return; }
        var doors = Doors();
        if (doors == null) return;
        if (patientHead == null) FindPatientBones();
        if (patientHead == null || patientPelvis == null || patientSpine.Length == 0) return;

        bool inside = doors.IsInDoorway(patient.transform.position);
        stoop = Mathf.MoveTowards(stoop, inside ? 1f : 0f, Time.deltaTime * 2.5f);
        if (stoop <= 0f) return;

        // The head bone sits about 0.18 m below the top of the head (hair included): keep the top 7 cm under the roof.
        float limit = doors.DoorTopWorldY - 0.25f;
        float over = patientHead.position.y - limit;
        if (over <= 0f) return;

        // Bend at the spine: the head drops by L(1 - cos a) for a bend a over a torso of length L.
        float torso = Mathf.Max(0.3f, patientHead.position.y - patientPelvis.position.y);
        float angle = Mathf.Acos(Mathf.Clamp(1f - over / torso, Mathf.Cos(65f * Mathf.Deg2Rad), 1f)) * Mathf.Rad2Deg * stoop;
        Vector3 axis = patient.transform.right;
        float share = angle / patientSpine.Length;
        foreach (var bone in patientSpine) bone.rotation = Quaternion.AngleAxis(share, axis) * bone.rotation;

        // Whatever the bend could not take, the knees do.
        float left = patientHead.position.y - limit;
        if (left > 0f) patientPelvis.position -= Vector3.up * left * stoop;
    }

    void FindPatientBones()
    {
        var spine = new List<Transform>();
        foreach (var t in patient.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.EndsWith("head") && patientHead == null) patientHead = t;
            else if (n.EndsWith("pelvis") || n.EndsWith("hips")) patientPelvis = t;
            else if (n.Contains("spine") && !n.Contains("end")) spine.Add(t);
        }
        patientSpine = spine.ToArray();
    }

    AmbulanceRearDoors Doors() => ambulance != null ? ambulance.GetComponentInParent<AmbulanceRearDoors>() : null;

    // Walks the patient to a (possibly moving) point; on the ground, or in a straight line (stepping up into the back).
    IEnumerator WalkTo(System.Func<Vector3> target, float speed, float timeout, bool grounded)
    {
        float started = Time.time;
        while (patient != null && Time.time - started < timeout)
        {
            var t = patient.transform;
            Vector3 goal = target();
            Vector3 to = goal - t.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (to.magnitude < 0.12f) break;
            if (flat.sqrMagnitude > 0.0004f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-8f * Time.deltaTime));
            Vector3 next = Vector3.MoveTowards(t.position, goal, speed * Time.deltaTime);
            t.position = grounded ? Ground(next) : next;
            yield return null;
        }
    }

    void Boarded()
    {
        if (patient != null) patient.SetActive(false);
        float distance = Vector3.Distance(ambulance.position, dropOff);
        float extra = baseSeconds + distance * secondsPerMetre;
        timeLeft += extra;
        timeGiven += extra;
        fadingShown = false;
        stage = Stage.ToHospital;
        SetMarker(dropOff);
        Show("Patient on board - get to the hospital", new Color(0.5f, 1f, 0.6f));
    }

    void StartUnloading()
    {
        stage = Stage.Unloading;
        ClearMarker();
        sequence = StartCoroutine(UnloadRoutine());
    }

    void Delivered()
    {
        float timeShare = Mathf.Clamp01(timeLeft / Mathf.Max(1f, timeGiven));
        int reward = baseReward + Mathf.RoundToInt(maxTimeBonus * timeShare) + Mathf.RoundToInt(maxCareBonus * Mathf.Clamp01(health / 100f));
        var money = OpenworldCrime.Economy.MoneyManager.Instance;
        if (money != null) money.Add(reward);
        Show("PATIENT DELIVERED   +$" + reward.ToString("N0"), new Color(0.45f, 1f, 0.5f));
        End();
    }

    public bool Active => stage != Stage.Idle;

    OpenworldCrime.Controls.ControlsModeManager controlsMode;

    void PlacePrompt(bool mobile)
    {
        var rect = (RectTransform)promptBox.transform;
        rect.anchorMin = rect.anchorMax = mobile ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.pivot = mobile ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.anchoredPosition = mobile ? new Vector2(0f, -96f) : new Vector2(36f, -96f);
    }
    bool MobileControls()
    {
        if (controlsMode == null) controlsMode = FindFirstObjectByType<OpenworldCrime.Controls.ControlsModeManager>(FindObjectsInactive.Include);
        return controlsMode != null && controlsMode.IsMobile;
    }
    // In an ambulance with no mission running: the moment to offer "start a mission" (the on-screen button shows then).
    public bool OfferingStart => promptBox != null && promptBox.activeSelf;

    // Ends a running mission from outside, for example when the player dies.
    public void Abort(string reason)
    {
        if (stage != Stage.Idle) Fail(reason);
    }

    void Fail(string reason)
    {
        Show("MISSION FAILED  -  " + reason, new Color(1f, 0.35f, 0.3f));
        End();
    }

    void End()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
        var doors = Doors();
        if (doors != null) doors.Close();
        stage = Stage.Idle;
        ClearMarker();
        if (patient != null) Destroy(patient);
        patient = null;
        if (sensor != null) { sensor.Crashed -= OnCrash; Destroy(sensor); }
        sensor = null;
        ambulance = null;
        hudPanel.SetActive(false);
    }

    // ------------------------------------------------------------------ patient

    void SpawnPatient(Vector3 at)
    {
        if (patientPrefabs == null || patientPrefabs.Length == 0) return;
        var prefab = patientPrefabs[Random.Range(0, patientPrefabs.Length)];
        if (prefab == null) return;
        patient = Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        patientHead = patientPelvis = null;
        stoop = 0f;
        patient.name = "Patient";
        // Only a figure: the pedestrian's own walking and reactions are switched off.
        foreach (var behaviour in patient.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var c in patient.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in patient.GetComponentsInChildren<Rigidbody>(true)) b.isKinematic = true;
        var agent = patient.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        patientAnimator = patient.GetComponentInChildren<Animator>();
        if (patientAnimator != null)
        {
            patientAnimator.enabled = true;
            bool woman = prefab.name.StartsWith("Girl");
            var controller = woman ? womanAnimator : manAnimator;
            if (controller != null) patientAnimator.runtimeAnimatorController = controller;
            patientAnimator.applyRootMotion = false;
            patientAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        PlayPatient("idle2", "idle1");
    }

    void PlayPatient(params string[] states)
    {
        if (patientAnimator == null || patientAnimator.runtimeAnimatorController == null) return;
        foreach (var state in states)
        {
            int hash = Animator.StringToHash(state);
            if (patientAnimator.HasState(0, hash)) { patientAnimator.CrossFadeInFixedTime(hash, 0.2f, 0); return; }
        }
    }

    // Behind the rear doors (an ambulance is about 5.6 m long).
    static Vector3 RearOf(Transform vehicle)
    {
        return vehicle == null ? Vector3.zero : vehicle.position - vehicle.forward * 3.4f;
    }

    // ------------------------------------------------------------------ crashes

    void AttachSensor()
    {
        var body = ambulance.GetComponentInParent<Rigidbody>();
        if (body == null) return;
        sensor = body.gameObject.AddComponent<AmbulanceCrashSensor>();
        sensor.Crashed += OnCrash;
    }

    void OnCrash(float kmh)
    {
        if (stage != Stage.ToHospital && stage != Stage.Boarding) return;
        if (kmh < crashThresholdKmh) return;
        float hurt = Mathf.Min((kmh - crashThresholdKmh) * crashHurt, maxHurtPerHit);
        health -= hurt;
        if (hurt > 3f) Show("Careful! The patient is hurt  -" + Mathf.RoundToInt(hurt), new Color(1f, 0.55f, 0.3f), 1.6f);
    }

    // ------------------------------------------------------------------ places

    bool FindPatientSpot(Vector3 from, out Vector3 spot)
    {
        spot = Vector3.zero;
        if (spots == null || spots.spots == null) return false;
        var candidates = new List<Vector3>();
        foreach (var s in spots.spots)
        {
            if (s.kind != AmbientSpotKind.Talk) continue;
            float d = Vector3.Distance(s.position, from);
            if (d >= minDistance && d <= maxDistance) candidates.Add(s.position);
        }
        if (candidates.Count == 0) return false;
        spot = Ground(candidates[Random.Range(0, candidates.Count)]);
        return true;
    }

    // In front of the hospital (the building that wears the City Hospital facade; its front faces +X).
    static bool FindHospital(out Vector3 point)
    {
        point = Vector3.zero;
        Renderer best = null;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m == null || m.name.IndexOf("City Hospital", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (best == null || r.bounds.size.sqrMagnitude > best.bounds.size.sqrMagnitude) best = r;
        }
        if (best == null) return false;
        var b = best.bounds;
        Vector3 front = new Vector3(b.max.x + 14f, b.min.y + 2f, b.center.z);
        point = Ground(front);
        return true;
    }

    static Vector3 Ground(Vector3 near)
    {
        if (Physics.Raycast(near + Vector3.up * 3f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return near;
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    static IVehicleController PlayerVehicle(out PlayerVehicleInteraction interaction)
    {
        interaction = null;
        var player = GamePlayer.Current;
        if (player == null) return null;
        interaction = player.transform.GetComponent<PlayerVehicleInteraction>();
        return interaction != null && interaction.IsInVehicle ? interaction.CurrentVehicle : null;
    }

    static void SetMarker(Vector3 at)
    {
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.SetMarker(at);
    }

    static void ClearMarker()
    {
        if (WaypointSystem.Instance != null) WaypointSystem.Instance.ClearMarker();
    }

    // ------------------------------------------------------------------ HUD

    void UpdateHud()
    {
        float t = Mathf.Max(0f, timeLeft);
        timeText.text = Mathf.FloorToInt(t / 60f) + ":" + Mathf.FloorToInt(t % 60f).ToString("00");
        timeFill.fillAmount = Mathf.Clamp01(t / Mathf.Max(1f, timeGiven));
        timeFill.color = t < 15f ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.78f, 0.24f);
        float h = Mathf.Clamp01(health / 100f);
        healthFill.fillAmount = h;
        healthFill.color = Color.Lerp(new Color(1f, 0.25f, 0.25f), new Color(0.35f, 0.95f, 0.45f), h);
        healthLabel.text = "PATIENT  " + Mathf.CeilToInt(Mathf.Max(0f, health)) + "%";
    }

    void Show(string text, Color colour, float seconds = 3.5f)
    {
        messageText.text = text;
        messageText.color = colour;
        messageUntil = Time.unscaledTime + seconds;
        messageBox.SetActive(true);
    }

    void UpdateMessage()
    {
        if (messageBox.activeSelf && Time.unscaledTime > messageUntil) messageBox.SetActive(false);
    }

    void BuildHud()
    {
        var canvasObject = new GameObject("Ambulance Mission Canvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 140;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasObject.transform;

        // Mission panel, top centre.
        hudPanel = Box("Mission", root, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(560f, 128f), new Color(0.03f, 0.03f, 0.05f, 0.82f));
        var accent = Box("Accent", hudPanel.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 4f), new Color(1f, 0.25f, 0.25f, 1f));
        accent.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
        var title = Label("Title", hudPanel.transform, "+  PARAMEDIC", 20, FontStyle.Bold, new Vector2(20f, -14f), TextAnchor.UpperLeft);
        title.color = new Color(1f, 0.4f, 0.4f);
        objective = Label("Objective", hudPanel.transform, "", 22, FontStyle.Normal, new Vector2(20f, -40f), TextAnchor.UpperLeft);
        timeFill = Bar("Time", hudPanel.transform, new Vector2(20f, -78f), out timeText);
        healthFill = Bar("Health", hudPanel.transform, new Vector2(290f, -78f), out healthLabel);
        hudPanel.SetActive(false);

        // "Press E" prompt, top left under the health bar.
        promptBox = Box("Prompt", root, new Vector2(0f, 1f), new Vector2(36f, -96f), new Vector2(520f, 56f), new Color(0.03f, 0.03f, 0.04f, 0.82f));
        promptBox.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        promptText = Label("Text", promptBox.transform, "Press <color=#FFC83D><b> E </b></color> to start a paramedic mission", 24, FontStyle.Normal, new Vector2(20f, -14f), TextAnchor.UpperLeft);
        promptBox.SetActive(false);

        // Big message, upper middle.
        messageBox = new GameObject("Message", typeof(RectTransform));
        messageBox.transform.SetParent(root, false);
        var mr = messageBox.GetComponent<RectTransform>();
        mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0.72f);
        mr.sizeDelta = new Vector2(1400f, 80f);
        messageText = messageBox.AddComponent<Text>();
        messageText.font = font;
        messageText.fontSize = 40;
        messageText.fontStyle = FontStyle.Bold;
        messageText.alignment = TextAnchor.MiddleCenter;
        messageText.raycastTarget = false;
        var shadow = messageBox.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        messageBox.SetActive(false);
    }

    GameObject Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return go;
    }

    Text Label(string name, Transform parent, string text, int size, FontStyle style, Vector2 position, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(520f, 30f);
        var label = go.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = align;
        label.color = Color.white;
        label.supportRichText = true;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        return label;
    }

    // A bar 250 wide with a caption above it; returns the fill.
    Image Bar(string name, Transform parent, Vector2 position, out Text caption)
    {
        caption = Label(name + " Caption", parent, "", 15, FontStyle.Bold, position, TextAnchor.UpperLeft);
        caption.color = new Color(1f, 1f, 1f, 0.75f);
        var back = new GameObject(name + " Bar", typeof(RectTransform));
        back.transform.SetParent(parent, false);
        var rect = back.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position + new Vector2(0f, -22f);
        rect.sizeDelta = new Vector2(250f, 12f);
        back.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        var fillObject = new GameObject("Fill", typeof(RectTransform));
        fillObject.transform.SetParent(back.transform, false);
        var fr = fillObject.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        var fill = fillObject.AddComponent<Image>();
        fill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.raycastTarget = false;
        return fill;
    }
}
