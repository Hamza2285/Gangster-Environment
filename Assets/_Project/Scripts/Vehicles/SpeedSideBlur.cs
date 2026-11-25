using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// A little motion blur at the left and right of the screen while the player drives near the vehicle's top speed
    /// (full-screen pass "OpenworldCrime/Speed Side Blur" on the URP renderers). The pass is off, at no cost, the rest of the time.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeedSideBlur : MonoBehaviour
    {
        [Tooltip("The blur pass on each URP renderer (filled by the setup).")]
        [SerializeField] private ScriptableRendererFeature[] features = new ScriptableRendererFeature[0];
        [Tooltip("Share of the vehicle's top speed where the blur starts / is full.")]
        [SerializeField, Range(0f, 1f)] private float startShare = 0.7f;
        [SerializeField, Range(0f, 1f)] private float fullShare = 0.95f;
        [Tooltip("Never below this speed (km/h), and full at this speed whatever the vehicle's top speed.")]
        [SerializeField, Min(0f)] private float minSpeed = 100f;
        [SerializeField, Min(1f)] private float fullSpeed = 190f;
        [SerializeField, Range(0f, 1f)] private float strength = 0.8f;

        private static readonly int AmountId = Shader.PropertyToID("_SpeedSideBlur");
        private PlayerVehicleInteraction[] drivers = new PlayerVehicleInteraction[0];
        private float amount, nextScan;
        private bool on;

        /// <summary>Blur shown now (0..1).</summary>
        public float Amount => amount;

        private void OnEnable() => Switch(false);

        private void OnDisable()
        {
            amount = 0f;
            Switch(false);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                drivers = FindObjectsByType<PlayerVehicleInteraction>(FindObjectsSortMode.None);
            }
            float target = 0f;
            foreach (PlayerVehicleInteraction driver in drivers)
            {
                if (driver == null || driver.State != VehicleInteractionState.InVehicle) continue;
                var vehicle = driver.CurrentVehicle as Component;
                if (vehicle == null) continue;
                var car = vehicle.GetComponent<RCC_CarControllerV3>();
                var body = vehicle.GetComponent<Rigidbody>();
                if (car == null || body == null) continue;
                float speed = body.linearVelocity.magnitude * 3.6f;
                if (speed < minSpeed) continue;
                float share = Mathf.InverseLerp(startShare, fullShare, speed / Mathf.Max(1f, car.maxspeed));
                target = Mathf.Max(share, Mathf.InverseLerp(minSpeed + 40f, fullSpeed, speed)) * strength;
            }
            amount = Mathf.MoveTowards(amount, target, Time.deltaTime * 1.5f);
            Switch(amount > 0.02f);
            if (on) Shader.SetGlobalFloat(AmountId, amount);
        }

        private void Switch(bool value)
        {
            on = value;
            for (int i = 0; i < features.Length; i++)
                if (features[i] != null && features[i].isActive != value) features[i].SetActive(value);
        }

#if UNITY_EDITOR
        public void EditorSetup(ScriptableRendererFeature[] passes) => features = passes;
#endif
    }
}
