using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class RooftopSearchlightRotator : MonoBehaviour
{
    [Min(0f)] public float rotationSpeed = 18f;
    [Tooltip("Keeps the beam visible while inspecting the scene in the Editor. Play mode follows the city day/night controller.")]
    public bool showEditorPreview = true;

    static readonly int BeamVisibility = Shader.PropertyToID("_RooftopBeamVisibility");
    static readonly int NightBlend = Shader.PropertyToID("_GangstarNight");
    MeshRenderer beamRenderer;
    MaterialPropertyBlock properties;
    float lastVisibility = -1f;

    void OnEnable()
    {
        beamRenderer = GetComponent<MeshRenderer>();
        properties = new MaterialPropertyBlock();
        ApplyVisibility();
    }

    void OnDisable()
    {
        if (beamRenderer) beamRenderer.forceRenderingOff = false;
    }

    void OnValidate()
    {
        if (beamRenderer) ApplyVisibility();
    }

    void Update()
    {
        ApplyVisibility();
        if (Application.isPlaying && !Mathf.Approximately(rotationSpeed, 0f))
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    void ApplyVisibility()
    {
        if (!beamRenderer) beamRenderer = GetComponent<MeshRenderer>();
        if (!beamRenderer) return;
        beamRenderer.forceRenderingOff = !Application.isPlaying && showEditorPreview;
        float visibility = !Application.isPlaying && showEditorPreview
            ? 1f
            : Mathf.Clamp01(Shader.GetGlobalFloat(NightBlend));
        if (Mathf.Abs(visibility - lastVisibility) < .002f) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        beamRenderer.GetPropertyBlock(properties);
        properties.SetFloat(BeamVisibility, visibility);
        beamRenderer.SetPropertyBlock(properties);
        lastVisibility = visibility;
    }
}

