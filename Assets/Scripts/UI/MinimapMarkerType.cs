using UnityEngine;

/// <summary>
/// How one kind of minimap marker looks and behaves (objective, mission, vehicle, enemy, shop...). Pure configuration shared by
/// every <see cref="MinimapMarker"/> of that kind; nothing here changes at runtime.
/// </summary>
[CreateAssetMenu(fileName = "MinimapMarkerType", menuName = "OpenworldCrime/UI/Minimap Marker Type")]
public class MinimapMarkerType : ScriptableObject
{
    [Tooltip("Icon sprite. Tinted with Colour.")]
    [SerializeField] private Sprite icon;
    [SerializeField] private Color colour = Color.white;
    [Tooltip("Icon size in UI units (the minimap is ~260 units wide).")]
    [SerializeField, Min(2f)] private float size = 16f;
    [Tooltip("Off-map markers stay pinned to the rim, pointing the way (missions, objectives). Otherwise they are hidden off-map.")]
    [SerializeField] private bool clampToEdge;
    [Tooltip("The icon turns with the marked object (vehicles, NPC arrows). Otherwise it stays upright.")]
    [SerializeField] private bool rotateWithTarget;
    [Tooltip("Hidden beyond this distance (m) from the player. 0 = no limit.")]
    [SerializeField, Min(0f)] private float maxDistance;
    [Tooltip("Higher values are drawn on top of lower ones.")]
    [SerializeField] private int drawOrder;

    public Sprite Icon => icon;
    public Color Colour => colour;
    public float Size => size;
    public bool ClampToEdge => clampToEdge;
    public bool RotateWithTarget => rotateWithTarget;
    public float MaxDistance => maxDistance;
    public int DrawOrder => drawOrder;
}
