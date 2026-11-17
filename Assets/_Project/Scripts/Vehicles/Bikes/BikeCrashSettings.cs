using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// When a motorbike rider comes off in a collision (GTA style), shared by the player's bikes (<see cref="BikeCrashSensor"/>)
    /// and the traffic bikes (<see cref="TrafficBikeAdapter"/>). Impact speed is the closing speed along the contact normal, so a
    /// fast glancing scrape counts far less than a slow head-on hit.
    /// - below Stay On Below: a wobble, the rider stays on;
    /// - up to Always Thrown Above: thrown off only if the hit lands where it unseats a rider (side, front, rear thresholds), otherwise
    ///   a stumble (bigger wobble, a traffic rider stops for a moment);
    /// - above Always Thrown Above: always thrown, flying on with the speed he had.
    /// Read-only at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Bike Crash Settings", fileName = "BikeCrashSettings")]
    public class BikeCrashSettings : ScriptableObject
    {
        [Header("Thresholds (impact speed, km/h)")]
        [Tooltip("Below this the rider always stays on (a wobble at most).")]
        [Min(0f)] public float stayOnBelowKmh = 27f;
        [Tooltip("Hit from the side (T-boned or sliding into something sideways) from this speed: thrown off.")]
        [Min(0f)] public float sideThrowKmh = 33f;
        [Tooltip("Hit at the front (riding into something) from this speed: thrown off over the bars.")]
        [Min(0f)] public float frontThrowKmh = 40f;
        [Tooltip("Hit from behind (rear-ended) from this speed: thrown off.")]
        [Min(0f)] public float rearThrowKmh = 48f;
        [Tooltip("Above this the rider is always thrown, wherever the hit lands.")]
        [Min(0f)] public float alwaysThrowKmh = 58f;
        [Tooltip("Degrees either side of straight ahead / behind that count as a front / rear hit; the rest is a side hit.")]
        [Range(10f, 80f)] public float frontRearConeDegrees = 40f;

        [Header("What Counts")]
        [Tooltip("A moving body counts only from this mass (kg): a car or another bike, not a bin or a person.")]
        [Min(0f)] public float minOtherMass = 300f;
        [Tooltip("Something fixed counts only from this size (m): a wall or a pole, not a kerb stone.")]
        [Min(0f)] public float minStaticSize = 0.8f;
        [Tooltip("Static contacts whose normal points up more than this (0-1) are the ground (landing a jump, a kerb), not a crash.")]
        [Range(0f, 1f)] public float groundNormalY = 0.7f;
        [Tooltip("Seconds after a hit before another one is judged (one crash produces several contacts).")]
        [Min(0f)] public float hitCooldown = 0.6f;

        [Header("Wobble / Stumble")]
        [Tooltip("Roll kick (degrees per second) of a light wobble at Stay On Below; scales with the impact.")]
        [Min(0f)] public float wobbleRollSpeed = 40f;
        [Tooltip("Roll kick (degrees per second) of a stumble just below the throw threshold.")]
        [Min(0f)] public float stumbleRollSpeed = 85f;
        [Tooltip("Seconds a traffic rider stops after a stumble before riding on (random between the two).")]
        public Vector2 trafficStumbleStopSeconds = new Vector2(1.2f, 2.4f);

        [Header("Thrown Off")]
        [Tooltip("Share of the rider's own speed he flies on with.")]
        [Range(0f, 1.2f)] public float carriedVelocity = 0.9f;
        [Tooltip("Share of the other vehicle's push added (hit by a car).")]
        [Range(0f, 1f)] public float pushShare = 0.5f;
        [Tooltip("Upward speed (m/s) of the throw at Always Thrown Above (scaled down for slower throws).")]
        [Min(0f)] public float upSpeed = 3f;

        /// <summary>Speed (km/h) from which a hit at this angle unseats the rider (angle: 0 = straight ahead, 180 = from behind).</summary>
        public float ThrowSpeedFor(float hitAngleDegrees)
        {
            if (hitAngleDegrees <= frontRearConeDegrees) return frontThrowKmh;
            if (hitAngleDegrees >= 180f - frontRearConeDegrees) return rearThrowKmh;
            return sideThrowKmh;
        }

        public enum Outcome { None, Wobble, Stumble, Thrown }

        /// <summary>The verdict for an impact of <paramref name="impactKmh"/> landing at <paramref name="hitAngleDegrees"/> from the bike's nose.</summary>
        public Outcome Judge(float impactKmh, float hitAngleDegrees)
        {
            if (impactKmh >= alwaysThrowKmh) return Outcome.Thrown;
            if (impactKmh < stayOnBelowKmh) return impactKmh >= stayOnBelowKmh * 0.35f ? Outcome.Wobble : Outcome.None;
            return impactKmh >= ThrowSpeedFor(hitAngleDegrees) ? Outcome.Thrown : Outcome.Stumble;
        }
    }
}
