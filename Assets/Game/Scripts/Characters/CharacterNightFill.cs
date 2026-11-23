using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GTA-style "character light" for the playable characters at night. Away from the street lamps the night light probes leave
/// the player nearly black; this lights only the character (no extra light, no shadow, no cost to the city): it takes the real
/// light probe at the character's position and adds a soft key from the camera side plus a cool moonlight lift, scaled by the
/// day/night blend, and hands it to the character's renderers as custom probe lighting. Daytime is untouched.
/// </summary>
[DisallowMultipleComponent]
public class CharacterNightFill : MonoBehaviour
{
    [Tooltip("Soft key light from the camera side at full night (so the face and the front of the body read).")]
    [SerializeField] private Color keyColor = new Color(0.78f, 0.84f, 1f);
    [SerializeField, Range(0f, 3f)] private float keyIntensity = 1.5f;
    [Tooltip("Flat lift added to the probe lighting at full night (moonlight / city glow).")]
    [SerializeField] private Color ambientLift = new Color(0.24f, 0.26f, 0.32f);
    [Tooltip("How far above the camera direction the key light comes from (0 = straight from the camera).")]
    [SerializeField, Range(0f, 2f)] private float keyHeight = 0.6f;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly Dictionary<Renderer, LightProbeUsage> originalUsage = new Dictionary<Renderer, LightProbeUsage>();
    private readonly SphericalHarmonicsL2[] shArray = new SphericalHarmonicsL2[1];
    private MaterialPropertyBlock block;
    private CityDayNightController dayNight;
    private float nextRendererScan;
    private bool applied;

    private void OnEnable()
    {
        block ??= new MaterialPropertyBlock();
        nextRendererScan = 0f;
    }

    private void OnDisable() => Restore();

    private void LateUpdate()
    {
        if (dayNight == null)
        {
            dayNight = FindFirstObjectByType<CityDayNightController>();
            if (dayNight == null) return;
        }
        float night = dayNight.NightBlend;
        if (night <= 0.01f)
        {
            Restore();
            return;
        }

        // Clothing changes, weapons and vehicles swap renderers: rescan now and then.
        if (Time.unscaledTime >= nextRendererScan) ScanRenderers();
        if (renderers.Count == 0) return;

        Vector3 centre = transform.position + Vector3.up * 1.1f;
        LightProbes.GetInterpolatedProbe(centre, null, out SphericalHarmonicsL2 sh);
        sh.AddAmbientLight(ambientLift * night);
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCamera = cam.transform.position - centre;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.01f) toCamera = -cam.transform.forward;
            Vector3 keyDir = (toCamera.normalized + Vector3.up * keyHeight).normalized;
            sh.AddDirectionalLight(keyDir, keyColor, keyIntensity * night);
        }
        shArray[0] = sh;

        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            Renderer r = renderers[i];
            if (r == null) { renderers.RemoveAt(i); continue; }
            if (!originalUsage.ContainsKey(r)) originalUsage[r] = r.lightProbeUsage;
            r.lightProbeUsage = LightProbeUsage.CustomProvided;
            r.GetPropertyBlock(block);   // keeps whatever else the character sets (paint, tint)
            block.CopySHCoefficientArraysFrom(shArray);
            r.SetPropertyBlock(block);
        }
        applied = true;
    }

    private void ScanRenderers()
    {
        nextRendererScan = Time.unscaledTime + 1.5f;
        renderers.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(false))
            if (r is SkinnedMeshRenderer || r is MeshRenderer) renderers.Add(r);
    }

    private void Restore()
    {
        if (!applied) return;
        applied = false;
        foreach (KeyValuePair<Renderer, LightProbeUsage> pair in originalUsage)
            if (pair.Key != null) pair.Key.lightProbeUsage = pair.Value;
        originalUsage.Clear();
    }
}
