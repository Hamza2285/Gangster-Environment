namespace OpenworldCrime.Player
{
    /// <summary>
    /// High-level gameplay state of the player, evaluated by <see cref="PlayerManager"/>.
    /// UI, vehicles, weapon wheel, melee and other systems listen to PlayerManager.OnStateChanged.
    /// </summary>
    public enum PlayerState
    {
        Idle,
        Walk,
        Run,
        Sprint,
        Jump,
        Fall,
        Aim,
        Strafe,
        Shoot,
        Reload,
        Hit,
        Dead,
        /// <summary>Getting in, driving or getting out of a vehicle (see PlayerVehicleInteraction).</summary>
        InVehicle
    }
}
