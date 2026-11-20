using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Single place for gameplay input. Uses the Input System when it is active and
// the old Input Manager otherwise. On phones the on-screen buttons (MobileGameButtons, Ghost's VirtualButton) press the same
// actions through CrossPlatformInputManager, and a screen tap counts as the primary click.
public static class GameInput
{
    static bool Touch(string button) => UnityStandardAssets.CrossPlatformInput.CrossPlatformInputManager.GetButtonDown(button);
    static bool TouchHeld(string button) => UnityStandardAssets.CrossPlatformInput.CrossPlatformInputManager.GetButton(button);

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

    // A finger on the screen wins over the mouse: on phones (and the Device Simulator) a mouse device may still exist but
    // never moves, so the map read every tap at a stale spot.
    public static bool TouchActive
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame)) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0) return true;
#endif
            return false;
        }
    }

    // Fingers on the screen right now (0 with a mouse).
    public static int TouchCount
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var touch = Touchscreen.current;
            if (touch != null)
            {
                int n = 0;
                foreach (var t in touch.touches) if (t.press.isPressed) n++;
                if (n > 0) return n;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.touchCount;
#else
            return 0;
#endif
        }
    }

    // Screen position of the first two fingers (only meaningful while TouchCount >= 2).
    public static bool TwoFingers(out Vector2 a, out Vector2 b)
    {
        a = b = Vector2.zero;
        int found = 0;
#if ENABLE_INPUT_SYSTEM
        var touch = Touchscreen.current;
        if (touch != null)
        {
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed) continue;
                if (found == 0) a = t.position.ReadValue(); else b = t.position.ReadValue();
                if (++found == 2) return true;
            }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount >= 2)
        {
            a = Input.GetTouch(0).position;
            b = Input.GetTouch(1).position;
            return true;
        }
#endif
        return false;
    }

    /// <summary>"Enter Vehicle  [F]" -> "Enter Vehicle": key hints mean nothing on a touch screen.</summary>
    public static string WithoutKeys(string text) =>
        string.IsNullOrEmpty(text) ? text : System.Text.RegularExpressions.Regex.Replace(text, @"\s*\[[^\]]*\]", "").Trim();

    /// <summary>True while the on-screen phone controls are in use.</summary>
    public static bool MobileControls
    {
        get
        {
            if (controlsMode == null) controlsMode = Object.FindFirstObjectByType<OpenworldCrime.Controls.ControlsModeManager>(FindObjectsInactive.Include);
            return controlsMode != null && controlsMode.IsMobile;
        }
    }
    static OpenworldCrime.Controls.ControlsModeManager controlsMode;

    public static Vector2 PointerPosition
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
                return touch.primaryTouch.position.ReadValue();
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }
    }

    public static bool Sprint => KeyHeld(KeyCode.LeftShift) || TouchHeld("M_Sprint");
    public static bool JumpDown => KeyDown(KeyCode.Space) || Touch("M_Jump");
    public static bool InteractDown => KeyDown(KeyCode.E);
    public static bool HornDown => KeyDown(KeyCode.H) || Touch("M_Horn");
    public static bool WhistleDown => KeyDown(KeyCode.T) || Touch("M_Taxi");
    public static bool EnterDown => KeyDown(KeyCode.Return) || KeyDown(KeyCode.KeypadEnter) || Touch("M_Enter");
    public static bool MapDown => KeyDown(KeyCode.M) || Touch("M_Map");
    public static bool CameraDown => KeyDown(KeyCode.C) || Touch("M_Camera");
    public static bool OutfitDown => KeyDown(KeyCode.O);
    public static bool WatchOptionDown => KeyDown(KeyCode.Alpha1) || Touch("M_Watch");
    public static bool SkipOptionDown => KeyDown(KeyCode.Alpha2) || Touch("M_Skip");
    public static bool CancelDown => KeyDown(KeyCode.Escape) || Touch("M_Cancel");
    // Starting a mission (the ambulance): E, or the START MISSION button.
    public static bool MissionDown => KeyDown(KeyCode.E) || Touch("M_Mission");


    // Menus: arrows or WASD move the selection, Q and E turn the preview.
    public static bool MenuUpDown => KeyDown(KeyCode.UpArrow) || KeyDown(KeyCode.W);
    public static bool MenuDownDown => KeyDown(KeyCode.DownArrow) || KeyDown(KeyCode.S);
    public static bool MenuLeftDown => KeyDown(KeyCode.LeftArrow) || KeyDown(KeyCode.A);
    public static bool MenuRightDown => KeyDown(KeyCode.RightArrow) || KeyDown(KeyCode.D) || KeyDown(KeyCode.Tab);
    public static bool MenuBackDown => KeyDown(KeyCode.Escape) || KeyDown(KeyCode.Backspace);
    public static float TurnKeys => (KeyHeld(KeyCode.E) ? 1f : 0f) - (KeyHeld(KeyCode.Q) ? 1f : 0f);
    public static bool ResetViewDown => KeyDown(KeyCode.R);
    // Holding Alt (or the SWITCH button) opens the character wheel; letting go switches to the picked character.
    public static bool SwitchHeld => KeyHeld(KeyCode.LeftAlt) || KeyHeld(KeyCode.RightAlt) || TouchHeld("M_Switch");
    public static bool SwitchDown => KeyDown(KeyCode.LeftAlt) || KeyDown(KeyCode.RightAlt) || Touch("M_Switch");

    public static bool PrimaryClickDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Touchscreen.current != null) return Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            if (Mouse.current != null) return false;
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
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Touchscreen.current != null) return Touchscreen.current.primaryTouch.press.isPressed;
            if (Mouse.current != null) return false;
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
            case KeyCode.H: return kb.hKey;
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
            case KeyCode.T: return kb.tKey;
            case KeyCode.Q: return kb.qKey;
            case KeyCode.W: return kb.wKey;
            case KeyCode.A: return kb.aKey;
            case KeyCode.S: return kb.sKey;
            case KeyCode.D: return kb.dKey;
            case KeyCode.R: return kb.rKey;
            case KeyCode.Tab: return kb.tabKey;
            case KeyCode.Backspace: return kb.backspaceKey;
            case KeyCode.UpArrow: return kb.upArrowKey;
            case KeyCode.DownArrow: return kb.downArrowKey;
            case KeyCode.LeftArrow: return kb.leftArrowKey;
            case KeyCode.RightArrow: return kb.rightArrowKey;
            case KeyCode.LeftAlt: return kb.leftAltKey;
            case KeyCode.RightAlt: return kb.rightAltKey;
            default: return kb.escapeKey;
        }
    }
#endif
}
