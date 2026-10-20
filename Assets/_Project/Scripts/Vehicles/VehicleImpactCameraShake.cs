using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Small, short camera shake when the vehicle the player drives hits a character (<see cref="VehicleCharacterImpact.OnCharacterHit"/>).
    /// Lives on the camera object of the scene RCC camera (RCCCamera/Pivot/Camera): RCC_Camera moves its root and pivot,
    /// this only offsets the camera's local position while a shake is running. The component is disabled (no LateUpdate)
    /// between shakes.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleImpactCameraShake : MonoBehaviour
    {
        [Tooltip("Shake amplitude (m) at Full Shake Speed.")]
        [SerializeField, Min(0f)] private float maxAmplitude = 0.06f;
        [Tooltip("Impact speed (m/s) giving the full amplitude; slower hits shake less.")]
        [SerializeField, Min(0.1f)] private float fullShakeSpeed = 12f;
        [SerializeField, Min(0.05f)] private float duration = 0.25f;
        [Tooltip("Shake oscillations per second.")]
        [SerializeField, Min(1f)] private float frequency = 30f;

        private float amplitude;
        private float timeLeft;
        private float seed;

        private void Awake()
        {
            VehicleCharacterImpact.OnCharacterHit += HandleCharacterHit;
            enabled = false;
        }

        private void OnDestroy()
        {
            VehicleCharacterImpact.OnCharacterHit -= HandleCharacterHit;
        }

        private void OnDisable()
        {
            transform.localPosition = Vector3.zero;
        }

        private void HandleCharacterHit(VehicleImpact impact)
        {
            if (!gameObject.activeInHierarchy || impact.Vehicle == null) return;
            RCC_CarControllerV3 driven = RCC_SceneManager.Instance != null ? RCC_SceneManager.Instance.activePlayerVehicle : null;
            if (driven == null || driven.transform != impact.Vehicle || !driven.canControl) return;

            float strength = Mathf.Clamp01(impact.Speed / fullShakeSpeed);
            amplitude = Mathf.Max(amplitude * (timeLeft / duration), maxAmplitude * strength);
            timeLeft = duration;
            seed = Random.value * 100f;
            enabled = true;
        }

        private void LateUpdate()
        {
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f)
            {
                enabled = false;
                return;
            }
            float decay = timeLeft / duration;
            float t = Time.time * frequency;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(seed, t) - 0.5f, Mathf.PerlinNoise(seed + 13f, t) - 0.5f, 0f) * (2f * amplitude * decay);
            transform.localPosition = offset;
        }
    }
}
