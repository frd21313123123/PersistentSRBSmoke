using System;

namespace PersistentSRBSmoke
{
    internal static class VolumetricSmokeAlgorithmTests
    {
        private const float Epsilon = 0.0001f;

        private static int Main()
        {
            try
            {
                ContinuousInsertionIncludesStationarySource();
                MergeRulesPreserveMassAndMomentum();
                FiveMinuteTrailUsesBoundedLodBuckets();
                DissipationIsStableAcrossSimulationSteps();
                UniversalTimeWarpAdvancesAge();
                BodyRelativeCoordinatesSurviveOriginShift();
                Console.WriteLine("Volumetric smoke algorithm tests passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void ContinuousInsertionIncludesStationarySource()
        {
            AssertEqual(1, VolumetricTrailRules.CalculateInsertionCount(0f, 5.5f, 1f, 24),
                "A held-down SRB must produce one source segment.");
            AssertEqual(0, VolumetricTrailRules.CalculateInsertionCount(0f, 5.5f, 0f, 24),
                "An idle source must not create trail topology.");
            AssertEqual(3, VolumetricTrailRules.CalculateInsertionCount(12f, 5.5f, 0f, 24),
                "Moving deposition must cover travelled distance without gaps.");
            AssertEqual(24, VolumetricTrailRules.CalculateInsertionCount(1000f, 5.5f, 0f, 24),
                "Per-injection topology must remain bounded.");
        }

        private static void MergeRulesPreserveMassAndMomentum()
        {
            const float leftMass = 3f;
            const float rightMass = 7f;
            float totalMass = leftMass + rightMass;
            AssertNear(10f, totalMass, "Merge must preserve optical mass.");
            AssertNear(9f, VolumetricTrailRules.MassWeighted(2f, leftMass, 12f, rightMass),
                "Mass-weighted velocity must preserve linear momentum.");
            float radius = VolumetricTrailRules.AreaWeightedRadius(2f, leftMass, 4f, rightMass);
            AssertNear((float)Math.Sqrt(12.4f), radius,
                "Area-weighted radius must preserve the merged column density.");
        }

        private static void FiveMinuteTrailUsesBoundedLodBuckets()
        {
            int near = 0;
            int mid = 0;
            int far = 0;
            float nearSquared = 350f * 350f;
            float midSquared = 1800f * 1800f;
            float farSquared = 9000f * 9000f;
            // 300 seconds at four dynamic evaluations per second: a five-minute historical trail.
            for (int step = 0; step < 1200; step++)
            {
                float distance = step * 8f;
                int bucket = VolumetricTrailRules.SelectLodBucket(distance * distance,
                    nearSquared, midSquared, farSquared, false);
                if (bucket == 0) near++;
                else if (bucket == 1) mid++;
                else if (bucket == 2) far++;
            }
            Assert(near > 0 && mid > 0 && far > 0, "Five-minute trail must exercise all LOD bands.");
            AssertEqual(-1, VolumetricTrailRules.SelectLodBucket(10000f * 10000f,
                nearSquared, midSquared, farSquared, false), "Out-of-range history must be culled by view LOD.");
            AssertEqual(0, VolumetricTrailRules.SelectLodBucket(10000f * 10000f,
                nearSquared, midSquared, farSquared, true), "Nozzle core is always near-priority.");
        }

        private static void DissipationIsStableAcrossSimulationSteps()
        {
            float oneStep = VolumetricTrailRules.DissipateMass(100f, 0.88f, 30f, 210f);
            float splitStep = VolumetricTrailRules.DissipateMass(
                VolumetricTrailRules.DissipateMass(100f, 0.88f, 10f, 210f), 0.88f, 20f, 210f);
            Assert(oneStep > 0f && oneStep < 100f, "Dissipation must be finite and monotonic.");
            AssertNear(oneStep, splitStep, "Dissipation must not depend on dynamic update partitioning.");
        }

        private static void UniversalTimeWarpAdvancesAge()
        {
            float age = VolumetricTrailRules.AdvanceAge(4f, 120f);
            AssertNear(124f, age, "Time-warp age must use game/Universal Time, not render delta.");
            Assert(!VolumetricTrailRules.IsExpired(age, 210f, 1f), "A valid warped segment must remain alive.");
            age = VolumetricTrailRules.AdvanceAge(age, 100f);
            Assert(VolumetricTrailRules.IsExpired(age, 210f, 1f), "Expired warped segment must be removed.");
        }

        private static void BodyRelativeCoordinatesSurviveOriginShift()
        {
            float originalBodyOrigin = 1000000f;
            float world = 1000081.25f;
            float bodyRelative = VolumetricTrailRules.ToBodyRelativeCoordinate(world, originalBodyOrigin);
            float shiftedBodyOrigin = 950000f;
            float shiftedWorld = VolumetricTrailRules.ToWorldCoordinate(shiftedBodyOrigin, bodyRelative);
            AssertNear(81.25f, bodyRelative, "Stored segment coordinate must remain body-relative.");
            AssertNear(-50000f, shiftedWorld - world, "World origin move must not mutate the stored relative segment.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new InvalidOperationException(message + " Expected " + expected + ", got " + actual + ".");
        }

        private static void AssertNear(float expected, float actual, string message)
        {
            if (Math.Abs(expected - actual) > Epsilon)
                throw new InvalidOperationException(message + " Expected " + expected + ", got " + actual + ".");
        }
    }
}
