using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Generic contract between the player's vehicle interaction and a drivable vehicle.
    /// The player side owns the enter/exit sequence (animation, input, weapon, player camera);
    /// the vehicle side (e.g. <see cref="RCCVehicleAdapter"/>) owns everything vehicle-specific:
    /// driving control, engine, vehicle camera and the seated driver visual.
    /// Bikes, helicopters, etc. implement this with their own adapter.
    /// </summary>
    public interface IVehicleController : IVehicleInteractionTarget
    {
        /// <summary>True while a driver is entering, driving or exiting.</summary>
        bool IsOccupied { get; }
        /// <summary>True while a driver has driving control.</summary>
        bool IsControlled { get; }
        /// <summary>Current speed in metres per second.</summary>
        float CurrentSpeed { get; }
        /// <summary>True when the driver may get out (slow enough).</summary>
        bool CanExit { get; }
        /// <summary>
        /// True for vehicles mounted without a door (bikes): the player swings straight onto the seat and steps off again instead
        /// of playing the car door enter / exit clips.
        /// </summary>
        bool UsesQuickMount { get; }

        /// <summary>Character root anchor while seated (the seated pose's root, not the hips).</summary>
        Transform GetDriverSeat();
        /// <summary>Where the character stands when the exit animation ends.</summary>
        Transform GetExitPoint();
        /// <summary>Optional fallback exit (e.g. other side). May be null.</summary>
        Transform GetAlternateExitPoint();
        /// <summary>Driver door opened/closed around the enter/exit animations. May be null (no animated door).</summary>
        VehicleDoor GetDriverDoor();
        /// <summary>
        /// Cabin floor at the driver door sill, used to keep the driver's feet inside the car. Its up axis is the floor
        /// normal, its right axis points from the sill into the cabin, its forward axis is the vehicle forward. May be null.
        /// </summary>
        Transform GetCabinFloor();
        /// <summary>Camera currently rendering the vehicle while it is driven (null when not controlled).</summary>
        Camera GetActiveCamera();
        /// <summary>NPC driver still sitting at the wheel, pulled out during the enter sequence. May be null (no NPC driver setup).</summary>
        TrafficVehicleDriver GetNpcDriver();

        /// <summary>Reserve/release the vehicle for a driver.</summary>
        void SetOccupied(bool occupied);
        /// <summary>Give driving control to the player: vehicle input, engine and vehicle camera on.</summary>
        void EnableControl();
        /// <summary>Take driving control away (vehicle brakes/parks), vehicle camera off.</summary>
        void DisableControl();
        /// <summary>
        /// Take driving control away without braking (the driver bailed out of a moving vehicle): vehicle camera off, no throttle,
        /// brake or handbrake, so the vehicle rolls on on its own momentum and parks once it has (nearly) stopped.
        /// </summary>
        void ReleaseControlRolling();
        /// <summary>Show/hide the static seated driver visual used while driving.</summary>
        void SetDriverVisualActive(bool active);
    }
}
