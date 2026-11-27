using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Parachute;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only, added by hand / MCP, never saved in a scene): parachute pickup distance (stands the played
    /// character 1.3 m then 0.6 m from a bag), parachute open sound (drop from height, open), letting go of the canopy (ragdoll
    /// fall: counts the screams) and the hard landing (blood event). Screenshots + log to <see cref="folder"/>.
    /// </summary>
    public class FallTestRunner : MonoBehaviour
    {
        public string folder = "Captures/Fixes3";
        public string prefix = "fall";
        public Vector3 dropPoint = new Vector3(-640f, 190f, -200f);
        public bool Done { get; private set; }
        public string Log => log.ToString();

        private readonly StringBuilder log = new StringBuilder();
        private int hardLandings;
        private float lastSeverity;

        private void Shot(string name) => ScreenCapture.CaptureScreenshot(folder + "/" + prefix + "_" + name + ".png");

        private void OnEnable() => PlayerFreeFall.OnHardLanding += HandleLanding;
        private void OnDisable() => PlayerFreeFall.OnHardLanding -= HandleLanding;

        private void HandleLanding(PlayerFreeFall faller, Vector3 point, float severity)
        {
            hardLandings++;
            lastSeverity = severity;
            log.AppendLine($"  [t {Time.time:F1}] hard landing at {point}, severity {severity:F2}");
            Shot("d_impact_blood");
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            GameObject player = GameObject.FindWithTag("Player");
            var game = player.GetComponent<InvectorGamePlayer>();
            var parachute = player.GetComponent<PlayerParachute>();
            var fall = player.GetComponent<PlayerFreeFall>();
            var controller = player.GetComponent<vThirdPersonController>();
            log.AppendLine("player " + player.name);

            // --- pickup distance
            ParachutePickup pickup = FindFirstObjectByType<ParachutePickup>();
            Vector3 bag = pickup.transform.position;
            game.Teleport(bag + new Vector3(1.3f, 0.1f, 0f), -90f);
            yield return new WaitForSeconds(1.2f);
            log.AppendLine($"1.3 m from the bag (inside its 2.2 m trigger): taken {!pickup.IsAvailable}, state {parachute.State}");
            Shot("a_pickup_1m3_not_taken");
            game.Teleport(bag + new Vector3(0.6f, 0.1f, 0f), -90f);
            yield return new WaitForSeconds(1.2f);
            log.AppendLine($"0.6 m from the bag: taken {!pickup.IsAvailable}, state {parachute.State}");

            // --- drop with the bag, open the parachute (sound), let go (ragdoll + screams), land (blood)
            if (parachute.State != ParachuteState.Equipped) log.AppendLine("equip directly: " + parachute.TryEquip());
            game.Teleport(dropPoint, 0f);
            float t = 0f;
            while (!parachute.CanDeploy && t < 5f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.8f);
            parachute.Deploy();
            yield return null;
            var wind = typeof(PlayerParachute).GetField("windSource", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(parachute) as AudioSource;
            var openClip = typeof(PlayerParachute).GetField("openClip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(parachute) as AudioClip;
            log.AppendLine($"deploy: state {parachute.State}, open clip {(openClip != null ? openClip.name : "none")}, character source playing {(wind != null && wind.isPlaying)}");
            yield return new WaitForSeconds(0.6f);
            Shot("b_parachute_open");
            yield return new WaitForSeconds(2.5f);

            FieldInfo tokensField = typeof(PlayerFreeFall).GetField("screamTokens", BindingFlags.NonPublic | BindingFlags.Instance);
            int landingsBefore = hardLandings;
            parachute.Release();
            float releaseY = player.transform.position.y;
            log.AppendLine($"released the canopy at y {releaseY:F0}: ragdoll {fall.IsRagdollFalling}");
            t = 0f;
            int lastTotal = 0;
            bool fallShot = false;
            while ((fall.IsRagdollFalling || t < 0.5f) && t < 20f && hardLandings == landingsBefore)
            {
                t += Time.deltaTime;
                int[] tokens = (int[])tokensField.GetValue(fall);
                int total = tokens[0] + tokens[1];
                if (total != lastTotal) { log.AppendLine($"  [t {t:F2}] scream #{total}"); lastTotal = total; }
                if (!fallShot && t > 2f) { Shot("c_falling_screaming"); fallShot = true; }
                yield return null;
            }
            log.AppendLine($"fell {t:F1} s with {lastTotal} screams; hard landings {hardLandings}, severity {lastSeverity:F2}");
            yield return new WaitForSeconds(3.5f);
            Shot("e_blood_after");
            log.AppendLine($"after landing: dead {controller.isDead}, health {controller.currentHealth:F0}");
            File.WriteAllText(folder + "/" + prefix + "_log.txt", log.ToString());
            Done = true;
        }
    }
}
