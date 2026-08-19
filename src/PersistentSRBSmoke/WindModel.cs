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
        /// Precomputes only the large-scale altitude profile. The smaller-scale spreading field is
        /// evaluated analytically from the particle's local surface coordinates, so neighbouring
        /// smoke cloudlets do not all receive exactly the same velocity at the same altitude.
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

            // Emission can ask for wind every FixedUpdate. Rebuild no faster than the configured
            // dynamic-motion cadence; UpdateDynamicMotion explicitly calls Prepare at that cadence.
            double cacheLifetime = 1.0 / System.Math.Max(1.0, _settings.DynamicMotionHz);
            if (!_prepared || _preparedBody != body || System.Math.Abs(_preparedUniversalTime - universalTime) >= cacheLifetime)
                Prepare(body, universalTime);

            if (!_prepared || _samples == null || _samples.Length == 0)
                return Vector3.zero;

            if (up.sqrMagnitude < 0.001f)
                up = Vector3.up;
            up.Normalize();

            Vector3 bodyNorth = body.transform.up;
            Vector3 tangentA = Vector3.Cross(up, bodyNorth);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            // Catmull-Rom interpolation removes visible transitions between cached altitude samples.
            // The previous linear/layer-driven profile could make the trail appear to change course
            // only at a few distinct heights.
            float topAltitude = Mathf.Max(1f, _settings.WindTopAltitude);
            float normalizedAltitude = Mathf.Clamp01(Mathf.Max(0f, altitude) / topAltitude);
            float samplePosition = normalizedAltitude * (_samples.Length - 1);
            int i1 = Mathf.Clamp(Mathf.FloorToInt(samplePosition), 0, _samples.Length - 1);
            int i2 = Mathf.Min(i1 + 1, _samples.Length - 1);
            int i0 = Mathf.Max(0, i1 - 1);
            int i3 = Mathf.Min(_samples.Length - 1, i2 + 1);
            float blend = samplePosition - i1;

            float x = CatmullRom(_samples[i0].X, _samples[i1].X, _samples[i2].X, _samples[i3].X, blend);
            float y = CatmullRom(_samples[i0].Y, _samples[i1].Y, _samples[i2].Y, _samples[i3].Y, blend);

            // Add a continuous horizontal flow field. It uses spherical surface coordinates derived
            // from the radial up vector, so it remains stable through KSP FloatingOrigin shifts.
            // Neighbouring cloudlets sample slightly different flow and naturally separate instead
            // of an entire altitude band translating as one rigid strip.
            HorizontalWindSample spreading = EvaluateSpreadingField(body, up, altitude, universalTime);
            x += spreading.X;
            y += spreading.Y;

            return tangentA * x + tangentB * y;
        }

        private HorizontalWindSample EvaluateHorizontalSample(float altitude, double universalTime)
        {
            float topAltitude = Mathf.Max(1f, _settings.WindTopAltitude);
            float normalizedAltitude = Mathf.Clamp01(altitude / topAltitude);
            float verticalScale = Mathf.Max(250f, _settings.WindLayerHeight);
            float vertical = altitude / verticalScale;
            float time = (float)(universalTime * _settings.WindTimeScale);

            // Broad, smoothly varying shear instead of a direction that repeatedly rotates once per
            // artificial altitude layer. Two low-frequency noise bands make the whole trail bend
            // gently without producing obvious horizontal shelves.
            float broadDirection = Mathf.PerlinNoise(vertical * 0.31f + 11.7f, time * 0.55f + 4.2f) * 2f - 1f;
            float fineDirection = Mathf.PerlinNoise(vertical * 0.73f + 91.1f, time * 0.31f + 17.3f) * 2f - 1f;
            float prevailingDirection = Mathf.PerlinNoise(7.3f, time * 0.08f + 63.4f) * Mathf.PI * 2f;
            float angle = prevailingDirection
                + _settings.WindDirectionChangeRadians * (broadDirection * 0.68f + fineDirection * 0.32f);

            float speedNoise = Mathf.PerlinNoise(vertical * 0.44f + 37.4f, time * 0.47f + 53.2f);
            float altitudeBoost = Mathf.Lerp(0.82f, 1.18f, Mathf.SmoothStep(0f, 1f, normalizedAltitude));
            float speed = _settings.WindSpeed * altitudeBoost * Mathf.Lerp(0.78f, 1.18f, speedNoise);

            return new HorizontalWindSample
            {
                X = Mathf.Cos(angle) * speed,
                Y = Mathf.Sin(angle) * speed
            };
        }

        private HorizontalWindSample EvaluateSpreadingField(
            CelestialBody body,
            Vector3 up,
            float altitude,
            double universalTime)
        {
            float strength = Mathf.Max(0f, _settings.WindSpreadSpeed);
            if (strength <= 0.0001f)
                return new HorizontalWindSample();

            Vector3 north = body.transform.up.normalized;
            Vector3 axisX = body.transform.right.normalized;
            Vector3 axisY = body.transform.forward.normalized;

            // Convert the radial vector into stable approximate surface coordinates in metres.
            // These coordinates change continuously along and across the smoke trail and do not
            // depend on the floating world-space origin.
            float northDot = Mathf.Clamp(Vector3.Dot(up, north), -1f, 1f);
            float latitude = Mathf.Asin(northDot);
            float longitude = Mathf.Atan2(Vector3.Dot(up, axisY), Vector3.Dot(up, axisX));
            float radius = Mathf.Max(1000f, (float)body.Radius + Mathf.Max(0f, altitude));
            float eastMeters = longitude * radius * Mathf.Max(0.12f, Mathf.Cos(latitude));
            float northMeters = latitude * radius;

            float horizontalScale = Mathf.Max(30f, _settings.WindSpreadScale);
            float verticalScale = Mathf.Max(80f, _settings.WindSpreadVerticalScale);
            float u = eastMeters / horizontalScale;
            float v = northMeters / horizontalScale;
            float z = Mathf.Max(0f, altitude) / verticalScale;
            float time = (float)(universalTime * _settings.WindSpreadTimeScale);

            // A cheap multi-scale curl-like field. It is intentionally not a single dominant side
            // gust: every altitude contains small opposing lobes so a long SRB trail gradually fans
            // out and wrinkles in both horizontal directions.
            float x =
                Mathf.Sin(v * 1.37f + z * 0.83f + time * 1.11f + 0.4f) * 0.62f
                + Mathf.Sin((u + v) * 0.71f - z * 1.29f - time * 0.63f + 2.1f) * 0.28f
                + Mathf.Cos(u * 2.07f + z * 0.31f + time * 1.67f) * 0.10f;

            float y =
                -Mathf.Sin(u * 1.31f - z * 0.91f + time * 1.03f + 1.7f) * 0.62f
                + Mathf.Cos((u - v) * 0.67f + z * 1.17f - time * 0.71f + 4.3f) * 0.28f
                + Mathf.Sin(v * 1.93f - z * 0.37f + time * 1.51f) * 0.10f;

            // Keep the extra field subtle close to the nozzle and slightly stronger with altitude,
            // without creating isolated "wind layers".
            float altitudeWeight = Mathf.Lerp(0.72f, 1.12f,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(altitude / Mathf.Max(1f, _settings.WindTopAltitude))));

            return new HorizontalWindSample
            {
                X = x * strength * altitudeWeight,
                Y = y * strength * altitudeWeight
            };
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
