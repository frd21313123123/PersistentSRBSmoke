using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class WindModel
    {
        private readonly SmokeSettings _settings;

        public WindModel(SmokeSettings settings)
        {
            _settings = settings;
        }

        public Vector3 GetWind(Vessel vessel, Vector3 up, double universalTime)
        {
            if (!_settings.WindEnabled || vessel == null || vessel.mainBody == null)
                return Vector3.zero;

            float altitude = Mathf.Max(0f, (float)vessel.altitude);
            float normalizedAltitude = Mathf.Clamp01(altitude / Mathf.Max(1f, _settings.WindTopAltitude));

            Vector3 tangentA = Vector3.Cross(up, vessel.mainBody.transform.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float time = (float)(universalTime * _settings.WindTimeScale);
            float layer = altitude / Mathf.Max(100f, _settings.WindLayerHeight);

            float dirNoise = Mathf.PerlinNoise(layer * 0.37f + 11.7f, time + 4.2f) * 2f - 1f;
            float dirNoise2 = Mathf.PerlinNoise(layer * 0.19f + 91.1f, time * 0.73f + 17.3f) * 2f - 1f;
            float angle = (layer * _settings.WindDirectionChangeRadians) + dirNoise * 1.8f + dirNoise2 * 0.7f;

            float speedNoise = Mathf.PerlinNoise(layer * 0.31f + 37.4f, time * 0.9f + 53.2f);
            float altitudeBoost = Mathf.Lerp(0.55f, 1.35f, Mathf.SmoothStep(0f, 1f, normalizedAltitude));
            float speed = _settings.WindSpeed * altitudeBoost * Mathf.Lerp(0.45f, 1.25f, speedNoise);

            return (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) * speed;
        }
    }
}
