using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Test tool: photographs the opening cutscene. Arm it, then press Play: a screenshot every few tenths of a second while it runs,
/// and a log with the drift, the road room and where both heads are on screen (0..1 = in frame).
/// </summary>
[InitializeOnLoad]
public static class IntroCutsceneCapture
{
    const string Key = "IntroCutsceneCapture.Folder";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static float next, ended;
    static int count;
    static string lastPhase;
    static IntroMission mission;

    static IntroCutsceneCapture()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) EditorApplication.update += Tick;
    }

    public static string Arm(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var f in Directory.GetFiles(folder)) if (f.EndsWith(".png") || f.EndsWith("log.txt")) File.Delete(f);
        SessionState.SetString(Key, folder);
        next = ended = 0f;
        count = 0;
        lastPhase = null;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "armed";
    }

    static T Get<T>(string name)
    {
        var field = typeof(IntroMission).GetField(name, F);
        return field != null ? (T)field.GetValue(mission) : (T)typeof(IntroMission).GetProperty(name, F).GetValue(mission, null);
    }

    static void Tick()
    {
        string folder = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(folder)) { EditorApplication.update -= Tick; return; }
        if (!Application.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (mission == null) mission = Object.FindFirstObjectByType<IntroMission>();
        if (mission == null) return;
        string log = folder + "/log.txt";
        string phase = typeof(IntroMission).GetField("phase", F).GetValue(mission).ToString();
        if (phase != lastPhase) { File.AppendAllText(log, Time.time.ToString("F1") + " " + phase + "\n"); lastPhase = phase; }
        if (phase == "LoseCops" && ended <= 0f) ended = Time.unscaledTime;
        if (ended > 0f && Time.unscaledTime - ended > 8f)
        {
            File.AppendAllText(log, "END\n");
            SessionState.EraseString(Key);
            EditorApplication.update -= Tick;
            return;
        }
        if ((phase != "Cutscene" && ended <= 0f) || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.35f;
        var car = Get<GameObject>("car");
        if (car == null) return;
        string line = "f" + count + " t " + Time.unscaledTime.ToString("F1") + " fps " + (1f / Mathf.Max(0.001f, Time.smoothDeltaTime)).ToString("F0")
            + " at " + car.transform.position.ToString("F0") + " v " + car.GetComponent<Rigidbody>().linearVelocity.magnitude.ToString("F0");
        var camera = GameObject.Find("Intro Camera");
        if (camera != null)
            foreach (var head in new[] { "JasonHead", "MiaHead" })
                line += " " + head + " " + camera.GetComponent<Camera>().WorldToViewportPoint(Get<Vector3>(head)).ToString("F2");
        File.AppendAllText(log, line + "\n");
        ScreenCapture.CaptureScreenshot(folder + "/f" + count.ToString("00") + ".png");
        count++;
    }
}
