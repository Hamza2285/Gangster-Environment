using UnityEngine;

// Flashes a police car's roof lights: two quick red flashes, then two quick blue ones.
public class PoliceLightBar : MonoBehaviour
{
    public Renderer redPanel;
    public Renderer bluePanel;
    public Light redLight;
    public Light blueLight;
    public float cycleSeconds = 0.8f;
    [Tooltip("Beyond this distance from the camera only the panels flash; the lights that shine on the street switch off.")]
    public float lightDistance = 90f;

    float offset;
    bool redOn, blueOn, lightsOn;

    void Awake()
    {
        // So that two cars passing each other don't flash in step.
        offset = Random.value * cycleSeconds;
        SetRed(false);
        SetBlue(false);
    }

    void Update()
    {
        float phase = Mathf.Repeat((Time.time + offset) / cycleSeconds, 1f);
        var camera = Camera.main;
        lightsOn = camera != null && (camera.transform.position - transform.position).sqrMagnitude < lightDistance * lightDistance;
        SetRed(phase < 0.1f || (phase >= 0.16f && phase < 0.26f));
        SetBlue((phase >= 0.5f && phase < 0.6f) || (phase >= 0.66f && phase < 0.76f));
    }

    void SetRed(bool on)
    {
        redOn = on;
        redPanel.enabled = on;
        redLight.enabled = on && lightsOn;
    }

    void SetBlue(bool on)
    {
        blueOn = on;
        bluePanel.enabled = on;
        blueLight.enabled = on && lightsOn;
    }

    public bool RedOn => redOn;
    public bool BlueOn => blueOn;
}
