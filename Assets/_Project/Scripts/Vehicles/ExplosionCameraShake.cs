using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Camera shake of a nearby explosion, scaled by distance to the main camera. A static helper (like <see cref="VehicleExplosion"/>):
    /// no component on the camera and no change to whichever controller drives it (Invector on foot, RCC in a car). The offset is
    /// added just before the main camera renders and taken off right after (URP camera callbacks), so camera scripts never see it.
    /// The callbacks are only hooked while a shake runs.
    /// </summary>
    public static class ExplosionCameraShake
    {
        private const float Frequency = 22f;

        private static float amplitude;
        private static float duration;
        private static float endTime;
        private static float seed;
        private static bool hooked;
        private static Camera shakenCamera;
        private static Vector3 appliedOffset;
        private static Quaternion appliedTilt = Quaternion.identity;

        /// <summary>Shakes the main camera: full maxAmplitude (m) at the centre, none beyond radius (m).</summary>
        public static void Shake(Vector3 center, float maxAmplitude, float radius, float seconds)
        {
            Camera camera = Camera.main;
            if (camera == null || maxAmplitude <= 0f || radius <= 0f || seconds <= 0f) return;
            float distance = Vector3.Distance(camera.transform.position, center);
            float share = 1f - Mathf.Clamp01(distance / radius);
            if (share <= 0f) return;
            share *= share;

            // A stronger shake wins over what is left of a running one.
            float remaining = hooked ? Mathf.Clamp01((endTime - Time.time) / duration) * amplitude : 0f;
            amplitude = Mathf.Max(remaining, maxAmplitude * share);
            duration = seconds;
            endTime = Time.time + seconds;
            seed = Random.value * 100f;
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            RenderPipelineManager.endCameraRendering += HandleEndCamera;
        }

        private static void HandleBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || camera != Camera.main) return;
            float left = endTime - Time.time;
            if (left <= 0f)
            {
                Unhook();
                return;
            }
            float decay = left / duration;
            float t = Time.time * Frequency;
            float a = amplitude * decay * decay;
            appliedOffset = new Vector3(Mathf.PerlinNoise(seed, t) - 0.5f, Mathf.PerlinNoise(seed + 7f, t) - 0.5f, 0f) * (2f * a);
            appliedTilt = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(seed + 19f, t) - 0.5f) * 6f * a);
            Transform tr = camera.transform;
            tr.SetPositionAndRotation(tr.position + tr.rotation * appliedOffset, tr.rotation * appliedTilt);
            shakenCamera = camera;
        }

        private static void HandleEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != shakenCamera || camera == null) return;
            Transform tr = camera.transform;
            Quaternion rotation = tr.rotation * Quaternion.Inverse(appliedTilt);
            tr.SetPositionAndRotation(tr.position - rotation * appliedOffset, rotation);
            shakenCamera = null;
        }

        private static void Unhook()
        {
            hooked = false;
            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            RenderPipelineManager.endCameraRendering -= HandleEndCamera;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (hooked) Unhook();
            shakenCamera = null;
            amplitude = 0f;
        }
    }
}
