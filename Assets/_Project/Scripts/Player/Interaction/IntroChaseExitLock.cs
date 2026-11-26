using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Keeps the player in the car during the intro chase: from the moment he takes the wheel until the cops are lost (and while
    /// the mission puts him back after a failure) his own exit input and the mobile exit button do nothing; he is told to lose the
    /// cops instead. Forced exits (the mission's own reset, a wreck, a sinking car) still work.
    /// Reads Hamza's <see cref="IntroMission"/> from outside (its <see cref="IntroMission.HandlesDeath"/> is exactly "the chase is
    /// on"); that mission raises no events, so this checks one static bool per frame and only acts when it changes.
    /// (The cutscene itself is covered by PlayerVehicleInteraction: no exit while GamePlayer.MenuOpen.)
    /// Lives under ///Manager/// (not a Singleton).
    /// </summary>
    [DisallowMultipleComponent]
    public class IntroChaseExitLock : MonoBehaviour
    {
        [Tooltip("Off: the player may get out during the intro chase (as before).")]
        [SerializeField] private bool lockExitDuringChase = true;
        [Tooltip("Shown when he tries to get out during the chase.")]
        [SerializeField] private string lockedMessage = "Lose the cops first!";

        private bool locked;

        private void Update()
        {
            bool shouldLock = lockExitDuringChase && IntroMission.HandlesDeath;
            if (shouldLock == locked) return;
            locked = shouldLock;
            PlayerVehicleInteraction.SetExitLock(this, locked, lockedMessage);
        }

        private void OnDisable()
        {
            if (!locked) return;
            locked = false;
            PlayerVehicleInteraction.SetExitLock(this, false);
        }
    }
}
