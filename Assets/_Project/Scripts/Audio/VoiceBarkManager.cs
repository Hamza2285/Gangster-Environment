using System.Collections;
using System.Collections.Generic;
using Invector;
using OpenworldCrime.Interiors;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using OpenworldCrime.Police;
using OpenworldCrime.Racing;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Audio
{
    /// <summary>
    /// Voice barks (global manager under ///Manager///): street people, the player (Jason / Mia, whoever is played) and the police
    /// speak now and then, in 3D at the speaker, from the <see cref="VoiceBank"/>.
    /// - Approaching people (a light check every 0.4 s): someone in a standing group (or now and then someone alone) greets,
    ///   grumbles, stares ("You looking at me?") or is annoyed; frightened when the player holds a gun; impressed by the car when
    ///   the player rolls up slowly. The player sometimes answers.
    /// - Events (existing events, no polling): punched / shot people (hurt, witnesses call the police or stay out of it),
    ///   gunshots (panic), a death (shock), a stolen car ("That's my car!"), fights starting, the wanted level going up / clearing,
    ///   race start / win, hard crashes.
    /// - Police: the nearest officer on foot calls out when close ("Police! Stop right there!", "Drop your weapon!"), during an
    ///   arrest ("Get on the ground!"), radios backup when it gets serious and reports a fleeing suspect in a chase.
    /// Limits: at most two street / police voices and one player voice at once, nobody talks over himself, cooldowns per
    /// situation, per speaker and overall, so it stays occasional. Silent indoors (club, shops) and while the game is paused.
    /// Each pedestrian keeps one of the two voices of his gender for as long as he exists.
    /// </summary>
    [DisallowMultipleComponent]
    public class VoiceBarkManager : MonoBehaviour
    {
        [SerializeField] private VoiceBank bank;
        [Tooltip("Race events (start / win). Empty = the scene's race manager, found once at start.")]
        [SerializeField] private RaceManager races;

        [Header("Audio")]
        [SerializeField, Range(0f, 1f)] private float npcVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float playerVolume = 0.9f;
        [SerializeField, Min(0.5f)] private float minDistance = 3f;
        [SerializeField, Min(2f)] private float maxDistance = 38f;
        [SerializeField, Range(1, 4)] private int maxNpcVoices = 2;

        [Header("Approach barks")]
        [SerializeField, Min(0.1f)] private float checkInterval = 0.4f;
        [Tooltip("Distance (m) at which people on foot react to the player.")]
        [SerializeField, Min(1f)] private float approachRange = 5.5f;
        [Tooltip("Distance (m) at which people react to the player's car rolling by slowly.")]
        [SerializeField, Min(1f)] private float carRange = 9f;
        [SerializeField, Min(0f)] private float slowCarSpeed = 6f;
        [Tooltip("People within this distance of each other count as a group.")]
        [SerializeField, Min(0.5f)] private float groupRadius = 3.5f;
        [SerializeField, Range(0f, 1f)] private float groupChance = 0.8f;
        [SerializeField, Range(0f, 1f)] private float aloneChance = 0.3f;
        [SerializeField, Range(0f, 1f)] private float playerReplyChance = 0.35f;

        [Header("Cooldowns (s)")]
        [SerializeField, Min(0f)] private float approachCooldown = 8f;
        [SerializeField, Min(0f)] private float personCooldown = 45f;
        [SerializeField, Min(0f)] private float eventCooldown = 3f;
        [SerializeField, Min(0f)] private float playerCooldown = 12f;
        [SerializeField, Min(0f)] private float policeCooldown = 6f;

        [Tooltip("Logs every line played (situation, clip, speaker) for tuning.")]
        [SerializeField] private bool logLines;

        private class Source
        {
            public AudioSource audio;
            public Transform speaker;
            public float endTime;
        }

        private readonly List<Source> npcSources = new List<Source>();
        private Source playerSource;
        private readonly Dictionary<int, float> personNext = new Dictionary<int, float>();
        private readonly HashSet<int> greeted = new HashSet<int>();
        private readonly Dictionary<VoiceLine, float> situationNext = new Dictionary<VoiceLine, float>();
        private readonly HashSet<int> fightAnswered = new HashSet<int>();
        private readonly List<PedestrianDamage> nearby = new List<PedestrianDamage>();
        private float nextCheck, nextApproach, nextPlayer, nextPolice, nextEvent;
        private AudioClip lastClip;

        private PlayableCharacter player;
        private PlayerVehicleInteraction vehicleInteraction;
        private PlayerCombatController combat;
        private VehicleHealth watchedVehicle;
        private int lastStars;

        private void Awake()
        {
            if (bank == null) { Debug.LogError($"[{nameof(VoiceBarkManager)}] No VoiceBank on '{name}'.", this); enabled = false; return; }
            for (int i = 0; i < maxNpcVoices; i++) npcSources.Add(new Source { audio = CreateSource("Voice " + (i + 1), 1f) });
            playerSource = new Source { audio = CreateSource("Player Voice", 0f) };
        }

        private AudioSource CreateSource(string sourceName, float spatial)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = spatial;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = minDistance;
            a.maxDistance = maxDistance;
            a.dopplerLevel = 0f;
            a.priority = 64;
            return a;
        }

        private void Start()
        {
            if (races == null) races = RaceManager.Instance;
            if (races != null)
            {
                races.OnCountdown += HandleCountdown;
                races.OnFinished += HandleRaceFinished;
            }
        }

        private void OnEnable()
        {
            PedestrianDamage.OnAnyDamaged += HandlePedestrianDamaged;
            PedestrianDamage.OnAnyDied += HandlePedestrianDied;
            WantedManager.OnCrimeWitnessed += HandleCrime;
            WantedManager.OnWantedChanged += HandleWanted;
            WantedManager.OnBustProgressChanged += HandleBustProgress;
        }

        private void OnDisable()
        {
            PedestrianDamage.OnAnyDamaged -= HandlePedestrianDamaged;
            PedestrianDamage.OnAnyDied -= HandlePedestrianDied;
            WantedManager.OnCrimeWitnessed -= HandleCrime;
            WantedManager.OnWantedChanged -= HandleWanted;
            WantedManager.OnBustProgressChanged -= HandleBustProgress;
            WatchVehicle(null);
        }

        private void OnDestroy()
        {
            if (races == null) return;
            races.OnCountdown -= HandleCountdown;
            races.OnFinished -= HandleRaceFinished;
        }

        // ---------------------------------------------------------------- per tick

        private void Update()
        {
            // Sources follow their speakers while talking.
            for (int i = 0; i < npcSources.Count; i++) Follow(npcSources[i]);
            Follow(playerSource);

            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            FindPlayer();
            if (player == null || Muted) return;
            WatchVehicle(vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null
                ? vehicleInteraction.CurrentVehicle.transform.GetComponent<VehicleHealth>() : null);
            CheckApproach();
            CheckPolice();
        }

        private static void Follow(Source s)
        {
            if (s.speaker == null || !s.audio.isPlaying) return;
            s.audio.transform.position = s.speaker.position + Vector3.up * 1.6f;
        }

        private bool Muted =>
            Time.timeScale < 0.5f || (InteriorTravelManager.Instance != null && (InteriorTravelManager.Instance.IsInside || InteriorTravelManager.Instance.IsTransitioning));

        // The character being played (the active one; its PlayableCharacter component is switched off while it drives).
        private void FindPlayer()
        {
            if (player != null && player.gameObject.activeInHierarchy) return;
            player = null;
            foreach (PlayableCharacter c in PlayableCharacter.All)
                if (c != null && c.gameObject.activeInHierarchy) { player = c; break; }
            vehicleInteraction = player != null ? player.GetComponent<PlayerVehicleInteraction>() : null;
            combat = player != null ? player.GetComponent<PlayerCombatController>() : null;
        }

        private bool PlayerIsFemale => player != null && player.id == "girl";
        private Vector3 PlayerPosition => vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null
            ? vehicleInteraction.CurrentVehicle.transform.position : player.transform.position;

        private void CheckApproach()
        {
            Vector3 p = PlayerPosition;
            bool inCar = vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null;
            float carSpeed = inCar ? vehicleInteraction.CurrentVehicle.CurrentSpeed : 0f;
            float range = inCar ? carRange : approachRange;
            bool threatening = !inCar && combat != null && combat.IsArmed;

            // People the player has walked away from can react again next time.
            nearby.Clear();
            PedestrianDamage nearest = null;
            float nearestD = float.MaxValue;
            foreach (PedestrianDamage person in PedestrianDamage.Active)
            {
                if (!CanTalk(person)) continue;
                float d = (person.transform.position - p).sqrMagnitude;
                int id = person.GetInstanceID();
                if (d > 14f * 14f) { greeted.Remove(id); continue; }
                if (d > range * range) continue;
                nearby.Add(person);
                if (!greeted.Contains(id) && d < nearestD) { nearestD = d; nearest = person; }
            }
            if (nearest == null || Time.time < nextApproach) return;
            if (inCar && carSpeed > slowCarSpeed) return;
            greeted.Add(nearest.GetInstanceID());

            int group = 0;
            foreach (PedestrianDamage other in nearby)
                if (other != nearest && (other.transform.position - nearest.transform.position).sqrMagnitude < groupRadius * groupRadius) group++;
            if (Random.value > (group > 0 ? groupChance : aloneChance)) return;

            VoiceBank.Voice voice = NpcVoice(nearest);
            VoiceLine line;
            if (threatening) line = VoiceLine.Threatened;
            else if (inCar) line = VoiceLine.Impressed;
            else
            {
                // A random everyday line, not one just heard (situation cooldown) and one this voice has.
                line = VoiceLine.Greeting;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    float r = Random.value;
                    line = r < 0.35f ? VoiceLine.Greeting : r < 0.6f ? VoiceLine.AmbientGrumble : r < 0.82f ? VoiceLine.Stare : VoiceLine.Annoyed;
                    if (voice.Has(line) && !(situationNext.TryGetValue(line, out float cool) && Time.time < cool)) break;
                }
                if (!voice.Has(line)) line = VoiceLine.Annoyed;
            }
            float length = Speak(nearest.transform, voice, line, "approach" + (group > 0 ? " (group of " + (group + 1) + ")" : ""));
            if (length <= 0f) return;
            nextApproach = Time.time + approachCooldown;
            if (Random.value < playerReplyChance) ReplyLater(Reply(line), length + 0.35f);
        }

        private static VoiceLine Reply(VoiceLine heard)
        {
            switch (heard)
            {
                case VoiceLine.Stare: return VoiceLine.ReplyStare;
                case VoiceLine.Annoyed: return VoiceLine.ReplyRude;
                case VoiceLine.CallPolice: return VoiceLine.ReplyThreat;
                case VoiceLine.Impressed: return VoiceLine.ReplyImpressed;
                case VoiceLine.Panic:
                case VoiceLine.Shocked: return VoiceLine.ReplyChaos;
                default: return VoiceLine.ReplyCalm;
            }
        }

        private void CheckPolice()
        {
            if (WantedManager.Instance == null || !WantedManager.IsPlayerWanted || Time.time < nextPolice) return;
            Vector3 p = PlayerPosition;
            PoliceOfficerUnit nearest = null;
            float best = 25f * 25f;
            foreach (PoliceOfficerUnit officer in PoliceOfficerUnit.Active)
            {
                if (officer == null || !officer.IsStanding) continue;
                float d = (officer.transform.position - p).sqrMagnitude;
                if (d < best) { best = d; nearest = officer; }
            }
            bool inCar = vehicleInteraction != null && vehicleInteraction.IsInVehicle;
            if (nearest != null && !inCar)
            {
                VoiceLine line = combat != null && combat.IsArmed ? VoiceLine.PoliceDropWeapon : VoiceLine.PoliceStop;
                if (Speak(nearest.transform, bank.Police, line, "officer close") > 0f) nextPolice = Time.time + policeCooldown;
                return;
            }
            if (inCar)
            {
                // A chase: a pursuing car radios it in now and then.
                PoliceCarUnit car = null;
                float carBest = 70f * 70f;
                foreach (PoliceCarUnit unit in PoliceCarUnit.Active)
                {
                    if (unit == null) continue;
                    float d = (unit.transform.position - p).sqrMagnitude;
                    if (d < carBest) { carBest = d; car = unit; }
                }
                if (car != null && Speak(car.transform, bank.Police, VoiceLine.PoliceFleeing, "chase") > 0f) nextPolice = Time.time + policeCooldown * 2.5f;
            }
        }

        // ---------------------------------------------------------------- events

        private void HandlePedestrianDamaged(PedestrianDamage person, vDamage damage, bool killed)
        {
            if (Muted || person == null || killed || damage == null || !FromPlayer(damage.sender)) return;
            if (PoliceOfficerUnit.IsOfficer(person)) return;
            bool melee = damage.damageType != "Vehicle" && damage.sender != null && (damage.sender.position - person.transform.position).sqrMagnitude < 9f;
            if (Random.value < 0.7f && Speak(person.transform, NpcVoice(person), VoiceLine.Hurt, melee ? "punched" : "hurt", true) > 0f)
                nextEvent = Time.time + eventCooldown;
            Witness(person.transform.position, person, Random.value < 0.5f ? VoiceLine.CallPolice : VoiceLine.Witness, 1.0f, "saw an attack");
            if (melee)
            {
                if (Random.value < 0.15f) ReplyLater(VoiceLine.Brawl, 0.8f);
                StartCoroutine(CheckFightBack(person));
            }
        }

        private IEnumerator CheckFightBack(PedestrianDamage person)
        {
            yield return new WaitForSeconds(0.7f);
            if (person == null || fightAnswered.Contains(person.GetInstanceID())) yield break;
            var brawler = person.GetComponent<PedestrianBrawler>();
            if (brawler == null || !brawler.IsFighting) yield break;
            fightAnswered.Add(person.GetInstanceID());
            if (Random.value < 0.6f) ReplyLater(VoiceLine.FightStart, 0f);
        }

        private void HandlePedestrianDied(PedestrianDamage person)
        {
            if (Muted || person == null) return;
            Witness(person.transform.position, person, Random.value < 0.5f ? VoiceLine.Shocked : VoiceLine.Panic, 0.6f, "saw a death");
        }

        private void HandleCrime(CrimeReport report)
        {
            if (Muted) return;
            switch (report.Type)
            {
                case CrimeType.GunshotFired:
                    Witness(report.Position, null, Random.value < 0.6f ? VoiceLine.Panic : VoiceLine.Fleeing, 0.3f, "gunshot");
                    break;
                case CrimeType.VehicleStolen:
                    StartCoroutine(CarStolen(report.Position));
                    if (Random.value < 0.4f) ReplyLater(VoiceLine.StealCar, 1.4f);
                    break;
                case CrimeType.PedestrianRunOver:
                case CrimeType.PoliceVehicleDestroyed:
                    Witness(report.Position, null, VoiceLine.Shocked, 0.5f, "crash");
                    break;
            }
        }

        // The driver pulled out of his car is a street pedestrian by then: the one next to the car complains.
        private IEnumerator CarStolen(Vector3 where)
        {
            yield return new WaitForSeconds(1.3f);
            PedestrianDamage owner = Nearest(where, 8f, null);
            if (owner != null) Speak(owner.transform, NpcVoice(owner), VoiceLine.CarStolen, "car stolen", true);
        }

        private void HandleWanted(int stars, WantedState state)
        {
            if (!Muted)
            {
                if (lastStars == 0 && stars > 0 && Random.value < 0.45f) ReplyLater(VoiceLine.WantedRaised, 0.6f);
                if (lastStars > 0 && stars == 0 && Random.value < 0.6f) ReplyLater(VoiceLine.WantedCleared, 1f);
                if (stars >= 3 && stars > lastStars)
                {
                    Transform officer = NearestPolice(120f);
                    if (officer != null) Speak(officer, bank.Police, VoiceLine.PoliceBackup, "backup (" + stars + " stars)", true);
                }
            }
            lastStars = stars;
        }

        private void HandleBustProgress(float progress)
        {
            if (Muted || progress < 0.15f || Time.time < nextPolice) return;
            Transform officer = NearestPolice(15f);
            if (officer != null && Speak(officer, bank.Police, VoiceLine.PoliceArrest, "arrest", true) > 0f) nextPolice = Time.time + policeCooldown * 1.5f;
        }

        private void HandleCountdown(int n)
        {
            if (n == 0 && Random.value < 0.5f) ReplyLater(VoiceLine.RaceStart, 0.4f);
        }

        private void HandleRaceFinished(RaceResult result)
        {
            if (result.position == 1 && Random.value < 0.7f) ReplyLater(VoiceLine.RaceWin, 0.8f);
        }

        private void WatchVehicle(VehicleHealth health)
        {
            if (health == watchedVehicle) return;
            if (watchedVehicle != null) watchedVehicle.OnDamaged -= HandleVehicleDamaged;
            watchedVehicle = health;
            if (watchedVehicle != null) watchedVehicle.OnDamaged += HandleVehicleDamaged;
        }

        private void HandleVehicleDamaged(float amount, Transform source)
        {
            if (!Muted && amount >= 20f && Random.value < 0.3f) ReplyLater(VoiceLine.CloseCall, 0.3f);
        }

        // ---------------------------------------------------------------- speaking

        private void Witness(Vector3 where, PedestrianDamage except, VoiceLine line, float delay, string reason)
        {
            if (Time.time < nextEvent) return;
            PedestrianDamage witness = Nearest(where, 22f, except);
            if (witness == null) return;
            nextEvent = Time.time + eventCooldown;
            StartCoroutine(SpeakLater(witness, line, delay, reason));
        }

        private IEnumerator SpeakLater(PedestrianDamage person, VoiceLine line, float delay, string reason)
        {
            yield return new WaitForSeconds(delay);
            if (person != null && CanTalk(person)) Speak(person.transform, NpcVoice(person), line, reason);
        }

        private void ReplyLater(VoiceLine line, float delay)
        {
            if (Time.time < nextPlayer) return;
            nextPlayer = Time.time + playerCooldown;
            StartCoroutine(PlayerLine(line, delay));
        }

        private IEnumerator PlayerLine(VoiceLine line, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (player == null || Muted || playerSource.audio.isPlaying) yield break;
            VoiceBank.Voice voice = PlayerIsFemale ? bank.PlayerFemale : bank.PlayerMale;
            AudioClip clip = voice != null ? voice.Pick(line, lastClip) : null;
            if (clip == null) yield break;
            playerSource.speaker = player.transform;
            playerSource.audio.transform.position = player.transform.position + Vector3.up * 1.6f;
            playerSource.audio.volume = playerVolume;
            playerSource.audio.clip = clip;
            playerSource.audio.Play();
            lastClip = clip;
            if (logLines) Debug.Log($"[VoiceBark] player ({(PlayerIsFemale ? "Mia" : "Jason")}) {line}: '{clip.name}'", this);
        }

        /// <summary>Plays a line at the speaker. Returns its length, or 0 when nobody is free / it is on cooldown.</summary>
        private float Speak(Transform speaker, VoiceBank.Voice voice, VoiceLine line, string reason, bool important = false)
        {
            if (speaker == null || voice == null || Muted) return 0f;
            if (!important && situationNext.TryGetValue(line, out float next) && Time.time < next) return 0f;
            int id = speaker.GetInstanceID();
            if (!important && personNext.TryGetValue(id, out float personFree) && Time.time < personFree) return 0f;
            Source free = null;
            foreach (Source s in npcSources)
            {
                if (s.audio.isPlaying && s.speaker == speaker)
                {
                    if (!important) return 0f; // never over himself
                    s.audio.Stop();            // a punch cuts his small talk short
                }
                if (!s.audio.isPlaying && free == null) free = s;
            }
            if (free == null) return 0f;
            AudioClip clip = voice.Pick(line, lastClip);
            if (clip == null) return 0f;
            free.speaker = speaker;
            free.audio.transform.position = speaker.position + Vector3.up * 1.6f;
            free.audio.volume = npcVolume;
            free.audio.clip = clip;
            free.audio.Play();
            lastClip = clip;
            situationNext[line] = Time.time + clip.length + 6f;
            personNext[id] = Time.time + Mathf.Max(personCooldown, clip.length);
            if (logLines) Debug.Log($"[VoiceBark] {reason}: {speaker.name} ({voice.name}) {line}: '{clip.name}'", this);
            return clip.length;
        }

        // ---------------------------------------------------------------- people

        private static bool CanTalk(PedestrianDamage person) =>
            person != null && person.isActiveAndEnabled && !person.IsDead && !person.IsDown && !PoliceOfficerUnit.IsOfficer(person);

        private PedestrianDamage Nearest(Vector3 where, float radius, PedestrianDamage except)
        {
            PedestrianDamage best = null;
            float bestD = radius * radius;
            foreach (PedestrianDamage person in PedestrianDamage.Active)
            {
                if (person == except || !CanTalk(person)) continue;
                if (personNext.TryGetValue(person.GetInstanceID(), out float free) && Time.time < free) continue; // just spoke
                float d = (person.transform.position - where).sqrMagnitude;
                if (d < bestD) { bestD = d; best = person; }
            }
            return best;
        }

        private Transform NearestPolice(float radius)
        {
            if (player == null) return null;
            Vector3 p = PlayerPosition;
            Transform best = null;
            float bestD = radius * radius;
            foreach (PoliceOfficerUnit officer in PoliceOfficerUnit.Active)
            {
                if (officer == null || !officer.IsStanding) continue;
                float d = (officer.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = officer.transform; }
            }
            foreach (PoliceCarUnit car in PoliceCarUnit.Active)
            {
                if (car == null) continue;
                float d = (car.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = car.transform; }
            }
            return best;
        }

        // Women's voices for the women's models; one of the two voices per person, the same for as long as he exists.
        private VoiceBank.Voice NpcVoice(PedestrianDamage person)
        {
            string n = person.name;
            bool female = n.IndexOf("Girl", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Female", System.StringComparison.OrdinalIgnoreCase) >= 0
                          || n.IndexOf("Woman", System.StringComparison.OrdinalIgnoreCase) >= 0;
            IReadOnlyList<VoiceBank.Voice> voices = female ? bank.FemaleNpc : bank.MaleNpc;
            if (voices == null || voices.Count == 0) return null;
            return voices[(person.GetInstanceID() & 0x7fffffff) % voices.Count];
        }

        private bool FromPlayer(Transform sender)
        {
            if (sender == null || player == null) return false;
            return sender == player.transform || sender.IsChildOf(player.transform)
                   || (vehicleInteraction != null && vehicleInteraction.CurrentVehicle != null && sender == vehicleInteraction.CurrentVehicle.transform);
        }
    }
}
