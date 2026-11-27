using System.Collections;
using System.IO;
using System.Text;
using OpenworldCrime.Controls;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only, added by hand / MCP, never saved in a scene): walks the played character up to the first
    /// helicopter, gets in, takes off, flies forward and turns, lands and gets out, pressing the helicopter's touch inputs
    /// (switches to mobile controls for the run). Screenshots + log to Captures/Helicopter.
    /// </summary>
    public class HelicopterTestRunner : MonoBehaviour
    {
        public string prefix = "heli";
        public bool Done { get; private set; }
        public string Log => log.ToString();
        private readonly StringBuilder log = new StringBuilder();

        private IEnumerator Start()
        {
            Directory.CreateDirectory("Captures/Helicopter");
            GameObject player = GameObject.FindWithTag("Player");
            var interaction = player.GetComponent<PlayerVehicleInteraction>();
            var heli = FindFirstObjectByType<HelicopterController>();
            var mode = FindFirstObjectByType<ControlsModeManager>();
            Transform enter = heli.GetEnterPoint();
            player.GetComponent<InvectorGamePlayer>().Teleport(enter.position - enter.forward * 1.2f, enter.eulerAngles.y);
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_a_prompt.png");
            yield return null;
            log.AppendLine("focus " + (interaction.FocusedVehicle != null ? interaction.FocusedVehicle.transform.name : "none") + ", can enter " + heli.CanEnter);
            bool requested = interaction.RequestEnter(heli);
            float t = 0f;
            while (interaction.State != VehicleInteractionState.InVehicle && t < 10f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("enter requested " + requested + ", state " + interaction.State + " after " + t.ToString("F1") + " s, camera " + (heli.GetActiveCamera() != null));
            log.AppendLine("mobile mode " + mode.IsMobile + " (flight inputs scripted through SetExternalInput)");
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_b_seated_spoolup.png");
            yield return new WaitForSeconds(2.5f);
            log.AppendLine("rotor " + heli.RotorSpeed01.ToString("F2") + " grounded " + heli.IsGrounded);

            heli.SetExternalInput(true, 0f, 0f, 1f);
            yield return new WaitForSeconds(4f);
            heli.SetExternalInput(true);
            log.AppendLine("after climb: height " + heli.HeightAboveGround.ToString("F1") + " y " + heli.transform.position.y.ToString("F1"));
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_c_takeoff.png");
            yield return new WaitForSeconds(1.5f);
            float hoverY = heli.transform.position.y;
            yield return new WaitForSeconds(1.5f);
            log.AppendLine("hover drift " + (heli.transform.position.y - hoverY).ToString("F2") + " m in 1.5 s, speed " + heli.CurrentSpeed.ToString("F1"));
            log.AppendLine("can exit in the air: " + heli.CanExit);

            heli.SetExternalInput(true, 1f, 0.35f);
            yield return new WaitForSeconds(4f);
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_d_forward_turn.png");
            log.AppendLine("forward: speed " + heli.CurrentSpeed.ToString("F1") + " m/s, tilt " + heli.transform.eulerAngles.x.ToString("F1") + "/" + heli.transform.eulerAngles.z.ToString("F1"));
            yield return new WaitForSeconds(3f);
            heli.SetExternalInput(true);
            yield return new WaitForSeconds(4f);
            log.AppendLine("released stick: speed " + heli.CurrentSpeed.ToString("F1") + ", roll " + heli.transform.eulerAngles.z.ToString("F1"));
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_e_hover.png");

            heli.SetExternalInput(true, 0f, 0f, -1f);
            t = 0f;
            while (!heli.IsGrounded && t < 25f) { t += Time.deltaTime; yield return null; }
            heli.SetExternalInput(false);
            log.AppendLine("landed " + heli.IsGrounded + " after " + t.ToString("F1") + " s at " + heli.transform.position + ", health " + (heli.GetComponent<VehicleHealth>() != null ? heli.GetComponent<VehicleHealth>().CurrentHealth.ToString("F0") : "-"));
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_f_landed.png");
            log.AppendLine("can exit " + heli.CanExit + ", exit " + interaction.RequestExit());
            t = 0f;
            while (interaction.State != VehicleInteractionState.OnFoot && t < 8f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot("Captures/Helicopter/" + prefix + "_g_exited.png");
            log.AppendLine("state " + interaction.State + ", player " + player.transform.position + ", rotor " + heli.RotorSpeed01.ToString("F2"));
            File.WriteAllText("Captures/Helicopter/" + prefix + "_log.txt", log.ToString());
            Done = true;
        }
    }
}
