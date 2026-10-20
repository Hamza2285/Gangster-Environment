using UnityEngine;

// Two-wheeler on top of the iTS car physics. The traffic AI drives it like a narrow car (four wheel
// colliders set close together); this keeps the body upright, leans the visible bike and rider into
// turns like a real motorbike, and spins the model's own wheels.
[RequireComponent(typeof(Rigidbody))]
public class BikeRig : MonoBehaviour
{
    [Tooltip("Everything that leans: bike model and rider. Its origin sits on the ground under the bike.")]
    public Transform visual;
    public Transform frontWheelVisual;
    public Transform rearWheelVisual;
    public WheelCollider frontWheel;
    public WheelCollider rearWheel;

    [Tooltip("Most the bike leans into a turn, in degrees.")]
    public float maxLean = 28f;
    [Tooltip("How quickly the lean follows the turn.")]
    public float leanResponse = 4f;
    [Tooltip("How hard the hidden stabiliser pulls the physics body back upright (rad/s² per radian of roll).")]
    public float uprightStrength = 60f;
    [Tooltip("Damping of that pull, so it settles instead of rocking.")]
    public float uprightDamping = 12f;

    private Rigidbody _body;
    private float _lean;
    private float _frontSpin, _rearSpin;
    private Quaternion _frontRest, _rearRest;
    private Vector3 _frontRestPos, _rearRestPos;

    [Tooltip("Resistance to pitching, turning and rolling (kg m²). The iTS vehicles run with an inertia of 1, which " +
             "lets a short, narrow bike be flipped, thrown or spun in place by any wheel force.")]
    public float pitchInertia = 1400f;
    public float yawInertia = 1200f;
    public float rollInertia = 430f;

    [Tooltip("Move the visible wheels up and down with the wheel colliders' suspension (drivable bikes; their RCC wheel models " +
             "are hidden helpers, so the real tyres inside the leaning visual are positioned here).")]
    public bool followSuspension;

    private void OnEnable()
    {
        // After iTS has set up the body (it resets the centre of mass on enable).
        StartCoroutine(SetInertiaNextFrame());
    }

    private System.Collections.IEnumerator SetInertiaNextFrame()
    {
        yield return new WaitForFixedUpdate();
        ApplyInertia();
    }

    private void ApplyInertia()
    {
        if (_body == null) _body = GetComponent<Rigidbody>();
        _body.automaticInertiaTensor = false;
        _body.inertiaTensorRotation = Quaternion.identity;
        _body.inertiaTensor = new Vector3(pitchInertia, yawInertia, rollInertia);
    }

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        if (frontWheelVisual != null) { _frontRest = frontWheelVisual.localRotation; _frontRestPos = frontWheelVisual.localPosition; }
        if (rearWheelVisual != null) { _rearRest = rearWheelVisual.localRotation; _rearRestPos = rearWheelVisual.localPosition; }
    }

    private void FixedUpdate()
    {
        // Roll about the bike's own forward axis back toward upright (pitch on ramps is left alone).
        Vector3 forward = transform.forward;
        Vector3 up = Vector3.ProjectOnPlane(transform.up, forward);
        Vector3 wantedUp = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 1e-4f || wantedUp.sqrMagnitude < 1e-4f) return;
        float roll = Vector3.SignedAngle(wantedUp, up, forward) * Mathf.Deg2Rad;
        float rollRate = Vector3.Dot(_body.angularVelocity, forward);
        // Applied as an angular acceleration so it behaves the same whatever inertia the prefab's rigidbody
        // has (the iTS vehicles use an inertia of 1, so a mass-scaled torque would be hundreds of times too strong).
        float accel = -roll * uprightStrength - rollRate * uprightDamping;
        _body.AddTorque(forward * accel, ForceMode.Acceleration);
    }

    private void LateUpdate()
    {
        if (_body == null) return;
        float dt = Time.deltaTime;

        // Lean into the turn: the angle a real bike needs, tan(lean) = v * yawRate / g.
        float speed = Vector3.Dot(_body.linearVelocity, transform.forward);
        float yawRate = Vector3.Dot(_body.angularVelocity, transform.up);
        float wanted = Mathf.Atan2(speed * yawRate, 9.81f) * Mathf.Rad2Deg;
        wanted = Mathf.Clamp(-wanted, -maxLean, maxLean);
        if (Mathf.Abs(speed) < 1.5f) wanted = 0f;
        _lean = Mathf.Lerp(_lean, wanted, 1f - Mathf.Exp(-leanResponse * dt));
        if (visual != null) visual.localRotation = Quaternion.AngleAxis(_lean, Vector3.forward);

        // Spin the model's wheels at the speed the physics wheels are turning.
        _frontSpin = Spin(frontWheelVisual, frontWheel, _frontRest, _frontSpin, speed, dt);
        _rearSpin = Spin(rearWheelVisual, rearWheel, _rearRest, _rearSpin, speed, dt);

        if (followSuspension)
        {
            Suspend(frontWheelVisual, frontWheel, _frontRestPos);
            Suspend(rearWheelVisual, rearWheel, _rearRestPos);
        }
    }

    // Keeps the tyre centre at the wheel collider's current height (along the bike's up axis), so the visible wheels compress
    // and extend with the suspension while staying in line with the leaning bike.
    private void Suspend(Transform wheelVisual, WheelCollider collider, Vector3 restLocal)
    {
        if (wheelVisual == null || collider == null || wheelVisual.parent == null) return;
        collider.GetWorldPose(out Vector3 pose, out _);
        Transform parent = wheelVisual.parent;
        Vector3 rest = parent.TransformPoint(restLocal);
        Vector3 up = transform.up;
        wheelVisual.position = rest + up * Vector3.Dot(pose - rest, up);
    }

    private static float Spin(Transform wheelVisual, WheelCollider collider, Quaternion rest, float angle, float speed, float dt)
    {
        if (wheelVisual == null) return angle;
        float degPerSecond;
        if (collider != null && collider.isGrounded) degPerSecond = collider.rpm * 6f;
        else degPerSecond = collider != null && collider.radius > 0.01f ? speed / collider.radius * Mathf.Rad2Deg : 0f;
        angle = Mathf.Repeat(angle + degPerSecond * dt, 360f);
        wheelVisual.localRotation = rest * Quaternion.AngleAxis(angle, Vector3.right);
        return angle;
    }
}
