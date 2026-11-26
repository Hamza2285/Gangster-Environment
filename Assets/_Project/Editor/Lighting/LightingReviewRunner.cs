using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Repeatable lighting review (play mode): freezes the clock at noon, sunset, dusk and night, saves a screenshot from fixed
    /// viewpoints (street, downtown, underpass, crossing, overview, character, car) and measures render counters at the first
    /// four with a temporary game-view camera. Vehicles and people are left out of the counters (they move), so runs compare.
    /// Output: Captures/Lighting/{label}_*.png and {label}_stats.txt. Nothing is saved to the scene.
    /// </summary>
    public static class LightingReviewRunner
    {
        private const string MenuPath = "Tools/OpenworldCrime/Lighting/Run Lighting Review (play mode)";
        private static readonly string[] SpotNames = { "street", "downtown", "underpass", "crossing", "overview" };
        private static readonly Vector3[] SpotPositions =
        {
            new Vector3(509f, 30.5f, 211f), new Vector3(300f, 60f, -100f), new Vector3(560f, 28f, 57f),
            new Vector3(-117f, 32f, -56f), new Vector3(500f, 75f, 100f)
        };
        private static readonly Vector3[] SpotEulers =
        {
            new Vector3(3f, 270f, 0f), new Vector3(5f, 45f, 0f), new Vector3(0f, 90f, 0f), new Vector3(2f, 45f, 0f), new Vector3(15f, -40f, 0f)
        };
        private const int MeasuredSpots = 4;
        private const int Samples = 60;
        private static readonly float[] Hours = { 12f, 19f, 19.6f, 22.5f };
        private static readonly string[] HourNames = { "day", "sunset", "dusk", "night" };

        private const string ReviewCameraName = "LightingReviewCamera";

        private static IEnumerator<float> routine;
        private static double resumeAt;
        private static string label;
        // The temporary measuring camera and the main camera it switched off: always cleaned up, also when play mode stops
        // mid-measurement (a leaked review camera renders over the game camera at a fixed spot, see RemoveLeakedReviewCameras).
        private static GameObject reviewCamera;
        private static Camera disabledMain;

        public static bool IsRunning => routine != null;

        [MenuItem(MenuPath)]
        private static void RunMenu() => Run("review_" + DateTime.Now.ToString("HHmmss"));

        /// <summary>Runs in the current play session.</summary>
        public static void Run(string runLabel)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[LightingReview] Enter play mode first.");
                return;
            }
            label = runLabel;
            routine = Steps();
            resumeAt = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying) { Stop(); return; }
            if (EditorApplication.timeSinceStartup < resumeAt) return;
            try
            {
                if (!routine.MoveNext()) { Stop(); return; }
                resumeAt = EditorApplication.timeSinceStartup + routine.Current;
            }
            catch (Exception e) { Debug.LogException(e); Stop(); }
        }

        private static void Stop()
        {
            EditorApplication.update -= Tick;
            routine = null;
            ReleaseReviewCamera();
        }

        private static void ReleaseReviewCamera()
        {
            if (disabledMain != null) disabledMain.enabled = true;
            disabledMain = null;
            if (reviewCamera != null) UnityEngine.Object.DestroyImmediate(reviewCamera);
            reviewCamera = null;
        }

        [InitializeOnLoadMethod]
        private static void RegisterCleanup()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        private static void HandlePlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) Stop();
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode) RemoveLeakedReviewCameras();
        }

        /// <summary>
        /// Removes review cameras left over by older versions of this tool (created DontSave, so they outlived play mode and
        /// kept rendering over the game camera: after getting out of a car the view showed this fixed street spot, no player).
        /// </summary>
        private static void RemoveLeakedReviewCameras()
        {
            foreach (Camera cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (cam == null || cam.name != ReviewCameraName || EditorUtility.IsPersistent(cam)) continue;
                if (reviewCamera != null && cam.gameObject == reviewCamera) continue;
                Debug.Log("[LightingReview] Removed a leftover review camera.");
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }

        private static IEnumerator<float> Steps()
        {
            var sky = UnityEngine.Object.FindFirstObjectByType<SkyCycleController>();
            var so = new SerializedObject(sky);
            so.FindProperty("autoAdvance").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var report = new StringBuilder();
            report.AppendLine($"{label}  pipeline {UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name}  quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");

            for (int h = 0; h < Hours.Length; h++)
            {
                sky.SetHour(Hours[h]);
                yield return 2.5f;
                for (int s = 0; s < SpotNames.Length; s++)
                    LightingCaptureUtility.CaptureView($"{label}_{HourNames[h]}_{SpotNames[s]}", SpotPositions[s], SpotEulers[s]);
                CaptureCharacter(HourNames[h]);
                CaptureCar(HourNames[h]);
                yield return 0.3f;
                if (h != 0 && h != Hours.Length - 1) continue;
                for (int s = 0; s < MeasuredSpots; s++)
                {
                    Camera main = Camera.main;
                    // A plain play-mode object (not DontSave): Unity removes it with the play session whatever happens.
                    var go = new GameObject(ReviewCameraName);
                    reviewCamera = go;
                    var cam = go.AddComponent<Camera>();
                    cam.CopyFrom(main);
                    cam.cullingMask &= ~MovingLayers();
                    go.transform.SetPositionAndRotation(SpotPositions[s], Quaternion.Euler(SpotEulers[s]));
                    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                    main.enabled = false;
                    disabledMain = main;
                    yield return 1.5f;
                    double batches = 0, setPass = 0, tris = 0, casters = 0, frame = 0, render = 0;
                    for (int i = 0; i < Samples; i++)
                    {
                        batches += UnityStats.batches;
                        setPass += UnityStats.setPassCalls;
                        tris += UnityStats.triangles;
                        casters += UnityStats.shadowCasters;
                        frame += UnityStats.frameTime * 1000.0;
                        render += UnityStats.renderTime * 1000.0;
                        yield return 0.02f;
                    }
                    ReleaseReviewCamera();
                    report.AppendLine($"{HourNames[h],-6} {SpotNames[s],-10} batches {batches / Samples:0} setpass {setPass / Samples:0} tris {tris / Samples / 1000:0}k casters {casters / Samples:0} main {frame / Samples:0.00} ms render {render / Samples:0.00} ms");
                    yield return 0.3f;
                }
            }
            long lightmapBytes = 0;
            int lightmapCount = 0;
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (!t.name.Contains("_comp_light") && !t.name.StartsWith("Lightmap Blend")) continue;
                lightmapBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                lightmapCount++;
            }
            report.AppendLine($"lightmaps loaded {lightmapCount}, {lightmapBytes / 1048576f:0.0} MB; LightmapSettings {LightmapSettings.lightmaps.Length}; probes {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}");
            Directory.CreateDirectory(LightingCaptureUtility.OutputFolder);
            File.WriteAllText($"{LightingCaptureUtility.OutputFolder}/{label}_stats.txt", report.ToString());
            Debug.Log("[LightingReview] done\n" + report);
        }

        // Vehicles, people and the played character (it moves to layer 30).
        private static int MovingLayers()
        {
            return LayerMask.GetMask("Traffic AI", "Player", "Enemy", "RCC", "BodyPart", "Vehicle", "VehicleParts", "VehicleHitbox") | (1 << 30);
        }

        private static void CaptureCharacter(string hour)
        {
            GameObject player = GameObject.Find("Player_Jason");
            if (player == null) return;
            Transform t = player.transform;
            Vector3 eye = t.position + t.forward * 2.2f + t.right * 0.6f + Vector3.up * 1.55f;
            LightingCaptureUtility.CaptureView($"{label}_{hour}_character", eye, Quaternion.LookRotation(t.position + Vector3.up * 1.15f - eye).eulerAngles);
        }

        private static void CaptureCar(string hour)
        {
            GameObject parked = GameObject.Find("Parked Vehicles");
            if (parked == null || parked.transform.childCount == 0) return;
            Transform t = parked.transform.GetChild(0);
            Vector3 centre = t.position + Vector3.up * 0.7f;
            Vector3 eye = centre + t.forward * 5.2f + t.right * 3.2f + Vector3.up * 1.2f;
            LightingCaptureUtility.CaptureView($"{label}_{hour}_car", eye, Quaternion.LookRotation(centre - eye).eulerAngles);
        }
    }
}
