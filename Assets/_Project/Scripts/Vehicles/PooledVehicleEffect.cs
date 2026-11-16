using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// One pooled car effect (smoke, fire, explosion), handed out by <see cref="VehicleEffectPool"/>. It is never parented to the car:
    /// it follows its anchor's position (kept upright, so smoke always rises) and returns itself to the pool when the anchor goes
    /// inactive, so a car can be pooled or destroyed at any moment without leaving effects behind or changing its own hierarchy.
    /// Optionally flashes one light for a moment when it starts (the explosion). Added at runtime when a prefab does not carry it.
    /// LateUpdate only runs while it follows something or flashes.
    /// </summary>
    [DisallowMultipleComponent]
    public class PooledVehicleEffect : MonoBehaviour
    {
        [Tooltip("Plays one-shot sounds (the explosion). Optional: looping sounds of the prefab's own AudioSources play on their own.")]
        [SerializeField] private AudioSource oneShotSource;

        [Header("Flash (optional)")]
        [Tooltip("Light switched on at full intensity when the effect starts, faded out over Flash Duration, then switched off.")]
        [SerializeField] private Light flashLight;
        [SerializeField, Min(0f)] private float flashIntensity = 8f;
        [SerializeField, Min(0.01f)] private float flashDuration = 0.25f;

        /// <summary>The prefab this instance was made from (the pool key).</summary>
        public GameObject Prefab { get; private set; }
        public bool InUse { get; private set; }

        private ParticleSystem[] systems = new ParticleSystem[0];
        private AudioSource[] audioSources = new AudioSource[0];
        private Transform follow;
        private bool following; // follow can turn null by itself (anchor destroyed): that ends the effect too
        private Vector3 followOffset;
        private float flashTimeLeft;
        private Coroutine releaseRoutine;

        internal void Initialize(GameObject prefab)
        {
            Prefab = prefab;
            systems = GetComponentsInChildren<ParticleSystem>(true);
            audioSources = GetComponentsInChildren<AudioSource>(true);
            if (flashLight != null) flashLight.enabled = false;
            enabled = false;
        }

        internal void Activate()
        {
            InUse = true;
            follow = null;
            following = false;
            flashTimeLeft = 0f;
            if (flashLight != null)
            {
                flashTimeLeft = flashDuration;
                flashLight.intensity = flashIntensity;
                flashLight.enabled = true;
            }
            RefreshEnabled();
        }

        /// <summary>Keeps the effect on the anchor (position only; offset in the anchor's space) until it fades out or is released.</summary>
        public void Follow(Transform anchor, Vector3 localOffset)
        {
            follow = anchor;
            following = anchor != null;
            followOffset = localOffset;
            if (anchor != null) transform.SetPositionAndRotation(FollowPoint, Quaternion.identity);
            RefreshEnabled();
        }

        public void PlayOneShot(AudioClip clip)
        {
            if (clip == null || oneShotSource == null) return;
            oneShotSource.PlayOneShot(clip);
        }

        /// <summary>Stops emitting where it is and goes back to the pool once the last particles have had time to die.</summary>
        public void FadeOut(float seconds)
        {
            if (!InUse) return;
            follow = null;
            following = false;
            RefreshEnabled();
            for (int i = 0; i < systems.Length; i++) systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i].loop) audioSources[i].Stop();
            }
            ReleaseAfter(seconds);
        }

        /// <summary>Plays for playSeconds, then fades out over fadeSeconds (one-shot effects with lingering smoke).</summary>
        public void FadeOutAfter(float playSeconds, float fadeSeconds)
        {
            if (!InUse) return;
            if (releaseRoutine != null) StopCoroutine(releaseRoutine);
            if (!gameObject.activeInHierarchy) Release();
            else releaseRoutine = StartCoroutine(FadeOutRoutine(playSeconds, fadeSeconds));
        }

        /// <summary>Back to the pool after the given time.</summary>
        public void ReleaseAfter(float seconds)
        {
            if (!InUse) return;
            if (releaseRoutine != null) StopCoroutine(releaseRoutine);
            // The component itself is disabled when it has nothing to update; only an inactive object cannot run the timer.
            if (seconds <= 0f || !gameObject.activeInHierarchy) Release();
            else releaseRoutine = StartCoroutine(ReleaseRoutine(seconds));
        }

        /// <summary>Back to the pool at once.</summary>
        public void Release()
        {
            if (!InUse) return;
            InUse = false;
            follow = null;
            following = false;
            flashTimeLeft = 0f;
            if (flashLight != null) flashLight.enabled = false;
            enabled = false;
            if (releaseRoutine != null) StopCoroutine(releaseRoutine);
            releaseRoutine = null;
            for (int i = 0; i < systems.Length; i++) systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            VehicleEffectPool.Return(this);
        }

        private Vector3 FollowPoint => follow.position + follow.rotation * followOffset;

        private void RefreshEnabled() => enabled = InUse && (following || flashTimeLeft > 0f);

        private IEnumerator ReleaseRoutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            releaseRoutine = null;
            Release();
        }

        private IEnumerator FadeOutRoutine(float playSeconds, float fadeSeconds)
        {
            yield return new WaitForSeconds(playSeconds);
            releaseRoutine = null;
            FadeOut(fadeSeconds);
        }

        private void LateUpdate()
        {
            if (flashTimeLeft > 0f)
            {
                flashTimeLeft -= Time.deltaTime;
                float share = Mathf.Clamp01(flashTimeLeft / flashDuration);
                flashLight.intensity = flashIntensity * share * share;
                if (flashTimeLeft <= 0f) flashLight.enabled = false;
            }

            if (following)
            {
                if (follow == null || !follow.gameObject.activeInHierarchy)
                {
                    Release();
                    return;
                }
                transform.position = FollowPoint;
            }
            RefreshEnabled();
        }
    }
}
