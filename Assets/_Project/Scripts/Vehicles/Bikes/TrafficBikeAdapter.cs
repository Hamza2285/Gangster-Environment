using System;
using System.Collections;
using System.Collections.Generic;
using Invector;
using OpenworldCrime.Pedestrians;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Player-facing side of an iTS traffic motorbike (Hamza's BikePrefabBuilder bikes, through prefab variants under
    /// _Project/Prefabs/Vehicles/TrafficBikes). Per-instance, never a Singleton. It sits next to Hamza's <see cref="BikeCrash"/>
    /// and <see cref="BikeRig"/> without changing them:
    /// - Stealing (<see cref="IVehicleInteractionTarget"/>): the bike brakes to a stop for the player, then the drivable twin
    ///   (<see cref="RCCVehicleAdapter"/> with quick mount) takes its place at the same pose and paint, the rider is shoved off as
    ///   the real pedestrian (he falls, gets up and runs) and the traffic bike goes back to the iTS pool.
    /// - Shot rider (<see cref="SeatedDriverHealth"/> on the rider, trigger hit zones on his head and chest bones): he dies and
    ///   comes off as a ragdoll while the bike falls over and slides on (<see cref="BikeCrash.Crash"/>).
    /// - Collisions are judged here by <see cref="BikeCrashSettings"/> (BikeCrash's own trigger, which unseats the rider at any
    ///   touch, is switched off on this instance): a nudge is a wobble, a medium hit a stumble and a stop, a hard or badly placed
    ///   hit throws the rider off with the speed he had.
    /// Crimes are raised through <see cref="OnRiderShot"/> / <see cref="OnRiderKnockedOff"/> (CrimeReporter listens).
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficBikeAdapter : MonoBehaviour, IVehicleInteractionTarget, IVehicleEntrySide
    {
        [Header("Traffic (iTS)")]
        [SerializeField] private TSTrafficAI trafficAI;
        [SerializeField] private TSSimpleCar simpleCar;
        [SerializeField] private Rigidbody body;
        [Tooltip("Hamza's crash (wreck + rider ragdoll). Its automatic trigger is switched off; this adapter decides when it happens.")]
        [SerializeField] private BikeCrash bikeCrash;
        [SerializeField] private BikeCrashSettings crashSettings;

        [Header("Rider")]
        [Tooltip("The posed rider (Visual/Rider).")]
        [SerializeField] private GameObject riderVisual;
        [Tooltip("Health and hit zones of the rider (on the rider, zones on his head and chest bones).")]
        [SerializeField] private SeatedDriverHealth riderHealth;
        [Tooltip("The street pedestrian this rider is (spawned when he is shoved off the bike). Empty = he just disappears.")]
        [SerializeField] private GameObject riderPedPrefab;
        [Tooltip("Shoved off (stolen bike): speed (m/s) he is pushed away with, and how long he stays down.")]
        [SerializeField, Min(0f)] private float shoveSpeed = 2.2f;
        [SerializeField, Min(0f)] private float shoveGroundTime = 2.5f;
        [Tooltip("Layer for BikeCrash's solid crash box: one that vehicles collide with but bullets do not hit, so shots at the rider " +
                 "reach his hit zones instead of stopping on the box around his hips and chest.")]
        [SerializeField] private string crashBodyLayer = "Vehicle";
        [Tooltip("Logs every judged hit (impact speed, angle, verdict) for tuning.")]
        [SerializeField] private bool logHits;

        [Header("Knocked Off By A Car")]
        [Tooltip("A car (any moving vehicle) hitting the bike at least this hard (km/h) knocks the rider off, even when the hit is " +
                 "too soft for Bike Crash Settings to throw him: the bike wobbles, then he topples off and it falls over.")]
        [SerializeField, Min(0f)] private float carKnockOffKmh = 8f;
        [Tooltip("Seconds the bike wobbles before the rider goes down.")]
        [SerializeField, Min(0f)] private float carKnockOffDelay = 0.4f;
        [Tooltip("How hard (m/s) the soft fall throws rider and bike: low = he slides off and the bike tips over, no flying.")]
        [SerializeField, Min(0.5f)] private float softFallImpact = 2f;
        [Tooltip("Fastest (m/s) the rider and bike carry on along the road in a soft fall.")]
        [SerializeField, Min(0f)] private float softFallMaxSpeed = 4f;

        [Header("Takeover")]
        [Tooltip("Drivable twin of this bike (same model), placed at the bike's ground point when the player gets on.")]
        [SerializeField] private RCCVehicleAdapter drivablePrefab;
        [Tooltip("The bike's ground point (Visual) - the drivable twin's root goes here.")]
        [SerializeField] private Transform drivableAnchor;
        [Tooltip("Bike body renderer: its paint is copied onto the drivable twin.")]
        [SerializeField] private Renderer paintRenderer;
        [SerializeField] private Transform interactionPoint;
        [Tooltip("Where the player stands to get on (left side, facing the bike).")]
        [SerializeField] private Transform enterPoint;
        [Tooltip("Enter point on the right side (the player gets on from whichever side he stands).")]
        [SerializeField] private Transform alternateEnterPoint;
        [Tooltip("Max speed (m/s) at which the prompt is offered; the bike brakes to a stop once the player commits.")]
        [SerializeField, Min(0f)] private float maxTakeoverSpeed = 8f;
        [SerializeField, Min(0f)] private float stoppedSpeed = 0.8f;

        /// <summary>Raised when a traffic bike was replaced by its drivable twin (before the mount starts).</summary>
        public static event Action<TrafficBikeAdapter, RCCVehicleAdapter> OnTakenOver;
        /// <summary>Raised when the rider was shot. Args: the bike, who shot (may be null), killed.</summary>
        public static event Action<TrafficBikeAdapter, Transform, bool> OnRiderShot;
        /// <summary>Raised when the rider came off the bike in a crash or a theft. Args: the bike, who did it (vehicle or person, may be null).</summary>
        public static event Action<TrafficBikeAdapter, Transform> OnRiderKnockedOff;

        private enum BikeState { Traffic, Reserved, Gone }

        private readonly List<Collider> solidColliders = new List<Collider>();
        private BikeState state;
        private bool valid;
        private float baseBrakeTorque;
        private float nextHitTime;
        private Vector3 velocityBefore;
        private Coroutine stumbleRoutine;
        private Coroutine knockOffRoutine;
        private Transform lastShooter;
        private Transform usedEnterPoint;

        public bool HasRider => riderVisual != null && riderVisual.activeInHierarchy;
        public bool CanEnter => valid && isActiveAndEnabled && state == BikeState.Traffic && !bikeCrash.Crashed && trafficAI.IsEnabled && body.linearVelocity.magnitude <= maxTakeoverSpeed;
        public bool IsWrecked => bikeCrash != null && bikeCrash.Crashed;
        public bool IsReadyForEntry => body.linearVelocity.magnitude <= stoppedSpeed;
        public VehicleEntryPrompt EntryPrompt => HasRider ? VehicleEntryPrompt.Steal : VehicleEntryPrompt.Enter;
        public Transform GetInteractionPoint() => interactionPoint != null ? interactionPoint : transform;
        public Transform GetEnterPoint() => enterPoint;

        public Transform GetEnterPoint(Vector3 from)
        {
            if (alternateEnterPoint == null) return enterPoint;
            // Remembered: the rider is shoved off to the other side.
            usedEnterPoint = (alternateEnterPoint.position - from).sqrMagnitude < (enterPoint.position - from).sqrMagnitude ? alternateEnterPoint : enterPoint;
            return usedEnterPoint;
        }
        // No door to pull him through: the player walks to the seat side and shoves him off as he gets on.
        public Transform GetStealPoint() => null;
        public IReadOnlyList<Collider> GetColliders() => solidColliders;

        private void Awake()
        {
            valid = trafficAI != null && simpleCar != null && body != null && bikeCrash != null && crashSettings != null
                    && drivablePrefab != null && drivablePrefab.HasEntrySetup && drivableAnchor != null && enterPoint != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(TrafficBikeAdapter)}] Missing references on '{name}' (TSTrafficAI, TSSimpleCar, Rigidbody, BikeCrash, " +
                               "crash settings, drivable prefab with entry setup, drivable anchor, enter point). This bike cannot be stolen.", this);
                return;
            }
            // Hamza's crash fires on any touch of the player's car from 1.5 m/s; here the crash settings decide (instance value only).
            bikeCrash.minImpactSpeed = float.MaxValue;
            baseBrakeTorque = simpleCar.brakeTorque;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) solidColliders.Add(c);
        }

        // BikeCrash builds its solid "CrashBody" box in its Awake (Default layer), reaching up over the rider's hips and chest, so
        // bullets aimed at his body stopped on it. Moved here to a layer cars still hit but bullets do not (this instance only).
        private void Start()
        {
            Transform crashBody = transform.Find("CrashBody");
            int layer = LayerMask.NameToLayer(crashBodyLayer);
            if (crashBody != null && layer >= 0) crashBody.gameObject.layer = layer;
        }

        // iTS re-enables pooled bikes, so every reuse starts clean.
        private void OnEnable()
        {
            state = BikeState.Traffic;
            nextHitTime = 0f;
            stumbleRoutine = null;
            lastShooter = null;
            usedEnterPoint = null;
            if (riderVisual != null) riderVisual.SetActive(true);
            if (riderHealth != null)
            {
                riderHealth.ResetHealth();
                riderHealth.OnKilled += HandleRiderKilled;
                riderHealth.OnWounded += HandleRiderWounded;
            }
            if (valid) simpleCar.brakeTorque = baseBrakeTorque;
        }

        private void OnDisable()
        {
            if (knockOffRoutine != null) StopCoroutine(knockOffRoutine);
            knockOffRoutine = null;
            if (riderHealth != null)
            {
                riderHealth.OnKilled -= HandleRiderKilled;
                riderHealth.OnWounded -= HandleRiderWounded;
            }
            if (state == BikeState.Reserved && trafficAI != null) trafficAI.reservedForEventTrigger = false;
        }

        private void FixedUpdate()
        {
            velocityBefore = body != null ? body.linearVelocity : Vector3.zero;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Stealing
        // ---------------------------------------------------------------------------------------------------------------

        public void ReserveForEntry()
        {
            if (!valid || state != BikeState.Traffic) return;
            state = BikeState.Reserved;
            trafficAI.reservedForEventTrigger = true;
            Hold();
        }

        public void CancelEntryReservation()
        {
            if (state != BikeState.Reserved) return;
            state = BikeState.Traffic;
            Release();
        }

        public IVehicleController CompleteEntry()
        {
            if (!valid || state != BikeState.Reserved || !isActiveAndEnabled || bikeCrash.Crashed)
            {
                CancelEntryReservation();
                return null;
            }

            RCCVehicleAdapter drivable = TrafficVehicleAdapter.DrivableProvider != null ? TrafficVehicleAdapter.DrivableProvider(drivablePrefab) : null;
            if (drivable == null)
            {
                drivable = Instantiate(drivablePrefab);
                drivable.name = drivablePrefab.name;
            }
            Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);
            drivable.transform.SetPositionAndRotation(drivableAnchor.position, rotation);
            if (drivable.TryGetComponent(out Rigidbody drivableBody))
            {
                drivableBody.position = drivableAnchor.position;
                drivableBody.rotation = rotation;
                drivableBody.linearVelocity = Vector3.zero;
                drivableBody.angularVelocity = Vector3.zero;
            }
            CopyPaint(drivable);

            if (HasRider) ShoveRiderOff(drivable.transform);
            OnTakenOver?.Invoke(this, drivable);
            ReturnToTraffic();
            return drivable;
        }

        private void CopyPaint(RCCVehicleAdapter drivable)
        {
            if (paintRenderer == null) return;
            Material paint = paintRenderer.sharedMaterial;
            Transform model = drivable.transform.Find("Visual/Model");
            if (model == null || paint == null) return;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = paint;
        }

        // The real pedestrian takes the rider's place and falls off on the far side from the player, then gets up and runs.
        private void ShoveRiderOff(Transform drivable)
        {
            Transform rider = riderVisual.transform;
            Transform side = usedEnterPoint != null ? usedEnterPoint : enterPoint;
            Vector3 away = Vector3.ProjectOnPlane(drivable.position - side.position, Vector3.up);
            away = away.sqrMagnitude > 0.01f ? away.normalized : transform.right;
            riderVisual.SetActive(false);
            OnRiderKnockedOff?.Invoke(this, null);
            if (riderPedPrefab == null || TrafficVehicleDriver.PersonFactory == null) return;
            Vector3 point = rider.position + away * 0.35f;
            if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 4f, 1, QueryTriggerInteraction.Ignore)) point = ground.point;
            PedestrianWalker person = TrafficVehicleDriver.PersonFactory(riderPedPrefab, point, Quaternion.LookRotation(transform.forward));
            if (person != null) person.StartCoroutine(KnockDownNextFrame(person, away * shoveSpeed + Vector3.up * 0.8f, Vector3.Cross(Vector3.up, away) * 2f, drivable.position));
        }

        private IEnumerator KnockDownNextFrame(PedestrianWalker person, Vector3 velocity, Vector3 tumble, Vector3 threat)
        {
            // One frame for the new character to initialise; a knock-down before that does not count.
            yield return null;
            if (person != null && person.TryGetComponent(out PedestrianDamage damage)) damage.KnockDownBy(velocity, tumble, shoveGroundTime, threat);
        }

        // iTS' pause alone does not hold the bike: with the AI off, the physics keep braking with the last input.
        private void Hold()
        {
            trafficAI.Pause();
            trafficAI.enabled = false;
            simpleCar.brakeTorque = baseBrakeTorque;
            simpleCar.OnAIUpdate(0f, 1f, 0f, false);
        }

        private void Release()
        {
            trafficAI.reservedForEventTrigger = false;
            trafficAI.enabled = true;
            if (isActiveAndEnabled) trafficAI.UnPause();
        }

        private void ReturnToTraffic()
        {
            state = BikeState.Gone;
            trafficAI.reservedForEventTrigger = false;
            trafficAI.enabled = true;
            trafficAI.Disable(true, true);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Shot rider
        // ---------------------------------------------------------------------------------------------------------------

        private void HandleRiderWounded(SeatedDriverHealth health, vDamage damage)
        {
            OnRiderShot?.Invoke(this, damage != null ? damage.sender : null, false);
        }

        private void HandleRiderKilled(SeatedDriverHealth health, vDamage damage)
        {
            lastShooter = damage != null ? damage.sender : null;
            if (!isActiveAndEnabled || bikeCrash.Crashed || state == BikeState.Gone) return;
            // The bike carries on under its own momentum and goes over; the body comes off the way it was riding, pushed by the shot.
            Vector3 velocity = body.linearVelocity;
            Vector3 travel = velocity + health.LastHitDirection * 1.5f;
            float impact = Mathf.Max(velocity.magnitude * 0.9f, 1.5f);
            Vector3 point = damage != null ? damage.hitPosition : riderVisual.transform.position;
            if (state == BikeState.Reserved) state = BikeState.Traffic;
            trafficAI.reservedForEventTrigger = false;
            trafficAI.enabled = true;
            bikeCrash.Crash(null, travel, impact, point);
            state = BikeState.Gone;
            FallenBikeTarget.AttachToNewestWreck(transform.position, drivablePrefab, paintRenderer != null ? paintRenderer.sharedMaterial : null);
            OnRiderShot?.Invoke(this, lastShooter, true);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Collisions
        // ---------------------------------------------------------------------------------------------------------------

        private void OnCollisionEnter(Collision collision)
        {
            if (!valid || state == BikeState.Gone || bikeCrash.Crashed || !HasRider || Time.time < nextHitTime || collision.contactCount == 0) return;
            if (!BikeImpact.Measure(transform, velocityBefore, collision, crashSettings, out BikeImpact.Result hit)) return;
            BikeCrashSettings.Outcome outcome = crashSettings.Judge(hit.ImpactKmh, hit.AngleDegrees);
            // A car that hits the bike knocks the rider off however soft the hit (a hard one throws him, below).
            Rigidbody hitter = collision.rigidbody;
            bool byVehicle = hitter != null && !hitter.isKinematic && hitter.mass >= crashSettings.minOtherMass;
            if (byVehicle && outcome != BikeCrashSettings.Outcome.Thrown && hit.ImpactKmh >= carKnockOffKmh)
            {
                if (knockOffRoutine != null) return;
                nextHitTime = Time.time + crashSettings.hitCooldown;
                if (logHits) Debug.Log($"[{nameof(TrafficBikeAdapter)}] '{name}' knocked by '{hitter.name}': {hit.ImpactKmh:F0} km/h -> soft fall", this);
                BikeImpact.Wobble(body, transform, hit.Normal, crashSettings.stumbleRollSpeed);
                if (isActiveAndEnabled) knockOffRoutine = StartCoroutine(SoftFallRoutine(hitter, hit));
                return;
            }
            if (outcome == BikeCrashSettings.Outcome.None) return;
            nextHitTime = Time.time + crashSettings.hitCooldown;
            if (logHits) Debug.Log($"[{nameof(TrafficBikeAdapter)}] '{name}' hit '{collision.collider.name}': {hit.ImpactKmh:F0} km/h at {hit.AngleDegrees:F0} deg -> {outcome}", this);

            if (outcome == BikeCrashSettings.Outcome.Thrown)
            {
                Rigidbody other = collision.rigidbody;
                // Pushed by a vehicle: along its travel. Ridden into something: on the way the bike was going.
                Vector3 travel = other != null && !other.isKinematic && hit.OtherVelocity.sqrMagnitude > velocityBefore.sqrMagnitude
                    ? hit.OtherVelocity * crashSettings.pushShare + velocityBefore * crashSettings.carriedVelocity
                    : velocityBefore * crashSettings.carriedVelocity + hit.OtherVelocity * crashSettings.pushShare;
                if (state == BikeState.Reserved) state = BikeState.Traffic;
                trafficAI.reservedForEventTrigger = false;
                bikeCrash.Crash(other, travel, hit.ImpactKmh / 3.6f, hit.Point);
                state = BikeState.Gone;
                FallenBikeTarget.AttachToNewestWreck(transform.position, drivablePrefab, paintRenderer != null ? paintRenderer.sharedMaterial : null);
                OnRiderKnockedOff?.Invoke(this, other != null ? other.transform : collision.transform);
                return;
            }

            float kick = outcome == BikeCrashSettings.Outcome.Stumble ? crashSettings.stumbleRollSpeed : crashSettings.wobbleRollSpeed * Mathf.Clamp01(hit.ImpactKmh / crashSettings.stayOnBelowKmh);
            BikeImpact.Wobble(body, transform, hit.Normal, kick);
            if (outcome == BikeCrashSettings.Outcome.Stumble && state == BikeState.Traffic && isActiveAndEnabled)
            {
                if (stumbleRoutine != null) StopCoroutine(stumbleRoutine);
                stumbleRoutine = StartCoroutine(StumbleStopRoutine());
            }
        }

        // Hit by a car softly: the bike wobbles for a moment, then the rider slides off and the bike tips over (Hamza's crash with a
        // small impact, so neither flies).
        private IEnumerator SoftFallRoutine(Rigidbody hitter, BikeImpact.Result hit)
        {
            trafficAI.Pause();
            if (carKnockOffDelay > 0f) yield return new WaitForSeconds(carKnockOffDelay);
            knockOffRoutine = null;
            if (!valid || state == BikeState.Gone || bikeCrash.Crashed || !HasRider) yield break;
            Vector3 travel = Vector3.ClampMagnitude(body.linearVelocity * 0.6f + hit.OtherVelocity * 0.25f, softFallMaxSpeed);
            if (state == BikeState.Reserved) state = BikeState.Traffic;
            trafficAI.reservedForEventTrigger = false;
            bikeCrash.Crash(hitter, travel, softFallImpact, hit.Point);
            state = BikeState.Gone;
            FallenBikeTarget.AttachToNewestWreck(transform.position, drivablePrefab, paintRenderer != null ? paintRenderer.sharedMaterial : null);
            OnRiderKnockedOff?.Invoke(this, hitter != null ? hitter.transform : null);
        }

        // A short stop to get his balance back, then the traffic AI rides on (any other sequence takes precedence).
        private IEnumerator StumbleStopRoutine()
        {
            trafficAI.Pause();
            Vector2 range = crashSettings.trafficStumbleStopSeconds;
            yield return new WaitForSeconds(UnityEngine.Random.Range(range.x, Mathf.Max(range.x, range.y)));
            stumbleRoutine = null;
            if (state == BikeState.Traffic && trafficAI.enabled && !bikeCrash.Crashed) trafficAI.UnPause();
        }

        /// <summary>Raises <see cref="OnTakenOver"/> for a bike taken over without an adapter (a fallen wreck picked up).</summary>
        public static void RaiseTakenOver(TrafficBikeAdapter source, RCCVehicleAdapter drivable) => OnTakenOver?.Invoke(source, drivable);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnTakenOver = null;
            OnRiderShot = null;
            OnRiderKnockedOff = null;
        }
    }
}
