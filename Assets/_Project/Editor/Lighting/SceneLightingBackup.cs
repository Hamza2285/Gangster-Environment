using System;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// One complete lighting state of the gameplay scene (baked data, presets, sky, fog, post profiles, clouds, URP shadow
    /// values), written and read by <see cref="BrightLightingSetup"/> so either look can be put back exactly. Editor only (it
    /// lives in an Editor folder, so its references never reach a build). Do not edit by hand.
    /// </summary>
    public class SceneLightingBackup : ScriptableObject
    {
        [Serializable]
        public struct Fog { public bool enabled; public Color color; public float start, end; }

        [Serializable]
        public struct CloudLook { public Color lit, shade, sunGlow; public float opacity, coverage; }

        [Header("Baked data")]
        public LightingDataAsset lightingData;
        public LightingSettings lightingSettings;
        public bool switcherEnabled;
        public BakedLightingSet daySet, nightSet;
        public Shader blendShader;

        [Header("Look")]
        public CityDayNightPresets presets;
        public bool presetsWereOverride;
        public Material skyMaterial;
        public Fog dayFog, nightFog;
        public VolumeProfile dayProfile, nightProfile;
        public CloudLook[] clouds = new CloudLook[0];

        [Header("URP asset (Mobile_RPAsset)")]
        public bool softShadows;
        public int softShadowQuality;
        public float shadowDistance;
        public int cascades;
        public int shadowResolution;
    }
}
