using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Applies a <see cref="VehicleSpec"/> to the vehicle it sits on when it is created (Awake, before RCC builds its gear
    /// ratios in Start). Works on drivable (RCC) cars and on traffic cars (rigidbody mass only; iTS drives by lane speed).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class VehicleSpecApplier : MonoBehaviour
    {
        [SerializeField] private VehicleSpec spec;
        [SerializeField] private RCC_CarControllerV3 carController;
        [SerializeField] private Rigidbody body;

        public VehicleSpec Spec => spec;

        private void Awake()
        {
            if (spec == null)
            {
                Debug.LogWarning($"[{nameof(VehicleSpecApplier)}] No spec on '{name}'; the prefab's own values are used.", this);
                return;
            }
            if (body != null) body.mass = spec.Mass;
            if (carController == null) return;

            carController.maxspeed = spec.MaxSpeed;
            carController.maxEngineTorque = spec.EngineTorque;
            carController.brakeTorque = spec.BrakeTorque;
            carController.steerAngle = spec.SteerAngle;
            carController.highspeedsteerAngle = spec.HighSpeedSteerAngle;
            carController.highspeedsteerAngleAtspeed = spec.MaxSpeed * 0.8f;
            carController.downForce = spec.DownForce;
            carController.wheelTypeChoise = spec.DriveType;
        }

#if UNITY_EDITOR
        public void EditorAssign(VehicleSpec newSpec, RCC_CarControllerV3 controller, Rigidbody rigidBody)
        {
            spec = newSpec; carController = controller; body = rigidBody;
        }
#endif
    }
}
