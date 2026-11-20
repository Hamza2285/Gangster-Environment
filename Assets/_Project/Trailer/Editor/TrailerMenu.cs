using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.Trailer.EditorTools
{
    /// <summary>
    /// Tools > OpenworldCrime > Trailer: plays Gangstar_Gameplay and records the trailer (TrailerDirector) to
    /// Captures/Trailer/. Nothing is saved into the scenes; play mode ends by itself when the render is done.
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerMenu
    {
        private const string Scene = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";
        private const string PendingKey = "OWC.Trailer.Pending";
        private const string ModeKey = "OWC.Trailer.Mode";
        private const string FilterKey = "OWC.Trailer.Filter";

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string OutputDir => Path.Combine(ProjectRoot, "Captures", "Trailer");

        static TrailerMenu()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Tools/OpenworldCrime/Trailer/Render Trailer v2 (45 s) MP4")]
        public static void RenderVideo() => Launch("video", "");

        [MenuItem("Tools/OpenworldCrime/Trailer/Render Trailer v1 (87 s) MP4")]
        public static void RenderVideoV1() => Launch("video-v1", "");

        [MenuItem("Tools/OpenworldCrime/Trailer/Render Stills (every shot)")]
        public static void RenderStills() => Launch("stills", "");

        [MenuItem("Tools/OpenworldCrime/Trailer/Open Output Folder")]
        public static void OpenFolder()
        {
            Directory.CreateDirectory(OutputDir);
            EditorUtility.RevealInFinder(OutputDir);
        }

        public static void Launch(string mode, string filter)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Trailer] Stop play mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ModeKey, mode);
            SessionState.SetString(FilterKey, filter ?? "");
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            string mode = SessionState.GetString(ModeKey, "video");
            TrailerDirector.Begin(MakeSettings(mode, SessionState.GetString(FilterKey, ""), true));
        }

        /// <summary>Settings for a render; also used from scripts (stills of selected shots while iterating).</summary>
        public static TrailerDirector.Settings MakeSettings(string mode, string filter, bool exitWhenDone)
        {
            bool v1 = mode.EndsWith("-v1");
            var s = new TrailerDirector.Settings
            {
                Version = v1 ? 1 : 2,
                Width = v1 ? 1920 : 2880,
                Height = v1 ? 1080 : 1620,
                Letterbox = !v1,
                Crf = v1 ? 18 : 16,
                OutputPath = Path.Combine(OutputDir, v1 ? "GangsterTrailer.mp4" : "GangsterTrailer_v2_45s.mp4"),
                FfmpegPath = FindFfmpeg(),
                ShotFilter = filter ?? "",
                StillsDir = Path.Combine(OutputDir, v1 ? "Stills" : "Stills_v2"),
                ExitPlayModeWhenDone = exitWhenDone
            };
            if (mode.StartsWith("stills")) s.StillFractions = new List<float> { 0.05f, 0.5f, 0.95f };
            if (mode.StartsWith("dense")) s.StillFractions = new List<float> { 0.02f, 0.18f, 0.34f, 0.5f, 0.66f, 0.82f, 0.98f };
            return s;
        }

        /// <summary>ffmpeg.exe: Captures/Trailer/Tools, the project's Tools folder, or a sibling project's bundled copy.</summary>
        public static string FindFfmpeg()
        {
            string[] candidates =
            {
                Path.Combine(OutputDir, "Tools", "ffmpeg.exe"),
                Path.Combine(ProjectRoot, "Tools", "ffmpeg.exe"),
                @"D:\UnityWorkspace\video1\Tools\ffmpeg.exe"
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return "ffmpeg";
        }
    }
}
