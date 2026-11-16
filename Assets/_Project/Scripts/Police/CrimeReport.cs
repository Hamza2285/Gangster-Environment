using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>Crimes the police care about (raised by <see cref="CrimeReporter"/>, judged by <see cref="WantedManager"/>).</summary>
    public enum CrimeType
    {
        /// <summary>The player fired a gun (heard within the hearing radius, no line of sight needed).</summary>
        GunshotFired,
        /// <summary>The player kept a gun aimed at a police officer.</summary>
        AimedAtOfficer,
        /// <summary>The player shot a civilian car.</summary>
        VehicleShot,
        /// <summary>The player pulled a driver out of his car.</summary>
        VehicleStolen,
        /// <summary>The player's car knocked a pedestrian down.</summary>
        PedestrianRunOver,
        /// <summary>The player shot (or beat) a pedestrian who survived.</summary>
        PedestrianHurt,
        /// <summary>The player killed a pedestrian.</summary>
        PedestrianKilled,
        /// <summary>The player hurt a police officer.</summary>
        OfficerHurt,
        /// <summary>The player killed a police officer.</summary>
        OfficerKilled,
        /// <summary>The player rammed a police car.</summary>
        PoliceVehicleRammed,
        /// <summary>The player shot a police car.</summary>
        PoliceVehicleShot,
        /// <summary>A police car the player damaged blew up.</summary>
        PoliceVehicleDestroyed
    }

    /// <summary>One crime: what, where, and the victim (officer, car or person; may be null).</summary>
    public struct CrimeReport
    {
        public CrimeType Type;
        public Vector3 Position;
        public Transform Victim;
        /// <summary>The victim is the police (an officer or a police car): the crime is always witnessed.</summary>
        public bool AgainstPolice;

        public CrimeReport(CrimeType type, Vector3 position, Transform victim, bool againstPolice)
        {
            Type = type;
            Position = position;
            Victim = victim;
            AgainstPolice = againstPolice;
        }
    }

    /// <summary>Overall police response (see <see cref="WantedManager"/>).</summary>
    public enum WantedState
    {
        /// <summary>No wanted level.</summary>
        Clear,
        /// <summary>Wanted, police can see the player and follow him without shooting (busting is possible).</summary>
        Pursuit,
        /// <summary>Wanted, police can see the player and shoot on sight.</summary>
        Lethal,
        /// <summary>Police lost sight of the player and search the last known area; stars flash.</summary>
        Searching
    }
}
