using System;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class SmokeSettings
    {
        public bool Enabled = true;

        // Performance / lifetime
        public int MaxParticles = 48000;
        public float Lifetime = 210f;
        public int MaxEmitPerFrame = 192;
        public float DynamicMotionHz = 10f;
        public float TeleportDistance = 750f;

        // KSP time warp / universal-time synchronization
        public bool FollowUniversalTime = true;
        public float MaxWarpSimulationStep = 5f;

        // Suppress stock / legacy smoke
        public bool SuppressStockSmoke = true;
        public float StockSmokeRefreshInterval = 0.75f;

        // Emission / continuity
        public float BaseEmissionRate = 32f;
        public float ParticlesPerMeter = 0.30f;
        public float MaxParticleSpacing = 1.75f;
        public float HighAltitudeSpacingMultiplier = 1.25f;
        public float ThinAtmosphereDensityFloor = 0.42f;

        // Visual plume size / base albedo
        public float StartSize = 8.0f;
        public float SizeGrowth = 18.0f;
        public float HighAltitudeSizeMultiplier = 1.85f;
        public float Opacity = 0.80f;
        public float SmokeBrightness = 0.78f;
        public float EngineColorVariation = 0.08f;

        // Optical model shared by raymarch and fallback paths
        public bool VolumetricLightingEnabled = true;
        public float VolumetricScatteringForward = 0.85f;
        public float VolumetricScatteringBackward = -0.35f;
        public float VolumetricMultipleScattering = 0.55f;
        public float VolumetricSoftDepthFactor = 1.65f;
        public float VolumetricSunIntensity = 1.10f;
        public float VolumetricAmbientIntensity = 0.46f;
        public float VolumetricBeerPowderFactor = 0.72f;

        // True raymarched density volume. This path activates automatically when the custom shader
        // is available through a .shab asset bundle (normally loaded by Shabby).
        public bool RaymarchedVolumetricEnabled = true;
        public int RaymarchMaxCloudlets = 7000;
        public int RaymarchSteps = 24;
        public int RaymarchShadowSteps = 4;
        public float RaymarchDensityMultiplier = 1.15f;
        public float RaymarchExtinction = 2.10f;

        // Dependency-free 3D slice-volume fallback. Cards are distributed through the cloudlet
        // volume instead of all crossing at its centre.
        public int NativeVolumeSlicesPerAxis = 5;
        public float NativeVolumeSliceOpacity = 0.20f;
        public float FallbackMinimumLight = 0.72f;
        public float FallbackCoreShadow = 0.16f;

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

        // Near-pad hold
        public float NearGroundHoldHeight = 60f;
        public float NearGroundWindMultiplier = 0.12f;
        public float NearGroundDiffusionMultiplier = 0.25f;
        public float NearGroundBuoyancyMultiplier = 0.40f;

        // Density-driven launch-pad cloud
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

                settings.VolumetricLightingEnabled = ReadBool(node, "volumetricLightingEnabled", settings.VolumetricLightingEnabled);
                settings.VolumetricScatteringForward = ReadFloat(node, "volumetricScatteringForward", settings.VolumetricScatteringForward, -0.95f, 0.95f);
                settings.VolumetricScatteringBackward = ReadFloat(node, "volumetricScatteringBackward", settings.VolumetricScatteringBackward, -0.95f, 0.95f);
                settings.VolumetricMultipleScattering = ReadFloat(node, "volumetricMultipleScattering", settings.VolumetricMultipleScattering, 0f, 2f);
                settings.VolumetricSoftDepthFactor = ReadFloat(node, "volumetricSoftDepthFactor", settings.VolumetricSoftDepthFactor, 0.05f, 30f);
                settings.VolumetricSunIntensity = ReadFloat(node, "volumetricSunIntensity", settings.VolumetricSunIntensity, 0f, 5f);
                settings.VolumetricAmbientIntensity = ReadFloat(node, "volumetricAmbientIntensity", settings.VolumetricAmbientIntensity, 0f, 3f);
                settings.VolumetricBeerPowderFactor = ReadFloat(node, "volumetricBeerPowderFactor", settings.VolumetricBeerPowderFactor, 0.01f, 3f);

                settings.RaymarchedVolumetricEnabled = ReadBool(node, "raymarchedVolumetricEnabled", settings.RaymarchedVolumetricEnabled);
                settings.RaymarchMaxCloudlets = ReadInt(node, "raymarchMaxCloudlets", settings.RaymarchMaxCloudlets, 128, 50000);
                settings.RaymarchSteps = ReadInt(node, "raymarchSteps", settings.RaymarchSteps, 8, 32);
                settings.RaymarchShadowSteps = ReadInt(node, "raymarchShadowSteps", settings.RaymarchShadowSteps, 1, 6);
                settings.RaymarchDensityMultiplier = ReadFloat(node, "raymarchDensityMultiplier", settings.RaymarchDensityMultiplier, 0.1f, 4f);
                settings.RaymarchExtinction = ReadFloat(node, "raymarchExtinction", settings.RaymarchExtinction, 0.1f, 8f);

                settings.NativeVolumeSlicesPerAxis = ReadInt(node, "nativeVolumeSlicesPerAxis", settings.NativeVolumeSlicesPerAxis, 3, 9);
                settings.NativeVolumeSliceOpacity = ReadFloat(node, "nativeVolumeSliceOpacity", settings.NativeVolumeSliceOpacity, 0.03f, 0.8f);
                settings.FallbackMinimumLight = ReadFloat(node, "fallbackMinimumLight", settings.FallbackMinimumLight, 0.35f, 1.1f);
                settings.FallbackCoreShadow = ReadFloat(node, "fallbackCoreShadow", settings.FallbackCoreShadow, 0f, 0.5f);

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
            if ((settings.NativeVolumeSlicesPerAxis & 1) == 0)
                settings.NativeVolumeSlicesPerAxis = Math.Min(9, settings.NativeVolumeSlicesPerAxis + 1);

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
