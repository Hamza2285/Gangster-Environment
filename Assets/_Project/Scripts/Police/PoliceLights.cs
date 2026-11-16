using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Switches a police light bar (Hamza's <see cref="PoliceLightBar"/>, used as-is) on or off from our side. The bar only flashes
    /// while its component is enabled; switched off, its flash panels and street lights are turned dark as well (the bar leaves
    /// them in whatever state the last flash had).
    /// </summary>
    public static class PoliceLights
    {
        public static void SetFlashing(PoliceLightBar bar, bool on)
        {
            if (bar == null || bar.enabled == on && on) return;
            bar.enabled = on;
            if (on) return;
            if (bar.redPanel != null) bar.redPanel.enabled = false;
            if (bar.bluePanel != null) bar.bluePanel.enabled = false;
            if (bar.redLight != null) bar.redLight.enabled = false;
            if (bar.blueLight != null) bar.blueLight.enabled = false;
        }
    }
}
