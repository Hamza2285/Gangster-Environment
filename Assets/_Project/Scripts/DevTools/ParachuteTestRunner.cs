using System.Collections;
using System.IO;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only, added by hand / MCP, never saved in a scene): runs one parachute jump on the played character
    /// and writes screenshots + a log to Captures/Parachute. Equips the bag, drops the character from <see cref="dropPoint"/>,
    /// waits for the parachute button, opens it, glides (optional steering by overriding Invector's input) and lands.
    /// </summary>
    public class ParachuteTestRunner : MonoBehaviour
    {
        public Vector3 dropPoint = new Vector3(-222f, 145f, -26.5f);
        public float dropYaw = 180f;
        public float steer = 0.4f;
        public string prefix = "run";
        public bool Done { get; private set; }
        public string Log => log.ToString();

        private readonly StringBuilder log = new StringBuilder();
        private GameObject player;
        private vThirdPersonController controller;
        private PlayerParachute parachute;
        private bool steering;

        private IEnumerator Start()
        {
            player = GameObject.FindWithTag("Player");
            controller = player.GetComponent<vThirdPersonController>();
            parachute = player.GetComponent<PlayerParachute>();
            Directory.CreateDirectory("Captures/Parachute");
            float hp0 = controller.currentHealth;
            log.AppendLine("player " + player.name + " hp " + hp0 + " equip " + parachute.TryEquip() + " state " + parachute.State);
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_a_bag_on_back.png");
            yield return null;
            player.GetComponent<InvectorGamePlayer>().Teleport(dropPoint, dropYaw);
            float t = 0f;
            while (!parachute.CanDeploy && t < 5f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("can deploy after " + t.ToString("F2") + " s at y " + player.transform.position.y.ToString("F1"));
            yield return new WaitForSeconds(0.25f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_b_falling_button.png");
            yield return null;
            yield return new WaitForSeconds(0.6f);
            parachute.Deploy();
            log.AppendLine("deployed: " + parachute.State + " at y " + player.transform.position.y.ToString("F1") + " v " + player.GetComponent<Rigidbody>().linearVelocity);
            yield return new WaitForSeconds(0.35f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_c_opening.png");
            yield return new WaitForSeconds(1.8f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_d_gliding.png");
            log.AppendLine("gliding v " + player.GetComponent<Rigidbody>().linearVelocity + " y " + player.transform.position.y.ToString("F1"));
            float yawBefore = player.transform.eulerAngles.y;
            steering = true;
            yield return new WaitForSeconds(2.5f);
            log.AppendLine("steered: yaw " + yawBefore.ToString("F0") + " -> " + player.transform.eulerAngles.y.ToString("F0"));
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_e_turning.png");
            steering = false;
            t = 0f;
            while (parachute.State == ParachuteState.Gliding && t < 60f) { t += Time.deltaTime; yield return null; }
            log.AppendLine("landed after " + t.ToString("F1") + " s more at " + player.transform.position + " state " + parachute.State);
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_f_landed.png");
            yield return new WaitForSeconds(1.2f);
            ScreenCapture.CaptureScreenshot("Captures/Parachute/" + prefix + "_g_canopy_faded.png");
            Transform bag = null;
            foreach (Transform c in player.GetComponentsInChildren<Transform>(true)) if (c.name == "Parachute Bag") bag = c;
            log.AppendLine("hp " + controller.currentHealth + " (was " + hp0 + "), bag " + (bag == null ? "none" : bag.gameObject.activeSelf ? "ON BACK" : "used up") +
                           ", grounded " + controller.isGrounded + ", lockMovement " + controller.lockMovement + ", gravity " + player.GetComponent<Rigidbody>().useGravity);
            File.WriteAllText("Captures/Parachute/" + prefix + "_log.txt", log.ToString());
            Done = true;
        }

        // Steering test: overrides Invector's move input after it is read (Invector reads input in Update).
        private void LateUpdate()
        {
            if (steering && controller != null) controller.input = new Vector3(steer, 0f, 0.3f);
        }
    }
}
