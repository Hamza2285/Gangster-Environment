using System;
using System.Collections;
using System.Collections.Generic;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// GTA-style panic of traffic drivers threatened by the player (one per scene, under ///Manager///, not a Singleton).
    /// Nobody ever drives at the player: the only two reactions are getting away.
    /// - Threats: aiming at a driver or his car from in front or beside it (within Aim Distance, in line of sight), shooting at or
    ///   near it, shooting the car, and gunfire close by (a reduced chance for drivers who only hear it). Crashes never count:
    ///   a driver the player bumps or rams honks and drives on (<see cref="TrafficVehicleAdapter"/>).
    /// - Each threatened driver rolls once (<see cref="TrafficPanicSettings.rollAgainAfter"/>):
    ///   * bail out: the car brakes to a stop, the driver opens the door, climbs out and runs as an ordinary street pedestrian; the
    ///     car stays with its engine off and the door open, an abandoned drivable car (<see cref="TrafficVehicleAdapter.TryBeginBailOut"/>);
    ///   * floor it: iTS AI off, full throttle along the road ahead (lane, then junction connectors leading away from the player),
    ///     swerving round cars, walls and the player when he is in the way; it can still hit him if there is no room. After the
    ///     panic time or distance he drives on calmly and is recycled into the iTS pool out of sight, so traffic keeps its count;
    ///   * or nothing: he carries on in traffic.
    ///   A fleeing driver who gets stuck (walled in, or the player's car blocks him) stops and waits on the brakes until he can be
    ///   recycled out of sight; he only gives up and bails out when <see cref="TrafficPanicSettings.bailOutWhenStuck"/> is on.
    /// - Caps: at most Max Panicking at once, Max Reactions Per Threat for one shot or aim check, a cooldown between reactions and
    ///   Max Reactions Per Minute.
    /// - Police are left alone (their own chase behaviour). A car without a drivable twin cannot bail out and floors it instead.
    /// No Update: FixedUpdate only works while someone flees, and the aim check only runs while the player aims.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficPanicManager : MonoBehaviour
    {
        private const float WheelBase = 2.6f;
        private const float MaxSteerDegrees = 55f;

        public enum Reaction { None, BailOut, FloorIt }

        private sealed class Flight
        {
            public TrafficVehicleAdapter Car;
            public float StartedAt;
            public float PanicUntil;
            public bool Calm;
            public bool Parked;
            public float StuckTime;
            public float NextObstacleCheck;
            public float NextWarn;
            public float Swerve;
            public bool Blocked;
            public bool Yield;
            public readonly List<Vector3> Route = new List<Vector3>(64);
            public int RouteIndex;
            public int LastLane = -1;
            public int LastConnector = -1;
            public bool RouteEnded;
            public string EndReason;
        }

        private sealed class Pending
        {
            public TrafficVehicleAdapter Car;
            public Reaction Reaction;
            public float At;
            public Vector3 Threat;
        }

        [Header("References")]
        [SerializeField] private TrafficPanicSettings settings;
        [SerializeField] private PlayerCombatController combat;
        [Tooltip("Supplies the aim point and the active weapon's aim reference.")]
        [SerializeField] private vShooterMeleeInput shooterInput;
        [Tooltip("The player (rides along in a car while he drives one).")]
        [SerializeField] private Transform player;
        [Tooltip("Warned in front of a fleeing car. Optional.")]
        [SerializeField] private PedestrianSpawner pedestrians;
        [Tooltip("Lanes a fleeing car drives along. Empty = it brakes and waits to be recycled.")]
        [SerializeField] private TSMainManager roads;

        /// <summary>Raised when a driver's reaction starts. Args: the car, what he does.</summary>
        public static event Action<TrafficVehicleAdapter, Reaction> OnDriverPanicked;
        /// <summary>Raised when a fleeing car goes back to the traffic pool (or was taken by another sequence). Args: the car, why.</summary>
        public static event Action<TrafficVehicleAdapter, string> OnFlightEnded;

        private readonly List<Flight> flights = new List<Flight>(4);
        private readonly List<Pending> pending = new List<Pending>(4);
        private readonly List<TrafficVehicleAdapter> bailing = new List<TrafficVehicleAdapter>(4);
        private readonly Dictionary<TrafficVehicleAdapter, float> rolledUntil = new Dictionary<TrafficVehicleAdapter, float>();
        private readonly RaycastHit[] castHits = new RaycastHit[8];
        private readonly Queue<float> recentReactions = new Queue<float>(8);
        private float nextReactionAllowed;
        private Coroutine aimRoutine;
        private bool valid;

        /// <summary>Drivers fleeing right now (debugging, tests).</summary>
        public int ActiveFlights => flights.Count;
        /// <summary>Drivers between the threat and the hand-over of their stopped car (debugging, tests).</summary>
        public int ActiveBailOuts => bailing.Count;
        public int BailOutsStarted { get; private set; }
        public int FlightsStarted { get; private set; }
        public string LastEndReason { get; private set; }
        /// <summary>Panicking right now: fleeing, braking to bail out, or about to react.</summary>
        public int PanickingCount => flights.Count + bailing.Count + pending.Count;

        private void Awake()
        {
            valid = settings != null && combat != null && player != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(TrafficPanicManager)}] Missing settings, combat controller or player on '{name}'. Drivers will not react to guns.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!valid) return;
            combat.OnShot += HandleShot;
            combat.OnAimChanged += HandleAimChanged;
            TrafficVehicleAdapter.OnCarThreatened += HandleCarThreatened;
            TrafficVehicleAdapter.OnCarDisabled += HandleCarDisabled;
        }

        private void OnDisable()
        {
            if (combat != null)
            {
                combat.OnShot -= HandleShot;
                combat.OnAimChanged -= HandleAimChanged;
            }
            TrafficVehicleAdapter.OnCarThreatened -= HandleCarThreatened;
            TrafficVehicleAdapter.OnCarDisabled -= HandleCarDisabled;
            aimRoutine = null;
        }

        /// <summary>Makes this driver react now, skipping the roll and the cooldown but not the caps. For tests and scripted scenes.</summary>
        public bool ForceReaction(TrafficVehicleAdapter car, Reaction reaction)
        {
            if (car == null || reaction == Reaction.None || PanickingCount >= settings.maxPanicking || !CanPanic(car)) return false;
            return StartReaction(car, reaction, player.position);
        }

        // ---------- threats ----------

        private void HandleShot(vShooterWeapon weapon)
        {
            int budget = settings.maxReactionsPerThreat;
            Vector3 threat = player.position;
            // The car he shot at (or whose driver he shot past) first, with the full chance.
            if (TryGetAimLine(out Vector3 from, out Vector3 to))
            {
                TrafficVehicleAdapter aimed = FindAimedAtCar(from, to);
                if (aimed != null && Roll(aimed, 1f, threat)) budget--;
            }

            // Everyone else close to the player or to where the bullet went only heard it.
            float playerSqr = settings.gunfireRadius * settings.gunfireRadius;
            float impactSqr = settings.impactRadius * settings.impactRadius;
            bool hasImpact = TryGetAimLine(out _, out Vector3 impact);
            IReadOnlyList<TrafficVehicleAdapter> cars = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < cars.Count && budget > 0; i++)
            {
                TrafficVehicleAdapter car = cars[i];
                Vector3 position = car.transform.position;
                bool near = (position - threat).sqrMagnitude <= playerSqr || (hasImpact && (position - impact).sqrMagnitude <= impactSqr);
                if (near && Roll(car, settings.gunfireReactionShare, threat)) budget--;
            }
            if (budget < settings.maxReactionsPerThreat) nextReactionAllowed = Time.time + settings.reactionCooldown;
        }

        private void HandleCarThreatened(TrafficVehicleAdapter car, Transform by)
        {
            if (by == null) return;
            // The player himself or the car he drives; other cars and explosions do not count.
            Transform root = by.root;
            bool byPlayer = root == player.root || (root.TryGetComponent(out RCCVehicleAdapter vehicle) && vehicle.IsControlled);
            if (byPlayer && Roll(car, 1f, player.position)) nextReactionAllowed = Time.time + settings.reactionCooldown;
        }

        private void HandleAimChanged(bool aiming)
        {
            if (aimRoutine != null) StopCoroutine(aimRoutine);
            aimRoutine = aiming && shooterInput != null && isActiveAndEnabled ? StartCoroutine(AimRoutine()) : null;
        }

        private IEnumerator AimRoutine()
        {
            var wait = new WaitForSeconds(settings.aimCheckInterval);
            while (true)
            {
                yield return wait;
                if (!TryGetAimLine(out Vector3 from, out Vector3 to)) continue;
                TrafficVehicleAdapter car = FindAimedAtCar(from, to);
                if (car != null && Roll(car, 1f, player.position)) nextReactionAllowed = Time.time + settings.reactionCooldown;
            }
        }

        // The line Invector itself aims along: weapon aim reference -> AimPosition (already stopped by walls and hit colliders).
        private bool TryGetAimLine(out Vector3 from, out Vector3 to)
        {
            to = Vector3.zero;
            from = player.position + Vector3.up * 1.4f;
            if (shooterInput == null) return false;
            vShooterWeapon weapon = shooterInput.CurrentActiveWeapon;
            if (weapon != null && weapon.aimReference != null) from = weapon.aimReference.position;
            to = shooterInput.AimPosition;
            return (to - from).sqrMagnitude > 0.01f;
        }

        // The nearest car whose centre the aim line passes within Aim Tolerance (the aim point resting on its body or glass counts
        // too), close to the player, with him in front of or beside it, and not behind a wall.
        private TrafficVehicleAdapter FindAimedAtCar(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float length = line.magnitude;
            Vector3 direction = line / length;
            // The aim point usually sits on the car's surface: look a little past it.
            float reach = Mathf.Min(settings.aimDistance + 5f, length + 3f);
            float maxSqr = settings.aimDistance * settings.aimDistance;
            float toleranceSqr = settings.aimTolerance * settings.aimTolerance;
            Vector3 playerPosition = player.position;
            IReadOnlyList<TrafficVehicleAdapter> cars = TrafficVehicleAdapter.ActiveCars;
            TrafficVehicleAdapter best = null;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < cars.Count; i++)
            {
                TrafficVehicleAdapter car = cars[i];
                Transform t = car.transform;
                Vector3 toPlayer = playerPosition - t.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > maxSqr) continue;
                Vector3 centre = t.position + Vector3.up * 0.9f;
                Vector3 toCar = centre - from;
                float along = Vector3.Dot(toCar, direction);
                if (along < 0f || along > reach || along >= bestAlong) continue;
                if (toCar.sqrMagnitude - along * along > toleranceSqr) continue;
                if (Vector3.Dot(t.forward, toPlayer.normalized) < settings.minFrontDot) continue;
                best = car;
                bestAlong = along;
            }
            if (best == null) return null;
            // Line of sight to the cabin; the car's own colliders end the line.
            Vector3 cabin = best.transform.position + Vector3.up * 1.1f;
            if (Physics.Linecast(from, cabin, out RaycastHit hit, settings.lineOfSightBlockers, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(best.transform)) return null;
            return best;
        }

        // ---------- reactions ----------

        private bool CanPanic(TrafficVehicleAdapter car)
        {
            return car != null && car.CanPanic && (!car.IsPolice || settings.includePolice) && !IsPanicking(car);
        }

        private bool IsPanicking(TrafficVehicleAdapter car)
        {
            if (bailing.Contains(car)) return true;
            for (int i = 0; i < pending.Count; i++) if (pending[i].Car == car) return true;
            for (int i = 0; i < flights.Count; i++) if (flights[i].Car == car) return true;
            return false;
        }

        // One roll per driver per Roll Again After; returns true if he is going to react. The caller starts the cooldown, so one
        // shot can still scare up to Max Reactions Per Threat drivers.
        private bool Roll(TrafficVehicleAdapter car, float chanceScale, Vector3 threat)
        {
            if (!CanPanic(car)) return false;
            if (rolledUntil.TryGetValue(car, out float until) && Time.time < until) return false;
            if (PanickingCount >= settings.maxPanicking || Time.time < nextReactionAllowed) return false;
            while (recentReactions.Count > 0 && Time.time - recentReactions.Peek() > 60f) recentReactions.Dequeue();
            if (recentReactions.Count >= settings.maxReactionsPerMinute) return false;
            rolledUntil[car] = Time.time + settings.rollAgainAfter;

            float bail = settings.bailOutChance, flee = settings.floorItChance;
            float sum = bail + flee;
            if (sum > 1f)
            {
                bail /= sum;
                flee /= sum;
            }
            float roll = UnityEngine.Random.value;
            Reaction reaction = roll < bail * chanceScale ? Reaction.BailOut
                              : roll < (bail + flee) * chanceScale ? Reaction.FloorIt
                              : Reaction.None;
            if (reaction == Reaction.None) return false;
            recentReactions.Enqueue(Time.time);

            float delay = UnityEngine.Random.Range(settings.reactionDelay.x, Mathf.Max(settings.reactionDelay.x, settings.reactionDelay.y));
            pending.Add(new Pending { Car = car, Reaction = reaction, At = Time.time + delay, Threat = threat });
            if (pending.Count == 1) StartCoroutine(PendingRoutine());
            return true;
        }

        private IEnumerator PendingRoutine()
        {
            while (pending.Count > 0)
            {
                yield return null;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Pending p = pending[i];
                    if (Time.time < p.At) continue;
                    pending.RemoveAt(i);
                    if (p.Car != null && p.Car.CanPanic) StartReaction(p.Car, p.Reaction, p.Threat);
                }
            }
        }

        private bool StartReaction(TrafficVehicleAdapter car, Reaction reaction, Vector3 threat)
        {
            // No drivable twin (no door to open): he floors it instead.
            if (reaction == Reaction.BailOut && car.TryBeginBailOut(threat, settings.bailOutBrakeScale))
            {
                bailing.Add(car);
                BailOutsStarted++;
                CarHorn.Honk(car.transform, false);
                OnDriverPanicked?.Invoke(car, Reaction.BailOut);
                return true;
            }
            if (!car.TryBeginFlee(settings.engineTorque, settings.brakeTorque)) return false;
            var flight = new Flight
            {
                Car = car,
                StartedAt = Time.time,
                PanicUntil = Time.time + UnityEngine.Random.Range(settings.fleeSeconds.x, Mathf.Max(settings.fleeSeconds.x, settings.fleeSeconds.y))
            };
            PlanRoute(flight);
            flights.Add(flight);
            FlightsStarted++;
            CarHorn.Honk(car.transform, true);
            OnDriverPanicked?.Invoke(car, Reaction.FloorIt);
            return true;
        }

        private void HandleCarDisabled(TrafficVehicleAdapter car)
        {
            rolledUntil.Remove(car);
            bailing.Remove(car);
        }

        // ---------- driving ----------

        private void FixedUpdate()
        {
            for (int i = bailing.Count - 1; i >= 0; i--)
            {
                // Handed over (car disabled) or taken by another sequence (driver shot, wrecked).
                if (bailing[i] == null || bailing[i].State != TrafficVehicleState.BailingOut) bailing.RemoveAt(i);
            }
            if (flights.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight flight = flights[i];
                // Driver killed, car wrecked, or recycled: the adapter's own sequences own the car now.
                if (flight.Car == null || !flight.Car.isActiveAndEnabled || flight.Car.State != TrafficVehicleState.Fleeing)
                {
                    Finish(i, flight, flight.EndReason ?? (flight.Car != null ? flight.Car.State.ToString() : "gone"));
                    continue;
                }
                if (Tick(flight, dt))
                {
                    flight.Car.EndFlee();
                    Finish(i, flight, flight.EndReason ?? "recycled");
                }
            }
        }

        private void Finish(int index, Flight flight, string reason)
        {
            flights.RemoveAt(index);
            LastEndReason = reason;
            OnFlightEnded?.Invoke(flight.Car, reason);
        }

        // Returns true once the car can go back to the pool.
        private bool Tick(Flight flight, float dt)
        {
            TrafficVehicleAdapter car = flight.Car;
            Transform t = car.transform;
            Vector3 position = t.position;
            float speed = Vector3.Dot(car.Body.linearVelocity, t.forward);
            float absSpeed = Mathf.Abs(speed);
            Vector3 toPlayer = player.position - position;
            toPlayer.y = 0f;

            if (!flight.Calm)
            {
                if (t.up.y < 0.4f) BeginCalm(flight, "flipped");
                else if (Time.time >= flight.PanicUntil) BeginCalm(flight, "calmed down");
                else if (toPlayer.sqrMagnitude > settings.calmDistance * settings.calmDistance) BeginCalm(flight, "far away");
            }
            if (flight.Calm || flight.Parked)
            {
                float cameraSqr = Camera.main != null ? (Camera.main.transform.position - position).sqrMagnitude : float.MaxValue;
                bool overdue = Time.time - flight.StartedAt > settings.maxFleeSeconds && cameraSqr > settings.overdueRecycleDistance * settings.overdueRecycleDistance;
                bool far = cameraSqr > settings.farRecycleDistance * settings.farRecycleDistance;
                if (overdue || far || VehicleHealth.IsOutOfSight(position, settings.recycleKeepDistance)) return true;
            }

            if (Time.time >= flight.NextObstacleCheck)
            {
                flight.NextObstacleCheck = Time.time + settings.obstacleCheckInterval;
                UpdateAvoidance(flight, t, absSpeed);
            }
            if (pedestrians != null && !flight.Calm && Time.time >= flight.NextWarn)
            {
                flight.NextWarn = Time.time + settings.pedestrianWarnInterval;
                pedestrians.TriggerPanic(position + t.forward * settings.pedestrianWarnRadius * 0.6f, settings.pedestrianWarnRadius);
            }

            float steer = 0f, throttle = 0f, brake = 0f;
            if (flight.Parked || !AdvanceRoute(flight, position, out Vector3 aim, out float targetSpeed))
            {
                // Lost the road: he stops and waits to be recycled out of sight.
                brake = 1f;
                if (!flight.Calm) BeginCalm(flight, "no road");
            }
            else
            {
                if (Mathf.Abs(flight.Swerve) > 0.01f)
                {
                    Vector3 ahead = aim - position;
                    ahead.y = 0f;
                    Vector3 right = ahead.sqrMagnitude > 0.01f ? new Vector3(ahead.z, 0f, -ahead.x).normalized : t.right;
                    aim += right * flight.Swerve;
                }
                steer = SteerToward(t, aim, absSpeed);
                if (speed < -0.5f) brake = 1f;
                else if (absSpeed < targetSpeed) throttle = flight.Calm ? 0.5f : 1f;
                else if (absSpeed > targetSpeed + 2f) brake = 0.4f;
                if (flight.Blocked)
                {
                    throttle = 0f;
                    brake = 1f;
                }
                else if (flight.Yield && absSpeed > 3f)
                {
                    // The player is still on the car's line and close: off the gas and onto the brakes while it swerves.
                    throttle = 0f;
                    brake = Mathf.Max(brake, 0.6f);
                }
                else if (flight.Yield)
                {
                    // Slow enough: it creeps round him on the swerve line.
                    throttle = Mathf.Min(throttle, 0.35f);
                }
            }

            bool wantsToMove = throttle > 0f || ((flight.Blocked || flight.Yield) && !flight.Parked);
            flight.StuckTime = wantsToMove && absSpeed < settings.stuckSpeed ? flight.StuckTime + dt : 0f;
            if (flight.StuckTime > settings.stuckSeconds && !flight.Parked)
            {
                // Stuck: he waits on the brakes until the car can be recycled out of sight (or, if the settings allow, leaves the car
                // and runs; a car without a drivable twin always waits).
                if (settings.bailOutWhenStuck && car.TryBailOutFromFlee(player.position, settings.bailOutBrakeScale))
                {
                    flight.EndReason = "stuck, bailed out";
                    bailing.Add(car);
                    return false;
                }
                flight.Parked = true;
                if (!flight.Calm) BeginCalm(flight, "stuck");
            }
            car.DriveFlee(steer, brake, throttle);
            return false;
        }

        private void BeginCalm(Flight flight, string reason)
        {
            flight.Calm = true;
            flight.EndReason = reason;
        }

        // Looks ahead for the player, cars and walls; swerves to the free side, or brakes when neither side is free.
        private void UpdateAvoidance(Flight flight, Transform t, float speed)
        {
            flight.Blocked = false;
            flight.Yield = false;
            float range = Mathf.Max(6f, speed * speed / (2f * 7f) + 4f);
            Vector3 forward = t.forward;
            Vector3 right = t.right;
            // High enough to pass over kerbs and low planters (the wheels climb those); only walls and cars count.
            Vector3 origin = t.position + Vector3.up * 1.35f;

            // The player ahead: the car passes him with Player Clearance between its centre line and him, on whichever side has
            // room, and brakes hard when neither has. It never steers toward him; with no room it can still hit him.
            Vector3 toPlayer = player.position - t.position;
            toPlayer.y = 0f;
            float along = Vector3.Dot(toPlayer, forward);
            float side = Vector3.Dot(toPlayer, right);
            float clearance = settings.playerClearance;
            float maxSwerve = settings.swerveOffset * 1.5f;
            bool playerAhead = along > 0f && along < settings.playerAvoidDistance;
            // Still heading straight at him (the swerve takes a moment) within stopping distance plus a margin.
            flight.Yield = playerAhead && Mathf.Abs(side) < clearance && along < speed * speed / (2f * 6f) + 6f;
            if (playerAhead && Mathf.Abs(side - flight.Swerve) < clearance)
            {
                float passNear = side >= 0f ? side - clearance : side + clearance;
                float passFar = side >= 0f ? side + clearance : side - clearance;
                if (Mathf.Abs(passNear) <= maxSwerve && SideFree(origin, forward, right, passNear, along + 4f)) flight.Swerve = passNear;
                else if (Mathf.Abs(passFar) <= maxSwerve && SideFree(origin, forward, right, passFar, along + 4f)) flight.Swerve = passFar;
                else flight.Blocked = true;
                return;
            }

            if (Obstacle(flight, origin + right * flight.Swerve, forward, range, speed))
            {
                if (flight.Swerve == 0f || !SideFree(origin, forward, right, flight.Swerve, range))
                {
                    float offset = settings.swerveOffset;
                    if (SideFree(origin, forward, right, offset, range) && !(playerAhead && Mathf.Abs(side - offset) < clearance)) flight.Swerve = offset;
                    else if (SideFree(origin, forward, right, -offset, range) && !(playerAhead && Mathf.Abs(side + offset) < clearance)) flight.Swerve = -offset;
                    else flight.Blocked = true;
                }
                return;
            }
            // Path clear: ease back into the lane, unless that brings the player back into the way.
            float eased = Mathf.MoveTowards(flight.Swerve, 0f, settings.swerveOffset * settings.obstacleCheckInterval);
            if (!(playerAhead && Mathf.Abs(side - eased) < clearance)) flight.Swerve = eased;
        }

        private bool SideFree(Vector3 origin, Vector3 forward, Vector3 right, float offset, float range)
        {
            Vector3 start = origin + right * offset * 0.5f;
            Vector3 direction = (forward * range + right * offset * 0.5f).normalized;
            return !Physics.SphereCast(start, 0.6f, direction, out _, range, settings.obstacleLayers, QueryTriggerInteraction.Ignore);
        }

        private bool Obstacle(Flight flight, Vector3 origin, Vector3 forward, float range, float speed)
        {
            int count = Physics.SphereCastNonAlloc(origin + forward * 2.4f, 0.6f, forward, castHits, range, settings.obstacleLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (hit.normal.y > 0.6f || hit.transform.IsChildOf(flight.Car.transform)) continue;
                // Hydrants, bins and benches go down in front of him like under any fast car; they would stop him dead otherwise,
                // because traffic car colliders are not on the layers props break for.
                var prop = hit.collider.GetComponent<OpenworldCrime.Environment.BreakableProp>();
                if (prop != null)
                {
                    if (!prop.IsBroken && speed >= settings.propBreakSpeed && hit.distance <= speed * settings.obstacleCheckInterval * 2f + 0.5f)
                        prop.Break(flight.Car.Body, flight.Car.Body.linearVelocity, 0f, hit.point);
                    continue;
                }
                return true;
            }
            return false;
        }

        // Pure pursuit, with less lock at speed so the car cannot turn on a coin and cannot roll over.
        private float SteerToward(Transform t, Vector3 target, float speed)
        {
            Vector3 local = t.InverseTransformPoint(target);
            float lookAhead = Mathf.Max(6f, new Vector2(local.x, local.z).magnitude);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z));
            if (local.z < 0f) alpha = Mathf.Sign(local.x) * Mathf.PI * 0.5f;
            float degrees = Mathf.Atan2(2f * WheelBase * Mathf.Sin(alpha), lookAhead) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(degrees / MaxSteerDegrees * 3f, -1f, 1f);
            float limit = Mathf.Lerp(1f, settings.steerLockAtSpeed, Mathf.InverseLerp(6f, settings.fleeSpeed, speed));
            return Mathf.Clamp(steer, -limit, limit);
        }

        // ---------- route ----------

        // Follows the route with a look-ahead point; slower before sharp corners and when calm. False when the road ran out.
        private bool AdvanceRoute(Flight flight, Vector3 position, out Vector3 aim, out float targetSpeed)
        {
            aim = position;
            targetSpeed = 0f;
            List<Vector3> route = flight.Route;
            while (flight.RouteIndex < route.Count)
            {
                Vector3 d = route[flight.RouteIndex] - position;
                d.y = 0f;
                // Passed when close, or when it is already behind the car.
                if (d.sqrMagnitude > 49f && Vector3.Dot(d, flight.Car.transform.forward) > 0f) break;
                flight.RouteIndex++;
            }
            if (route.Count - flight.RouteIndex < 6 && !flight.RouteEnded) ExtendRoute(flight);
            if (flight.RouteIndex >= route.Count) return false;

            aim = route[flight.RouteIndex];
            targetSpeed = flight.Calm ? settings.calmSpeed : settings.fleeSpeed;
            // A sharp bend within braking distance: corner speed.
            Vector3 heading = route[flight.RouteIndex] - position;
            heading.y = 0f;
            float distance = 0f;
            for (int i = flight.RouteIndex; i + 1 < route.Count && distance < 35f; i++)
            {
                Vector3 segment = route[i + 1] - route[i];
                segment.y = 0f;
                distance += segment.magnitude;
                if (Vector3.Angle(heading, segment) > settings.cornerAngle)
                {
                    targetSpeed = Mathf.Min(targetSpeed, settings.cornerSpeed);
                    break;
                }
            }
            return true;
        }

        // The rest of the nearest lane going the way the car points, then onward through junctions.
        private void PlanRoute(Flight flight)
        {
            if (roads == null || roads.lanes == null)
            {
                flight.RouteEnded = true;
                return;
            }
            Transform t = flight.Car.transform;
            Vector3 position = t.position;
            Vector3 forward = t.forward;
            float best = settings.laneSearchDistance * settings.laneSearchDistance;
            int bestLane = -1, bestPoint = 0, bestConnector = -1;
            // Lanes, and the junction connectors between them (a car in a junction is on a connector).
            for (int l = 0; l < roads.lanes.Length; l++)
            {
                TSLaneInfo lane = roads.lanes[l];
                if (lane == null) continue;
                if (NearestAhead(lane.points, position, forward, ref best, out int p)) { bestLane = l; bestPoint = p; bestConnector = -1; }
                if (lane.connectors == null) continue;
                for (int c = 0; c < lane.connectors.Length; c++)
                {
                    if (lane.connectors[c] != null && NearestAhead(lane.connectors[c].Points, position, forward, ref best, out p)) { bestLane = l; bestPoint = p; bestConnector = c; }
                }
            }
            if (bestLane < 0)
            {
                flight.RouteEnded = true;
                return;
            }
            if (bestConnector < 0) AppendLane(flight, bestLane, bestPoint);
            else
            {
                TSLaneConnector connector = roads.lanes[bestLane].connectors[bestConnector];
                TSPoints[] points = connector.Points;
                for (int p = bestPoint; p < points.Length; p++) AddPoint(flight, points[p].point);
                if (connector.nextLane >= 0 && connector.nextLane < roads.lanes.Length) AppendLane(flight, connector.nextLane, 0);
                else flight.RouteEnded = true;
            }
            ExtendRoute(flight);
        }

        // The point of this list closest to the car (and closer than best) that lies ahead of it on a segment heading the way it points.
        private static bool NearestAhead(TSPoints[] points, Vector3 position, Vector3 forward, ref float best, out int index)
        {
            index = -1;
            if (points == null) return false;
            for (int p = 0; p + 1 < points.Length; p++)
            {
                Vector3 d = points[p].point - position;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr >= best || Vector3.Dot(d, forward) < 1f) continue;
                Vector3 segment = points[p + 1].point - points[p].point;
                segment.y = 0f;
                if (segment.sqrMagnitude < 0.0001f || Vector3.Dot(segment.normalized, forward) < 0.5f) continue;
                best = sqr;
                index = p;
            }
            return index >= 0;
        }

        private void AppendLane(Flight flight, int lane, int fromPoint)
        {
            TSPoints[] points = roads.lanes[lane].points;
            for (int p = fromPoint; p < points.Length; p++) AddPoint(flight, points[p].point);
            flight.LastLane = lane;
        }

        private static float RemainingLength(Flight flight)
        {
            float length = 0f;
            List<Vector3> route = flight.Route;
            for (int i = Mathf.Max(1, flight.RouteIndex); i < route.Count; i++) length += Vector3.Distance(route[i - 1], route[i]);
            return length;
        }

        private void AddPoint(Flight flight, Vector3 point)
        {
            List<Vector3> route = flight.Route;
            if (route.Count > 0)
            {
                Vector3 d = point - route[route.Count - 1];
                d.y = 0f;
                if (d.sqrMagnitude < 4f) return;
            }
            route.Add(point);
        }

        // Adds junction connectors and lanes until Route Look Ahead metres are planned. At each junction he takes the way leading
        // most directly away from the player while panicking, the straightest one once calm.
        private void ExtendRoute(Flight flight)
        {
            int guard = 0;
            while (flight.LastLane >= 0 && RemainingLength(flight) < settings.routeLookAhead && guard++ < 8)
            {
                TSLaneInfo lane = roads.lanes[flight.LastLane];
                TSLaneConnector[] connectors = lane.connectors;
                if (connectors == null || connectors.Length == 0 || flight.Route.Count < 2)
                {
                    flight.RouteEnded = true;
                    return;
                }
                Vector3 end = flight.Route[flight.Route.Count - 1];
                Vector3 heading = end - flight.Route[flight.Route.Count - 2];
                heading.y = 0f;
                heading.Normalize();
                Vector3 away = end - player.position;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : heading;
                int bestConnector = -1;
                float bestScore = float.MinValue;
                for (int c = 0; c < connectors.Length; c++)
                {
                    TSLaneConnector connector = connectors[c];
                    if (connector == null || connector.nextLane < 0 || connector.nextLane >= roads.lanes.Length) continue;
                    TSPoints[] next = roads.lanes[connector.nextLane].points;
                    if (next == null || next.Length < 2) continue;
                    Vector3 exit = next[Mathf.Min(next.Length - 1, 3)].point - end;
                    exit.y = 0f;
                    exit.Normalize();
                    float straight = Vector3.Dot(exit, heading);
                    float score = flight.Calm ? straight : straight * 0.5f + Vector3.Dot(exit, away) + UnityEngine.Random.value * 0.3f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestConnector = c;
                    }
                }
                if (bestConnector < 0)
                {
                    flight.RouteEnded = true;
                    return;
                }
                TSLaneConnector chosen = connectors[bestConnector];
                if (chosen.points != null)
                {
                    for (int p = 0; p < chosen.points.Length; p++) AddPoint(flight, chosen.points[p].point);
                }
                flight.LastConnector = bestConnector;
                AppendLane(flight, chosen.nextLane, 0);
            }
            // Keep the list short: drop what is far behind.
            if (flight.RouteIndex > 32)
            {
                flight.Route.RemoveRange(0, flight.RouteIndex - 4);
                flight.RouteIndex = 4;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(TrafficPanicSettings newSettings, PlayerCombatController combatController, vShooterMeleeInput shooter, Transform playerTransform, PedestrianSpawner spawner, TSMainManager mainManager)
        {
            settings = newSettings;
            combat = combatController;
            shooterInput = shooter;
            player = playerTransform;
            pedestrians = spawner;
            roads = mainManager;
        }
#endif
    }
}
