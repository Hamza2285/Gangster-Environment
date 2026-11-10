using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// How common each paint colour is on traffic and parked cars (configuration only, never changed at runtime).
    /// Every <see cref="VehiclePaintVariants"/> picks among its own model's colours with these relative weights, so a model's
    /// share of black, white or yellow follows this table whatever colours that model offers. Colours are matched by name
    /// (the names Apply Final Car Paint gives the variants, and the colour group of each model's original paint).
    /// Tuning here takes effect on the next pick; no prefab or material has to be rebuilt.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Vehicle Paint Settings", fileName = "VehiclePaintSettings")]
    public class VehiclePaintSettings : ScriptableObject
    {
        [Serializable]
        public struct ColourWeight
        {
            public string colour;
            [Min(0f)] public float weight;
        }

        [Tooltip("Relative weight per colour name. Higher = more common. 0 = never picked (unless a model has nothing else).")]
        [SerializeField] private ColourWeight[] weights =
        {
            new ColourWeight { colour = "Black", weight = 22f },
            new ColourWeight { colour = "White", weight = 20f },
            new ColourWeight { colour = "Silver", weight = 18f },
            new ColourWeight { colour = "Gunmetal", weight = 14f },
            new ColourWeight { colour = "MidnightBlue", weight = 9f },
            new ColourWeight { colour = "DeepRed", weight = 6f },
            new ColourWeight { colour = "RaceRed", weight = 3f },
            new ColourWeight { colour = "BritishGreen", weight = 2f },
            new ColourWeight { colour = "ElectricBlue", weight = 2f },
            new ColourWeight { colour = "Yellow", weight = 1f },
            new ColourWeight { colour = "Orange", weight = 1f }
        };
        [Tooltip("Weight of a colour name that is not in the list.")]
        [SerializeField, Min(0f)] private float unlistedWeight = 2f;

        /// <summary>Relative weight of a colour name.</summary>
        public float GetWeight(string colour)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                if (string.Equals(weights[i].colour, colour, StringComparison.OrdinalIgnoreCase)) return weights[i].weight;
            }
            return unlistedWeight;
        }
    }
}
