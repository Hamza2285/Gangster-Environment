using OpenworldCrime.Pedestrians;
using UnityEngine;
using UnityEngine.AI;

public class PedestrianWalker : MonoBehaviour
{
    // Ground speeds the clips were authored for; playback is scaled to match actual movement.
    const float WalkClipSpeed = 1.3f;
    const float RunClipSpeed = 4f;
    // A walker held up by a vehicle on its path picks a new route after this long.
    const float BlockedRepathTime = 0.35f;

    // Down: knocked over or dead (PedestrianDamage drives the ragdoll; the walker does not move the body).
    public enum Mode { Lane, Fleeing, Returning, Down }

    public Mode State { get; private set; }
    /// <summary>True while a scripted scene owns this person (a traffic driver being pulled out of a car): the walker leaves
    /// the body and its Animator alone and the spawner never recycles it.</summary>
    public bool IsHeld { get; private set; }
    public int Lane { get; private set; }
    public float Distance { get; private set; }
    public float GapAhead { get; set; }
    public bool WantsRecycle { get; private set; }
    public Renderer[] Renderers { get; private set; }

    /// <summary>Where the body actually is (the ragdoll hips while knocked down; they can be thrown away from the root).</summary>
    public Vector3 BodyPosition
    {
        get { return State == Mode.Down && _damage != null && _damage.IsDown ? _damage.BodyPosition : transform.position; }
    }

    private FootpathLaneNetwork _network;
    private PedestrianSpawner _owner;
    private Animator _animator;
    private NavMeshAgent _agent;
    private float _speed;
    private string _animState;
    private bool _castShadows = true;
    private int _laneLayer;
    private string _laneTag;

    private Vector3 _threat;
    private float _panicUntil;
    private int _returnLane;
    private float _returnDistance;
    private float _stuckTime;
    private float _blockedTime;

    private PedestrianDamage _damage;
    private bool _dead;
    private float _corpseUntil;

    public void Setup(FootpathLaneNetwork network, PedestrianSpawner owner, NavMeshAgent agent, PedestrianDamage damage)
    {
        _network = network;
        _owner = owner;
        _agent = agent;
        _agent.enabled = false;
        // The walker moves the transform itself (FollowAgent), so a vehicle in the way can hold it back.
        _agent.updatePosition = false;
        _damage = damage;
        if (_damage != null)
        {
            _damage.OnKnockedDown += HandleKnockedDown;
            _damage.OnDied += HandleDied;
            _damage.OnRecovered += HandleRecovered;
            _damage.OnHurt += HandleHurt;
        }
        _laneLayer = gameObject.layer;
        _laneTag = gameObject.tag;
        _animator = GetComponentInChildren<Animator>();
        Renderers = GetComponentsInChildren<Renderer>(true);
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
            // Pooled people are switched off and on all the time; keeping the Animator's graph avoids rebuilding it
            // (Animator.Initialize / avatar setup) on every spawn. Place() plays the walk state anyway.
            _animator.keepAnimatorStateOnDisable = true;
        }
    }

    public void Place(int lane, float distance, float speed)
    {
        LeaveNavMesh();
        State = Mode.Lane;
        _dead = false;
        Lane = lane;
        Distance = distance;
        _speed = speed;
        GapAhead = float.MaxValue;
        WantsRecycle = false;

        Vector3 forward;
        Vector3 position = _network.Evaluate(lane, distance, out forward);
        forward.y = 0f;
        transform.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward) : transform.rotation);
        _animState = null;
        PlayState("walk", Random.value);
    }

    // Returns the lane the walker was on before this step (differs from Lane when it moved onto another lane).
    public int Advance(float dt)
    {
        int previousLane = Lane;
        float speed = _speed * Mathf.Clamp01((GapAhead - 0.9f) / 1.2f);
        bool idle = speed < 0.15f;
        PlayState(idle ? "idle1" : "walk", Random.value);
        if (_animator != null) _animator.speed = idle ? 1f : Mathf.Clamp(speed / WalkClipSpeed, 0.5f, 1.4f);

        FootpathLane lane = _network.lanes[Lane];
        float distance = Distance + speed * dt;
        if (distance >= lane.Length)
        {
            if (lane.loop)
            {
                distance -= lane.Length;
            }
            else if (lane.nextLane >= 0)
            {
                distance = lane.nextDistance + (distance - lane.Length);
                Lane = lane.nextLane;
                if (distance >= _network.lanes[Lane].Length) distance = 0f;
            }
            else
            {
                distance = lane.Length;
                WantsRecycle = true;
            }
        }
        Distance = distance;

        Vector3 forward;
        Vector3 target = _network.Evaluate(Lane, Distance, out forward);
        Vector3 current = transform.position;
        // Glide across the short hop onto a connecting lane instead of snapping.
        Vector3 position = (target - current).sqrMagnitude > 0.25f
            ? Vector3.MoveTowards(current, target, Mathf.Max(speed, 1.5f) * 2.5f * dt)
            : target;

        forward.y = 0f;
        Quaternion rotation = transform.rotation;
        if (forward.sqrMagnitude > 1e-4f)
            rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(forward), 1f - Mathf.Exp(-8f * dt));
        transform.SetPositionAndRotation(position, rotation);

        return previousLane;
    }

    // Leaves the lane and runs away from the threat across any walkable surface. Returns false if no NavMesh here.
    public bool StartPanic(Vector3 threat, float duration, float runSpeed)
    {
        if (State == Mode.Down) return false;
        _threat = threat;
        _panicUntil = Time.time + duration;
        if (State == Mode.Fleeing) return true;

        if (!EnterNavMesh()) return false;
        State = Mode.Fleeing;
        _agent.speed = runSpeed;
        _agent.acceleration = 12f;
        _agent.angularSpeed = 540f;
        // The traffic AI brakes for anything on its player layer with the player tag.
        gameObject.layer = _owner.CarSensorLayer;
        gameObject.tag = "Player";
        PlayState("run", Random.value);
        PickFleeTarget();
        return true;
    }

    public void TickOffLane(float dt, Vector3 playerPosition, float calmDistance)
    {
        if (State == Mode.Down)
        {
            // Corpses are cleared after the spawner's corpse lifetime; knocked-down survivors wait for PedestrianDamage.
            if (_dead && Time.time >= _corpseUntil) WantsRecycle = true;
            return;
        }

        if (!_agent.enabled || !_agent.isOnNavMesh)
        {
            WantsRecycle = true;
            return;
        }

        FollowAgent(dt);
        if (_blockedTime > BlockedRepathTime)
        {
            _blockedTime = 0f;
            if (State == Mode.Fleeing) PickFleeTarget();
            else BeginReturn();
            return;
        }

        float v = _agent.velocity.magnitude;
        _stuckTime = v < 0.1f && !_agent.pathPending ? _stuckTime + dt : 0f;

        if (State == Mode.Fleeing)
        {
            if (_animator != null) _animator.speed = Mathf.Clamp(v / RunClipSpeed, 0.6f, 1.4f);

            Vector3 away = transform.position - playerPosition;
            away.y = 0f;
            bool playerClose = away.sqrMagnitude < calmDistance * calmDistance;
            if (playerClose) { _threat = playerPosition; _panicUntil = Mathf.Max(_panicUntil, Time.time + 2f); }

            if (Time.time >= _panicUntil && !playerClose)
            {
                BeginReturn();
                return;
            }
            if ((!_agent.pathPending && _agent.remainingDistance < 1.5f) || _stuckTime > 1.5f)
                PickFleeTarget();
        }
        else if (State == Mode.Returning)
        {
            PlayState("walk", Random.value);
            if (_animator != null) _animator.speed = Mathf.Clamp(v / WalkClipSpeed, 0.5f, 1.4f);

            if (!_agent.pathPending && _agent.remainingDistance < 0.4f)
            {
                RejoinLane();
                return;
            }
            if (_stuckTime > 4f) BeginReturn();
        }
    }

    /// <summary>Hands the person to a scripted scene (see <see cref="IsHeld"/>): off any lane / NavMesh, standing still.</summary>
    public void Hold()
    {
        LeaveNavMesh();
        State = Mode.Down;
        _dead = false;
        WantsRecycle = false;
        IsHeld = true;
        _animState = null;
    }

    /// <summary>
    /// Gives the person back after a scripted scene. Still standing (no knock-down / death in progress): it runs from the threat
    /// at once. Otherwise PedestrianDamage brings it back (gets up and runs, or stays a corpse).
    /// </summary>
    public void Release(Vector3 threat)
    {
        IsHeld = false;
        _animState = null;
        if (_damage == null || (!_damage.IsDown && !_damage.IsDead)) _owner.OnWalkerRecovered(this, threat);
    }

    public void ForceCalm()
    {
        if (State == Mode.Fleeing) _panicUntil = 0f;
    }

    public void OnDespawned()
    {
        LeaveNavMesh();
        IsHeld = false;
        State = Mode.Lane;
        _dead = false;
        _blockedTime = 0f;
        if (_damage != null) _damage.ResetForReuse();
    }

    /// <summary>A knocked-down survivor is back on its feet: it runs from the threat. Returns false (recycled) without NavMesh.</summary>
    public bool Recover(Vector3 threat, float duration, float runSpeed)
    {
        if (State != Mode.Down) return false;
        State = Mode.Returning;
        _animState = null;
        if (StartPanic(threat, duration, runSpeed)) return true;
        State = Mode.Down;
        WantsRecycle = true;
        return false;
    }

    private void EnterDown()
    {
        if (State == Mode.Down) return;
        Mode before = State;
        LeaveNavMesh();
        State = Mode.Down;
        _owner.OnWalkerDowned(this, before);
    }

    private void HandleKnockedDown(Vector3 threat)
    {
        EnterDown();
    }

    private void HandleDied(Vector3 threat)
    {
        EnterDown();
        _dead = true;
        _corpseUntil = Time.time + _owner.corpseLifetime;
    }

    private void HandleRecovered(Vector3 threat)
    {
        _owner.OnWalkerRecovered(this, threat);
    }

    private void HandleHurt(Vector3 threat)
    {
        if (State != Mode.Down) _owner.OnWalkerHurt(this, threat);
    }

    private void OnDestroy()
    {
        if (_damage == null) return;
        _damage.OnKnockedDown -= HandleKnockedDown;
        _damage.OnDied -= HandleDied;
        _damage.OnRecovered -= HandleRecovered;
        _damage.OnHurt -= HandleHurt;
    }

    // The agent plans the route; the walker only moves there when no vehicle stands in the way (never walks into cars).
    private void FollowAgent(float dt)
    {
        Vector3 next = _agent.nextPosition;
        if (_owner.IsBlockedByVehicle(transform.position, next))
        {
            _agent.nextPosition = transform.position;
            _blockedTime += dt;
            return;
        }
        transform.position = next;
        _blockedTime = 0f;
    }

    private void PickFleeTarget()
    {
        _stuckTime = 0f;
        Vector3 away = transform.position - _threat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;
        away.Normalize();

        var path = new NavMeshPath();
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-60f, 60f) * (1f + i * 0.25f), 0f) * away;
            Vector3 candidate = transform.position + dir * Random.Range(18f, 35f);
            Vector3 point;
            if (!_owner.TrySampleOpenNavPoint(candidate, 6f, out point)) continue;
            if (!_agent.CalculatePath(point, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            _agent.SetPath(path);
            return;
        }
        // Nowhere better found: keep moving directly away as far as the mesh allows.
        _agent.SetDestination(transform.position + away * 10f);
    }

    private void BeginReturn()
    {
        int lane;
        float distance;
        Vector3 point;
        if (!_owner.TryFindNearestLanePoint(transform.position, _agent, out lane, out distance, out point))
        {
            WantsRecycle = true;
            return;
        }
        State = Mode.Returning;
        _returnLane = lane;
        _returnDistance = distance;
        _stuckTime = 0f;
        _agent.speed = _speed;
        _agent.acceleration = 6f;
        gameObject.layer = _laneLayer;
        gameObject.tag = _laneTag;
        _agent.SetDestination(point);
    }

    private void RejoinLane()
    {
        State = Mode.Lane;
        Lane = _returnLane;
        Distance = _returnDistance;
        GapAhead = float.MaxValue;
        LeaveNavMesh();
        _owner.OnRejoinedLane(this);
    }

    private bool EnterNavMesh()
    {
        NavMeshHit hit;
        if (!_owner.EnsurePanicNavMesh() || !NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas)) return false;
        _agent.enabled = true;
        if (!_agent.Warp(hit.position))
        {
            _agent.enabled = false;
            return false;
        }
        transform.position = hit.position;
        _stuckTime = 0f;
        _blockedTime = 0f;
        return true;
    }

    private void LeaveNavMesh()
    {
        if (_agent != null && _agent.enabled)
        {
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.enabled = false;
        }
        gameObject.layer = _laneLayer;
        gameObject.tag = _laneTag;
    }

    public void SetVisible(bool visible)
    {
        for (int i = 0; i < Renderers.Length; i++)
            if (Renderers[i] != null) Renderers[i].enabled = visible;
    }

    /// <summary>Bone hitboxes / live corpse physics near the player, one capsule and frozen corpses farther away.</summary>
    public void SetHitboxDetail(bool near)
    {
        if (_damage != null) _damage.SetHitboxesEnabled(near);
    }

    public bool IsVisible
    {
        get { return Renderers.Length > 0 && Renderers[0] != null && Renderers[0].enabled; }
    }

    public void SetCastShadows(bool cast)
    {
        if (cast == _castShadows) return;
        _castShadows = cast;
        var mode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < Renderers.Length; i++)
            if (Renderers[i] != null) Renderers[i].shadowCastingMode = mode;
    }

    private void PlayState(string state, float normalizedTime)
    {
        if (_animator == null || state == _animState) return;
        _animState = state;
        _animator.speed = 1f;
        _animator.Play(state, 0, normalizedTime);
    }
}
