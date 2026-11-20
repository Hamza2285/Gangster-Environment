using UnityEngine;

// Picks the minimap's shape for the game: round, square or a wide rectangle with blurred edges. Lives on the "Minimap Manager" object at the top of the
// hierarchy. Runs before GameMapUI builds the minimap; changing it while playing switches the shape at once.
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public class MinimapManager : MonoBehaviour
{
    [Tooltip("Which minimap the game uses.")]
    public GameMapUI.MinimapShape shape = GameMapUI.MinimapShape.Rectangle;
    [SerializeField] GameMapUI map;

    void Awake() => Apply();

    void OnValidate() => Apply();

    void Apply()
    {
        if (map == null) map = FindFirstObjectByType<GameMapUI>(FindObjectsInactive.Include);
        if (map == null) return;
        if (Application.isPlaying) map.SetMinimapShape(shape);
        else map.minimapShape = shape;
    }
}
