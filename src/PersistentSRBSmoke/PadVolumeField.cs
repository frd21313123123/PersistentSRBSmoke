using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Bounded launch-pad pressure field. A tile represents a logical 32^3 density texture: its
    /// high-frequency shape is sampled in the raymarch shader, while this class owns conserved
    /// mass, wind, pressure outflow and the body-relative position of no more than eight tiles.
    /// </summary>
    internal sealed class PadVolumeField
    {
        private struct PadTile
        {
            public bool Active;
            public CelestialBody Body;
            public int VesselId;
            public Vector3 CenterRelative;
            public Vector3 Velocity;
            public Vector3 OutflowDirection;
            public float Radius;
            public float Height;
            public float OpticalMass;
            public float Age;
            public float Lifetime;
            public Color Color;
            public uint Seed;
        }

        private readonly SmokeSettings _settings;
        private readonly PadTile[] _tiles;
        private uint _seedCounter = 1U;

        public PadVolumeField(SmokeSettings settings)
        {
            _settings = settings;
            _tiles = new PadTile[Mathf.Clamp(settings.PadTileCount, 1, 8)];
        }

        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _tiles.Length; i++)
                    if (_tiles[i].Active)
                        count++;
                return count;
            }
        }

        public void Inject(SrbSmokeInjection injection)
        {
            if (!_settings.PadFieldEnabled || injection.Body == null
                || injection.HeightAboveGround > _settings.PadFieldHeight)
                return;

            float groundBlend = 1f - Mathf.Clamp01(injection.HeightAboveGround / Mathf.Max(1f, _settings.PadFieldHeight));
            float mass = injection.OpticalMass * groundBlend * _settings.PadMassBias;
            if (mass <= 0.001f)
                return;

            Vector3 center = injection.CurrentWorldPosition + injection.ExhaustDirection * _settings.NozzleOffset;
            Vector3 relativeCenter = center - injection.Body.transform.position;
            int index = FindTile(injection.Body, injection.VesselId, relativeCenter);
            PadTile tile = _tiles[index];

            if (!tile.Active)
            {
                Vector3 lateral = Vector3.ProjectOnPlane(injection.ExhaustDirection, injection.Up);
                if (lateral.sqrMagnitude < 0.001f)
                    lateral = Vector3.Cross(injection.Up, Vector3.right);
                if (lateral.sqrMagnitude < 0.001f)
                    lateral = Vector3.Cross(injection.Up, Vector3.forward);
                lateral.Normalize();

                tile.Active = true;
                tile.Body = injection.Body;
                tile.VesselId = injection.VesselId;
                tile.CenterRelative = relativeCenter;
                tile.Velocity = injection.Wind * _settings.NearGroundWindMultiplier;
                tile.OutflowDirection = lateral;
                tile.Radius = Mathf.Max(_settings.PadTileSize * 0.25f, injection.Radius * 2f);
                tile.Height = Mathf.Max(3f, _settings.PadFieldHeight * 0.18f);
                tile.OpticalMass = 0f;
                tile.Age = 0f;
                tile.Lifetime = Mathf.Max(1f, injection.Lifetime * 0.55f);
                tile.Color = injection.SmokeColor;
                tile.Seed = SrbSmokeMath.Hash(_seedCounter++);
            }

            float previousMass = Mathf.Max(0.0001f, tile.OpticalMass);
            float totalMass = previousMass + mass;
            tile.CenterRelative = (tile.CenterRelative * previousMass + relativeCenter * mass) / totalMass;
            tile.Velocity = (tile.Velocity * previousMass + injection.Wind * mass) / totalMass;
            tile.Color = (tile.Color * previousMass + injection.SmokeColor * mass) / totalMass;
            tile.Color.a = 1f;
            tile.OpticalMass = Mathf.Min(_settings.PadMassSaturation, totalMass);
            tile.Radius = Mathf.Min(_settings.PadTileSize, tile.Radius + mass * 0.05f);
            tile.Height = Mathf.Min(_settings.PadFieldHeight, tile.Height + mass * 0.025f);
            tile.Age = 0f;
            _tiles[index] = tile;
        }

        public void Advance(CelestialBody activeBody, WindModel wind, double universalTime, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            for (int i = 0; i < _tiles.Length; i++)
            {
                PadTile tile = _tiles[i];
                if (!tile.Active)
                    continue;

                if (tile.Body == null || tile.OpticalMass < 0.001f)
                {
                    tile.Active = false;
                    _tiles[i] = tile;
                    continue;
                }

                Vector3 worldCenter = tile.Body.transform.position + tile.CenterRelative;
                Vector3 up = (worldCenter - tile.Body.transform.position).normalized;
                if (up.sqrMagnitude < 0.001f)
                    up = Vector3.up;
                float altitude = Mathf.Max(0f, (worldCenter - tile.Body.transform.position).magnitude - (float)tile.Body.Radius);
                Vector3 air = wind == null ? Vector3.zero : wind.GetWind(tile.Body, up, altitude, universalTime);
                float pressure = Mathf.Clamp01(tile.OpticalMass / Mathf.Max(0.001f, _settings.PadMassSaturation));
                Vector3 desired = air * _settings.NearGroundWindMultiplier
                    + tile.OutflowDirection * (_settings.PadOutflowSpeed * pressure)
                    + up * (_settings.PadUpdraftSpeed * pressure);
                tile.Velocity = Vector3.Lerp(tile.Velocity, desired,
                    1f - Mathf.Exp(-deltaTime * Mathf.Max(0.01f, _settings.DynamicWindResponse)));
                tile.CenterRelative += tile.Velocity * deltaTime;
                tile.Radius += (_settings.PadOutflowSpeed * 0.16f + tile.Radius * 0.04f) * deltaTime;
                tile.Height += _settings.PadUpdraftSpeed * 0.10f * deltaTime;
                tile.OpticalMass *= Mathf.Exp(-_settings.DissipationRate * deltaTime * 0.65f);
                tile.Age += deltaTime;

                if (tile.Age >= tile.Lifetime || tile.OpticalMass < _settings.PadMassThreshold * 0.025f)
                    tile.Active = false;
                _tiles[i] = tile;
            }
        }

        public void AppendSegments(List<TrailSegment> destination)
        {
            if (!_settings.PadFieldEnabled)
                return;

            for (int i = 0; i < _tiles.Length; i++)
            {
                PadTile tile = _tiles[i];
                if (!tile.Active)
                    continue;

                Vector3 up = tile.CenterRelative.normalized;
                if (up.sqrMagnitude < 0.001f)
                    up = Vector3.up;
                destination.Add(new TrailSegment
                {
                    Active = true,
                    Body = tile.Body,
                    VesselId = tile.VesselId,
                    EmitterId = -1,
                    Kind = SmokeSegmentKind.Pad,
                    StartRelative = tile.CenterRelative - up * tile.Height * 0.5f,
                    EndRelative = tile.CenterRelative + up * tile.Height * 0.5f,
                    StartTangent = up * tile.Height,
                    EndTangent = up * tile.Height,
                    Velocity = tile.Velocity,
                    StartRadius = tile.Radius,
                    EndRadius = tile.Radius * 1.12f,
                    OpticalMass = tile.OpticalMass,
                    Temperature = 0.08f,
                    Age = tile.Age,
                    Lifetime = tile.Lifetime,
                    Color = tile.Color,
                    Seed = tile.Seed
                });
            }
        }

        public void AppendShadowSamples(List<VolumeShadowSample> destination)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                PadTile tile = _tiles[i];
                if (!tile.Active || tile.Body == null)
                    continue;
                destination.Add(new VolumeShadowSample
                {
                    Body = tile.Body,
                    WorldCenter = tile.Body.transform.position + tile.CenterRelative,
                    Direction = tile.OutflowDirection,
                    Radius = tile.Radius,
                    Length = Mathf.Max(tile.Radius, tile.Height),
                    Opacity = Mathf.Clamp01(tile.OpticalMass / Mathf.Max(0.01f, _settings.PadMassSaturation)),
                    Color = tile.Color,
                    Seed = tile.Seed
                });
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i] = new PadTile();
        }

        private int FindTile(CelestialBody body, int vesselId, Vector3 center)
        {
            int free = -1;
            int oldest = 0;
            float oldestAge = float.MinValue;
            float bestDistance = float.MaxValue;
            int best = -1;

            for (int i = 0; i < _tiles.Length; i++)
            {
                PadTile tile = _tiles[i];
                if (!tile.Active)
                {
                    free = i;
                    break;
                }
                if (tile.Age > oldestAge)
                {
                    oldestAge = tile.Age;
                    oldest = i;
                }
                if (tile.Body != body || tile.VesselId != vesselId)
                    continue;
                float distance = (tile.CenterRelative - center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            if (best >= 0 && bestDistance <= _settings.PadTileSize * _settings.PadTileSize)
                return best;
            return free >= 0 ? free : oldest;
        }
    }
}
