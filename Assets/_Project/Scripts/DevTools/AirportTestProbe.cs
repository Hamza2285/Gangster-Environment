using System.Collections;
using System.IO;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev probe (play mode only; render stats in the editor): walks the played character from the beach over the causeway to the airport, then
    /// gets into one of the airport's cars and drives it back along the causeway with RCC's external input, sampling render
    /// stats (batches, set-pass calls, triangles, frame time) at the airport. Screenshots + log to Captures/Airport.
    /// Start: AirportTestProbe.Begin() while playing.
    /// </summary>
    public class AirportTestProbe : MonoBehaviour
    {
        public bool Done { get; private set; }
        private readonly StringBuilder log = new StringBuilder();
        private vThirdPersonController controller;
        private bool walking;

        public static void Begin() => new GameObject("AirportTestProbe").AddComponent<AirportTestProbe>();

        private IEnumerator Start()
        {
            Directory.CreateDirectory("Captures/Airport");
            GameObject player = GameObject.FindWithTag("Player");
            controller = player.GetComponent<vThirdPersonController>();
            var gp = player.GetComponent<InvectorGamePlayer>();

            // Walk from the beach end of the causeway road towards the airport.
            gp.Teleport(new Vector3(-350f, 27.8f, -134f), 270f);
            yield return new WaitForSeconds(1f);
            walking = true;
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(3f);
                log.AppendLine($"walk {i * 3 + 3,2} s: {player.transform.position}  grounded {controller.isGrounded}  hp {controller.currentHealth}");
                if (i == 2) ScreenCapture.CaptureScreenshot("Captures/Airport/play_a_walk_causeway.png");
            }
            walking = false;

            // Around the airport: stats from a few spots.
            Vector3[] spots = { new Vector3(-505f, 27.8f, -90f), new Vector3(-630f, 27.8f, -250f), new Vector3(-690f, 27.8f, -134f) };
            float[] yaws = { 300f, 90f, 0f };
            string[] names = { "terminal", "hangars", "runway" };
            for (int i = 0; i < spots.Length; i++)
            {
                gp.Teleport(spots[i], yaws[i]);
                yield return new WaitForSeconds(2f);
                yield return Sample(names[i]);
                ScreenCapture.CaptureScreenshot("Captures/Airport/play_b_" + names[i] + ".png");
                yield return null;
            }

            // Drive: one of the airport cars, back along the causeway (RCC external input, full throttle).
            Transform cars = GameObject.Find("Airport/Parked Cars") != null ? GameObject.Find("Airport/Parked Cars").transform : null;
            var adapter = cars != null ? cars.GetComponentInChildren<OpenworldCrime.Vehicles.RCCVehicleAdapter>() : null;
            var interaction = player.GetComponent<PlayerVehicleInteraction>();
            if (adapter != null)
            {
                var carController = adapter.GetComponent<RCC_CarControllerV3>();
                var body = adapter.GetComponent<Rigidbody>();
                adapter.SetDormant(false);
                body.isKinematic = false;
                adapter.transform.SetPositionAndRotation(new Vector3(-500f, 28.4f, -134f), Quaternion.Euler(0f, 90f, 0f));
                body.position = adapter.transform.position;
                body.rotation = adapter.transform.rotation;
                yield return new WaitForSeconds(0.5f);
                Transform enter = adapter.GetEnterPoint();
                gp.Teleport(enter.position - enter.forward * 0.6f, enter.eulerAngles.y);
                yield return new WaitForSeconds(0.5f);
                log.AppendLine("enter car: " + interaction.RequestEnter(adapter));
                float t = 0f;
                while (interaction.State != VehicleInteractionState.InVehicle && t < 10f) { t += Time.deltaTime; yield return null; }
                log.AppendLine("in car: " + interaction.State);
                yield return new WaitForSeconds(1f);
                carController.externalController = true;
                carController.throttleInput = 0.8f;
                carController.steerInput = 0f;
                carController.brakeInput = 0f;
                carController.handbrakeInput = 0f;
                for (int i = 0; i < 5; i++)
                {
                    yield return new WaitForSeconds(2f);
                    // Keep it straight on the road (z -134).
                    carController.steerInput = Mathf.Clamp((adapter.transform.position.z + 134f) * 0.15f, -0.4f, 0.4f) * (adapter.transform.forward.x > 0f ? 1f : -1f);
                    log.AppendLine($"drive {i * 2 + 2,2} s: {adapter.transform.position}  {carController.speed:F0} km/h");
                    if (i == 2) ScreenCapture.CaptureScreenshot("Captures/Airport/play_c_drive_causeway.png");
                }
                carController.throttleInput = 0f;
                carController.brakeInput = 1f;
                yield return new WaitForSeconds(3f);
                carController.brakeInput = 0f;
                carController.externalController = false;
                log.AppendLine("exit car: " + interaction.RequestExit());
            }
            else log.AppendLine("no airport car found");

            File.WriteAllText("Captures/Airport/play_log.txt", log.ToString());
            Done = true;
        }

        private IEnumerator Sample(string name)
        {
            float time = 0f;
            int frames = 0;
            int batches = 0, setPass = 0, tris = 0;
            while (time < 1.5f)
            {
                time += Time.unscaledDeltaTime;
                frames++;
#if UNITY_EDITOR
                batches = Mathf.Max(batches, UnityEditor.UnityStats.batches);
                setPass = Mathf.Max(setPass, UnityEditor.UnityStats.setPassCalls);
                tris = Mathf.Max(tris, UnityEditor.UnityStats.triangles);
#endif
                yield return null;
            }
            log.AppendLine($"{name}: {frames / time:F0} fps (editor), max batches {batches}, set-pass {setPass}, triangles {tris / 1000}k");
        }

        private void LateUpdate()
        {
            if (walking && controller != null)
            {
                controller.input = new Vector3(0f, 0f, 1f);
                controller.isSprinting = true;
            }
        }
    }
}
