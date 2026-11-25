using System.Collections.Generic;
using UnityEngine;

// Aggressive driving for chase cars: follows a growing path at high speed, swerves around traffic instead of
// queuing, drifts through corners (loosened rear grip + yaw push, with smoke, skid marks and squeal) and
// recovers itself if it spins out or gets stuck.
[RequireComponent(typeof(Rigidbody))]
public class ChaseDriver : MonoBehaviour
{
    const float WheelBase = 2.6f;
    const float MaxSteerDegrees = 55f;

    public float topSpeed = 22f;
    public float cornerSpeed = 12f;
    [Tooltip("Optional car to chase: speed is matched and a gap kept when right behind it.")]
    public Transform target;
    public float targetGap = 6f;
    public bool drifting = true;
    [Tooltip("Rear tyre sideways grip while drifting (1 = normal).")]
    public float driftGrip = 0.55f;
    public float driftYawTorque = 1.2f;
    [Tooltip("Side-to-side weaving across the road, in metres (0 = drive straight down the lane).")]
    public float weave = 0f;
    [Tooltip("Calm pursuit driving (police): keeps its distance, never swerves into oncoming traffic, never drifts.")]
    public bool cautious = false;
    [Tooltip("Another chase car ahead to keep a gap behind (e.g. the second police car behind the first).")]
    public Transform leader;
    public float leaderGap = 10f;

    [Tooltip("Stops this far (m) short of the end of the path when there is no car to follow.")]
    public float endGap = 0f;

    /// <summary>A chased car this close (m) to the end of the path extends it.</summary>
    public const float TrailReach = 30f;

    public bool Stopped { get; private set; }
    public float Speed { get; private set; }
    public float RemainingOnPath { get { return _along.Count == 0 ? 0f : _along[_along.Count - 1] - AlongAt(transform.position); } }
    public Vector3 LastPathPoint { get { return _path.Count > 0 ? _path[_path.Count - 1] : transform.position; } }

    private TSSimpleCar _car;
    private Rigidbody _body;
    private Collider[] _own;
    private readonly List<Vector3> _path = new List<Vector3>();
    private readonly List<float> _along = new List<float>();
    private int _progress;
    private float _swerve;
    private float _stuckTime, _boxedTime;
    private float _savedTorque, _savedAccel, _savedBrake;
    private Vector3 _savedCentreOfMass;
    private WheelCollider[] _rear = new WheelCollider[0];
    private WheelFrictionCurve[] _rearGrip = new WheelFrictionCurve[0];
    private bool _isDrifting;
    private bool _ended;
    private float _weaveSeed;
    private float _reverseUntil, _reverseSteer, _wedged, _nextReverse;
    private float _halfLength = 2.4f, _halfWidth = 1f;
    private readonly RaycastHit[] _wallHits = new RaycastHit[24];

    // Drift effects.
    private readonly List<ParticleSystem> _smoke = new List<ParticleSystem>();
    private readonly List<TrailRenderer> _skids = new List<TrailRenderer>();
    private AudioSource _squeal;
    private static AudioClip _squealClip;
    private static Material _smokeMaterial, _skidMaterial;

    public void Begin(TSSimpleCar car, float engineTorque)
    {
        _car = car;
        _weaveSeed = Random.Range(0f, 10f);
        car.crashed = false;
        car.carCanCrash = false;
        _body = GetComponent<Rigidbody>();
        _own = GetComponentsInChildren<Collider>(true);
        MeasureCar();
        _savedTorque = car.EngineTorque;
        _savedAccel = car.maxAcceleration;
        _savedBrake = car.brakeTorque;
        // A lower centre of mass keeps the car on its wheels at chase speeds (restored afterwards).
        _savedCentreOfMass = _body.centerOfMass;
        _body.centerOfMass = _savedCentreOfMass - Vector3.up * 0.45f;
        car.EngineTorque = engineTorque;
        // Stock brakes are sized for city speeds; chase speeds need much more.
        car.brakeTorque = Mathf.Max(car.brakeTorque, 5000f);
        // Stop the car's own "too much acceleration" damping from capping chase speeds.
        car.maxAcceleration = 15f;
        _rear = car.BackWheels;
        _rearGrip = new WheelFrictionCurve[_rear.Length];
        for (int i = 0; i < _rear.Length; i++) _rearGrip[i] = _rear[i].sidewaysFriction;
        BuildEffects();
    }

    public void SetPath(List<Vector3> points)
    {
        _path.Clear();
        _along.Clear();
        _progress = 0;
        foreach (var p in points) Append(p);
    }

    public void Append(Vector3 p)
    {
        if (_path.Count > 0 && (p - _path[_path.Count - 1]).sqrMagnitude < 1f) return;
        _along.Add(_path.Count == 0 ? 0f : _along[_along.Count - 1] + Vector3.Distance(_path[_path.Count - 1], p));
        _path.Add(p);
        // Forget path far behind so long chases don't grow without bound.
        if (_progress > 400)
        {
            _path.RemoveRange(0, 300);
            _along.RemoveRange(0, 300);
            _progress -= 300;
        }
    }

    public void SetStopped(bool stopped) { Stopped = stopped; _stuckTime = 0f; }

    private void OnDestroy() => End();

    public void End()
    {
        if (_ended) return;
        _ended = true;
        enabled = false;
        if (_car != null)
        {
            _car.EngineTorque = _savedTorque;
            _car.maxAcceleration = _savedAccel;
            _car.brakeTorque = _savedBrake;
        }
        if (_body != null) _body.centerOfMass = _savedCentreOfMass;
        SetRearGrip(1f);
        foreach (var s in _smoke) if (s != null) Destroy(s.gameObject);
        foreach (var t in _skids) if (t != null) Destroy(t.gameObject);
        if (_squeal != null) Destroy(_squeal);
    }

    private void FixedUpdate()
    {
        if (_car == null) return;
        Vector3 pos = transform.position;
        Speed = Vector3.Dot(_body.linearVelocity, transform.forward);

        float playerGap = PlayerCarGuard.Brake(transform, _body, _halfLength, _halfWidth);

        if (Stopped || _path.Count < 2)
        {
            _reverseUntil = 0f;
            // Brakes along its path, not straight on over the footpath.
            float stopSteer = 0f;
            if (_path.Count >= 2 && Speed > 1.5f)
            {
                AdvanceProgress(pos);
                Vector3 to = transform.InverseTransformPoint(PointAt(AlongAt(pos) + Mathf.Clamp(Speed * 0.7f, 6f, 14f)));
                if (to.z > 1f) stopSteer = Mathf.Clamp(Mathf.Atan2(2f * WheelBase * to.x, to.sqrMagnitude) * Mathf.Rad2Deg / MaxSteerDegrees, -0.6f, 0.6f);
            }
            _car.OnAIUpdate(stopSteer, 1f, 0f, false);
            UpdateDrift(0f, false);
            return;
        }

        // The path grows along the very way the chased car takes, so it never ends where that car was a second ago.
        if (target != null)
        {
            float step = Flat(target.position - LastPathPoint).magnitude;
            if (step > 3f && step < TrailReach) Append(target.position);
        }
        AdvanceProgress(pos);
        float along = AlongAt(pos);
        float lookAhead = Mathf.Clamp(Mathf.Abs(Speed) * 0.7f, 6f, 18f);
        Vector3 aim = PointAt(along + lookAhead);
        bool alongside = false;
        // At (or past) the end of the path behind a moving car: on toward it, never round on the spot for a point left behind.
        if (target != null && _along[_along.Count - 1] - along < 5f)
        {
            // Alongside it (or past it): the same way it is heading, which is along the road.
            alongside = Vector3.Dot(target.position - pos, transform.forward) <= 3f;
            aim = alongside ? pos + Flat(target.forward).normalized * 10f : target.position;
        }
        Vector3 fwd = Flat(aim - pos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        // Something in the way: slip past on whichever side is clear instead of queuing behind it.
        float range = Mathf.Max(12f, Speed * 1.4f + 8f);
        float blockedAt = Blocked(pos, fwd, range);
        float wantSwerve = 0f;
        float swerveWidth = cautious ? 3f : 3.5f;
        // Pass on whichever side is clear; police only overtake with plenty of clear road ahead.
        if (blockedAt < range)
        {
            if (Blocked(pos + right * swerveWidth, fwd, range * (cautious ? 1.5f : 1f)) >= range * (cautious ? 1.5f : 1f)) wantSwerve = swerveWidth;
            else if (Blocked(pos - right * swerveWidth, fwd, range * (cautious ? 1.5f : 1f)) >= range * (cautious ? 1.5f : 1f)) wantSwerve = -swerveWidth;
        }
        // Never swings past the player on foot: it stops.
        if (!float.IsPositiveInfinity(playerGap)) wantSwerve = 0f;
        // ... nor into a wall beside the road.
        if (wantSwerve != 0f && WallAhead(pos + right * wantSwerve * 0.5f, (fwd * 12f + right * wantSwerve * 0.5f).normalized, 12f) < 11f) wantSwerve = 0f;
        _swerve = Mathf.MoveTowards(_swerve, wantSwerve, Time.fixedDeltaTime * (cautious ? 2.5f : 6f));
        float weaveOffset = weave > 0f ? Mathf.Sin(Time.time * 0.45f + _weaveSeed) * weave : 0f;
        aim += right * (_swerve + weaveOffset);

        Vector3 local = transform.InverseTransformPoint(aim);
        float alpha = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z));
        float steerDeg = Mathf.Atan2(2f * WheelBase * Mathf.Sin(alpha), lookAhead) * Mathf.Rad2Deg;
        float steer = Mathf.Clamp(steerDeg / MaxSteerDegrees, -1f, 1f);
        // Less steering lock at speed, like a real car, so a sharp correction can't roll it.
        float lockAtSpeed = Mathf.Lerp(1f, 0.3f, Mathf.InverseLerp(8f, 26f, Mathf.Abs(Speed)));
        steer = Mathf.Clamp(steer, -lockAtSpeed, lockAtSpeed);

        float desired = topSpeed;
        float turn = TurnAhead(along, 22f);
        if (turn > 30f) desired = Mathf.Lerp(topSpeed, cornerSpeed, Mathf.InverseLerp(30f, 80f, turn));
        // Grip limit: v = sqrt(lateral g * radius), looking far enough ahead to brake in time.
        float lateral = cautious ? 7f : 7.5f;
        float scan = Mathf.Clamp(Speed * Speed / 9f + 20f, 30f, 130f);
        for (float ahead = 0f; ahead <= scan; ahead += 6f)
        {
            float bend = TurnAhead(along + ahead, 18f) * Mathf.Deg2Rad;
            if (bend < 0.12f) continue;
            float radius = 14f / bend;
            float cornerLimit = Mathf.Sqrt(lateral * radius);
            // Allow for braking from here to the bend.
            desired = Mathf.Min(desired, Mathf.Sqrt(cornerLimit * cornerLimit + 2f * 4.5f * ahead));
        }
        if (blockedAt < range && wantSwerve == 0f) desired = Mathf.Min(desired, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, blockedAt - 4f)));
        desired = Mathf.Min(desired, GapSpeed(target, targetGap, pos));
        desired = Mathf.Min(desired, GapSpeed(leader, leaderGap, pos));

        // Pointing the wrong way: turn round slowly instead of swinging wide into a building.
        float offAngle = Vector3.Angle(Flat(transform.forward), Flat(aim - pos));
        if (offAngle > 28f) desired = Mathf.Min(desired, Mathf.Lerp(18f, 4.5f, Mathf.InverseLerp(28f, 75f, offAngle)));
        // Comes to rest at the end of its path (a car it follows may well be moving on).
        float followSpeed = 0f, chasedSpeed = 0f;
        if (target != null)
        {
            var other = target.GetComponent<Rigidbody>();
            if (other != null) chasedSpeed = other.linearVelocity.magnitude;
            if (other != null) followSpeed = Mathf.Max(0f, Vector3.Dot(other.linearVelocity, Flat(transform.forward).normalized));
        }
        float left = _along[_along.Count - 1] - along - (target == null ? endGap : targetGap);
        // No faster than it takes to close in (the engine would do twice the player's speed), and back behind a car it ran past.
        if (target != null) desired = Mathf.Min(desired, Mathf.Max(18f, chasedSpeed + Mathf.Clamp(left * 0.25f, 4f, 16f)));
        if (alongside) desired = Mathf.Min(desired, followSpeed * 0.75f);
        desired = Mathf.Min(desired, followSpeed + Mathf.Sqrt(2f * 3.5f * Mathf.Max(0f, left)));
        // A wall where the nose points: slow enough to turn away from it or stop.
        float wall = WallAhead(pos, Flat(transform.forward).normalized, Mathf.Clamp(Speed * Speed / 14f + 6f, 8f, 60f));
        if (wall < float.MaxValue) desired = Mathf.Min(desired, Mathf.Max(2.5f, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, wall - 2.5f))));
        if (!float.IsPositiveInfinity(playerGap)) desired = Mathf.Min(desired, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, playerGap - 2.5f)));

        if (Reverse(pos, steer, desired, wall, offAngle, playerGap)) return;

        float throttle = 0f, brake = 0f;
        if (Speed < desired - 0.3f) throttle = Mathf.Clamp01((desired - Speed) * (cautious ? 0.2f : 0.3f) + 0.1f);
        else if (Speed > desired + 0.5f) brake = Mathf.Clamp01((Speed - desired) * (cautious ? 0.4f : 0.45f) + 0.05f);
        if (desired < 0.3f) { throttle = 0f; brake = 1f; }
        // The stock brakes cannot shed chase speeds in time for a corner.
        if (Speed > desired + 2f && Speed > 5f && Grounded())
            _body.AddForce(-Flat(transform.forward).normalized * Mathf.Min(6f, Speed - desired), ForceMode.Acceleration);

        bool drift = drifting && !cautious && Mathf.Abs(steer) > 0.25f && Speed > 9f;
        UpdateDrift(steer, drift);
        _car.OnAIUpdate(steer, brake, throttle, false);

        Recover(pos, desired, along, blockedAt < range && wantSwerve == 0f);
    }

    // Nose against something, or no room to turn round: backs off with the wheel turned the other way, like any driver would.
    private bool Reverse(Vector3 pos, float steer, float desired, float wall, float offAngle, float playerGap)
    {
        if (Time.time >= _reverseUntil)
        {
            bool wedged = Mathf.Abs(Speed) < 0.9f && desired > 1.5f && float.IsPositiveInfinity(playerGap) && (wall < 4f || offAngle > 45f);
            _wedged = wedged ? _wedged + Time.fixedDeltaTime : 0f;
            if (_wedged < 0.9f || Time.time < _nextReverse) return false;
            _wedged = 0f;
            _reverseUntil = Time.time + 2f;
            _nextReverse = _reverseUntil + 0.8f;
            _reverseSteer = Mathf.Abs(steer) > 0.05f ? -Mathf.Sign(steer) : 1f;
        }
        Vector3 back = -Flat(transform.forward).normalized;
        bool blocked = WallAhead(pos, back, 3f) < 2f || Blocked(pos, back, 3f) < 2f;
        if (!blocked && PlayerCarGuard.OnFoot(out Vector3 player))
        {
            Vector3 to = Flat(player - pos);
            blocked = Vector3.Dot(to, back) > 0f && to.magnitude < _halfLength + 4f;
        }
        if (blocked) { _reverseUntil = 0f; return false; }
        bool tooFast = Speed < -5f;
        _car.OnAIUpdate(_reverseSteer * 0.8f, Speed > 1f ? 1f : 0f, 0f, false);
        for (int i = 0; i < _rear.Length; i++) _rear[i].motorTorque = tooFast || Speed > 1f ? 0f : -_car.EngineTorque * 0.4f;
        UpdateDrift(0f, false);
        return true;
    }

    // Distance from the bumper to the first solid, unmovable thing in a car-wide lane.
    private float WallAhead(Vector3 from, Vector3 dir, float range)
    {
        Vector3 origin = from + Vector3.up * 1.25f + dir * (_halfLength - 0.5f);
        float best = float.MaxValue;
        int count = Physics.BoxCastNonAlloc(origin, new Vector3(_halfWidth * 0.85f, 0.4f, 0.2f), dir, _wallHits,
            Quaternion.LookRotation(dir), range, ~((1 << 2) | (1 << 8) | (1 << gameObject.layer)), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = _wallHits[i];
            if (hit.normal.y > 0.6f || hit.collider.attachedRigidbody != null || IsOwn(hit.collider)) continue;
            if (hit.distance <= 0f && Vector3.Dot(hit.normal, dir) > -0.5f) continue;
            if (hit.collider.GetComponentInParent<PedestrianWalker>() != null) continue;
            if (hit.collider.GetComponentInParent<OpenworldCrime.Environment.BreakableProp>() != null) continue;
            best = Mathf.Min(best, Mathf.Max(0f, hit.distance - 0.5f));
        }
        return best;
    }

    private bool Grounded()
    {
        for (int i = 0; i < _rear.Length; i++) if (_rear[i] != null && _rear[i].isGrounded) return true;
        return false;
    }

    private void MeasureCar()
    {
        float length = 0f, width = 0f;
        foreach (var c in _own)
        {
            var box = c as BoxCollider;
            if (box == null || box.isTrigger) continue;
            Vector3 centre = transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
            Vector3 size = transform.InverseTransformVector(box.transform.TransformVector(box.size));
            length = Mathf.Max(length, Mathf.Abs(centre.z) + Mathf.Abs(size.z) * 0.5f);
            width = Mathf.Max(width, Mathf.Abs(centre.x) + Mathf.Abs(size.x) * 0.5f);
        }
        if (length > 1.5f) _halfLength = Mathf.Min(length, 3.2f);
        if (width > 0.6f) _halfWidth = Mathf.Min(width, 1.3f);
    }

    private void UpdateDrift(float steer, bool drift)
    {
        if (drift != _isDrifting)
        {
            _isDrifting = drift;
            SetRearGrip(drift ? driftGrip : 1f);
        }
        if (drift)
        {
            DriftSeconds += Time.fixedDeltaTime;
            // Kick the tail out in the direction of the turn; pure pursuit counter-steers it back.
            _body.AddTorque(Vector3.up * steer * driftYawTorque * _body.mass, ForceMode.Force);
        }

        bool sliding = drift;
        for (int i = 0; i < _rear.Length; i++)
        {
            WheelHit hit;
            if (_rear[i].GetGroundHit(out hit) && Mathf.Abs(hit.sidewaysSlip) > 0.35f && Speed > 6f) sliding = true;
        }
        foreach (var s in _smoke)
        {
            var e = s.emission;
            e.rateOverTime = sliding ? 40f : 0f;
        }
        foreach (var t in _skids) t.emitting = sliding;
        if (_squeal != null)
        {
            _squeal.volume = Mathf.MoveTowards(_squeal.volume, sliding ? 0.55f : 0f, Time.fixedDeltaTime * 3f);
            if (_squeal.volume > 0f && !_squeal.isPlaying) _squeal.Play();
            else if (_squeal.volume <= 0f && _squeal.isPlaying) _squeal.Stop();
        }
    }

    private void SetRearGrip(float factor)
    {
        for (int i = 0; i < _rear.Length; i++)
        {
            if (_rear[i] == null) continue;
            var f = _rearGrip[i];
            f.stiffness = _rearGrip[i].stiffness * factor;
            _rear[i].sidewaysFriction = f;
        }
    }

    // Spun out, flipped, wedged or boxed in: put it back on the path at the first clear spot ahead,
    // facing along it.
    private void Recover(Vector3 pos, float desired, float along, bool boxedIn)
    {
        bool flipped = transform.up.y < 0.4f;
        // Only counts when it is trying to go (not when holding back behind the car it is chasing).
        bool stalled = Mathf.Abs(Speed) < 1f && desired > 3f;
        // Boxed in by traffic with no gap either side: give it a while, then hop past.
        _boxedTime = Mathf.Abs(Speed) < 1f && boxedIn ? _boxedTime + Time.fixedDeltaTime : 0f;
        if (_boxedTime > (target == null ? 4f : 8f)) { stalled = true; _stuckTime = 10f; _boxedTime = 0f; }
        bool offPath = Flat(PointAt(along) - pos).magnitude > 12f;
        bool backwards = Vector3.Dot(transform.forward, Flat(PointAt(along + 6f) - pos).normalized) < -0.3f;
        _stuckTime = (flipped || stalled || offPath || backwards) ? _stuckTime + Time.fixedDeltaTime : 0f;
        float limit = flipped || offPath ? 3f : 4f;
        // In view, a jump would be obvious: give it much longer to sort itself out first.
        if (Seen(pos)) limit = flipped ? 8f : 15f;
        if (_stuckTime < limit) return;
        _stuckTime = 0f;

        // First spot ahead on the path with flat ground under it and nothing (cars, poles, kerbs) in the way.
        Vector3 p = Vector3.zero, h = Vector3.zero;
        bool found = false;
        for (float d = 8f; d <= 80f && !found; d += 4f)
        {
            Vector3 c = PointAt(along + d);
            Vector3 dir = Flat(PointAt(along + d + 4f) - c);
            if (dir.sqrMagnitude < 0.01f) continue;
            RaycastHit ground;
            if (!Physics.Raycast(c + Vector3.up * 3f, Vector3.down, out ground, 8f, ~((1 << 8) | (1 << gameObject.layer) | (1 << 2)), QueryTriggerInteraction.Ignore)) continue;
            if (ground.normal.y < 0.9f) continue;
            var rot = Quaternion.LookRotation(dir.normalized);
            bool clear = true;
            foreach (var col in Physics.OverlapBox(ground.point + Vector3.up * 1.1f, new Vector3(1.2f, 0.75f, 2.6f), rot, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!IsOwn(col) && col != ground.collider) { clear = false; break; }
            if (!clear) continue;
            p = ground.point; h = dir; found = true;
        }
        if (!found) return;
        _body.linearVelocity = Vector3.zero;
        _body.angularVelocity = Vector3.zero;
        var look = Quaternion.LookRotation(h.normalized);
        _body.position = p + Vector3.up * 0.5f;
        _body.rotation = look;
        transform.SetPositionAndRotation(p + Vector3.up * 0.5f, look);
        Recoveries++;
    }

    // Desired speed that holds a steady gap behind another car: match its speed, close or open the gap gently,
    // and brake hard if much too close.
    private float GapSpeed(Transform other, float gap, Vector3 pos)
    {
        if (other == null) return float.MaxValue;
        Vector3 toOther = Flat(other.position - pos);
        float distance = toOther.magnitude;
        // Only when it is ahead of us; a car alongside or behind doesn't limit our speed.
        if (Vector3.Dot(toOther, transform.forward) < 0f) return float.MaxValue;
        if (distance > gap * 3f) return float.MaxValue;
        var body = other.GetComponent<Rigidbody>();
        float otherSpeed = body != null ? Mathf.Max(0f, Vector3.Dot(body.linearVelocity, other.forward)) : 0f;
        if (distance < gap * 0.6f) return 0f;
        return Mathf.Max(0f, otherSpeed + (distance - gap) * 0.5f);
    }

    private bool Seen(Vector3 pos)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        if ((pos - cam.transform.position).sqrMagnitude > 200f * 200f) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(pos + Vector3.up, Vector3.one * 4f))) return false;
        int blockers = ~((1 << 8) | (1 << gameObject.layer) | (1 << 2));
        return !Physics.Linecast(cam.transform.position, pos + Vector3.up * 1.2f, blockers, QueryTriggerInteraction.Ignore);
    }

    /// Moves the car forward along its own path to about `behind` metres short of `targetPos`, at a clear, flat
    /// spot the camera can't see. Used to keep police in touch with the suspect without visible jumps.
    public bool CatchUp(Vector3 targetPos, float behind)
    {
        if (_path.Count < 2 || Seen(transform.position)) return false;
        int best = _progress;
        float bestD = float.MaxValue;
        for (int i = _progress; i < _path.Count; i++)
        {
            float d = Flat(_path[i] - targetPos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (bestD > 20f * 20f) return false;   // the suspect has left our path
        float here = AlongAt(transform.position);
        for (float along = _along[best] - behind; along > here + 20f; along -= 6f)
        {
            Vector3 c = PointAt(along);
            Vector3 dir = Flat(PointAt(along + 4f) - c);
            if (dir.sqrMagnitude < 0.01f || Seen(c)) continue;
            RaycastHit ground;
            if (!Physics.Raycast(c + Vector3.up * 3f, Vector3.down, out ground, 8f, ~((1 << 8) | (1 << gameObject.layer) | (1 << 2)), QueryTriggerInteraction.Ignore)) continue;
            if (ground.normal.y < 0.9f) continue;
            var rot = Quaternion.LookRotation(dir.normalized);
            bool clear = true;
            foreach (var col in Physics.OverlapBox(ground.point + Vector3.up * 1.1f, new Vector3(1.2f, 0.75f, 2.6f), rot, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!IsOwn(col) && col != ground.collider) { clear = false; break; }
            if (!clear) continue;
            // Arrive already moving, so it doesn't sit still and fall behind again.
            _body.position = ground.point + Vector3.up * 0.5f;
            _body.rotation = rot;
            transform.SetPositionAndRotation(ground.point + Vector3.up * 0.5f, rot);
            _body.angularVelocity = Vector3.zero;
            _body.linearVelocity = dir.normalized * Mathf.Min(topSpeed, 14f);
            AdvanceProgressFrom(along);
            CatchUps++;
            return true;
        }
        return false;
    }

    private void AdvanceProgressFrom(float along)
    {
        for (int i = _progress; i < _along.Count - 1; i++)
            if (_along[i + 1] > along) { _progress = i; return; }
    }

    public int CatchUps { get; private set; }

    public int Recoveries { get; private set; }
    public float DriftSeconds { get; private set; }

    private float Blocked(Vector3 from, Vector3 dir, float range)
    {
        Vector3 origin = from + Vector3.up * 0.9f + dir * 2.4f;
        float best = float.MaxValue;
        foreach (var hit in Physics.SphereCastAll(origin, 1.0f, dir, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y > 0.7f || IsOwn(hit.collider)) continue;
            if (target != null && hit.collider.transform.IsChildOf(target)) continue;
            if (leader != null && hit.collider.transform.IsChildOf(leader)) continue;
            // Only vehicles and people count; walls/kerbs are handled by the path.
            if (hit.collider.attachedRigidbody == null && hit.collider.GetComponentInParent<PedestrianWalker>() == null) continue;
            if (hit.distance < best) best = hit.distance;
        }
        return best;
    }

    private bool IsOwn(Collider c)
    {
        for (int i = 0; i < _own.Length; i++) if (_own[i] == c) return true;
        return false;
    }

    private void AdvanceProgress(Vector3 pos)
    {
        float best = float.MaxValue;
        int bestIndex = _progress;
        int limit = Mathf.Min(_path.Count - 1, _progress + 40);
        for (int i = _progress; i <= limit; i++)
        {
            float d = Flat(_path[i] - pos).sqrMagnitude;
            if (d < best) { best = d; bestIndex = i; }
        }
        _progress = bestIndex;
    }

    private float AlongAt(Vector3 pos)
    {
        if (_path.Count < 2) return 0f;
        int i = Mathf.Clamp(_progress, 0, _path.Count - 2);
        Vector3 ab = Flat(_path[i + 1] - _path[i]);
        float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(Flat(pos - _path[i]), ab) / ab.sqrMagnitude) : 0f;
        return _along[i] + t * ab.magnitude;
    }

    private Vector3 PointAt(float d)
    {
        if (d >= _along[_along.Count - 1]) return _path[_path.Count - 1];
        for (int i = Mathf.Max(1, _progress); i < _along.Count; i++)
        {
            if (_along[i] >= d)
            {
                float seg = _along[i] - _along[i - 1];
                return Vector3.Lerp(_path[i - 1], _path[i], seg > 1e-4f ? (d - _along[i - 1]) / seg : 0f);
            }
        }
        return _path[_path.Count - 1];
    }

    private float TurnAhead(float from, float distance)
    {
        Vector3 a = PointAt(from), b = PointAt(from + distance * 0.5f), c = PointAt(from + distance);
        Vector3 d1 = Flat(b - a), d2 = Flat(c - b);
        if (d1.sqrMagnitude < 0.01f || d2.sqrMagnitude < 0.01f) return 0f;
        return Vector3.Angle(d1, d2);
    }

    private void BuildEffects()
    {
        if (_smokeMaterial == null)
        {
            _smokeMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            _smokeMaterial.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f, 0.5f));
            _smokeMaterial.SetFloat("_Surface", 1f);
            _smokeMaterial.SetOverrideTag("RenderType", "Transparent");
            _smokeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _smokeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _smokeMaterial.SetInt("_ZWrite", 0);
            _smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _smokeMaterial.renderQueue = 3000;
        }
        if (_skidMaterial == null)
        {
            _skidMaterial = new Material(_smokeMaterial);
            _skidMaterial.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f, 0.75f));
        }

        foreach (var wheel in _rear)
        {
            if (wheel == null) continue;
            var smokeGo = new GameObject("DriftSmoke");
            smokeGo.transform.SetParent(transform, false);
            smokeGo.transform.position = wheel.transform.position - Vector3.up * (wheel.radius * 0.6f);
            var ps = smokeGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 1.4f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;
            smokeGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = _smokeMaterial;
            ps.Play();
            _smoke.Add(ps);

            var skidGo = new GameObject("SkidMark");
            skidGo.transform.SetParent(transform, false);
            skidGo.transform.position = wheel.transform.position - Vector3.up * (wheel.radius - 0.03f);
            var trail = skidGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _skidMaterial;
            trail.time = 6f;
            trail.widthMultiplier = 0.25f;
            trail.minVertexDistance = 0.3f;
            trail.alignment = LineAlignment.TransformZ;
            skidGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _skids.Add(trail);
        }

        if (_squealClip == null) _squealClip = BuildSquealClip();
        _squeal = gameObject.AddComponent<AudioSource>();
        _squeal.clip = _squealClip;
        _squeal.loop = true;
        _squeal.volume = 0f;
        _squeal.spatialBlend = 1f;
        _squeal.rolloffMode = AudioRolloffMode.Linear;
        _squeal.maxDistance = 90f;
    }

    // Tyre squeal: a noisy high whine with a wobble, generated so no audio asset is needed.
    private static AudioClip BuildSquealClip()
    {
        const int rate = 44100;
        int n = rate;   // one second loop
        var data = new float[n];
        var rng = new System.Random(7);
        double phase = 0;
        float noise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float f = 1150f + Mathf.Sin(t * Mathf.PI * 2f * 6f) * 90f;
            phase += 2.0 * Mathf.PI * f / rate;
            noise = noise * 0.7f + ((float)rng.NextDouble() * 2f - 1f) * 0.3f;
            data[i] = ((float)System.Math.Sin(phase) * 0.35f + (float)System.Math.Sin(phase * 2.01) * 0.15f + noise * 0.25f) * 0.6f;
        }
        var clip = AudioClip.Create("TyreSqueal", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
