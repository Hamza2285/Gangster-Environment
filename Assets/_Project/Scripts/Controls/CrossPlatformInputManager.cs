using System.Collections.Generic;
using UnityEngine;

namespace UnityStandardAssets.CrossPlatformInput
{
    /// <summary>
    /// Minimal virtual input registry with the API Invector's GenericInput calls when MOBILE_INPUT is defined
    /// (GetButton / GetButtonDown / GetButtonUp / GetAxis / GetAxisRaw). The project's on-screen controls write into it
    /// (OpenworldCrime.Controls: VirtualButton, VirtualJoystick, VirtualTouchLook); nothing reads hardware here.
    /// Presses are applied by <see cref="Tick"/> at the very start of the frame (ControlsModeManager), so every reader sees
    /// a ButtonDown / ButtonUp for exactly one frame whatever its script execution order.
    /// </summary>
    public static class CrossPlatformInputManager
    {
        private class ButtonState
        {
            public bool Held;
            public bool PendingDown;
            public bool PendingUp;
            public int DownFrame = -1;
            public int UpFrame = -1;
        }

        private static readonly Dictionary<string, ButtonState> buttons = new Dictionary<string, ButtonState>();
        private static readonly Dictionary<string, float> axes = new Dictionary<string, float>();

        public static bool GetButton(string name) => name != null && buttons.TryGetValue(name, out ButtonState b) && b.Held;
        public static bool GetButtonDown(string name) => name != null && buttons.TryGetValue(name, out ButtonState b) && b.DownFrame == Time.frameCount;
        public static bool GetButtonUp(string name) => name != null && buttons.TryGetValue(name, out ButtonState b) && b.UpFrame == Time.frameCount;
        public static float GetAxis(string name) => name != null && axes.TryGetValue(name, out float v) ? v : 0f;
        public static float GetAxisRaw(string name) => GetAxis(name);

        /// <summary>Registers a press; becomes visible as ButtonDown on the next <see cref="Tick"/>.</summary>
        public static void SetButtonDown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            ButtonState b = Get(name);
            b.PendingDown = true;
        }

        /// <summary>Registers a release; becomes visible as ButtonUp on the next <see cref="Tick"/>.</summary>
        public static void SetButtonUp(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            ButtonState b = Get(name);
            b.PendingUp = true;
        }

        public static void SetAxis(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            axes[name] = value;
        }

        public static void SetAxisZero(string name) => SetAxis(name, 0f);

        /// <summary>Applies pending presses and releases for this frame. Call once per frame, before gameplay scripts.</summary>
        public static void Tick()
        {
            int frame = Time.frameCount;
            foreach (ButtonState b in buttons.Values)
            {
                if (b.PendingDown)
                {
                    b.PendingDown = false;
                    b.Held = true;
                    b.DownFrame = frame;
                }
                else if (b.PendingUp)
                {
                    b.PendingUp = false;
                    b.Held = false;
                    b.UpFrame = frame;
                }
            }
        }

        /// <summary>Releases every button and zeroes every axis (switching control mode, scene change).</summary>
        public static void ResetAll()
        {
            foreach (ButtonState b in buttons.Values)
            {
                b.Held = false;
                b.PendingDown = false;
                b.PendingUp = false;
            }
            var keys = new List<string>(axes.Keys);
            for (int i = 0; i < keys.Count; i++) axes[keys[i]] = 0f;
        }

        private static ButtonState Get(string name)
        {
            if (!buttons.TryGetValue(name, out ButtonState b))
            {
                b = new ButtonState();
                buttons.Add(name, b);
            }
            return b;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            buttons.Clear();
            axes.Clear();
        }
    }
}
