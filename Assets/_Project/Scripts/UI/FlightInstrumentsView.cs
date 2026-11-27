using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Small flight instrument panel (speed km/h, altitude above ground m, throttle % for a plane, STALL warning) shown while the
    /// player flies a helicopter or plane (<see cref="IFlyingVehicle"/>), from the vehicle interaction's entered / exited
    /// events like <see cref="VehicleSpeedReadout"/>. LateUpdate only runs while shown; the texts only change when a whole
    /// number changes (cached strings, no per-frame allocation). A HUD widget: it only drives its own elements.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlightInstrumentsView : MonoBehaviour
    {
        private const int MaxShown = 999;

        [Header("References")]
        [Tooltip("Vehicle interaction of every playable character (character switching).")]
        [SerializeField] private PlayerVehicleInteraction[] players = new PlayerVehicleInteraction[0];
        [Tooltip("The panel's root, shown while flying.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Text speedText;
        [SerializeField] private Text altitudeText;
        [Tooltip("Throttle (planes only; hidden for a helicopter).")]
        [SerializeField] private Text throttleText;
        [SerializeField] private GameObject throttleRow;
        [Tooltip("Shown while a plane is stalled.")]
        [SerializeField] private GameObject stallWarning;

        private string[] numbers;
        private IVehicleController vehicle;
        private IFlyingVehicle flying;
        private PlaneController plane;
        private int shownSpeed = -1, shownAltitude = -1, shownThrottle = -1;

        private void Awake()
        {
            if (players.Length == 0 || panel == null || speedText == null || altitudeText == null)
            {
                Debug.LogError($"[{nameof(FlightInstrumentsView)}] Missing references on '{name}' (players, panel, texts).", this);
                enabled = false;
                return;
            }
            numbers = new string[MaxShown + 1];
            for (int i = 0; i <= MaxShown; i++) numbers[i] = i.ToString();
            IVehicleController driven = null;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].OnVehicleEntered += HandleEntered;
                players[i].OnVehicleExited += HandleExited;
                if (players[i].IsInVehicle && players[i].isActiveAndEnabled) driven = players[i].CurrentVehicle;
            }
            Show(driven);
        }

        // Subscribed for the object's lifetime: the component itself switches off while hidden.
        private void OnDestroy()
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                players[i].OnVehicleEntered -= HandleEntered;
                players[i].OnVehicleExited -= HandleExited;
            }
        }

        private void HandleEntered(IVehicleController entered) => Show(entered);

        private void HandleExited(IVehicleController exited)
        {
            if (exited == null || exited == vehicle || vehicle == null) Show(null);
        }

        private void Show(IVehicleController driven)
        {
            flying = driven as IFlyingVehicle;
            vehicle = flying != null ? driven : null;
            plane = driven as PlaneController;
            bool visible = flying != null;
            panel.SetActive(visible);
            if (throttleRow != null) throttleRow.SetActive(plane != null);
            if (stallWarning != null) stallWarning.SetActive(false);
            shownSpeed = shownAltitude = shownThrottle = -1;
            enabled = visible;
        }

        private void LateUpdate()
        {
            if (vehicle == null || (vehicle as Object) == null || !vehicle.transform.gameObject.activeInHierarchy)
            {
                Show(null);
                return;
            }
            Set(speedText, ref shownSpeed, vehicle.CurrentSpeed * 3.6f);
            Set(altitudeText, ref shownAltitude, flying.HeightAboveGround);
            if (plane != null)
            {
                if (throttleText != null) Set(throttleText, ref shownThrottle, plane.Throttle01 * 100f);
                if (stallWarning != null && stallWarning.activeSelf != plane.IsStalled) stallWarning.SetActive(plane.IsStalled);
            }
        }

        private void Set(Text text, ref int shown, float value)
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(value), 0, MaxShown);
            if (v == shown) return;
            shown = v;
            text.text = numbers[v];
        }

#if UNITY_EDITOR
        public void EditorSetup(PlayerVehicleInteraction[] interactions, GameObject root, Text speed, Text altitude, Text throttle, GameObject throttleLine, GameObject stall)
        {
            players = interactions; panel = root; speedText = speed; altitudeText = altitude; throttleText = throttle; throttleRow = throttleLine; stallWarning = stall;
        }
#endif
    }
}
