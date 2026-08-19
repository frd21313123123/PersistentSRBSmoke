using System;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class SmokeSettings
    {
        public bool Enabled = true;
        public int MaxParticles = 16000;
        public float Lifetime = 150f;
        public float BaseEmissionRate = 24f;
        public float ParticlesPerMeter = 0.22f;
        public float StartSize = 2.8f;
        public float SizeGrowth = 9.0f;
        public float DriftSpeed = 0.75f;
        public float Buoyancy = 0.18f;
        public float TurbulenceStrength = 0.65f;
        public float TurbulenceFrequency = 0.08f;
        public float Opacity = 0.72f;
        public int MaxEmitPerFrame = 96;
        public float TeleportDistance = 750f;
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
                settings.MaxParticles = ReadInt(node, "maxParticles", settings.MaxParticles, 1000, 100000);
                settings.Lifetime = ReadFloat(node, "lifetime", settings.Lifetime, 5f, 600f);
                settings.BaseEmissionRate = ReadFloat(node, "baseEmissionRate", settings.BaseEmissionRate, 0f, 300f);
                settings.ParticlesPerMeter = ReadFloat(node, "particlesPerMeter", settings.ParticlesPerMeter, 0f, 5f);
                settings.StartSize = ReadFloat(node, "startSize", settings.StartSize, 0.1f, 50f);
                settings.SizeGrowth = ReadFloat(node, "sizeGrowth", settings.SizeGrowth, 1f, 30f);
                settings.DriftSpeed = ReadFloat(node, "driftSpeed", settings.DriftSpeed, 0f, 20f);
                settings.Buoyancy = ReadFloat(node, "buoyancy", settings.Buoyancy, -10f, 10f);
                settings.TurbulenceStrength = ReadFloat(node, "turbulenceStrength", settings.TurbulenceStrength, 0f, 20f);
                settings.TurbulenceFrequency = ReadFloat(node, "turbulenceFrequency", settings.TurbulenceFrequency, 0.001f, 2f);
                settings.Opacity = ReadFloat(node, "opacity", settings.Opacity, 0.01f, 1f);
                settings.MaxEmitPerFrame = ReadInt(node, "maxEmitPerFrame", settings.MaxEmitPerFrame, 1, 1000);
                settings.TeleportDistance = ReadFloat(node, "teleportDistance", settings.TeleportDistance, 10f, 10000f);
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
