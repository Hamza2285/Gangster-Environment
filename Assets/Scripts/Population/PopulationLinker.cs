using UnityEngine;

// Root of the Population prefab (traffic + pedestrians). A prefab can't keep links to objects in a scene, so
// the spawners are saved switched off; when the scene starts this finds the iTS road manager, the camera and
// the player, hands them to the spawners and only then switches them on.
[DefaultExecutionOrder(-500)]
public class PopulationLinker : MonoBehaviour
{
    public TSTrafficSpawner traffic;
    public PedestrianSpawner pedestrians;

    [Tooltip("Left empty: the iTS road manager in the scene.")]
    public TSMainManager roads;
    [Tooltip("Left empty: the object tagged Player (its CharacterController if it has one), else the main camera.")]
    public Transform player;

    private void Awake()
    {
        if (roads == null) roads = FindFirstObjectByType<TSMainManager>();
        if (roads == null)
        {
            Debug.LogError("[Population] No iTS road manager (TSMainManager) in the scene, so no traffic or pedestrians.", this);
            return;
        }
        if (player == null) player = FindPlayer();

        if (traffic != null)
        {
            traffic.manager = roads;
            // Cars don't pop into existence right next to the player.
            if (traffic.transformSpawningCheck != null)
                foreach (var check in traffic.transformSpawningCheck)
                    if (check != null) check.transform = player;
        }
        if (pedestrians != null && player != null) pedestrians.player = player;

        if (traffic != null) traffic.gameObject.SetActive(true);
        if (pedestrians != null) pedestrians.gameObject.SetActive(true);
    }

    private static Transform FindPlayer()
    {
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            if (cc.CompareTag("Player")) return cc.transform;
        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }
}
