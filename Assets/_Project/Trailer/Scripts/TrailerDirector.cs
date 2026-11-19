using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace OpenworldCrime.Trailer
{
    /// <summary>
    /// Plays the gameplay trailer inside the running city (play mode of Gangstar_Gameplay) and records it frame-exact:
    /// fixed capture frame rate, one camera rendering into a 1920x1080 target, each frame read back and piped to ffmpeg.
    /// Shots (TrailerDirector.Shots.cs) are functions of their local time; before each shot the hidden player is moved to
    /// the shot's anchor so traffic and pedestrians spawn there, and a short warm-up runs unrecorded.
    /// Nothing is saved: every change (time of day, paint, staged cars, hidden player) lives only in this play session.
    /// Started by Tools > OpenworldCrime > Trailer (TrailerMenu) or by adding the component in play mode and calling Begin.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public partial class TrailerDirector : MonoBehaviour
    {
        public class Settings
        {
            public int Fps = 30;
            public int Width = 1920;
            public int Height = 1080;
            public string OutputPath = "";
            public string FfmpegPath = "";
            public int Crf = 18;
            /// <summary>Only these shots (names, comma separated) are played; empty = all.</summary>
            public string ShotFilter = "";
            /// <summary>If set, no video: JPG stills at these timeline seconds (absolute) are written to StillsDir.</summary>
            public List<float> Stills = new List<float>();
            /// <summary>Stills at these fractions of every played shot (e.g. 0.1, 0.5, 0.95).</summary>
            public List<float> StillFractions = new List<float>();
            public string StillsDir = "";
            public bool ExitPlayModeWhenDone = true;
            public float FadeIn = 0.6f;
            public float FadeOut = 2.0f;
        }

        public class Shot
        {
            public string Name;
            public float Duration;
            public Vector3 Anchor;
            public float AnchorYaw;
            public float Warmup = 1.5f;
            public Action Enter;
            public Action Ready;
            /// <summary>(local seconds, 0-1 progress)</summary>
            public Action<float, float> Update;
            public Action Exit;
            public float Start; // timeline start (filled in)
        }

        public static TrailerDirector Instance { get; private set; }
        public bool Done { get; private set; }
        public string Status { get; private set; } = "idle";

        private Settings settings;
        private readonly List<Shot> shots = new List<Shot>();
        private Camera cam;
        private RenderTexture rt;
        private Texture2D readback;
        private Process ffmpeg;
        private Stream ffmpegIn;
        private int framesWritten;
        private byte[] frameBytes;
        private Transform stage;
        private float dt;

        // World references
        private CityDayNightController dayNight;
        private MonoBehaviour skyCycle;
        private Material skyMaterial;
        private MethodInfo phaseWeights, sunDirection, moonDirection;
        private GameObject player;
        private readonly List<Camera> disabledCameras = new List<Camera>();
        private Scene clubScene;

        private static readonly int CycleWeightsId = Shader.PropertyToID("_CycleWeights");
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");

        public static TrailerDirector Begin(Settings s)
        {
            if (Instance != null) { Debug.LogWarning("[Trailer] Already running."); return Instance; }
            var go = new GameObject("Trailer Director");
            var d = go.AddComponent<TrailerDirector>();
            d.settings = s ?? new Settings();
            return d;
        }

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            CloseFfmpeg();
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            if (RenderTexture.active == rt) RenderTexture.active = null;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            if (settings == null) settings = new Settings();
            dt = 1f / settings.Fps;
            IEnumerator run = null;
            try { run = Run(); }
            catch (Exception e) { Debug.LogError("[Trailer] Start failed: " + e); }
            if (run != null) yield return run;
        }

        private IEnumerator Run()
        {
            Status = "setup";
            UnityEngine.Random.InitState(1234);
            Time.captureFramerate = settings.Fps;
            SetupWorld();
            yield return LoadClub();
            SetupCamera();
            HookVisibility();
            BuildShots();

            float total = 0f;
            foreach (var s in shots) { s.Start = total; total += s.Duration; }
            Debug.Log($"[Trailer] {shots.Count} shots, {total:F2} s.");

            bool video = settings.Stills.Count == 0 && settings.StillFractions.Count == 0;
            var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in (settings.ShotFilter ?? "").Split(',')) if (n.Trim().Length > 0) filter.Add(n.Trim());

            if (video)
            {
                float playedTotal = 0f;
                foreach (var s in shots) if (filter.Count == 0 || filter.Contains(s.Name)) playedTotal += s.Duration;
                if (!OpenFfmpeg(playedTotal)) { Finish(); yield break; }
            }

            foreach (var shot in shots)
            {
                if (filter.Count > 0 && !filter.Contains(shot.Name)) continue;
                Status = "shot " + shot.Name;
                currentShot = shot;
                ClearStage();
                MovePlayer(shot.Anchor, shot.AnchorYaw);
                Exec(shot.Enter, shot.Name + ".Enter");
                int warm = Mathf.Max(1, Mathf.RoundToInt(shot.Warmup * settings.Fps));
                for (int i = 0; i < warm; i++)
                {
                    ApplyFrame(shot, 0f, i == warm - 1 ? 1 : 0);
                    yield return null;
                }
                Exec(shot.Ready, shot.Name + ".Ready");
                int frames = Mathf.RoundToInt(shot.Duration * settings.Fps);
                var wantStills = new List<int>();
                foreach (float f in settings.StillFractions) wantStills.Add(Mathf.Clamp(Mathf.RoundToInt(f * (frames - 1)), 0, frames - 1));
                foreach (float t in settings.Stills)
                    if (t >= shot.Start && t < shot.Start + shot.Duration) wantStills.Add(Mathf.Clamp(Mathf.RoundToInt((t - shot.Start) * settings.Fps), 0, frames - 1));
                int lastNeeded = video ? frames - 1 : -1;
                foreach (int w in wantStills) lastNeeded = Mathf.Max(lastNeeded, w);

                for (int f = 0; f <= lastNeeded; f++)
                {
                    float lt = f * dt;
                    ApplyFrame(shot, lt, 0);
                    yield return new WaitForEndOfFrame();
                    if (video) { Capture(); WriteFrame(); }
                    else if (wantStills.Contains(f))
                    {
                        Capture();
                        SaveStill(Path.Combine(settings.StillsDir, $"{shot.Name}_{(shot.Start + lt):000.00}.jpg"));
                    }
                }
                Exec(shot.Exit, shot.Name + ".Exit");
            }

            Status = "finishing";
            ClearStage();
            CloseFfmpeg();
            Debug.Log($"[Trailer] Done. Frames written: {framesWritten}. Output: {(video ? settings.OutputPath : settings.StillsDir)}");
            Finish();
        }

        private Shot currentShot;

        private void Exec(Action a, string what)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogError($"[Trailer] {what} failed: {e}"); }
        }

        private void ApplyFrame(Shot shot, float lt, int _)
        {
            try
            {
                shot.Update?.Invoke(lt, Mathf.Clamp01(lt / Mathf.Max(0.001f, shot.Duration)));
            }
            catch (Exception e) { Debug.LogError($"[Trailer] {shot.Name}.Update failed at {lt:F2}: {e}"); }
        }

        private void Finish()
        {
            Done = true;
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            foreach (var c in disabledCameras) if (c != null) c.enabled = true;
#if UNITY_EDITOR
            if (settings.ExitPlayModeWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ------------------------------------------------------------------ world

        private void SetupWorld()
        {
            dayNight = FindFirstObjectByType<CityDayNightController>();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb.GetType().Name == "SkyCycleController") { skyCycle = mb; break; }
            if (skyCycle != null)
            {
                var t = skyCycle.GetType();
                const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
                skyMaterial = t.GetField("skyInstance", bf)?.GetValue(skyCycle) as Material;
                phaseWeights = t.GetMethod("PhaseWeights", bf);
                sunDirection = t.GetMethod("SunDirection", bf);
                moonDirection = t.GetMethod("MoonDirection", bf);
                skyCycle.enabled = false; // the trailer drives the clock
            }
            if (dayNight == null) Debug.LogError("[Trailer] No CityDayNightController.");

            var gp = FindFirstObjectByType<OpenworldCrime.Player.InvectorGamePlayer>();
            player = gp != null ? gp.gameObject : GameObject.Find("Player_Jason");
            if (player != null)
            {
                foreach (var r in player.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            // Police never come for the hidden player.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                string n = mb.GetType().Name;
                if (n == "CrimeReporter" || n == "PoliceDispatcher" || n == "WantedManager" || n == "BustedSequence") mb.enabled = false;
            }
            // Gameplay markers (entrance columns / rings, race starts) stay out of the trailer.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb.GetType().Name == "MarkerBob") mb.gameObject.SetActive(false);
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Marker Column" || t.name == "Marker Ring") t.gameObject.SetActive(false);
            stage = new GameObject("Trailer Stage").transform;
        }

        /// <summary>Distance hiding of small props follows the trailer camera, with longer distances for the wide shots.</summary>
        private void HookVisibility()
        {
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb.GetType().Name != "EnvironmentVisibilityManager") continue;
                var t = mb.GetType();
                t.GetField("cameras", bf)?.SetValue(mb, new[] { cam });
                t.GetField("distanceReference", bf)?.SetValue(mb, cam.transform);
                t.GetField("activeCamera", bf)?.SetValue(mb, null);
                t.GetMethod("UpdateCamera", bf)?.Invoke(mb, null);
                t.GetMethod("SetDistances")?.Invoke(mb, new object[] { 260f, 520f, 520f });
            }
        }

        private IEnumerator LoadClub()
        {
            clubScene = SceneManager.GetSceneByName("Club_Interior");
            if (!clubScene.isLoaded)
            {
                var op = SceneManager.LoadSceneAsync("Club_Interior", LoadSceneMode.Additive);
                if (op != null) while (!op.isDone) yield return null;
                clubScene = SceneManager.GetSceneByName("Club_Interior");
            }
            if (clubScene.isLoaded)
            {
                LightProbes.TetrahedralizeAsync();
                // Tasteful trailer: the club's catwalk / pole performers stay out of the trailer.
                foreach (var root in clubScene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name.Contains("nude") && t.parent != null && t.parent.name == "Crowd") t.gameObject.SetActive(false);
                foreach (var root in clubScene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("Dancer Catwalk") || t.name.StartsWith("Dancer Pole")) t.gameObject.SetActive(false);
                // Its gameplay pieces (seats, cash, HUD) are not needed.
                foreach (var root in clubScene.GetRootGameObjects())
                {
                    foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                    foreach (var al in root.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
                    // Its post-processing only while the trailer is inside (EnterClub / ExitClub).
                    foreach (var v in root.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true)) { v.enabled = false; clubVolumes.Add(v); }
                }
            }
            else Debug.LogWarning("[Trailer] Club_Interior could not be loaded; club shots will show the city.");
        }

        private void SetupCamera()
        {
            Camera source = Camera.main;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.enabled) { c.enabled = false; disabledCameras.Add(c); }

            var go = new GameObject("Trailer Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 1600f;
            cam.fieldOfView = 50f;
            cam.allowHDR = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            var src = source != null ? source.GetComponent<UniversalAdditionalCameraData>() : null;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            if (src != null)
            {
                data.volumeLayerMask = src.volumeLayerMask;
                data.renderShadows = src.renderShadows;
                data.requiresDepthOption = src.requiresDepthOption;
            }
            if (source != null) cam.cullingMask = source.cullingMask;
            go.AddComponent<AudioListener>().enabled = false;

            rt = new RenderTexture(settings.Width, settings.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "TrailerRT" };
            rt.Create();
            cam.targetTexture = rt;
            readback = new Texture2D(settings.Width, settings.Height, TextureFormat.RGBA32, false);
            go.AddComponent<TrailerPreview>().Source = rt;
        }

        /// <summary>Moves the hidden player (traffic and pedestrians follow it) to a shot's anchor.</summary>
        private void MovePlayer(Vector3 pos, float yaw)
        {
            if (player == null || pos == Vector3.zero) return;
            var gp = player.GetComponent<OpenworldCrime.Player.InvectorGamePlayer>();
            Vector3 ground = TrailerCar.Ground(pos, pos.y);
            if (gp != null) gp.Teleport(ground, yaw);
            else player.transform.position = ground;
        }

        // ------------------------------------------------------------------ time of day

        /// <summary>Sky clock (hours) and lighting night blend (0 day - 1 night), applied now.</summary>
        protected void SetTimeOfDay(float hour, float nightBlend)
        {
            if (dayNight != null) dayNight.SetManualBlend(Mathf.Clamp01(nightBlend));
            if (skyMaterial != null && phaseWeights != null)
            {
                skyMaterial.SetVector(CycleWeightsId, (Vector4)phaseWeights.Invoke(skyCycle, new object[] { hour }));
                skyMaterial.SetVector(SunDirectionId, (Vector4)sunDirection.Invoke(skyCycle, new object[] { hour }));
                skyMaterial.SetVector(MoonDirectionId, (Vector4)moonDirection.Invoke(skyCycle, null));
                RenderSettings.skybox = skyMaterial;
            }
        }

        // ------------------------------------------------------------------ camera helpers

        protected void Cam(Vector3 pos, Vector3 lookAt, float fov, float roll = 0f)
        {
            Vector3 d = lookAt - pos;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(d) * Quaternion.Euler(0f, 0f, roll));
            cam.fieldOfView = fov;
        }

        protected Transform Stage => stage;
        protected Camera TrailerCamera => cam;
        protected float FrameDt => dt;

        private void ClearStage()
        {
            if (stage == null) return;
            for (int i = stage.childCount - 1; i >= 0; i--) Destroy(stage.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------ capture

        private void Capture()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, settings.Width, settings.Height), 0, 0, false);
            RenderTexture.active = prev;
        }

        private void SaveStill(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, readback.EncodeToJPG(90));
        }

        private bool OpenFfmpeg(float seconds)
        {
            string exe = settings.FfmpegPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { Debug.LogError("[Trailer] ffmpeg not found: " + exe); return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath));
            string vf = "vflip";
            if (settings.FadeIn > 0f) vf += $",fade=t=in:st=0:d={settings.FadeIn:0.###}";
            if (settings.FadeOut > 0f) vf += $",fade=t=out:st={Mathf.Max(0f, seconds - settings.FadeOut):0.###}:d={settings.FadeOut:0.###}";
            string args = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {settings.Width}x{settings.Height} -r {settings.Fps} -i - " +
                          $"-vf \"{vf}\" -c:v libx264 -preset slow -crf {settings.Crf} -pix_fmt yuv420p -movflags +faststart \"{settings.OutputPath}\"";
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            ffmpeg = Process.Start(psi);
            ffmpegIn = ffmpeg.StandardInput.BaseStream;
            Debug.Log("[Trailer] ffmpeg " + args);
            return true;
        }

        private void WriteFrame()
        {
            if (ffmpegIn == null) return;
            var data = readback.GetRawTextureData<byte>();
            if (frameBytes == null || frameBytes.Length != data.Length) frameBytes = new byte[data.Length];
            data.CopyTo(frameBytes); // one reused buffer: no 8 MB garbage per frame
            ffmpegIn.Write(frameBytes, 0, frameBytes.Length);
            framesWritten++;
        }

        private void CloseFfmpeg()
        {
            if (ffmpeg == null) return;
            try
            {
                ffmpegIn.Flush();
                ffmpegIn.Close();
                ffmpeg.WaitForExit(120000);
            }
            catch (Exception e) { Debug.LogError("[Trailer] ffmpeg close: " + e.Message); }
            ffmpeg = null;
            ffmpegIn = null;
        }

        // ------------------------------------------------------------------ easing

        protected static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        protected static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        protected static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
        protected static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x; }
        protected static Vector3 Lerp(Vector3 a, Vector3 b, float t) => Vector3.LerpUnclamped(a, b, t);
    }

    /// <summary>Shows the trailer camera's render target in the Game view while recording.</summary>
    public class TrailerPreview : MonoBehaviour
    {
        public RenderTexture Source;
        private void OnGUI()
        {
            if (Source == null || Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Source, ScaleMode.ScaleToFit, false);
        }
    }
}
