using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName="Gangstar City/Day Night Presets")]
public sealed class CityDayNightPresets : ScriptableObject
{
    public CityLightingLook day = new CityLightingLook();
    public CityLightingLook night = new CityLightingLook();
    [Header("Night street lighting")]
    [Range(0,4)] public int nearbyStreetLights = 4;
    [Min(1)] public float streetLightRange = 22;
    [Min(0)] public float streetLightIntensity = 5;
    public Color streetLightColor = new Color(1f,.66f,.32f);
    [Min(10)] public float streetLightDistance = 100;
}
[System.Serializable]
public sealed class CityLightingLook
{
    [Range(0,1)] public float windowColorMix = 1;
    [Range(0,6)] public float accentBrightness = 1.4f;
    [ColorUsage(false,true)] public Color whiteWindowColor = new Color(.945f,.974f,1);
    [ColorUsage(false,true)] public Color blueWindowColor = new Color(.46f,.68f,1);
    [ColorUsage(false,true)] public Color blueAccentColor = new Color(.272f,.566f,1);
    [ColorUsage(false,true)] public Color pinkAccentColor = new Color(1,.206f,.65f);
    [Range(0,6)] public float venueBrightness = 2.4f;
    public Material skybox;
    public DefaultReflectionMode reflectionMode;
    public Cubemap reflection;
    [Range(0,2)] public float reflectionIntensity=1;
    [Range(1,5)] public int reflectionBounces=1;
    public AmbientMode ambientMode=AmbientMode.Trilight;
    [ColorUsage(false,true)] public Color ambientSky,ambientEquator,ambientGround;
    [Range(0,8)] public float ambientIntensity=1;
    public bool fog;
    public FogMode fogMode=FogMode.ExponentialSquared;
    public Color fogColor;
    [Min(0)] public float fogDensity=.002f;
    public float fogStart,fogEnd=300;
    public Vector3 lightRotation;
    public Color lightColor=Color.white;
    [Min(0)] public float lightIntensity=1, indirectIntensity=1;
    public bool lightEnabled=true, useColorTemperature;
    public float colorTemperature=6500;
    public LightShadows shadows=LightShadows.Soft;
    [Range(0,1)] public float shadowStrength=1;
    [Header("Brightness")]
    [Range(.25f,2f)] public float sceneBrightness=1;
    [Header("Window lights")]
    public bool windowLightsEnabled=true;
    [Range(0,1)] public float windowWarmth=.65f;
    [Range(0,1)] public float litWindowAmount=.65f;
    [Header("Night effects")]
    [Range(0,1)] public float nightAmount;
    [Range(0,12)] public float windowBrightness=1.6f;
    [ColorUsage(false,true)] public Color windowColor=new Color(1,.7f,.35f);
    [Range(0,1)] public float roadEmission=.06f;
    public VolumeProfile effects;
}





