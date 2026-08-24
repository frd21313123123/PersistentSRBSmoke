using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal enum SmokeSegmentKind : uint
    {
        Nozzle = 0,
        Trail = 1,
        Pad = 2
    }

    /// <summary>
    /// Controller-to-simulation input. All mass is expressed as optical mass so source throttling
    /// can reduce topology without making a clustered booster visually transparent.
    /// </summary>
    internal struct SrbSmokeInjection
    {
        public CelestialBody Body;
        public int EmitterId;
        public int VesselId;
        public Vector3 PreviousWorldPosition;
        public Vector3 CurrentWorldPosition;
        public Vector3 ExhaustDirection;
        public Vector3 Up;
        public Vector3 Wind;
        public Vector3 EmitterVelocity;
        public float DeltaTime;
        public float Travel;
        public float Atmosphere;
        public float Thrust;
        public float OpticalMass;
        public float Radius;
        public float Lifetime;
        public float HeightAboveGround;
        public float StationaryBlend;
        public Color SmokeColor;
        public EngineSmokeProfile Profile;
    }

    /// <summary>
    /// Hermite centreline segment in coordinates relative to its celestial body's transform.
    /// Keeping the simulation body-relative makes Floating Origin shifts a rendering concern rather
    /// than a destructive transform of thousands of trail records.
    /// </summary>
    internal struct TrailSegment
    {
        public bool Active;
        public CelestialBody Body;
        public int EmitterId;
        public int VesselId;
        public SmokeSegmentKind Kind;
        public Vector3 StartRelative;
        public Vector3 EndRelative;
        public Vector3 StartTangent;
        public Vector3 EndTangent;
        public Vector3 Velocity;
        public float StartRadius;
        public float EndRadius;
        public float OpticalMass;
        public float Temperature;
        public float Age;
        public float Lifetime;
        public Color Color;
        public uint Seed;

        public Vector3 CenterRelative
        {
            get { return (StartRelative + EndRelative) * 0.5f; }
        }

        public float Radius
        {
            get { return Mathf.Max(StartRadius, EndRadius); }
        }

        public float NormalizedAge
        {
            get { return Lifetime <= 0.0001f ? 1f : Mathf.Clamp01(Age / Lifetime); }
        }

        public Vector3 GetWorldStart()
        {
            Vector3 origin = Body == null ? Vector3.zero : Body.transform.position;
            return new Vector3(
                VolumetricTrailRules.ToWorldCoordinate(origin.x, StartRelative.x),
                VolumetricTrailRules.ToWorldCoordinate(origin.y, StartRelative.y),
                VolumetricTrailRules.ToWorldCoordinate(origin.z, StartRelative.z));
        }

        public Vector3 GetWorldEnd()
        {
            Vector3 origin = Body == null ? Vector3.zero : Body.transform.position;
            return new Vector3(
                VolumetricTrailRules.ToWorldCoordinate(origin.x, EndRelative.x),
                VolumetricTrailRules.ToWorldCoordinate(origin.y, EndRelative.y),
                VolumetricTrailRules.ToWorldCoordinate(origin.z, EndRelative.z));
        }

        public Vector3 GetWorldCenter()
        {
            return (Body == null ? Vector3.zero : Body.transform.position) + CenterRelative;
        }
    }

    internal struct VolumeShadowSample
    {
        public CelestialBody Body;
        public Vector3 WorldCenter;
        public Vector3 Direction;
        public float Radius;
        public float Length;
        public float Opacity;
        public Color Color;
        public uint Seed;
    }

    /// <summary>
    /// Eight float4s: matching the Shader Model 5 StructuredBuffer layout exactly avoids platform
    /// specific packing ambiguity when Unity uploads the D3D11 segment buffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SegmentGpuData
    {
        public Vector4 StartRadius;
        public Vector4 EndRadius;
        public Vector4 StartTangentMass;
        public Vector4 EndTangentTemperature;
        public Vector4 VelocityAge;
        public Vector4 Color;
        public Vector4 Metadata;
        public Vector4 Bounds;
    }

    internal struct SegmentCellKey : IEquatable<SegmentCellKey>
    {
        private readonly int _bodyId;
        private readonly int _vesselId;
        private readonly int _x;
        private readonly int _y;
        private readonly int _z;

        public SegmentCellKey(CelestialBody body, int vesselId, Vector3 position, float cellSize)
        {
            _bodyId = body == null ? 0 : body.GetInstanceID();
            _vesselId = vesselId;
            float safeSize = Mathf.Max(1f, cellSize);
            _x = Mathf.FloorToInt(position.x / safeSize);
            _y = Mathf.FloorToInt(position.y / safeSize);
            _z = Mathf.FloorToInt(position.z / safeSize);
        }

        public bool Equals(SegmentCellKey other)
        {
            return _bodyId == other._bodyId && _vesselId == other._vesselId
                && _x == other._x && _y == other._y && _z == other._z;
        }

        public override bool Equals(object obj)
        {
            return obj is SegmentCellKey && Equals((SegmentCellKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _bodyId;
                hash = hash * 397 ^ _vesselId;
                hash = hash * 397 ^ _x;
                hash = hash * 397 ^ _y;
                hash = hash * 397 ^ _z;
                return hash;
            }
        }
    }

    internal static class SmokeSegmentMath
    {
        /// <summary>
        /// Preserves optical mass and linear momentum. Radius is area-weighted, which keeps the
        /// Beer-Lambert column density stable when several old records become one coarser volume.
        /// </summary>
        public static void Merge(ref TrailSegment destination, TrailSegment source)
        {
            float destinationMass = Mathf.Max(0.0001f, destination.OpticalMass);
            float sourceMass = Mathf.Max(0.0001f, source.OpticalMass);
            float totalMass = destinationMass + sourceMass;
            float destinationWeight = destinationMass / totalMass;
            float sourceWeight = sourceMass / totalMass;

            destination.StartRelative = destination.StartRelative * destinationWeight + source.StartRelative * sourceWeight;
            destination.EndRelative = destination.EndRelative * destinationWeight + source.EndRelative * sourceWeight;
            destination.StartTangent = destination.StartTangent * destinationWeight + source.StartTangent * sourceWeight;
            destination.EndTangent = destination.EndTangent * destinationWeight + source.EndTangent * sourceWeight;
            destination.Velocity = destination.Velocity * destinationWeight + source.Velocity * sourceWeight;
            destination.StartRadius = VolumetricTrailRules.AreaWeightedRadius(
                destination.StartRadius, destinationMass, source.StartRadius, sourceMass);
            destination.EndRadius = VolumetricTrailRules.AreaWeightedRadius(
                destination.EndRadius, destinationMass, source.EndRadius, sourceMass);
            destination.OpticalMass = totalMass;
            destination.Temperature = destination.Temperature * destinationWeight + source.Temperature * sourceWeight;
            destination.Age = destination.Age * destinationWeight + source.Age * sourceWeight;
            destination.Lifetime = Mathf.Max(destination.Lifetime, source.Lifetime);
            destination.Color = destination.Color * destinationWeight + source.Color * sourceWeight;
            destination.Color.a = Mathf.Clamp01(destination.Color.a);
            destination.Seed ^= source.Seed + 0x9E3779B9U + (destination.Seed << 6) + (destination.Seed >> 2);
        }
    }

    internal static class VolumetricSmokeRegistry
    {
        public static VolumetricSmokeSystem Current;
    }
}
