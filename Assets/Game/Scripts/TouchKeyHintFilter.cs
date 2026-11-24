using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// Phones: keyboard key hints ("Enter Vehicle  [F]", "START  [ENTER]", "Skip  [2 / Enter]") mean nothing on a touch screen.
// Most screens already leave them out in mobile mode; this is the safety net for any label that still carries one: twice a
// second, while the touch controls are on, the "[key]" part is removed from every visible UI text (uGUI and TextMeshPro).
// Starts by itself; does nothing with keyboard controls.
public class TouchKeyHintFilter : MonoBehaviour
{
    // A bracketed key: one key, a key pair ("Q / E"), or a named key; never longer text in brackets.
    static readonly Regex KeyHint = new Regex(
        @"\s*\[(?:[A-Z0-9]|[A-Z0-9] ?/ ?[A-Za-z0-9]+|Enter|ENTER|Esc|ESC|Space|SPACE|Tab|TAB|Shift|SHIFT|Ctrl|CTRL|Backspace|LMB|RMB|Scroll)\]",
        RegexOptions.Compiled);

    const float Interval = 0.5f;
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<TouchKeyHintFilter>() != null) return;
        var go = new GameObject("Touch Key Hint Filter");
        DontDestroyOnLoad(go);
        go.AddComponent<TouchKeyHintFilter>();
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + Interval;
        if (!GameInput.MobileControls) return;

        foreach (var text in FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            string s = text.text;
            if (!string.IsNullOrEmpty(s) && s.IndexOf('[') >= 0) text.text = Strip(s);
        }
        foreach (var text in FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
        {
            string s = text.text;
            if (!string.IsNullOrEmpty(s) && s.IndexOf('[') >= 0) text.text = Strip(s);
        }
    }

    static string Strip(string s)
    {
        string stripped = KeyHint.Replace(s, "");
        return stripped.Length == s.Length ? s : stripped.TrimEnd();
    }
}
