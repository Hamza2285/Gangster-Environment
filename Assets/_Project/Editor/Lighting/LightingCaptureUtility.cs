using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Saves lighting comparison screenshots (Captures/Lighting) in play mode: a temporary camera copies the gameplay camera's
    /// settings (clip planes, culling, post processing, volume mask) and renders a fixed viewpoint, so day / night and
    /// before / after shots line up exactly. UI overlays are not included (lighting review only).
    /// </summary>
    public static class LightingCaptureUtility
    {
        public const string OutputFolder = "Captures/Lighting";

        /// <summary>Renders the view from a world position / rotation and saves it as PNG. Returns the file path or an error.</summary>
        public static string CaptureView(string fileName, Vector3 position, Vector3 euler, int width = 1280, int height = 720)
        {
            Camera template = Camera.main;
            if (template == null) return "[LightingCaptureUtility] No main camera.";
            return Render(template, fileName, position, Quaternion.Euler(euler), width, height);
        }

        /// <summary>Renders exactly what the gameplay camera currently sees (e.g. inside an interior).</summary>
        public static string CaptureMainCamera(string fileName, int width = 1280, int height = 720)
        {
            Camera template = Camera.main;
            if (template == null) return "[LightingCaptureUtility] No main camera.";
            return Render(template, fileName, template.transform.position, template.transform.rotation, width, height);
        }

        private static string Render(Camera template, string fileName, Vector3 position, Quaternion rotation, int width, int height)
        {
            var go = new GameObject("LightingCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(template);
                cam.enabled = false;
                go.transform.SetPositionAndRotation(position, rotation);
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                var source = template.GetComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = source == null || source.renderPostProcessing;
                data.volumeLayerMask = source != null ? source.volumeLayerMask : (LayerMask)1;
                data.volumeTrigger = go.transform;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous == rt ? null : previous;
                cam.targetTexture = null;

                Directory.CreateDirectory(OutputFolder);
                string path = $"{OutputFolder}/{fileName}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return path;
            }
            finally
            {
                if (RenderTexture.active == rt) RenderTexture.active = null;
                if (rt != null) Object.DestroyImmediate(rt);
                if (tex != null) Object.DestroyImmediate(tex);
                Object.DestroyImmediate(go);
            }
        }
    }
}
