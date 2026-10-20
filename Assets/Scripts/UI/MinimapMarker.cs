using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts this object on the minimap with the look of its <see cref="MinimapMarkerType"/> (mission, objective, vehicle, enemy...).
/// Markers register themselves while enabled; the <see cref="Minimap"/> listens to <see cref="OnMarkerAdded"/> /
/// <see cref="OnMarkerRemoved"/> and draws them from a pooled icon set. No Update here.
/// </summary>
[DisallowMultipleComponent]
public class MinimapMarker : MonoBehaviour
{
    [SerializeField] private MinimapMarkerType type;
    [Tooltip("Tracked transform. Empty = this object.")]
    [SerializeField] private Transform target;

    private static readonly List<MinimapMarker> active = new List<MinimapMarker>();

    /// <summary>All enabled markers.</summary>
    public static IReadOnlyList<MinimapMarker> Active => active;
    public static event Action<MinimapMarker> OnMarkerAdded;
    public static event Action<MinimapMarker> OnMarkerRemoved;
    /// <summary>Raised when an enabled marker changes its type (e.g. an objective becomes the current one).</summary>
    public static event Action<MinimapMarker> OnMarkerChanged;

    public MinimapMarkerType Type => type;
    public Transform Target => target != null ? target : transform;

    /// <summary>Changes the marker's look at runtime (e.g. from "objective" to "current objective").</summary>
    public void SetType(MinimapMarkerType newType)
    {
        if (newType == type) return;
        type = newType;
        if (!isActiveAndEnabled) return;
        bool registered = active.Contains(this);
        if (type == null)
        {
            if (registered) OnDisable();
        }
        else if (!registered)
        {
            active.Add(this);
            OnMarkerAdded?.Invoke(this);
        }
        else OnMarkerChanged?.Invoke(this);
    }

    private void OnEnable()
    {
        if (type == null)
        {
            Debug.LogWarning($"[{nameof(MinimapMarker)}] '{name}' has no marker type; it is not shown on the minimap.", this);
            return;
        }
        active.Add(this);
        OnMarkerAdded?.Invoke(this);
    }

    private void OnDisable()
    {
        if (!active.Remove(this)) return;
        OnMarkerRemoved?.Invoke(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        OnMarkerAdded = null;
        OnMarkerRemoved = null;
        OnMarkerChanged = null;
    }
}
