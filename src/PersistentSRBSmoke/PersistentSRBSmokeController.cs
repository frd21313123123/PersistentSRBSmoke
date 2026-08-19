using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal struct EngineSmokeProfile
    {
        public float Strength;
        public float EmissionMultiplier;
        public float SizeMultiplier;
        public float LifetimeMultiplier;
        public float OpacityMultiplier;
        public float SpacingMultiplier;
        public Color BaseColor;

        public static EngineSmokeProfile Create(ModuleEngines engine, SmokeSettings settings, float emitterShare)
        {
            emitterShare = Mathf.Clamp(emitterShare, 0.05f, 1f);

            float strength = 1f;
            if (settings.EngineScalingEnabled && engine != null)
            {
                float thrust = Mathf.Max(0.1f, engine.maxThrust);
                float minLog = Mathf.Log10(Mathf.Max(0.1f, settings.EngineMinThrust));
                float maxLog = Mathf.Log10(Mathf.Max(settings.EngineMinThrust + 0.1f, settings.EngineMaxThrust));
                float thrustLog = Mathf.Log10(thrust);
                strength = Mathf.Clamp01(Mathf.InverseLerp(minLog, maxLog, thrustLog));
            }

            float curve = Mathf.SmoothStep(0f, 1f, strength);

            float emission = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineEmissionMultiplier, settings.LargeEngineEmissionMultiplier, curve)
                : 1f;
            float size = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineSizeMultiplier, settings.LargeEngineSizeMultiplier, curve)
                : 1f;
            float lifetime = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineLifetimeMultiplier, settings.LargeEngineLifetimeMultiplier, curve)
                : 1f;
            float opacity = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineOpacityMultiplier, settings.LargeEngineOpacityMultiplier, curve)
                : 1f;
            float spacing = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineSpacingMultiplier, settings.LargeEngineSpacingMultiplier, curve)
                : 1f;

            // When one engine exposes several thrust transforms, share the particle budget between
            // them instead of treating every nozzle as a complete independent SRB.
            emission *= emitterShare;
            spacing *= Mathf.Sqrt(1f / emitterShare);

            // Small separation motors are intentionally darker and a little warmer. Large boosters
            // remain neutral grey, but the whole palette is darker than v0.3.
            Color smallColor = new Color(0.36f, 0.33f, 0.30f, 1f);
            Color largeColor = new Color(0.56f, 0.55f, 0.53f, 1f);
            Color baseColor = Color.Lerp(smallColor, largeColor, curve);

            string engineName = engine != null && engine.part != null ? engine.part.name : string.Empty;
            float variation = (StableHashToUnit(engineName) * 2f - 1f) * settings.EngineColorVariation;
            baseColor.r *= 1f + variation;
            baseColor.g *= 1f + variation * 0.20f;
            baseColor.b *= 1f - variation * 0.85f;

            float brightness = settings.SmokeBrightness;
            baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
            baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
            baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
            baseColor.a = 1f;

            return new EngineSmokeProfile
            {
                Strength = strength,
                EmissionMultiplier = Mathf.Max(0.01f, emission),
                SizeMultiplier = Mathf.Max(0.05f, size),
                LifetimeMultiplier = Mathf.Max(0.05f, lifetime),
                OpacityMultiplier = Mathf.Max(0.05f, opacity),
                SpacingMultiplier = Mathf.Max(0.20f, spacing),
                BaseColor = baseColor
            };
        }

        private static float StableHashToUnit(string text)
        {
            unchecked
            {
                uint hash = 2166136261U;
                if (!string.IsNullOrEmpty(text))
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        hash ^= text[i];
                        hash *= 16777619U;
                    }
                }

                return (hash & 0x00FFFFFFU) / 16777215f;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class PersistentSRBSmokeController : MonoBehaviour
    {
        private sealed class EngineEmitter
        {
            public ModuleEngines Engine;
            public Transform Transform;
            public Vector3 LastPosition;
            public bool HasLastPosition;
            public float EmissionAccumulator;
            public EngineSmokeProfile Profile;
        }

        private SmokeSettings _settings;
        private SmokeParticlePool _smoke;
        private WindModel _wind;
        private readonly Dictionary<int, EngineEmitter> _emitters = new Dictionary<int, EngineEmitter>();
        private float _nextEngineScan;
        private float _nextDynamicMotion;
        private float _lastDynamicMotion;
        private float _nextDebugLog;

        private void Start()
        {
            _settings = SmokeSettings.Load();
            if (!_settings.Enabled)
            {
                Debug.Log("[PersistentSRBSmoke] Disabled in Settings.cfg");
                enabled = false;
                return;
            }

            try
            {
                _smoke = new SmokeParticlePool(_settings);
                _wind = new WindModel(_settings);
                ScanEngines();
                _lastDynamicMotion = Time.realtimeSinceStartup;
                Debug.Log("[PersistentSRBSmoke] v0.3.1 initialized with engine-specific smoke profiles.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PersistentSRBSmoke] Initialization failed: " + ex);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            if (_smoke == null || !HighLogic.LoadedSceneIsFlight)
                return;

            float now = Time.realtimeSinceStartup;

            if (now >= _nextEngineScan)
            {
                ScanEngines();
                _nextEngineScan = now + 1.0f;
            }

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            foreach (EngineEmitter emitter in _emitters.Values)
                UpdateEmitter(emitter, dt);

            float dynamicInterval = 1f / Mathf.Max(1f, _settings.DynamicMotionHz);
            if (now >= _nextDynamicMotion)
            {
                Vessel activeVessel = FlightGlobals.ActiveVessel;
                if (activeVessel != null && activeVessel.mainBody != null)
                {
                    float dynamicDt = Mathf.Clamp(now - _lastDynamicMotion, 0.001f, 0.5f);
                    _smoke.UpdateDynamicMotion(
                        activeVessel.mainBody,
                        _wind,
                        Planetarium.GetUniversalTime(),
                        dynamicDt);
                }

                _lastDynamicMotion = now;
                _nextDynamicMotion = now + dynamicInterval;
            }

            if (_settings.DebugLogging && now >= _nextDebugLog)
            {
                Debug.Log("[PersistentSRBSmoke] SRB emitters=" + _emitters.Count + " particles=" + _smoke.ParticleCount);
                _nextDebugLog = now + 5f;
            }
        }

        private void UpdateEmitter(EngineEmitter emitter, float dt)
        {
            ModuleEngines engine = emitter.Engine;
            Transform exhaust = emitter.Transform;
            if (engine == null || exhaust == null || engine.part == null || engine.vessel == null)
                return;

            Vector3 currentPosition = exhaust.position;
            if (!emitter.HasLastPosition)
            {
                emitter.LastPosition = currentPosition;
                emitter.HasLastPosition = true;
                return;
            }

            Vector3 previousPosition = emitter.LastPosition;
            emitter.LastPosition = currentPosition;

            Vector3 travelVector = currentPosition - previousPosition;
            float travel = travelVector.magnitude;
            if (travel > _settings.TeleportDistance)
            {
                emitter.EmissionAccumulator = 0f;
                return;
            }

            if (!IsProducingThrust(engine))
                return;

            Vessel vessel = engine.vessel;
            float atmosphere = GetAtmosphereFactor(vessel);
            if (atmosphere <= 0.001f)
                return;

            float thrustFactor = GetThrustFactor(engine);
            EngineSmokeProfile profile = emitter.Profile;

            float desired = (_settings.BaseEmissionRate * dt + travel * _settings.ParticlesPerMeter)
                * thrustFactor
                * atmosphere
                * profile.EmissionMultiplier;
            emitter.EmissionAccumulator += desired;
            int accumulatedCount = Mathf.FloorToInt(emitter.EmissionAccumulator);

            // The continuity budget now scales with engine size too. A tiny separation motor is
            // allowed a much larger spacing than a Shuttle-class SRB, so it no longer paints the
            // same huge persistent tube along the trajectory.
            float effectiveSpacing = _settings.MaxParticleSpacing
                * Mathf.Lerp(_settings.HighAltitudeSpacingMultiplier, 1.0f, atmosphere)
                * profile.SpacingMultiplier;
            int spacingCount = travel > 0.001f
                ? Mathf.CeilToInt(travel / Mathf.Max(0.25f, effectiveSpacing))
                : 0;

            int count = Mathf.Min(_settings.MaxEmitPerFrame, Mathf.Max(accumulatedCount, spacingCount));
            if (count <= 0)
                return;

            emitter.EmissionAccumulator -= Mathf.Min(accumulatedCount, count);

            Vector3 up = vessel.upAxis;
            if (up.sqrMagnitude < 0.001f)
                up = currentPosition.normalized;
            up.Normalize();

            Vector3 trailDirection = travel > 0.001f ? travelVector / travel : -exhaust.forward;
            if (trailDirection.sqrMagnitude < 0.001f)
                trailDirection = up;
            trailDirection.Normalize();

            Vector3 tangentA = Vector3.Cross(trailDirection, up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(trailDirection, Vector3.right);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(trailDirection, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(trailDirection, tangentA).normalized;

            double universalTime = Planetarium.GetUniversalTime();
            Vector3 wind = _wind == null ? Vector3.zero : _wind.GetWind(vessel, up, universalTime);
            float scale = Mathf.Lerp(0.78f, 1.30f, Mathf.Sqrt(thrustFactor));

            for (int i = 0; i < count; i++)
            {
                float slotJitter = UnityEngine.Random.Range(-0.12f, 0.12f);
                float t = count == 1 ? 1f : ((i + 0.5f + slotJitter) / count);
                Vector3 point = Vector3.Lerp(previousPosition, currentPosition, Mathf.Clamp01(t));

                float radialJitter = _settings.StartSize * scale * profile.SizeMultiplier * 0.12f;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(UnityEngine.Random.value) * radialJitter;
                point += tangentA * (Mathf.Cos(angle) * radius) + tangentB * (Mathf.Sin(angle) * radius);

                _smoke.Emit(point, up, wind, atmosphere, scale, profile);
            }
        }

        private void ScanEngines()
        {
            var seen = new HashSet<int>();

            IList<Vessel> vessels = FlightGlobals.VesselsLoaded;
            if (vessels == null)
                return;

            for (int v = 0; v < vessels.Count; v++)
            {
                Vessel vessel = vessels[v];
                if (vessel == null || !vessel.loaded || vessel.parts == null)
                    continue;

                for (int p = 0; p < vessel.parts.Count; p++)
                {
                    Part part = vessel.parts[p];
                    if (part == null || part.Modules == null)
                        continue;

                    for (int m = 0; m < part.Modules.Count; m++)
                    {
                        ModuleEngines engine = part.Modules[m] as ModuleEngines;
                        if (engine == null || !UsesSolidFuel(engine))
                            continue;

                        if (engine.thrustTransforms != null && engine.thrustTransforms.Count > 0)
                        {
                            float share = 1f / engine.thrustTransforms.Count;
                            for (int t = 0; t < engine.thrustTransforms.Count; t++)
                                RegisterEmitter(engine, engine.thrustTransforms[t], seen, share);
                        }
                        else
                        {
                            RegisterEmitter(engine, part.transform, seen, 1f);
                        }
                    }
                }
            }

            if (_emitters.Count == seen.Count)
                return;

            var toRemove = new List<int>();
            foreach (KeyValuePair<int, EngineEmitter> pair in _emitters)
            {
                if (!seen.Contains(pair.Key))
                    toRemove.Add(pair.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
                _emitters.Remove(toRemove[i]);
        }

        private void RegisterEmitter(ModuleEngines engine, Transform transform, HashSet<int> seen, float emitterShare)
        {
            if (transform == null)
                return;

            int key = transform.GetInstanceID();
            seen.Add(key);
            if (_emitters.ContainsKey(key))
                return;

            EngineSmokeProfile profile = EngineSmokeProfile.Create(engine, _settings, emitterShare);
            _emitters.Add(key, new EngineEmitter
            {
                Engine = engine,
                Transform = transform,
                LastPosition = transform.position,
                HasLastPosition = true,
                EmissionAccumulator = 0f,
                Profile = profile
            });

            if (_settings.DebugLogging)
            {
                string partName = engine != null && engine.part != null ? engine.part.name : "unknown";
                float thrust = engine == null ? 0f : engine.maxThrust;
                Debug.Log(
                    "[PersistentSRBSmoke] profile part=" + partName +
                    " thrust=" + thrust.ToString("F1") + "kN" +
                    " strength=" + profile.Strength.ToString("F2") +
                    " emission=" + profile.EmissionMultiplier.ToString("F2") +
                    " size=" + profile.SizeMultiplier.ToString("F2") +
                    " lifetime=" + profile.LifetimeMultiplier.ToString("F2"));
            }
        }

        private static bool UsesSolidFuel(ModuleEngines engine)
        {
            if (engine.propellants == null)
                return false;

            for (int i = 0; i < engine.propellants.Count; i++)
            {
                Propellant propellant = engine.propellants[i];
                if (propellant != null && string.Equals(propellant.name, "SolidFuel", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsProducingThrust(ModuleEngines engine)
        {
            return engine.EngineIgnited && !engine.flameout && engine.finalThrust > 0.01f;
        }

        private static float GetThrustFactor(ModuleEngines engine)
        {
            if (engine.maxThrust <= 0.001f)
                return 1f;
            return Mathf.Clamp01(engine.finalThrust / engine.maxThrust);
        }

        private float GetAtmosphereFactor(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.atmosphere)
                return 0f;

            float atmosphereDepth = Mathf.Max(1f, (float)vessel.mainBody.atmosphereDepth);
            float altitude = Mathf.Max(0f, (float)vessel.altitude);
            if (altitude >= atmosphereDepth)
                return 0f;

            float altitudeRatio = Mathf.Clamp01(altitude / atmosphereDepth);
            float edgeT = Mathf.Clamp01((altitudeRatio - 0.88f) / 0.12f);
            float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, edgeT);

            double pressureKpa = vessel.staticPressurekPa;
            float normalizedPressure = Mathf.Clamp01((float)(pressureKpa / 101.325));
            float pressureResponse = Mathf.Pow(normalizedPressure, 0.20f);
            float density = Mathf.Lerp(_settings.ThinAtmosphereDensityFloor, 1f, pressureResponse);

            return Mathf.Clamp01(density * edgeFade);
        }

        private void OnDestroy()
        {
            if (_smoke != null)
            {
                _smoke.Dispose();
                _smoke = null;
            }
            _wind = null;
            _emitters.Clear();
        }
    }
}
