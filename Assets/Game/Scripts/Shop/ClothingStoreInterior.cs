using UnityEngine;

// Root of the clothing store's own scene: where the player arrives, where the fitting area is and where its camera sits.
public class ClothingStoreInterior : MonoBehaviour
{
    public static ClothingStoreInterior Instance { get; private set; }

    [Tooltip("Just inside the door, facing into the store.")]
    public Transform arrival;
    [Tooltip("Where the player stands while trying clothes on, facing the fitting camera.")]
    public Transform fittingSpot;
    [Tooltip("The fitting camera's resting place, looking at the fitting spot.")]
    public Transform fittingCamera;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
