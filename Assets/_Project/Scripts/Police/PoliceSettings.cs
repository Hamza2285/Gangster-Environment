using System;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Every tunable number of the police system: witnesses, crimes, wanted level, busting, dispatch, officers and cars.
    /// Read-only at runtime (runtime state lives on <see cref="WantedManager"/> and the units).
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Police/Police Settings", fileName = "PoliceSettings")]
    public class PoliceSettings : ScriptableObject
    {
        [Serializable]
        public struct CrimeRule
        {
            public CrimeType crime;
            [Tooltip("The wanted level becomes at least this many stars.")]
            [Range(1, 5)] public int minStars;
            [Tooltip("Extra stars when the player is already wanted (repeat offences escalate).")]
            [Range(0, 2)] public int addStarsWhileWanted;
            [Tooltip("Police shoot on sight from now on (until the wanted level is lost).")]
            public bool lethal;

            public CrimeRule(CrimeType crime, int minStars, int addStarsWhileWanted, bool lethal)
            {
                this.crime = crime;
                this.minStars = minStars;
                this.addStarsWhileWanted = addStarsWhileWanted;
                this.lethal = lethal;
            }
        }

        [Header("Witnesses")]
        [Tooltip("Police see a crime within this distance (m) when nothing blocks the view.")]
        [Min(1f)] public float sightRadius = 40f;
        [Tooltip("Police hear a gunshot within this distance (m), walls or not.")]
        [Min(0f)] public float hearingRadius = 30f;
        [Tooltip("Solid geometry that blocks the view (buildings, walls).")]
        public LayerMask sightBlockers = 1;
        [Min(0f)] public float eyeHeight = 1.6f;

        [Header("Crimes")]
        public CrimeRule[] crimes =
        {
            new CrimeRule(CrimeType.GunshotFired, 1, 0, false),
            new CrimeRule(CrimeType.AimedAtOfficer, 1, 0, true),
            new CrimeRule(CrimeType.VehicleShot, 1, 0, false),
            new CrimeRule(CrimeType.VehicleStolen, 1, 0, false),
            new CrimeRule(CrimeType.PedestrianRunOver, 1, 0, false),
            new CrimeRule(CrimeType.PedestrianHurt, 2, 0, true),
            new CrimeRule(CrimeType.PedestrianKilled, 2, 1, true),
            new CrimeRule(CrimeType.OfficerHurt, 2, 0, true),
            new CrimeRule(CrimeType.OfficerKilled, 3, 1, true),
            new CrimeRule(CrimeType.PoliceVehicleRammed, 1, 0, false),
            new CrimeRule(CrimeType.PoliceVehicleShot, 2, 0, true),
            new CrimeRule(CrimeType.PoliceVehicleDestroyed, 3, 1, true),
        };
        [Tooltip("Seconds the player has to keep a gun on an officer before it counts as a threat.")]
        [Min(0.1f)] public float aimThreatSeconds = 1.5f;
        [Tooltip("Aiming at an officer only counts within this distance (m).")]
        [Min(1f)] public float aimThreatDistance = 25f;

        [Header("Wanted Level")]
        [Tooltip("From this many stars police always shoot on sight.")]
        [Range(1, 5)] public int lethalFromStars = 3;
        [Tooltip("Police lose the player after this many seconds without seeing him; the search starts.")]
        [Min(0.5f)] public float lostSightSeconds = 5f;
        [Tooltip("After a crime they saw, police know where the player is for this long (radio call), even with every witness " +
                 "dead: the search can only start once reinforcements had time to find him.")]
        [Min(0f)] public float crimeTrackingSeconds = 12f;
        [Tooltip("Police this close (m) to the player keep track of him without line of sight (round a corner, behind a car).")]
        [Min(0f)] public float awarenessRadius = 20f;
        [Tooltip("Seconds unseen during the search before the wanted level is lost: base + per star.")]
        [Min(1f)] public float searchSecondsBase = 10f;
        [Min(0f)] public float searchSecondsPerStar = 5f;
        [Tooltip("Radius (m) of the search area shown on the map: base + per star.")]
        [Min(5f)] public float searchRadiusBase = 60f;
        [Min(0f)] public float searchRadiusPerStar = 20f;
        [Tooltip("An officer on foot spots the player within this distance (m) with line of sight.")]
        [Min(5f)] public float officerSightRadius = 45f;
        [Tooltip("A police car spots the player within this distance (m) with line of sight.")]
        [Min(5f)] public float carSightRadius = 60f;
        [Tooltip("Seconds between the police 'can anybody see him' checks.")]
        [Min(0.05f)] public float visibilityCheckInterval = 0.25f;

        [Header("Busted")]
        [Tooltip("Busting only while police are not shooting (otherwise you lose them or die).")]
        public bool bustOnlyWhenNonLethal = true;
        [Tooltip("An officer this close (m) to the player on foot can arrest him.")]
        [Min(0.5f)] public float bustRadiusOnFoot = 3.5f;
        [Tooltip("An officer this close (m) to the player's car can arrest him.")]
        [Min(0.5f)] public float bustRadiusInVehicle = 5f;
        [Tooltip("Moving slower than this (m/s) on foot lets the arrest timer fill (a walk is too slow, a run escapes).")]
        [Min(0f)] public float bustMaxSpeedOnFoot = 2.5f;
        [Tooltip("Driving slower than this (m/s) lets the arrest timer fill.")]
        [Min(0f)] public float bustMaxSpeedInVehicle = 2f;
        [Tooltip("Seconds to fill the arrest bar.")]
        [Min(0.2f)] public float bustFillSeconds = 3f;
        [Tooltip("Seconds for a full bar to drain once the player gets away.")]
        [Min(0.1f)] public float bustDrainSeconds = 1.5f;

        [Header("Busted Consequences")]
        [Tooltip("Share of the player's cash taken when busted.")]
        [Range(0f, 1f)] public float bustedCashShare = 0.1f;
        [Min(0)] public int bustedCashMin = 100;
        [Min(0)] public int bustedCashMax = 5000;
        [Tooltip("Slow motion while the BUSTED screen shows (GTA style).")]
        [Range(0.05f, 1f)] public float bustedTimeScale = 0.35f;
        [Tooltip("Real-time seconds the BUSTED screen shows before the fade.")]
        [Min(0f)] public float bustedScreenSeconds = 2.5f;
        [Min(0f)] public float bustedFadeSeconds = 0.6f;

        [Header("Dispatch")]
        [Tooltip("Most officers on foot at once, car crews included, per wanted level (index = stars). Crews beyond it stay in their car.")]
        public int[] footUnitsPerStar = { 0, 2, 2, 3, 4, 5 };
        [Tooltip("Of those, officers who come on foot (not out of a car), per wanted level (index = stars). GTA style: most police " +
                 "arrive and chase by car.")]
        public int[] walkingReinforcementsPerStar = { 0, 0, 0, 1, 1, 2 };
        [Tooltip("Police cars at once, per wanted level (index = stars).")]
        public int[] carUnitsPerStar = { 0, 1, 1, 2, 3, 3 };
        [Tooltip("Seconds between reinforcements.")]
        [Min(0.5f)] public float dispatchInterval = 4f;
        [Tooltip("Reinforcements on foot appear out of sight this far (m) from the player.")]
        public Vector2 footSpawnDistance = new Vector2(35f, 60f);
        [Tooltip("Police cars appear out of sight this far (m) back along the road.")]
        public Vector2 carSpawnDistance = new Vector2(70f, 120f);
        [Tooltip("Units this far (m) from the player are recalled (once out of sight).")]
        [Min(30f)] public float recallDistance = 200f;
        [Tooltip("Officers who sit in a police car (and climb out when it reached the player on foot).")]
        [Range(0, 2)] public int officersPerCar = 2;

        [Header("Car Crews")]
        [Tooltip("Seconds between the driver and the passenger climbing out.")]
        [Min(0f)] public float crewExitStagger = 0.6f;
        [Tooltip("The crew runs back and gets in once the suspect drives off faster than this (m/s) ...")]
        [Min(0f)] public float crewReboardSpeed = 5f;
        [Tooltip("... and is farther than this (m) from the car (or 2.5 times as far whatever he does).")]
        [Min(5f)] public float crewReboardDistance = 30f;
        [Tooltip("Seconds the car waits for its crew to get back in before it leaves without the missing ones.")]
        [Min(2f)] public float crewBoardTimeout = 14f;

        [Header("Officer On Foot")]
        [Min(0.5f)] public float officerRunSpeed = 5.2f;
        [Min(0.5f)] public float officerWalkSpeed = 1.6f;
        [Tooltip("A pursuing officer stops this close (m) to the player and points his gun.")]
        [Min(0.5f)] public float arrestStandDistance = 2f;
        [Tooltip("A pursuing officer draws his gun within this distance (m).")]
        [Min(1f)] public float drawWeaponDistance = 12f;
        [Tooltip("A lethal officer opens fire within this distance (m).")]
        [Min(2f)] public float fireRange = 28f;
        [Tooltip("A lethal officer stops this far (m) from the player to shoot.")]
        [Min(1f)] public float fireStandDistance = 10f;
        [Tooltip("Seconds between shots (random range).")]
        public Vector2 fireInterval = new Vector2(0.7f, 1.4f);
        [Tooltip("Seconds before the first shot after spotting a target.")]
        [Min(0f)] public float fireReactionSeconds = 0.8f;
        [Tooltip("Health damage of a hit on the player.")]
        [Min(0)] public int shotDamage = 7;
        [Tooltip("Hit chance at point blank and at Fire Range.")]
        [Range(0f, 1f)] public float hitChanceNear = 0.55f;
        [Range(0f, 1f)] public float hitChanceFar = 0.15f;
        [Tooltip("Hit chance multiplier when the target moves fast (sprint / driving).")]
        [Range(0f, 1f)] public float movingTargetFactor = 0.6f;
        [Tooltip("Damage to the player's car per hit while he is inside (the player takes Inside Damage Share of the shot).")]
        [Min(0f)] public float vehicleShotDamage = 30f;
        [Range(0f, 1f)] public float playerInsideDamageShare = 0.35f;
        [Tooltip("Seconds between path updates.")]
        [Min(0.1f)] public float officerRepathInterval = 0.4f;
        [Tooltip("Seconds a dead officer stays before he is cleared (once out of sight).")]
        [Min(1f)] public float corpseSeconds = 20f;

        [Header("Police Car")]
        [Min(5f)] public float carTopSpeed = 30f;
        [Min(3f)] public float carCornerSpeed = 13f;
        [Min(100f)] public float carEngineTorque = 3400f;
        [Tooltip("Gap (m) kept behind the player's car (ChaseDriver target gap).")]
        [Min(2f)] public float carChaseGap = 6f;
        [Tooltip("A car whose suspect is on foot stops this close (m) and its officers get out.")]
        [Min(4f)] public float carDeployDistance = 16f;
        [Tooltip("Seconds between route updates.")]
        [Min(0.3f)] public float carRepathInterval = 1.5f;
        [Tooltip("When lethal, officers fire from the car within this distance (m).")]
        [Min(0f)] public float carFireRange = 25f;

        public bool TryGetRule(CrimeType crime, out CrimeRule rule)
        {
            for (int i = 0; i < crimes.Length; i++)
            {
                if (crimes[i].crime != crime) continue;
                rule = crimes[i];
                return true;
            }
            rule = default;
            return false;
        }

        public int FootUnitsFor(int stars) => footUnitsPerStar.Length == 0 ? 0 : footUnitsPerStar[Mathf.Clamp(stars, 0, footUnitsPerStar.Length - 1)];
        public int WalkingReinforcementsFor(int stars) => walkingReinforcementsPerStar.Length == 0 ? 0 : walkingReinforcementsPerStar[Mathf.Clamp(stars, 0, walkingReinforcementsPerStar.Length - 1)];
        public int CarUnitsFor(int stars) => carUnitsPerStar.Length == 0 ? 0 : carUnitsPerStar[Mathf.Clamp(stars, 0, carUnitsPerStar.Length - 1)];
        public float SearchSecondsFor(int stars) => searchSecondsBase + searchSecondsPerStar * stars;
        public float SearchRadiusFor(int stars) => searchRadiusBase + searchRadiusPerStar * stars;
    }
}
