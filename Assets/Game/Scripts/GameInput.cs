using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Single place for gameplay input. Uses the Input System when it is active and
// the old Input Manager otherwise.
public static class GameInput
{
    public static Vector2 Move
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#else
            return Vector2.zero;
#endif
        }
    }

    // Degrees-like mouse movement for this frame.
    public static Vector2 Look
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.delta.ReadValue() * 0.1f;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }
    }

    public static Vector2 PointerPosition
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }
    }

    public static bool Sprint => KeyHeld(KeyCode.LeftShift);
    public static bool JumpDown => KeyDown(KeyCode.Space);
    public static bool InteractDown => KeyDown(KeyCode.E);
    public static bool WhistleDown => KeyDown(KeyCode.T);
    public static bool EnterDown => KeyDown(KeyCode.Return) || KeyDown(KeyCode.KeypadEnter);
    public static bool MapDown => KeyDown(KeyCode.M);
    public static bool CameraDown => KeyDown(KeyCode.C);
    public static bool OutfitDown => KeyDown(KeyCode.O);
    public static bool WatchOptionDown => KeyDown(KeyCode.Alpha1);
    public static bool SkipOptionDown => KeyDown(KeyCode.Alpha2);
    public static bool CancelDown => KeyDown(KeyCode.Escape);

    public static bool PrimaryClickDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }
    }

    public static bool SecondaryClickDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.rightButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(1);
#else
            return false;
#endif
        }
    }

    public static bool PrimaryHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.leftButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }
    }

    // One step per wheel notch: +1 up, -1 down, 0 when not scrolling.
    public static float ScrollStep
    {
        get
        {
            float y = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) y = Mouse.current.scroll.ReadValue().y;
#elif ENABLE_LEGACY_INPUT_MANAGER
            y = Input.mouseScrollDelta.y;
#endif
            return y > 0f ? 1f : y < 0f ? -1f : 0f;
        }
    }

    // Held +/- keys: +1 zooms in, -1 zooms out.
    public static float ZoomKeys => (KeyHeld(KeyCode.Equals) || KeyHeld(KeyCode.KeypadPlus) ? 1f : 0f) - (KeyHeld(KeyCode.Minus) || KeyHeld(KeyCode.KeypadMinus) ? 1f : 0f);

    static bool KeyHeld(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) return ToControl(kb, key).isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(key);
#else
        return false;
#endif
    }

    static bool KeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) return ToControl(kb, key).wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    static UnityEngine.InputSystem.Controls.KeyControl ToControl(Keyboard kb, KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift: return kb.leftShiftKey;
            case KeyCode.Space: return kb.spaceKey;
            case KeyCode.E: return kb.eKey;
            case KeyCode.M: return kb.mKey;
            case KeyCode.C: return kb.cKey;
            case KeyCode.O: return kb.oKey;
            case KeyCode.Alpha1: return kb.digit1Key;
            case KeyCode.Alpha2: return kb.digit2Key;
            case KeyCode.Return: return kb.enterKey;
            case KeyCode.KeypadEnter: return kb.numpadEnterKey;
            case KeyCode.Equals: return kb.equalsKey;
            case KeyCode.Minus: return kb.minusKey;
            case KeyCode.KeypadPlus: return kb.numpadPlusKey;
            case KeyCode.KeypadMinus: return kb.numpadMinusKey;
            default: return kb.escapeKey;
        }
    }
#endif
}
