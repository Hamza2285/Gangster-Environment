using System.Collections.Generic;
using UnityEngine;

// Car horns: a short two-tone honk (made in code, no audio asset needed) played from a car.
// Limits how many horns sound at once and only plays near the listener, so jams don't turn into noise.
public static class CarHorn
{
    public static int MaxSimultaneous = 3;
    public static float MaxDistance = 90f;
    public static int HonksPlayed { get; private set; }

    private static AudioClip _short, _double;
    private static readonly Dictionary<Transform, AudioSource> _sources = new Dictionary<Transform, AudioSource>();

    /// Honks from this car. Returns false if it was too far away or too many horns are already sounding.
    public static bool Honk(Transform car, bool twice = false)
    {
        if (car == null) return false;
        var listener = Object.FindFirstObjectByType<AudioListener>();
        if (listener != null && (listener.transform.position - car.position).sqrMagnitude > MaxDistance * MaxDistance) return false;

        int playing = 0;
        var dead = new List<Transform>();
        foreach (var kv in _sources)
        {
            if (kv.Key == null || kv.Value == null) { dead.Add(kv.Key); continue; }
            if (kv.Value.isPlaying) playing++;
        }
        foreach (var d in dead) _sources.Remove(d);
        if (playing >= MaxSimultaneous) return false;

        AudioSource source;
        if (!_sources.TryGetValue(car, out source) || source == null)
        {
            var go = new GameObject("Horn");
            go.transform.SetParent(car, false);
            go.transform.localPosition = new Vector3(0f, 0.8f, 1.5f);
            source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 6f;
            source.maxDistance = MaxDistance;
            source.playOnAwake = false;
            // Every car sounds a little different.
            source.pitch = Random.Range(0.85f, 1.15f);
            _sources[car] = source;
        }
        if (source.isPlaying) return false;
        if (_short == null) { _short = Build(false); _double = Build(true); }
        source.clip = twice ? _double : _short;
        source.volume = 0.55f;
        source.Play();
        HonksPlayed++;
        return true;
    }

    // Two detuned tones a major third apart with a little grit, like a real dual-note horn.
    private static AudioClip Build(bool twice)
    {
        const int rate = 22050;
        float beep = 0.32f, gap = 0.12f;
        float length = twice ? beep * 2f + gap : beep * 1.4f;
        int n = Mathf.CeilToInt(length * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float local;
            if (!twice) local = t;
            else if (t < beep) local = t;
            else if (t < beep + gap) { data[i] = 0f; continue; }
            else local = t - beep - gap;
            float len = twice ? beep : length;
            float env = Mathf.Clamp01(local / 0.015f) * Mathf.Clamp01((len - local) / 0.03f);
            float a = Mathf.Sin(2f * Mathf.PI * 420f * t), b = Mathf.Sin(2f * Mathf.PI * 530f * t);
            float s = Mathf.Clamp((a + b) * 1.6f, -1f, 1f) * 0.6f + (a + b) * 0.2f;   // soft clipping = brassy
            data[i] = s * env * 0.5f;
        }
        var clip = AudioClip.Create(twice ? "HornDouble" : "Horn", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
