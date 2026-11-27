using System.Collections;
using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only, added by MCP, never saved in a scene). One run checks:
    /// parachute deploy out of the bag (high drop and a rooftop run-off; behind + side + close-up shots, canopy pivot vs bag
    /// top), glide steering smoothness, jet pitch smoothness (step input on / off, pitch rate per physics step) and the jet
    /// being turned back at the map's soft limit. Log + screenshots to <see cref="folder"/>.
    /// </summary>
    [DefaultExecutionOrder(2000)] // measures after PlayerParachute (1002) and PlayerFreeFall (1003) set the step velocity
    public class FlightFixesTestRunner : MonoBehaviour
    {
        public string folder = "Captures/Fixes4";
        public Vector3 highDrop = new Vector3(150f, 170f, -150f);
        public Vector3 rooftopPickup = new Vector3(-212.5f, 141.7f, -10.5f);
        public bool flightTests = true;
        public bool rooftopTest = true;
        public bool highDropTest = true;
        public bool Done { get; private set; }
        public string Log => log.ToString();

        private readonly StringBuilder log = new StringBuilder();
        private GameObject player;
        private Invector.vCharacterController.vThirdPersonController controller;
        private PlayerParachute parachute;
        private Camera sideCam;
        private RenderTexture rt;
        private Vector3 forcedInput;
        private bool forceInput;

        private void Shot(string name) => ScreenCapture.CaptureScreenshot(folder + "/" + name + ".png");

        private void SideShot(string name, Vector3 localOffset, float lookUp)
        {
            if (sideCam == null)
            {
                sideCam = new GameObject("Test Side Camera").AddComponent<Camera>();
                sideCam.enabled = false;
                sideCam.fieldOfView = 50f;
                sideCam.farClipPlane = 3000f;
                rt = new RenderTexture(1280, 720, 24);
            }
            Transform p = player.transform;
            Vector3 look = p.position + Vector3.up * lookUp;
            sideCam.transform.position = p.position + p.rotation * localOffset;
            sideCam.transform.rotation = Quaternion.LookRotation(look - sideCam.transform.position);
            sideCam.targetTexture = rt;
            sideCam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(folder + "/" + name + ".png", tex.EncodeToPNG());
            Destroy(tex);
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform c in player.GetComponentsInChildren<Transform>(true)) if (c.name == childName) return c;
            return null;
        }

        private void LogCanopy(string label)
        {
            Transform canopy = FindChild("Parachute Canopy");
            Transform bag = FindChild("Parachute Bag");
            if (canopy == null) { log.AppendLine(label + ": no canopy"); return; }
            string bagInfo = "";
            if (bag != null)
            {
                var r = bag.GetComponentsInChildren<Renderer>();
                if (r.Length > 0)
                {
                    Bounds b = r[0].bounds;
                    for (int i = 1; i < r.Length; i++) b.Encapsulate(r[i].bounds);
                    // The bag's own top (its local up, which is the character's up as worn), not the world box top.
                    Bounds lb = r[0].localBounds;
                    Vector3 top = r[0].transform.TransformPoint(lb.center + Vector3.up * lb.extents.y);
                    bagInfo = ", pivot-to-bag-top " + Vector3.Distance(canopy.position, top).ToString("F2") + " m (bag centre " + b.center.ToString("F1") + ")";
                }
            }
            log.AppendLine(label + ": canopy scale " + canopy.localScale.ToString("F2") + ", tilt " + canopy.localEulerAngles.ToString("F0") + bagInfo);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            player = GameObject.FindWithTag("Player");
            controller = player.GetComponent<Invector.vCharacterController.vThirdPersonController>();
            parachute = player.GetComponent<PlayerParachute>();
            var gp = player.GetComponent<InvectorGamePlayer>();
            log.AppendLine("player " + player.name);
            var startInteraction = player.GetComponent<PlayerVehicleInteraction>();
            if (startInteraction.State != VehicleInteractionState.OnFoot)
            {
                log.AppendLine("was in a vehicle: force exit " + startInteraction.ForceExitAt(player.transform.position + Vector3.up * 3f, player.transform.rotation));
                float w = 0f;
                while (startInteraction.State != VehicleInteractionState.OnFoot && w < 5f) { w += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(0.5f);
            }

            // ---------------- 1. high drop
            log.AppendLine("equip " + parachute.TryEquip());
            gp.Teleport(highDrop, 0f);
            float t = 0f;
            while (!parachute.CanDeploy && t < 5f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.2f);
            Rigidbody rb = player.GetComponent<Rigidbody>();
            log.AppendLine("high drop: fall speed before open " + rb.linearVelocity.y.ToString("F1"));
            stepLastVy = rb.linearVelocity.y;
            stepMaxDecel = 0f;
            measureVy = true;
            parachute.Deploy();
            float[] marks = { 0.05f, 0.2f, 0.4f, 0.65f, 1.0f };
            float since = 0f;
            int m = 0;
            float maxDv = 0f, lastVy = rb.linearVelocity.y;
            while (m < marks.Length)
            {
                yield return new WaitForFixedUpdate();
                since += Time.fixedDeltaTime;
                maxDv = Mathf.Max(maxDv, Mathf.Abs(rb.linearVelocity.y - lastVy) / Time.fixedDeltaTime);
                lastVy = rb.linearVelocity.y;
                if (since >= marks[m])
                {
                    yield return new WaitForEndOfFrame();
                    string tag = "deploy_t" + Mathf.RoundToInt(marks[m] * 100f).ToString("000");
                    LogCanopy(tag);
                    Shot("fix_para_high_" + tag + "_behind");
                    SideShot("fix_para_high_" + tag + "_side", new Vector3(9f, 1.5f, 0f), 2.5f);
                    SideShot("fix_para_high_" + tag + "_closeup", new Vector3(-2.2f, 1.6f, -2.4f), 1.6f);
                    m++;
                }
            }
            measureVy = false;
            log.AppendLine("  vy per step: " + vyTrace);
            log.AppendLine("open brake: max vertical decel per physics step " + stepMaxDecel.ToString("F1") + " m/s^2 (per sample " + maxDv.ToString("F1") + ")" + " m/s^2, vy now " + rb.linearVelocity.y.ToString("F1"));

            File.WriteAllText(folder + "/fix_test_log.partial.txt", log.ToString());
            // ---------------- 2. glide steering (and the wide glide camera)
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();
            Shot("fix_para_glide_camera_wide");
            float yaw0 = player.transform.eulerAngles.y, lastYaw = yaw0, maxYawAcc = 0f, lastRate = 0f;
            forcedInput = new Vector3(1f, 0f, 0f);
            forceInput = true;
            for (int i = 0; i < 100; i++) // 2 s right, then release
            {
                yield return new WaitForFixedUpdate();
                if (i == 60) forcedInput = Vector3.zero;
                float y = player.transform.eulerAngles.y;
                float rate = Mathf.DeltaAngle(lastYaw, y) / Time.fixedDeltaTime;
                if (i > 0) maxYawAcc = Mathf.Max(maxYawAcc, Mathf.Abs(rate - lastRate) / Time.fixedDeltaTime);
                lastRate = rate;
                lastYaw = y;
                if (i == 45) { yield return new WaitForEndOfFrame(); Shot("fix_para_glide_turn_behind"); SideShot("fix_para_glide_turn_side", new Vector3(0f, 2f, 10f), 2.5f); }
            }
            forceInput = false;
            log.AppendLine("glide turn: yaw " + yaw0.ToString("F0") + " -> " + player.transform.eulerAngles.y.ToString("F0") + ", max yaw accel " + maxYawAcc.ToString("F0") + " deg/s^2, rate after release " + lastRate.ToString("F1"));

            File.WriteAllText(folder + "/fix_test_log.partial.txt", log.ToString());
            if (!rooftopTest) { File.WriteAllText(folder + "/fix_test_log.txt", log.ToString()); Done = true; yield break; }
            // ---------------- 3. rooftop run-off
            Vector3 roof = Vector3.zero, edgeDir = Vector3.forward;
            float bestH = 0f;
            // A rooftop with a parachute pickup: stand on the parapet on the side where the street is 30 m+ below.
            Vector3 pickup = rooftopPickup;
            for (float s = 1f; s <= 14f; s += 0.25f)
            {
                if (!Physics.Raycast(pickup + edgeDir * s + Vector3.up * 6f, Vector3.down, out RaycastHit h2, 400f, 1, QueryTriggerInteraction.Ignore)) break;
                if (pickup.y - h2.point.y > 30f) break;
                roof = h2.point;
                bestH = h2.point.y;
            }
            roof -= edgeDir * 0.2f;
            if (bestH <= 0f) log.AppendLine("rooftop: no suitable roof found");
            else
            {
                log.AppendLine("rooftop " + roof.ToString("F1") + " height " + bestH.ToString("F0") + " equip " + parachute.TryEquip() + " state " + parachute.State);
                // Land first if still gliding from part 2.
                t = 0f;
                while (parachute.State == ParachuteState.Gliding && t < 60f) { t += Time.deltaTime; yield return null; }
                parachute.TryEquip();
                yield return new WaitForSeconds(0.5f);
                gp.Teleport(roof + Vector3.up * 0.1f, Quaternion.LookRotation(edgeDir).eulerAngles.y);
                yield return new WaitForSeconds(1f);
                Shot("fix_para_roof_a_standing");
                forcedInput = new Vector3(0f, 0f, 1f);
                forceInput = true;
                t = 0f;
                while (!parachute.CanDeploy && t < 6f) { t += Time.deltaTime; yield return null; }
                forceInput = false;
                log.AppendLine("rooftop: deploy available after " + t.ToString("F2") + " s, height " + player.transform.position.y.ToString("F0") + ", state " + parachute.State);
                yield return new WaitForSeconds(0.4f);
                parachute.Deploy();
                yield return new WaitForSeconds(0.25f);
                yield return new WaitForEndOfFrame();
                LogCanopy("roof t025");
                Shot("fix_para_roof_b_opening_behind");
                SideShot("fix_para_roof_b_opening_side", new Vector3(8f, 1f, 0f), 2.5f);
                yield return new WaitForSeconds(0.9f);
                yield return new WaitForEndOfFrame();
                LogCanopy("roof t115");
                Shot("fix_para_roof_c_open_behind");
                SideShot("fix_para_roof_c_open_side", new Vector3(8f, 1f, 0f), 2.5f);
                t = 0f;
                while (parachute.State == ParachuteState.Gliding && t < 60f) { t += Time.deltaTime; yield return null; }
                log.AppendLine("rooftop: landed after " + t.ToString("F1") + " s, hp " + controller.currentHealth + ", state " + parachute.State);
            }

            File.WriteAllText(folder + "/fix_test_log.partial.txt", log.ToString());
            if (!flightTests) { File.WriteAllText(folder + "/fix_test_log.txt", log.ToString()); Done = true; yield break; }
            // ---------------- 4. jet pitch feel
            var interaction = player.GetComponent<PlayerVehicleInteraction>();
            var plane = FindFirstObjectByType<PlaneController>();
            yield return new WaitForSeconds(1f);
            log.AppendLine("enter jet " + interaction.EnterInstant(plane));
            yield return new WaitForSeconds(1f);
            plane.PlaceAt(new Vector3(0f, 300f, -300f), 0f, 70f);
            plane.SetExternalInput(true, 0.7f);
            yield return new WaitForSeconds(1.5f);
            float maxAcc = 0f, maxRate = 0f, lastPr = plane.PitchRate, maxStepPitch = 0f, lastPitch = plane.Pitch;
            var trace = new StringBuilder();
            for (int i = 0; i < 300; i++) // 6 s: nose up 1.5 s, release 1.5 s, nose down 1.5 s, release 1.5 s
            {
                float cmd = i < 75 ? 1f : i < 150 ? 0f : i < 225 ? -1f : 0f;
                plane.SetExternalInput(true, 0.7f, 0f, cmd);
                yield return new WaitForFixedUpdate();
                float pr = plane.PitchRate;
                maxAcc = Mathf.Max(maxAcc, Mathf.Abs(pr - lastPr) / Time.fixedDeltaTime);
                maxRate = Mathf.Max(maxRate, Mathf.Abs(pr));
                maxStepPitch = Mathf.Max(maxStepPitch, Mathf.Abs(plane.Pitch - lastPitch));
                lastPr = pr;
                lastPitch = plane.Pitch;
                if (i % 15 == 0) trace.Append(i * 0.02f).Append("s:").Append(plane.Pitch.ToString("F1")).Append("/").Append(pr.ToString("F1")).Append(" ");
                if (i == 60) { yield return new WaitForEndOfFrame(); Shot("fix_jet_pitch_up_chase"); }
                if (i == 210) { yield return new WaitForEndOfFrame(); Shot("fix_jet_pitch_down_chase"); }
            }
            log.AppendLine("jet pitch: max rate " + maxRate.ToString("F1") + " deg/s, max pitch accel " + maxAcc.ToString("F1") + " deg/s^2, max change per step " + maxStepPitch.ToString("F2") + " deg");
            log.AppendLine("  trace (t: pitch/rate) " + trace);

            // ---------------- 5. boundary turn-back
            var boundary = FindFirstObjectByType<WorldBoundary>();
            Rect play = boundary.PlayArea;
            plane.PlaceAt(new Vector3(play.xMax - 150f, 350f, play.center.y), 90f, 80f);
            plane.SetExternalInput(true, 1f);
            float maxX = 0f;
            bool warned = false, shotWarn = false;
            t = 0f;
            float yawStart = plane.transform.eulerAngles.y;
            while (t < 25f)
            {
                t += Time.deltaTime;
                maxX = Mathf.Max(maxX, plane.transform.position.x);
                if (boundary.PlayerOutside) warned = true;
                if (warned && !shotWarn && t > 4f) { shotWarn = true; yield return new WaitForEndOfFrame(); Shot("fix_jet_boundary_turnback_warning"); }
                if (t > 6f && Mathf.Abs(Mathf.DeltaAngle(plane.transform.eulerAngles.y, 270f)) < 30f && !boundary.PlayerOutside) break;
                yield return null;
            }
            log.AppendLine("boundary: soft edge x " + play.xMax.ToString("F0") + ", hard wall x " + boundary.HardArea.xMax.ToString("F0") + ", max x reached " + maxX.ToString("F0") +
                           " (" + (maxX - play.xMax).ToString("F0") + " m past soft edge), heading " + yawStart.ToString("F0") + " -> " + plane.transform.eulerAngles.y.ToString("F0") +
                           " after " + t.ToString("F1") + " s, warning shown " + warned + ", warning now " + boundary.PlayerOutside + ", wrecked " + plane.IsWrecked);
            Shot("fix_jet_boundary_turned_back");
            yield return null;

            // ---------------- 6. ceiling
            plane.PlaceAt(new Vector3(0f, 690f, 0f), 0f, 80f);
            float maxY = 0f;
            for (int i = 0; i < 400; i++)
            {
                plane.SetExternalInput(true, 1f, 0f, 1f);
                yield return new WaitForFixedUpdate();
                maxY = Mathf.Max(maxY, plane.transform.position.y);
            }
            log.AppendLine("ceiling: full nose-up from 690 m, max height " + maxY.ToString("F0") + " (soft " + boundary.SoftCeiling + ", lid " + boundary.HardCeiling + ")");
            plane.SetExternalInput(false);

            File.WriteAllText(folder + "/fix_test_log.txt", log.ToString());
            if (sideCam != null) Destroy(sideCam.gameObject);
            if (rt != null) rt.Release();
            Done = true;
        }

        // Per physics step (not per coroutine resume, which skips steps on slow screenshot frames).
        private bool measureVy;
        private readonly StringBuilder vyTrace = new StringBuilder();
        private float stepMaxDecel, stepLastVy;
        private void FixedUpdate()
        {
            if (!measureVy || player == null) return;
            float vy = player.GetComponent<Rigidbody>().linearVelocity.y;
            stepMaxDecel = Mathf.Max(stepMaxDecel, Mathf.Abs(vy - stepLastVy) / Time.fixedDeltaTime);
            if (vyTrace.Length < 1200) vyTrace.Append(vy.ToString("F1")).Append(parachute.IsGliding ? "g " : "f ");
            stepLastVy = vy;
        }

        private void LateUpdate()
        {
            if (forceInput && controller != null) controller.input = forcedInput;
        }
    }
}
