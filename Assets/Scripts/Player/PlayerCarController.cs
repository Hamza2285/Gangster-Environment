using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Keyboard / gamepad driving for the player's car, driving the wheel colliders of an iTS car directly.
// W/S or Up/Down: accelerate, brake, reverse (hold S when stopped). A/D or Left/Right: steer.
// Space: handbrake. Gamepad: right/left trigger, left stick, A button.
[RequireComponent(typeof(Rigidbody))]
public class PlayerCarController : MonoBehaviour
{
    [Header("Engine")]
    [Tooltip("Drive torque on each rear wheel, N·m.")]
    public float motorTorque = 2600f;
    [Tooltip("Top speed forwards, m/s (35 = 126 km/h).")]
    public float topSpeed = 35f;
    public float reverseTopSpeed = 8f;
    public float brakeTorque = 4500f;
    public float handbrakeTorque = 9000f;

    [Header("Steering")]
    public float maxSteerAngle = 34f;
    [Tooltip("Steering lock left at top speed, as a fraction of the full lock.")]
    public float highSpeedSteerFactor = 0.3f;
    [Tooltip("How fast the wheels turn toward the input, degrees per second.")]
    public float steerSpeed = 140f;

    [Header("Stability")]
    [Tooltip("Pushes the car onto the road at speed so it doesn't go light over bumps.")]
    public float downforce = 25f;
    [Tooltip("Resistance to pitching, turning and rolling (kg m²). iTS cars run with an inertia of 1, which makes a " +
             "player-driven car spin and flip far too easily.")]
    public Vector3 inertia = new Vector3(4700f, 4600f, 1200f);

    public bool InputEnabled { get; set; }
    public float SpeedKmh { get { return _body != null ? Vector3.Dot(_body.linearVelocity, transform.forward) * 3.6f : 0f; } }

    private Rigidbody _body;
    private TSSimpleCar _car;
    private WheelCollider[] _front = new WheelCollider[0], _rear = new WheelCollider[0];
    private float _steer;
    private AudioSource _engine;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        // Swept collision checks so the car can't skip through thin poles or walls at speed.
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _car = GetComponent<TSSimpleCar>();
        if (_car != null)
        {
            _front = _car.FrontWheels;
            _rear = _car.BackWheels;
            _car.playerControlled = false;   // we drive the wheels ourselves
            _car.maxAcceleration = 50f;      // stop iTS adding drag whenever the car accelerates hard
            _car.maxSpeed = topSpeed + 10f;
            _car.carCanCrash = false;
        }
        _engine = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        StartCoroutine(ApplyInertiaAfterSetup());
    }

    private System.Collections.IEnumerator ApplyInertiaAfterSetup()
    {
        yield return new WaitForFixedUpdate();
        _body.automaticInertiaTensor = false;
        _body.inertiaTensorRotation = Quaternion.identity;
        _body.inertiaTensor = inertia;
    }

    private void FixedUpdate()
    {
        float throttle = 0f, steer = 0f;
        bool handbrake = false;
        if (InputEnabled) ReadInput(out throttle, out steer, out handbrake);
        else handbrake = true;   // parked: nobody in it, so it stays put

        float speed = Vector3.Dot(_body.linearVelocity, transform.forward);
        float drive = 0f, brake = 0f;
        if (throttle > 0.05f)
        {
            if (speed < -1f) brake = throttle;              // rolling backwards: brake first
            else if (speed < topSpeed) drive = throttle;
        }
        else if (throttle < -0.05f)
        {
            if (speed > 1f) brake = -throttle;              // moving forwards: brake
            else if (speed > -reverseTopSpeed) drive = throttle;   // stopped: reverse
            else brake = 0.3f;                              // at reverse top speed: hold it there
        }
        else
        {
            // Nothing pressed: gentle engine braking, and hold still on slopes once stopped.
            brake = Mathf.Abs(speed) < 0.5f ? 0.3f : 0.04f;
        }

        // Smooth, speed-sensitive steering.
        float lockAtSpeed = Mathf.Lerp(1f, highSpeedSteerFactor, Mathf.InverseLerp(5f, topSpeed, Mathf.Abs(speed)));
        float targetSteer = steer * maxSteerAngle * lockAtSpeed;
        _steer = Mathf.MoveTowards(_steer, targetSteer, steerSpeed * Time.fixedDeltaTime);

        foreach (var w in _front)
        {
            w.steerAngle = _steer;
            w.brakeTorque = brake * brakeTorque;
            w.motorTorque = 0f;
        }
        foreach (var w in _rear)
        {
            w.motorTorque = drive * motorTorque;
            w.brakeTorque = handbrake ? handbrakeTorque : brake * brakeTorque * 0.6f;
        }

        _body.AddForce(-transform.up * downforce * Mathf.Abs(speed) * _body.mass * 0.01f, ForceMode.Force);

        // Brake lights and engine note.
        if (_car != null && _car.brakeLigths != null)
            foreach (var r in _car.brakeLigths) if (r != null) r.enabled = InputEnabled && (brake > 0.05f || handbrake);
        if (_engine != null && _engine.clip != null)
        {
            if (!_engine.isPlaying) _engine.Play();
            _engine.pitch = 0.75f + Mathf.Clamp01(Mathf.Abs(speed) / topSpeed) * 1.1f + Mathf.Abs(drive) * 0.15f;
        }
    }

    // Lets go of everything, e.g. when the player gets out.
    public void Park()
    {
        foreach (var w in _front) { w.motorTorque = 0f; w.brakeTorque = brakeTorque; }
        foreach (var w in _rear) { w.motorTorque = 0f; w.brakeTorque = handbrakeTorque; }
        if (_engine != null) _engine.Stop();
    }

    private static void ReadInput(out float throttle, out float steer, out bool handbrake)
    {
        throttle = 0f; steer = 0f; handbrake = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.wKey.isPressed || k.upArrowKey.isPressed) throttle += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) throttle -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) steer += 1f;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) steer -= 1f;
            handbrake |= k.spaceKey.isPressed;
        }
        var g = Gamepad.current;
        if (g != null)
        {
            throttle += g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue();
            steer += g.leftStick.ReadValue().x;
            handbrake |= g.buttonSouth.isPressed;
        }
#else
        throttle = Input.GetAxis("Vertical");
        steer = Input.GetAxis("Horizontal");
        handbrake = Input.GetKey(KeyCode.Space);
#endif
        throttle = Mathf.Clamp(throttle, -1f, 1f);
        steer = Mathf.Clamp(steer, -1f, 1f);
    }
}
