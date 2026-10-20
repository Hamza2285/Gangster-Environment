using UnityEngine;
// Caps Android presentation without changing resolution, textures, effects, or quality.
[DefaultExecutionOrder(1000)]
public sealed class CityAndroidFramePacing : MonoBehaviour {
 [Range(30,120)] public int targetFramesPerSecond=60;
 void Start(){if(Application.platform==RuntimePlatform.Android){QualitySettings.vSyncCount=0;Application.targetFrameRate=targetFramesPerSecond;}}
}
