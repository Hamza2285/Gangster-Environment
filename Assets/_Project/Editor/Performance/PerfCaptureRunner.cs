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
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Repeatable short performance capture (editor play mode, about 65 s), one graphics profile per run:
    /// warm-up, then short samples at the player's start position (city centre): STAND (as spawned), occlusion OFF,
    /// DENSE BLOCK (camera turned to the direction with the most scenery), occlusion OFF there, OPEN AREA (direction with the
    /// least scenery), COMBAT (simulated: gunshot panic reports and pedestrians shot dead into ragdolls, no player input), and
    /// DRIVING (player mounts the Sport bike and is moved along the iTS lanes through traffic at driving speed).
    /// Records frame timing, render counters, GC, main-thread marker breakdown, the top script costs and a scene census
    /// (traffic LOD, pedestrians, rigidbodies), writes Captures/perf_&lt;label&gt;.txt plus screenshots, and leaves play mode.
    /// Nothing in the scene is saved. Editor numbers include editor overhead; compare runs with each other, not with a build.
    /// </summary>
    [InitializeOnLoad]
    public static class PerfCaptureRunner
    {
        private const string LabelKey = "OWC.Perf.Label";
        private const string ActiveKey = "OWC.Perf.Active";
        private const float WarmupSeconds = 10f;
        private const float SampleSeconds = 6f;
        private const float SettleSeconds = 1f;
        private const float CombatSeconds = 8f;
        private const float MountTimeout = 7f;
        private const float DriveSeconds = 12f;
        private const float DriveSpeed = 22f;
        private const string PlayerPath = "Player/Player_Jason";
        private const string BikePath = "Vehicles/Drivable_Bike_Sport";
        private const int HierarchyFramesPerPhase = 90;
        private const float OcclusionOffSeconds = 4f;
        private const float CombatShotInterval = 0.4f;
        private const float CombatKillInterval = 1.2f;

        private enum Phase { Idle, Warmup, Stand, StandOcclusionOff, Dense, DenseOcclusionOff, Open, Combat, Mount, Drive, Done }

        private static Phase phase;
        private static double phaseStart;
        private static bool running;
        private static string label;
        private static readonly StringBuilder report = new StringBuilder();
        private static readonly List<Recorder> recorders = new List<Recorder>();
        private static int phaseStartFrame;
        private static int profilerFirstFrame;
        private static double ftCpuMain, ftCpuRender, ftGpu;
        private static int ftCount;
        private static readonly Dictionary<string, double> statSums = new Dictionary<string, double>();
        private static int statTicks;
        private static readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private static bool sampling;
        private static double sampleStart;

        private static RCCVehicleAdapter bike;
        private static Transform mover;
        private static readonly List<Vector3> path = new List<Vector3>();
        private static float travelled;
        private static int pathIndex;
        private static double lastTick;
        private static bool mounted;

        private static float denseYaw, openYaw;
        private static readonly List<PedestrianWalker> combatTargets = new List<PedestrianWalker>();
        private static double nextShot, nextKill;
        private static int kills;

        private struct Recorder
        {
            public string Name;
            public ProfilerRecorder Rec;
            public double Scale;
        }

        static PerfCaptureRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlaying) EditorApplication.delayCall += Begin;
        }

        [MenuItem("Tools/OpenworldCrime/Performance/Run Perf Capture (about 65 s, scene profile)")]
        private static void RunFromMenu() => Run("run_" + DateTime.Now.ToString("HHmmss"));

        [MenuItem("Tools/OpenworldCrime/Performance/Run Perf Capture - Low")]
        private static void RunLow() => Run("run_low_" + DateTime.Now.ToString("HHmmss"), "Low");

        [MenuItem("Tools/OpenworldCrime/Performance/Run Perf Capture - Medium")]
        private static void RunMedium() => Run("run_medium_" + DateTime.Now.ToString("HHmmss"), "Medium");

        [MenuItem("Tools/OpenworldCrime/Performance/Run Perf Capture - High")]
        private static void RunHigh() => Run("run_high_" + DateTime.Now.ToString("HHmmss"), "High");

        /// <summary>
        /// Enters play mode and runs the capture; the report goes to Captures/perf_{runLabel}.txt. A profile name (Low, Medium,
        /// High) forces that graphics profile for this play session only (nothing in the scene changes).
        /// </summary>
        public static void Run(string runLabel, string profile = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PerfCapture] Stop play mode first.");
                return;
            }
            SessionState.SetString(LabelKey, runLabel);
            SessionState.SetBool(ActiveKey, true);
            if (string.IsNullOrEmpty(profile)) SessionState.EraseString(GraphicsSettingsManager.EditorProfileOverrideKey);
            else SessionState.SetString(GraphicsSettingsManager.EditorProfileOverrideKey, profile);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false)) Begin();
            if (change == PlayModeStateChange.ExitingPlayMode && running) Finish("play mode stopped early");
        }

        private static void Begin()
        {
            if (running || !EditorApplication.isPlaying) return;
            running = true;
            SessionState.SetBool(ActiveKey, false);
            label = SessionState.GetString(LabelKey, "capture");
            report.Clear();
            GraphicsSettingsManager settings = UnityEngine.Object.FindFirstObjectByType<GraphicsSettingsManager>();
            report.AppendLine($"Perf capture '{label}'  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion}");
            report.AppendLine($"Graphics profile: {(settings != null && settings.CurrentProfile != null ? settings.CurrentProfile.DisplayName : "none")}");
            report.AppendLine($"Quality level: {QualitySettings.names[QualitySettings.GetQualityLevel()]}  lodBias {QualitySettings.lodBias}  mipLimit {QualitySettings.globalTextureMipmapLimit}  vSync {QualitySettings.vSyncCount}");
            report.AppendLine($"Device: {SystemInfo.graphicsDeviceName}  RAM {SystemInfo.systemMemorySize} MB  VRAM {SystemInfo.graphicsMemorySize} MB  Game view {Screen.width}x{Screen.height}");
            report.AppendLine("Editor play mode: numbers include editor overhead (EditorLoop, inspector, scene view). Compare runs, not builds.");
            SetPhase(Phase.Warmup);
            EditorApplication.update += Tick;
        }

        private static void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = EditorApplication.timeSinceStartup;
        }

        private static double Elapsed => EditorApplication.timeSinceStartup - phaseStart;

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                switch (phase)
                {
                    case Phase.Warmup:
                        if (Elapsed >= WarmupSeconds)
                        {
                            FindViewDirections();
                            BeginSample();
                            SetPhase(Phase.Stand);
                        }
                        break;
                    case Phase.Stand:
                        Accumulate();
                        if (Elapsed >= SampleSeconds)
                        {
                            EndSampling("STAND (player start, city centre, camera as spawned)", true);
                            Screenshot("stand");
                            BeginOcclusionOff();
                            SetPhase(Phase.StandOcclusionOff);
                        }
                        break;
                    case Phase.StandOcclusionOff:
                        if (Elapsed >= SettleSeconds) Accumulate();
                        if (Elapsed >= OcclusionOffSeconds)
                        {
                            EndOcclusionOff("STAND");
                            SetCameraYaw(denseYaw);
                            sampling = false;
                            SetPhase(Phase.Dense);
                        }
                        break;
                    case Phase.Dense:
                        SetCameraYaw(denseYaw);
                        if (SettleThenSample())
                        {
                            Accumulate();
                            if (Elapsed >= SettleSeconds + SampleSeconds)
                            {
                                EndSampling($"DENSE BLOCK (same position, camera yaw {denseYaw:F0})", false);
                                Screenshot("dense");
                                BeginOcclusionOff();
                                SetPhase(Phase.DenseOcclusionOff);
                            }
                        }
                        break;
                    case Phase.DenseOcclusionOff:
                        SetCameraYaw(denseYaw);
                        if (Elapsed >= SettleSeconds) Accumulate();
                        if (Elapsed >= OcclusionOffSeconds)
                        {
                            EndOcclusionOff("DENSE BLOCK");
                            sampling = false;
                            SetPhase(Phase.Open);
                        }
                        break;
                    case Phase.Open:
                        SetCameraYaw(openYaw);
                        if (SettleThenSample())
                        {
                            Accumulate();
                            if (Elapsed >= SettleSeconds + SampleSeconds)
                            {
                                EndSampling($"OPEN AREA (same position, camera yaw {openYaw:F0})", false);
                                Screenshot("open");
                                StartCombat();
                                sampling = false;
                                SetPhase(Phase.Combat);
                            }
                        }
                        break;
                    case Phase.Combat:
                        SetCameraYaw(denseYaw);
                        TickCombat();
                        if (SettleThenSample())
                        {
                            Accumulate();
                            if (Elapsed >= SettleSeconds + CombatSeconds)
                            {
                                EndSampling($"COMBAT (simulated: gunshot panic every {CombatShotInterval} s, {kills} pedestrians shot dead into ragdolls)", true);
                                Screenshot("combat");
                                StartMount();
                                SetPhase(Phase.Mount);
                            }
                        }
                        break;
                    case Phase.Mount:
                        if ((bike != null && bike.IsControlled) || Elapsed >= MountTimeout)
                        {
                            PrepareDrive();
                            BeginSample();
                            SetPhase(Phase.Drive);
                        }
                        break;
                    case Phase.Drive:
                        MoveAlongPath();
                        Accumulate();
                        if (Elapsed >= DriveSeconds)
                        {
                            EndSampling($"DRIVING ({(mounted ? "mounted Sport bike" : "MOUNT FAILED: player moved")} along iTS lanes at {DriveSpeed} m/s)", true);
                            Screenshot("drive");
                            SetPhase(Phase.Done);
                        }
                        break;
                    case Phase.Done:
                        // One second for the screenshot to be written before play mode ends.
                        if (Elapsed >= 1.0) Finish(null);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish("exception: " + e.Message);
            }
        }

        // First call after the settle time starts the recorders; true from then on.
        private static bool SettleThenSample()
        {
            if (Elapsed < SettleSeconds) return false;
            if (!sampling) BeginSample();
            return true;
        }

        private static void BeginSample()
        {
            StartSampling();
            sampling = true;
            sampleStart = EditorApplication.timeSinceStartup;
        }

        private static void BeginOcclusionOff()
        {
            SetOcclusion(false);
            statSums.Clear();
            statTicks = 0;
            ftCpuMain = ftCpuRender = ftGpu = 0;
            ftCount = 0;
        }

        private static void EndOcclusionOff(string where)
        {
            report.AppendLine();
            report.AppendLine($"{where}, camera occlusion culling OFF ({OcclusionOffSeconds - SettleSeconds:F0} s, same view):");
            if (ftCount > 0) report.AppendLine($"  FrameTimingManager avg: CPU main {ftCpuMain / ftCount:F2} ms  CPU render {ftCpuRender / ftCount:F2} ms  GPU {ftGpu / ftCount:F2} ms");
            foreach (KeyValuePair<string, double> kv in statSums)
                report.AppendLine($"  {kv.Key,-30} avg {kv.Value / Math.Max(1, statTicks),10:F2}");
            SetOcclusion(true);
        }

        // ---------------------------------------------------------------- views and combat

        // Densest / emptiest view from the camera: renderer count in 30-degree sectors between 20 and 250 m.
        private static void FindViewDirections()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.transform.position;
            const int sectors = 12;
            var counts = new int[sectors];
            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r is SkinnedMeshRenderer || r is ParticleSystemRenderer) continue;
                Vector3 d = r.bounds.center - origin;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 20f || dist > 250f) continue;
                float yaw = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
                counts[Mathf.Min(sectors - 1, (int)(yaw / (360f / sectors)))]++;
            }
            int max = 0, min = 0;
            for (int i = 1; i < sectors; i++)
            {
                if (counts[i] > counts[max]) max = i;
                if (counts[i] < counts[min]) min = i;
            }
            denseYaw = (max + 0.5f) * (360f / sectors);
            openYaw = (min + 0.5f) * (360f / sectors);
            report.AppendLine($"View sectors from {origin:F0} (renderers 20-250 m per 30 deg): {string.Join(",", counts)} -> dense yaw {denseYaw:F0}, open yaw {openYaw:F0}");
        }

        // Invector's third-person camera keeps its own yaw (internal mouseX); set it so the orbit faces the wanted direction.
        private static void SetCameraYaw(float yaw)
        {
            vThirdPersonCamera tpc = vThirdPersonCamera.instance;
            if (tpc == null) return;
            System.Reflection.FieldInfo fx = typeof(vThirdPersonCamera).GetField("_mouseX", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.FieldInfo fy = typeof(vThirdPersonCamera).GetField("_mouseY", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            fx?.SetValue(tpc, yaw);
            fy?.SetValue(tpc, 5f);
        }

        private static void StartCombat()
        {
            combatTargets.Clear();
            kills = 0;
            Camera cam = Camera.main;
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
            foreach (PedestrianWalker w in UnityEngine.Object.FindObjectsByType<PedestrianWalker>(FindObjectsSortMode.None))
            {
                if (w.State == PedestrianWalker.Mode.Down) continue;
                if ((w.transform.position - origin).sqrMagnitude < 60f * 60f) combatTargets.Add(w);
            }
            combatTargets.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
            nextShot = nextKill = EditorApplication.timeSinceStartup;
            report.AppendLine();
            report.AppendLine($"Combat: {combatTargets.Count} pedestrians within 60 m of the camera at start");
        }

        // Simulated shooting (no weapon input): gunshot reports through the player's threat source (panic, aim line) and
        // a lethal shot on the nearest pedestrian every CombatKillInterval (Invector damage, ragdoll death).
        private static void TickCombat()
        {
            double now = EditorApplication.timeSinceStartup;
            GameObject playerGo = GameObject.Find(PlayerPath);
            if (playerGo == null) return;
            if (now >= nextShot)
            {
                nextShot = now + CombatShotInterval;
                PedestrianThreatSource threat = playerGo.GetComponent<PedestrianThreatSource>();
                if (threat != null) threat.ReportGunshot();
            }
            if (now < nextKill || combatTargets.Count == 0) return;
            nextKill = now + CombatKillInterval;
            PedestrianWalker target = combatTargets[0];
            combatTargets.RemoveAt(0);
            if (target == null || !target.isActiveAndEnabled) return;
            vCharacter character = target.GetComponent<vCharacter>();
            if (character == null || character.isDead) return;
            Vector3 dir = (target.transform.position - playerGo.transform.position).normalized;
            character.TakeDamage(new vDamage
            {
                damageValue = 1000f,
                sender = playerGo.transform,
                hitPosition = target.transform.position + Vector3.up * 1.4f,
                force = dir * 15f,
                damageType = "Bullet",
                ignoreDefense = true
            });
            kills++;
        }


        // ---------------------------------------------------------------- sampling

        private static void SetOcclusion(bool on)
        {
            foreach (Camera c in Camera.allCameras) c.useOcclusionCulling = on;
        }

        private static void StartSampling()
        {
            DisposeRecorders();
            AddRecorder("Main Thread (ms)", ProfilerCategory.Internal, "Main Thread", 1e-6);
            AddRecorder("Batches", ProfilerCategory.Render, "Batches Count", 1);
            AddRecorder("SetPass calls", ProfilerCategory.Render, "SetPass Calls Count", 1);
            AddRecorder("Draw calls", ProfilerCategory.Render, "Draw Calls Count", 1);
            AddRecorder("Triangles (k)", ProfilerCategory.Render, "Triangles Count", 1e-3);
            AddRecorder("Vertices (k)", ProfilerCategory.Render, "Vertices Count", 1e-3);
            AddRecorder("Shadow casters", ProfilerCategory.Render, "Shadow Casters Count", 1);
            AddRecorder("Visible skinned meshes", ProfilerCategory.Render, "Visible Skinned Meshes Count", 1);
            AddRecorder("GC alloc per frame (KB)", ProfilerCategory.Memory, "GC Allocated In Frame", 1.0 / 1024.0);
            AddRecorder("GC allocs per frame (count)", ProfilerCategory.Memory, "GC Allocation In Frame Count", 1);
            AddRecorder("Total used memory (MB)", ProfilerCategory.Memory, "Total Used Memory", 1.0 / (1024.0 * 1024.0));
            AddRecorder("Texture memory (MB)", ProfilerCategory.Memory, "Texture Memory", 1.0 / (1024.0 * 1024.0));
            AddRecorder("Mesh memory (MB)", ProfilerCategory.Memory, "Mesh Memory", 1.0 / (1024.0 * 1024.0));
            AddRecorder("Active rigidbodies", ProfilerCategory.Physics, "Active Dynamic Bodies", 1);
            AddRecorder("Active kinematic bodies", ProfilerCategory.Physics, "Active Kinematic Bodies", 1);
            AddRecorder("Physics contacts", ProfilerCategory.Physics, "Contacts", 1);
            ftCpuMain = ftCpuRender = ftGpu = 0;
            ftCount = 0;
            statSums.Clear();
            statTicks = 0;
            phaseStartFrame = Time.frameCount;
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            profilerFirstFrame = -1;
        }

        private static void AddRecorder(string name, ProfilerCategory category, string stat, double scale)
        {
            recorders.Add(new Recorder { Name = name, Rec = ProfilerRecorder.StartNew(category, stat, 4000), Scale = scale });
        }

        private static void Accumulate()
        {
            if (profilerFirstFrame < 0 && ProfilerDriver.lastFrameIndex >= 0) profilerFirstFrame = ProfilerDriver.lastFrameIndex;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
            {
                ftCpuMain += frameTimings[0].cpuMainThreadFrameTime;
                ftCpuRender += frameTimings[0].cpuRenderThreadFrameTime;
                ftGpu += frameTimings[0].gpuFrameTime;
                ftCount++;
            }
            AddStat("UnityStats batches", UnityStats.batches);
            AddStat("UnityStats setPass", UnityStats.setPassCalls);
            AddStat("UnityStats tris (k)", UnityStats.triangles / 1000.0);
            AddStat("UnityStats verts (k)", UnityStats.vertices / 1000.0);
            AddStat("UnityStats shadow casters", UnityStats.shadowCasters);
            AddStat("UnityStats render time (ms)", UnityStats.renderTime * 1000.0);
            statTicks++;
        }

        private static void AddStat(string key, double value)
        {
            statSums.TryGetValue(key, out double sum);
            statSums[key] = sum + value;
        }

        private static void EndSampling(string title, bool withCensus)
        {
            double seconds = EditorApplication.timeSinceStartup - sampleStart;
            int frames = Time.frameCount - phaseStartFrame;
            sampling = false;
            report.AppendLine();
            report.AppendLine("==================================================================");
            report.AppendLine(title);
            report.AppendLine("==================================================================");
            report.AppendLine($"Frames {frames} in {seconds:F1} s  ->  avg {(seconds * 1000.0 / Math.Max(1, frames)):F2} ms/frame  ({frames / seconds:F1} fps)");
            if (ftCount > 0)
                report.AppendLine($"FrameTimingManager avg: CPU main {ftCpuMain / ftCount:F2} ms  CPU render {ftCpuRender / ftCount:F2} ms  GPU {ftGpu / ftCount:F2} ms (0 = not supported here)");

            foreach (Recorder r in recorders)
            {
                if (!r.Rec.Valid || r.Rec.Count == 0) { report.AppendLine($"  {r.Name,-30} n/a"); continue; }
                int n = r.Rec.Count;
                double sum = 0, max = double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    double v = r.Rec.GetSample(i).Value * r.Scale;
                    sum += v;
                    if (v > max) max = v;
                }
                report.AppendLine($"  {r.Name,-30} avg {sum / n,10:F2}   max {max,10:F2}   (n={n})");
            }
            foreach (KeyValuePair<string, double> kv in statSums)
                report.AppendLine($"  {kv.Key,-30} avg {kv.Value / Math.Max(1, statTicks),10:F2}");

            ReportHierarchy();
            if (withCensus) ReportCensus();
            DisposeRecorders();
            ProfilerDriver.enabled = false;
        }

        private static void DisposeRecorders()
        {
            foreach (Recorder r in recorders) r.Rec.Dispose();
            recorders.Clear();
        }

        // Main-thread breakdown and top script costs from the profiler's merged hierarchy (last N frames of the phase).
        private static void ReportHierarchy()
        {
            int last = ProfilerDriver.lastFrameIndex;
            int first = Math.Max(Math.Max(ProfilerDriver.firstFrameIndex, profilerFirstFrame), last - HierarchyFramesPerPhase + 1);
            if (last < 0 || first > last) { report.AppendLine("  (no profiler frames)"); return; }

            var total = new Dictionary<string, double>();
            var self = new Dictionary<string, double>();
            var gc = new Dictionary<string, double>();
            var scripts = new Dictionary<string, double>();
            var scriptGc = new Dictionary<string, double>();
            var children = new List<int>();
            var stack = new Stack<(int id, int scriptDepth, int depth)>();
            var layered = new Dictionary<string, double>();
            var calls = new Dictionary<string, double>();
            int frameCount = 0;

            for (int f = first; f <= last; f++)
            {
                using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    frameCount++;
                    stack.Clear();
                    stack.Push((view.GetRootItemID(), 0, 0));
                    while (stack.Count > 0)
                    {
                        (int id, int scriptDepth, int depth) = stack.Pop();
                        string name = view.GetItemName(id);
                        float t = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                        float s = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                        float g = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                        Add(total, name, t);
                        Add(self, name, s);
                        Add(gc, name, g);
                        Add(calls, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls));
                        if (depth >= 2 && depth <= 4) Add(layered, new string(' ', (depth - 2) * 2) + name, t);
                        if (scriptDepth == 1)
                        {
                            Add(scripts, name, t);
                            Add(scriptGc, name, g);
                        }
                        bool isScriptRoot = name == "BehaviourUpdate" || name == "FixedBehaviourUpdate" || name == "LateBehaviourUpdate"
                                            || name == "CoroutinesDelayedCalls" || name == "PreLateUpdate.ScriptRunBehaviourLateUpdate";
                        int childDepth = isScriptRoot ? 1 : scriptDepth > 0 ? scriptDepth + 1 : 0;
                        view.GetItemChildren(id, children);
                        for (int i = 0; i < children.Count; i++) stack.Push((children[i], childDepth, depth + 1));
                    }
                }
            }
            if (frameCount == 0) { report.AppendLine("  (profiler frames not readable)"); return; }

            report.AppendLine($"  Main-thread breakdown (profiler, avg of {frameCount} frames, ms):");
            string[] keys =
            {
                "PlayerLoop", "EditorLoop", "BehaviourUpdate", "FixedBehaviourUpdate", "LateBehaviourUpdate", "CoroutinesDelayedCalls",
                "FixedUpdate.PhysicsFixedUpdate", "Physics.Simulate", "Physics.Processing", "PreLateUpdate.DirectorUpdateAnimationBegin",
                "PreLateUpdate.DirectorUpdateAnimationEnd", "Animators.Update", "PostLateUpdate.FinishFrameRendering", "Render.OpaqueGeometry",
                "Culling", "Shadows.Draw", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "GC.Collect", "UpdateRendererBoundingVolumes",
                "NavMeshManager", "AudioManager.Update", "ParticleSystem.Update", "Camera.Render", "Inl_ScriptableRenderer.Execute"
            };
            foreach (string k in keys)
                if (total.TryGetValue(k, out double v)) report.AppendLine($"    {k,-48} {v / frameCount,8:F3}");

            if (calls.TryGetValue("FixedUpdate.PhysicsFixedUpdate", out double steps))
                report.AppendLine($"    Physics steps per frame: {steps / frameCount:F2} (fixedDeltaTime {Time.fixedDeltaTime:F3}, maximumDeltaTime {Time.maximumDeltaTime:F3})");
            report.AppendLine("  Heaviest PlayerLoop/EditorLoop sections, depth 1-3 (total ms per frame):");
            foreach (KeyValuePair<string, double> kv in Top(layered, 30))
                report.AppendLine($"    {kv.Key,-60} {kv.Value / frameCount,8:F3}");

            report.AppendLine("  Top script entries under Update/FixedUpdate/LateUpdate/coroutines (ms per frame, GC bytes per frame):");
            foreach (KeyValuePair<string, double> kv in Top(scripts, 18))
                report.AppendLine($"    {kv.Key,-60} {kv.Value / frameCount,8:F3}   gc {(scriptGc.TryGetValue(kv.Key, out double sg) ? sg / frameCount : 0),8:F0}");

            report.AppendLine("  Top self-time markers (ms per frame):");
            foreach (KeyValuePair<string, double> kv in Top(self, 22))
                report.AppendLine($"    {kv.Key,-60} {kv.Value / frameCount,8:F3}");

            report.AppendLine("  Top GC allocating markers (bytes per frame):");
            foreach (KeyValuePair<string, double> kv in Top(gc, 10))
                if (kv.Value > 0) report.AppendLine($"    {kv.Key,-60} {kv.Value / frameCount,10:F0}");
        }

        private static void Add(Dictionary<string, double> d, string key, double value)
        {
            d.TryGetValue(key, out double sum);
            d[key] = sum + value;
        }

        private static List<KeyValuePair<string, double>> Top(Dictionary<string, double> d, int count)
        {
            var list = new List<KeyValuePair<string, double>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
            return list;
        }

        private static void ReportCensus()
        {
            report.AppendLine("  Scene census:");
            var updaters = new Dictionary<string, int>();
            int updateCount = 0;
            foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || !mb.isActiveAndEnabled) continue;
                Type t = mb.GetType();
                if (!HasMessage(t, "Update") && !HasMessage(t, "LateUpdate") && !HasMessage(t, "FixedUpdate")) continue;
                updateCount++;
                updaters.TryGetValue(t.Name, out int c);
                updaters[t.Name] = c + 1;
            }
            report.AppendLine($"    Active behaviours with Update/LateUpdate/FixedUpdate: {updateCount}");
            var upd = new List<KeyValuePair<string, int>>(updaters);
            upd.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < Math.Min(14, upd.Count); i++) report.AppendLine($"      {upd[i].Key,-40} {upd[i].Value}");

            int animators = 0, animAlways = 0, animTransforms = 0, animCompletely = 0;
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled) continue;
                animators++;
                if (a.cullingMode == AnimatorCullingMode.AlwaysAnimate) animAlways++;
                else if (a.cullingMode == AnimatorCullingMode.CullUpdateTransforms) animTransforms++;
                else animCompletely++;
            }
            report.AppendLine($"    Animators active {animators}: AlwaysAnimate {animAlways}, CullUpdateTransforms {animTransforms}, CullCompletely {animCompletely}");

            int lights = 0, shadowLights = 0;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled || l.lightmapBakeType == LightmapBakeType.Baked) continue;
                lights++;
                if (l.shadows != LightShadows.None) shadowLights++;
            }
            report.AppendLine($"    Realtime lights enabled {lights} (with shadows {shadowLights})");

            int sources = 0, playing = 0;
            foreach (AudioSource s in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!s.isActiveAndEnabled) continue;
                sources++;
                if (s.isPlaying) playing++;
            }
            report.AppendLine($"    AudioSources enabled {sources}, playing {playing}");

            int bodies = 0, awake = 0;
            foreach (Rigidbody rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if (rb.isKinematic || !rb.gameObject.activeInHierarchy) continue;
                bodies++;
                if (!rb.IsSleeping()) awake++;
            }
            report.AppendLine($"    Dynamic rigidbodies {bodies}, awake {awake}");
            int kinematic = 0, wheels = 0;
            foreach (Rigidbody rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (rb.isKinematic && rb.gameObject.activeInHierarchy) kinematic++;
            foreach (WheelCollider w in UnityEngine.Object.FindObjectsByType<WheelCollider>(FindObjectsSortMode.None))
                if (w.enabled) wheels++;
            report.AppendLine($"    Kinematic rigidbodies {kinematic}; WheelColliders enabled {wheels}");

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            int cars = 0, cars80 = 0, cars160 = 0, carsKinematic = 0;
            foreach (TSTrafficAI ai in UnityEngine.Object.FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None))
            {
                cars++;
                float d = Vector3.Distance(ai.transform.position, camPos);
                if (d <= 80f) cars80++;
                if (d <= 160f) cars160++;
                if (ai.TryGetComponent(out Rigidbody carBody) && carBody.isKinematic) carsKinematic++;
            }
            report.AppendLine($"    Traffic cars active {cars} (within 80 m {cars80}, within 160 m {cars160}, kinematic body {carsKinematic})");
            int peds = 0, peds25 = 0, pedsDown = 0, pedBodies = 0;
            foreach (PedestrianWalker w in UnityEngine.Object.FindObjectsByType<PedestrianWalker>(FindObjectsSortMode.None))
            {
                peds++;
                if (Vector3.Distance(w.transform.position, camPos) <= 25f) peds25++;
                if (w.State == PedestrianWalker.Mode.Down) pedsDown++;
                pedBodies += w.GetComponentsInChildren<Rigidbody>().Length;
            }
            report.AppendLine($"    Pedestrians active {peds} (within 25 m {peds25}, down/dead {pedsDown}); rigidbodies on pedestrians {pedBodies}");

            int particles = 0, particlesPlaying = 0;
            foreach (ParticleSystem p in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                particles++;
                if (p.isPlaying) particlesPlaying++;
            }
            report.AppendLine($"    ParticleSystems active {particles}, playing {particlesPlaying}");

            int rends = 0, rendsVisible = 0, casters = 0, skinned = 0, skinnedVisible = 0, forcedOff = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                rends++;
                if (r.forceRenderingOff) forcedOff++;
                if (r.isVisible) rendsVisible++;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
                if (r is SkinnedMeshRenderer)
                {
                    skinned++;
                    if (r.isVisible) skinnedVisible++;
                }
            }
            report.AppendLine($"    Renderers enabled {rends} (isVisible to any camera {rendsVisible}, forceRenderingOff {forcedOff}, shadow casting {casters}); skinned {skinned} (visible {skinnedVisible})");

            int instances = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.name.EndsWith("(Instance)", StringComparison.Ordinal)) instances++;
            report.AppendLine($"    Runtime material instances named '(Instance)': {instances}");
            long combinedBytes = 0;
            int combinedCount = 0;
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (!mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) continue;
                combinedCount++;
                combinedBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
            }
            report.AppendLine($"    Static batching combined meshes: {combinedCount} ({combinedBytes / (1024.0 * 1024.0):F1} MB)");
            report.AppendLine($"    Cameras rendering: {Camera.allCamerasCount}");
            foreach (Camera c in Camera.allCameras)
                report.AppendLine($"      {c.name} far {c.farClipPlane} target {(c.targetTexture != null ? c.targetTexture.name : "screen")} occlusion {c.useOcclusionCulling}");
        }

        private static bool HasMessage(Type t, string method)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            for (Type c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
                if (c.GetMethod(method, flags | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) != null) return true;
            return false;
        }

        // ---------------------------------------------------------------- driving

        private static void StartMount()
        {
            mounted = false;
            GameObject bikeGo = GameObject.Find(BikePath);
            GameObject playerGo = GameObject.Find(PlayerPath);
            bike = bikeGo != null ? bikeGo.GetComponent<RCCVehicleAdapter>() : null;
            PlayerVehicleInteraction interaction = playerGo != null ? playerGo.GetComponent<PlayerVehicleInteraction>() : null;
            bool requested = bike != null && interaction != null && interaction.RequestEnter(bike);
            report.AppendLine();
            report.AppendLine($"Mount request: {(requested ? "accepted" : "refused")}");
        }

        private static void PrepareDrive()
        {
            mounted = bike != null && bike.IsControlled;
            GameObject playerGo = GameObject.Find(PlayerPath);
            mover = mounted ? bike.transform : playerGo != null ? playerGo.transform : null;
            if (mover == null) return;

            Rigidbody body = mover.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            foreach (Collider c in mover.GetComponentsInChildren<Collider>()) c.enabled = false;

            BuildPath(mover.position, DriveSpeed * DriveSeconds + 80f);
            travelled = 0f;
            pathIndex = 0;
            lastTick = EditorApplication.timeSinceStartup;
            report.AppendLine($"Drive path: {path.Count} points from {(path.Count > 0 ? path[0].ToString("F0") : "-")}");
        }

        private static void BuildPath(Vector3 from, float length)
        {
            path.Clear();
            TSMainManager roads = UnityEngine.Object.FindFirstObjectByType<TSMainManager>();
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
            while (lane >= 0 && total < length && guard++ < 64)
            {
                TSLaneInfo info = roads.lanes[lane];
                for (int p = point; p < info.points.Length; p++) AppendPoint(info.points[p].point, ref total);
                point = 0;
                if (info.connectors == null || info.connectors.Length == 0) break;
                // Straightest connector keeps the route deterministic and on main roads.
                Vector3 heading = path.Count > 1 ? (path[path.Count - 1] - path[path.Count - 2]).normalized : Vector3.forward;
                TSLaneConnector chosen = null;
                float bestDot = -2f;
                foreach (TSLaneConnector con in info.connectors)
                {
                    if (con == null || con.points == null || con.points.Length == 0) continue;
                    Vector3 dir = (con.points[con.points.Length - 1].point - con.points[0].point).normalized;
                    float dot = Vector3.Dot(dir, heading);
                    if (dot > bestDot) { bestDot = dot; chosen = con; }
                }
                if (chosen == null) break;
                foreach (TSConnectorPoint cp in chosen.points) AppendPoint(cp.point, ref total);
                lane = chosen.nextLane;
            }
        }

        private static void AppendPoint(Vector3 p, ref float total)
        {
            if (path.Count > 0)
            {
                float d = Vector3.Distance(path[path.Count - 1], p);
                if (d < 0.5f) return;
                total += d;
            }
            path.Add(p);
        }

        private static void MoveAlongPath()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastTick);
            lastTick = now;
            if (mover == null || path.Count < 2) return;
            float step = DriveSpeed * Mathf.Min(dt, 0.1f);
            Vector3 pos = mover.position;
            while (step > 0f && pathIndex < path.Count - 1)
            {
                Vector3 target = path[pathIndex + 1] + Vector3.up * 0.05f;
                float d = Vector3.Distance(pos, target);
                if (d <= step) { pos = target; step -= d; pathIndex++; }
                else { pos = Vector3.MoveTowards(pos, target, step); step = 0f; }
            }
            Vector3 dir = path[Mathf.Min(pathIndex + 1, path.Count - 1)] - pos;
            dir.y = 0f;
            mover.position = pos;
            if (dir.sqrMagnitude > 0.01f) mover.rotation = Quaternion.Slerp(mover.rotation, Quaternion.LookRotation(dir), 0.25f);
        }

        // ---------------------------------------------------------------- output

        private static void Screenshot(string suffix)
        {
            Directory.CreateDirectory("Captures");
            ScreenCapture.CaptureScreenshot(Path.Combine("Captures", $"perf_{label}_{suffix}.png"));
        }

        private static void Finish(string error)
        {
            if (!running) return;
            running = false;
            sampling = false;
            SessionState.EraseString(GraphicsSettingsManager.EditorProfileOverrideKey);
            EditorApplication.update -= Tick;
            DisposeRecorders();
            ProfilerDriver.enabled = false;
            if (error != null) report.AppendLine("ABORTED: " + error);
            Directory.CreateDirectory("Captures");
            string file = Path.Combine("Captures", $"perf_{label}.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log($"[PerfCapture] Report written to {Path.GetFullPath(file)}");
            bike = null;
            mover = null;
            if (EditorApplication.isPlaying) EditorApplication.delayCall += () => { if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); };
        }
    }
}
