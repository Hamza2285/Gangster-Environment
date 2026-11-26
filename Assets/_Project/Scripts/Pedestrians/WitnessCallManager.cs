using System;
using OpenworldCrime.Police;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Carjack victims who phone the police (scene manager under ///Manager///, not a Singleton).
    /// When the player pulls a living driver out of his car (<see cref="TrafficVehicleDriver.OnDriverThrownOut"/>) this rolls
    /// Call Chance; a picked driver gets a <see cref="PedestrianPhoneCall"/> (once per pooled person) and calls once he is back on
    /// his feet - unless the player fired a gun nearby a moment ago (then he just runs). A call that gets through is reported as a
    /// stolen vehicle through the existing police rules (<see cref="CrimeReporter.ReportWitnessCall"/> -&gt; WantedManager), and
    /// <see cref="OnWitnessCalledPolice"/> tells the HUD.
    /// Everything is event driven; no Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class WitnessCallManager : MonoBehaviour
    {
        [SerializeField] private WitnessCallSettings settings;
        [Tooltip("Off: carjack victims never call.")]
        [SerializeField] private bool callsEnabled = true;

        /// <summary>A witness got through to the police. Args: where he stands, the HUD line (may be empty).</summary>
        public static event Action<Vector3, string> OnWitnessCalledPolice;

        /// <summary>The played character (whoever is played right now), or null.</summary>
        public static Transform PlayerTransform
        {
            get
            {
                IGamePlayer current = GamePlayer.Current;
                return current != null ? current.transform : null;
            }
        }

        private float lastGunshotTime = -1000f;
        private Vector3 lastGunshotPosition;

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError($"[{nameof(WitnessCallManager)}] No WitnessCallSettings on '{name}': carjack victims will not call the police.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (settings == null) return;
            TrafficVehicleDriver.OnDriverThrownOut += HandleDriverThrownOut;
            CrimeReporter.OnCrimeCommitted += HandleCrime;
            PedestrianPhoneCall.OnAnyCallCompleted += HandleCallCompleted;
        }

        private void OnDisable()
        {
            TrafficVehicleDriver.OnDriverThrownOut -= HandleDriverThrownOut;
            CrimeReporter.OnCrimeCommitted -= HandleCrime;
            PedestrianPhoneCall.OnAnyCallCompleted -= HandleCallCompleted;
        }

        /// <summary>False while the area is in a gunfire panic (the player shot nearby a moment ago): people run, nobody calls.</summary>
        public bool CanCall(Vector3 position)
        {
            if (!callsEnabled || !isActiveAndEnabled) return false;
            if (Time.time - lastGunshotTime > settings.gunfirePanicSeconds) return true;
            return (position - lastGunshotPosition).sqrMagnitude > settings.gunfirePanicRadius * settings.gunfirePanicRadius;
        }

        private void HandleDriverThrownOut(PedestrianDamage driver, Vector3 car)
        {
            if (!callsEnabled || driver == null || driver.IsDead || UnityEngine.Random.value >= settings.callChance) return;
            if (driver.GetComponent<PoliceOfficerUnit>() != null || driver.GetComponent<PedestrianWalker>() == null) return; // civilians only
            PedestrianPhoneCall call = driver.GetComponent<PedestrianPhoneCall>();
            if (call == null) call = driver.gameObject.AddComponent<PedestrianPhoneCall>();
            if (call != null) call.Arm(settings, this);
        }

        private void HandleCrime(CrimeReport report)
        {
            if (report.Type != CrimeType.GunshotFired) return;
            lastGunshotTime = Time.time;
            lastGunshotPosition = report.Position;
        }

        private void HandleCallCompleted(PedestrianPhoneCall caller)
        {
            if (caller == null) return;
            Vector3 position = caller.transform.position;
            CrimeReporter.ReportWitnessCall(CrimeType.VehicleStolen, position, caller.transform);
            OnWitnessCalledPolice?.Invoke(position, settings.calledMessage);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnWitnessCalledPolice = null;
    }
}
