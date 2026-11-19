using OpenworldCrime.Controls;
using UnityEngine;

// One place to pick the controls: tick Mobile Controls (on-screen joystick and buttons) or PC Controls (keyboard and mouse).
// Exactly one is on: ticking one unticks the other, and unticking one ticks the other. The choice drives the scene's
// ControlsModeManager (which shows the touch controls, sets Invector's and RCC's input), at start and, in play mode, the moment
// a box is ticked. On a phone or tablet the touch controls are always used, whatever is ticked here.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10001)]   // before ControlsModeManager (-10000) reads its mode
public class ControlsManager : MonoBehaviour
{
    [Tooltip("On-screen joystick and buttons (touch).")]
    public bool mobileControls;
    [Tooltip("Keyboard and mouse, no on-screen buttons.")]
    public bool pcControls = true;

    [Tooltip("The scene's controls mode manager (found automatically when empty).")]
    [SerializeField] private ControlsModeManager modeManager;

    // What the boxes were last time, to tell which one was just changed.
    [SerializeField, HideInInspector] private bool lastMobile;
    [SerializeField, HideInInspector] private bool lastPc = true;

    public bool UseMobile => mobileControls;

    private void Awake()
    {
        Resolve();
        Enforce();
        if (modeManager == null) return;
#if UNITY_EDITOR
        // The mode manager starts in whatever mode its own field says: make that this choice before it starts.
        WriteManagerField();
#endif
    }

    private void Start()
    {
        if (modeManager != null && modeManager.IsMobile != mobileControls && !Application.isMobilePlatform) modeManager.SetMobile(mobileControls);
    }

    // Keeps exactly one box ticked.
    private void Enforce()
    {
        if (mobileControls && pcControls)
        {
            // Both on: the one just ticked wins.
            if (mobileControls != lastMobile) pcControls = false;
            else mobileControls = false;
        }
        else if (!mobileControls && !pcControls)
        {
            // Both off: the other one comes back on.
            if (lastMobile) pcControls = true;
            else mobileControls = true;
        }
        lastMobile = mobileControls;
        lastPc = pcControls;
    }

    private void Resolve()
    {
        if (modeManager == null) modeManager = FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Enforce();
        Resolve();
        if (modeManager == null) return;
        if (Application.isPlaying)
        {
            if (modeManager.IsMobile != mobileControls) modeManager.SetMobile(mobileControls);
            return;
        }
        // In the editor the mode manager's own checkbox follows these boxes (saved with the scene).
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && modeManager != null) WriteManagerField();
        };
    }

    private void WriteManagerField()
    {
        var serialized = new UnityEditor.SerializedObject(modeManager);
        var field = serialized.FindProperty("useMobileControls");
        if (field == null || field.boolValue == mobileControls) return;
        field.boolValue = mobileControls;
        if (Application.isPlaying) serialized.ApplyModifiedPropertiesWithoutUndo();
        else serialized.ApplyModifiedProperties();
    }
#endif
}
