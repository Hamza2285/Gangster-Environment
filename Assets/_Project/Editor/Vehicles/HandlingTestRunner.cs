using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Repeatable handling test for the drivable (RCC) cars, run in editor play mode on a flat test pad far above the city (no
    /// traffic, no kerbs). Each car is spawned from its prefab and driven with scripted inputs the way the player drives it: RCC
    /// reads them every frame, its own gearbox, clutch, counter-steering and helpers run, and steering is ramped like the
    /// keyboard axis RCC reads (its Input Manager sensitivity / gravity), so slow keyboard steering shows in the numbers.
    /// Measures 0-100 km/h (with the squat pitch), speed after 10 s, top speed in 30 s, braking 100-0 (with the nose-dive pitch),
    /// turning circle at 25 km/h, a lane change at 80 km/h, a handbrake turn at 60 km/h (slip angle, recovery), power oversteer
    /// at 40 km/h (full throttle in a full-lock turn, then countersteer: slip angle, recovery), a full-lock corner at 60 km/h
    /// (grip, body roll, wheel lift) and a rollover check: full lock at 80 km/h.
    /// Report: Captures/Handling/handling_&lt;label&gt;.txt. Menu: Tools > OpenworldCrime > Vehicles > Run Handling Test.
    /// </summary>
    public static class HandlingTestRunner
    {
        private const string OutputFolder = "Captures/Handling";
        private const string PendingKey = "OWC.Handling.Label";
        private const string BaselineKey = "OWC.Handling.Baseline";
        private static readonly string[] DefaultCars =
        {
            "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car3.prefab",
            "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car2.prefab",
            "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Talenter.prefab",
            "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_GWagon.prefab",
        };

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.EnteredPlayMode) return;
                string label = SessionState.GetString(PendingKey, null);
                if (string.IsNullOrEmpty(label)) return;
                SessionState.EraseString(PendingKey);
                Start(label, true, SessionState.GetBool(BaselineKey, false));
            };
        }

        [MenuItem("Tools/OpenworldCrime/Vehicles/Run Handling Test (about 3 min, play mode)")]
        private static void RunFromMenu() => Run("run_" + System.DateTime.Now.ToString("HHmmss"));

        /// <summary>
        /// Starts the test (enters play mode if needed). Leaves play mode when done if it entered it. Baseline = the cars as they
        /// were before the handling pass, for comparison: prefab RCC values (no handling profile), steering on the old keyboard
        /// axis ("Horizontal") and 50 Hz physics; all of it is put back afterwards.
        /// </summary>
        public static void Run(string label, bool baseline = false)
        {
            if (EditorApplication.isPlaying) { Start(label, false, baseline); return; }
            SessionState.SetString(PendingKey, label);
            SessionState.SetBool(BaselineKey, baseline);
            EditorApplication.EnterPlaymode();
        }

        private static void Start(string label, bool exitWhenDone, bool baseline)
        {
            var go = new GameObject("HandlingTest");
            var probe = go.AddComponent<HandlingTestProbe>();
            probe.Begin(label, DefaultCars, exitWhenDone, baseline);
        }

        /// <summary>Keyboard axis (sensitivity, gravity) from the Input Manager.</summary>
        public static void GetSteerAxis(string axis, out float sensitivity, out float gravity)
        {
            sensitivity = 3f;
            gravity = 3f;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (assets == null || assets.Length == 0) return;
            SerializedProperty axes = new SerializedObject(assets[0]).FindProperty("m_Axes");
            for (int i = 0; i < axes.arraySize; i++)
            {
                SerializedProperty a = axes.GetArrayElementAtIndex(i);
                if (a.FindPropertyRelative("m_Name").stringValue != axis || a.FindPropertyRelative("type").intValue != 0) continue;
                sensitivity = a.FindPropertyRelative("sensitivity").floatValue;
                gravity = a.FindPropertyRelative("gravity").floatValue;
                return;
            }
        }

        public static void WriteReport(string label, string text)
        {
            Directory.CreateDirectory(OutputFolder);
            string file = Path.Combine(OutputFolder, $"handling_{label}.txt");
            File.WriteAllText(file, text);
            Debug.Log($"[HandlingTest] Report written to {Path.GetFullPath(file)}\n{text}");
        }
    }

    /// <summary>Runtime side of <see cref="HandlingTestRunner"/> (editor only, created and destroyed by it).</summary>
    [DefaultExecutionOrder(-200)] // inputs are written before RCC reads them in its Update
    public class HandlingTestProbe : MonoBehaviour
    {
        private const float PadHeight = 3000f;
        private const float TimeScale = 2f;
        private static readonly Vector3 StartPosition = new Vector3(-1600f, PadHeight, 0f);

        private RCC_CarControllerV3 car;
        private Rigidbody body;
        private string label;
        private bool exitWhenDone;
        private bool baseline;
        private string steerAxis;
        private float steerSensitivity, steerGravity;
        private readonly StringBuilder report = new StringBuilder();
        private readonly StringBuilder diagnostics = new StringBuilder();
        private readonly StringBuilder report2 = new StringBuilder();

        // Wanted inputs; steering is ramped toward wantedSteer like the keyboard axis.
        private float wantedSteer, steer, throttle, brake, handbrake;
        private float holdSpeedKmh = -1f;

        public void Begin(string runLabel, string[] prefabs, bool exit, bool baselineRun)
        {
            label = runLabel;
            exitWhenDone = exit;
            baseline = baselineRun;
            steerAxis = baseline ? "Horizontal" : RCC_Settings.Instance.horizontalInput;
            HandlingTestRunner.GetSteerAxis(steerAxis, out steerSensitivity, out steerGravity);
            StartCoroutine(RunAll(prefabs));
        }

        private IEnumerator RunAll(string[] prefabs)
        {
            float oldScale = Time.timeScale;
            float oldFixed = Time.fixedDeltaTime, oldRccFixed = RCC_Settings.Instance.fixedTimeStep;
            if (baseline)
            {
                Time.fixedDeltaTime = 0.02f;
                RCC_Settings.Instance.fixedTimeStep = 0.02f;
            }
            Time.timeScale = TimeScale;
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "HandlingTestPad";
            pad.transform.position = new Vector3(0f, PadHeight - 0.5f, 0f);
            pad.transform.localScale = new Vector3(4000f, 1f, 4000f);
            pad.GetComponent<Renderer>().enabled = false;

            report.AppendLine($"Handling test '{label}'  {System.DateTime.Now:yyyy-MM-dd HH:mm}  fixedDeltaTime {Time.fixedDeltaTime:F3}");
            report.AppendLine($"{(baseline ? "BASELINE (prefab RCC values, no handling profile)" : "Current setup")}. Steering ramp like keyboard axis '{steerAxis}': sensitivity {steerSensitivity}, gravity {steerGravity} (units/s)");
            report.AppendLine();
            report.AppendLine("car            | 0-100 s | 10s km/h | top30s | brake100 m | circle25 m | lane80: lat m, yaw dps, roll deg, settle s | hb60: turn deg, slip deg, settle s, spun | corner60: lat g, roll deg, lift");
            report2.AppendLine("car            | squat deg | dive deg | power-oversteer40: slip deg, settle s, spun | full-lock80: peak lat g, avg lat g, roll deg, lift, flipped");

            foreach (string path in prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { report.AppendLine($"{path}: missing"); continue; }
                // Created under an inactive holder so the baseline can drop the handling profile before Awake applies it.
                var holder = new GameObject("HandlingTestHolder");
                holder.SetActive(false);
                GameObject instance = Instantiate(prefab, StartPosition + Vector3.up * 0.6f, Quaternion.Euler(0f, 90f, 0f), holder.transform);
                if (baseline && instance.TryGetComponent(out RCCVehicleAdapter adapter))
                {
                    var serialized = new SerializedObject(adapter);
                    serialized.FindProperty("handlingProfile").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                instance.transform.SetParent(null, true);
                Destroy(holder);
                car = instance.GetComponent<RCC_CarControllerV3>();
                body = instance.GetComponent<Rigidbody>();
                yield return null;
                yield return null;
                // Driven like the player's car, but by this script: RCC runs its gearbox and clutch only while canControl is on.
                car.externalController = true;
                car.SetCanControl(true);
                car.StartEngine(true);
                yield return RunCar(prefab.name);
                Destroy(instance);
                yield return null;
            }
            Destroy(pad);
            Time.timeScale = oldScale;
            Time.fixedDeltaTime = oldFixed;
            RCC_Settings.Instance.fixedTimeStep = oldRccFixed;
            report.AppendLine();
            report.Append(report2);
            report.AppendLine();
            report.AppendLine("Details:");
            report.Append(diagnostics);
            HandlingTestRunner.WriteReport(label, report.ToString());
            Destroy(gameObject);
            if (exitWhenDone) EditorApplication.ExitPlaymode();
        }

        private float SpeedKmh => body.linearVelocity.magnitude * 3.6f;
        private float ForwardKmh => Vector3.Dot(body.linearVelocity, car.transform.forward) * 3.6f;

        private void Update()
        {
            if (car == null) return;
            float rate = Mathf.Abs(wantedSteer) > 0.001f && Mathf.Sign(wantedSteer) == Mathf.Sign(steer) || Mathf.Abs(steer) < 0.001f ? steerSensitivity : steerGravity;
            if (Mathf.Abs(wantedSteer) > 0.001f && Mathf.Abs(steer) > 0.001f && Mathf.Sign(wantedSteer) != Mathf.Sign(steer)) steer = 0f; // axis snap
            steer = Mathf.MoveTowards(steer, wantedSteer, rate * Time.deltaTime);
            float t = throttle, b = brake;
            if (holdSpeedKmh >= 0f)
            {
                float v = ForwardKmh;
                t = Mathf.Clamp01((holdSpeedKmh - v) / 6f);
                b = Mathf.Clamp01((v - holdSpeedKmh - 4f) / 12f);
            }
            car.throttleInput = t;
            car.brakeInput = b;
            car.steerInput = steer;
            car.handbrakeInput = handbrake;
        }

        private void SetInputs(float newThrottle, float newBrake, float newSteer, float newHandbrake, float hold = -1f)
        {
            throttle = newThrottle; brake = newBrake; wantedSteer = newSteer; handbrake = newHandbrake; holdSpeedKmh = hold;
        }

        private IEnumerator ResetCar()
        {
            // Held with the handbrake: the foot brake at a standstill makes RCC's automatic gearbox select reverse.
            SetInputs(0f, 0f, 0f, 1f);
            steer = 0f;
            body.position = StartPosition + Vector3.up * 0.6f;
            body.rotation = Quaternion.Euler(0f, 90f, 0f);
            car.transform.SetPositionAndRotation(body.position, body.rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(1.2f);
            SetInputs(0f, 0f, 0f, 0f);
            float timeout = Time.time + 3f;
            while ((car.direction != 1 || car.changingGear) && Time.time < timeout) yield return null;
            yield return new WaitForFixedUpdate();
        }

        private IEnumerator ReachSpeed(float kmh, float timeout)
        {
            SetInputs(1f, 0f, 0f, 0f);
            float end = Time.time + timeout;
            while (ForwardKmh < kmh && Time.time < end) yield return new WaitForFixedUpdate();
            SetInputs(0f, 0f, 0f, 0f, kmh);
            yield return new WaitForSeconds(0.8f);
        }

        private IEnumerator RunCar(string name)
        {
            float t100 = -1f, at10 = 0f, top = 0f, squat = 0f;
            yield return ResetCar();
            float restPitch = Pitch();
            SetInputs(1f, 0f, 0f, 0f);
            float start = Time.fixedTime;
            while (Time.fixedTime - start < 30f)
            {
                yield return new WaitForFixedUpdate();
                float elapsed = Time.fixedTime - start, v = SpeedKmh;
                if (t100 < 0f && v >= 100f) t100 = elapsed;
                if (elapsed <= 10f) at10 = v;
                if (elapsed <= 3f) squat = Mathf.Max(squat, Pitch() - restPitch);
                top = Mathf.Max(top, v);
            }

            // Braking 100 -> 0.
            yield return ResetCar();
            yield return ReachSpeed(100f, 15f);
            SetInputs(0f, 0f, 0f, 0f, 100f);
            yield return new WaitForSeconds(0.5f);
            Vector3 brakeStart = body.position;
            float brakeV = SpeedKmh;
            float cruisePitch = Pitch(), dive = 0f;
            SetInputs(0f, 1f, 0f, 0f);
            float brakeEnd = Time.fixedTime + 10f;
            while (SpeedKmh > 1f && Time.fixedTime < brakeEnd)
            {
                yield return new WaitForFixedUpdate();
                dive = Mathf.Max(dive, cruisePitch - Pitch());
            }
            float brakeDistance = Vector3.Distance(brakeStart, body.position) * Mathf.Pow(100f / Mathf.Max(1f, brakeV), 2f);

            // Turning circle at 25 km/h, full lock.
            yield return ResetCar();
            yield return ReachSpeed(25f, 8f);
            SetInputs(0f, 0f, 1f, 0f, 25f);
            yield return new WaitForSeconds(3f);
            float yawSum = 0f, speedSum = 0f; int n = 0;
            float circleEnd = Time.fixedTime + 3f;
            while (Time.fixedTime < circleEnd)
            {
                yield return new WaitForFixedUpdate();
                yawSum += Mathf.Abs(body.angularVelocity.y); speedSum += body.linearVelocity.magnitude; n++;
            }
            float circleRadius = (speedSum / n) / Mathf.Max(0.01f, yawSum / n);

            // Lane change at 80 km/h: right for 0.45 s, left for 0.45 s, straight.
            yield return ResetCar();
            yield return ReachSpeed(80f, 15f);
            yield return new WaitForSeconds(0.5f);
            Vector3 laneOrigin = body.position, laneDir = car.transform.forward;
            float laneV = ForwardKmh, laneSteerAngle = car.steerAngle;
            Vector3 laneRight = Vector3.Cross(Vector3.up, laneDir).normalized;
            float maxYaw = 0f, maxRoll = 0f, settle = -1f;
            float laneStart = Time.fixedTime;
            SetInputs(0f, 0f, 1f, 0f, 80f);
            while (Time.fixedTime - laneStart < 4.5f)
            {
                float e = Time.fixedTime - laneStart;
                if (e > 0.45f && e <= 0.9f) wantedSteer = -1f;
                else if (e > 0.9f) wantedSteer = 0f;
                yield return new WaitForFixedUpdate();
                float yawRate = Mathf.Abs(body.angularVelocity.y) * Mathf.Rad2Deg;
                maxYaw = Mathf.Max(maxYaw, yawRate);
                maxRoll = Mathf.Max(maxRoll, Roll());
                if (e > 0.9f && settle < 0f && yawRate < 2f && Roll() < 1f) settle = e - 0.9f;
                if (e > 0.9f && yawRate >= 2f) settle = -1f;
            }
            float lateral = Vector3.Dot(body.position - laneOrigin, laneRight);
            float headingError = Vector3.SignedAngle(laneDir, Flat(car.transform.forward), Vector3.up);

            // Handbrake turn at 60 km/h.
            yield return ResetCar();
            yield return ReachSpeed(60f, 12f);
            Vector3 hbDir = Flat(car.transform.forward);
            SetInputs(0f, 0f, 1f, 1f);
            float hbStart = Time.fixedTime, hbSettle = -1f, hbTurn = 0f, hbSlip = 0f;
            while (Time.fixedTime - hbStart < 4.5f)
            {
                float e = Time.fixedTime - hbStart;
                if (e > 0.8f) { handbrake = 0f; wantedSteer = 0f; throttle = 0.5f; }
                yield return new WaitForFixedUpdate();
                float signed = Vector3.SignedAngle(hbDir, Flat(car.transform.forward), Vector3.up);
                hbTurn = Mathf.Max(hbTurn, Mathf.Abs(signed));
                hbSlip = Mathf.Max(hbSlip, SlipAngle());
                float yawRate = Mathf.Abs(body.angularVelocity.y) * Mathf.Rad2Deg;
                if (e > 0.8f && hbSettle < 0f && yawRate < 8f) hbSettle = e - 0.8f;
                if (e > 0.8f && yawRate >= 8f) hbSettle = -1f;
            }
            bool spun = hbTurn > 200f;

            // Full-lock corner at 60 km/h.
            yield return ResetCar();
            yield return ReachSpeed(60f, 12f);
            SetInputs(0f, 0f, 1f, 0f, 60f);
            yield return new WaitForSeconds(1.5f);
            float latSum = 0f, cornerRoll = 0f; int ln = 0; bool lift = false;
            float cornerEnd = Time.fixedTime + 3f;
            while (Time.fixedTime < cornerEnd)
            {
                yield return new WaitForFixedUpdate();
                latSum += body.linearVelocity.magnitude * Mathf.Abs(body.angularVelocity.y) / 9.81f; ln++;
                cornerRoll = Mathf.Max(cornerRoll, Roll());
                foreach (RCC_WheelCollider w in car.allWheelColliders) if (!w.isGrounded) lift = true;
            }

            // Power oversteer at 40 km/h: full lock, then full throttle for 1.5 s, then off the throttle and countersteer for 0.6 s.
            yield return ResetCar();
            yield return ReachSpeed(40f, 10f);
            SetInputs(0f, 0f, 1f, 0f, 40f);
            yield return new WaitForSeconds(0.6f);
            Vector3 poDir = Flat(car.transform.forward);
            SetInputs(1f, 0f, 1f, 0f);
            float poStart = Time.fixedTime, poSlip = 0f, poSettle = -1f, poTurn = 0f;
            while (Time.fixedTime - poStart < 5f)
            {
                float e = Time.fixedTime - poStart;
                if (e > 1.5f && e <= 2.1f) { throttle = 0f; wantedSteer = -0.6f; }
                else if (e > 2.1f) { throttle = 0.2f; wantedSteer = 0f; }
                yield return new WaitForFixedUpdate();
                float slip = SlipAngle();
                poSlip = Mathf.Max(poSlip, slip);
                poTurn = Mathf.Max(poTurn, Mathf.Abs(Vector3.SignedAngle(poDir, Flat(car.transform.forward), Vector3.up)));
                float yawRate = Mathf.Abs(body.angularVelocity.y) * Mathf.Rad2Deg;
                bool calm = yawRate < 8f && (slip < 6f || SpeedKmh < 5f);
                if (e > 1.5f && poSettle < 0f && calm) poSettle = e - 1.5f;
                if (e > 1.5f && !calm) poSettle = -1f;
            }
            // Accumulated heading change, so a full spin counts even though SignedAngle wraps at 180.
            bool poSpun = poSlip > 120f;

            // Rollover check: full lock at 80 km/h, held 4 s (speed falls off as the car scrubs).
            yield return ResetCar();
            yield return ReachSpeed(80f, 15f);
            SetInputs(0f, 0f, 1f, 0f, 80f);
            float rollStart = Time.fixedTime, peakLat = 0f, latAvg = 0f, maxRoll80 = 0f; int rn = 0;
            bool lift80 = false, flipped = false;
            while (Time.fixedTime - rollStart < 4f)
            {
                yield return new WaitForFixedUpdate();
                float g = body.linearVelocity.magnitude * Mathf.Abs(body.angularVelocity.y) / 9.81f;
                peakLat = Mathf.Max(peakLat, g);
                if (Time.fixedTime - rollStart > 1f) { latAvg += g; rn++; }
                maxRoll80 = Mathf.Max(maxRoll80, Roll());
                foreach (RCC_WheelCollider w in car.allWheelColliders) if (!w.isGrounded) lift80 = true;
                if (car.transform.up.y < 0.5f) flipped = true;
            }
            report2.AppendLine($"{name,-14} | {squat,9:F2} | {dive,8:F2} | {poSlip:F0}, {(poSettle < 0 ? ">3.5" : poSettle.ToString("F2"))}, {(poSpun ? "SPUN" : "no")} | " +
                               $"{peakLat:F2}, {latAvg / Mathf.Max(1, rn):F2}, {maxRoll80:F1}, {(lift80 ? "WHEEL LIFT" : "no")}, {(flipped ? "FLIPPED" : "no")}");

            diagnostics.AppendLine($"  {name}: brake from {brakeV:F0} km/h; lane change from {laneV:F0} km/h, front wheel lock {laneSteerAngle:F1} deg; " +
                                   $"torque {car.maxEngineTorque:F0} Nm @ {car.maxEngineTorqueAtRPM:F0} rpm, final {car.finalRatio:F2}, gears {car.gears.Length} " +
                                   $"(targets {GearTargets()}), mass {body.mass:F0}, COM y {car.COM.localPosition.y:F2}, TCS {car.TCS} ABS {car.ABS} ESP {car.ESP}, upright {car.transform.up.y:F2}");
            report.AppendLine($"{name,-14} | {(t100 < 0 ? "  >20" : t100.ToString("F2")),7} | {at10,8:F0} | {top,6:F0} | {brakeDistance,10:F1} | {circleRadius,10:F1} | " +
                              $"{lateral:F2}, {maxYaw:F0}, {maxRoll:F1}, {(settle < 0 ? ">3.6" : settle.ToString("F2"))} (heading {headingError:F0}) | " +
                              $"{hbTurn:F0}, {hbSlip:F0}, {(hbSettle < 0 ? ">3.7" : hbSettle.ToString("F2"))}, {(spun ? "SPUN" : "no")} | {latSum / Mathf.Max(1, ln):F2}, {cornerRoll:F1}, {(lift ? "WHEEL LIFT" : "no")}");
            SetInputs(0f, 1f, 0f, 1f);
        }

        private string GearTargets()
        {
            var parts = new List<string>();
            foreach (RCC_CarControllerV3.Gear g in car.gears) parts.Add(g.targetSpeedForNextGear.ToString());
            return string.Join("/", parts);
        }

        private float Roll() => Mathf.Abs(Mathf.DeltaAngle(0f, car.transform.eulerAngles.z));
        // Nose up positive.
        private float Pitch() => -Mathf.DeltaAngle(0f, car.transform.eulerAngles.x);

        // Angle between where the car points and where it travels (0 when moving slowly).
        private float SlipAngle()
        {
            Vector3 v = body.linearVelocity;
            v.y = 0f;
            if (v.magnitude < 2f) return 0f;
            return Vector3.Angle(Flat(car.transform.forward), v.normalized);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
    }
}
