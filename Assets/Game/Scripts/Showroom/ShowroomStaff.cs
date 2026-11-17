using UnityEngine;

// A showroom employee: sits at a desk, stands at a post, or walks the floor between the cars, with small idle, listening and
// talking moves now and then. Only a figure (the pedestrian scripts are stripped when the showroom is built), with a solid
// body so the player walks round them.
public class ShowroomStaff : MonoBehaviour
{
    public enum Duty { Sit, Stand, Patrol }

    public Duty duty = Duty.Stand;
    [Tooltip("Sit: the middle of the chair's seat. Stand: where they stand. Patrol: the stops, in order.")]
    public Vector3[] points = new Vector3[1];
    [Tooltip("The way they face while sitting or standing.")]
    public float facingYaw;
    public float walkSpeed = 1.15f;
    [Tooltip("How long they stay at each stop when walking the floor.")]
    public Vector2 pauseSeconds = new Vector2(5f, 9f);
    [Tooltip("The hip bone above the seat when sitting.")]
    public float hipsAboveSeat = 0.12f;

    static readonly string[] StandMoves = { "idle1", "idle2", "listen", "idle1", "talk1", "idle2", "talk2" };
    static readonly string[] SitMoves = { "sit_a", "sit_b", "sit_c" };

    Animator animator;
    Transform hips;
    Vector3 seatHips;
    int stop;
    float waitUntil, nextMoveAt;
    bool walking;
    int sitAligned;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        hips = FindHips(transform);

        switch (duty)
        {
            case Duty.Sit:
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);
                seatHips = points[0] + Vector3.up * hipsAboveSeat;
                transform.position = points[0];
                Play(SitMoves[Random.Range(0, SitMoves.Length)], 0f);
                break;
            case Duty.Stand:
                transform.SetPositionAndRotation(points[0], Quaternion.Euler(0f, facingYaw, 0f));
                Play(StandMoves[Random.Range(0, StandMoves.Length)], 0f);
                nextMoveAt = Time.time + Random.Range(4f, 9f);
                break;
            case Duty.Patrol:
                stop = Random.Range(0, points.Length);
                transform.position = points[stop];
                Arrive();
                break;
        }
    }

    void Update()
    {
        if (duty == Duty.Stand && Time.time > nextMoveAt)
        {
            Play(StandMoves[Random.Range(0, StandMoves.Length)], 0.4f);
            nextMoveAt = Time.time + Random.Range(5f, 10f);
        }
        if (duty != Duty.Patrol) return;

        if (!walking)
        {
            if (Time.time < waitUntil) return;
            stop = (stop + 1) % points.Length;
            walking = true;
            Play("walk", 0.3f);
        }
        Vector3 to = points[stop] - transform.position;
        to.y = 0f;
        if (to.magnitude < 0.08f) { Arrive(); return; }
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-6f * Time.deltaTime));
        transform.position = Vector3.MoveTowards(transform.position, new Vector3(points[stop].x, points[stop].y, points[stop].z), walkSpeed * Time.deltaTime);
    }

    // Seated: the sitting clip puts the hips wherever its root is, so the body is moved until the hips are on the seat.
    void LateUpdate()
    {
        if (duty != Duty.Sit || hips == null || sitAligned > 30) return;
        transform.position += seatHips - hips.position;
        sitAligned++;
    }

    void Arrive()
    {
        walking = false;
        waitUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
        Play(Random.value < 0.5f ? "idle1" : "listen", 0.3f);
    }

    void Play(string state, float fade)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        int hash = Animator.StringToHash(state);
        if (!animator.HasState(0, hash)) hash = Animator.StringToHash("idle1");
        animator.CrossFadeInFixedTime(hash, fade, 0, Random.Range(0f, 2f));
    }

    static Transform FindHips(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.EndsWith("hips") || n.EndsWith("pelvis")) return t;
        }
        return null;
    }
}
