using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>What the player is offered when standing at a vehicle.</summary>
    public enum VehicleEntryPrompt
    {
        /// <summary>Nobody at the wheel: plain enter.</summary>
        Enter,
        /// <summary>A living driver sits at the wheel and is pulled out first.</summary>
        Steal
    }

    /// <summary>
    /// Anything the player can walk up to and get into: a drivable vehicle (<see cref="IVehicleController"/>) or a
    /// vehicle that first has to be turned into one (e.g. a lightweight iTS traffic car, <see cref="TrafficVehicleAdapter"/>).
    /// Found through a trigger collider under the vehicle, like <see cref="IVehicleController"/>.
    /// Entry protocol: <see cref="ReserveForEntry"/> when the player commits (the vehicle stops and nobody else takes it),
    /// then either <see cref="CancelEntryReservation"/> (the vehicle carries on as before) or, once the player is at the
    /// door and <see cref="IsReadyForEntry"/>, <see cref="CompleteEntry"/>.
    /// </summary>
    public interface IVehicleInteractionTarget
    {
        Transform transform { get; }

        /// <summary>True when the player may commit to getting in right now.</summary>
        bool CanEnter { get; }
        /// <summary>True once a reserved vehicle is still enough for the enter animation to start.</summary>
        bool IsReadyForEntry { get; }
        /// <summary>Prompt kind shown while this vehicle is focused.</summary>
        VehicleEntryPrompt EntryPrompt { get; }

        /// <summary>Point used to rank nearby vehicles (falls back to the vehicle root).</summary>
        Transform GetInteractionPoint();
        /// <summary>Where the character stands, facing the door, when the enter animation starts.</summary>
        Transform GetEnterPoint();
        /// <summary>
        /// Where the character stands to pull a seated driver out (carjack), or null when there is nobody to pull out.
        /// The player walks here instead of to the enter point, then walks on to the enter point after the carjack.
        /// </summary>
        Transform GetStealPoint();
        /// <summary>All colliders of the vehicle (walking path around it, collision ignoring).</summary>
        IReadOnlyList<Collider> GetColliders();

        /// <summary>Reserves the vehicle for the player: it brakes to a stop and nothing else may take or recycle it.</summary>
        void ReserveForEntry();
        /// <summary>Releases a reservation that did not lead to entering (the vehicle carries on as before).</summary>
        void CancelEntryReservation();
        /// <summary>
        /// Called when the reserved vehicle is ready and the player is at the door. Returns the drivable vehicle the enter
        /// sequence runs on: the vehicle itself, or a drivable replacement. Returns null (reservation released) if not possible.
        /// </summary>
        IVehicleController CompleteEntry();
    }
}
