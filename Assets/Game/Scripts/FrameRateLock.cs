using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Forces 60 fps everywhere (menu, loading, gameplay, interiors): V-Sync off and Application.targetFrameRate = 60, applied
/// before the first scene, after every scene load and re-asserted every frame, so no graphics profile, scene component or
/// plugin can drop the cap to 30. The game still has to be light enough to reach 60 on the device.
/// </summary>
public sealed class FrameRateLock : MonoBehaviour
{
    public const int TargetFps = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        Apply();
        var go = new GameObject("[FrameRateLock]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<FrameRateLock>();
        SceneManager.sceneLoaded += (_, __) => Apply();
    }

    private static void Apply()
    {
        if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
        if (Application.targetFrameRate != TargetFps) Application.targetFrameRate = TargetFps;
    }

    // Checked every frame (two property reads): a menu, graphics profile or plugin that changes the cap is undone on the
    // same frame instead of up to a second later. Also re-applied when the app comes back from the background (Android).
    private void LateUpdate() => Apply();

    private void OnApplicationFocus(bool focus) { if (focus) Apply(); }

    private void OnApplicationPause(bool paused) { if (!paused) Apply(); }
}
