using System.Reflection;
using OpenworldCrime.Police;
using OpenworldCrime.Racing;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Keeps character switching (Hamza's CharacterSwitcher, Alt + wheel) closed while the police are after the player (any
    /// wanted level) or a street race is on, without changing the switcher: it is switched off for that time and switched back
    /// on when the wanted level clears / the race ends. Pressing the switch key meanwhile shows the reason in the switcher's own
    /// message. It never interrupts a switch or an open wheel (the wheel slows time): it waits for it to finish.
    /// Lives under ///Manager/// (not a Singleton). Update runs only while switching is blocked.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSwitchGuard : MonoBehaviour
    {
        [SerializeField] private CharacterSwitcher switcher;
        [SerializeField] private RaceManager race;
        [SerializeField] private string wantedMessage = "Lose the cops to switch character";
        [SerializeField] private string raceMessage = "Finish the race to switch character";

        private static readonly FieldInfo WheelOpenField = typeof(CharacterSwitcher).GetField("wheelOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WheelField = typeof(CharacterSwitcher).GetField("wheel", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool blocked;
        private bool switchedOff;

        // Subscribed here (not in Start): the component is disabled while switching is allowed, and a disabled one gets no Start.
        private void Awake()
        {
            if (switcher == null) Debug.LogError($"[{nameof(CharacterSwitchGuard)}] No CharacterSwitcher on '{name}'.", this);
            if (race != null) race.OnStateChanged += HandleRaceState;
            WantedManager.OnWantedChanged += HandleWanted;
            Refresh();
        }

        private void OnDestroy()
        {
            WantedManager.OnWantedChanged -= HandleWanted;
            if (race != null) race.OnStateChanged -= HandleRaceState;
            if (switchedOff && switcher != null) switcher.enabled = true;
        }

        private void HandleWanted(int stars, WantedState state) => Refresh();
        private void HandleRaceState(RaceState state) => Refresh();

        private void Refresh()
        {
            if (switcher == null) return;
            blocked = WantedManager.IsPlayerWanted || RaceManager.IsRaceActive;
            if (!blocked && switchedOff)
            {
                switcher.enabled = true;
                switchedOff = false;
            }
            enabled = blocked || switchedOff;
        }

        // Only while blocked: switch the switcher off once it is idle, and answer the switch key with the reason.
        private void Update()
        {
            if (!blocked) { Refresh(); return; }
            if (!switchedOff && !switcher.Busy && !IsWheelOpen())
            {
                switcher.enabled = false;
                switchedOff = true;
            }
            if (switchedOff && GameInput.SwitchDown) ShowReason();
        }

        private bool IsWheelOpen() => WheelOpenField != null && (bool)WheelOpenField.GetValue(switcher);

        private void ShowReason()
        {
            string reason = RaceManager.IsRaceActive ? raceMessage : wantedMessage;
            var wheel = WheelField != null ? WheelField.GetValue(switcher) as CharacterWheelUI : null;
            if (wheel != null) wheel.Flash(reason);
        }
    }
}
