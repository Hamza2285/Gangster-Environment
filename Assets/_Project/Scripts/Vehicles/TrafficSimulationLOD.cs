using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Simulation level of detail for the iTS traffic cars (one per scene, under ///Manager///, not a Singleton).
    /// - NEAR (within Near Physics Distance of the camera, or anything the player is dealing with): untouched iTS car - full
    ///   Rigidbody + WheelCollider physics and the AI every physics step.
    /// - FAR (farther away, or behind the camera beyond Behind Physics Distance): the Rigidbody is kinematic and the four
    ///   WheelColliders, the iTS wheel / anti-roll scripts, TSSimpleCar and VehicleCharacterImpact are switched off. The car is
    ///   moved by this manager along the iTS lane the AI is steering to (pure pursuit on the AI's steer waypoint), at a speed
    ///   integrated from the AI's own throttle / brake output, at the car's resting height above the lane. The AI keeps making
    ///   every traffic decision (lights, queues, junction reservations, lane changes); it only runs every Far AI Tick Interval
    ///   physics steps. Visible far cars still spin their wheels and show brake lights (cheap transform writes, no physics).
    /// - Switching back to NEAR hands the current speed to the Rigidbody and re-enables the wheels, so a car never stops or
    ///   jumps on the switch; hysteresis keeps cars from flipping at the boundary.
    /// iTS has a far/close range callback (TSTrafficAI.OnFarRange / OnCloserRange) but its physics switch in TSSimpleCar is
    /// commented out as experimental, and the callbacks are raised from the spawner's worker threads when multithreading is
    /// on, where Unity physics may not be touched. So the decision is made here, on the main thread, in batches.
    /// No iTS source file is changed. Distances come from the graphics profiles (<see cref="SetDistances"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // before TSTrafficAI (0): the AI tick switch is decided before the AI's FixedUpdate runs
    public class TrafficSimulationLOD : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Distances are measured from this camera. Empty = Camera.main.")]
        [SerializeField] private Camera targetCamera;

        [Header("Distances (set by the graphics profiles)")]
        [Tooltip("Cars closer than this (m) to the camera run full physics.")]
        [SerializeField, Min(10f)] private float nearPhysicsDistance = 80f;
        [Tooltip("Cars behind the camera only run full physics within this distance (m).")]
        [SerializeField, Min(10f)] private float behindPhysicsDistance = 45f;
        [Tooltip("A far car must come this much (m) closer than the physics distance before it switches back (no flip-flop).")]
        [SerializeField, Min(1f)] private float hysteresis = 10f;

        [Header("Budget")]
        [Tooltip("Cars whose LOD is re-evaluated per frame.")]
        [SerializeField, Min(1)] private int carsCheckedPerFrame = 24;
        [Tooltip("Far cars run their AI only every N physics steps (1 = every step).")]
        [SerializeField, Range(1, 6)] private int farAiTickInterval = 3;
        [Tooltip("Far cars that are not rendered (beyond the traffic view radius) are moved every N physics steps (one bigger step).")]
        [SerializeField, Range(1, 6)] private int hiddenMoveInterval = 3;

        [Header("Far Car Motion")]
        [Tooltip("Acceleration (m/s²) at full AI throttle.")]
        [SerializeField, Min(0.1f)] private float acceleration = 3.5f;
        [Tooltip("Deceleration (m/s²) at full AI brake.")]
        [SerializeField, Min(0.1f)] private float brakeDeceleration = 9f;
        [Tooltip("Deceleration (m/s²) with neither throttle nor brake (rolling resistance).")]
        [SerializeField, Min(0f)] private float coastDeceleration = 0.6f;
        [Tooltip("Fastest turn (degrees per second) of a far car.")]
        [SerializeField, Min(1f)] private float maxTurnRate = 90f;
        [Tooltip("Turn rate per m/s of speed (a slow car turns slowly, like a real one).")]
        [SerializeField, Min(0.1f)] private float turnRatePerSpeed = 14f;
        [Tooltip("How fast (per second) a far car settles to the lane height and slope.")]
        [SerializeField, Min(0.1f)] private float heightFollow = 6f;

        public enum Tier { Near, Far }

        /// <summary>Cars currently simulated with full physics / kinematically (for the perf capture and debugging).</summary>
        public int NearCount { get; private set; }
        public int FarCount { get; private set; }

        private sealed class Car
        {
            public TSTrafficAI Ai;
            public TSSimpleCar Simple;
            public TrafficVehicleAdapter Adapter;
            public VehicleCharacterImpact Impact;
            public Rigidbody Body;
            public Transform Transform;
            public WheelCollider[] Wheels;
            public TSSimpleCar_Wheel[] WheelScripts;
            public Transform[] WheelVisuals;
            public float[] WheelRadius;
            public bool[] WheelIsFront;
            public Renderer WheelRenderer;
            public TSAntiRollBar[] AntiRoll;
            public CollisionDetectionMode Detection;
            public float RestHeight;
            public float MaxSpeed;
            public Tier Tier;
            public bool Started;
            public int Index;

            public float Speed;
            public float Throttle;
            public float Brake;
            public float Steering;
            public float WheelAngle;
            public Func<Vector3> FarSpeed;
            public Func<Vector3> SavedSpeed;
            public TSTrafficAI.OnUpdateAIDelegate AiHandler;
        }

        private readonly List<Car> cars = new List<Car>();
        private readonly Dictionary<TSTrafficAI, Car> byAi = new Dictionary<TSTrafficAI, Car>();
        private int cursor;
        private int fixedStep;
        private Coroutine aiTickRoutine;
        private static readonly WaitForFixedUpdate AfterFixedStep = new WaitForFixedUpdate();

        /// <summary>Physics distances from the graphics profile.</summary>
        public void SetDistances(float nearDistance, float behindDistance)
        {
            nearPhysicsDistance = Mathf.Max(10f, nearDistance);
            behindPhysicsDistance = Mathf.Clamp(behindDistance, 10f, nearPhysicsDistance);
        }

        private void OnEnable()
        {
            TrafficVehicleAdapter.OnCarEnabled += HandleCarEnabled;
            TrafficVehicleAdapter.OnCarDisabled += HandleCarDisabled;
            IReadOnlyList<TrafficVehicleAdapter> active = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < active.Count; i++) HandleCarEnabled(active[i]);
            aiTickRoutine = StartCoroutine(FarAiTicks());
        }

        private void OnDisable()
        {
            TrafficVehicleAdapter.OnCarEnabled -= HandleCarEnabled;
            TrafficVehicleAdapter.OnCarDisabled -= HandleCarDisabled;
            if (aiTickRoutine != null) StopCoroutine(aiTickRoutine);
            aiTickRoutine = null;
            for (int i = 0; i < cars.Count; i++)
                if (cars[i].Ai != null && cars[i].Tier == Tier.Far) SetNear(cars[i]);
        }

        private void HandleCarEnabled(TrafficVehicleAdapter adapter)
        {
            if (adapter == null || !adapter.TryGetComponent(out TSTrafficAI ai)) return;
            if (!byAi.TryGetValue(ai, out Car car))
            {
                car = CreateCar(adapter, ai);
                if (car == null) return;
                car.Index = cars.Count;
                cars.Add(car);
                byAi[ai] = car;
            }
            // Every (re)spawn starts as a normal iTS car; the batched check decides once TSSimpleCar has started.
            car.Tier = Tier.Near;
            car.Speed = 0f;
        }

        // iTS disables pooled cars through TSTrafficAI.Disable (SetActive false) and later re-enables them with Enable(), which
        // writes velocities on the bodies: they have to be dynamic again before that.
        private void HandleCarDisabled(TrafficVehicleAdapter adapter)
        {
            if (adapter == null || !adapter.TryGetComponent(out TSTrafficAI ai)) return;
            if (byAi.TryGetValue(ai, out Car car) && car.Tier == Tier.Far) SetNear(car);
        }

        private Car CreateCar(TrafficVehicleAdapter adapter, TSTrafficAI ai)
        {
            Rigidbody body = ai.GetComponent<Rigidbody>();
            TSSimpleCar simple = ai.GetComponent<TSSimpleCar>();
            if (body == null || simple == null) return null;

            var car = new Car
            {
                Ai = ai,
                Simple = simple,
                Adapter = adapter,
                Impact = ai.GetComponent<VehicleCharacterImpact>(),
                Body = body,
                Transform = ai.transform,
                AntiRoll = ai.GetComponents<TSAntiRollBar>(),
                Detection = body.collisionDetectionMode,
                MaxSpeed = Mathf.Max(5f, simple.maxSpeed)
            };

            var wheels = new List<WheelCollider>();
            wheels.AddRange(simple.FrontWheels);
            wheels.AddRange(simple.BackWheels);
            car.Wheels = wheels.ToArray();
            car.WheelScripts = ai.GetComponentsInChildren<TSSimpleCar_Wheel>(true);
            car.WheelVisuals = new Transform[car.WheelScripts.Length];
            car.WheelRadius = new float[car.WheelScripts.Length];
            car.WheelIsFront = new bool[car.WheelScripts.Length];
            for (int i = 0; i < car.WheelScripts.Length; i++)
            {
                TSSimpleCar_Wheel w = car.WheelScripts[i];
                car.WheelVisuals[i] = w.transform;
                WheelCollider wc = w.CorrespondingCollider != null ? w.CorrespondingCollider : w.GetComponentInParent<WheelCollider>();
                car.WheelRadius[i] = wc != null ? Mathf.Max(0.1f, wc.radius * wc.transform.lossyScale.y) : 0.35f;
                car.WheelIsFront[i] = wc != null && Array.IndexOf(simple.FrontWheels, wc) >= 0;
                if (car.WheelRenderer == null) car.WheelRenderer = w.GetComponentInChildren<Renderer>(true);
            }
            car.RestHeight = ComputeRestHeight(car);

            // One delegate per car for its whole lifetime (no per-switch allocations).
            car.FarSpeed = () => new Vector3(0f, 0f, car.Speed);
            car.AiHandler = (steering, brake, throttle, upsideDown) =>
            {
                car.Steering = steering;
                car.Brake = brake;
                car.Throttle = throttle;
            };
            ai.OnUpdateAI += car.AiHandler;
            return car;
        }

        // Height of the car's pivot above the road when it stands on its wheels at the suspension's rest position.
        private static float ComputeRestHeight(Car car)
        {
            if (car.Wheels.Length == 0) return 0.5f;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < car.Wheels.Length; i++)
            {
                WheelCollider wc = car.Wheels[i];
                if (wc == null) continue;
                float scale = wc.transform.lossyScale.y;
                float drop = (wc.suspensionDistance * (1f - wc.suspensionSpring.targetPosition) + wc.radius) * scale;
                Vector3 contact = wc.transform.position - car.Transform.up * drop;
                sum += Vector3.Dot(car.Transform.position - contact, car.Transform.up);
                count++;
            }
            return count > 0 ? sum / count : 0.5f;
        }

        private void Update()
        {
            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                targetCamera = Camera.main;
                if (targetCamera == null) return;
            }
            int n = cars.Count;
            if (n == 0) return;

            Transform cam = targetCamera.transform;
            Vector3 camPos = cam.position;
            Vector3 camForward = cam.forward;
            int budget = Mathf.Min(carsCheckedPerFrame, n);
            for (int i = 0; i < budget; i++)
            {
                if (cursor >= n) cursor = 0;
                Car car = cars[cursor++];
                if (car.Ai == null || !car.Ai.gameObject.activeInHierarchy) continue;
                if (!car.Started)
                {
                    // TSSimpleCar.Start registers its speed callback on the AI; never switch before that happened.
                    car.Started = car.Ai.Initialized && car.Ai.UpdateCarSpeed != null;
                    if (!car.Started) continue;
                }
                Tier wanted = Evaluate(car, camPos, camForward);
                if (wanted == car.Tier) continue;
                if (wanted == Tier.Far) SetFar(car);
                else SetNear(car);
            }

            int near = 0;
            for (int i = 0; i < n; i++)
                if (cars[i].Tier == Tier.Near && cars[i].Ai != null && cars[i].Ai.gameObject.activeInHierarchy) near++;
            NearCount = near;
            FarCount = CountFar();
        }

        private int CountFar()
        {
            int far = 0;
            for (int i = 0; i < cars.Count; i++)
                if (cars[i].Tier == Tier.Far) far++;
            return far;
        }

        private Tier Evaluate(Car car, Vector3 camPos, Vector3 camForward)
        {
            // Anything the player is interacting with (carjack, killed driver, crash) stays a normal physics car.
            if (car.Adapter != null && car.Adapter.State != TrafficVehicleState.TrafficControlled) return Tier.Near;
            if (car.Ai.crashed || car.Ai.reservedForEventTrigger || !car.Ai.IsEnabled) return Tier.Near;

            Vector3 offset = car.Transform.position - camPos;
            bool behind = Vector3.Dot(offset, camForward) < 0f;
            float limit = behind ? behindPhysicsDistance : nearPhysicsDistance;
            if (car.Tier == Tier.Far) limit -= hysteresis;
            return offset.sqrMagnitude > limit * limit ? Tier.Far : Tier.Near;
        }

        private void SetFar(Car car)
        {
            car.Tier = Tier.Far;
            Vector3 velocity = car.Body.linearVelocity;
            car.Speed = Mathf.Max(0f, Vector3.Dot(velocity, car.Transform.forward));

            car.SavedSpeed = car.Ai.UpdateCarSpeed;
            car.Ai.UpdateCarSpeed = car.FarSpeed;
            car.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            car.Body.isKinematic = true;
            SetCarScripts(car, false);
        }

        private void SetNear(Car car)
        {
            car.Tier = Tier.Near;
            if (car.Ai != null)
            {
                if (car.SavedSpeed != null) car.Ai.UpdateCarSpeed = car.SavedSpeed;
                car.Ai.enabled = true;
            }
            if (car.Body != null)
            {
                car.Body.isKinematic = false;
                car.Body.collisionDetectionMode = car.Detection;
                if (car.Body.gameObject.activeInHierarchy)
                {
                    car.Body.linearVelocity = car.Transform.forward * car.Speed;
                    car.Body.angularVelocity = Vector3.zero;
                }
            }
            SetCarScripts(car, true);
        }

        private static void SetCarScripts(Car car, bool physics)
        {
            for (int i = 0; i < car.Wheels.Length; i++)
                if (car.Wheels[i] != null) car.Wheels[i].enabled = physics;
            for (int i = 0; i < car.WheelScripts.Length; i++)
                if (car.WheelScripts[i] != null) car.WheelScripts[i].enabled = physics;
            for (int i = 0; i < car.AntiRoll.Length; i++)
                if (car.AntiRoll[i] != null) car.AntiRoll[i].enabled = physics;
            if (car.Simple != null) car.Simple.enabled = physics;
            if (car.Impact != null) car.Impact.enabled = physics;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < cars.Count; i++)
            {
                Car car = cars[i];
                if (car.Tier != Tier.Far || car.Ai == null || !car.Ai.gameObject.activeInHierarchy) continue;
                // Nobody sees a hidden car move: one bigger step every few physics steps is enough.
                bool hidden = car.WheelRenderer != null && car.WheelRenderer.forceRenderingOff;
                if (!hidden || hiddenMoveInterval <= 1) MoveFarCar(car, dt);
                else if ((fixedStep + car.Index) % hiddenMoveInterval == 0) MoveFarCar(car, dt * hiddenMoveInterval);
            }
        }

        // After each physics step: far cars whose turn is the next step get their AI enabled, the others disabled. Behaviours
        // enabled during a FixedUpdate only run from the following step, so the switch is made between steps. Each car has its
        // own phase, so the AI work is spread evenly over the steps.
        private IEnumerator FarAiTicks()
        {
            while (true)
            {
                yield return AfterFixedStep;
                fixedStep++;
                for (int i = 0; i < cars.Count; i++)
                {
                    Car car = cars[i];
                    if (car.Ai == null) continue;
                    bool run = car.Tier == Tier.Near || farAiTickInterval <= 1 || (fixedStep + car.Index) % farAiTickInterval == 0;
                    if (car.Ai.enabled != run) car.Ai.enabled = run;
                }
            }
        }

        private void MoveFarCar(Car car, float dt)
        {
            // Speed from the AI's own pedals (it brakes for lights, queues and junctions exactly as it would with physics).
            float speed = car.Speed;
            if (car.Brake > 0.01f) speed -= car.Brake * brakeDeceleration * dt;
            else if (car.Throttle > 0.01f) speed += car.Throttle * acceleration * dt;
            else speed -= coastDeceleration * dt;
            speed = Mathf.Clamp(speed, 0f, car.MaxSpeed);
            car.Speed = speed;

            TSPoints target = car.Ai.CurrentSteerWaypoint;
            if (target == null) return;
            Vector3 position = car.Body.position;
            Vector3 toTarget = target.point - position;
            toTarget.y = 0f;

            Vector3 forward = car.Transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            if (toTarget.sqrMagnitude > 0.25f && speed > 0.05f)
            {
                float maxDegrees = Mathf.Min(maxTurnRate, speed * turnRatePerSpeed) * dt;
                forward = Vector3.RotateTowards(forward, toTarget.normalized, maxDegrees * Mathf.Deg2Rad, 0f);
            }
            position += forward * (speed * dt);

            // Height and slope from the lane segment the car is on (previous -> current steer waypoint).
            TSPoints previous = car.Ai.PreviousSteerWaypoint;
            float laneY = target.point.y;
            float slope = 0f;
            if (previous != null)
            {
                Vector3 segment = target.point - previous.point;
                float flatLength = new Vector2(segment.x, segment.z).magnitude;
                if (flatLength > 0.01f)
                {
                    Vector3 fromPrev = position - previous.point;
                    float t = Mathf.Clamp01(Vector3.Dot(new Vector3(fromPrev.x, 0f, fromPrev.z), new Vector3(segment.x, 0f, segment.z)) / (flatLength * flatLength));
                    laneY = previous.point.y + segment.y * t;
                    slope = segment.y / flatLength;
                }
            }
            float follow = 1f - Mathf.Exp(-heightFollow * dt);
            position.y = Mathf.Lerp(position.y, laneY + car.RestHeight, follow);
            Vector3 look = new Vector3(forward.x, Mathf.Clamp(slope, -0.4f, 0.4f), forward.z);
            Quaternion rotation = Quaternion.Slerp(car.Body.rotation, Quaternion.LookRotation(look, Vector3.up), follow);

            car.Body.MovePosition(position);
            car.Body.MoveRotation(rotation);
        }

        // Wheel spin and brake lights for far cars that are on screen (transform writes only).
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < cars.Count; i++)
            {
                Car car = cars[i];
                if (car.Tier != Tier.Far || car.WheelRenderer == null || car.WheelRenderer.forceRenderingOff || !car.WheelRenderer.isVisible) continue;
                car.WheelAngle = Mathf.Repeat(car.WheelAngle + car.Speed * dt * Mathf.Rad2Deg / car.WheelRadius[0], 360f);
                float steer = car.Simple != null ? car.Simple.maxSteering * Mathf.Clamp(car.Steering, -1f, 1f) : 0f;
                for (int w = 0; w < car.WheelVisuals.Length; w++)
                {
                    Transform visual = car.WheelVisuals[w];
                    if (visual == null) continue;
                    visual.rotation = visual.parent.rotation * Quaternion.Euler(car.WheelAngle, car.WheelIsFront[w] ? steer : 0f, 0f);
                }
                if (car.Simple != null) car.Simple.BrakeLight(car.Brake);
            }
        }
    }
}
