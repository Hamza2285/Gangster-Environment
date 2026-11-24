using System;
using System.Collections.Generic;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Audio
{
    /// <summary>
    /// GTA-style car radio (global scene manager under ///Manager///, not a Singleton; UI listens to <see cref="OnStationChanged"/>).
    /// - Plays while the player is in a vehicle (any playable character: listens to their PlayerVehicleInteraction events), fades
    ///   out on exit. The first car gets a random station; later cars keep the last one.
    /// - Stations are "live": each has its own shuffled running order (songs, with a station ident now and then) that keeps
    ///   playing in the background on a virtual clock, so tuning back in lands in the middle of whatever is on air, like real radio.
    /// - Switching (keyboard: Next / Previous Station inputs; the radio bar's arrow buttons call NextStation / PreviousStation) plays
    ///   a short burst of tuning static, then the new station fades in. Cycle: stations ... -> Radio Off -> first station.
    /// - Only stations with songs are used (empty ones are skipped).
    /// Per-frame work while driving: one clip-finished check; nothing on foot.
    /// </summary>
    [DisallowMultipleComponent]
    public class RadioManager : MonoBehaviour
    {
        [Header("Stations")]
        [SerializeField] private RadioStation[] stations = new RadioStation[0];
        [Tooltip("Tuning static / dial noise played when the station changes (a short burst from a random point of one of them).")]
        [SerializeField] private AudioClip[] switchSounds = new AudioClip[0];

        [Header("References")]
        [Tooltip("The playable characters' vehicle interaction (Jason, Mia, ...).")]
        [SerializeField] private PlayerVehicleInteraction[] players = new PlayerVehicleInteraction[0];
        [SerializeField] private AudioSource music;
        [SerializeField] private AudioSource tuning;

        [Header("Feel")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float switchVolume = 0.5f;
        [Tooltip("Length (s) of the tuning static burst.")]
        [SerializeField] private Vector2 switchSeconds = new Vector2(0.45f, 0.7f);
        [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.6f;
        [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.5f;

        [Header("Input (keyboard / gamepad)")]
        [SerializeField] private GenericInput nextStationInput = new GenericInput("Period", "", "");
        [SerializeField] private GenericInput previousStationInput = new GenericInput("Comma", "", "");

        /// <summary>Raised when the station changes (null = radio off) and when the radio comes on in a vehicle.</summary>
        public event Action<RadioStation> OnStationChanged;
        /// <summary>Raised when the radio becomes available (player entered a vehicle) or not (left it).</summary>
        public event Action<bool> OnRadioActiveChanged;

        /// <summary>Current station, null when off.</summary>
        public RadioStation CurrentStation => current >= 0 ? usable[current] : null;
        public bool IsActive => inVehicle;
        /// <summary>Number of stations in use (with songs). The carousel adds a Radio Off slot after the last one.</summary>
        public int StationCount => usable.Count;
        /// <summary>Index of the station on air, -1 when off.</summary>
        public int CurrentIndex => current >= 0 ? current : -1;
        public RadioStation GetStation(int index) => index >= 0 && index < usable.Count ? usable[index] : null;

        // Runtime: running order per station (built once; the asset data is never changed).
        private sealed class Broadcast
        {
            public readonly List<AudioClip> items = new List<AudioClip>();
            public readonly List<double> starts = new List<double>();
            public double length;
            public double phase;
        }

        private readonly List<RadioStation> usable = new List<RadioStation>();
        private readonly List<Broadcast> broadcasts = new List<Broadcast>();
        private int current = -2; // -2: not chosen yet, -1: off
        private bool inVehicle;
        private float fadeStartAt, fadeFrom, fadeTo, fadeLength;
        private float tuneUntil;
        private AudioClip playingItem;
        private float seekTo = -1f, seekAskedAt; // streamed clips can only seek once their stream is open

        private void Awake()
        {
            if (music == null) { Debug.LogError($"[{nameof(RadioManager)}] No music AudioSource on '{name}'.", this); enabled = false; return; }
            foreach (RadioStation s in stations)
            {
                if (s == null || !s.HasSongs) continue;
                usable.Add(s);
                broadcasts.Add(BuildBroadcast(s, usable.Count));
            }
            music.loop = false;
            music.playOnAwake = false;
            music.volume = 0f;
        }

        private void OnEnable()
        {
            foreach (PlayerVehicleInteraction p in players)
            {
                if (p == null) continue;
                p.OnVehicleEntered += HandleEntered;
                p.OnVehicleExitStarted += HandleExited;
                p.OnVehicleBailStarted += HandleExited;
                p.OnVehicleExited += HandleExited;
            }
        }

        private void OnDisable()
        {
            foreach (PlayerVehicleInteraction p in players)
            {
                if (p == null) continue;
                p.OnVehicleEntered -= HandleEntered;
                p.OnVehicleExitStarted -= HandleExited;
                p.OnVehicleBailStarted -= HandleExited;
                p.OnVehicleExited -= HandleExited;
            }
            if (music != null) music.Stop();
        }

        // ------------------------------------------------------------------ controls

        /// <summary>Next station (after the last: Radio Off, then the first again).</summary>
        public void NextStation() => Step(+1);

        /// <summary>Previous station.</summary>
        public void PreviousStation() => Step(-1);

        /// <summary>Tunes to a station index (-1 = off).</summary>
        public void SetStation(int index)
        {
            if (usable.Count == 0) return;
            index = Mathf.Clamp(index, -1, usable.Count - 1);
            if (index == current) return;
            current = index;
            if (inVehicle) { PlayTuning(); TuneIn(); }
            OnStationChanged?.Invoke(CurrentStation);
        }

        private void Step(int dir)
        {
            if (!inVehicle || usable.Count == 0) return;
            // Positions: -1 (off), 0..n-1 (stations), cycling.
            int count = usable.Count + 1;
            int pos = (Mathf.Max(current, -1) + 1 + dir + count) % count;
            SetStation(pos - 1);
        }

        // ------------------------------------------------------------------ vehicle

        private void HandleEntered(OpenworldCrime.Vehicles.IVehicleController vehicle)
        {
            if (inVehicle || usable.Count == 0) return;
            inVehicle = true;
            if (current == -2) current = UnityEngine.Random.Range(0, usable.Count);
            TuneIn();
            OnRadioActiveChanged?.Invoke(true);
            OnStationChanged?.Invoke(CurrentStation);
        }

        private void HandleExited(OpenworldCrime.Vehicles.IVehicleController vehicle)
        {
            if (!inVehicle) return;
            inVehicle = false;
            Fade(0f, fadeOutSeconds);
            OnRadioActiveChanged?.Invoke(false);
        }

        // ------------------------------------------------------------------ playback

        private void Update()
        {
            if (inVehicle)
            {
                if (nextStationInput.GetButtonDown()) NextStation();
                else if (previousStationInput.GetButtonDown()) PreviousStation();
            }

            if (fadeLength > 0f)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - fadeStartAt) / fadeLength);
                music.volume = Mathf.Lerp(fadeFrom, fadeTo, k);
                if (k >= 1f)
                {
                    fadeLength = 0f;
                    if (fadeTo <= 0f) { music.Stop(); playingItem = null; }
                }
            }
            if (tuneUntil > 0f && Time.unscaledTime >= tuneUntil)
            {
                tuneUntil = 0f;
                if (tuning != null) tuning.Stop();
            }

            // A streamed clip can report the seek and then still open its stream from 0 (measured: position 208 s right after
            // Play, 2 s two seconds later). So for the first 3 s the position is kept on the station's clock, re-seeking when it
            // is more than a second off.
            if (seekTo >= 0f && music.clip != null && music.isPlaying)
            {
                float elapsed = Time.unscaledTime - seekAskedAt;
                float target = Mathf.Min(seekTo + elapsed, music.clip.length - 0.5f);
                if (elapsed > 3f) seekTo = -1f;
                else if (Mathf.Abs(music.time - target) > 1f) music.time = target;
            }

            // The item on air ended: carry on with what the station plays now.
            if (inVehicle && current >= 0 && playingItem != null && !music.isPlaying && fadeLength <= 0f) StartLiveItem(current, true);
        }

        private void TuneIn()
        {
            if (current < 0) { Fade(0f, 0.15f); return; }
            StartLiveItem(current, false);
            music.volume = 0f;
            // The new station comes in under the end of the tuning burst.
            Fade(musicVolume, fadeInSeconds);
        }

        private void StartLiveItem(int station, bool keepVolume)
        {
            Broadcast b = broadcasts[station];
            double t = (Time.unscaledTimeAsDouble + b.phase) % b.length;
            int i = b.starts.BinarySearch(t);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, b.items.Count - 1);
            AudioClip clip = b.items[i];
            float offset = (float)(t - b.starts[i]);
            if (offset > clip.length - 0.25f) { i = (i + 1) % b.items.Count; clip = b.items[i]; offset = 0f; }
            music.Stop();
            music.clip = clip;
            // Seek before Play by samples (works for streamed clips too; a seek by time right after Play can be dropped).
            music.timeSamples = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, offset) * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
            music.Play();
            seekTo = offset > 0.5f ? offset : -1f;
            seekAskedAt = Time.unscaledTime;
            playingItem = clip;
            if (keepVolume) music.volume = musicVolume;
        }

        private void PlayTuning()
        {
            if (tuning == null || switchSounds.Length == 0) return;
            AudioClip clip = switchSounds[UnityEngine.Random.Range(0, switchSounds.Length)];
            if (clip == null) return;
            float burst = Mathf.Min(UnityEngine.Random.Range(switchSeconds.x, switchSeconds.y), clip.length);
            tuning.clip = clip;
            tuning.volume = switchVolume;
            tuning.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, clip.length - burst));
            tuning.Play();
            tuneUntil = Time.unscaledTime + burst;
        }

        private void Fade(float to, float seconds)
        {
            fadeFrom = music.volume;
            fadeTo = to;
            fadeStartAt = Time.unscaledTime;
            fadeLength = Mathf.Max(0.01f, seconds);
        }

        // A shuffled running order: every song once per cycle, an ident between some of them.
        private static Broadcast BuildBroadcast(RadioStation s, int seed)
        {
            var rnd = new System.Random(seed * 7919 + s.DisplayName.Length);
            var songs = new List<AudioClip>();
            foreach (AudioClip c in s.Songs) if (c != null) songs.Add(c);
            for (int i = songs.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); AudioClip x = songs[i]; songs[i] = songs[j]; songs[j] = x; }
            var b = new Broadcast();
            double t = 0;
            foreach (AudioClip song in songs)
            {
                b.items.Add(song); b.starts.Add(t); t += song.length;
                if (s.Idents.Length > 0 && rnd.NextDouble() < s.IdentChance)
                {
                    AudioClip ident = s.Idents[rnd.Next(s.Idents.Length)];
                    if (ident != null) { b.items.Add(ident); b.starts.Add(t); t += ident.length; }
                }
            }
            b.length = Math.Max(1.0, t);
            b.phase = rnd.NextDouble() * b.length;
            return b;
        }

#if UNITY_EDITOR
        public void EditorSetup(RadioStation[] stationAssets, AudioClip[] tuningClips, PlayerVehicleInteraction[] playerInteractions, AudioSource musicSource, AudioSource tuningSource)
        {
            stations = stationAssets; switchSounds = tuningClips; players = playerInteractions; music = musicSource; tuning = tuningSource;
        }
#endif
    }
}
