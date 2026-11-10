using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Tuning of how traffic drivers panic when the player threatens them with a gun
    /// (Assets/_Project/Data/Vehicles/TrafficPanicSettings.asset). Read-only at runtime. Nobody ever drives at the player:
    /// a threatened driver either bails out and runs, or floors it away along the road.
    /// Damage to the player from a car that hits him by accident lives on PlayerVehicleImpact.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Traffic Panic Settings", fileName = "TrafficPanicSettings")]
    public class TrafficPanicSettings : ScriptableObject
    {
        [Header("Reaction (per threatened driver, rolled once)")]
        [Tooltip("Chance the driver stops, opens the door and runs away on foot, leaving the car with its door open.")]
        [Range(0f, 1f)] public float bailOutChance = 0.5f;
        [Tooltip("Chance the driver hits the accelerator and flees along the road. Bail Out + Floor It above 1 is scaled down; " +
                 "the rest of the chance means he just carries on.")]
        [Range(0f, 1f)] public float floorItChance = 0.5f;
        [Tooltip("Drivers who only hear a gunshot nearby (not aimed or shot at) react with this share of the chances above.")]
        [Range(0f, 1f)] public float gunfireReactionShare = 0.6f;
        [Tooltip("A driver who rolled is not asked again for this many seconds (whether he reacted or not).")]
        [Min(0f)] public float rollAgainAfter = 20f;
        [Tooltip("Seconds of startle between the threat and the reaction (random between the two).")]
        public Vector2 reactionDelay = new Vector2(0.15f, 0.55f);
        [Tooltip("Police cars panic too (they have their own behaviour, so this is off).")]
        public bool includePolice = false;

        [Header("Caps")]
        [Tooltip("At most this many drivers bailing out or fleeing at the same time.")]
        [Min(1)] public int maxPanicking = 4;
        [Tooltip("At most this many drivers react to one shot or one aim check.")]
        [Min(1)] public int maxReactionsPerThreat = 2;
        [Tooltip("Seconds after a reaction started before the next driver may react (stops one burst from emptying the street).")]
        [Min(0f)] public float reactionCooldown = 0.8f;
        [Tooltip("At most this many drivers react within any minute (bail-outs leave parked cars behind; this keeps a long stand-off " +
                 "from emptying the street).")]
        [Min(1)] public int maxReactionsPerMinute = 6;

        [Header("What Scares Them")]
        [Tooltip("Aiming at a driver or his car scares him within this distance (m) of the player.")]
        [Min(1f)] public float aimDistance = 22f;
        [Tooltip("How close (m) the aim line has to pass to the car's centre.")]
        [Min(0.1f)] public float aimTolerance = 1.8f;
        [Tooltip("Seconds between aim checks while aiming.")]
        [Min(0.05f)] public float aimCheckInterval = 0.2f;
        [Tooltip("The player has to be in front of or beside the car: dot(car forward, direction to player) at least this " +
                 "(-0.35 = up to about 110 degrees off the nose; behind the car does not count).")]
        [Range(-1f, 1f)] public float minFrontDot = -0.35f;
        [Tooltip("A shot scares every driver within this distance (m) of the player ...")]
        [Min(0f)] public float gunfireRadius = 18f;
        [Tooltip("... and within this distance (m) of where the bullet went.")]
        [Min(0f)] public float impactRadius = 7f;
        [Tooltip("Layers that hide a car from the aim check (buildings, walls).")]
        public LayerMask lineOfSightBlockers = 1;

        [Header("Bail Out")]
        [Tooltip("Brake torque multiplier while the car stops for the driver to get out (1 = iTS' full emergency brake).")]
        [Range(0.1f, 1f)] public float bailOutBrakeScale = 0.6f;

        [Header("Floor It")]
        [Tooltip("Top speed (m/s) while fleeing. 20 m/s = 72 km/h.")]
        [Min(1f)] public float fleeSpeed = 20f;
        [Tooltip("Engine torque while fleeing (the stock traffic engine is sized for city traffic).")]
        [Min(0f)] public float engineTorque = 3200f;
        [Tooltip("Brake torque while fleeing.")]
        [Min(0f)] public float brakeTorque = 6000f;
        [Tooltip("Steering limit (share of full lock) at flee speed, so the car cannot turn on a coin or roll over.")]
        [Range(0.05f, 1f)] public float steerLockAtSpeed = 0.35f;
        [Tooltip("Lateral distance (m) he swerves by to pass an obstacle or the player.")]
        [Min(0f)] public float swerveOffset = 2.6f;
        [Tooltip("The player counts as in the way when he is ahead within this distance (m) ...")]
        [Min(0f)] public float playerAvoidDistance = 18f;
        [Tooltip("... and closer than this (m) to the car's centre line; the car then passes him with this much room, or brakes.")]
        [Min(0f)] public float playerClearance = 2.4f;
        [Tooltip("Corners (lane connectors turning more than this, degrees) are taken at Corner Speed.")]
        [Range(5f, 90f)] public float cornerAngle = 30f;
        [Min(1f)] public float cornerSpeed = 9f;
        [Tooltip("From this speed (m/s) he knocks over hydrants, bins and benches in his way.")]
        [Min(0f)] public float propBreakSpeed = 4f;
        [Tooltip("Layers that count as walls and cars in his way.")]
        public LayerMask obstacleLayers = 1;

        [Header("Calming Down")]
        [Tooltip("He floors it for this many seconds (random between the two) ...")]
        public Vector2 fleeSeconds = new Vector2(7f, 11f);
        [Tooltip("... or until he is this far (m) from the player, then drives on calmly.")]
        [Min(1f)] public float calmDistance = 90f;
        [Tooltip("Calm speed (m/s) after the panic.")]
        [Min(1f)] public float calmSpeed = 10f;
        [Tooltip("Slower than this (m/s) while trying to move counts as stuck ...")]
        [Min(0f)] public float stuckSpeed = 1.2f;
        [Tooltip("... for this many seconds, then he gives up and waits on the brakes until he can be recycled.")]
        [Min(0f)] public float stuckSeconds = 3f;
        [Tooltip("A stuck fleeing driver leaves the car and runs instead of waiting. Off: being boxed in (often by the player's car) " +
                 "never makes a driver get out; only the gun threat itself can.")]
        public bool bailOutWhenStuck = false;

        [Header("Back To Traffic")]
        [Tooltip("Once calm (or stuck) he goes back into the traffic pool as soon as he is out of sight and at least this far (m) from the camera.")]
        [Min(0f)] public float recycleKeepDistance = 25f;
        [Tooltip("After this many seconds of fleeing in total he is recycled even in view ...")]
        [Min(0f)] public float maxFleeSeconds = 35f;
        [Tooltip("... if he is at least this far (m) from the camera.")]
        [Min(0f)] public float overdueRecycleDistance = 45f;
        [Tooltip("Once calm he is recycled at this distance (m) from the camera even when in view (iTS itself drops cars at 180 m).")]
        [Min(0f)] public float farRecycleDistance = 150f;
        [Tooltip("How far (m) from the car the lane he flees along may be.")]
        [Min(1f)] public float laneSearchDistance = 25f;
        [Tooltip("Route (m) planned ahead along lanes and junctions.")]
        [Min(20f)] public float routeLookAhead = 120f;

        [Header("Cost")]
        [Tooltip("Seconds between the fleeing car's look-ahead checks for walls, cars and the player.")]
        [Min(0.02f)] public float obstacleCheckInterval = 0.1f;
        [Tooltip("Seconds between warning people in front of the fleeing car.")]
        [Min(0.1f)] public float pedestrianWarnInterval = 0.5f;
        [Min(0f)] public float pedestrianWarnRadius = 10f;
    }
}
