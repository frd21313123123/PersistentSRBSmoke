using System;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class SmokeSettings
    {
        public bool Enabled = true;

        // Performance / lifetime
        public int MaxParticles = 36000;
        public float Lifetime = 210f;
        public int MaxEmitPerFrame = 160;
        public float DynamicMotionHz = 6f;
        public float TeleportDistance = 750f;
        public float EngineScanInterval = 2f;

        // Render cost controls. Three crossed quads keep a cloudlet volumetric-looking while
        // cutting transparent overdraw roughly in half compared with the old six-plane mesh.
        public int CloudletPlanes = 3;
        public bool SortParticles = false;

        // Dynamic-motion LOD. Old/far particles keep their current velocity between updates and
        // are refreshed less often; Unity still integrates their motion every frame.
        public float DynamicMidAge = 0.20f;
        public float DynamicOldAge = 0.55f;
        public int DynamicMidStride = 2;
        public int DynamicOldStride = 4;
        public float DynamicFarDistance = 5000f;
        public int DynamicFarStrideMultiplier = 2;

        // Number of altitude samples used by WindModel. Perlin noise is evaluated once per layer
        // per dynamic tick instead of several times for every smoke particle.
        public int WindCacheLayers = 96;

        // KSP time warp / universal-time synchronization
        public bool FollowUniversalTime = true;
        public float MaxWarpSimulationStep = 5f;

        // Suppress stock / legacy smoke so only this mod owns the persistent SRB trail.
        public bool SuppressStockSmoke = true;
        public float StockSmokeRefreshInterval = 0.75f;

        // Emission / continuity
        public float BaseEmissionRate = 32f;
        public float ParticlesPerMeter = 0.30f;
        public float MaxParticleSpacing = 1.75f;
        public float HighAltitudeSpacingMultiplier = 1.25f;
        public float ThinAtmosphereDensityFloor = 0.42f;

        // Visual plume size
        public float StartSize = 8.0f;
        public float SizeGrowth = 18.0f;
        public float HighAltitudeSizeMultiplier = 1.85f;
        public float Opacity = 0.80f;
        public float SmokeBrightness = 0.96f;
        public float EngineColorVariation = 0.05f;

        // Engine-dependent smoke scaling. KSP engine thrust is measured in kN.
        public bool EngineScalingEnabled = true;
        public float EngineMinThrust = 8f;
        public float EngineMaxThrust = 800f;
        public float SmallEngineEmissionMultiplier = 0.18f;
        public float LargeEngineEmissionMultiplier = 1.10f;
        public float SmallEngineSizeMultiplier = 0.38f;
        public float LargeEngineSizeMultiplier = 1.10f;
        public float SmallEngineLifetimeMultiplier = 0.45f;
        public float LargeEngineLifetimeMultiplier = 1.00f;
        public float SmallEngineOpacityMultiplier = 0.72f;
        public float LargeEngineOpacityMultiplier = 1.00f;
        public float SmallEngineSpacingMultiplier = 2.40f;
        public float LargeEngineSpacingMultiplier = 0.95f;

        // Local cloud motion / diffusion
        public float DriftSpeed = 1.8f;
        public float DiffusionSpeed = 3.2f;
        public float DiffusionGrowth = 1.55f;
        public float Buoyancy = 0.24f;
        public float DynamicWindResponse = 2.4f;
        public float TurbulenceStrength = 1.1f;
        public float TurbulenceFrequency = 0.055f;

        // Near-pad hold. Wind stays weak here so the whole cloud is not translated off the pad.
        public float NearGroundHoldHeight = 60f;
        public float NearGroundWindMultiplier = 0.12f;
        public float NearGroundDiffusionMultiplier = 0.25f;
        public float NearGroundBuoyancyMultiplier = 0.40f;

        // Density-driven launch-pad cloud. Dense exhaust is pushed sideways across the ground while
        // the thinning outer lobes curl upward, approximating the large Shuttle-style pad billow.
        public bool PadCloudEnabled = true;
        public float PadCloudHeight = 120f;
        public float PadCloudCellSize = 18f;
        public float PadCloudDensityThreshold = 5f;
        public float PadCloudDensitySaturation = 24f;
        public float PadCloudOutflowSpeed = 18f;
        public float PadCloudUpdraftSpeed = 5.5f;
        public float PadCloudGlobalBias = 0.72f;

        // Altitude-dependent wind shear
        public bool WindEnabled = true;
        public float WindSpeed = 7.0f;
        public float WindLayerHeight = 1800f;
        public float WindTopAltitude = 32000f;
        public float WindDirectionChangeRadians = 1.15f;
        public float WindTimeScale = 0.0006f;

        public bool DebugLogging = false;

        public static SmokeSettings Load()
        {
            var settings = new SmokeSettings();
            try
            {
                string path = KSPUtil.ApplicationRootPath + "GameData/PersistentSRBSmoke/PluginData/Settings.cfg";
                ConfigNode file = ConfigNode.Load(path);
                ConfigNode node = file == null ? null : file.GetNode("PERSISTENT_SRB_SMOKE");
                if (node == null)
                {
                    Debug.Log("[PersistentSRBSmoke] Settings not found, using defaults.");
                    return settings;
                }

                settings.Enabled = ReadBool(node, "enabled", settings.Enabled);

                settings.MaxParticles = ReadInt(node, "maxParticles", settings.MaxParticles, 1000, 150000);
                settings.Lifetime = ReadFloat(node, "lifetime", settings.Lifetime, 5f, 600f);
                settings.MaxEmitPerFrame = ReadInt(node, "maxEmitPerFrame", settings.MaxEmitPerFrame, 1, 2000);
                settings.DynamicMotionHz = ReadFloat(node, "dynamicMotionHz", settings.DynamicMotionHz, 1f, 30f);
                settings.TeleportDistance = ReadFloat(node, "teleportDistance", settings.TeleportDistance, 10f, 10000f);
                settings.EngineScanInterval = ReadFloat(node, "engineScanInterval", settings.EngineScanInterval, 0.25f, 30f);
                settings.CloudletPlanes = ReadInt(node, "cloudletPlanes", settings.CloudletPlanes, 2, 6);
                settings.SortParticles = ReadBool(node, "sortParticles", settings.SortParticles);
                settings.DynamicMidAge = ReadFloat(node, "dynamicMidAge", settings.DynamicMidAge, 0f, 0.95f);
                settings.DynamicOldAge = ReadFloat(node, "dynamicOldAge", settings.DynamicOldAge, 0.01f, 1f);
                settings.DynamicMidStride = ReadInt(node, "dynamicMidStride", settings.DynamicMidStride, 1, 16);
                settings.DynamicOldStride = ReadInt(node, "dynamicOldStride", settings.DynamicOldStride, 1, 32);
                settings.DynamicFarDistance = ReadFloat(node, "dynamicFarDistance", settings.DynamicFarDistance, 100f, 100000f);
                settings.DynamicFarStrideMultiplier = ReadInt(node, "dynamicFarStrideMultiplier", settings.DynamicFarStrideMultiplier, 1, 16);
                settings.WindCacheLayers = ReadInt(node, "windCacheLayers", settings.WindCacheLayers, 8, 512);

                settings.FollowUniversalTime = ReadBool(node, "followUniversalTime", settings.FollowUniversalTime);
                settings.MaxWarpSimulationStep = ReadFloat(node, "maxWarpSimulationStep", settings.MaxWarpSimulationStep, 0.25f, 30f);
                settings.SuppressStockSmoke = ReadBool(node, "suppressStockSmoke", settings.SuppressStockSmoke);
                settings.StockSmokeRefreshInterval = ReadFloat(node, "stockSmokeRefreshInterval", settings.StockSmokeRefreshInterval, 0.1f, 10f);

                settings.BaseEmissionRate = ReadFloat(node, "baseEmissionRate", settings.BaseEmissionRate, 0f, 500f);
                settings.ParticlesPerMeter = ReadFloat(node, "particlesPerMeter", settings.ParticlesPerMeter, 0f, 10f);
                settings.MaxParticleSpacing = ReadFloat(node, "maxParticleSpacing", settings.MaxParticleSpacing, 0.25f, 25f);
                settings.HighAltitudeSpacingMultiplier = ReadFloat(node, "highAltitudeSpacingMultiplier", settings.HighAltitudeSpacingMultiplier, 1f, 5f);
                settings.ThinAtmosphereDensityFloor = ReadFloat(node, "thinAtmosphereDensityFloor", settings.ThinAtmosphereDensityFloor, 0f, 1f);

                settings.StartSize = ReadFloat(node, "startSize", settings.StartSize, 0.1f, 100f);
                settings.SizeGrowth = ReadFloat(node, "sizeGrowth", settings.SizeGrowth, 1f, 40f);
                settings.HighAltitudeSizeMultiplier = ReadFloat(node, "highAltitudeSizeMultiplier", settings.HighAltitudeSizeMultiplier, 1f, 5f);
                settings.Opacity = ReadFloat(node, "opacity", settings.Opacity, 0.01f, 1f);
                settings.SmokeBrightness = ReadFloat(node, "smokeBrightness", settings.SmokeBrightness, 0.2f, 1.4f);
                settings.EngineColorVariation = ReadFloat(node, "engineColorVariation", settings.EngineColorVariation, 0f, 0.3f);

                settings.EngineScalingEnabled = ReadBool(node, "engineScalingEnabled", settings.EngineScalingEnabled);
                settings.EngineMinThrust = ReadFloat(node, "engineMinThrust", settings.EngineMinThrust, 0.1f, 5000f);
                settings.EngineMaxThrust = ReadFloat(node, "engineMaxThrust", settings.EngineMaxThrust, 0.2f, 20000f);
                settings.SmallEngineEmissionMultiplier = ReadFloat(node, "smallEngineEmissionMultiplier", settings.SmallEngineEmissionMultiplier, 0.01f, 3f);
                settings.LargeEngineEmissionMultiplier = ReadFloat(node, "largeEngineEmissionMultiplier", settings.LargeEngineEmissionMultiplier, 0.01f, 3f);
                settings.SmallEngineSizeMultiplier = ReadFloat(node, "smallEngineSizeMultiplier", settings.SmallEngineSizeMultiplier, 0.05f, 3f);
                settings.LargeEngineSizeMultiplier = ReadFloat(node, "largeEngineSizeMultiplier", settings.LargeEngineSizeMultiplier, 0.05f, 3f);
                settings.SmallEngineLifetimeMultiplier = ReadFloat(node, "smallEngineLifetimeMultiplier", settings.SmallEngineLifetimeMultiplier, 0.05f, 2f);
                settings.LargeEngineLifetimeMultiplier = ReadFloat(node, "largeEngineLifetimeMultiplier", settings.LargeEngineLifetimeMultiplier, 0.05f, 2f);
                settings.SmallEngineOpacityMultiplier = ReadFloat(node, "smallEngineOpacityMultiplier", settings.SmallEngineOpacityMultiplier, 0.05f, 2f);
                settings.LargeEngineOpacityMultiplier = ReadFloat(node, "largeEngineOpacityMultiplier", settings.LargeEngineOpacityMultiplier, 0.05f, 2f);
                settings.SmallEngineSpacingMultiplier = ReadFloat(node, "smallEngineSpacingMultiplier", settings.SmallEngineSpacingMultiplier, 0.2f, 10f);
                settings.LargeEngineSpacingMultiplier = ReadFloat(node, "largeEngineSpacingMultiplier", settings.LargeEngineSpacingMultiplier, 0.2f, 10f);

                settings.DriftSpeed = ReadFloat(node, "driftSpeed", settings.DriftSpeed, 0f, 20f);
                settings.DiffusionSpeed = ReadFloat(node, "diffusionSpeed", settings.DiffusionSpeed, 0f, 30f);
                settings.DiffusionGrowth = ReadFloat(node, "diffusionGrowth", settings.DiffusionGrowth, 0f, 5f);
                settings.Buoyancy = ReadFloat(node, "buoyancy", settings.Buoyancy, -10f, 10f);
                settings.DynamicWindResponse = ReadFloat(node, "dynamicWindResponse", settings.DynamicWindResponse, 0f, 20f);
                settings.TurbulenceStrength = ReadFloat(node, "turbulenceStrength", settings.TurbulenceStrength, 0f, 20f);
                settings.TurbulenceFrequency = ReadFloat(node, "turbulenceFrequency", settings.TurbulenceFrequency, 0.001f, 2f);

                settings.NearGroundHoldHeight = ReadFloat(node, "nearGroundHoldHeight", settings.NearGroundHoldHeight, 0f, 500f);
                settings.NearGroundWindMultiplier = ReadFloat(node, "nearGroundWindMultiplier", settings.NearGroundWindMultiplier, 0f, 1f);
                settings.NearGroundDiffusionMultiplier = ReadFloat(node, "nearGroundDiffusionMultiplier", settings.NearGroundDiffusionMultiplier, 0f, 1f);
                settings.NearGroundBuoyancyMultiplier = ReadFloat(node, "nearGroundBuoyancyMultiplier", settings.NearGroundBuoyancyMultiplier, 0f, 1f);

                settings.PadCloudEnabled = ReadBool(node, "padCloudEnabled", settings.PadCloudEnabled);
                settings.PadCloudHeight = ReadFloat(node, "padCloudHeight", settings.PadCloudHeight, 10f, 1000f);
                settings.PadCloudCellSize = ReadFloat(node, "padCloudCellSize", settings.PadCloudCellSize, 2f, 100f);
                settings.PadCloudDensityThreshold = ReadFloat(node, "padCloudDensityThreshold", settings.PadCloudDensityThreshold, 1f, 100f);
                settings.PadCloudDensitySaturation = ReadFloat(node, "padCloudDensitySaturation", settings.PadCloudDensitySaturation, 2f, 300f);
                settings.PadCloudOutflowSpeed = ReadFloat(node, "padCloudOutflowSpeed", settings.PadCloudOutflowSpeed, 0f, 80f);
                settings.PadCloudUpdraftSpeed = ReadFloat(node, "padCloudUpdraftSpeed", settings.PadCloudUpdraftSpeed, 0f, 40f);
                settings.PadCloudGlobalBias = ReadFloat(node, "padCloudGlobalBias", settings.PadCloudGlobalBias, 0f, 1f);

                settings.WindEnabled = ReadBool(node, "windEnabled", settings.WindEnabled);
                settings.WindSpeed = ReadFloat(node, "windSpeed", settings.WindSpeed, 0f, 80f);
                settings.WindLayerHeight = ReadFloat(node, "windLayerHeight", settings.WindLayerHeight, 100f, 20000f);
                settings.WindTopAltitude = ReadFloat(node, "windTopAltitude", settings.WindTopAltitude, 1000f, 100000f);
                settings.WindDirectionChangeRadians = ReadFloat(node, "windDirectionChangeRadians", settings.WindDirectionChangeRadians, 0f, 6.283185f);
                settings.WindTimeScale = ReadFloat(node, "windTimeScale", settings.WindTimeScale, 0f, 0.05f);

                settings.DebugLogging = ReadBool(node, "debugLogging", settings.DebugLogging);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PersistentSRBSmoke] Failed to load settings: " + ex);
            }

            if (settings.EngineMaxThrust <= settings.EngineMinThrust)
                settings.EngineMaxThrust = settings.EngineMinThrust + 1f;
            if (settings.PadCloudDensitySaturation <= settings.PadCloudDensityThreshold)
                settings.PadCloudDensitySaturation = settings.PadCloudDensityThreshold + 1f;
            if (settings.DynamicOldAge < settings.DynamicMidAge)
                settings.DynamicOldAge = settings.DynamicMidAge;
            if (settings.DynamicOldStride < settings.DynamicMidStride)
                settings.DynamicOldStride = settings.DynamicMidStride;

            return settings;
        }

        private static float ReadFloat(ConfigNode node, string key, float fallback, float min, float max)
        {
            string raw = node.GetValue(key);
            float value;
            if (raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return Mathf.Clamp(value, min, max);
            return fallback;
        }

        private static int ReadInt(ConfigNode node, string key, int fallback, int min, int max)
        {
            string raw = node.GetValue(key);
            int value;
            if (raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return Math.Max(min, Math.Min(max, value));
            return fallback;
        }

        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            string raw = node.GetValue(key);
            bool value;
            if (raw != null && bool.TryParse(raw, out value))
                return value;
            return fallback;
        }
    }
}
