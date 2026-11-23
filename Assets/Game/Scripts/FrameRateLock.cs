using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Forces 60 fps everywhere (menu, loading, gameplay, interiors): V-Sync off and Application.targetFrameRate = 60, applied
/// before the first scene, after every scene load and re-asserted each second, so no graphics profile, scene component or
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

    private float next;

    private void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1f;
        Apply();
    }
}
