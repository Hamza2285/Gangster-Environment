using System.Collections;
using System.Reflection;
using Invector.vCamera;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only: runs the gait tests for the current character one after the other on an open road —
    /// "before" (shared 2 / 4 / 6 m/s, normal playback) and "after" (the character's stride profile) at walk, half stick,
    /// run and sprint — each logged by a <see cref="GaitProbe"/> to Captures/PlayerFeel/g_[id]_[gait]_[mode].csv.
    /// Added at runtime only.
    /// </summary>
    public class GaitTestRunner : MonoBehaviour
    {
        public Vector3 start = new Vector3(540f, 27.6f, 195f);
        public float heading = 90f;
        public string[] modes = { "before", "after" };
        public string[] gaits = { "walk", "half", "run", "sprint" };
        public float moveSeconds = 4.6f;
        [Tooltip("Controller used for the 'before' runs (e.g. a copy with the original clips); null = keep the current one.")]
        public RuntimeAnimatorController beforeController;
        public bool Done { get; private set; }

        private IEnumerator Start()
        {
            var pc = GetComponent<PlayableCharacter>();
            var cc = GetComponent<vThirdPersonController>();
            var feel = FindObjectOfType<PlayerFeelSettings>();
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo match = typeof(PlayerFeelSettings).GetField("matchStride", bf);
            var animator = GetComponent<Animator>();
            RuntimeAnimatorController current = animator.runtimeAnimatorController;
            while (!cc.enabled || transform.parent != null) yield return null; // wait until on foot (not in a vehicle)
            yield return new WaitForSeconds(1f);
            foreach (string mode in modes)
            {
                foreach (string gait in gaits)
                {
                    Time.timeScale = 1f;
                    match.SetValue(feel, mode == "after");
                    feel.Apply();
                    RuntimeAnimatorController want = mode == "before" && beforeController != null ? beforeController : current;
                    if (animator.runtimeAnimatorController != want) animator.runtimeAnimatorController = want;
                    if (mode == "before") { cc.freeSpeed.walkSpeed = 2f; cc.freeSpeed.runningSpeed = 4f; cc.freeSpeed.sprintSpeed = 6f; }
                    GetComponent<InvectorGamePlayer>().Teleport(start, heading);
                    foreach (vThirdPersonCamera cam in Resources.FindObjectsOfTypeAll<vThirdPersonCamera>())
                        if (cam.gameObject.activeInHierarchy)
                        {
                            typeof(vThirdPersonCamera).GetProperty("mouseX", bf).SetValue(cam, heading, null);
                            typeof(vThirdPersonCamera).GetProperty("mouseY", bf).SetValue(cam, 5f, null);
                        }
                    yield return new WaitForSeconds(0.3f);
                    var lp = gameObject.AddComponent<LocomotionProbe>();
                    lp.idleSeconds = 0.6f; lp.moveSeconds = moveSeconds; lp.stopSeconds = 0.3f;
                    lp.inputAmount = gait == "walk" ? 0.5f : gait == "half" ? 0.75f : 1f;
                    lp.sprint = gait == "sprint";
                    lp.outputPath = "Captures/PlayerFeel/loco_tmp.csv";
                    var gp = gameObject.AddComponent<GaitProbe>();
                    gp.seconds = moveSeconds + 0.5f;
                    gp.outputPath = $"Captures/PlayerFeel/g_{pc.id}_{gait}_{mode}.csv";
                    while (gp != null) yield return null;
                    while (lp != null && !lp.Done) yield return null;
                    if (lp != null) Destroy(lp);
                    yield return new WaitForSeconds(0.3f);
                }
            }
            if (animator.runtimeAnimatorController != current) animator.runtimeAnimatorController = current;
            match.SetValue(feel, true);
            feel.Apply();
            Done = true;
            Debug.Log("[GaitTestRunner] done " + pc.id);
            Destroy(this);
        }
    }
}
