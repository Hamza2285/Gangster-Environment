using System;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    public enum AmbientSpotKind
    {
        /// <summary>Open ground on a sidewalk or plaza: a small group stands there and talks.</summary>
        Talk,
        /// <summary>The seat of a bench (breakable prop).</summary>
        Bench,
        /// <summary>A low wall, planter or ledge edge found in the scene.</summary>
        Ledge
    }

    [Serializable]
    public struct AmbientSpot
    {
        public AmbientSpotKind kind;
        [Tooltip("Talk: the middle of the group, on the ground. Seats: the point on the seat surface under the hips.")]
        public Vector3 position;
        [Tooltip("Direction a seated person faces (degrees around Y).")]
        public float yaw;
        [Tooltip("Height (m) of the seat surface above the ground in front of it.")]
        public float seatHeight;
    }

    /// <summary>
    /// Where people stand and sit in one scene. Generated from the scene by Tools > OpenworldCrime > Population > Find Ambient Spots
    /// and rerunnable; hand edits are overwritten. Read-only at runtime (who is where lives on <see cref="AmbientPopulationManager"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Population/Ambient Spot Data", fileName = "AmbientSpotData")]
    public class AmbientSpotData : ScriptableObject
    {
        public AmbientSpot[] spots = new AmbientSpot[0];
    }
}
