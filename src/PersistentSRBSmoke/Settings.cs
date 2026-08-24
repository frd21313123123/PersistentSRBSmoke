using System;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Version-2 configuration for the standalone D3D11 volume renderer. The old particle
    /// configuration deliberately is not read, so an outdated file cannot silently reactivate it.
    /// </summary>
    internal sealed class SmokeSettings
    {
        public const int CurrentSchemaVersion = 2;

        public bool Enabled = true;
        public int SchemaVersion = CurrentSchemaVersion;

        // Fixed simulation pool and screen-space budget.
        public int MaxStoredSegments = 4096;
        public int VisibleNearSegments = 256;
        public int VisibleMidSegments = 512;
        public int VisibleFarSegments = 256;
        public float NearDistance = 350f;
        public float MidDistance = 1800f;
        public float FarDistance = 9000f;
        public float SegmentLength = 5.5f;
        public int MaxSegmentsPerInjection = 24;
        public float MergeMinAge = 8f;
        public float MergeCellSize = 42f;

        // Source integration. Mass is optical mass, not a count of visual particles.
        public float BaseEmissionRate = 58f;
        public float MassPerMeter = 8.5f;
        public float TimeEmissionFadeSpeed = 20f;
        public float TeleportDistance = 750f;
        public float NozzleOffset = 2.2f;
        public float NozzleLength = 10f;
        public float NozzleRadius = 1.45f;
        public float NozzleLifetime = 0.90f;
        public float TrailRadius = 9f;
        public float TrailLifetime = 210f;
        public float ThinAtmosphereDensityFloor = 0.42f;

        // Evolution. This is evaluated at a bounded cadence, including Universal Time warp.
        public float DynamicMotionHz = 4f;
        public float OffscreenDynamicMotionHz = 0.5f;
        public float MaxWarpSimulationStep = 5f;
        public float DissipationRate = 0.88f;
        public float RadiusGrowth = 1.95f;
        public float DiffusionSpeed = 4.4f;
        public float Buoyancy = 0.24f;
        public float DynamicWindResponse = 1.6f;
        public float TurbulenceStrength = 1.1f;
        public float TurbulenceFrequency = 0.055f;
        public float NearGroundHoldHeight = 60f;
        public float NearGroundWindMultiplier = 0.12f;
        public float NearGroundDiffusionMultiplier = 0.25f;
        public float NearGroundBuoyancyMultiplier = 0.40f;

        // Logical 32^3 pad tiles. The shader evaluates the volume procedurally from these tiles.
        public bool PadFieldEnabled = true;
        public int PadTileCount = 8;
        public int PadTileResolution = 32;
        public float PadTileSize = 90f;
        public float PadFieldHeight = 120f;
        public float PadOutflowSpeed = 18f;
        public float PadUpdraftSpeed = 5.5f;
        public float PadMassThreshold = 5f;
        public float PadMassSaturation = 24f;
        public float PadMassBias = 0.72f;

        // Lighting and raymarch parameters for the bundled shader.
        public float Extinction = 0.074f;
        public float Scattering = 0.82f;
        public float AmbientLight = 0.28f;
        public float SunLight = 1.05f;
        public float SunsetWarmth = 0.72f;
        public float NoiseScale = 0.082f;
        public float NoiseStrength = 0.58f;
        public int NearViewSamples = 24;
        public int MidViewSamples = 14;
        public int FarViewSamples = 8;
        public int SunShadowSamples = 4;
        public int TileSize = 16;
        public int MaxTileCandidates = 64;
        public bool TemporalReconstruction = true;
        public float TemporalBlend = 0.82f;
        public float TemporalDepthThreshold = 0.004f;

        // Projected volume-shadow layer. It shares the established terrain cache strategy.
        public bool ShadowsEnabled = true;
        public float ShadowUpdateHz = 8f;
        public int ShadowMaxQuads = 900;
        public float ShadowOpacity = 0.18f;
        public float ShadowSizeMultiplier = 1.55f;
        public float ShadowLengthMultiplier = 1.15f;
        public float ShadowSurfaceOffset = 4f;
        public float ShadowMaxAltitude = 14000f;
        public int ShadowTerrainQueriesPerFrame = 16;
        public float ShadowTerrainCacheMeters = 220f;
        public int ShadowTerrainCacheCapacity = 12000;

        // SRB selection, scale and retained KSP behavior.
        public float EngineScanInterval = 2f;
        public bool EngineScalingEnabled = true;
        public float EngineMinThrust = 8f;
        public float EngineMaxThrust = 800f;
        public float SmallEngineEmissionMultiplier = 0.90f;
        public float LargeEngineEmissionMultiplier = 1.10f;
        public float SmallEngineSizeMultiplier = 1.30f;
        public float LargeEngineSizeMultiplier = 1.65f;
        public float SmallEngineLifetimeMultiplier = 0.75f;
        public float LargeEngineLifetimeMultiplier = 1.00f;
        public float SmallEngineOpacityMultiplier = 0.95f;
        public float LargeEngineOpacityMultiplier = 1.08f;
        public float SmallEngineSpacingMultiplier = 1.00f;
        public float LargeEngineSpacingMultiplier = 0.95f;
        public float SmokeBrightness = 1.16f;
        public float EngineColorVariation = 0.05f;
        public int FullDensityEmitterBudget = 8;
        public float MinimumEmitterDensityScale = 0.35f;

        // Wind and KSP lifecycle.
        public bool WindEnabled = true;
        public int WindCacheLayers = 64;
        public float WindSpeed = 4.4f;
        public float WindLayerHeight = 9000f;
        public float WindTopAltitude = 32000f;
        public float WindDirectionChangeRadians = 0.30f;
        public float WindTimeScale = 0.00012f;
        public float WindSpreadSpeed = 1.0f;
        public float WindSpreadScale = 850f;
        public float WindSpreadVerticalScale = 2600f;
        public float WindSpreadTimeScale = 0.00035f;
        public bool FollowUniversalTime = true;
        public bool SuppressStockSmoke = true;
        public float StockSmokeRefreshInterval = 0.75f;
        public bool DebugLogging = false;

        public int MaxVisibleSegments
        {
            get { return VisibleNearSegments + VisibleMidSegments + VisibleFarSegments; }
        }

        public static SmokeSettings Load()
        {
            SmokeSettings settings = new SmokeSettings();
            string path = KSPUtil.ApplicationRootPath
                + "GameData/PersistentSRBSmoke/PluginData/Settings.cfg";

            try
            {
                ConfigNode file = ConfigNode.Load(path);
                ConfigNode node = file == null ? null : file.GetNode("VOLUMETRIC_SRB_SMOKE");
                if (node == null)
                {
                    Debug.LogWarning(
                        "[PersistentSRBSmoke] Settings.cfg has no VOLUMETRIC_SRB_SMOKE node; "
                        + "using clean schema v2 defaults. Legacy settings are intentionally ignored.");
                    return settings;
                }

                settings.SchemaVersion = ReadInt(node, "schemaVersion", settings.SchemaVersion, 1, 99);
                if (settings.SchemaVersion != CurrentSchemaVersion)
                {
                    Debug.LogWarning(
                        "[PersistentSRBSmoke] Settings schema " + settings.SchemaVersion
                        + " is incompatible with volumetric schema " + CurrentSchemaVersion
                        + "; using clean defaults. Replace Settings.cfg from this release.");
                    return new SmokeSettings();
                }

                settings.Enabled = ReadBool(node, "enabled", settings.Enabled);
                settings.MaxStoredSegments = ReadInt(node, "maxStoredSegments", settings.MaxStoredSegments, 256, 4096);
                settings.VisibleNearSegments = ReadInt(node, "visibleNearSegments", settings.VisibleNearSegments, 0, 4096);
                settings.VisibleMidSegments = ReadInt(node, "visibleMidSegments", settings.VisibleMidSegments, 0, 4096);
                settings.VisibleFarSegments = ReadInt(node, "visibleFarSegments", settings.VisibleFarSegments, 0, 4096);
                ClampVisibleBudget(settings);
                settings.NearDistance = ReadFloat(node, "nearDistance", settings.NearDistance, 5f, 100000f);
                settings.MidDistance = ReadFloat(node, "midDistance", settings.MidDistance, settings.NearDistance, 100000f);
                settings.FarDistance = ReadFloat(node, "farDistance", settings.FarDistance, settings.MidDistance, 1000000f);
                settings.SegmentLength = ReadFloat(node, "segmentLength", settings.SegmentLength, 0.5f, 100f);
                settings.MaxSegmentsPerInjection = ReadInt(node, "maxSegmentsPerInjection", settings.MaxSegmentsPerInjection, 1, 128);
                settings.MergeMinAge = ReadFloat(node, "mergeMinAge", settings.MergeMinAge, 0f, 10000f);
                settings.MergeCellSize = ReadFloat(node, "mergeCellSize", settings.MergeCellSize, 2f, 1000f);

                settings.BaseEmissionRate = ReadFloat(node, "baseEmissionRate", settings.BaseEmissionRate, 0f, 10000f);
                settings.MassPerMeter = ReadFloat(node, "massPerMeter", settings.MassPerMeter, 0f, 10000f);
                settings.TimeEmissionFadeSpeed = ReadFloat(node, "timeEmissionFadeSpeed", settings.TimeEmissionFadeSpeed, 0.1f, 300f);
                settings.TeleportDistance = ReadFloat(node, "teleportDistance", settings.TeleportDistance, 10f, 50000f);
                settings.NozzleOffset = ReadFloat(node, "nozzleOffset", settings.NozzleOffset, 0f, 100f);
                settings.NozzleLength = ReadFloat(node, "nozzleLength", settings.NozzleLength, 0.1f, 200f);
                settings.NozzleRadius = ReadFloat(node, "nozzleRadius", settings.NozzleRadius, 0.05f, 100f);
                settings.NozzleLifetime = ReadFloat(node, "nozzleLifetime", settings.NozzleLifetime, 0.05f, 10f);
                settings.TrailRadius = ReadFloat(node, "trailRadius", settings.TrailRadius, 0.1f, 500f);
                settings.TrailLifetime = ReadFloat(node, "trailLifetime", settings.TrailLifetime, 1f, 10000f);
                settings.ThinAtmosphereDensityFloor = ReadFloat(node, "thinAtmosphereDensityFloor", settings.ThinAtmosphereDensityFloor, 0f, 1f);

                settings.DynamicMotionHz = ReadFloat(node, "dynamicMotionHz", settings.DynamicMotionHz, 0.1f, 60f);
                settings.OffscreenDynamicMotionHz = ReadFloat(node, "offscreenDynamicMotionHz", settings.OffscreenDynamicMotionHz, 0.05f, 60f);
                settings.MaxWarpSimulationStep = ReadFloat(node, "maxWarpSimulationStep", settings.MaxWarpSimulationStep, 0.05f, 120f);
                settings.DissipationRate = ReadFloat(node, "dissipationRate", settings.DissipationRate, 0f, 20f);
                settings.RadiusGrowth = ReadFloat(node, "radiusGrowth", settings.RadiusGrowth, 0f, 20f);
                settings.DiffusionSpeed = ReadFloat(node, "diffusionSpeed", settings.DiffusionSpeed, 0f, 100f);
                settings.Buoyancy = ReadFloat(node, "buoyancy", settings.Buoyancy, -20f, 20f);
                settings.DynamicWindResponse = ReadFloat(node, "dynamicWindResponse", settings.DynamicWindResponse, 0f, 30f);
                settings.TurbulenceStrength = ReadFloat(node, "turbulenceStrength", settings.TurbulenceStrength, 0f, 50f);
                settings.TurbulenceFrequency = ReadFloat(node, "turbulenceFrequency", settings.TurbulenceFrequency, 0.001f, 5f);
                settings.NearGroundHoldHeight = ReadFloat(node, "nearGroundHoldHeight", settings.NearGroundHoldHeight, 0f, 1000f);
                settings.NearGroundWindMultiplier = ReadFloat(node, "nearGroundWindMultiplier", settings.NearGroundWindMultiplier, 0f, 1f);
                settings.NearGroundDiffusionMultiplier = ReadFloat(node, "nearGroundDiffusionMultiplier", settings.NearGroundDiffusionMultiplier, 0f, 1f);
                settings.NearGroundBuoyancyMultiplier = ReadFloat(node, "nearGroundBuoyancyMultiplier", settings.NearGroundBuoyancyMultiplier, 0f, 1f);

                settings.PadFieldEnabled = ReadBool(node, "padFieldEnabled", settings.PadFieldEnabled);
                settings.PadTileCount = ReadInt(node, "padTileCount", settings.PadTileCount, 1, 8);
                settings.PadTileResolution = ReadInt(node, "padTileResolution", settings.PadTileResolution, 32, 32);
                settings.PadTileSize = ReadFloat(node, "padTileSize", settings.PadTileSize, 10f, 1000f);
                settings.PadFieldHeight = ReadFloat(node, "padFieldHeight", settings.PadFieldHeight, 10f, 1000f);
                settings.PadOutflowSpeed = ReadFloat(node, "padOutflowSpeed", settings.PadOutflowSpeed, 0f, 100f);
                settings.PadUpdraftSpeed = ReadFloat(node, "padUpdraftSpeed", settings.PadUpdraftSpeed, 0f, 100f);
                settings.PadMassThreshold = ReadFloat(node, "padMassThreshold", settings.PadMassThreshold, 0.01f, 10000f);
                settings.PadMassSaturation = ReadFloat(node, "padMassSaturation", settings.PadMassSaturation, settings.PadMassThreshold, 10000f);
                settings.PadMassBias = ReadFloat(node, "padMassBias", settings.PadMassBias, 0f, 1f);

                settings.Extinction = ReadFloat(node, "extinction", settings.Extinction, 0.001f, 5f);
                settings.Scattering = ReadFloat(node, "scattering", settings.Scattering, 0f, 5f);
                settings.AmbientLight = ReadFloat(node, "ambientLight", settings.AmbientLight, 0f, 5f);
                settings.SunLight = ReadFloat(node, "sunLight", settings.SunLight, 0f, 5f);
                settings.SunsetWarmth = ReadFloat(node, "sunsetWarmth", settings.SunsetWarmth, 0f, 1f);
                settings.NoiseScale = ReadFloat(node, "noiseScale", settings.NoiseScale, 0.001f, 2f);
                settings.NoiseStrength = ReadFloat(node, "noiseStrength", settings.NoiseStrength, 0f, 1f);
                settings.NearViewSamples = ReadInt(node, "nearViewSamples", settings.NearViewSamples, 4, 64);
                settings.MidViewSamples = ReadInt(node, "midViewSamples", settings.MidViewSamples, 2, 64);
                settings.FarViewSamples = ReadInt(node, "farViewSamples", settings.FarViewSamples, 1, 64);
                settings.SunShadowSamples = ReadInt(node, "sunShadowSamples", settings.SunShadowSamples, 0, 16);
                settings.TileSize = ReadInt(node, "tileSize", settings.TileSize, 8, 64);
                settings.MaxTileCandidates = ReadInt(node, "maxTileCandidates", settings.MaxTileCandidates, 8, 128);
                settings.TemporalReconstruction = ReadBool(node, "temporalReconstruction", settings.TemporalReconstruction);
                settings.TemporalBlend = ReadFloat(node, "temporalBlend", settings.TemporalBlend, 0f, 0.98f);
                settings.TemporalDepthThreshold = ReadFloat(node, "temporalDepthThreshold", settings.TemporalDepthThreshold, 0.0001f, 0.1f);

                settings.ShadowsEnabled = ReadBool(node, "shadowsEnabled", settings.ShadowsEnabled);
                settings.ShadowUpdateHz = ReadFloat(node, "shadowUpdateHz", settings.ShadowUpdateHz, 0.1f, 60f);
                settings.ShadowMaxQuads = ReadInt(node, "shadowMaxQuads", settings.ShadowMaxQuads, 1, 4096);
                settings.ShadowOpacity = ReadFloat(node, "shadowOpacity", settings.ShadowOpacity, 0f, 1f);
                settings.ShadowSizeMultiplier = ReadFloat(node, "shadowSizeMultiplier", settings.ShadowSizeMultiplier, 0.1f, 10f);
                settings.ShadowLengthMultiplier = ReadFloat(node, "shadowLengthMultiplier", settings.ShadowLengthMultiplier, 0.1f, 10f);
                settings.ShadowSurfaceOffset = ReadFloat(node, "shadowSurfaceOffset", settings.ShadowSurfaceOffset, 0f, 100f);
                settings.ShadowMaxAltitude = ReadFloat(node, "shadowMaxAltitude", settings.ShadowMaxAltitude, 100f, 100000f);
                settings.ShadowTerrainQueriesPerFrame = ReadInt(node, "shadowTerrainQueriesPerFrame", settings.ShadowTerrainQueriesPerFrame, 0, 512);
                settings.ShadowTerrainCacheMeters = ReadFloat(node, "shadowTerrainCacheMeters", settings.ShadowTerrainCacheMeters, 20f, 5000f);
                settings.ShadowTerrainCacheCapacity = ReadInt(node, "shadowTerrainCacheCapacity", settings.ShadowTerrainCacheCapacity, 256, 100000);

                settings.EngineScanInterval = ReadFloat(node, "engineScanInterval", settings.EngineScanInterval, 0.2f, 30f);
                settings.EngineScalingEnabled = ReadBool(node, "engineScalingEnabled", settings.EngineScalingEnabled);
                settings.EngineMinThrust = ReadFloat(node, "engineMinThrust", settings.EngineMinThrust, 0.1f, 5000f);
                settings.EngineMaxThrust = ReadFloat(node, "engineMaxThrust", settings.EngineMaxThrust, settings.EngineMinThrust + 0.1f, 20000f);
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
                settings.SmokeBrightness = ReadFloat(node, "smokeBrightness", settings.SmokeBrightness, 0.1f, 2f);
                settings.EngineColorVariation = ReadFloat(node, "engineColorVariation", settings.EngineColorVariation, 0f, 0.3f);
                settings.FullDensityEmitterBudget = ReadInt(node, "fullDensityEmitterBudget", settings.FullDensityEmitterBudget, 1, 64);
                settings.MinimumEmitterDensityScale = ReadFloat(node, "minimumEmitterDensityScale", settings.MinimumEmitterDensityScale, 0.05f, 1f);

                settings.WindEnabled = ReadBool(node, "windEnabled", settings.WindEnabled);
                settings.WindCacheLayers = ReadInt(node, "windCacheLayers", settings.WindCacheLayers, 8, 256);
                settings.WindSpeed = ReadFloat(node, "windSpeed", settings.WindSpeed, 0f, 80f);
                settings.WindLayerHeight = ReadFloat(node, "windLayerHeight", settings.WindLayerHeight, 100f, 20000f);
                settings.WindTopAltitude = ReadFloat(node, "windTopAltitude", settings.WindTopAltitude, 1000f, 100000f);
                settings.WindDirectionChangeRadians = ReadFloat(node, "windDirectionChangeRadians", settings.WindDirectionChangeRadians, 0f, 6.283185f);
                settings.WindTimeScale = ReadFloat(node, "windTimeScale", settings.WindTimeScale, 0f, 0.05f);
                settings.WindSpreadSpeed = ReadFloat(node, "windSpreadSpeed", settings.WindSpreadSpeed, 0f, 20f);
                settings.WindSpreadScale = ReadFloat(node, "windSpreadScale", settings.WindSpreadScale, 30f, 5000f);
                settings.WindSpreadVerticalScale = ReadFloat(node, "windSpreadVerticalScale", settings.WindSpreadVerticalScale, 80f, 20000f);
                settings.WindSpreadTimeScale = ReadFloat(node, "windSpreadTimeScale", settings.WindSpreadTimeScale, 0f, 0.1f);
                settings.FollowUniversalTime = ReadBool(node, "followUniversalTime", settings.FollowUniversalTime);
                settings.SuppressStockSmoke = ReadBool(node, "suppressStockSmoke", settings.SuppressStockSmoke);
                settings.StockSmokeRefreshInterval = ReadFloat(node, "stockSmokeRefreshInterval", settings.StockSmokeRefreshInterval, 0.1f, 10f);
                settings.DebugLogging = ReadBool(node, "debugLogging", settings.DebugLogging);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PersistentSRBSmoke] Failed to load volumetric Settings.cfg: " + ex);
            }

            return settings;
        }

        private static void ClampVisibleBudget(SmokeSettings settings)
        {
            int total = settings.MaxVisibleSegments;
            if (total <= settings.MaxStoredSegments)
                return;

            float scale = settings.MaxStoredSegments / (float)Mathf.Max(1, total);
            settings.VisibleNearSegments = Mathf.FloorToInt(settings.VisibleNearSegments * scale);
            settings.VisibleMidSegments = Mathf.FloorToInt(settings.VisibleMidSegments * scale);
            settings.VisibleFarSegments = Mathf.Max(0, settings.MaxStoredSegments
                - settings.VisibleNearSegments - settings.VisibleMidSegments);
        }

        private static float ReadFloat(ConfigNode node, string key, float fallback, float min, float max)
        {
            string raw = node.GetValue(key);
            float value;
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? Mathf.Clamp(value, min, max)
                : fallback;
        }

        private static int ReadInt(ConfigNode node, string key, int fallback, int min, int max)
        {
            string raw = node.GetValue(key);
            int value;
            return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? Math.Max(min, Math.Min(max, value))
                : fallback;
        }

        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            string raw = node.GetValue(key);
            bool value;
            return raw != null && bool.TryParse(raw, out value) ? value : fallback;
        }
    }
}
