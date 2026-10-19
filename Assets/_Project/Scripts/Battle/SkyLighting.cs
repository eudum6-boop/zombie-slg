using UnityEngine;

namespace ZombieSLG.Battle
{
    /// <summary>Day / night / dawn light presets shared by the base and the battle.</summary>
    public static class SkyLighting
    {
        private static readonly Color DaySun = new Color(1.0f, 0.95f, 0.86f);
        private static readonly Color NightSun = new Color(0.62f, 0.70f, 1.0f);
        private static readonly Color DawnSun = new Color(1.0f, 0.74f, 0.52f);
        private static readonly Color DayAmbient = new Color(0.50f, 0.52f, 0.56f);
        private static readonly Color NightAmbient = new Color(0.20f, 0.23f, 0.33f);
        private static readonly Color DawnAmbient = new Color(0.42f, 0.36f, 0.34f);
        private static readonly Color DaySky = new Color(0.16f, 0.18f, 0.20f);
        private static readonly Color NightSky = new Color(0.05f, 0.06f, 0.08f);

        /// <param name="night">0 = full day, 1 = full night.</param>
        public static void DayNight(Light sun, Camera cam, float night)
        {
            night = Mathf.Clamp01(night);
            if (sun != null)
            {
                sun.color = Color.Lerp(DaySun, NightSun, night);
                sun.intensity = Mathf.Lerp(1.35f, 1.0f, night);
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(58f, 52f, night), Mathf.Lerp(-25f, -35f, night), 0f);
            }
            RenderSettings.ambientLight = Color.Lerp(DayAmbient, NightAmbient, night);
            if (cam != null) cam.backgroundColor = Color.Lerp(DaySky, NightSky, night);
        }

        /// <param name="dawn">0 = night, 1 = dawn (end of a won night).</param>
        public static void Dawn(Light sun, float dawn)
        {
            dawn = Mathf.Clamp01(dawn);
            if (sun != null)
            {
                sun.color = Color.Lerp(NightSun, DawnSun, dawn);
                sun.intensity = Mathf.Lerp(1.0f, 1.5f, dawn);
            }
            RenderSettings.ambientLight = Color.Lerp(NightAmbient, DawnAmbient, dawn);
        }
    }
}
