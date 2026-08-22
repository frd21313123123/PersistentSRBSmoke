using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Coarse near-ground density field used to turn a packed SRB exhaust cloud into a spreading
    /// launch-pad billow. This is deliberately not pairwise particle physics: a small spatial hash
    /// estimates local crowding, then supplies a pressure-like horizontal outflow plus edge lift.
    /// </summary>
    internal sealed class PadCloudDensityField
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

        private struct CellData
        {
            public int Count;
            public Vector3 PositionSum;
        }

        private readonly SmokeSettings _settings;
        private readonly Dictionary<CellKey, CellData> _cells = new Dictionary<CellKey, CellData>(512);

        private bool _active;
        private float _cellSize;
        private Vector3 _cloudCenter;
        private int _cloudCount;

        public PadCloudDensityField(SmokeSettings settings)
        {
            _settings = settings;
        }

        public void Rebuild(
            ParticleSystem.Particle[] particles,
            int count,
            Vector3 bodyCenter,
            float bodyRadius,
            bool hasSurfaceReference,
            float surfaceReferenceAltitude)
        {
            _cells.Clear();
            _active = _settings.PadCloudEnabled && hasSurfaceReference && count > 0;
            _cloudCenter = Vector3.zero;
            _cloudCount = 0;

            if (!_active)
                return;

            _cellSize = Mathf.Max(2f, _settings.PadCloudCellSize);
            float maxHeight = Mathf.Max(1f, _settings.PadCloudHeight);
            float minimumRadius = Mathf.Max(1f, bodyRadius + surfaceReferenceAltitude - 8f);
            float maximumRadius = Mathf.Max(minimumRadius, bodyRadius + surfaceReferenceAltitude + maxHeight);
            float minimumRadiusSqr = minimumRadius * minimumRadius;
            float maximumRadiusSqr = maximumRadius * maximumRadius;

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = particles[i];
                Vector3 radial = particle.position - bodyCenter;
                float radiusSqr = radial.sqrMagnitude;
                if (radiusSqr < minimumRadiusSqr || radiusSqr > maximumRadiusSqr)
                    continue;

                CellKey key = GetKey(particle.position);
                CellData data;
                if (_cells.TryGetValue(key, out data))
                {
                    data.Count++;
                    data.PositionSum += particle.position;
                    _cells[key] = data;
                }
                else
                {
                    _cells.Add(key, new CellData
                    {
                        Count = 1,
                        PositionSum = particle.position
                    });
                }

                _cloudCenter += particle.position;
                _cloudCount++;
            }

            if (_cloudCount > 0)
                _cloudCenter /= _cloudCount;
            else
                _active = false;
        }

        public Vector3 GetFlow(
            ParticleSystem.Particle particle,
            Vector3 up,
            float heightAboveSurface,
            float sourceScale,
            float normalizedAge)
        {
            if (!_active || heightAboveSurface < -8f || heightAboveSurface > _settings.PadCloudHeight)
                return Vector3.zero;

            CellData cell;
            if (!_cells.TryGetValue(GetKey(particle.position), out cell) || cell.Count <= 0)
                return Vector3.zero;

            float threshold = Mathf.Max(1f, _settings.PadCloudDensityThreshold);
            float saturation = Mathf.Max(threshold + 1f, _settings.PadCloudDensitySaturation);
            float densityT = Mathf.Clamp01((cell.Count - threshold) / (saturation - threshold));
            densityT = Mathf.SmoothStep(0f, 1f, densityT);

            // Edge lift begins slightly before full horizontal pressure. This allows the large outer
            // lobes to keep climbing after they have expanded enough for their local cell count to drop.
            float liftThreshold = Mathf.Max(1f, threshold * 0.45f);
            float liftT = Mathf.Clamp01((cell.Count - liftThreshold) / Mathf.Max(1f, saturation - liftThreshold));
            liftT = Mathf.SmoothStep(0f, 1f, liftT);

            if (densityT <= 0.0001f && liftT <= 0.0001f)
                return Vector3.zero;

            Vector3 localCenter = cell.PositionSum / cell.Count;
            Vector3 localOut = ProjectOnSurface(particle.position - localCenter, up);
            Vector3 globalOut = ProjectOnSurface(particle.position - _cloudCenter, up);

            float seed = HashToUnit(particle.randomSeed);
            Vector3 fallback = StableTangent(up, seed);
            if (localOut.sqrMagnitude < 0.04f)
                localOut = fallback;
            if (globalOut.sqrMagnitude < 0.04f)
                globalOut = fallback;

            localOut.Normalize();
            globalOut.Normalize();

            float globalBias = Mathf.Clamp01(_settings.PadCloudGlobalBias);
            Vector3 outward = Vector3.Lerp(localOut, globalOut, globalBias);
            if (outward.sqrMagnitude < 0.001f)
                outward = fallback;
            outward.Normalize();

            float heightT = Mathf.Clamp01(Mathf.Max(0f, heightAboveSurface) / Mathf.Max(1f, _settings.PadCloudHeight));
            float groundWeight = 1f - Mathf.SmoothStep(0f, 1f, heightT);
            float source = Mathf.Clamp(sourceScale, 0.30f, 1.15f);

            // Fresh dense exhaust produces the strongest horizontal ground cloud. As a cloudlet ages,
            // the pressure component eases but never instantly vanishes, preserving the wide billow.
            float freshPressure = Mathf.Lerp(1f, 0.58f, Mathf.SmoothStep(0f, 1f, normalizedAge));
            float outflowSpeed = _settings.PadCloudOutflowSpeed
                * densityT
                * groundWeight
                * source
                * freshPressure;

            // Sparse outer lobes receive relatively more vertical motion, producing the characteristic
            // rising cauliflower towers seen beside Shuttle-class launch pads instead of a flat ring.
            float edgeLift = Mathf.Lerp(0.40f, 1.0f, 1f - densityT);
            float ageLift = Mathf.Lerp(0.45f, 1.0f, Mathf.SmoothStep(0f, 1f, normalizedAge * 2.2f));
            float updraftSpeed = _settings.PadCloudUpdraftSpeed
                * liftT
                * groundWeight
                * source
                * edgeLift
                * ageLift;

            return outward * outflowSpeed + up * updraftSpeed;
        }

        private CellKey GetKey(Vector3 position)
        {
            return new CellKey(
                Mathf.FloorToInt(position.x / _cellSize),
                Mathf.FloorToInt(position.y / _cellSize),
                Mathf.FloorToInt(position.z / _cellSize));
        }

        private static Vector3 ProjectOnSurface(Vector3 vector, Vector3 up)
        {
            return vector - up * Vector3.Dot(vector, up);
        }

        private static Vector3 StableTangent(Vector3 up, float seed)
        {
            Vector3 tangentA = Vector3.Cross(up, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float angle = seed * Mathf.PI * 2f;
            return (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)).normalized;
        }

        private static float HashToUnit(uint value)
        {
            value ^= value >> 17;
            value *= 0xed5ad4bbU;
            value ^= value >> 11;
            value *= 0xac4c1b51U;
            value ^= value >> 15;
            return (value & 0x00FFFFFFU) / 16777215f;
        }
    }
}
