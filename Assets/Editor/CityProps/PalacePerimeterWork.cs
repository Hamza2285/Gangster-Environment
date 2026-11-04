using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PalacePerimeterWork
{
    const string Request = "PalacePerimeterWork.request";
    const string Report = "FinalEnvironmentRoadReview/PalacePerimeterWork.txt";
    static PalacePerimeterWork() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        var mode = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (mode == "capture") Capture(); else Audit(); }
        catch (Exception e) { Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report, e.ToString()); Debug.LogException(e); }
    }
    static Scene Scene()
    {
        var s = SceneManager.GetActiveScene();
        if (!s.IsValid() || !s.isLoaded) throw new Exception("The active city scene is not loaded.");
        return s;
    }
    static void Capture()
    {
        var s = Scene(); var camObj = new GameObject("Palace perimeter overview capture") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(camObj, s);
        try
        {
            var c = camObj.AddComponent<Camera>(); c.enabled = false; c.orthographic = true; c.orthographicSize = 1100; c.nearClipPlane = .1f; c.farClipPlane = 8000; c.clearFlags = CameraClearFlags.Skybox;
            c.transform.SetPositionAndRotation(new Vector3(199, 1800, -164), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var rt = RenderTexture.GetTemporary(1400, 1000, 24); var old = RenderTexture.active;
            try { c.targetTexture = rt; c.Render(); RenderTexture.active = rt; var tex = new Texture2D(1400, 1000, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); tex.Apply(); Directory.CreateDirectory("FinalEnvironmentRoadReview"); File.WriteAllBytes("FinalEnvironmentRoadReview/PalacePerimeterOverview.png", tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex); }
            finally { RenderTexture.active = old; c.targetTexture = null; RenderTexture.ReleaseTemporary(rt); }
        }
        finally { UnityEngine.Object.DestroyImmediate(camObj); }
        Audit();
    }
    static void Audit()
    {
        var s = Scene(); var sb = new StringBuilder("SCENE=" + s.path + "\n");
        foreach (var root in s.GetRootGameObjects())
        {
            var r = root.GetComponentsInChildren<Renderer>(true); if (r.Length == 0) continue;
            var b = r.Select(x => x.bounds).Aggregate((a, x) => { a.Encapsulate(x); return a; });
            sb.AppendLine("ROOT " + root.name + " path=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) + " count=" + r.Length + " bounds=" + b);
        }
        foreach (var t in s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => { var n=t.name.ToLowerInvariant(); return n.Contains("coast")||n.Contains("island")||n.Contains("beach")||n.Contains("boundary")||n.Contains("fence")||n.Contains("palace"); }).Take(300))
        {
            var rr=t.GetComponentsInChildren<Renderer>(true); var b=rr.Length==0?new Bounds(t.position,Vector3.zero):rr.Select(x=>x.bounds).Aggregate((a,x)=>{a.Encapsulate(x);return a;});
            sb.AppendLine("MATCH " + t.name + " pos=" + t.position + " bounds=" + b + " source=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report,sb.ToString());
    }
}
