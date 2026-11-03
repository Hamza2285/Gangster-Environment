using UnityEngine;

// Baked road map (see Game > Setup Gameplay In Open Scene) and the world area it covers.
public class MiniMapData : ScriptableObject
{
    public Texture2D roadMap;
    public Vector2 worldMin;
    public float worldSize = 1f;

    // 0..1 across the map.
    public Vector2 WorldToUV(Vector3 world)
    {
        return new Vector2((world.x - worldMin.x) / worldSize, (world.z - worldMin.y) / worldSize);
    }

    public Vector3 UVToWorld(Vector2 uv, float y = 0f)
    {
        return new Vector3(worldMin.x + uv.x * worldSize, y, worldMin.y + uv.y * worldSize);
    }
}
