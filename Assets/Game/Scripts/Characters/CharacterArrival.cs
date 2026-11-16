using System.Collections.Generic;
using Invector.vCharacterController;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public enum ArrivalActivity { Standing, Walking, Running, Sitting, Driving }

// Where a character is found when the player switches to them, and what they are doing there.
public struct ArrivalPlan
{
    public ArrivalActivity activity;
    public Vector3 position;
    public float yaw;
    public Vector3 seat;
    public RCCVehicleAdapter car;

    public override string ToString() => activity + " at " + position.ToString("F0");
}

// Runs what the character is doing when the camera comes down on them, until the player takes over: walking on along the
// pavement, sitting on the bench until they get up, or getting into the car.
[DisallowMultipleComponent]
public class CharacterArrival : MonoBehaviour
{
    // Picks somewhere in the city, away from where the camera leaves, for one of the activities.
    public static ArrivalPlan Choose(AmbientSpotData spots, Vector3 awayFrom, float minDistance, float maxDistance)
    {
        // Weights: standing, walking, running, sitting, driving.
        float[] weights = { 0.18f, 0.27f, 0.17f, 0.2f, 0.18f };
        float roll = Random.value * Sum(weights);
        var activity = ArrivalActivity.Standing;
        for (int i = 0; i < weights.Length; i++)
        {
            if (roll < weights[i]) { activity = (ArrivalActivity)i; break; }
            roll -= weights[i];
        }

        ArrivalPlan plan;
        if (activity == ArrivalActivity.Driving && TryCar(awayFrom, minDistance, out plan)) return plan;
        if (activity == ArrivalActivity.Sitting && TrySpot(spots, AmbientSpotKind.Bench, awayFrom, minDistance, maxDistance, out plan)) return plan;
        if (activity == ArrivalActivity.Driving || activity == ArrivalActivity.Sitting) activity = ArrivalActivity.Walking;
        if (!TrySpot(spots, AmbientSpotKind.Talk, awayFrom, minDistance, maxDistance, out plan))
        {
            plan = new ArrivalPlan { activity = ArrivalActivity.Standing, position = awayFrom, yaw = Random.Range(0f, 360f) };
            return plan;
        }
        plan.activity = activity;
        if (activity != ArrivalActivity.Standing)
        {
            if (FindClearWay(plan.position, activity == ArrivalActivity.Running ? 16f : 9f, out float yaw)) plan.yaw = yaw;
            else plan.activity = ArrivalActivity.Standing;
        }
        return plan;
    }

    static float Sum(float[] values)
    {
        float s = 0f;
        foreach (var v in values) s += v;
        return s;
    }

    static bool TrySpot(AmbientSpotData spots, AmbientSpotKind kind, Vector3 awayFrom, float min, float max, out ArrivalPlan plan)
    {
        plan = default;
        if (spots == null || spots.spots == null) return false;
        var list = new List<int>();
        for (int i = 0; i < spots.spots.Length; i++)
        {
            var s = spots.spots[i];
            if (s.kind != kind) continue;
            float d = Vector3.Distance(s.position, awayFrom);
            if (d >= min && d <= max) list.Add(i);
        }
        if (list.Count == 0) return false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var spot = spots.spots[list[Random.Range(0, list.Count)]];
            if (kind == AmbientSpotKind.Bench)
            {
                Vector3 ground = spot.position - Vector3.up * spot.seatHeight;
                if (Occupied(spot.position + Vector3.up * 0.75f, 0.3f)) continue;
                plan = new ArrivalPlan { activity = ArrivalActivity.Sitting, position = ground, seat = spot.position, yaw = spot.yaw };
                return true;
            }
            Vector3 position = spot.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, 1.5f);
            if (!Ground(position, out position) || Occupied(position + Vector3.up * 1.1f, 0.4f)) continue;
            plan = new ArrivalPlan { activity = ArrivalActivity.Standing, position = position, yaw = Random.Range(0f, 360f) };
            return true;
        }
        return false;
    }

    static bool TryCar(Vector3 awayFrom, float min, out ArrivalPlan plan)
    {
        plan = default;
        var cars = new List<RCCVehicleAdapter>();
        foreach (var car in FindObjectsByType<RCCVehicleAdapter>(FindObjectsSortMode.None))
            if (car.CanEnter && car.HasEntrySetup && !car.IsDormant && Vector3.Distance(car.transform.position, awayFrom) >= min) cars.Add(car);
        if (cars.Count == 0) return false;
        var chosen = cars[Random.Range(0, cars.Count)];
        var enter = chosen.GetEnterPoint();
        if (!Ground(enter.position, out Vector3 at)) at = enter.position;
        plan = new ArrivalPlan { activity = ArrivalActivity.Driving, position = at, yaw = enter.eulerAngles.y, car = chosen };
        return true;
    }

    static bool Ground(Vector3 near, out Vector3 ground)
    {
        ground = near;
        if (!Physics.Raycast(near + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
        ground = hit.point;
        return Vector3.Angle(hit.normal, Vector3.up) < 25f;
    }

    static bool Occupied(Vector3 point, float radius) => Physics.CheckSphere(point, radius, ~0, QueryTriggerInteraction.Ignore);

    // A direction with flat, open ground ahead for a while.
    static bool FindClearWay(Vector3 from, float length, out float yaw)
    {
        float start = Random.Range(0f, 360f);
        for (int i = 0; i < 12; i++)
        {
            yaw = start + i * 30f;
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            if (Physics.SphereCast(from + Vector3.up * 1f, 0.35f, dir, out _, length, ~0, QueryTriggerInteraction.Ignore)) continue;
            bool flat = true;
            for (float d = 3f; d <= length && flat; d += 3f)
                flat = Ground(from + dir * d, out var g) && Mathf.Abs(g.y - from.y) < 0.6f;
            if (flat) return true;
        }
        yaw = 0f;
        return false;
    }

    // ---------------------------------------------------------------------------------------------------------------

    public ArrivalPlan Plan { get; private set; }
    public bool Ready => Plan.activity != ArrivalActivity.Driving || (vehicles != null && vehicles.IsInVehicle) || Time.time > giveUpAt;

    vThirdPersonController controller;
    vThirdPersonInput input;
    OpenworldCrime.Player.PlayerManager manager;
    OpenworldCrime.Player.PlayerVehicleInteraction vehicles;
    Rigidbody body;
    CapsuleCollider capsule;
    Animator animator;
    PlayableGraph sitGraph;
    bool released, seated, fitted;
    int fitFrame;
    float releasedAt, giveUpAt;
    Vector3 heading;

    public static CharacterArrival Begin(PlayableCharacter character, ArrivalPlan plan)
    {
        var arrival = character.GetComponent<CharacterArrival>();
        if (arrival == null) arrival = character.gameObject.AddComponent<CharacterArrival>();
        arrival.Run(plan);
        return arrival;
    }

    void Run(ArrivalPlan plan)
    {
        Plan = plan;
        controller = GetComponent<vThirdPersonController>();
        input = GetComponent<vThirdPersonInput>();
        manager = GetComponent<OpenworldCrime.Player.PlayerManager>();
        vehicles = GetComponent<OpenworldCrime.Player.PlayerVehicleInteraction>();
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        animator = GetComponent<Animator>();
        released = seated = fitted = false;
        heading = Quaternion.Euler(0f, plan.yaw, 0f) * Vector3.forward;

        var player = GamePlayer.Current;
        player.Teleport(plan.position, plan.yaw);
        player.ResetLook();

        switch (plan.activity)
        {
            case ArrivalActivity.Walking:
            case ArrivalActivity.Running:
                if (input != null) input.SetLockUpdateMoveDirection(true);
                break;
            case ArrivalActivity.Sitting:
                SitDown();
                break;
            case ArrivalActivity.Driving:
                giveUpAt = Time.time + 9f;
                player.SetControl(true);
                if (plan.car == null || !vehicles.RequestEnter(plan.car)) giveUpAt = 0f;
                player.SetControl(false);
                break;
        }
    }

    // The camera has landed: the player is in charge, though the character carries on until told otherwise.
    public void Release()
    {
        released = true;
        releasedAt = Time.time;
        if (Plan.activity == ArrivalActivity.Driving || Plan.activity == ArrivalActivity.Standing) Finish();
    }

    void LateUpdate()
    {
        switch (Plan.activity)
        {
            case ArrivalActivity.Walking:
            case ArrivalActivity.Running:
                KeepMoving();
                break;
            case ArrivalActivity.Sitting:
                KeepSitting();
                break;
        }
    }

    void KeepMoving()
    {
        if (released && (GameInput.Move.sqrMagnitude > 0.01f || Time.time - releasedAt > 2.5f)) { Finish(); return; }
        // Turn away from anything in the way.
        if (Physics.SphereCast(transform.position + Vector3.up, 0.3f, heading, out _, 2.2f, ~0 & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
        {
            var left = Quaternion.Euler(0f, -60f, 0f) * heading;
            var right = Quaternion.Euler(0f, 60f, 0f) * heading;
            heading = !Physics.Raycast(transform.position + Vector3.up, left, 3f, ~0 & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore) ? left : right;
        }
        controller.MoveToPosition(transform.position + heading * 10f);
        controller.input = controller.input.normalized * (Plan.activity == ArrivalActivity.Walking ? 0.5f : 1f);
        controller.isSprinting = false;
    }

    void SitDown()
    {
        var clip = GetComponent<PlayableCharacter>().sitClip;
        if (clip == null || animator == null) { Plan = WithActivity(ArrivalActivity.Standing); return; }
        seated = true;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        capsule.enabled = false;
        controller.enabled = false;
        if (input != null) input.enabled = false;
        sitGraph = PlayableGraph.Create(name + " Sit");
        sitGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(sitGraph, "Sit", animator);
        var playable = AnimationClipPlayable.Create(sitGraph, clip);
        playable.SetTime(Random.Range(0f, clip.length));
        output.SetSourcePlayable(playable);
        sitGraph.Play();
        fitFrame = Time.frameCount + 1;
    }

    void KeepSitting()
    {
        if (!seated) return;
        // Once the pose is on, the hips are put on the seat.
        if (!fitted && Time.frameCount >= fitFrame)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                Vector3 wanted = Plan.seat + Vector3.up * 0.1f;
                transform.position += wanted - hips.position;
                fitted = true;
            }
        }
        if (released && (GameInput.Move.sqrMagnitude > 0.01f || GameInput.JumpDown)) StandUp();
    }

    void StandUp()
    {
        seated = false;
        if (sitGraph.IsValid()) sitGraph.Destroy();
        Vector3 front = Plan.position + heading * 0.6f;
        if (Ground(front, out var g)) front = g;
        transform.SetPositionAndRotation(front, Quaternion.LookRotation(heading));
        body.isKinematic = false;
        body.position = front;
        capsule.enabled = true;
        controller.enabled = true;
        if (input != null) input.enabled = true;
        Physics.SyncTransforms();
        Finish();
    }

    ArrivalPlan WithActivity(ArrivalActivity activity)
    {
        var plan = Plan;
        plan.activity = activity;
        return plan;
    }

    void Finish()
    {
        if (seated) { StandUp(); return; }
        if (input != null) input.SetLockUpdateMoveDirection(false);
        Destroy(this);
    }

    // Switched away (or the character is removed) while still doing something.
    void OnDisable()
    {
        if (seated)
        {
            seated = false;
            if (sitGraph.IsValid()) sitGraph.Destroy();
            if (body != null) body.isKinematic = false;
            if (capsule != null) capsule.enabled = true;
            if (controller != null) controller.enabled = true;
            if (input != null) input.enabled = true;
        }
        if (input != null) input.SetLockUpdateMoveDirection(false);
        Destroy(this);
    }
}
