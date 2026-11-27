namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Optional extra on an <see cref="IVehicleController"/> that flies (helicopter, plane). The HUD uses it: the car speed
    /// readout stays hidden for these and the flight instruments (speed / altitude) show instead.
    /// </summary>
    public interface IFlyingVehicle
    {
        /// <summary>Height (m) of the lowest point (skids, wheels) above the ground or water under it.</summary>
        float HeightAboveGround { get; }
        /// <summary>True while resting / rolling on the ground.</summary>
        bool IsGrounded { get; }
    }
}
