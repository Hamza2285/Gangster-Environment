using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Trailer
{
    /// <summary>
    /// Trailer helper: renders one view (position, look-at or euler, fov) of the open scene(s) to a PNG with a temporary
    /// camera. Used for location scouting and still checks; works in edit and play mode. Not used by gameplay.
    /// </summary>
    public static class TrailerSnap
    {
        public static void Shot(string path, Vector3 pos, Vector3 lookAt, float fov = 50f, int w = 960, int h = 540, float far = 1500f, bool ortho = false, float orthoSize = 100f)
        {
            var go = new GameObject("TrailerSnapCam");
            try
            {
                go.transform.position = pos;
                if (ortho) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                else go.transform.LookAt(lookAt);
                Render(go, path, fov, w, h, far, ortho, orthoSize);
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void ShotEuler(string path, Vector3 pos, Vector3 euler, float fov = 50f, int w = 960, int h = 540)
        {
            var go = new GameObject("TrailerSnapCam");
            try
            {
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                Render(go, path, fov, w, h, 1500f, false, 0f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void Render(GameObject go, string path, float fov, int w, int h, float far, bool ortho, float orthoSize)
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = far;
            cam.orthographic = ortho;
            cam.orthographicSize = orthoSize;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToJPG(92));
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
