using UnityEngine;
public class RoadAtlasSources : ScriptableObject {
    public Texture2D[] colors = new Texture2D[5];
    public Texture2D[] surfaces = new Texture2D[5];
    public bool pending;
    public string bakedFingerprint;
}

