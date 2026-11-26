using System;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Every value <see cref="RealtimeLightingSetup"/> replaced, so the baked lighting can be put back exactly. Editor only (lives
    /// in an Editor folder: its references never reach a build). Do not edit by hand.
    /// </summary>
    public class RealtimeLightingBackup : ScriptableObject
    {
        [Serializable]
        public struct Fog { public bool enabled; public Color color; public float start, end; }

        [Serializable]
        public struct CloudLook { public Color lit, shade, sunGlow; public float opacity, coverage; }

        [Header("Scene")]
        public string scenePath;
        public LightingDataAsset lightingData;
        public CityDayNightPresets presets;
        public int sunLightmapping;
        public bool sunLightmappingWasOverride;
        public bool presetsWereOverride;

        [Header("Baked switcher")]
        public bool switcherEnabled;
        public BakedLightingSet daySet, nightSet;
        public Shader blendShader;

        [Header("Reflection probes")]
        public bool probeSwitcherEnabled;

        [Header("Atmosphere")]
        public Fog dayFog, nightFog;
        public VolumeProfile dayProfile, nightProfile;
        public Material skyMaterial;
        public CloudLook[] clouds;

        [Header("URP asset")]
        public bool softShadows;
        public int softShadowQuality;
        public float shadowDistance;
        public int cascades;
        public int shadowResolution;
    }
}
