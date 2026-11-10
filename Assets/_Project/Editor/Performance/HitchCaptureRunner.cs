using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Invector;
using Invector.vCamera;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using OpenworldCrime.Settings;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Frame-time SPIKE capture (editor play mode, about 2 minutes): a scripted session that covers what the player does -
    /// start-up, looking around the city, a gunfight with panic, a day/night transition, a carjack (or a parked car when no traffic
    /// car is close), fast driving through traffic with real RCC physics (scripted steering along the iTS lanes) and getting out.
    /// Every frame's main-thread time is read from the profiler. A spike is a frame at least twice the median of the frames
    /// before it (and 40 ms over it): the editor in play mode runs far slower than a build, so an absolute threshold would flag
    /// every frame. Each spike is compared with the last normal frame before it, marker by marker, and attributed to the
    /// markers that grew the most; game (PlayerLoop) and editor (EditorLoop) growth are separated. Report: Captures/Hitches/hitch_&lt;label&gt;.txt with
    /// max / p99 / p95 per phase and spike classes. Frame rate stays capped as in the game. Nothing in the scene is saved.
    /// Editor numbers include the editor; the report separates editor-only spikes from game spikes.
    /// </summary>
    [InitializeOnLoad]
    public static class HitchCaptureRunner
    {
        private const string LabelKey = "OWC.Hitch.Label";
        private const string QualityKey = "OWC.Hitch.Quality";
        private const string OutputFolder = "Captures/Hitches";
        private const float SpikeRatio = 2f;
        private const float SpikeMinExtraMs = 40f;
        private const int MedianWindow = 90;
        private const string PlayerPath = "Player/Player_Jason";
        private const float DriveKmh = 85f;

        private enum Phase { Startup, LookAround, Combat, DayNight, Carjack, Drive, Exit, Done }
        private static readonly float[] PhaseEnd = { 10f, 22f, 34f, 46f, 60f, 112f, 120f, 121f };

        private struct FrameSample { public Phase Phase; public float Ms; public bool Spike; }

        private struct Spike
        {
            public float Time;
            public Phase Phase;
            public float Ms, Median, PlayerLoopExtra, EditorLoopExtra, GcKb;
            public string Top;
            public string Class;
        }

        private static bool running;
        private static string label;
        private static double startTime;
        private static Phase phase;
        private static int lastFrameRead = -1;
        private static int lastNormalFrame = -1;
        private static readonly List<float> window = new List<float>();
        private static readonly List<float> sortBuffer = new List<float>();
        private static readonly List<FrameSample> frames = new List<FrameSample>();
        private static readonly List<Spike> spikes = new List<Spike>();
        private static readonly StringBuilder log = new StringBuilder();
        private static int originalQuality = -1;

        // Session state.
        private static double nextShot, nextKill;
        private static readonly List<PedestrianWalker> combatTargets = new List<PedestrianWalker>();
        private static RCC_CarControllerV3 driven;
        private static readonly List<Vector3> path = new List<Vector3>();
        private static int pathIndex;
        private static float stuckSince = -1f;
        private static bool enterRequested, exitRequested, fallbackRequested;
        private static int unstuck;

        static HitchCaptureRunner()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(LabelKey, null))) Begin();
                if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("play mode stopped early");
            };
        }

        [MenuItem("Tools/OpenworldCrime/Performance/Run Hitch Capture (about 2 min) - PC Quality (Good)")]
        private static void RunPc() => Run("pc_" + DateTime.Now.ToString("HHmmss"), 3);

        [MenuItem("Tools/OpenworldCrime/Performance/Run Hitch Capture (about 2 min) - Android Quality (Simple)")]
        private static void RunAndroid() => Run("android_" + DateTime.Now.ToString("HHmmss"), 2);

        public static void Run(string runLabel, int qualityLevel = -1)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[HitchCapture] Stop play mode first."); return; }
            SessionState.SetString(LabelKey, runLabel);
            SessionState.SetInt(QualityKey, qualityLevel);
            originalQuality = QualitySettings.GetQualityLevel();
            EditorApplication.EnterPlaymode();
        }

        private static void Begin()
        {
            if (running) return;
            running = true;
            label = SessionState.GetString(LabelKey, "capture");
            SessionState.EraseString(LabelKey);
            int quality = SessionState.GetInt(QualityKey, -1);
            if (quality >= 0 && quality < QualitySettings.names.Length)
            {
                // The graphics profile with this quality level, so its pipeline asset and distances apply too.
                GraphicsSettingsManager settings = UnityEngine.Object.FindFirstObjectByType<GraphicsSettingsManager>();
                string profilePath = quality == 2 ? "Assets/_Project/Data/Settings/GraphicsProfile_Mobile.asset" : "Assets/_Project/Data/Settings/GraphicsProfile_PC.asset";
                var profile = AssetDatabase.LoadAssetAtPath<GraphicsProfile>(profilePath);
                if (settings != null && profile != null) settings.SetProfile(profile);
                else QualitySettings.SetQualityLevel(quality, true);
            }
            frames.Clear();
            spikes.Clear();
            log.Clear();
            combatTargets.Clear();
            driven = null;
            enterRequested = exitRequested = fallbackRequested = false;
            unstuck = 0;
            stuckSince = -1f;
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            lastFrameRead = -1;
            lastNormalFrame = -1;
            window.Clear();
            startTime = EditorApplication.timeSinceStartup;
            phase = Phase.Startup;
            EditorApplication.update += Tick;
        }

        private static float Elapsed => (float)(EditorApplication.timeSinceStartup - startTime);

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                ReadFrames();
                Phase now = Phase.Done;
                for (int i = 0; i < PhaseEnd.Length; i++) if (Elapsed < PhaseEnd[i]) { now = (Phase)i; break; }
                if (now != phase) EnterPhase(now);
                RunPhase();
                if (phase == Phase.Done) Finish(null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish("exception: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ scripted session

        private static void EnterPhase(Phase next)
        {
            phase = next;
            log.AppendLine($"{Elapsed,6:F1} s  -> {next}");
            GameObject player = FindPlayer();
            switch (next)
            {
                case Phase.Combat:
                    Camera cam = Camera.main;
                    Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
                    foreach (PedestrianWalker w in UnityEngine.Object.FindObjectsByType<PedestrianWalker>(FindObjectsSortMode.None))
                        if ((w.transform.position - origin).sqrMagnitude < 60f * 60f) combatTargets.Add(w);
                    combatTargets.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
                    nextShot = nextKill = EditorApplication.timeSinceStartup;
                    log.AppendLine($"         {combatTargets.Count} pedestrians within 60 m");
                    break;
                case Phase.DayNight:
                    var dayNight = UnityEngine.Object.FindFirstObjectByType<CityDayNightController>();
                    if (dayNight != null)
                    {
                        dayNight.transitionSeconds = 10f;
                        dayNight.TransitionTo(!dayNight.IsNight);
                        log.AppendLine($"         transition to {(dayNight.IsNight ? "night" : "day")} over 10 s");
                    }
                    break;
                case Phase.Carjack:
                    RequestCarjack(player);
                    break;
                case Phase.Drive:
                    StartDriving(player);
                    break;
                case Phase.Exit:
                    if (driven != null)
                    {
                        driven.externalController = false;
                        driven.throttleInput = 0f;
                        driven.brakeInput = 1f;
                    }
                    break;
            }
        }

        private static void RunPhase()
        {
            switch (phase)
            {
                case Phase.LookAround:
                    SetCameraYaw((Elapsed - PhaseEnd[0]) / (PhaseEnd[1] - PhaseEnd[0]) * 360f);
                    break;
                case Phase.Combat:
                    TickCombat();
                    break;
                case Phase.Drive:
                    TickDrive();
                    break;
                case Phase.Exit:
                    if (!exitRequested && driven != null && driven.speed < 3f)
                    {
                        exitRequested = true;
                        PlayerVehicleInteraction interaction = FindPlayer()?.GetComponent<PlayerVehicleInteraction>();
                        log.AppendLine($"{Elapsed,6:F1} s  exit request {(interaction != null && interaction.RequestExit() ? "accepted" : "refused")}");
                    }
                    break;
            }
        }

        private static void RequestCarjack(GameObject player)
        {
            PlayerVehicleInteraction interaction = player != null ? player.GetComponent<PlayerVehicleInteraction>() : null;
            if (interaction == null) return;
            TrafficVehicleAdapter best = null;
            float bestDistance = 35f;
            IReadOnlyList<TrafficVehicleAdapter> cars = TrafficVehicleAdapter.ActiveCars;
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i] == null || !cars[i].CanEnter) continue;
                float d = Vector3.Distance(cars[i].transform.position, player.transform.position);
                if (d < bestDistance) { bestDistance = d; best = cars[i]; }
            }
            if (best != null)
            {
                enterRequested = interaction.RequestEnter(best);
                log.AppendLine($"         carjack {best.name} at {bestDistance:F0} m: {(enterRequested ? "accepted" : "refused")}");
            }
            if (!enterRequested) EnterParkedCar(interaction, player);
        }

        private static void EnterParkedCar(PlayerVehicleInteraction interaction, GameObject player)
        {
            if (fallbackRequested) return;
            fallbackRequested = true;
            RCCVehicleAdapter best = null;
            float bestDistance = float.MaxValue;
            foreach (RCCVehicleAdapter v in UnityEngine.Object.FindObjectsByType<RCCVehicleAdapter>(FindObjectsSortMode.None))
            {
                if (!v.CanEnter) continue;
                float d = Vector3.Distance(v.transform.position, player.transform.position);
                if (d < bestDistance) { bestDistance = d; best = v; }
            }
            bool ok = best != null && interaction.RequestEnter(best);
            log.AppendLine($"         parked car {(best != null ? best.name : "none")} at {bestDistance:F0} m: {(ok ? "accepted" : "refused")}");
        }

        private static void StartDriving(GameObject player)
        {
            PlayerVehicleInteraction interaction = player != null ? player.GetComponent<PlayerVehicleInteraction>() : null;
            var adapter = interaction != null ? interaction.CurrentVehicle as RCCVehicleAdapter : null;
            if (interaction == null || !interaction.IsInVehicle || adapter == null)
            {
                log.AppendLine($"         not in a car (state {(interaction != null ? interaction.State.ToString() : "-")}): driving phase is spent on foot");
                return;
            }
            driven = adapter.GetComponent<RCC_CarControllerV3>();
            // Scripted inputs instead of the keyboard; RCC's gearbox, clutch and helpers run as for the player (canControl on).
            driven.externalController = true;
            BuildPath(driven.transform.position, 2500f);
            pathIndex = 0;
            log.AppendLine($"         driving {adapter.name} along {path.Count} lane points at {DriveKmh} km/h");
        }

        private static void TickDrive()
        {
            if (driven == null || path.Count < 2) return;
            Transform t = driven.transform;
            Vector3 position = t.position;
            while (pathIndex < path.Count - 1 && (path[pathIndex] - position).sqrMagnitude < 14f * 14f) pathIndex++;
            Vector3 to = path[pathIndex] - position;
            float angle = Vector3.SignedAngle(Flat(t.forward), Flat(to), Vector3.up);
            float steerLimit = Mathf.Max(1f, driven.steerAngle);
            driven.steerInput = Mathf.Clamp(angle / steerLimit, -1f, 1f);
            float target = Mathf.Abs(angle) > 35f ? 35f : DriveKmh;
            float v = driven.speed;
            driven.throttleInput = Mathf.Clamp01((target - v) / 8f);
            driven.brakeInput = Mathf.Clamp01((v - target - 6f) / 15f);
            driven.handbrakeInput = 0f;

            // Crashed into something and stopped: put the car back on the lane a little further on (counted in the report).
            if (v < 4f)
            {
                if (stuckSince < 0f) stuckSince = Elapsed;
                else if (Elapsed - stuckSince > 3f && pathIndex + 3 < path.Count)
                {
                    pathIndex += 3;
                    Vector3 dir = Flat(path[pathIndex + 1 < path.Count ? pathIndex + 1 : pathIndex] - path[pathIndex]);
                    Rigidbody body = driven.GetComponent<Rigidbody>();
                    body.position = path[pathIndex] + Vector3.up * 0.5f;
                    body.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : t.forward);
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    stuckSince = -1f;
                    unstuck++;
                }
            }
            else stuckSince = -1f;
        }

        private static void TickCombat()
        {
            double now = EditorApplication.timeSinceStartup;
            GameObject player = FindPlayer();
            if (player == null) return;
            if (now >= nextShot)
            {
                nextShot = now + 0.4;
                PedestrianThreatSource threat = player.GetComponent<PedestrianThreatSource>();
                if (threat != null) threat.ReportGunshot();
            }
            if (now < nextKill || combatTargets.Count == 0) return;
            nextKill = now + 1.2;
            PedestrianWalker target = combatTargets[0];
            combatTargets.RemoveAt(0);
            if (target == null || !target.isActiveAndEnabled) return;
            vCharacter character = target.GetComponent<vCharacter>();
            if (character == null || character.isDead) return;
            Vector3 dir = (target.transform.position - player.transform.position).normalized;
            character.TakeDamage(new vDamage
            {
                damageValue = 1000f, sender = player.transform, hitPosition = target.transform.position + Vector3.up * 1.4f,
                force = dir * 15f, damageType = "Bullet", ignoreDefense = true
            });
        }

        private static void SetCameraYaw(float yaw)
        {
            vThirdPersonCamera tpc = vThirdPersonCamera.instance;
            if (tpc == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(vThirdPersonCamera).GetField("_mouseX", flags)?.SetValue(tpc, yaw);
            typeof(vThirdPersonCamera).GetField("_mouseY", flags)?.SetValue(tpc, 5f);
        }

        private static GameObject FindPlayer()
        {
            GameObject go = GameObject.Find(PlayerPath);
            if (go != null) return go;
            var interaction = UnityEngine.Object.FindFirstObjectByType<PlayerVehicleInteraction>();
            return interaction != null ? interaction.gameObject : null;
        }

        private static void BuildPath(Vector3 from, float length)
        {
            path.Clear();
            var roads = UnityEngine.Object.FindFirstObjectByType<TSMainManager>();
            if (roads == null || roads.lanes == null) return;
            int lane = -1, point = 0;
            float best = float.MaxValue;
            for (int l = 0; l < roads.lanes.Length; l++)
            {
                TSPoints[] pts = roads.lanes[l].points;
                if (pts == null) continue;
                for (int p = 0; p < pts.Length; p++)
                {
                    float d = (pts[p].point - from).sqrMagnitude;
                    if (d < best) { best = d; lane = l; point = p; }
                }
            }
            float total = 0f;
            int guard = 0;
            var random = new System.Random(7);
            while (lane >= 0 && total < length && guard++ < 80)
            {
                TSLaneInfo info = roads.lanes[lane];
                for (int p = point; p < info.points.Length; p++) Append(info.points[p].point, ref total);
                point = 0;
                if (info.connectors == null || info.connectors.Length == 0) break;
                TSLaneConnector chosen = info.connectors[random.Next(info.connectors.Length)];
                if (chosen == null || chosen.points == null) break;
                foreach (TSConnectorPoint cp in chosen.points) Append(cp.point, ref total);
                lane = chosen.nextLane;
            }
        }

        private static void Append(Vector3 p, ref float total)
        {
            if (path.Count > 0)
            {
                float d = Vector3.Distance(path[path.Count - 1], p);
                if (d < 1f) return;
                total += d;
            }
            path.Add(p);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        // ------------------------------------------------------------------ frames and spikes

        private static void ReadFrames()
        {
            int last = ProfilerDriver.lastFrameIndex;
            int first = Math.Max(ProfilerDriver.firstFrameIndex, lastFrameRead + 1);
            for (int f = first; f <= last; f++)
            {
                using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(f, 0))
                {
                    if (raw == null || !raw.valid) continue;
                    float ms = raw.frameTimeMs;
                    float median = Median();
                    bool spike = window.Count >= 20 && ms >= median * SpikeRatio && ms >= median + SpikeMinExtraMs;
                    frames.Add(new FrameSample { Phase = phase, Ms = ms, Spike = spike });
                    if (spike) AnalyzeSpike(f, ms, median);
                    else lastNormalFrame = f;
                    window.Add(ms);
                    if (window.Count > MedianWindow) window.RemoveAt(0);
                }
                lastFrameRead = f;
            }
        }

        private static float Median()
        {
            if (window.Count == 0) return 0f;
            sortBuffer.Clear();
            sortBuffer.AddRange(window);
            sortBuffer.Sort();
            return sortBuffer[sortBuffer.Count / 2];
        }

        private static readonly HashSet<string> IgnoredSelf = new HashSet<string> { "WaitForTargetFPS", "EditorLoop", "PlayerLoop", "Main Thread", "Idle" };

        private struct FrameProfile
        {
            public Dictionary<string, float> Self;
            public float PlayerLoop, EditorLoop, Gc, GcCollect;
        }

        private static bool ReadProfile(int frame, out FrameProfile profile)
        {
            profile = new FrameProfile { Self = new Dictionary<string, float>() };
            if (frame < 0 || frame < ProfilerDriver.firstFrameIndex) return false;
            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) return false;
                var children = new List<int>();
                var stack = new Stack<int>();
                stack.Push(view.GetRootItemID());
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    string name = view.GetItemName(id);
                    float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                    float s = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                    if (name == "PlayerLoop") { profile.PlayerLoop += total; profile.Gc += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory); }
                    else if (name == "EditorLoop") profile.EditorLoop += total;
                    if (name == "GC.Collect") profile.GcCollect += total;
                    if (!IgnoredSelf.Contains(name))
                    {
                        profile.Self.TryGetValue(name, out float sum);
                        profile.Self[name] = sum + s;
                    }
                    view.GetItemChildren(id, children);
                    for (int i = 0; i < children.Count; i++) stack.Push(children[i]);
                }
            }
            return true;
        }

        // Attributed to the markers that grew the most against the last normal frame before the spike.
        private static void AnalyzeSpike(int frame, float ms, float median)
        {
            if (!ReadProfile(frame, out FrameProfile spike)) return;
            bool haveReference = ReadProfile(lastNormalFrame, out FrameProfile reference);
            var growth = new List<KeyValuePair<string, float>>();
            foreach (KeyValuePair<string, float> kv in spike.Self)
            {
                float before = 0f;
                if (haveReference) reference.Self.TryGetValue(kv.Key, out before);
                if (kv.Value - before > 0.5f) growth.Add(new KeyValuePair<string, float>(kv.Key, kv.Value - before));
            }
            growth.Sort((a, b) => b.Value.CompareTo(a.Value));
            var top = new StringBuilder();
            for (int i = 0; i < Math.Min(5, growth.Count); i++) top.Append($"{growth[i].Key} +{growth[i].Value:F1}; ");
            float playerExtra = spike.PlayerLoop - (haveReference ? reference.PlayerLoop : 0f);
            float editorExtra = spike.EditorLoop - (haveReference ? reference.EditorLoop : 0f);
            bool game = playerExtra >= editorExtra;
            string cls = !game ? "EDITOR (" + (growth.Count > 0 ? growth[0].Key : "?") + ")"
                : spike.GcCollect > 5f ? "GC.Collect" : growth.Count > 0 ? growth[0].Key : "?";
            spikes.Add(new Spike
            {
                Time = Elapsed, Phase = phase, Ms = ms, Median = median, PlayerLoopExtra = playerExtra, EditorLoopExtra = editorExtra,
                GcKb = spike.Gc / 1024f, Top = top.ToString(), Class = cls
            });
        }

        // ------------------------------------------------------------------ report

        private static void Finish(string error)
        {
            if (!running) return;
            running = false;
            EditorApplication.update -= Tick;
            ProfilerDriver.enabled = false;
            if (driven != null) driven.externalController = false;

            var report = new StringBuilder();
            report.AppendLine($"Hitch capture '{label}'  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion}");
            report.AppendLine($"Quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}  pipeline {(QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "-")}  " +
                              $"target fps {Application.targetFrameRate}  vSync {QualitySettings.vSyncCount}  fixedDeltaTime {Time.fixedDeltaTime:F3}  maximumDeltaTime {Time.maximumDeltaTime:F2}  " +
                              $"incremental GC {UnityEngine.Scripting.GarbageCollector.isIncremental}");
            report.AppendLine($"Spike = frame >= {SpikeRatio}x the median of the {MedianWindow} frames before it and >= median + {SpikeMinExtraMs} ms. " +
                              "GAME spike = PlayerLoop grew more than EditorLoop against the last normal frame; otherwise the editor caused it.");
            if (error != null) report.AppendLine("ABORTED: " + error);
            report.AppendLine();
            report.AppendLine("phase        frames   avg ms   p50    p95    p99     max  p99/p50      spikes  game spikes  editor spikes");
            for (int p = 0; p <= (int)Phase.Exit; p++) AppendStats(report, ((Phase)p).ToString(), (Phase)p, false);
            AppendStats(report, "ALL", Phase.Done, true);
            AppendStats(report, "ALL-startup", Phase.Startup, true, true);

            report.AppendLine();
            report.AppendLine("Game spike classes (marker that grew most against the last normal frame):");
            var classes = new Dictionary<string, List<float>>();
            foreach (Spike s in spikes)
            {
                if (s.Class.StartsWith("EDITOR", StringComparison.Ordinal)) continue;
                if (!classes.TryGetValue(s.Class, out List<float> l)) classes[s.Class] = l = new List<float>();
                l.Add(s.Ms);
            }
            var ordered = new List<KeyValuePair<string, List<float>>>(classes);
            ordered.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            foreach (var kv in ordered)
            {
                kv.Value.Sort();
                report.AppendLine($"  {kv.Key,-55} count {kv.Value.Count,3}   max {kv.Value[kv.Value.Count - 1],6:F1} ms");
            }

            report.AppendLine();
            report.AppendLine("Spike frames (time, phase, frame ms, median ms, PlayerLoop / EditorLoop growth ms, GC alloc KB, markers that grew most):");
            foreach (Spike s in spikes)
                report.AppendLine($"  {s.Time,6:F1} {s.Phase,-10} {s.Ms,7:F1} (med {s.Median,5:F1})  PL +{s.PlayerLoopExtra,6:F1}  EL +{s.EditorLoopExtra,6:F1}  gc {s.GcKb,7:F1}  {s.Class,-40} | {s.Top}");

            report.AppendLine();
            report.AppendLine($"Session log (driving: {unstuck} times put back on the lane after getting stuck):");
            report.Append(log);

            Directory.CreateDirectory(OutputFolder);
            string file = Path.Combine(OutputFolder, $"hitch_{label}.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log($"[HitchCapture] Report written to {Path.GetFullPath(file)}");
            driven = null;
            if (EditorApplication.isPlaying) EditorApplication.delayCall += () => { if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); };
            if (originalQuality >= 0)
            {
                int restore = originalQuality;
                EditorApplication.delayCall += () => QualitySettings.SetQualityLevel(restore, true);
            }
        }

        private static void AppendStats(StringBuilder report, string title, Phase which, bool all, bool skipStartup = false)
        {
            var values = new List<float>();
            foreach (FrameSample f in frames)
                if ((all || f.Phase == which) && !(skipStartup && f.Phase == Phase.Startup)) values.Add(f.Ms);
            if (values.Count == 0) { report.AppendLine($"{title,-12} (no frames)"); return; }
            int gameSpikes = 0, editorSpikes = 0;
            foreach (Spike s in spikes)
            {
                if (!all && s.Phase != which) continue;
                if (skipStartup && s.Phase == Phase.Startup) continue;
                if (s.Class.StartsWith("EDITOR", StringComparison.Ordinal)) editorSpikes++; else gameSpikes++;
            }
            double sum = 0;
            foreach (float v in values) sum += v;
            values.Sort();
            float p50 = Pct(values, 0.5f), p99 = Pct(values, 0.99f);
            report.AppendLine($"{title,-12} {values.Count,6} {sum / values.Count,8:F1} {p50,6:F1} {Pct(values, 0.95f),6:F1} {p99,6:F1} {values[values.Count - 1],7:F1} " +
                              $"{p99 / Mathf.Max(0.1f, p50),8:F2} {gameSpikes + editorSpikes,11} {gameSpikes,12} {editorSpikes,14}");
        }

        private static float Pct(List<float> sorted, float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
