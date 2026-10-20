using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Crash sounds for vehicles whose own controller does not play any: iTS traffic cars (TSSimpleCar has no collision audio)
    /// and the custom drivable cars (RCC only plays its crash clips inside its damage code, which is off on those prefabs).
    /// Original RCC vehicles keep RCC's own crash sound and do not need this component.
    /// - Intensity comes from the contact's normal relative speed (side contacts count fully, ground-like contacts along the
    ///   vehicle's up axis are scaled down), so grazes and parking bumps stay quiet and only real hits are loud.
    /// - Below Min Impact Speed nothing plays; a short cooldown stops sustained contact (scraping, wedged cars) from spamming.
    /// - Two vehicles that both carry this component receive the same collision: only one of them plays it.
    /// Lives on the vehicle root (next to the Rigidbody, which receives OnCollisionEnter). Pooling-safe: state resets in OnEnable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleCollisionAudio : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("3D source on the vehicle used for crash sounds (spatial blend 1, sensible min/max distance).")]
        [SerializeField] private AudioSource source;
        [Tooltip("Crash clips (the project uses RCC's crash clips).")]
        [SerializeField] private AudioClip[] crashClips = new AudioClip[0];

        [Header("Intensity")]
        [Tooltip("Layers that make a crash sound when hit (world, other vehicles). Characters are handled by VehicleCharacterImpact.")]
        [SerializeField] private LayerMask impactLayers = (1 << 0) | (1 << 13) | (1 << 19) | (1 << 20);
        [Tooltip("Impacts slower than this (m/s, along the contact normal) are silent.")]
        [SerializeField, Min(0f)] private float minImpactSpeed = 2.5f;
        [Tooltip("Share of the impact speed that counts for contacts along the vehicle's up axis (landings, kerbs).")]
        [SerializeField, Range(0f, 1f)] private float verticalContactScale = 0.3f;
        [Tooltip("Impact speed (m/s) at which the sound reaches full volume.")]
        [SerializeField, Min(0.1f)] private float fullVolumeSpeed = 16f;
        [SerializeField, Range(0f, 1f)] private float minVolume = 0.12f;
        [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;
        [Tooltip("Pitch for the lightest audible hit; heavy hits play at pitch 1.")]
        [SerializeField, Range(0.5f, 2f)] private float lightHitPitch = 1.2f;
        [Tooltip("Random pitch variation (+/-).")]
        [SerializeField, Range(0f, 0.3f)] private float pitchVariation = 0.06f;

        [Header("Spam Protection")]
        [Tooltip("Seconds after a crash sound during which further contacts are ignored, unless clearly harder.")]
        [SerializeField, Min(0f)] private float cooldown = 0.35f;
        [Tooltip("During the cooldown, a hit this many times harder than the last one still plays.")]
        [SerializeField, Min(1f)] private float overrideFactor = 2f;

        private Rigidbody body;
        private float lastPlayTime = -100f;
        private float lastPlaySpeed;
        private bool isValid;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            isValid = source != null && crashClips != null && crashClips.Length > 0;
            if (!isValid)
                Debug.LogWarning($"[{nameof(VehicleCollisionAudio)}] '{name}' has no AudioSource or crash clips; it plays no crash sounds.", this);
        }

        private void OnEnable()
        {
            lastPlayTime = -100f;
            lastPlaySpeed = 0f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isValid || collision.contactCount == 0) return;
            // The hit collider's own layer (a traffic car's root is on "Traffic AI", its body collider on "Vehicle").
            if ((impactLayers.value & (1 << collision.collider.gameObject.layer)) == 0) return;

            // The same collision reaches both vehicles: the one with the lower instance id plays it.
            Rigidbody otherBody = collision.rigidbody;
            if (otherBody != null && otherBody != body)
            {
                VehicleCollisionAudio other = otherBody.GetComponent<VehicleCollisionAudio>();
                if (other != null && other.isActiveAndEnabled && other.isValid && other.GetInstanceID() < GetInstanceID()) return;
            }

            float speed = GetImpactSpeed(collision, transform.up, verticalContactScale);
            if (speed < minImpactSpeed) return;

            float sinceLast = Time.time - lastPlayTime;
            if (sinceLast < cooldown && speed < lastPlaySpeed * overrideFactor) return;

            float t = Mathf.InverseLerp(minImpactSpeed, fullVolumeSpeed, speed);
            source.pitch = Mathf.Lerp(lightHitPitch, 1f, t) + Random.Range(-pitchVariation, pitchVariation);
            source.transform.position = collision.GetContact(0).point;
            source.PlayOneShot(crashClips[Random.Range(0, crashClips.Length)], Mathf.Lerp(minVolume, maxVolume, t * t));
            lastPlayTime = Time.time;
            lastPlaySpeed = speed;
        }

        /// <summary>
        /// Relative speed along the contact normal (m/s). Contacts along the vehicle's up axis (landing, kerbs, scraping the
        /// road) are scaled down to Vertical Contact Scale, so only side/front/rear hits count fully.
        /// </summary>
        public static float GetImpactSpeed(Collision collision, Vector3 vehicleUp, float verticalContactScale)
        {
            Vector3 normal = collision.GetContact(0).normal;
            float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            float horizontal = 1f - Mathf.Abs(Vector3.Dot(vehicleUp, normal));
            return normalSpeed * Mathf.Lerp(verticalContactScale, 1f, horizontal);
        }
    }
}
