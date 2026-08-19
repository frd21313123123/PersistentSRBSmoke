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

        // Local cloud motion / diffusion
        public float DriftSpeed = 1.8f;
        public float DiffusionSpeed = 3.2f;
        public float DiffusionGrowth = 1.55f;
        public float Buoyancy = 0.24f;
        public float DynamicWindResponse = 2.4f;
        public float TurbulenceStrength = 1.1f;
        public float TurbulenceFrequency = 0.055f;

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

                settings.BaseEmissionRate = ReadFloat(node, "baseEmissionRate", settings.BaseEmissionRate, 0f, 500f);
                settings.ParticlesPerMeter = ReadFloat(node, "particlesPerMeter", settings.ParticlesPerMeter, 0f, 10f);
                settings.MaxParticleSpacing = ReadFloat(node, "maxParticleSpacing", settings.MaxParticleSpacing, 0.25f, 25f);
                settings.HighAltitudeSpacingMultiplier = ReadFloat(node, "highAltitudeSpacingMultiplier", settings.HighAltitudeSpacingMultiplier, 1f, 5f);
                settings.ThinAtmosphereDensityFloor = ReadFloat(node, "thinAtmosphereDensityFloor", settings.ThinAtmosphereDensityFloor, 0f, 1f);

                settings.StartSize = ReadFloat(node, "startSize", settings.StartSize, 0.1f, 100f);
                settings.SizeGrowth = ReadFloat(node, "sizeGrowth", settings.SizeGrowth, 1f, 40f);
                settings.HighAltitudeSizeMultiplier = ReadFloat(node, "highAltitudeSizeMultiplier", settings.HighAltitudeSizeMultiplier, 1f, 5f);
                settings.Opacity = ReadFloat(node, "opacity", settings.Opacity, 0.01f, 1f);

                settings.DriftSpeed = ReadFloat(node, "driftSpeed", settings.DriftSpeed, 0f, 20f);
                settings.DiffusionSpeed = ReadFloat(node, "diffusionSpeed", settings.DiffusionSpeed, 0f, 30f);
                settings.DiffusionGrowth = ReadFloat(node, "diffusionGrowth", settings.DiffusionGrowth, 0f, 5f);
                settings.Buoyancy = ReadFloat(node, "buoyancy", settings.Buoyancy, -10f, 10f);
                settings.DynamicWindResponse = ReadFloat(node, "dynamicWindResponse", settings.DynamicWindResponse, 0f, 20f);
                settings.TurbulenceStrength = ReadFloat(node, "turbulenceStrength", settings.TurbulenceStrength, 0f, 20f);
                settings.TurbulenceFrequency = ReadFloat(node, "turbulenceFrequency", settings.TurbulenceFrequency, 0.001f, 2f);

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
