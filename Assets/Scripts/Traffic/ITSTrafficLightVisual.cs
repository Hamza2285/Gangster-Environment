using HealthbarGames;
using UnityEngine;

/// <summary>
/// Mirrors the state of an iTS TSTrafficLight (which stops the traffic AI)
/// onto Traffic Lights System visual heads (TrafficLightBase).
/// </summary>
public class ITSTrafficLightVisual : MonoBehaviour
{
    public TSTrafficLight source;
    public TrafficLightBase[] visuals;

    private TSTrafficLight.LightType lastType = TSTrafficLight.LightType.NoLights;
    private bool initialized;

    private void Update()
    {
        if (source == null || visuals == null || source.lights.Count == 0)
            return;

        int index = Mathf.Clamp(source.lightToPlay, 0, source.lights.Count - 1);
        TSTrafficLight.LightType current = source.lights[index].lightType;
        if (initialized && current == lastType)
            return;

        initialized = true;
        lastType = current;
        bool red = current == TSTrafficLight.LightType.Red;
        bool yellow = current == TSTrafficLight.LightType.Yellow;
        bool green = current == TSTrafficLight.LightType.Green;
        foreach (TrafficLightBase visual in visuals)
        {
            if (visual != null)
                visual.ChangeLightState(red, yellow, green, null);
        }
    }
}
