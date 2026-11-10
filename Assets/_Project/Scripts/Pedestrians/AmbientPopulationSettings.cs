using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>Tuning of the standing and sitting people (Assets/_Project/Data/Pedestrians/AmbientPopulationSettings.asset). Read-only at runtime.</summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Population/Ambient Population Settings", fileName = "AmbientPopulationSettings")]
    public class AmbientPopulationSettings : ScriptableObject
    {
        [Header("Caps (taken from the normal crowd: the total people count does not grow)")]
        [Min(0)] public int maxTalkingGroups = 4;
        [Min(0)] public int maxSitters = 7;
        [Tooltip("Share of the crowd that may be held at spots at most, so the sidewalks keep some walkers.")]
        [Range(0f, 1f)] public float maxShareOfCrowd = 0.4f;

        [Header("Where (metres from the player)")]
        [Tooltip("Nobody is placed closer than this.")]
        [Min(5f)] public float activateMinDistance = 28f;
        [Tooltip("Spots beyond this are ignored.")]
        [Min(10f)] public float activateMaxDistance = 85f;
        [Tooltip("From this distance a person may appear even in view (a few pixels tall); closer spots must be off screen or behind a building.")]
        [Min(10f)] public float appearInViewDistance = 65f;
        [Tooltip("People farther than this go back to the crowd once the camera does not see them.")]
        [Min(10f)] public float recycleDistance = 110f;
        [Tooltip("Two occupied spots keep at least this distance.")]
        [Min(0f)] public float minSpotSeparation = 7f;

        [Header("Timing")]
        [Min(0.1f)] public float checkInterval = 0.75f;
        [Tooltip("A spot stays empty this long after its people left.")]
        [Min(0f)] public float spotCooldown = 45f;
        [Min(1f)] public float lineSecondsMin = 3f;
        [Min(1f)] public float lineSecondsMax = 7f;

        [Header("Talking Groups")]
        [Range(0f, 1f)] public float threePersonChance = 0.35f;
        [Min(0.3f)] public float pairRadius = 0.6f;
        [Min(0.3f)] public float trioRadius = 0.78f;
        [Tooltip("Degrees a listener's facing wanders off the speaker, so they do not stare like robots.")]
        [Min(0f)] public float facingJitter = 18f;

        [Header("Seats")]
        [Tooltip("Hips above the seat surface (flesh and clothes).")]
        public float hipsAboveSeat = 0.1f;
        [Tooltip("Hips stay at least this high above the ground, whatever the seat.")]
        public float minHipsAboveGround = 0.42f;
        [Tooltip("Seats lower than this use the low sitting clips (knees up, feet may dangle a little on the higher ones).")]
        public float lowSeatHeight = 0.68f;
        [Tooltip("Top of a seated body above the seat surface, for its capsule and far hitbox.")]
        public float seatedBodyHeight = 0.85f;

        [Header("Cars Coming At People")]
        [Tooltip("Vehicle bodies on these layers scare people when on a course to hit them.")]
        public LayerMask vehicleLayers = (1 << 8) | (1 << 13) | (1 << 19);
        [Min(0.05f)] public float carCheckInterval = 0.25f;
        [Tooltip("Only people this close to the player check for cars (nothing else is visible).")]
        [Min(5f)] public float carCheckPlayerDistance = 70f;
        [Min(1f)] public float carScanRadius = 18f;
        [Tooltip("Slower cars do not scare anybody.")]
        [Min(0f)] public float carMinSpeed = 5f;
        [Tooltip("The car must pass within this distance (m) ...")]
        [Min(0f)] public float carPassDistance = 2.2f;
        [Tooltip("... within this many seconds.")]
        [Min(0f)] public float carWarningSeconds = 1.8f;

        [Header("Bench Breaks")]
        [Tooltip("Sitters this close to a broken bench are thrown off it.")]
        [Min(0.5f)] public float brokenBenchRadius = 1.7f;
        [Min(0f)] public float benchThrowSpeed = 1.6f;
        [Min(0f)] public float benchGroundTime = 2f;
    }
}
