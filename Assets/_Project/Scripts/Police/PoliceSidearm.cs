using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Visuals and sound of a police gun (officer's pistol, or a gun point on a police car): muzzle flash, a short tracer and the
    /// shot sound. Hit or miss and damage are decided by the unit; this only shows the shot. Update runs only for the few frames
    /// the flash and tracer are visible.
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceSidearm : MonoBehaviour
    {
        [SerializeField] private Transform muzzle;
        [Tooltip("Optional muzzle flash object (quad / light), shown for Flash Seconds.")]
        [SerializeField] private GameObject muzzleFlash;
        [Tooltip("Optional two-point tracer line, shown for Tracer Seconds.")]
        [SerializeField] private LineRenderer tracer;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] shotClips = new AudioClip[0];
        [SerializeField, Min(0f)] private float flashSeconds = 0.05f;
        [SerializeField, Min(0f)] private float tracerSeconds = 0.06f;
        [SerializeField] private Vector2 pitchRange = new Vector2(0.92f, 1.08f);

        public Transform Muzzle => muzzle != null ? muzzle : transform;

        private float flashUntil;
        private float tracerUntil;

        private void Awake()
        {
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            if (tracer != null)
            {
                tracer.positionCount = 2;
                tracer.useWorldSpace = true;
                tracer.enabled = false;
            }
            enabled = false;
        }

        private void OnDisable()
        {
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            if (tracer != null) tracer.enabled = false;
        }

        /// <summary>Shows one shot from the muzzle towards <paramref name="impact"/>.</summary>
        public void Fire(Vector3 impact)
        {
            float now = Time.time;
            Vector3 from = Muzzle.position;
            if (muzzleFlash != null)
            {
                muzzleFlash.SetActive(true);
                flashUntil = now + flashSeconds;
            }
            if (tracer != null)
            {
                tracer.SetPosition(0, from);
                tracer.SetPosition(1, impact);
                tracer.enabled = true;
                tracerUntil = now + tracerSeconds;
            }
            if (audioSource != null && shotClips.Length > 0)
            {
                audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
                audioSource.PlayOneShot(shotClips[Random.Range(0, shotClips.Length)]);
            }
            enabled = true;
        }

        private void Update()
        {
            float now = Time.time;
            if (muzzleFlash != null && muzzleFlash.activeSelf && now >= flashUntil) muzzleFlash.SetActive(false);
            if (tracer != null && tracer.enabled && now >= tracerUntil) tracer.enabled = false;
            if ((muzzleFlash == null || !muzzleFlash.activeSelf) && (tracer == null || !tracer.enabled)) enabled = false;
        }
    }
}
