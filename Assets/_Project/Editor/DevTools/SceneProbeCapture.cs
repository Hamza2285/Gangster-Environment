using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Dev tool: renders a still from any point of the open scene to a PNG (edit or play mode) without touching the scene's
    /// cameras. Fog can be left out for the shot (restored straight after). Used for review captures (Captures/...).
    /// </summary>
    public static class SceneProbeCapture
    {
        /// <summary>Renders from <paramref name="position"/> looking along <paramref name="euler"/>. Ortho size &gt; 0 = top-down orthographic.</summary>
        public static string Render(string path, Vector3 position, Vector3 euler, float fieldOfView = 55f, float orthoSize = 0f,
            bool withoutFog = true, int width = 1280, int height = 720)
        {
            Camera source = Camera.main;
            var go = new GameObject("SceneProbeCapture") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.enabled = false;
            cam.farClipPlane = 4000f;
            cam.nearClipPlane = 0.1f;
            cam.fieldOfView = fieldOfView;
            cam.orthographic = orthoSize > 0f;
            if (cam.orthographic) cam.orthographicSize = orthoSize;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            bool fog = RenderSettings.fog;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (withoutFog) RenderSettings.fog = false;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderSettings.fog = fog;
                if (RenderTexture.active == rt) RenderTexture.active = previous;
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            return path;
        }

        /// <summary>Renders looking at <paramref name="target"/> from <paramref name="position"/>.</summary>
        public static string RenderLookAt(string path, Vector3 position, Vector3 target, float fieldOfView = 55f)
        {
            Vector3 dir = target - position;
            Vector3 euler = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir).eulerAngles : Vector3.zero;
            return Render(path, position, euler, fieldOfView);
        }
    }
}
