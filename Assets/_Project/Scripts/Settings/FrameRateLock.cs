using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Settings
{
    /// <summary>
    /// Locks the game to 60 fps in every scene (menus and loading too, where no graphics profile exists). vSync is off so the
    /// target is used; physics already steps at 60 per second, so one frame is one physics step. Put back after play in the editor.
    /// </summary>
    public static class FrameRateLock
    {
        private const int TargetFps = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_EDITOR
            int oldVSync = QualitySettings.vSyncCount;
            int oldTarget = Application.targetFrameRate;
            Application.quitting += Restore;
            void Restore()
            {
                Application.quitting -= Restore;
                QualitySettings.vSyncCount = oldVSync;
                Application.targetFrameRate = oldTarget;
            }
#endif
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Apply();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        private static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFps;
        }
    }
}
