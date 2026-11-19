using UnityEngine;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// Drives a race opponent's RCC car along the race route: steers at a point ahead on the route, keeps to the route's
    /// advised corner speeds (scaled by its skill and the manager's light rubber-banding), backs out when stuck and is put
    /// back on the route when flipped or lost. Added at runtime by <see cref="RaceManager"/> to the cars it spawns.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceOpponentDriver : MonoBehaviour
    {
        private RCC_CarControllerV3 car;
        private Rigidbody body;
        private Vector3[] route;
        private float[] speeds;
        private float[] distances;
        private float lapLength;
        private bool circuit;

        private int index;
        private int lap;
        private float skill = 1f;
        private float speedScale = 1f;
        private bool driving;

        private float stuckTimer;
        private float reverseTimer;
        private int reverseCount;
        private float lostTimer;

        /// <summary>Distance driven along the route (laps included), for race positions.</summary>
        public float Progress => lap * lapLength + (distances != null && index < distances.Length ? distances[index] : 0f);
        public int Lap => lap;
        public bool Finished { get; set; }
        public float FinishTime { get; set; }
        public string DriverName { get; private set; }
        public Rigidbody Body => body;

        public void Setup(RCC_CarControllerV3 controller, RaceDefinition race, float[] cumulative, float driverSkill, string driverName)
        {
            car = controller;
            body = controller.GetComponent<Rigidbody>();
            route = race.RoutePoints;
            speeds = race.RouteSpeeds;
            distances = cumulative;
            lapLength = race.LapLength;
            circuit = race.IsCircuit;
            skill = driverSkill;
            DriverName = driverName;
            index = 0;
            lap = 0;
            car.externalController = true;
            car.SetCanControl(false);
            car.StartEngine(true);
        }

        /// <summary>Hold on the grid (handbrake) or race.</summary>
        public void SetDriving(bool drive)
        {
            driving = drive;
            if (car == null) return;
            car.SetCanControl(drive);
            if (!drive) SetInputs(0f, 0f, 0f, 1f);
        }

        /// <summary>Rubber-banding from the manager: above 1 to catch up, below 1 to wait.</summary>
        public void SetSpeedScale(float scale) => speedScale = scale;

        private void SetInputs(float throttle, float brake, float steer, float handbrake)
        {
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.steerInput = steer;
            car.handbrakeInput = handbrake;
        }

        private void FixedUpdate()
        {
            if (car == null || route == null || route.Length < 2) return;
            if (!driving || Finished)
            {
                SetInputs(0f, Finished ? 0.6f : 0f, 0f, driving ? 0f : 1f);
                return;
            }

            AdvanceIndex();
            float speed = body.linearVelocity.magnitude;
            float dt = Time.fixedDeltaTime;

            // Lost or upside down: back on the route like GTA's catch-up.
            if (Vector3.Dot(transform.up, Vector3.up) < 0.3f || (route[index] - transform.position).sqrMagnitude > 30f * 30f) lostTimer += dt;
            else lostTimer = 0f;
            if (lostTimer > 2.5f) { Reset(index); return; }

            // Steering at a point ahead (further at speed).
            float look = 7f + speed * 0.55f;
            Vector3 target = PointAhead(look);
            float avoidCap = AvoidTraffic(speed, dt);
            target += transform.right * avoidOffset;
            Vector3 local = transform.InverseTransformPoint(target);
            float steer = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * Mathf.Rad2Deg / 32f, -1f, 1f);

            // Speed: the route's advised speed (it already ramps down before corners) a short way ahead.
            float advised = Mathf.Min(MinSpeedAhead(4f + speed * 0.35f) * skill * speedScale, avoidCap);
            float throttle, brake;
            if (speed < advised - 1f) { throttle = 1f; brake = 0f; }
            else if (speed > advised + 2f) { throttle = 0f; brake = Mathf.Clamp01((speed - advised) / 6f); }
            else { throttle = 0.6f; brake = 0f; }

            // Stuck against something: reverse out with opposite lock.
            if (reverseTimer > 0f)
            {
                reverseTimer -= dt;
                // RCC's auto reverse: brake input drives backwards once stopped.
                SetInputs(0f, 1f, -steer, 0f);
                return;
            }
            if (throttle > 0.5f && speed < 1.5f) stuckTimer += dt; else stuckTimer = 0f;
            if (stuckTimer > 1.8f)
            {
                stuckTimer = 0f;
                reverseCount++;
                if (reverseCount > 2) { Reset(index); return; }
                reverseTimer = 1.3f;
                return;
            }
            if (speed > 6f) reverseCount = 0;

            SetInputs(throttle, brake, steer, 0f);
        }

        private float avoidOffset;

        // A slower car in the way: pass it on whichever side is free, or follow it when neither is. Returns a speed cap.
        private float AvoidTraffic(float speed, float dt)
        {
            float scan = 6f + speed * 1.1f;
            float cap = float.MaxValue;
            if (Probe(Vector3.zero, scan, out Rigidbody other, out float distance) && other != null)
            {
                float otherSpeed = Vector3.Dot(other.linearVelocity, transform.forward);
                if (otherSpeed < speed - 1.5f || distance < 8f)
                {
                    float side = 0f;
                    if (avoidOffset >= 0f && !Probe(transform.right * 3.2f, scan, out _, out _)) side = 1f;
                    else if (!Probe(-transform.right * 3.2f, scan, out _, out _)) side = -1f;
                    else if (!Probe(transform.right * 3.2f, scan, out _, out _)) side = 1f;
                    if (side != 0f) avoidOffset = Mathf.MoveTowards(avoidOffset, side * 3.2f, dt * 8f);
                    else cap = Mathf.Max(0f, otherSpeed) + Mathf.Max(0f, distance - 8f) * 0.4f;
                    return cap;
                }
            }
            avoidOffset = Mathf.MoveTowards(avoidOffset, 0f, dt * 2.5f);
            return cap;
        }

        // Sphere cast ahead from in front of the bumper (so it never starts inside this car). True when something solid is hit.
        private bool Probe(Vector3 offset, float distance, out Rigidbody hitBody, out float hitDistance)
        {
            hitBody = null;
            hitDistance = 0f;
            Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 3f + offset;
            if (!Physics.SphereCast(origin, 1.1f, transform.forward, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.rigidbody == body) return false;
            // Low static bumps (kerbs, ramps) are not obstacles; walls and cars are.
            if (hit.rigidbody == null && hit.normal.y > 0.6f) return false;
            hitBody = hit.rigidbody;
            hitDistance = hit.distance;
            return true;
        }

        // Nearest route point, searched a little ahead of the current one (never backwards past the start of the lap).
        private void AdvanceIndex()
        {
            Vector3 p = transform.position;
            int best = index;
            float bestD = (route[index] - p).sqrMagnitude;
            for (int k = 1; k <= 12; k++)
            {
                int i = index + k;
                if (i >= route.Length)
                {
                    if (!circuit) break;
                    i -= route.Length;
                }
                float d = (route[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = index + k; }
            }
            if (best >= route.Length)
            {
                best -= route.Length;
                lap++;
            }
            index = best;
        }

        private Vector3 PointAhead(float metres)
        {
            int i = index;
            float d = 0f;
            while (d < metres)
            {
                int n = i + 1;
                if (n >= route.Length)
                {
                    if (!circuit) return route[route.Length - 1];
                    n = 0;
                }
                d += Vector3.Distance(route[i], route[n]);
                i = n;
                if (i == index) break;
            }
            return route[i];
        }

        private float MinSpeedAhead(float metres)
        {
            float min = speeds.Length > index ? speeds[index] : 30f;
            int i = index;
            float d = 0f;
            while (d < metres)
            {
                int n = i + 1;
                if (n >= route.Length)
                {
                    if (!circuit) break;
                    n = 0;
                }
                d += Vector3.Distance(route[i], route[n]);
                i = n;
                if (i < speeds.Length) min = Mathf.Min(min, speeds[i]);
                if (i == index) break;
            }
            return min;
        }

        /// <summary>Puts the car back on the route at a point, facing along it, at rest.</summary>
        public void Reset(int routeIndex)
        {
            int i = Mathf.Clamp(routeIndex, 0, route.Length - 1);
            int n = Mathf.Min(i + 1, route.Length - 1);
            Vector3 forward = route[n] - route[i];
            if (forward.sqrMagnitude < 0.01f && i > 0) forward = route[i] - route[i - 1];
            forward.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward.normalized : transform.forward, Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = route[i] + Vector3.up * 0.6f;
            body.rotation = rotation;
            transform.SetPositionAndRotation(body.position, rotation);
            lostTimer = stuckTimer = reverseTimer = 0f;
            reverseCount = 0;
        }
    }
}
