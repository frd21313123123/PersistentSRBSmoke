using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class WindModel
    {
        private struct HorizontalWindSample
        {
            public float X;
            public float Y;
        }

        private readonly SmokeSettings _settings;
        private HorizontalWindSample[] _samples;
        private CelestialBody _preparedBody;
        private double _preparedUniversalTime;
        private bool _prepared;

        public WindModel(SmokeSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Precomputes the expensive Perlin-based wind profile once per dynamic smoke update.
        /// GetWind then only interpolates two cached samples and builds the local tangent basis.
        /// </summary>
        public void Prepare(CelestialBody body, double universalTime)
        {
            if (!_settings.WindEnabled || body == null)
            {
                _prepared = false;
                return;
            }

            int sampleCount = Mathf.Max(8, _settings.WindCacheLayers);
            if (_samples == null || _samples.Length != sampleCount)
                _samples = new HorizontalWindSample[sampleCount];

            float topAltitude = Mathf.Max(1f, _settings.WindTopAltitude);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
                float altitude = t * topAltitude;
                _samples[i] = EvaluateHorizontalSample(altitude, universalTime);
            }

            _preparedBody = body;
            _preparedUniversalTime = universalTime;
            _prepared = true;
        }

        public Vector3 GetWind(Vessel vessel, Vector3 up, double universalTime)
        {
            if (vessel == null || vessel.mainBody == null)
                return Vector3.zero;

            return GetWind(vessel.mainBody, up, Mathf.Max(0f, (float)vessel.altitude), universalTime);
        }

        public Vector3 GetWind(CelestialBody body, Vector3 up, float altitude, double universalTime)
        {
            if (!_settings.WindEnabled || body == null)
                return Vector3.zero;

            if (!_prepared || _preparedBody != body || System.Math.Abs(_preparedUniversalTime - universalTime) > 0.0001)
                Prepare(body, universalTime);

            if (!_prepared || _samples == null || _samples.Length == 0)
                return Vector3.zero;

            if (up.sqrMagnitude < 0.001f)
                up = Vector3.up;
            up.Normalize();

            Vector3 tangentA = Vector3.Cross(up, body.transform.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float topAltitude = Mathf.Max(1f, _settings.WindTopAltitude);
            float normalizedAltitude = Mathf.Clamp01(Mathf.Max(0f, altitude) / topAltitude);
            float samplePosition = normalizedAltitude * (_samples.Length - 1);
            int lower = Mathf.Clamp(Mathf.FloorToInt(samplePosition), 0, _samples.Length - 1);
            int upper = Mathf.Min(lower + 1, _samples.Length - 1);
            float blend = samplePosition - lower;

            HorizontalWindSample a = _samples[lower];
            HorizontalWindSample b = _samples[upper];
            float x = Mathf.Lerp(a.X, b.X, blend);
            float y = Mathf.Lerp(a.Y, b.Y, blend);

            return tangentA * x + tangentB * y;
        }

        private HorizontalWindSample EvaluateHorizontalSample(float altitude, double universalTime)
        {
            float normalizedAltitude = Mathf.Clamp01(altitude / Mathf.Max(1f, _settings.WindTopAltitude));
            float time = (float)(universalTime * _settings.WindTimeScale);
            float layer = altitude / Mathf.Max(100f, _settings.WindLayerHeight);

            float dirNoise = Mathf.PerlinNoise(layer * 0.37f + 11.7f, time + 4.2f) * 2f - 1f;
            float dirNoise2 = Mathf.PerlinNoise(layer * 0.19f + 91.1f, time * 0.73f + 17.3f) * 2f - 1f;
            float angle = (layer * _settings.WindDirectionChangeRadians) + dirNoise * 1.8f + dirNoise2 * 0.7f;

            float speedNoise = Mathf.PerlinNoise(layer * 0.31f + 37.4f, time * 0.9f + 53.2f);
            float altitudeBoost = Mathf.Lerp(0.55f, 1.35f, Mathf.SmoothStep(0f, 1f, normalizedAltitude));
            float speed = _settings.WindSpeed * altitudeBoost * Mathf.Lerp(0.45f, 1.25f, speedNoise);

            return new HorizontalWindSample
            {
                X = Mathf.Cos(angle) * speed,
                Y = Mathf.Sin(angle) * speed
            };
        }
    }
}
