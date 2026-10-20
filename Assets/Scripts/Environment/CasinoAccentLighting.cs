using UnityEngine;
[ExecuteAlways]
public sealed class CasinoAccentLighting : MonoBehaviour
{
    public Light[] accents;
    public float dayIntensity = 8f;
    public float nightIntensity = 180f;
    void LateUpdate()
    {
        if (accents == null) return;
        float strength = Mathf.Lerp(dayIntensity, nightIntensity, Mathf.Clamp01(Shader.GetGlobalFloat("_GangstarNight")));
        foreach (var accent in accents) if (accent) accent.intensity = strength;
    }
}


