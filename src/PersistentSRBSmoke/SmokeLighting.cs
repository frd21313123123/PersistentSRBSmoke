using UnityEngine;

namespace PersistentSRBSmoke
{
    internal static class SmokeLighting
    {
        public static bool TryGetSunDirection(CelestialBody body, out Vector3 sunDirection)
        {
            sunDirection = Vector3.zero;
            if (body == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                return false;

            CelestialBody sun = FlightGlobals.Bodies[0];
            if (sun == null || sun == body)
                return false;

            Vector3 toSun = sun.transform.position - body.transform.position;
            if (toSun.sqrMagnitude < 1f)
                return false;
            sunDirection = toSun.normalized;
            return true;
        }

        public static Color EvaluateSunTint(float solarElevation, float warmth)
        {
            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.28f, solarElevation));
            Color horizon = Color.Lerp(Color.white, new Color(1.0f, 0.53f, 0.28f, 1f), Mathf.Clamp01(warmth));
            return Color.Lerp(horizon, Color.white, daylight);
        }

        public static float EvaluatePhase(float cosine)
        {
            cosine = Mathf.Clamp(cosine, -1f, 1f);
            return HenyeyGreenstein(cosine, 0.65f) * 0.78f
                + HenyeyGreenstein(cosine, -0.28f) * 0.22f;
        }

        private static float HenyeyGreenstein(float cosine, float anisotropy)
        {
            float g2 = anisotropy * anisotropy;
            float denominator = Mathf.Pow(Mathf.Max(0.001f, 1f + g2 - 2f * anisotropy * cosine), 1.5f);
            return (1f - g2) / denominator;
        }
    }
}
