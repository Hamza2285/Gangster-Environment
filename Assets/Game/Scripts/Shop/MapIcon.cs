using System.Collections.Generic;
using UnityEngine;

// A place shown on the minimap and the full map with its own icon, like a shop.
public class MapIcon : MonoBehaviour
{
    public static readonly List<MapIcon> All = new List<MapIcon>();

    public Sprite icon;
    public string label;
    public Color tint = Color.white;
    public float size = 30f;
    [Tooltip("On the minimap, only shown within this distance of the player. 0 = whenever it fits on the minimap.")]
    public float minimapRange;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }
}
