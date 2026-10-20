using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Car paints that traffic cars pick from at random each time they spawn (<see cref="TrafficCarPaint"/>).
    /// Pure configuration: nothing here is changed at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "TrafficPaintPalette", menuName = "OpenworldCrime/Vehicles/Traffic Paint Palette")]
    public class TrafficPaintPalette : ScriptableObject
    {
        [Serializable]
        public struct Paint
        {
            [Tooltip("Shown in the Inspector and in diagnostics only.")]
            public string name;
            [Tooltip("Paint colour as it should appear on a fully lit panel.")]
            public Color color;
            [Tooltip("Metallic of the car's body material while this paint is on (solid paints low, metallic paints higher).")]
            [Range(0f, 1f)] public float metallic;
            [Tooltip("Smoothness (clear-coat gloss) of the car's body material while this paint is on.")]
            [Range(0f, 1f)] public float smoothness;
        }

        [SerializeField] private Paint[] paints = new Paint[0];

        public int Count => paints != null ? paints.Length : 0;

        public Paint Get(int index) => paints[index];
    }
}
