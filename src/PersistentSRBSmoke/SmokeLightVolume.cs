using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// A compact, independently implemented approximation of the light-volume architecture used
    /// by modern volumetric renderers. Smoke optical depth is accumulated into body-relative cells;
    /// direct sunlight and ambient multiple scattering are then refreshed in separate time slices.
    /// No EVE code, shader or texture is used here.
    /// </summary>
    internal sealed class SmokeLightVolume
    {
        private struct CellKey : IEquatable<CellKey>
        {
            public int X;
            public int Y;
            public int Z;

            public CellKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(CellKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey && Equals((CellKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X * 73856093;
                    hash ^= Y * 19349663;
                    hash ^= Z * 83492791;
                    return hash;
                }
            }
        }

        private sealed class CellData
        {
            public int FrameStamp;
            public float Density;
            public float DirectLight = 1f;
            public float AmbientLight = 1f;
            public bool DirectInitialized;
            public bool AmbientInitialized;
        }

        private readonly SmokeSettings _settings;
        private readonly Dictionary<CellKey, CellData> _cells = new Dictionary<CellKey, CellData>(4096);
        private readonly List<CellKey> _activeKeys = new List<CellKey>(4096);
        private readonly List<CellKey> _staleKeys = new List<CellKey>(1024);

        private int _frameIndex;
        private float _cellSize;
        private Vector3 _sunDirection;

        public SmokeLightVolume(SmokeSettings settings)
        {
            _settings = settings;
            _cellSize = Mathf.Max(8f, settings.LightVolumeCellSize);
        }

        public void Rebuild(
            ParticleSystem.Particle[] particles,
            int count,
            Vector3 bodyCenter,
            Vector3 sunDirection)
        {
            if (!_settings.LightVolumeEnabled || particles == null || count <= 0)
                return;

            _frameIndex = (_frameIndex + 1) & 0x3FFFFFFF;
            _cellSize = Mathf.Max(8f, _settings.LightVolumeCellSize);
            _sunDirection = sunDirection.sqrMagnitude > 0.001f
                ? sunDirection.normalized
                : Vector3.zero;
            _activeKeys.Clear();

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = particles[i];
                if (particle.remainingLifetime <= 0f || particle.startLifetime <= 0.001f)
                    continue;

                float age = Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);
                Color32 startColor = particle.startColor;
                float alpha = (startColor.a / 255f) * EvaluateTrailAlpha(age);
                if (alpha <= 0.003f)
                    continue;

                Vector3 relativePosition = particle.position - bodyCenter;
                CellKey key = GetKey(relativePosition);
                CellData cell;
                if (!_cells.TryGetValue(key, out cell))
                {
                    cell = new CellData();
                    _cells.Add(key, cell);
                }

                if (cell.FrameStamp != _frameIndex)
                {
                    cell.FrameStamp = _frameIndex;
                    cell.Density = 0f;
                    _activeKeys.Add(key);
                }

                float currentSize = Mathf.Max(
                    0.5f,
                    particle.startSize * EvaluateTrailExpansion(age, _settings.SizeGrowth));
                float projectedCoverage = Mathf.Clamp(
                    currentSize * currentSize / (_cellSize * _cellSize),
                    0.15f,
                    4f);
                float opticalDepth = -Mathf.Log(Mathf.Max(0.001f, 1f - Mathf.Clamp01(alpha)));
                cell.Density += opticalDepth * projectedCoverage;
            }

            UpdateLightSlices();
            CleanupStaleCells();
        }

        public Color SampleLighting(
            Vector3 worldPosition,
            Vector3 bodyCenter,
            float bodyRadius,
            Vector3 viewDirection)
        {
            if (!_settings.LightVolumeEnabled)
                return Color.white;

            Vector3 relativePosition = worldPosition - bodyCenter;
            CellData cell;
            float direct = 1f;
            float ambient = 1f;
            if (_cells.TryGetValue(GetKey(relativePosition), out cell)
                && cell.FrameStamp == _frameIndex)
            {
                direct = cell.DirectLight;
                ambient = cell.AmbientLight;
            }

            if (_sunDirection.sqrMagnitude < 0.001f)
            {
                float noSun = Mathf.Clamp(_settings.LightNightBrightness * ambient, 0.04f, 1f);
                return new Color(noSun * 0.72f, noSun * 0.80f, noSun, 1f);
            }

            float radialMagnitude = relativePosition.magnitude;
            Vector3 up = radialMagnitude > 1f ? relativePosition / radialMagnitude : Vector3.up;
            float solarVisibility = SmokeLightModel.EvaluateSolarVisibility(
                radialMagnitude,
                bodyRadius,
                up,
                _sunDirection);
            float solarElevation = Vector3.Dot(up, _sunDirection);
            Color sunTint = SmokeLightModel.EvaluateSunTint(solarElevation, _settings.LightSunsetWarmth);

            Vector3 toCamera = viewDirection.sqrMagnitude > 0.001f
                ? viewDirection.normalized
                : -_sunDirection;
            float scatteringCosine = Vector3.Dot(-_sunDirection, toCamera);
            float phase = SmokeLightModel.EvaluatePhase(scatteringCosine);
            float phaseMultiplier = Mathf.Lerp(
                1f,
                Mathf.Clamp(phase, 0.35f, 2.5f),
                Mathf.Clamp01(_settings.LightPhaseStrength));

            float dayAmbient = _settings.LightAmbientBrightness * ambient;
            float nightAmbient = _settings.LightNightBrightness * ambient;
            float ambientAmount = Mathf.Lerp(nightAmbient, dayAmbient, solarVisibility);
            Color ambientTint = Color.Lerp(
                new Color(0.54f, 0.64f, 0.82f, 1f),
                new Color(0.86f, 0.90f, 0.98f, 1f),
                solarVisibility);

            float directAmount = _settings.LightDirectBrightness
                * direct
                * solarVisibility
                * phaseMultiplier;
            Color result = ambientTint * ambientAmount + sunTint * directAmount;
            result.r = Mathf.Clamp(result.r, 0.04f, 1.30f);
            result.g = Mathf.Clamp(result.g, 0.04f, 1.30f);
            result.b = Mathf.Clamp(result.b, 0.05f, 1.35f);
            result.a = 1f;
            return result;
        }

        private void UpdateLightSlices()
        {
            int directSlices = Mathf.Max(1, _settings.LightDirectTimeSlices);
            int ambientSlices = Mathf.Max(1, _settings.LightAmbientTimeSlices);
            int directPhase = _frameIndex % directSlices;
            int ambientPhase = _frameIndex % ambientSlices;

            for (int i = 0; i < _activeKeys.Count; i++)
            {
                CellKey key = _activeKeys[i];
                CellData cell = _cells[key];
                uint hash = HashKey(key);

                if (!cell.DirectInitialized || (hash % (uint)directSlices) == (uint)directPhase)
                    UpdateDirectLight(key, cell);

                if (!cell.AmbientInitialized || (hash % (uint)ambientSlices) == (uint)ambientPhase)
                    UpdateAmbientLight(key, cell);
            }
        }

        private void UpdateDirectLight(CellKey key, CellData cell)
        {
            float target = 1f;
            if (_sunDirection.sqrMagnitude > 0.001f)
            {
                int steps = Mathf.Max(1, _settings.LightMarchSteps);
                float marchDistance = Mathf.Max(_cellSize, _settings.LightMarchDistance);
                float stepDistance = marchDistance / steps;
                float jitter = 0.30f + HashToUnit(HashKey(key) ^ 0x9E3779B9U) * 0.55f;
                Vector3 centre = GetCellCenter(key);
                float accumulatedDensity = 0f;

                for (int step = 0; step < steps; step++)
                {
                    float distance = stepDistance * (step + jitter);
                    accumulatedDensity += GetDensity(centre + _sunDirection * distance);
                }

                float meanDensity = accumulatedDensity / steps;
                float transmittance = Mathf.Exp(
                    -meanDensity * Mathf.Max(0f, _settings.LightExtinction));

                float dx = GetDensity(new CellKey(key.X + 1, key.Y, key.Z))
                    - GetDensity(new CellKey(key.X - 1, key.Y, key.Z));
                float dy = GetDensity(new CellKey(key.X, key.Y + 1, key.Z))
                    - GetDensity(new CellKey(key.X, key.Y - 1, key.Z));
                float dz = GetDensity(new CellKey(key.X, key.Y, key.Z + 1))
                    - GetDensity(new CellKey(key.X, key.Y, key.Z - 1));
                Vector3 densityGradient = new Vector3(dx, dy, dz);
                float wrappedLambert = 0.84f;
                if (densityGradient.sqrMagnitude > 0.0001f)
                {
                    Vector3 outwardNormal = -densityGradient.normalized;
                    float wrapped = Mathf.Clamp01(Vector3.Dot(outwardNormal, _sunDirection) * 0.5f + 0.5f);
                    wrappedLambert = Mathf.Lerp(0.52f, 1f, wrapped);
                }

                target = Mathf.Clamp(
                    transmittance * wrappedLambert,
                    Mathf.Clamp01(_settings.LightMinimumDirect),
                    1f);
            }

            cell.DirectLight = cell.DirectInitialized
                ? Mathf.Lerp(cell.DirectLight, target, 0.62f)
                : target;
            cell.DirectInitialized = true;
        }

        private void UpdateAmbientLight(CellKey key, CellData cell)
        {
            float neighbourDensity =
                GetDensity(new CellKey(key.X + 1, key.Y, key.Z))
                + GetDensity(new CellKey(key.X - 1, key.Y, key.Z))
                + GetDensity(new CellKey(key.X, key.Y + 1, key.Z))
                + GetDensity(new CellKey(key.X, key.Y - 1, key.Z))
                + GetDensity(new CellKey(key.X, key.Y, key.Z + 1))
                + GetDensity(new CellKey(key.X, key.Y, key.Z - 1));
            neighbourDensity /= 6f;

            float combinedDensity = cell.Density * 0.62f + neighbourDensity * 0.38f;
            float densityT = Mathf.Clamp01(
                combinedDensity / Mathf.Max(0.1f, _settings.LightDensitySaturation));
            float target = Mathf.Lerp(1f, _settings.LightMinimumAmbient, Mathf.Sqrt(densityT));
            cell.AmbientLight = cell.AmbientInitialized
                ? Mathf.Lerp(cell.AmbientLight, target, 0.45f)
                : target;
            cell.AmbientInitialized = true;
        }

        private float GetDensity(Vector3 relativePosition)
        {
            return GetDensity(GetKey(relativePosition));
        }

        private float GetDensity(CellKey key)
        {
            CellData cell;
            return _cells.TryGetValue(key, out cell) && cell.FrameStamp == _frameIndex
                ? cell.Density
                : 0f;
        }

        private CellKey GetKey(Vector3 relativePosition)
        {
            return new CellKey(
                Mathf.FloorToInt(relativePosition.x / _cellSize),
                Mathf.FloorToInt(relativePosition.y / _cellSize),
                Mathf.FloorToInt(relativePosition.z / _cellSize));
        }

        private Vector3 GetCellCenter(CellKey key)
        {
            return new Vector3(
                (key.X + 0.5f) * _cellSize,
                (key.Y + 0.5f) * _cellSize,
                (key.Z + 0.5f) * _cellSize);
        }

        private void CleanupStaleCells()
        {
            if ((_frameIndex & 31) != 0 || _cells.Count < _activeKeys.Count * 2 + 512)
                return;

            _staleKeys.Clear();
            foreach (KeyValuePair<CellKey, CellData> pair in _cells)
            {
                if (_frameIndex - pair.Value.FrameStamp > 16)
                    _staleKeys.Add(pair.Key);
            }

            for (int i = 0; i < _staleKeys.Count; i++)
                _cells.Remove(_staleKeys[i]);
        }

        private static float EvaluateTrailAlpha(float age)
        {
            age = Mathf.Clamp01(age);
            if (age <= 0.08f)
                return Mathf.Lerp(0.98f, 0.90f, Mathf.SmoothStep(0f, 1f, age / 0.08f));
            if (age <= 0.30f)
                return Mathf.Lerp(0.90f, 0.74f, Mathf.SmoothStep(0f, 1f, (age - 0.08f) / 0.22f));
            if (age <= 0.72f)
                return Mathf.Lerp(0.74f, 0.48f, Mathf.SmoothStep(0f, 1f, (age - 0.30f) / 0.42f));
            return Mathf.Lerp(0.48f, 0f, Mathf.SmoothStep(0f, 1f, (age - 0.72f) / 0.28f));
        }

        private static float EvaluateTrailExpansion(float age, float growth)
        {
            const float response = 0.25f;
            const float birthScale = 0.60f;
            age = Mathf.Clamp01(age);
            growth = Mathf.Max(1f, growth);
            float denominator = 1f - Mathf.Exp(-1f / response);
            float normalized = (1f - Mathf.Exp(-age / response)) / denominator;
            return birthScale + (growth - birthScale) * normalized;
        }

        private static uint HashKey(CellKey key)
        {
            unchecked
            {
                uint value = (uint)(key.X * 73856093);
                value ^= (uint)(key.Y * 19349663);
                value ^= (uint)(key.Z * 83492791);
                value ^= value >> 16;
                value *= 0x7FEB352DU;
                value ^= value >> 15;
                value *= 0x846CA68BU;
                value ^= value >> 16;
                return value;
            }
        }

        private static float HashToUnit(uint value)
        {
            return (value & 0x00FFFFFFU) / 16777215f;
        }
    }

    internal static class SmokeLightModel
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

        public static float EvaluateSolarVisibility(
            float radialMagnitude,
            float bodyRadius,
            Vector3 up,
            Vector3 sunDirection)
        {
            if (sunDirection.sqrMagnitude < 0.001f || radialMagnitude <= 1f)
                return 0f;

            float radiusRatio = Mathf.Clamp01(bodyRadius / radialMagnitude);
            float horizonCosine = -Mathf.Sqrt(Mathf.Max(0f, 1f - radiusRatio * radiusRatio));
            float sunCosine = Vector3.Dot(up, sunDirection);
            float horizonT = Mathf.InverseLerp(horizonCosine - 0.012f, horizonCosine + 0.075f, sunCosine);
            return Mathf.SmoothStep(0f, 1f, horizonT);
        }

        public static Color EvaluateSunTint(float solarElevation, float warmth)
        {
            float daylightT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.28f, solarElevation));
            Color horizon = Color.Lerp(
                Color.white,
                new Color(1.00f, 0.53f, 0.28f, 1f),
                Mathf.Clamp01(warmth));
            return Color.Lerp(horizon, Color.white, daylightT);
        }

        public static float EvaluatePhase(float cosine)
        {
            cosine = Mathf.Clamp(cosine, -1f, 1f);
            // Two broad lobes echo the single/multiple-scattering split used by EVE, while the
            // restrained anisotropy avoids unstable highlights on particle impostors.
            float forward = HenyeyGreenstein(cosine, 0.65f);
            float backward = HenyeyGreenstein(cosine, -0.28f);
            return forward * 0.78f + backward * 0.22f;
        }

        private static float HenyeyGreenstein(float cosine, float anisotropy)
        {
            float g2 = anisotropy * anisotropy;
            float denominator = Mathf.Pow(
                Mathf.Max(0.001f, 1f + g2 - 2f * anisotropy * cosine),
                1.5f);
            // Omitting 4*pi normalizes isotropic scattering to 1, which is convenient as a colour
            // multiplier and equivalent to dividing the physical phase function by 1/(4*pi).
            return (1f - g2) / denominator;
        }
    }
}
