using OpenworldCrime.Player;
using UnityEngine;

// The playable character's hurt and death sounds: a random hurt clip when damage lands (not more often than Hurt Interval, so
// automatic fire does not stack them), a random death clip when he dies (no hurt sound on the killing blow).
// Lives beside PlayerHealth on each playable character (Jason, Mia).
[DisallowMultipleComponent]
public class PlayerHurtSounds : MonoBehaviour
{
    [SerializeField] private PlayerHealth health;
    [Tooltip("One of these plays when the character takes damage.")]
    [SerializeField] private AudioClip[] hurtClips = new AudioClip[0];
    [Tooltip("One of these plays when the character dies.")]
    [SerializeField] private AudioClip[] deathClips = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] private float hurtVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float deathVolume = 1f;
    [Tooltip("Shortest time (s) between two hurt sounds.")]
    [SerializeField, Min(0f)] private float hurtInterval = 0.35f;
    [Tooltip("Damage below this is too small to make a sound.")]
    [SerializeField, Min(0f)] private float minDamage = 1f;

    private AudioSource source;
    private float nextHurt;
    private int lastHurt = -1, lastDeath = -1;

    private void Awake()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0.5f;   // mostly heard as the player's own voice, a little placed in the world
        source.minDistance = 3f;
        source.maxDistance = 40f;
        source.rolloffMode = AudioRolloffMode.Linear;
    }

    private void OnEnable()
    {
        if (health == null) return;
        health.OnDamaged += HandleDamaged;
        health.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        if (health == null) return;
        health.OnDamaged -= HandleDamaged;
        health.OnDied -= HandleDied;
    }

    private void HandleDamaged(Invector.vDamage damage)
    {
        // Invector reports the damage before the death event: the killing blow only gets the death sound.
        if (health.IsDead || damage == null || damage.damageValue < minDamage) return;
        if (Time.time < nextHurt) return;
        nextHurt = Time.time + hurtInterval;
        Play(hurtClips, ref lastHurt, hurtVolume);
    }

    private void HandleDied()
    {
        nextHurt = Time.time + 2f;
        source.Stop();
        Play(deathClips, ref lastDeath, deathVolume);
    }

    // A random clip, never the same one twice in a row.
    private void Play(AudioClip[] clips, ref int last, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == last) index = (index + 1) % clips.Length;
        last = index;
        if (clips[index] == null) return;
        source.pitch = Random.Range(0.95f, 1.05f);
        source.PlayOneShot(clips[index], volume);
    }
}
