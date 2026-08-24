using System;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Small deterministic rules shared by the Unity simulation and the platform-independent unit
    /// tests. Keeping these scalar operations outside rendering code makes their conservation and
    /// time-warp behavior testable without KSP or a graphics device.
    /// </summary>
    internal static class VolumetricTrailRules
    {
        public static int CalculateInsertionCount(
            float travel,
            float segmentLength,
            float stationaryBlend,
            int maximum)
        {
            int count = (int)Math.Ceiling(Math.Max(0f, travel) / Math.Max(0.5f, segmentLength));
            if (count <= 0 && stationaryBlend > 0.001f)
                count = 1;
            return Math.Min(Math.Max(0, maximum), count);
        }

        public static float AdvanceAge(float age, float gameDeltaTime)
        {
            return Math.Max(0f, age) + Math.Max(0f, gameDeltaTime);
        }

        public static bool IsExpired(float age, float lifetime, float opticalMass)
        {
            return age >= Math.Max(0.0001f, lifetime) || opticalMass < 0.0001f;
        }

        public static float DissipateMass(float opticalMass, float dissipationRate, float deltaTime, float lifetime)
        {
            float exponent = -Math.Max(0f, dissipationRate) * Math.Max(0f, deltaTime)
                / Math.Max(1f, lifetime);
            return Math.Max(0f, opticalMass) * (float)Math.Exp(exponent);
        }

        public static float MassWeighted(float left, float leftMass, float right, float rightMass)
        {
            float safeLeft = Math.Max(0.0001f, leftMass);
            float safeRight = Math.Max(0.0001f, rightMass);
            return (left * safeLeft + right * safeRight) / (safeLeft + safeRight);
        }

        public static float AreaWeightedRadius(float leftRadius, float leftMass, float rightRadius, float rightMass)
        {
            float squared = MassWeighted(leftRadius * leftRadius, leftMass, rightRadius * rightRadius, rightMass);
            return (float)Math.Sqrt(Math.Max(0f, squared));
        }

        /// <returns>0 near, 1 mid, 2 far, -1 culled.</returns>
        public static int SelectLodBucket(float distanceSquared, float nearSquared, float midSquared, float farSquared, bool isNozzle)
        {
            if (isNozzle || distanceSquared <= nearSquared)
                return 0;
            if (distanceSquared <= midSquared)
                return 1;
            return distanceSquared <= farSquared ? 2 : -1;
        }

        public static float ToWorldCoordinate(float bodyOrigin, float bodyRelative)
        {
            return bodyOrigin + bodyRelative;
        }

        public static float ToBodyRelativeCoordinate(float worldCoordinate, float bodyOrigin)
        {
            return worldCoordinate - bodyOrigin;
        }
    }
}
