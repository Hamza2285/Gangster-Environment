using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only, added at runtime: drives each listed drivable bike on a flat pad far above the city with scripted inputs (steering
    /// ramped like the keyboard axis, 3/s) and reports acceleration, wheelie, straight-line wobble, braking, step-steer response at
    /// 20 / 50 / 90 km/h (yaw rate rise time, radius, body roll, visual lean and its lag), a low-speed full-lock turn and a
    /// slalom. Report: Captures/Fixes/bike_handling_[label].txt.
    /// </summary>
    public class BikeHandlingProbe : MonoBehaviour
    {
        public GameObject[] bikes = new GameObject[0];
        public string label = "run";
        public bool Done { get; private set; }

        private const float PadHeight = 3000f;
        private RCC_CarControllerV3 car;
        private Rigidbody body;
        private BikeRig rig;
        private float steer;
        private readonly StringBuilder report = new StringBuilder();

        private IEnumerator Start()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "BikeTestPad";
            pad.transform.position = new Vector3(0f, PadHeight - 0.5f, 0f);
            pad.transform.localScale = new Vector3(4000f, 1f, 4000f);
            pad.GetComponent<Renderer>().enabled = false;
            report.AppendLine("Bike handling " + label + "  " + System.DateTime.Now);
            foreach (GameObject prefab in bikes)
            {
                if (prefab == null) continue;
                report.AppendLine("\n== " + prefab.name);
                yield return Test("accel", prefab, Accel());
                yield return Test("straight 80", prefab, Straight(80f));
                yield return Test("brake 80-0", prefab, Brake(80f));
                foreach (float kmh in new[] { 20f, 50f, 90f }) yield return Test("step steer " + kmh, prefab, StepSteer(kmh));
                yield return Test("full lock 8", prefab, StepSteer(8f));
                yield return Test("slalom 60", prefab, Slalom(60f));
            }
            Destroy(pad);
            Directory.CreateDirectory("Captures/Fixes");
            string path = "Captures/Fixes/bike_handling_" + label + ".txt";
            File.WriteAllText(path, report.ToString());
            Debug.Log("[BikeHandlingProbe] wrote " + path + "\n" + report);
            Done = true;
        }

        private IEnumerator Test(string name, GameObject prefab, IEnumerator body2)
        {
            GameObject instance = Instantiate(prefab, new Vector3(-1600f, PadHeight + 0.6f, 0f), Quaternion.Euler(0f, 90f, 0f));
            yield return null;
            yield return null;
            car = instance.GetComponent<RCC_CarControllerV3>();
            body = instance.GetComponent<Rigidbody>();
            rig = instance.GetComponent<BikeRig>();
            car.externalController = true;
            car.SetCanControl(true);
            car.StartEngine(true);
            steer = 0f;
            yield return new WaitForSeconds(0.5f);
            report.Append(name.PadRight(14) + ": ");
            yield return body2;
            report.AppendLine();
            Destroy(instance);
            yield return null;
        }

        private float Speed => Vector3.Dot(body.linearVelocity, body.transform.forward) * 3.6f;
        private float Roll => Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, body.transform.forward), Vector3.ProjectOnPlane(body.transform.up, body.transform.forward), body.transform.forward);
        private float Pitch => -Mathf.Asin(Mathf.Clamp(body.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg; // + = nose up
        private float YawRate => Vector3.Dot(body.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
        private float VisualLean => rig != null && rig.visual != null ? Mathf.DeltaAngle(0f, rig.visual.localEulerAngles.z) : 0f;
        private bool Tipped => body.transform.up.y < 0.6f;

        private void Drive(float throttle, float brake, float steerTarget)
        {
            steer = Mathf.MoveTowards(steer, steerTarget, 3f * Time.fixedDeltaTime);
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.steerInput = steer;
            car.handbrakeInput = 0f;
        }

        private void Hold(float kmh, float steerTarget)
        {
            float e = kmh - Speed;
            Drive(Mathf.Clamp01(e * 0.15f), Mathf.Clamp01(-e * 0.1f), steerTarget);
        }

        private IEnumerator ReachSpeed(float kmh, float timeout)
        {
            float t = 0f;
            while (Speed < kmh - 1f && t < timeout) { Drive(1f, 0f, 0f); t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            for (int i = 0; i < 60; i++) { Hold(kmh, 0f); yield return new WaitForFixedUpdate(); }
        }

        private IEnumerator Accel()
        {
            float t = 0f, t60 = -1f, t100 = -1f, maxPitch = 0f, maxRoll = 0f, v10 = 0f, top = 0f;
            while (t < 20f)
            {
                Drive(1f, 0f, 0f);
                t += Time.fixedDeltaTime;
                float v = Speed;
                if (t60 < 0f && v >= 60f) t60 = t;
                if (t100 < 0f && v >= 100f) t100 = t;
                if (t <= 10f) v10 = v;
                top = Mathf.Max(top, v);
                maxPitch = Mathf.Max(maxPitch, Pitch);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Roll));
                if (Tipped) { report.Append("TIPPED at " + t.ToString("F1") + "s; "); break; }
                yield return new WaitForFixedUpdate();
            }
            report.Append($"0-60 {F(t60)}s, 0-100 {F(t100)}s, 10s {v10:F0} km/h, top(20s) {top:F0} km/h, max nose-up {maxPitch:F1} deg, max roll {maxRoll:F1}");
        }

        private IEnumerator Straight(float kmh)
        {
            yield return ReachSpeed(kmh, 15f);
            float yaw0 = body.transform.eulerAngles.y, rollMin = 0f, rollMax = 0f, leanMax = 0f;
            for (float t = 0f; t < 4f; t += Time.fixedDeltaTime)
            {
                Hold(kmh, 0f);
                rollMin = Mathf.Min(rollMin, Roll); rollMax = Mathf.Max(rollMax, Roll);
                leanMax = Mathf.Max(leanMax, Mathf.Abs(VisualLean));
                yield return new WaitForFixedUpdate();
            }
            float drift = Mathf.DeltaAngle(yaw0, body.transform.eulerAngles.y) / 4f;
            report.Append($"heading drift {drift:F2} deg/s, body roll {rollMin:F1}..{rollMax:F1} deg, visual lean max {leanMax:F1}");
        }

        private IEnumerator Brake(float kmh)
        {
            yield return ReachSpeed(kmh, 15f);
            Vector3 start = body.position;
            float t = 0f, minPitch = 0f;
            bool tipped = false;
            while (Speed > 1f && t < 10f)
            {
                Drive(0f, 1f, 0f);
                minPitch = Mathf.Min(minPitch, Pitch);
                tipped |= Tipped;
                t += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            float d = Vector3.Distance(start, body.position);
            report.Append($"{d:F1} m in {t:F1}s, max nose-down {-minPitch:F1} deg{(tipped ? ", TIPPED" : "")}");
        }

        private IEnumerator StepSteer(float kmh)
        {
            yield return ReachSpeed(kmh, 15f);
            float t = 0f, steady = 0f, rise = -1f, rollMax = 0f, leanSum = 0f, peak = 0f;
            int n = 0;
            float leanLag = -1f;
            var rates = new float[200];
            int count = 0;
            for (; t < 3f; t += Time.fixedDeltaTime)
            {
                Hold(kmh, 1f);
                float r = Mathf.Abs(YawRate);
                if (count < rates.Length) rates[count++] = r;
                peak = Mathf.Max(peak, r);
                rollMax = Mathf.Max(rollMax, Mathf.Abs(Roll));
                if (t > 2f) { steady += r; leanSum += Mathf.Abs(VisualLean); n++; }
                if (Tipped) { report.Append("TIPPED at " + t.ToString("F1") + "s; "); break; }
                yield return new WaitForFixedUpdate();
            }
            steady = n > 0 ? steady / n : 0f;
            float lean = n > 0 ? leanSum / n : 0f;
            for (int i = 0; i < count; i++) if (rates[i] >= steady * 0.9f) { rise = i * Time.fixedDeltaTime; break; }
            float v = Mathf.Max(0.1f, Speed / 3.6f);
            float radius = steady > 0.1f ? v / (steady * Mathf.Deg2Rad) : 0f;
            report.Append($"speed {Speed:F0} km/h, yaw rate {steady:F0} deg/s (peak {peak:F0}), 90% after {F(rise)}s, radius {radius:F1} m, " +
                          $"body roll max {rollMax:F1} deg, visual lean {lean:F0} deg");
        }

        private IEnumerator Slalom(float kmh)
        {
            yield return ReachSpeed(kmh, 15f);
            float rollMax = 0f, rateMax = 0f, minSpeed = 999f;
            bool tipped = false;
            for (float t = 0f; t < 6f; t += Time.fixedDeltaTime)
            {
                float target = Mathf.Repeat(t, 2.4f) < 1.2f ? 1f : -1f;
                Hold(kmh, target);
                rollMax = Mathf.Max(rollMax, Mathf.Abs(Roll));
                rateMax = Mathf.Max(rateMax, Mathf.Abs(YawRate));
                minSpeed = Mathf.Min(minSpeed, Speed);
                tipped |= Tipped;
                yield return new WaitForFixedUpdate();
            }
            report.Append($"body roll max {rollMax:F1} deg, yaw rate max {rateMax:F0} deg/s, min speed {minSpeed:F0} km/h{(tipped ? ", TIPPED" : "")}");
        }

        private static string F(float v) => v < 0f ? "-" : v.ToString("F1");
    }
}
