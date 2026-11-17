using UnityEngine;

// Root of the car showroom's own scene: where the player arrives on walking in.
public class ShowroomInterior : MonoBehaviour
{
    public static ShowroomInterior Instance { get; private set; }

    [Tooltip("Just inside the entrance, facing into the showroom.")]
    public Transform arrival;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
