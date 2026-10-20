using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Lets the player's car knock a traffic bike over. The traffic AI holds its vehicles still while it owns them,
// so the bike itself can't fall: on impact it is hidden and handed straight back to the traffic spawner (it
// reappears later as ordinary traffic), and in its place a loose wreck copy of the bike is thrown over by the
// hit while an identical ragdoll copy of the rider (same pose, bones mapped by their Bip01 names) is flung
// off. The wreck and the body are cleared away once the player has moved on and can't see them.
[RequireComponent(typeof(Rigidbody))]
public class BikeCrash : MonoBehaviour
{
    [Tooltip("Slowest hit by the player's car that knocks the bike over, m/s.")]
    public float minImpactSpeed = 1.5f;
    [Tooltip("Share of its speed the player's car keeps after hitting the bike.")]
    [Range(0f, 1f)] public float carCarryOn = 0.8f;
    [Tooltip("Weight of the thrown wreck, kg.")]
    public float wreckMass = 220f;
    public float riderMass = 70f;
    [Tooltip("Seconds before the wreck can be cleared away.")]
    public float clearDelay = 20f;
    [Tooltip("The wreck is only cleared when the camera is at least this far away and not looking at it.")]
    public float clearDistance = 40f;

    // True from the hit until the spawner hands the bike out again.
    public bool Crashed { get; private set; }

    private Rigidbody _body;
    private TSTrafficAI _ai;
    private Transform _visual, _model, _rider;
    private BoxCollider _crashBody;
    private static AudioClip _crashClip;
    private static PedestrianSpawner _pedestrians;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _ai = GetComponent<TSTrafficAI>();
        _visual = transform.Find("Visual");
        _model = transform.Find("Visual/Model");
        _rider = transform.Find("Visual/Rider");

        // The bike's own box is only a trigger for the traffic sensors; this solid body is what a car hits.
        // It starts above the wheels' contact with the road.
        var go = new GameObject("CrashBody");
        go.transform.SetParent(transform, false);
        _crashBody = go.AddComponent<BoxCollider>();
        _crashBody.center = new Vector3(0f, 0.85f, 0f);
        _crashBody.size = new Vector3(0.6f, 0.8f, 2.1f);
    }

    // Handed out again by the spawner: whole and visible.
    private void OnEnable()
    {
        Crashed = false;
        if (_visual != null) _visual.gameObject.SetActive(true);
        if (_crashBody != null) _crashBody.enabled = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Crashed) return;
        var other = collision.rigidbody;
        if (other == null || other.GetComponent<PlayerCarController>() == null) return;
        float impact = collision.relativeVelocity.magnitude;
        if (impact < minImpactSpeed) return;

        // The car's direction of travel (relativeVelocity's sign depends on which side reports it).
        Vector3 travel = collision.relativeVelocity;
        if (Vector3.Dot(travel, transform.position - other.position) < 0f) travel = -travel;
        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Crash(other, travel, impact, hitPoint);
    }

    public void Crash(Rigidbody car, Vector3 travel, float impact, Vector3 hitPoint)
    {
        if (Crashed || _model == null) return;
        Crashed = true;
        Vector3 bikeVelocity = _body.linearVelocity;

        // The car ploughs on rather than stopping dead against a 2.5 t traffic bike.
        if (car != null && car.linearVelocity.sqrMagnitude < (travel * carCarryOn).sqrMagnitude)
            car.linearVelocity = travel * carCarryOn;

        Vector3 push = new Vector3(travel.x, 0f, travel.z);
        Vector3 pushDir = push.sqrMagnitude > 0.01f ? push.normalized : -transform.right;

        var wreck = SpawnWreck(bikeVelocity, pushDir, impact);
        GameObject ragdoll = null;
        if (_rider != null && _rider.gameObject.activeInHierarchy)
            ragdoll = SpawnRagdoll(_rider, bikeVelocity, pushDir, impact);
        wreck.Begin(ragdoll, car, clearDelay, clearDistance);

        // The traffic bike itself goes: hidden now, and back to the spawner's pool.
        _visual.gameObject.SetActive(false);
        _crashBody.enabled = false;
        if (_ai != null) _ai.Disable(true, false);

        PlayCrash(hitPoint, impact);
        if (_pedestrians == null) _pedestrians = FindFirstObjectByType<PedestrianSpawner>();
        if (_pedestrians != null) _pedestrians.TriggerPanic(hitPoint, 18f);
    }

    // ---------- wreck ----------

    // A loose copy of the bike model, knocked along and over, away from the car.
    private BikeWreck SpawnWreck(Vector3 bikeVelocity, Vector3 pushDir, float impact)
    {
        var root = new GameObject("Bike wreck");
        root.transform.SetPositionAndRotation(transform.position, transform.rotation);
        var model = Instantiate(_model.gameObject, _model.position, _model.rotation, root.transform);
        model.name = _model.name;

        var box = root.AddComponent<BoxCollider>();
        box.center = _crashBody.center;
        box.size = _crashBody.size;
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = wreckMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxDepenetrationVelocity = 3f;
        rb.centerOfMass = box.center;   // a real bike's weight, in its middle, so it falls on its side and stays there

        rb.linearVelocity = bikeVelocity * 0.5f + pushDir * impact * 0.55f + Vector3.up * Mathf.Min(1f + impact * 0.08f, 3.5f);
        float side = Mathf.Sign(Vector3.Dot(pushDir, root.transform.right));
        if (side == 0f) side = 1f;
        rb.angularVelocity = root.transform.forward * -side * Mathf.Clamp(impact * 0.35f, 2.5f, 7f)
                             + Vector3.up * Random.Range(-1.5f, 1.5f);
        return root.AddComponent<BikeWreck>();
    }

    // ---------- ragdoll ----------

    private GameObject SpawnRagdoll(Transform rider, Vector3 bikeVelocity, Vector3 pushDir, float impact)
    {
        var copy = Instantiate(rider.gameObject, rider.position, rider.rotation);
        copy.name = "Crashed rider";
        copy.transform.localScale = rider.lossyScale;
        foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

        var bones = new Dictionary<string, Transform>();
        foreach (var t in copy.GetComponentsInChildren<Transform>()) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        System.Func<string, Transform> B = n => { Transform t; return bones.TryGetValue("Bip01 " + n, out t) ? t : null; };

        Transform pelvis = B("Pelvis"), chest = B("Spine1"), neck = B("Neck"), head = B("Head");
        if (pelvis == null || chest == null || head == null) { Destroy(copy); return null; }

        var bodies = new List<Rigidbody>();
        // Pelvis: a ball between the hips.
        Transform lThigh = B("L Thigh"), rThigh = B("R Thigh");
        Vector3 hips = lThigh != null && rThigh != null ? (lThigh.position + rThigh.position) * 0.5f : pelvis.position;
        var pelvisCol = pelvis.gameObject.AddComponent<SphereCollider>();
        pelvisCol.center = pelvis.InverseTransformPoint((hips + pelvis.position) * 0.5f);
        pelvisCol.radius = 0.15f / Scale(pelvis);
        bodies.Add(AddBody(pelvis, riderMass * 0.17f));

        AddLimb(chest, neck != null ? neck : head, 0.15f, 0.2f, riderMass * 0.2f, 40f, bodies);

        var headCol = head.gameObject.AddComponent<SphereCollider>();
        Vector3 up = neck != null ? (head.position - neck.position).normalized : rider.up;
        headCol.center = head.InverseTransformPoint(head.position + up * 0.08f);
        headCol.radius = 0.11f / Scale(head);
        bodies.Add(AddBody(head, riderMass * 0.07f));
        AddJoint(head, 30f);

        foreach (string s in new[] { "L", "R" })
        {
            AddLimb(B(s + " Thigh"), B(s + " Calf"), 0.085f, 0f, riderMass * 0.1f, 50f, bodies);
            AddLimb(B(s + " Calf"), B(s + " Foot"), 0.06f, 0.25f, riderMass * 0.05f, 70f, bodies);
            AddLimb(B(s + " UpperArm"), B(s + " Forearm"), 0.05f, 0f, riderMass * 0.03f, 60f, bodies);
            AddLimb(B(s + " Forearm"), B(s + " Hand"), 0.045f, 0.35f, riderMass * 0.02f, 70f, bodies);
        }

        // Thrown off with the bike's speed plus the hit, up and over.
        Vector3 launch = bikeVelocity * 0.6f + pushDir * impact * 0.6f + Vector3.up * Mathf.Clamp(2f + impact * 0.12f, 2f, 6f);
        foreach (var rb in bodies)
        {
            rb.linearVelocity = launch + Random.insideUnitSphere * 0.6f;
            rb.angularVelocity = Random.insideUnitSphere * Mathf.Clamp(impact * 0.3f, 1f, 6f);
        }

        return copy;
    }

    private static float Scale(Transform t)
    {
        float s = t.lossyScale.x;
        return Mathf.Abs(s) > 1e-4f ? Mathf.Abs(s) : 1f;
    }

    private static Rigidbody AddBody(Transform bone, float mass)
    {
        var rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.maxDepenetrationVelocity = 3f;
        rb.angularDamping = 0.5f;
        return rb;
    }

    // A capsule from bone to child (plus a bit beyond for hands and feet), a body, and a joint to its parent.
    private static void AddLimb(Transform bone, Transform child, float radius, float extend, float mass, float swing, List<Rigidbody> bodies)
    {
        if (bone == null || child == null) return;
        Vector3 d = bone.InverseTransformPoint(child.position);
        if (d.sqrMagnitude < 1e-6f) return;
        int axis = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (Mathf.Abs(d.x) > Mathf.Abs(d.z) ? 0 : 2) : (Mathf.Abs(d.y) > Mathf.Abs(d.z) ? 1 : 2);
        float length = d.magnitude * (1f + extend);
        float r = radius / Scale(bone);
        var cap = bone.gameObject.AddComponent<CapsuleCollider>();
        cap.direction = axis;
        cap.radius = r;
        cap.height = Mathf.Max(length, r * 2f);
        cap.center = d.normalized * length * 0.5f;
        bodies.Add(AddBody(bone, mass));
        AddJoint(bone, swing, d.normalized);
    }

    private static void AddJoint(Transform bone, float swing, Vector3? along = null)
    {
        Rigidbody parent = null;
        for (var t = bone.parent; t != null && parent == null; t = t.parent) parent = t.GetComponent<Rigidbody>();
        if (parent == null) return;
        var j = bone.gameObject.AddComponent<CharacterJoint>();
        j.connectedBody = parent;
        Vector3 twist = along ?? Vector3.right;
        j.axis = twist;
        j.swingAxis = Vector3.Cross(twist, Mathf.Abs(Vector3.Dot(twist, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        j.lowTwistLimit = new SoftJointLimit { limit = -25f };
        j.highTwistLimit = new SoftJointLimit { limit = 25f };
        j.swing1Limit = new SoftJointLimit { limit = swing };
        j.swing2Limit = new SoftJointLimit { limit = swing * 0.6f };
        j.enableProjection = true;
    }

    // ---------- sound ----------

    private void PlayCrash(Vector3 at, float impact)
    {
        if (_crashClip == null) _crashClip = BuildCrashClip();
        AudioSource.PlayClipAtPoint(_crashClip, at, Mathf.Clamp01(0.5f + impact / 20f));
    }

    // Thump of the hit, then metal scraping along the road, made in code.
    private static AudioClip BuildCrashClip()
    {
        const int rate = 22050;
        int n = (int)(rate * 1.6f);
        var data = new float[n];
        var rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.08f;
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f);
            float crunch = noise * Mathf.Exp(-t * 18f);
            float scrape = (noise - low) * 0.35f * Mathf.Clamp01(t * 6f) * Mathf.Exp(-t * 2.2f)
                           * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 23f * t));
            data[i] = Mathf.Clamp((thump * 0.9f + crunch * 0.6f + scrape) * 0.8f, -1f, 1f);
        }
        var clip = AudioClip.Create("BikeCrash", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

// The thrown bike and its rider's body after a crash. Keeps them from exploding apart at the start (the body
// begins sitting on the bike, touching the car), and removes both once the player has moved on and can't see.
public class BikeWreck : MonoBehaviour
{
    private GameObject _ragdoll;
    private float _clearDistance;

    public void Begin(GameObject ragdoll, Rigidbody car, float clearDelay, float clearDistance)
    {
        _ragdoll = ragdoll;
        _clearDistance = clearDistance;
        var wreck = GetComponentsInChildren<Collider>();
        var body = ragdoll != null ? ragdoll.GetComponentsInChildren<Collider>() : new Collider[0];
        var carColliders = car != null ? car.GetComponentsInChildren<Collider>() : new Collider[0];
        foreach (var b in body) foreach (var w in wreck) Physics.IgnoreCollision(b, w, true);
        StartCoroutine(IgnoreBriefly(body, carColliders, 0.3f));
        StartCoroutine(IgnoreBriefly(wreck, carColliders, 0.15f));
        StartCoroutine(ClearWhenUnseen(clearDelay));
    }

    private static IEnumerator IgnoreBriefly(Collider[] a, Collider[] b, float seconds)
    {
        foreach (var x in a) foreach (var y in b) if (x != null && y != null && !y.isTrigger) Physics.IgnoreCollision(x, y, true);
        yield return new WaitForSeconds(seconds);
        foreach (var x in a) foreach (var y in b) if (x != null && y != null && !y.isTrigger) Physics.IgnoreCollision(x, y, false);
    }

    private IEnumerator ClearWhenUnseen(float delay)
    {
        yield return new WaitForSeconds(delay);
        var wait = new WaitForSeconds(1f);
        while (!CanClear()) yield return wait;
        if (_ragdoll != null) Destroy(_ragdoll);
        Destroy(gameObject);
    }

    private bool CanClear()
    {
        var cam = Camera.main;
        if (cam == null) return true;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        Vector3 eye = cam.transform.position;
        var body = _ragdoll != null ? _ragdoll.GetComponentInChildren<Rigidbody>() : null;
        foreach (var at in new[] { transform.position, body != null ? body.position : transform.position })
        {
            Vector3 p = at + Vector3.up * 0.5f;
            if ((p - eye).sqrMagnitude < _clearDistance * _clearDistance) return false;
            if (GeometryUtility.TestPlanesAABB(planes, new Bounds(p, new Vector3(3f, 2f, 3f))) && !Hidden(eye, p))
                return false;
        }
        return true;
    }

    // Something solid (a building, another car) between the camera and the spot, not the wreck or body itself.
    private bool Hidden(Vector3 eye, Vector3 p)
    {
        Vector3 d = p - eye;
        foreach (var hit in Physics.RaycastAll(eye, d.normalized, d.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            var t = hit.collider.transform;
            if (t.IsChildOf(transform) || (_ragdoll != null && t.IsChildOf(_ragdoll.transform))) continue;
            return true;
        }
        return false;
    }
}
