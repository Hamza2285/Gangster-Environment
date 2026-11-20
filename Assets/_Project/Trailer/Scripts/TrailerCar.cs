using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Trailer
{
    /// <summary>
    /// A car prefab instance posed by the trailer each frame (kinematic, no RCC / iTS driving): sits on the road by raycast,
    /// spins and steers its wheels from the distance travelled, drifts (body yaw off the path), leans, smokes its rear tyres
    /// and keeps its headlights / police light bar on. <see cref="Release"/> hands it to physics for a crash.
    /// </summary>
    public class TrailerCar
    {
        public GameObject Go { get; private set; }
        public Transform T => Go.transform;
        public Rigidbody Body { get; private set; }
        public bool Dynamic { get; private set; }

        private readonly List<Transform> wheels = new List<Transform>();
        private readonly List<Quaternion> wheelRest = new List<Quaternion>();
        private readonly List<bool> wheelFront = new List<bool>();
        private readonly List<ParticleSystem> smoke = new List<ParticleSystem>();
        private Collider[] colliders = new Collider[0];
        private float wheelRadius = 0.36f;
        private float spinDeg;
        private Vector3 lastPos;
        private bool hasLast;
        private float wheelBase = 2.8f;
        private float smoothY;

        private static readonly string[] DisableTypes =
        {
            "RCC_CarControllerV3", "RCCVehicleAdapter", "TrafficVehicleDriver", "VehicleCharacterImpact", "VehicleHumanContactFx",
            "NavMeshObstacle", "TSSimpleCar", "TSAntiRollBar", "TSNavigation", "TSTrafficAI", "TrafficVehicleAdapter", "PoliceCarUnit",
            "PoliceCarCrew", "MapBlip", "VehicleHealth", "VehicleDamageDeformer", "VehicleGlassHit", "WindshieldCrashSensor",
            "VehicleCollisionAudio", "RCC_Light", "RCC_Exhaust", "RCC_Skidmarks", "RCC_WheelCollider", "VehicleNavMeshObstacle",
            "SeatedDriverHealth", "VehicleImpactCameraShake"
        };

        public static TrailerCar Spawn(GameObject prefab, Vector3 pos, float yaw, int paintIndex, Transform holder, GameObject smokePrefab, bool police = false,
            OpenworldCrime.Vehicles.PedDriverRoster roster = null, int driverIndex = -1)
        {
            var car = new TrailerCar();
            // Spawn under an inactive holder so no Awake / OnEnable of the driving components runs before they are switched off.
            var parking = new GameObject("TrailerSpawnHolder");
            parking.SetActive(false);
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parking.transform);
            go.name = prefab.name + " (Trailer)";
            car.Go = go;

            var rcc = go.GetComponent<RCC_CarControllerV3>();
            if (rcc != null)
            {
                Add(car, rcc.FrontLeftWheelTransform, true);
                Add(car, rcc.FrontRightWheelTransform, true);
                Add(car, rcc.RearLeftWheelTransform, false);
                Add(car, rcc.RearRightWheelTransform, false);
                rcc.engineRunning = true;
                rcc.lowBeamHeadLightsOn = true;
            }
            else
            {
                foreach (Transform c in go.transform)
                {
                    string n = c.name.ToLowerInvariant();
                    if (!n.Contains("wheel") && !n.Contains("tire") && !n.Contains("tyre")) continue;
                    if (c.GetComponentInChildren<Renderer>(true) == null) continue;
                    if (Mathf.Abs(c.localPosition.x) < 0.3f) continue;
                    Add(car, c, c.localPosition.z > 0f);
                }
            }
            if (car.wheels.Count >= 2)
            {
                float y = 0f, zf = -99f, zr = 99f;
                foreach (var w in car.wheels) { y += w.localPosition.y; zf = Mathf.Max(zf, w.localPosition.z); zr = Mathf.Min(zr, w.localPosition.z); }
                car.wheelRadius = Mathf.Max(0.25f, y / car.wheels.Count);
                car.wheelBase = Mathf.Max(1.5f, zf - zr);
            }

            foreach (var mb in go.GetComponentsInChildren<Behaviour>(true))
            {
                string tn = mb.GetType().Name;
                for (int i = 0; i < DisableTypes.Length; i++) if (tn == DisableTypes[i]) { mb.enabled = false; break; }
            }
            // iTS wheel scripts expect their (now disabled) car in Awake; the posed car does not need them.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "TSSimpleCar_Wheel") Object.DestroyImmediate(mb);
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) { a.playOnAwake = false; a.enabled = false; }
            foreach (var wc in go.GetComponentsInChildren<WheelCollider>(true)) wc.enabled = false;

            var paint = go.GetComponent<OpenworldCrime.Vehicles.VehiclePaintVariants>();
            if (paint != null && paintIndex >= 0) paint.ApplyVariant(Mathf.Clamp(paintIndex, 0, paint.Count - 1));

            car.Body = go.GetComponent<Rigidbody>();
            if (car.Body != null) { car.Body.isKinematic = true; car.Body.interpolation = RigidbodyInterpolation.None; }
            car.colliders = go.GetComponentsInChildren<Collider>(true);
            car.SetColliders(false);

            if (police)
            {
                foreach (var bar in go.GetComponentsInChildren<PoliceLightBar>(true)) bar.enabled = true;
            }

            if (smokePrefab != null)
            {
                foreach (var w in car.wheels)
                {
                    if (car.wheelFront[car.wheels.IndexOf(w)]) continue;
                    var s = Object.Instantiate(smokePrefab, go.transform);
                    s.transform.localPosition = new Vector3(w.localPosition.x, 0.15f, w.localPosition.z);
                    s.transform.localRotation = Quaternion.identity;
                    foreach (var ps in s.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                        main.playOnAwake = false;
                        main.startColor = new Color(0.45f, 0.45f, 0.5f, 0.22f); // grey smoke, no blown-out white blob
                        var em = ps.emission;
                        em.rateOverTime = 0f;
                        em.rateOverDistance = 0f;
                        car.smoke.Add(ps);
                    }
                }
            }

            go.transform.SetParent(holder, true);
            Object.Destroy(parking);
            // Seat the driver (and the second officer in a cruiser) with the project's own seated driver visuals.
            var crew = go.GetComponent<OpenworldCrime.Police.PoliceCarCrew>();
            if (police && crew != null) crew.Board(1); // driver only: the passenger officer's seated pose clips the dash
            else if (roster != null && driverIndex >= 0)
            {
                var driver = go.GetComponent<OpenworldCrime.Vehicles.TrafficVehicleDriver>();
                if (driver != null) driver.SetSeatedPedestrian(true, roster, driverIndex);
            }
            car.DisableHitZones();
            car.AddCabinLight();
            foreach (var ps in car.smoke) ps.Play(true);
            return car;
        }

        private static void Add(TrailerCar car, Transform w, bool front)
        {
            if (w == null) return;
            car.wheels.Add(w);
            car.wheelRest.Add(w.localRotation);
            car.wheelFront.Add(front);
        }

        // A soft dashboard glow so the people inside read through the glass at night (trailer only).
        private void AddCabinLight()
        {
            var seat = Go.transform.Find("DriverSeat");
            if (seat == null) return;
            var go = new GameObject("Trailer Cabin Light");
            go.transform.SetParent(Go.transform, false);
            Vector3 local = Go.transform.InverseTransformPoint(seat.position);
            // A soft dash glow: a wide spot from the dashboard aimed back at the driver's face (level, so it never lights
            // the road under the car).
            go.transform.localPosition = new Vector3(local.x, local.y + 1.2f, local.z + 0.75f);
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.05f, -1f));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 75f;
            l.innerSpotAngle = 30f;
            l.range = 1.5f;
            l.intensity = 4.5f;
            l.color = new Color(0.65f, 0.8f, 1f);
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>
        /// Trailer "car lighting": a soft key above the bonnet and two coloured rims (neon magenta / cyan) that follow the
        /// car, so a black car reads at night with highlights along its body lines.
        /// </summary>
        public void AddHeroLights()
        {
            // Ranges kept short so the lights catch the body, not the road around it.
            AddLight("Trailer Key", new Vector3(0.4f, 2.1f, 0.8f), new Color(0.85f, 0.9f, 1f), 5f, 2.15f);
            AddLight("Trailer Rim L", new Vector3(-1.7f, 1.6f, -1.3f), new Color(1f, 0.35f, 0.85f), 5f, 1.95f);
            AddLight("Trailer Rim R", new Vector3(1.7f, 1.6f, -0.9f), new Color(0.3f, 0.85f, 1f), 5f, 1.95f);
            // The car's own real-time headlight lights are tuned for the LDR game look; in the HDR grade they blow the
            // road out to white, so they are dimmed on this car.
            foreach (var l in Go.GetComponentsInChildren<Light>(true))
                if (!l.name.StartsWith("Trailer ")) l.intensity *= 0.15f;
        }

        private void AddLight(string name, Vector3 local, Color colour, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Go.transform, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
        }

        // Seated-driver hit zones are not needed (and must not catch the road raycasts or the crash).
        private void DisableHitZones()
        {
            foreach (var mb in Go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string n = mb.GetType().Name;
                if (n == "SeatedDriverHitZone") foreach (var c in mb.GetComponents<Collider>()) c.enabled = false;
            }
        }

        public void SetColliders(bool on)
        {
            foreach (var c in colliders) if (c != null && !(c is WheelCollider)) c.enabled = on;
        }

        /// <summary>
        /// Poses the car: ground point under xz, body yaw (deg), front wheel steer (deg), lean (deg, + = roll right), smoke 0-1.
        /// </summary>
        public void Pose(Vector3 xz, float yaw, float steer, float lean, float smokeAmount, float dt, bool snap = false)
        {
            if (Dynamic) return;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;
            float half = wheelBase * 0.5f;
            Vector3 c = Ground(xz, xz.y);
            Vector3 f = Ground(xz + fwd * half, c.y);
            Vector3 r = Ground(xz - fwd * half, c.y);
            float y = (f.y + r.y) * 0.5f;
            if (snap || !hasLast) smoothY = y; else smoothY = Mathf.Lerp(smoothY, y, 0.6f);
            float pitch = -Mathf.Atan2(f.y - r.y, wheelBase) * Mathf.Rad2Deg;
            Vector3 pos = new Vector3(xz.x, smoothY, xz.z);
            Quaternion rot = yawRot * Quaternion.Euler(pitch, 0f, -lean);
            T.SetPositionAndRotation(pos, rot);
            if (Body != null) { Body.position = pos; Body.rotation = rot; }

            // Wheels roll with the distance moved along the car's heading.
            float moved = hasLast && !snap ? Vector3.Dot(pos - lastPos, fwd) : 0f;
            lastPos = pos;
            hasLast = true;
            spinDeg = Mathf.Repeat(spinDeg + moved / wheelRadius * Mathf.Rad2Deg, 360f);
            for (int i = 0; i < wheels.Count; i++)
            {
                float st = wheelFront[i] ? steer : 0f;
                wheels[i].localRotation = Quaternion.AngleAxis(st, Vector3.up) * Quaternion.AngleAxis(spinDeg, Vector3.right) * wheelRest[i];
            }

            float rate = Mathf.Clamp01(smokeAmount) * 70f;
            foreach (var ps in smoke) { var em = ps.emission; em.rateOverTime = rate; }
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Highest static (non-rigidbody) surface below a point, searched from just above the expected height.</summary>
        public static Vector3 Ground(Vector3 p, float expectY)
        {
            Vector3 from = new Vector3(p.x, expectY + 4f, p.z);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, 14f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].rigidbody != null) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return new Vector3(p.x, float.IsNegativeInfinity(best) ? expectY : best, p.z);
        }

        /// <summary>Hands the car to physics (crash): colliders on, velocity and spin given.</summary>
        public void Release(Vector3 velocity, Vector3 angularVelocity)
        {
            if (Body == null) return;
            Dynamic = true;
            SetColliders(true);
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.linearVelocity = velocity;
            Body.angularVelocity = angularVelocity;
            foreach (var ps in smoke) { var em = ps.emission; em.rateOverTime = 40f; }
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
