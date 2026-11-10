using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>Data of one vehicle hitting something (built by <see cref="VehicleCharacterImpact"/>).</summary>
    public readonly struct VehicleImpact
    {
        /// <summary>Velocity (m/s) of the vehicle body at the contact point, relative to the object that was hit.</summary>
        public readonly Vector3 Velocity;
        /// <summary>Contact point in world space.</summary>
        public readonly Vector3 Point;
        public readonly float VehicleMass;
        public readonly Transform Vehicle;
        /// <summary>
        /// The vehicle's impact component. Receivers that switch to a ragdoll ask it to ignore the new limbs for a moment
        /// (<see cref="VehicleCharacterImpact.IgnoreCollidersTemporarily"/>), so the body is never spawned inside the car. May be null.
        /// </summary>
        public readonly VehicleCharacterImpact Source;

        /// <summary>
        /// The vehicle's own velocity (m/s) at the contact point going into the hit. <see cref="Velocity"/> is relative to the hit
        /// object after the solver ran, so it is almost zero for a light dynamic body (the player) the car has just knocked along.
        /// </summary>
        public readonly Vector3 VehicleVelocity;

        public float Speed => Velocity.magnitude;

        public VehicleImpact(Vector3 velocity, Vector3 point, float vehicleMass, Transform vehicle, VehicleCharacterImpact source = null, Vector3 vehicleVelocity = default)
        {
            Velocity = velocity;
            Point = point;
            VehicleMass = vehicleMass;
            Vehicle = vehicle;
            Source = source;
            VehicleVelocity = vehicleVelocity;
        }
    }

    /// <summary>Anything that reacts to being hit by a vehicle (e.g. characters). Implemented on the object's root.</summary>
    public interface IVehicleImpactReceiver
    {
        void ReceiveVehicleImpact(VehicleImpact impact);
    }
}
