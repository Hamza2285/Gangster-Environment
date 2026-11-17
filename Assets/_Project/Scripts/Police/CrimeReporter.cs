using System;
using Invector;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Turns what the player does into <see cref="CrimeReport"/>s (scene manager under ///Manager///, not a Singleton: listeners use
    /// the static <see cref="OnCrimeCommitted"/>). It only reports; <see cref="WantedManager"/> decides whether police saw it.
    /// Sources (all events, nothing polled except the aim check while aiming):
    /// - gunshots (<see cref="PlayerCombatController.OnShot"/>);
    /// - pedestrians and officers hurt or killed by the player or his car (<see cref="PedestrianDamage.OnAnyDamaged"/>);
    /// - traffic cars shot by the player, police patrol cars included (<see cref="TrafficVehicleAdapter.OnCarThreatened"/>);
    /// - carjacking a car with a driver at the wheel (<see cref="PlayerVehicleInteraction.OnVehicleEnterStarted"/>);
    /// - police pursuit cars shot, rammed or blown up (<see cref="PoliceCarUnit.OnAnyDamaged"/> / <see cref="PoliceCarUnit.OnAnyWrecked"/>);
    /// - keeping a gun aimed at an officer for Aim Threat Seconds.
    /// </summary>
    [DisallowMultipleComponent]
    public class CrimeReporter : MonoBehaviour
    {
        [SerializeField] private PoliceSettings settings;

        [Header("Player")]
        [SerializeField] private Transform player;
        [SerializeField] private PlayerCombatController combat;
        [SerializeField] private vShooterMeleeInput shooterInput;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;

        [Header("Street")]
        [Tooltip("Finds street officers on the aim line (aiming at a policeman).")]
        [SerializeField] private PedestrianSpawner pedestrians;
        [Tooltip("Seconds between aim checks while aiming.")]
        [SerializeField, Min(0.05f)] private float aimCheckInterval = 0.2f;
        [Tooltip("How close (m) the aim line has to pass to an officer's body.")]
        [SerializeField, Min(0.1f)] private float aimTolerance = 1f;

        /// <summary>Raised for every crime the player commits (witnessed or not).</summary>
        public static event Action<CrimeReport> OnCrimeCommitted;

        private bool isAiming;
        private float nextAimCheck;
        private float aimedAtOfficerTime;
        private bool aimThreatReported;
        private bool valid;

        private void Awake()
        {
            valid = settings != null && player != null && combat != null && shooterInput != null && vehicleInteraction != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(CrimeReporter)}] Missing references on '{name}' (settings, player, PlayerCombatController, " +
                               "vShooterMeleeInput, PlayerVehicleInteraction). No crimes are reported.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!valid) return;
            combat.OnShot += HandleShot;
            combat.OnAimChanged += HandleAimChanged;
            vehicleInteraction.OnVehicleEnterStarted += HandleVehicleEnterStarted;
            PedestrianDamage.OnAnyDamaged += HandlePedestrianDamaged;
            TrafficVehicleAdapter.OnCarThreatened += HandleTrafficCarShot;
            PoliceCarUnit.OnAnyDamaged += HandlePoliceCarDamaged;
            PoliceCarUnit.OnAnyWrecked += HandlePoliceCarWrecked;
            TrafficBikeAdapter.OnRiderShot += HandleBikeRiderShot;
            TrafficBikeAdapter.OnRiderKnockedOff += HandleBikeRiderKnockedOff;
        }

        private void OnDisable()
        {
            if (combat != null)
            {
                combat.OnShot -= HandleShot;
                combat.OnAimChanged -= HandleAimChanged;
            }
            if (vehicleInteraction != null) vehicleInteraction.OnVehicleEnterStarted -= HandleVehicleEnterStarted;
            PedestrianDamage.OnAnyDamaged -= HandlePedestrianDamaged;
            TrafficVehicleAdapter.OnCarThreatened -= HandleTrafficCarShot;
            PoliceCarUnit.OnAnyDamaged -= HandlePoliceCarDamaged;
            PoliceCarUnit.OnAnyWrecked -= HandlePoliceCarWrecked;
            TrafficBikeAdapter.OnRiderShot -= HandleBikeRiderShot;
            TrafficBikeAdapter.OnRiderKnockedOff -= HandleBikeRiderKnockedOff;
            isAiming = false;
        }

        // Runs the aim check only while aiming, at Aim Check Interval.
        private void Update()
        {
            if (!isAiming || Time.time < nextAimCheck) return;
            nextAimCheck = Time.time + aimCheckInterval;
            if (IsAimingAtOfficer(out Transform officer))
            {
                aimedAtOfficerTime += aimCheckInterval;
                if (!aimThreatReported && aimedAtOfficerTime >= settings.aimThreatSeconds)
                {
                    aimThreatReported = true;
                    Report(CrimeType.AimedAtOfficer, officer.position, officer, true);
                }
            }
            else
            {
                aimedAtOfficerTime = 0f;
                aimThreatReported = false;
            }
        }

        /// <summary>True when the damage came from the player or the car he drives.</summary>
        public bool IsFromPlayer(Transform sender)
        {
            if (sender == null) return false;
            if (sender == player || sender.IsChildOf(player)) return true;
            if (vehicleInteraction.CurrentVehicle == null) return false;
            Transform car = vehicleInteraction.CurrentVehicle.transform;
            return sender == car || sender.IsChildOf(car);
        }

        private void Report(CrimeType type, Vector3 position, Transform victim, bool againstPolice)
        {
            OnCrimeCommitted?.Invoke(new CrimeReport(type, position, victim, againstPolice));
        }

        private void HandleShot(vShooterWeapon weapon) => Report(CrimeType.GunshotFired, player.position, null, false);

        private void HandleAimChanged(bool aiming)
        {
            isAiming = aiming;
            nextAimCheck = 0f;
            aimedAtOfficerTime = 0f;
            aimThreatReported = false;
        }

        private void HandleVehicleEnterStarted(IVehicleController vehicle)
        {
            if (vehicle != null && vehicle.EntryPrompt == VehicleEntryPrompt.Steal)
                Report(CrimeType.VehicleStolen, vehicle.transform.position, vehicle.transform, false);
        }

        private void HandlePedestrianDamaged(PedestrianDamage pedestrian, vDamage damage, bool killed)
        {
            if (damage == null || !IsFromPlayer(damage.sender)) return;
            bool officer = PoliceOfficerUnit.IsOfficer(pedestrian);
            CrimeType type;
            if (officer) type = killed ? CrimeType.OfficerKilled : CrimeType.OfficerHurt;
            else if (killed) type = CrimeType.PedestrianKilled;
            else type = damage.damageType == "Vehicle" ? CrimeType.PedestrianRunOver : CrimeType.PedestrianHurt;
            Report(type, pedestrian.BodyPosition, pedestrian.transform, officer);
        }

        // A traffic biker is a pedestrian on a bike: shooting him is shooting a person.
        private void HandleBikeRiderShot(TrafficBikeAdapter bike, Transform sender, bool killed)
        {
            if (bike == null || !IsFromPlayer(sender)) return;
            Report(killed ? CrimeType.PedestrianKilled : CrimeType.PedestrianHurt, bike.transform.position, bike.transform, false);
        }

        // Rammed off his bike by the player's vehicle (a run-over), or shoved off it by the player stealing it (a theft).
        private void HandleBikeRiderKnockedOff(TrafficBikeAdapter bike, Transform by)
        {
            if (bike == null) return;
            if (by == null) Report(CrimeType.VehicleStolen, bike.transform.position, bike.transform, false);
            else if (IsFromPlayer(by)) Report(CrimeType.PedestrianRunOver, bike.transform.position, bike.transform, false);
        }

        private void HandleTrafficCarShot(TrafficVehicleAdapter car, Transform sender)
        {
            if (car == null || !IsFromPlayer(sender)) return;
            Report(car.IsPolice ? CrimeType.PoliceVehicleShot : CrimeType.VehicleShot, car.transform.position, car.transform, car.IsPolice);
        }

        private void HandlePoliceCarDamaged(PoliceCarUnit car, Transform sender, bool crash)
        {
            if (!IsFromPlayer(sender)) return;
            Report(crash ? CrimeType.PoliceVehicleRammed : CrimeType.PoliceVehicleShot, car.transform.position, car.transform, true);
        }

        private void HandlePoliceCarWrecked(PoliceCarUnit car, Transform lastAttacker)
        {
            if (!IsFromPlayer(lastAttacker)) return;
            Report(CrimeType.PoliceVehicleDestroyed, car.transform.position, car.transform, true);
        }

        // The line Invector aims along (weapon aim reference -> AimPosition, already stopped by walls), within Aim Threat Distance.
        private bool IsAimingAtOfficer(out Transform officer)
        {
            officer = null;
            vShooterWeapon weapon = shooterInput.CurrentActiveWeapon;
            Vector3 from = weapon != null && weapon.aimReference != null ? weapon.aimReference.position : player.position + Vector3.up * 1.4f;
            Vector3 to = shooterInput.AimPosition;
            Vector3 line = to - from;
            float length = line.magnitude;
            if (length < 0.01f) return false;
            float reach = Mathf.Min(length + aimTolerance, settings.aimThreatDistance);
            Vector3 direction = line / length;

            PoliceOfficerUnit unit = PoliceOfficerUnit.FindAlongLine(from, direction, reach, aimTolerance);
            if (unit != null)
            {
                officer = unit.transform;
                return true;
            }
            if (pedestrians == null) return false;
            PedestrianWalker walker = pedestrians.FindFirstAlongLine(from, from + direction * reach, aimTolerance);
            if (walker == null || !PoliceDispatcher.IsStreetOfficer(walker)) return false;
            officer = walker.transform;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnCrimeCommitted = null;
    }
}
