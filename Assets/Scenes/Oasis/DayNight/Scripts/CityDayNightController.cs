using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

[ExecuteAlways, DisallowMultipleComponent]
public sealed class CityDayNightController : MonoBehaviour
{
    public CityDayNightPresets presets;
    public Light sun;
    public Volume nightEffects;
    public SceneMetaData sceneMetadata;
    public Behaviour[] sunFlares;
    public bool[] dayFlareEnabled;
    public bool originalMetadataFog;
    public Vector3[] streetLightPositions;
    public Light[] streetLights;
    public GameObject controls;
    public Renderer nightLampBulbs;
    public bool showControlsInGame=true;
    [SerializeField] bool isNight;
    float nextLightUpdate;
    public bool IsNight => isNight;
    static readonly int Night=Shader.PropertyToID("_GangstarNight");
    static readonly int Windows=Shader.PropertyToID("_CityNightWindows");
    static readonly int WindowColor=Shader.PropertyToID("_CityNightColor");
    static readonly int Road=Shader.PropertyToID("_CityNightRoadEmission");
    GameObject ownedEventSystem;

    void OnEnable(){Apply(isNight);}
    void Start(){
        if(!Application.isPlaying)return;
        if(!timeOfDay)Apply(isNight); // with the cycle running, it owns the look from its first update
        if(controls)controls.SetActive(showControlsInGame);
        if(showControlsInGame && !EventSystem.current){
            ownedEventSystem=new GameObject("Day Night Input",typeof(EventSystem));
            ownedEventSystem.transform.SetParent(transform,false);
#if ENABLE_INPUT_SYSTEM
            var input=ownedEventSystem.AddComponent<InputSystemUIInputModule>();
            input.AssignDefaultActions();
#else
            ownedEventSystem.AddComponent<StandaloneInputModule>();
#endif
        }
    }
    void OnDisable(){
        Shader.SetGlobalFloat(Night,0);
        if(streetLights!=null)foreach(var l in streetLights)if(l)l.enabled=false;
        if(nightEffects)nightEffects.weight=0;
    }
    void Update(){
        if(!Application.isPlaying||!isNight||Time.unscaledTime<nextLightUpdate)return;
        nextLightUpdate=Time.unscaledTime+.5f;
        RefreshStreetLights(Camera.main);
    }
    [Tooltip("Optional automatic day/night cycle. When set, the Day/Night buttons jump its clock instead of snapping the look.")]
    public OpenworldCrime.Environment.TimeOfDayManager timeOfDay;
    [Tooltip("Optional sky for the middle of the day (the day preset's sky is used near sunrise and sunset).")]
    public Material middaySkybox;
    [Tooltip("Optional baked day/night lightmaps; switched together with the street lamps.")]
    public OpenworldCrime.Environment.BakedLightingSwitcher bakedLighting;
    Material blendSkybox;
    [ContextMenu("Day")] public void ApplyDay(){if(timeOfDay&&Application.isPlaying){timeOfDay.JumpToDay();return;}Apply(false);}
    [ContextMenu("Night")] public void ApplyNight(){if(timeOfDay&&Application.isPlaying){timeOfDay.JumpToNight();return;}Apply(true);}

    /// <summary>
    /// Continuous look between the night (0) and day (1) presets, used by the time-of-day cycle. The sun rotation comes
    /// from the cycle; noon (0..1) lifts the day preset's warm low-sun light towards a whiter, stronger midday light.
    /// Sky/reflection switch only when crossing the half-way point (the environment rebuild is not per frame).
    /// </summary>
    public void ApplyBlend(float day,Quaternion sunRotation,float noon){
        if(!presets||!sun)return;
        day=Mathf.Clamp01(day);noon=Mathf.Clamp01(noon);
        var n=presets.night;var d=presets.day;
        bool nightNow=day<.5f;var side=nightNow?n:d;
        sun.enabled=true;sun.transform.rotation=sunRotation;
        Color dayColor=Color.Lerp(d.lightColor,new Color(1f,.96f,.9f),noon);
        float dayIntensity=d.lightIntensity*d.sceneBrightness*Mathf.Lerp(1f,1.12f,noon);
        sun.color=Color.Lerp(n.lightColor,dayColor,day);
        sun.intensity=Mathf.Lerp(n.lightIntensity*n.sceneBrightness,dayIntensity,day);
        sun.bounceIntensity=Mathf.Lerp(n.indirectIntensity,d.indirectIntensity,day);
        sun.useColorTemperature=false;
        sun.shadows=side.shadows;sun.shadowStrength=Mathf.Lerp(n.shadowStrength,d.shadowStrength,day);
        RenderSettings.ambientMode=d.ambientMode;
        RenderSettings.ambientSkyColor=Color.Lerp(n.ambientSky*n.sceneBrightness,d.ambientSky*d.sceneBrightness,day);
        RenderSettings.ambientEquatorColor=Color.Lerp(n.ambientEquator*n.sceneBrightness,d.ambientEquator*d.sceneBrightness,day);
        RenderSettings.ambientGroundColor=Color.Lerp(n.ambientGround*n.sceneBrightness,d.ambientGround*d.sceneBrightness,day);
        RenderSettings.ambientIntensity=Mathf.Lerp(n.ambientIntensity,d.ambientIntensity,day);
        RenderSettings.reflectionIntensity=Mathf.Lerp(n.reflectionIntensity,d.reflectionIntensity,day);
        RenderSettings.fog=n.fog||d.fog;RenderSettings.fogColor=Color.Lerp(n.fogColor,d.fogColor,day);
        RenderSettings.fogDensity=Mathf.Lerp(n.fogDensity,d.fogDensity,day);
        RenderSettings.fogStartDistance=Mathf.Lerp(n.fogStart,d.fogStart,day);RenderSettings.fogEndDistance=Mathf.Lerp(n.fogEnd,d.fogEnd,day);
        Material sky=nightNow?n.skybox:(middaySkybox&&noon>.45f?middaySkybox:d.skybox);
        if(blendSkybox!=sky){
            blendSkybox=sky;RenderSettings.skybox=sky;
            RenderSettings.defaultReflectionMode=side.reflectionMode;RenderSettings.customReflectionTexture=side.reflection;
            if(sceneMetadata){sceneMetadata.skybox=sky;sceneMetadata.reflection=side.reflection;}
            if(sunFlares!=null)for(int i=0;i<sunFlares.Length;i++)if(sunFlares[i])
                sunFlares[i].enabled=!nightNow&&dayFlareEnabled!=null&&i<dayFlareEnabled.Length&&dayFlareEnabled[i];
            if(!nightNow)DynamicGI.UpdateEnvironment();
        }
        if(nightEffects){nightEffects.sharedProfile=n.effects;nightEffects.weight=n.effects?1f-day:0f;}
        Shader.SetGlobalFloat(Night,Mathf.Lerp(n.nightAmount,d.nightAmount,day));
        Shader.SetGlobalFloat("_CityWindowMix",Mathf.Lerp(n.windowColorMix,d.windowColorMix,day));
        Shader.SetGlobalFloat("_CityAccentBrightness",Mathf.Lerp(n.accentBrightness,d.accentBrightness,day));
        Shader.SetGlobalFloat("_CityVenueBrightness",Mathf.Lerp(n.venueBrightness,d.venueBrightness,day));
        Shader.SetGlobalColor("_CityWhiteWindowColor",Color.Lerp(n.whiteWindowColor,d.whiteWindowColor,day).linear);
        Shader.SetGlobalColor("_CityBlueWindowColor",Color.Lerp(n.blueWindowColor,d.blueWindowColor,day).linear);
        Shader.SetGlobalColor("_CityBlueAccentColor",Color.Lerp(n.blueAccentColor,d.blueAccentColor,day).linear);
        Shader.SetGlobalColor("_CityPinkAccentColor",Color.Lerp(n.pinkAccentColor,d.pinkAccentColor,day).linear);
        float nWin=n.windowLightsEnabled?n.windowBrightness:0,dWin=d.windowLightsEnabled?d.windowBrightness:0;
        Shader.SetGlobalFloat(Windows,Mathf.Lerp(nWin,dWin,day));
        Shader.SetGlobalFloat("_CityWindowCoverage",Mathf.Lerp(n.litWindowAmount,d.litWindowAmount,day));
        Shader.SetGlobalColor(WindowColor,(Color.Lerp(n.windowColor,d.windowColor,day)*Color.Lerp(new Color(.92f,.96f,1),new Color(1,.65f,.32f),Mathf.Lerp(n.windowWarmth,d.windowWarmth,day))).linear);
        Shader.SetGlobalFloat(Road,Mathf.Lerp(n.roadEmission,d.roadEmission,day));
        bool lampsOn=day<.4f;
        if(nightLampBulbs)nightLampBulbs.enabled=lampsOn;
        if(lampsOn!=isNight){
            isNight=lampsOn;
            if(streetLights!=null)foreach(var l in streetLights)if(l)l.enabled=false;
            if(isNight)RefreshStreetLights(Camera.main);
        }
        if(bakedLighting&&Application.isPlaying)bakedLighting.Apply(lampsOn);
    }
    public void Apply(bool night){
        if(!presets||!sun)return;
        blendSkybox=null; // a fixed preset replaces the sky; the next cycle update must set its own again
        isNight=night;var p=night?presets.night:presets.day;
        sun.enabled=p.lightEnabled;sun.transform.rotation=Quaternion.Euler(p.lightRotation);
        sun.color=p.lightColor;sun.intensity=p.lightIntensity*p.sceneBrightness;sun.bounceIntensity=p.indirectIntensity;
        sun.useColorTemperature=p.useColorTemperature;sun.colorTemperature=p.colorTemperature;
        sun.shadows=p.shadows;sun.shadowStrength=p.shadowStrength;
        RenderSettings.skybox=p.skybox;
        RenderSettings.ambientMode=p.ambientMode;RenderSettings.ambientSkyColor=p.ambientSky*p.sceneBrightness;
        RenderSettings.ambientEquatorColor=p.ambientEquator*p.sceneBrightness;RenderSettings.ambientGroundColor=p.ambientGround*p.sceneBrightness;
        RenderSettings.ambientIntensity=p.ambientIntensity;
        RenderSettings.defaultReflectionMode=p.reflectionMode;
        RenderSettings.customReflectionTexture=p.reflection;
        RenderSettings.reflectionIntensity=p.reflectionIntensity;RenderSettings.reflectionBounces=p.reflectionBounces;
        RenderSettings.fog=p.fog;RenderSettings.fogMode=p.fogMode;RenderSettings.fogColor=p.fogColor;
        RenderSettings.fogDensity=p.fogDensity;RenderSettings.fogStartDistance=p.fogStart;RenderSettings.fogEndDistance=p.fogEnd;
        if(sceneMetadata){sceneMetadata.skybox=p.skybox;sceneMetadata.reflection=p.reflection;sceneMetadata.FogEnabled=night?p.fog:originalMetadataFog;}
        if(sunFlares!=null)for(int i=0;i<sunFlares.Length;i++)if(sunFlares[i])
            sunFlares[i].enabled=!night&&dayFlareEnabled!=null&&i<dayFlareEnabled.Length&&dayFlareEnabled[i];
        if(nightEffects){nightEffects.sharedProfile=p.effects;nightEffects.weight=p.effects?1:0;}
        Shader.SetGlobalFloat(Night,p.nightAmount);
        Shader.SetGlobalFloat("_CityWindowMix",p.windowColorMix);
        Shader.SetGlobalFloat("_CityAccentBrightness",p.accentBrightness);
        Shader.SetGlobalFloat("_CityVenueBrightness",p.venueBrightness);
        Shader.SetGlobalColor("_CityWhiteWindowColor",p.whiteWindowColor.linear);
        Shader.SetGlobalColor("_CityBlueWindowColor",p.blueWindowColor.linear);
        Shader.SetGlobalColor("_CityBlueAccentColor",p.blueAccentColor.linear);
        Shader.SetGlobalColor("_CityPinkAccentColor",p.pinkAccentColor.linear);
        Shader.SetGlobalFloat(Windows,p.windowLightsEnabled?p.windowBrightness:0);
        Shader.SetGlobalFloat("_CityWindowCoverage",p.litWindowAmount);
        if(nightLampBulbs)nightLampBulbs.enabled=night;
        Shader.SetGlobalColor(WindowColor,(p.windowColor*Color.Lerp(new Color(.92f,.96f,1),new Color(1,.65f,.32f),p.windowWarmth)).linear);
        Shader.SetGlobalFloat(Road,p.roadEmission);
        if(streetLights!=null)foreach(var l in streetLights)if(l)l.enabled=false;
        if(night)RefreshStreetLights(Camera.main);
        if(bakedLighting&&Application.isPlaying)bakedLighting.Apply(night);
        // Rebuild the existing sky reflection only when the user changes the preset.
        // Night uses a precomputed cubemap, never realtime reflection probes.
        if(!night)DynamicGI.UpdateEnvironment();
    }
    public void RefreshStreetLights(Camera camera){
        if(streetLights==null)return;
        int count=presets&&isNight&&camera?Mathf.Min(presets.nearbyStreetLights,streetLights.Length):0;
        for(int slot=0;slot<streetLights.Length;slot++){
            var light=streetLights[slot];if(!light)continue;light.enabled=false;
            if(slot>=count||streetLightPositions==null)continue;
            float best=presets.streetLightDistance*presets.streetLightDistance;int index=-1;
            for(int i=0;i<streetLightPositions.Length;i++){
                var pos=streetLightPositions[i];bool used=false;
                for(int j=0;j<slot;j++)if(streetLights[j]&&streetLights[j].enabled&&(streetLights[j].transform.position-pos).sqrMagnitude<.01f){used=true;break;}
                if(used)continue;
                float d=(pos-camera.transform.position).sqrMagnitude;if(d<best){best=d;index=i;}
            }
            if(index<0)continue;
            light.transform.position=streetLightPositions[index];light.color=presets.streetLightColor;
            light.intensity=presets.streetLightIntensity;light.range=presets.streetLightRange;
            light.shadows=LightShadows.None;light.enabled=true;
        }
    }
}




