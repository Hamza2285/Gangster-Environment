using System.Collections;
using System.IO;
using System.Text;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only, added by hand / MCP, never saved in a scene): walks the played character to the jet, gets in,
    /// taxis, moves to the runway start, takes off, climbs, turns, stalls and recovers, flies a scripted approach and lands,
    /// brakes and gets out; then (optional) a mid-air exit. Flight inputs go through <see cref="PlaneController.SetExternalInput"/>.
    /// Screenshots + log to <see cref="folder"/>.
    /// </summary>
    public class PlaneTestRunner : MonoBehaviour
    {
        public string folder = "Captures/Fixes3";
        public string prefix = "plane";
        public Vector3 runwayStart = new Vector3(-705f, 27.75f, -440f);
        public float runwayHeading = 0f;
        public bool airExit = true;
        [Tooltip("Only the mid-air exit with a parachute bag (enters at once, starts in the air).")]
        public bool onlyAirExit;
        public bool Done { get; private set; }
        public string Log => log.ToString();

        private readonly StringBuilder log = new StringBuilder();
        private PlaneController plane;

        private void Shot(string name) => ScreenCapture.CaptureScreenshot(folder + "/" + prefix + "_" + name + ".png");

        private float Pitch => -Mathf.Asin(Mathf.Clamp(plane.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg * -1f;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            GameObject player = GameObject.FindWithTag("Player");
            var interaction = player.GetComponent<PlayerVehicleInteraction>();
            plane = FindFirstObjectByType<PlaneController>();
            var readout = FindFirstObjectByType<OpenworldCrime.UI.VehicleSpeedReadout>(FindObjectsInactive.Include);
            if (onlyAirExit)
            {
                log.AppendLine("bag: " + player.GetComponent<PlayerParachute>().TryEquip());
                log.AppendLine("enter instant: " + interaction.EnterInstant(plane));
                yield return new WaitForSeconds(1f);
                yield return AirExit(player, interaction);
                File.WriteAllText(folder + "/" + prefix + "_airexit_log.txt", log.ToString());
                Done = true;
                yield break;
            }
            Transform enter = plane.GetEnterPoint();
            player.GetComponent<InvectorGamePlayer>().Teleport(enter.position - enter.forward * 1.5f, enter.eulerAngles.y);
            yield return new WaitForSeconds(1f);
            Shot("a_at_door");
            yield return null;
            log.AppendLine("focus " + (interaction.FocusedVehicle != null ? interaction.FocusedVehicle.transform.name : "none") + ", can enter " + plane.CanEnter);
            bool requested = interaction.RequestEnter(plane);
            float t = 0f;
            bool boardingShot = false;
            while (interaction.State != VehicleInteractionState.InVehicle && t < 12f)
            {
                t += Time.deltaTime;
                if (!boardingShot && t > 1.2f) { Shot("b_boarding"); boardingShot = true; }
                yield return null;
            }
            log.AppendLine("enter requested " + requested + ", state " + interaction.State + " after " + t.ToString("F1") + " s, camera " + (plane.GetActiveCamera() != null));
            yield return new WaitForSeconds(2f);
            Shot("c_seated_idle");
            log.AppendLine("car KM/H readout visible while in plane: " + (readout != null && readout.isActiveAndEnabled));

            // Taxi: a quarter throttle with a turn.
            float yaw0 = plane.transform.eulerAngles.y;
            plane.SetExternalInput(true, 0.35f, 0.6f);
            yield return new WaitForSeconds(5f);
            log.AppendLine("taxi: speed " + plane.CurrentSpeed.ToString("F1") + " m/s, turned " + Mathf.DeltaAngle(yaw0, plane.transform.eulerAngles.y).ToString("F0") + " deg, grounded " + plane.IsGrounded);
            Shot("d_taxi");
            plane.SetExternalInput(true, 0f);
            yield return new WaitForSeconds(3f);
            log.AppendLine("braked: speed " + plane.CurrentSpeed.ToString("F1"));

            // Exit on the apron (stopped), take a parachute bag, get back in.
            plane.SetExternalInput(false);
            log.AppendLine("apron: can exit " + plane.CanExit + ", exit " + interaction.RequestExit());
            t = 0f;
            while (interaction.State != VehicleInteractionState.OnFoot && t < 10f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1f);
            Shot("d2_exited_apron");
            log.AppendLine("apron exit: state " + interaction.State + " after " + t.ToString("F1") + " s, player " + player.transform.position);
            var parachute = player.GetComponent<PlayerParachute>();
            log.AppendLine("bag for the mid-air exit: " + parachute.TryEquip());
            interaction.RequestEnter(plane);
            t = 0f;
            while (interaction.State != VehicleInteractionState.InVehicle && t < 12f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("re-entered: " + interaction.State + " after " + t.ToString("F1") + " s");

            // Runway: full throttle, pull up at takeoff speed.
            plane.PlaceAt(runwayStart, runwayHeading);
            yield return new WaitForFixedUpdate();
            Vector3 start = plane.transform.position;
            plane.SetExternalInput(true, 1f);
            t = 0f;
            while (plane.CurrentSpeed < 43f && t < 30f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("takeoff roll: " + plane.CurrentSpeed.ToString("F1") + " m/s after " + t.ToString("F1") + " s, " + (plane.transform.position - start).magnitude.ToString("F0") + " m");
            Shot("e_takeoff_roll");
            plane.SetExternalInput(true, 1f, 0f, 1f);
            t = 0f;
            while (plane.IsGrounded && t < 6f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("lift-off " + !plane.IsGrounded + " after pulling up " + t.ToString("F1") + " s, distance " + (plane.transform.position - start).magnitude.ToString("F0") + " m");
            yield return new WaitForSeconds(1f);
            Shot("f_liftoff");
            yield return new WaitForSeconds(3f);
            plane.SetExternalInput(true, 1f, 0f, 0f);
            yield return new WaitForSeconds(3f);
            log.AppendLine("climb: alt " + plane.HeightAboveGround.ToString("F0") + " m, speed " + plane.CurrentSpeed.ToString("F1") + ", pitch " + Pitch.ToString("F1"));
            Shot("g_climb_hud");

            // Turn.
            yaw0 = plane.transform.eulerAngles.y;
            plane.SetExternalInput(true, 0.9f, 0.7f, 0.2f);
            yield return new WaitForSeconds(5f);
            log.AppendLine("bank turn: turned " + Mathf.DeltaAngle(yaw0, plane.transform.eulerAngles.y).ToString("F0") + " deg in 5 s, bank " + (-plane.transform.eulerAngles.z).ToString("F0") + ", alt " + plane.HeightAboveGround.ToString("F0"));
            Shot("h_turn");
            plane.SetExternalInput(true, 0.9f);
            yield return new WaitForSeconds(3f);
            log.AppendLine("auto level: bank " + Mathf.DeltaAngle(0f, plane.transform.eulerAngles.z).ToString("F1") + ", pitch " + Pitch.ToString("F1"));

            // Stall and recovery.
            plane.SetExternalInput(true, 0f, 0f, 1f);
            t = 0f;
            while (!plane.IsStalled && t < 20f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("stall " + plane.IsStalled + " after " + t.ToString("F1") + " s, speed " + plane.CurrentSpeed.ToString("F1") + ", alt " + plane.HeightAboveGround.ToString("F0"));
            yield return new WaitForSeconds(1.5f);
            Shot("i_stall");
            plane.SetExternalInput(true, 1f);
            t = 0f;
            while (plane.IsStalled && t < 15f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("recovered " + !plane.IsStalled + " after " + t.ToString("F1") + " s, alt " + plane.HeightAboveGround.ToString("F0") + ", wrecked " + plane.IsWrecked);

            // Approach: put it 22 m over the runway threshold at 48 m/s, hold ~3 m/s sink, flare, touch down, brake.
            plane.PlaceAt(runwayStart + Vector3.up * 22f, runwayHeading, 48f);
            yield return new WaitForFixedUpdate();
            t = 0f;
            float maxSink = 0f;
            while (!plane.IsGrounded && !plane.IsWrecked && t < 30f)
            {
                t += Time.deltaTime;
                Rigidbody rb = plane.GetComponent<Rigidbody>();
                float sink = -rb.linearVelocity.y;
                float target = plane.HeightAboveGround > 3f ? -3f : -0.8f;
                float pitchIn = Mathf.Clamp((-target - sink) * -0.25f, -1f, 1f);
                plane.SetExternalInput(true, plane.HeightAboveGround > 3f ? 0.35f : 0.15f, 0f, pitchIn);
                maxSink = Mathf.Max(maxSink, sink);
                yield return null;
            }
            log.AppendLine("touchdown " + plane.IsGrounded + " after " + t.ToString("F1") + " s, wrecked " + plane.IsWrecked + ", speed " + plane.CurrentSpeed.ToString("F1"));
            Shot("j_touchdown");
            plane.SetExternalInput(true, 0f);
            t = 0f;
            while (plane.CurrentSpeed > 0.5f && t < 25f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("stopped after " + t.ToString("F1") + " s; can exit " + plane.CanExit);
            plane.SetExternalInput(false);
            log.AppendLine("exit " + interaction.RequestExit());
            t = 0f;
            while (interaction.State != VehicleInteractionState.OnFoot && t < 10f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
            Shot("k_exited");
            log.AppendLine("state " + interaction.State + " after " + t.ToString("F1") + " s, player " + player.transform.position + ", plane " + plane.transform.position);

            if (airExit)
            {
                interaction.RequestEnter(plane);
                t = 0f;
                while (interaction.State != VehicleInteractionState.InVehicle && t < 12f) { t += Time.deltaTime; yield return null; }
                yield return AirExit(player, interaction);
            }
            File.WriteAllText(folder + "/" + prefix + "_log.txt", log.ToString());
            Done = true;
        }

        // Out at 200 m: with a parachute bag the stable fall and the parachute, without one the screaming ragdoll fall.
        private IEnumerator AirExit(GameObject player, PlayerVehicleInteraction interaction)
        {
            plane.PlaceAt(runwayStart + new Vector3(0f, 200f, 200f), runwayHeading, 60f);
            plane.SetExternalInput(true, 0.7f);
            yield return new WaitForSeconds(1.5f);
            log.AppendLine("air: can exit " + plane.CanExit + ", air jump allowed " + plane.AllowsAirJump);
            log.AppendLine("air exit " + interaction.RequestExit() + ", state " + interaction.State);
            yield return new WaitForSeconds(0.8f);
            Shot("l_air_exit");
            yield return new WaitForSeconds(2.5f);
            Shot("m_falling");
            var chute = player.GetComponent<PlayerParachute>();
            log.AppendLine("falling: parachute " + chute.State + ", can deploy " + chute.CanDeploy + ", state " + interaction.State + ", y " + player.transform.position.y.ToString("F0"));
            chute.Deploy();
            yield return new WaitForSeconds(1.2f);
            log.AppendLine("deployed from the jump: " + chute.State);
            Shot("n_parachute_after_jump");
            yield return new WaitForSeconds(4f);
            log.AppendLine("abandoned plane: alt " + plane.HeightAboveGround.ToString("F0") + ", speed " + plane.CurrentSpeed.ToString("F0") + ", wrecked " + plane.IsWrecked);
        }
    }
}
