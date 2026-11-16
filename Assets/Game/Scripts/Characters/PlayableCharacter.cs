using System.Collections.Generic;
using UnityEngine;

// One of the characters the player can switch between. Lives on the character's player root (the Invector player).
// Only the character being played is active; the others wait switched off until they are switched to.
public class PlayableCharacter : MonoBehaviour
{
    public static readonly List<PlayableCharacter> All = new List<PlayableCharacter>();

    [Tooltip("Stable id, used for saves and for the clothing store (\"jason\").")]
    public string id = "jason";
    public string displayName = "Jason";
    public Sprite portrait;
    public Color accent = new Color(0.35f, 0.75f, 1f, 1f);
    [Tooltip("Animation for sitting on a bench when the character is found sitting. Humanoid.")]
    public AnimationClip sitClip;

    void Awake()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDestroy()
    {
        All.Remove(this);
    }

    // The character being played right now (the active one).
    public static PlayableCharacter Current
    {
        get
        {
            foreach (var c in All) if (c != null && c.isActiveAndEnabled) return c;
            return null;
        }
    }

    // Whoever is being played, as a character id ("jason" when the player has no PlayableCharacter at all).
    public static string CurrentId
    {
        get
        {
            var current = Current;
            return current != null ? current.id : "jason";
        }
    }
}
