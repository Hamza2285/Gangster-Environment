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
    [Min(.2f)] public float transitionSeconds=8f;
    float nightBlend, targetBlend, blendVelocity;
    bool transitioning;
    readonly CityLightingLook blended=new CityLightingLook();
    float[] flareIntensity;
    public float NightBlend => nightBlend;
    public bool IsTransitioning => transitioning;
#if UNITY_EDITOR
    double editorTime;
    void EditorTick(){double now=UnityEditor.EditorApplication.timeSinceStartup; if(!Application.isPlaying&&transitioning){AdvanceTransition((float)(now-editorTime));UnityEditor.SceneView.RepaintAll();UnityEditor.EditorApplication.QueuePlayerLoopUpdate();}editorTime=now;}
#endif
    float nextLightUpdate;
    public bool IsNight => isNight;
    static readonly int Night=Shader.PropertyToID("_GangstarNight");
    static readonly int Windows=Shader.PropertyToID("_CityNightWindows");
    static readonly int WindowColor=Shader.PropertyToID("_CityNightColor");
    static readonly int Road=Shader.PropertyToID("_CityNightRoadEmission");
    GameObject ownedEventSystem;

    void OnEnable(){
        flareIntensity=new float[sunFlares==null?0:sunFlares.Length];
        for(int i=0;i<flareIntensity.Length;i++){var f=sunFlares[i] as LensFlareComponentSRP;flareIntensity[i]=f?f.intensity:1;}
        ConfigureSky();Apply(isNight);
#if UNITY_EDITOR
        editorTime=UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update+=EditorTick;
#endif
    }
    void ConfigureSky(){if(!presets||!presets.day.skybox||!presets.night.skybox)return;var d=presets.day.skybox;var n=presets.night.skybox;if(d.HasProperty("_Tex"))n.SetTexture("_DayCube",d.GetTexture("_Tex"));if(d.HasProperty("_Tint"))n.SetColor("_DayTint",d.GetColor("_Tint"));if(d.HasProperty("_Exposure"))n.SetFloat("_DayExposure",d.GetFloat("_Exposure"));if(d.HasProperty("_Rotation"))n.SetFloat("_DayRotation",d.GetFloat("_Rotation"));}
    void Start(){
        if(!Application.isPlaying)return;
        Apply(isNight);
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
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update-=EditorTick;
#endif
        transitioning=false;
        if(flareIntensity!=null&&sunFlares!=null)for(int i=0;i<Mathf.Min(flareIntensity.Length,sunFlares.Length);i++){var f=sunFlares[i] as LensFlareComponentSRP;if(f)f.intensity=flareIntensity[i];}
        Shader.SetGlobalFloat(Night,0);
        if(streetLights!=null)foreach(var l in streetLights)if(l)l.enabled=false;
        if(nightEffects)nightEffects.weight=0;
    }
    void Update(){
        if(Application.isPlaying&&transitioning)AdvanceTransition(Time.unscaledDeltaTime);
        if(!Application.isPlaying||nightBlend<.01f||Time.unscaledTime<nextLightUpdate)return;
        nextLightUpdate=Time.unscaledTime+.5f;
        RefreshStreetLights(Camera.main);
    }
    [ContextMenu("Day")] public void ApplyDay(){TransitionTo(false);}
    [ContextMenu("Night")] public void ApplyNight(){TransitionTo(true);}
    public void Apply(bool night){
        if(!presets||!sun)return;
        transitioning=false;blendVelocity=0;nightBlend=targetBlend=night?1:0;isNight=night;Shader.SetGlobalFloat("_CitySkyBlend",nightBlend);if(flareIntensity!=null&&sunFlares!=null)for(int i=0;i<Mathf.Min(flareIntensity.Length,sunFlares.Length);i++){var f=sunFlares[i] as LensFlareComponentSRP;if(f)f.intensity=flareIntensity[i];}ApplyLook(night?presets.night:presets.day,night,true);
    }
    void ApplyLook(CityLightingLook p,bool night,bool endpoint){
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
        if(endpoint&&sunFlares!=null)for(int i=0;i<sunFlares.Length;i++)if(sunFlares[i])
            sunFlares[i].enabled=!night&&dayFlareEnabled!=null&&i<dayFlareEnabled.Length&&dayFlareEnabled[i];
        if(nightEffects){if(nightEffects.sharedProfile!=p.effects)nightEffects.sharedProfile=p.effects;nightEffects.weight=p.effects?1:0;}
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
        if(endpoint&&streetLights!=null)foreach(var l in streetLights)if(l)l.enabled=false;
        if(endpoint&&night)RefreshStreetLights(Camera.main);
        // Rebuild the existing sky reflection only when the user changes the preset.
        // Night uses a precomputed cubemap, never realtime reflection probes.
        if(endpoint&&!night)DynamicGI.UpdateEnvironment();
    }
    public void TransitionTo(bool night){if(!presets||!sun)return;targetBlend=night?1:0;isNight=night;transitioning=Mathf.Abs(targetBlend-nightBlend)>.00001f;
#if UNITY_EDITOR
        editorTime=UnityEditor.EditorApplication.timeSinceStartup;
#endif
    }
    // Analytic critically damped motion preserves velocity when the user reverses direction.
    // Unlike a frame-dependent lerp, this gives the same timing at 30, 60 and 120 FPS.
    public void AdvanceTransition(float seconds){
        if(!transitioning||seconds<=0)return;
        float omega=12f/Mathf.Max(.2f,transitionSeconds);
        float offset=nightBlend-targetBlend;
        float coefficient=blendVelocity+omega*offset;
        float decay=Mathf.Exp(-omega*seconds);
        float value=targetBlend+(offset+coefficient*seconds)*decay;
        blendVelocity=(blendVelocity-omega*coefficient*seconds)*decay;
        if(Mathf.Abs(value-targetBlend)<.0002f&&Mathf.Abs(blendVelocity)<.001f){Apply(targetBlend>.5f);return;}
        PreviewBlend(value);
    }
    static float SmootherStep(float x){x=Mathf.Clamp01(x);return x*x*x*(x*(x*6-15)+10);}
    public void PreviewBlend(float amount){nightBlend=Mathf.Clamp01(amount);var a=presets.day;var b=presets.night;float t=SmootherStep(nightBlend);var p=blended;
        p.skybox=presets.night.skybox;p.lightEnabled=true;p.lightRotation=Quaternion.Slerp(Quaternion.Euler(a.lightRotation),Quaternion.Euler(b.lightRotation),t).eulerAngles;
        p.lightColor=Color.Lerp(a.lightColor,b.lightColor,t);p.lightIntensity=Mathf.Lerp(a.lightIntensity*a.sceneBrightness,b.lightIntensity*b.sceneBrightness,t);p.sceneBrightness=1;p.indirectIntensity=Mathf.Lerp(a.indirectIntensity,b.indirectIntensity,t);
        p.useColorTemperature=a.useColorTemperature;p.colorTemperature=Mathf.Lerp(a.colorTemperature,b.colorTemperature,t);p.shadows=t<.5f?a.shadows:b.shadows;p.shadowStrength=Mathf.Lerp(a.shadowStrength,b.shadowStrength,t)*SmootherStep(Mathf.Abs(t*2-1));
        p.ambientMode=a.ambientMode;p.ambientSky=Color.Lerp(a.ambientSky*a.sceneBrightness,b.ambientSky*b.sceneBrightness,t);p.ambientEquator=Color.Lerp(a.ambientEquator*a.sceneBrightness,b.ambientEquator*b.sceneBrightness,t);p.ambientGround=Color.Lerp(a.ambientGround*a.sceneBrightness,b.ambientGround*b.sceneBrightness,t);p.ambientIntensity=Mathf.Lerp(a.ambientIntensity,b.ambientIntensity,t);
        // Exchange the existing reflection sources at zero contribution, avoiding an endpoint pop.
        var reflectionLook=t<.5f?a:b;
        p.reflectionMode=reflectionLook.reflectionMode;p.reflection=reflectionLook.reflection;
        p.reflectionIntensity=Mathf.Lerp(a.reflectionIntensity,b.reflectionIntensity,t)*SmootherStep(Mathf.Abs(t*2-1));p.reflectionBounces=reflectionLook.reflectionBounces;
        p.fog=a.fog||b.fog;p.fogMode=a.fogMode;p.fogColor=Color.Lerp(a.fogColor,b.fogColor,t);p.fogDensity=Mathf.Lerp(a.fog?a.fogDensity:0,b.fog?b.fogDensity:0,t);p.fogStart=Mathf.Lerp(a.fogStart,b.fogStart,t);p.fogEnd=Mathf.Lerp(a.fogEnd,b.fogEnd,t);
        p.windowLightsEnabled=true;p.windowBrightness=Mathf.Lerp(a.windowLightsEnabled?a.windowBrightness:0,b.windowLightsEnabled?b.windowBrightness:0,t);p.windowWarmth=Mathf.Lerp(a.windowWarmth,b.windowWarmth,t);p.windowColor=Color.Lerp(a.windowColor,b.windowColor,t);p.litWindowAmount=Mathf.Lerp(a.litWindowAmount,b.litWindowAmount,t);p.nightAmount=Mathf.Lerp(a.nightAmount,b.nightAmount,t);
        p.windowColorMix=Mathf.Lerp(a.windowColorMix,b.windowColorMix,t);p.accentBrightness=Mathf.Lerp(a.accentBrightness,b.accentBrightness,t);p.venueBrightness=Mathf.Lerp(a.venueBrightness,b.venueBrightness,t);p.whiteWindowColor=Color.Lerp(a.whiteWindowColor,b.whiteWindowColor,t);p.blueWindowColor=Color.Lerp(a.blueWindowColor,b.blueWindowColor,t);p.blueAccentColor=Color.Lerp(a.blueAccentColor,b.blueAccentColor,t);p.pinkAccentColor=Color.Lerp(a.pinkAccentColor,b.pinkAccentColor,t);p.roadEmission=Mathf.Lerp(a.roadEmission,b.roadEmission,t);p.effects=b.effects;
        ApplyLook(p,false,false);Shader.SetGlobalFloat("_CitySkyBlend",t);
        if(nightEffects)nightEffects.weight=t;
        if(sunFlares!=null)for(int i=0;i<sunFlares.Length;i++){var f=sunFlares[i] as LensFlareComponentSRP;if(f){f.intensity=(flareIntensity!=null&&i<flareIntensity.Length?flareIntensity[i]:1)*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.65f)));f.enabled=f.intensity>.0001f&&dayFlareEnabled!=null&&i<dayFlareEnabled.Length&&dayFlareEnabled[i];}}
        if(nightLampBulbs)nightLampBulbs.enabled=t>.01f;
        if(streetLights!=null)foreach(var l in streetLights)if(l&&l.enabled)l.intensity=presets.streetLightIntensity*t;
    }
    public void RefreshStreetLights(Camera camera){
        if(streetLights==null)return;
        int count=presets&&nightBlend>.01f&&camera?Mathf.Min(presets.nearbyStreetLights,streetLights.Length):0;
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
            light.intensity=presets.streetLightIntensity*SmootherStep(nightBlend);light.range=presets.streetLightRange;
            light.shadows=LightShadows.None;light.enabled=true;
        }
    }
}









