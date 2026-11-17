using UnityEngine;
using UnityEngine.AI;

public class PedestrianWalker : MonoBehaviour
{
    // Ground speeds the clips were authored for; playback is scaled to match actual movement.
    const float WalkClipSpeed = 1.3f;
    const float RunClipSpeed = 4f;
    const float LateralSpeed = 1.1f;
    const float ArriveDistance = 0.06f;

    // Down: knocked over or dead; a ragdoll owns the body and the walker leaves it alone (see PedestrianDamageBridge).
    public enum Mode { Lane, Fleeing, Returning, Standing, Down }

    public Mode State { get; private set; }
    public PedestrianSpawner Owner { get { return _owner; } }
    public event System.Action Despawned;
    public int Lane { get; private set; }
    public float Distance { get; private set; }
    public float GapAhead { get; set; }
    public bool WantsRecycle { get; private set; }
    public Renderer[] Renderers { get; private set; }

    // Set each frame by the spawner to keep people apart.
    public float SpeedScale { get; set; } = 1f;
    public float LateralTarget { get; set; }
    public float Lateral { get; private set; }

    public Vector3 Forward { get; private set; } = Vector3.forward;
    public Vector3 Velocity { get; private set; }
    public bool ArrivedAtSpot { get; private set; }
    public float StandCooldownUntil { get; set; }
    public bool CanTalk { get; private set; }
    public bool HasSecondIdle { get; private set; }
    public bool Rejoining { get { return _rejoining; } }

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

    private Vector3 _spot;
    private Vector3 _faceToward;
    private bool _rejoining;

    public void Setup(FootpathLaneNetwork network, PedestrianSpawner owner, NavMeshAgent agent)
    {
        _network = network;
        _owner = owner;
        _agent = agent;
        _agent.enabled = false;
        _laneLayer = gameObject.layer;
        _laneTag = gameObject.tag;
        _animator = GetComponentInChildren<Animator>();
        Renderers = GetComponentsInChildren<Renderer>(true);
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
            var clips = _animator.runtimeAnimatorController as RuntimeAnimatorController;
            CanTalk = HasClipFor(clips, "talk");
            HasSecondIdle = HasClipFor(clips, "idle2") || HasClipFor(clips, "idle_2");
        }
    }

    // Officers have only walking clips, so they never join a conversation.
    private static bool HasClipFor(RuntimeAnimatorController controller, string part)
    {
        if (controller == null) return false;
        foreach (var clip in controller.animationClips)
            if (clip != null && clip.name.ToLowerInvariant().Contains(part)) return true;
        return false;
    }

    // The footpath's baked heights are only a guide: kerbs, planters and uneven pavement differ, and walking at the baked
    // height sinks legs into them. Every few frames each walker looks at the real surface under the path and steps up
    // (a little) or down to it, smoothly.
    const float MaxStepUp = 0.45f, MaxStepDown = 0.5f;
    static int _groundMask = -1;
    float _groundOffset, _groundTarget;
    int _groundSlot = -1;

    float GroundOffset(Vector3 onPath, float dt, bool snap)
    {
        if (_groundMask == -1)
            _groundMask = ~LayerMask.GetMask("Player", "Vehicle", "Traffic AI", "Ignore Raycast", "UI", "Water");
        if (_groundSlot < 0) _groundSlot = Random.Range(0, 4);
        if (snap || (Time.frameCount + _groundSlot) % 4 == 0)
        {
            _groundTarget = 0f;
            RaycastHit hit;
            if (Physics.Raycast(onPath + Vector3.up * MaxStepUp, Vector3.down, out hit, MaxStepUp + MaxStepDown,
                    _groundMask & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                _groundTarget = hit.point.y - onPath.y;
        }
        _groundOffset = snap ? _groundTarget : Mathf.MoveTowards(_groundOffset, _groundTarget, dt * 1.6f);
        return _groundOffset;
    }

    // ---------- cars in the way ----------
    // A walker is a kinematic body moved by script, which shoves any car it walks into. So walkers stop for a car in their
    // path, step round it after a moment, and (whatever they are doing) are pushed back out of a car rather than pushing it.
    static int _vehicleMask = -1;
    static readonly Collider[] _carHits = new Collider[6];
    CapsuleCollider _body;
    float _blockedFor;
    Collider _blockingCar;
    int _blockSide;

    static int VehicleMask
    {
        get
        {
            if (_vehicleMask == -1) _vehicleMask = LayerMask.GetMask("Vehicle", "Traffic AI");
            return _vehicleMask;
        }
    }

    // A car (parked, stopped or driven) right ahead on the way to 'next'?
    Collider CarAhead(Vector3 from, Vector3 direction, float reach)
    {
        if (direction.sqrMagnitude < 1e-4f) return null;
        RaycastHit hit;
        Vector3 chest = from + Vector3.up * 0.9f;
        if (Physics.SphereCast(chest, 0.32f, direction.normalized, out hit, reach, VehicleMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
            return hit.collider;
        return null;
    }

    // Moves the walker out of any car its body ended up inside, unless the car is the one moving into them (then the car's
    // own impact handling decides what happens to the walker).
    void LateUpdate()
    {
        if (State == Mode.Down) return;
        if (_body == null) _body = GetComponent<CapsuleCollider>();
        if (_body == null || !_body.enabled) return;
        Vector3 centre = transform.TransformPoint(_body.center);
        float half = Mathf.Max(0f, _body.height * 0.5f - _body.radius);
        int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * half, centre - Vector3.up * half, _body.radius, _carHits, VehicleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var car = _carHits[i];
            if (car == null || car.transform.IsChildOf(transform)) continue;
            var carBody = car.attachedRigidbody;
            if (carBody != null && !carBody.isKinematic && carBody.linearVelocity.sqrMagnitude > 1.5f * 1.5f) continue;
            Vector3 direction;
            float distance;
            if (Physics.ComputePenetration(_body, transform.position, transform.rotation, car, car.transform.position, car.transform.rotation, out direction, out distance))
            {
                direction.y = 0f;
                if (direction.sqrMagnitude > 1e-4f) transform.position += direction.normalized * (distance + 0.01f);
            }
        }
    }

    public void Place(int lane, float distance, float speed)
    {
        LeaveNavMesh();
        State = Mode.Lane;
        Lane = lane;
        Distance = distance;
        _speed = speed;
        GapAhead = float.MaxValue;
        WantsRecycle = false;
        Lateral = 0f;
        LateralTarget = 0f;
        SpeedScale = 1f;
        _rejoining = false;

        Vector3 forward;
        Vector3 position = _network.Evaluate(lane, distance, out forward);
        position.y += GroundOffset(position, 0f, true);
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-4f) Forward = forward.normalized;
        transform.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward) : transform.rotation);
        _animState = null;
        PlayState("walk", Random.value);
    }

    // Returns the lane the walker was on before this step (differs from Lane when it moved onto another lane).
    public int Advance(float dt)
    {
        int previousLane = Lane;
        // At a true dead end they wait there until they can be removed unseen.
        float speed = WantsRecycle ? 0f : _speed * Mathf.Clamp01((GapAhead - 0.9f) / 1.2f) * SpeedScale;

        // A car in the way: wait for it; after a moment step round it; if it still blocks, walk on (it is let through
        // rather than shoved).
        if (speed > 0.05f && (Time.frameCount + Mathf.Max(0, _groundSlot)) % 3 == 0)
        {
            var car = CarAhead(transform.position, Forward, 0.55f + speed * 0.5f);
            if (car != null)
            {
                if (car != _blockingCar) { _blockingCar = car; _blockSide = Vector3.Dot(car.bounds.center - transform.position, new Vector3(-Forward.z, 0f, Forward.x)) > 0f ? -1 : 1; }
            }
            else if (_blockingCar != null && _blockedFor < 6f)
            {
                _blockingCar = null;
                _blockedFor = 0f;
                LateralTarget = 0f;
            }
        }
        if (_blockingCar != null && _blockedFor < 6f)
        {
            _blockedFor += dt;
            if (_blockedFor > 1.2f) LateralTarget = _blockSide * 1.1f;
            if (_blockedFor < 1.2f || CarAhead(transform.position, Forward, 0.5f) != null) speed = 0f;
            if (_blockedFor >= 6f && _body != null)
            {
                foreach (var c in _blockingCar.attachedRigidbody != null ? _blockingCar.attachedRigidbody.GetComponentsInChildren<Collider>() : new[] { _blockingCar })
                    Physics.IgnoreCollision(_body, c, true);
            }
        }
        else if (_blockedFor >= 6f && (_blockingCar == null || CarAhead(transform.position, Forward, 2f) == null))
        {
            _blockingCar = null;
            _blockedFor = 0f;
            LateralTarget = 0f;
        }
        bool idle = speed < 0.15f;
        PlayState(idle ? "idle1" : "walk", Random.value);
        if (_animator != null) _animator.speed = idle ? 1f : Mathf.Clamp(speed / WalkClipSpeed, 0.5f, 1.4f);

        // Coming back from a conversation: the lane waits until they have stepped back onto it.
        float distance = Distance + (_rejoining ? 0f : speed * dt);
        FootpathLane lane = _network.lanes[Lane];
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
        forward.y = 0f;
        if (forward.sqrMagnitude > 1e-4f) Forward = forward.normalized;

        Lateral = Mathf.MoveTowards(Lateral, LateralTarget, LateralSpeed * dt);
        target += new Vector3(-Forward.z, 0f, Forward.x) * Lateral;
        target.y += GroundOffset(target, dt, false);

        Vector3 current = transform.position;
        // Short hops onto a connecting lane and steps back from a conversation are walked, not snapped.
        bool offLine = (target - current).sqrMagnitude > 0.04f;
        Vector3 position = offLine ? Vector3.MoveTowards(current, target, Mathf.Max(speed, 0.6f) * dt * (_rejoining ? 1f : 1.25f)) : target;
        if (_rejoining && !offLine) _rejoining = false;

        Vector3 moved = position - current;
        moved.y = 0f;
        Velocity = dt > 0f ? moved / dt : Vector3.zero;
        Vector3 heading = offLine && moved.sqrMagnitude > 1e-6f ? moved : Forward;
        Quaternion rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading), 1f - Mathf.Exp(-8f * dt));
        transform.SetPositionAndRotation(position, rotation);

        return previousLane;
    }

    // Steps out of the flow to a spot and stands there: for a conversation, or just to wait a moment.
    public void BeginStanding(Vector3 spot, Vector3 faceToward)
    {
        State = Mode.Standing;
        _spot = spot;
        _faceToward = faceToward;
        ArrivedAtSpot = false;
        _rejoining = false;
        Lateral = 0f;
        LateralTarget = 0f;
        PlayState("walk", Random.value);
    }

    public void TickStanding(float dt)
    {
        Vector3 current = transform.position;
        if (!ArrivedAtSpot)
        {
            Vector3 position = Vector3.MoveTowards(current, _spot, _speed * dt);
            Vector3 moved = position - current;
            moved.y = 0f;
            Velocity = dt > 0f ? moved / dt : Vector3.zero;
            if (_animator != null) _animator.speed = Mathf.Clamp(_speed / WalkClipSpeed, 0.5f, 1.4f);
            Quaternion rotation = moved.sqrMagnitude > 1e-6f
                ? Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moved), 1f - Mathf.Exp(-8f * dt))
                : transform.rotation;
            transform.SetPositionAndRotation(position, rotation);
            if ((position - _spot).sqrMagnitude < ArriveDistance * ArriveDistance)
            {
                ArrivedAtSpot = true;
                Velocity = Vector3.zero;
                PlayStand("idle1");
            }
            return;
        }

        Vector3 toFace = _faceToward - current;
        toFace.y = 0f;
        if (toFace.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toFace), 1f - Mathf.Exp(-5f * dt));
    }

    public void PlayStand(string state)
    {
        PlayState(state, Random.value);
        if (_animator != null) _animator.speed = 1f;
    }

    public void FinishStanding(int lane, float distance)
    {
        State = Mode.Lane;
        Lane = lane;
        Distance = distance;
        GapAhead = float.MaxValue;
        Lateral = 0f;
        LateralTarget = 0f;
        SpeedScale = 1f;
        _rejoining = true;
        PlayState("walk", Random.value);
    }

    // Held at a spot by another system (AmbientPopulationManager): placed there at once, in the Standing state, until
    // something makes them leave (panic, knock-down, recycling), which raises LeftHold.
    public event System.Action LeftHold;
    public bool IsHeld { get { return _held; } }
    public const float HoldNavMeshReach = 6f;
    private bool _held;
    private bool _blendNextState;
    private Vector3 _exitFrom;
    private float _exitLeft;
    private float _exitSeconds;

    public void BeginHold(Vector3 position, Vector3 faceToward)
    {
        LeaveNavMesh();
        State = Mode.Standing;
        _spot = position;
        _faceToward = faceToward;
        ArrivedAtSpot = true;
        _rejoining = false;
        Lateral = 0f;
        LateralTarget = 0f;
        Velocity = Vector3.zero;
        WantsRecycle = false;
        if (_speed <= 0f) _speed = WalkClipSpeed;
        _held = true;
        _animState = null;
        Vector3 facing = faceToward - position;
        facing.y = 0f;
        transform.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : transform.rotation);
    }

    public void MoveHold(Vector3 position)
    {
        _spot = position;
        transform.position = position;
    }

    public void SetHoldFacing(Vector3 faceToward)
    {
        if (_held) _faceToward = faceToward;
    }

    private void ReleaseHold(bool blendOut)
    {
        if (!_held) return;
        _held = false;
        _blendNextState = blendOut;
        if (LeftHold != null) LeftHold();
    }

    // A small push to keep two people from standing in each other; only a walker in the flow takes it.
    public void Nudge(Vector3 push)
    {
        if (State == Mode.Down) return;
        transform.position += push;
        if (State == Mode.Lane) Lateral += Vector3.Dot(push, new Vector3(-Forward.z, 0f, Forward.x));
    }

    // Leaves the lane and runs away from the threat across any walkable surface. Returns false if no NavMesh here.
    public bool StartPanic(Vector3 threat, float duration, float runSpeed)
    {
        if (State == Mode.Down) return false;
        _threat = threat;
        _panicUntil = Time.time + duration;
        if (State == Mode.Fleeing) return true;

        if (!EnterNavMesh()) return false;
        ReleaseHold(true);
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
        if (State == Mode.Down) return;
        if (!_agent.enabled || !_agent.isOnNavMesh)
        {
            WantsRecycle = true;
            return;
        }

        // Dashing from the seat to the mesh (see EnterNavMesh).
        if (_exitLeft > 0f)
        {
            _exitLeft = Mathf.MoveTowards(_exitLeft, 0f, dt / _exitSeconds);
            transform.position = Vector3.Lerp(_agent.nextPosition, _exitFrom, _exitLeft);
            if (_exitLeft <= 0f) _agent.updatePosition = true;
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

    public void ForceCalm()
    {
        if (State == Mode.Fleeing) _panicUntil = 0f;
    }

    public void OnDespawned()
    {
        ReleaseHold(false);
        _blendNextState = false;
        LeaveNavMesh();
        State = Mode.Lane;
        if (Despawned != null) Despawned();
    }

    // Knocked over, killed or held by a scripted scene: off the lane and NavMesh, standing still until told otherwise.
    public void EnterDown()
    {
        if (State == Mode.Down) return;
        Mode before = State;
        ReleaseHold(false);
        LeaveNavMesh();
        State = Mode.Down;
        Velocity = Vector3.zero;
        _animState = null;
        _owner.OnWalkerDowned(this, before);
    }

    // Back on the feet: runs from the threat, or asks to be recycled if there is no NavMesh here.
    public void RecoverFromDown(Vector3 threat, float duration, float runSpeed)
    {
        if (State != Mode.Down) return;
        State = Mode.Returning;
        _animState = null;
        if (StartPanic(threat, duration, runSpeed)) return;
        State = Mode.Down;
        WantsRecycle = true;
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
        Vector3 from = transform.position;
        // People held at a seat may be a few metres from the mesh: they dash over to it instead of snapping.
        if (!_owner.EnsurePanicNavMesh() || !NavMesh.SamplePosition(from, out hit, _held ? HoldNavMeshReach : 3f, NavMesh.AllAreas)) return false;
        _agent.enabled = true;
        if (!_agent.Warp(hit.position))
        {
            _agent.enabled = false;
            return false;
        }
        _stuckTime = 0f;
        // Anyone a little off the mesh (a lane hugging a wall, a seat) runs onto it from where he stands; a Warp alone snaps him there.
        if ((from - hit.position).sqrMagnitude > 0.04f)
        {
            _exitFrom = from;
            _exitLeft = 1f;
            _exitSeconds = Mathf.Max(0.25f, Vector3.Distance(from, hit.position) / RunClipSpeed);
            _agent.updatePosition = false;
            transform.position = from;
        }
        return true;
    }

    private void LeaveNavMesh()
    {
        if (_agent != null && _agent.enabled)
        {
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.updatePosition = true;
            _exitLeft = 0f;
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
        // Getting up from a seat blends out of the sitting pose instead of snapping.
        if (_blendNextState)
        {
            _blendNextState = false;
            _animator.CrossFadeInFixedTime(state, 0.25f, 0, 0f);
            return;
        }
        _animator.Play(state, 0, normalizedTime);
    }
}
