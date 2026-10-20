using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    public enum VehicleClass { Compact, Sedan, Sports, Super, SUV, Van, Bus, Emergency }

    /// <summary>
    /// Authored definition of one car model: which FBX it uses, its real-world size, whether it appears in traffic, and
    /// its handling. Pure configuration (never changed at runtime). The editor builder (Tools > OpenworldCrime > Vehicles >
    /// Build Vehicles From Specs) creates the Traffic_ and Drivable_ prefabs from it; <see cref="VehicleSpecApplier"/>
    /// pushes the handling values into RCC / the rigidbody when a car spawns, so tuning a spec changes the car without a rebuild.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Vehicle Spec", fileName = "VehicleSpec")]
    public class VehicleSpec : ScriptableObject
    {
        [Header("Model")]
        [SerializeField] private string displayName = "Car";
        [SerializeField] private GameObject model;
        [SerializeField] private VehicleClass vehicleClass = VehicleClass.Sedan;
        [Tooltip("Real bumper-to-bumper length (m). The model is scaled to it so every car matches the player and each other.")]
        [SerializeField, Min(2f)] private float targetLength = 4.8f;

        [Header("Use")]
        [SerializeField] private bool useInTraffic = true;
        [Tooltip("Relative spawn weight in traffic (higher = more common).")]
        [SerializeField, Range(1, 20)] private int trafficFrequency = 5;

        [Header("Handling (drivable)")]
        [SerializeField, Min(200f)] private float mass = 1500f;
        [Tooltip("Top speed (km/h).")]
        [SerializeField, Min(30f)] private float maxSpeed = 200f;
        [Tooltip("Engine torque (Nm) - acceleration.")]
        [SerializeField, Min(50f)] private float engineTorque = 300f;
        [SerializeField, Min(100f)] private float brakeTorque = 2500f;
        [Tooltip("Steer angle at low speed (deg) - how sharp it turns.")]
        [SerializeField, Range(10f, 50f)] private float steerAngle = 38f;
        [Tooltip("Steer angle at top speed (deg) - stability at high speed.")]
        [SerializeField, Range(2f, 25f)] private float highSpeedSteerAngle = 8f;
        [Tooltip("Downforce with speed; sports cars grip better at speed.")]
        [SerializeField, Min(0f)] private float downForce = 25f;
        [SerializeField] private RCC_CarControllerV3.WheelType driveType = RCC_CarControllerV3.WheelType.RWD;
        [Tooltip("Centre of mass height above the wheel centres (m). Lower = more stable.")]
        [SerializeField] private float centerOfMassHeight = 0.05f;

        [Header("Damage")]
        [Tooltip("Dent depth multiplier on impact (1 = default).")]
        [SerializeField, Range(0f, 3f)] private float damageMultiplier = 1f;

        public string DisplayName => displayName;
        public GameObject Model => model;
        public VehicleClass Class => vehicleClass;
        public float TargetLength => targetLength;
        public bool UseInTraffic => useInTraffic;
        public int TrafficFrequency => trafficFrequency;
        public float Mass => mass;
        public float MaxSpeed => maxSpeed;
        public float EngineTorque => engineTorque;
        public float BrakeTorque => brakeTorque;
        public float SteerAngle => steerAngle;
        public float HighSpeedSteerAngle => highSpeedSteerAngle;
        public float DownForce => downForce;
        public RCC_CarControllerV3.WheelType DriveType => driveType;
        public float CenterOfMassHeight => centerOfMassHeight;
        public float DamageMultiplier => damageMultiplier;

#if UNITY_EDITOR
        /// <summary>Points the spec at a (re-exported) model, keeping every tuned handling value.</summary>
        public void EditorSetModel(GameObject newModel) => model = newModel;

        /// <summary>Editor only: lets the builder fill a new spec from a preset.</summary>
        public void EditorSetup(string newName, GameObject newModel, VehicleClass cls, float length, bool traffic, int frequency,
            float newMass, float speed, float torque, float brake, float steer, float highSteer, float down,
            RCC_CarControllerV3.WheelType drive)
        {
            displayName = newName; model = newModel; vehicleClass = cls; targetLength = length; useInTraffic = traffic;
            trafficFrequency = frequency; mass = newMass; maxSpeed = speed; engineTorque = torque; brakeTorque = brake;
            steerAngle = steer; highSpeedSteerAngle = highSteer; downForce = down; driveType = drive;
        }
#endif
    }
}
