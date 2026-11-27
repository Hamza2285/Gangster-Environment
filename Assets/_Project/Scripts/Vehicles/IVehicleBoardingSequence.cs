namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Optional, on a quick-mount vehicle (<see cref="IVehicleController.UsesQuickMount"/>) that is boarded through a door with a
    /// camera cut instead of a hop onto the seat (the helicopter). PlayerVehicleInteraction then: walks the player to the door,
    /// opens it, cuts to the vehicle's own camera on the far side (so the move onto the seat is hidden behind the body), seats him,
    /// closes the door and lets the vehicle camera glide behind. Getting out runs the other way round.
    /// </summary>
    public interface IVehicleBoardingSequence
    {
        /// <summary>Seconds the door takes to open or close.</summary>
        float DoorSeconds { get; }

        /// <summary>Opens / closes the boarding door (animated over <see cref="DoorSeconds"/>; no door = nothing happens).</summary>
        void SetDoorOpen(bool open);

        /// <summary>
        /// Turns the vehicle camera on at a fixed view from the side away from the door. <paramref name="glide"/>: move there from
        /// where the camera is now (getting out); otherwise cut straight to it (getting in).
        /// </summary>
        void ShowBoardingView(bool glide);

        /// <summary>Turns the boarding view (vehicle camera) off again, at the end of getting out.</summary>
        void EndBoardingView();
    }

    public static class VehicleBoarding
    {
        /// <summary>The vehicle's boarding sequence, if it is boarded through a door with a camera cut (else false).</summary>
        public static bool TryGet(IVehicleController vehicle, out IVehicleBoardingSequence boarding)
        {
            boarding = null;
            var component = vehicle as UnityEngine.Component;
            return component != null && component.TryGetComponent(out boarding);
        }
    }
}
