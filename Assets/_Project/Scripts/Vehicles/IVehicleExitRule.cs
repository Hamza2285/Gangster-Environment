namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Optional extra rule on an <see cref="IVehicleController"/>: whether the driver may bail out (jump out while it moves).
    /// Vehicles without it allow bailing (cars, bikes). A helicopter only lets the pilot out when landed or hovering low.
    /// </summary>
    public interface IVehicleExitRule
    {
        /// <summary>False: exiting while <see cref="IVehicleController.CanExit"/> is false is refused instead of bailing out.</summary>
        bool AllowsBailOut { get; }

        /// <summary>
        /// True: exiting while <see cref="IVehicleController.CanExit"/> is false makes the driver jump out into the air (a
        /// helicopter in flight): he falls in the stable skydiving pose with a parachute bag, as a screaming ragdoll without.
        /// </summary>
        bool AllowsAirJump { get; }
    }
}
