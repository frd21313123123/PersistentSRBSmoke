using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal struct EngineSmokeProfile
    {
        public float Strength;
        public float SolidFuelMass;
        public float PartMass;
        public float EmitterShare;
        public float EmissionMultiplier;
        public float SizeMultiplier;
        public float LifetimeMultiplier;
        public float OpacityMultiplier;
        public float SpacingMultiplier;
        public Color BaseColor;

        public static EngineSmokeProfile Create(ModuleEngines engine, SmokeSettings settings, float emitterShare)
        {
            emitterShare = Mathf.Clamp(emitterShare, 0.05f, 1f);

            float solidFuelMass = 0f;
            float partMass = 0f;
            float strength = 1f;
            if (settings.EngineScalingEnabled && engine != null)
            {
                strength = CalculateMotorScale(engine, settings, out solidFuelMass, out partMass);
            }

            // Ordinary emission/opacity scaling remains smooth, while persistence deliberately uses
            // a steeper curve. A tiny separation motor should disappear quickly instead of receiving
            // nearly the same minutes-long lifetime as a large first-stage SRB.
            float curve = Mathf.SmoothStep(0f, 1f, strength);
            float persistenceCurve = Mathf.Pow(curve, 1.80f);
            float sizeCurve = Mathf.Pow(curve, 1.25f);

            float emission = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineEmissionMultiplier, settings.LargeEngineEmissionMultiplier, curve)
                : 1f;
            float size = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineSizeMultiplier, settings.LargeEngineSizeMultiplier, sizeCurve)
                : 1f;
            float lifetime = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineLifetimeMultiplier, settings.LargeEngineLifetimeMultiplier, persistenceCurve)
                : 1f;
            float opacity = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineOpacityMultiplier, settings.LargeEngineOpacityMultiplier, curve)
                : 1f;
            float spacing = settings.EngineScalingEnabled
                ? Mathf.Lerp(settings.SmallEngineSpacingMultiplier, settings.LargeEngineSpacingMultiplier, curve)
                : 1f;

            emission *= emitterShare;
            spacing *= Mathf.Sqrt(1f / emitterShare);

            // SRB exhaust is predominantly neutral light grey/white in direct sunlight. Keep a
            // subtle warm tint for small motors without the previous brown base colour.
            Color smallColor = new Color(0.82f, 0.82f, 0.80f, 1f);
            Color largeColor = new Color(0.95f, 0.95f, 0.94f, 1f);
            Color baseColor = Color.Lerp(smallColor, largeColor, curve);

            string engineName = engine != null && engine.part != null ? engine.part.name : string.Empty;
            float variation = (StableHashToUnit(engineName) * 2f - 1f) * settings.EngineColorVariation;
            baseColor.r *= 1f + variation;
            baseColor.g *= 1f + variation * 0.20f;
            baseColor.b *= 1f - variation * 0.35f;

            float brightness = settings.SmokeBrightness;
            baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
            baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
            baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
            baseColor.a = 1f;

            return new EngineSmokeProfile
            {
                Strength = strength,
                SolidFuelMass = solidFuelMass,
                PartMass = partMass,
                EmitterShare = emitterShare,
                EmissionMultiplier = Mathf.Max(0.01f, emission),
                SizeMultiplier = Mathf.Max(0.05f, size),
                LifetimeMultiplier = Mathf.Max(0.05f, lifetime),
                OpacityMultiplier = Mathf.Max(0.05f, opacity),
                SpacingMultiplier = Mathf.Max(0.20f, spacing),
                BaseColor = baseColor
            };
        }

        /// <summary>
        /// Estimate how much smoke mass a motor can put into one section of trail. SolidFuel mass is
        /// the primary signal because it tracks the physical propellant volume of an SRB, while dry
        /// part mass and max thrust provide robust fallbacks for unusual/modded motors.
        /// </summary>
        private static float CalculateMotorScale(
            ModuleEngines engine,
            SmokeSettings settings,
            out float solidFuelMass,
            out float partMass)
        {
            solidFuelMass = GetSolidFuelMass(engine == null ? null : engine.part);
            partMass = engine != null && engine.part != null
                ? Mathf.Max(0f, engine.part.mass)
                : 0f;

            float thrust = engine == null ? 0f : Mathf.Max(0.1f, engine.maxThrust);
            float thrustScale = LogNormalize(
                thrust,
                Mathf.Max(0.1f, settings.EngineMinThrust),
                Mathf.Max(settings.EngineMinThrust + 0.1f, settings.EngineMaxThrust));

            // 0.03 t is roughly the scale of very small separation motors; 25 t of SolidFuel is
            // already in large-booster territory. Logarithmic mapping keeps modded sizes sensible.
            float fuelScale = solidFuelMass > 0.0001f
                ? LogNormalize(solidFuelMass, 0.03f, 25f)
                : 0f;
            float dryMassScale = partMass > 0.0001f
                ? LogNormalize(partMass, 0.02f, 8f)
                : 0f;

            float weighted = thrustScale * 0.30f;
            float totalWeight = 0.30f;
            if (solidFuelMass > 0.0001f)
            {
                weighted += fuelScale * 0.55f;
                totalWeight += 0.55f;
            }
            if (partMass > 0.0001f)
            {
                weighted += dryMassScale * 0.15f;
                totalWeight += 0.15f;
            }

            return Mathf.Clamp01(weighted / Mathf.Max(0.001f, totalWeight));
        }

        private static float GetSolidFuelMass(Part part)
        {
            if (part == null || part.Resources == null)
                return 0f;

            try
            {
                PartResource resource = part.Resources["SolidFuel"];
                if (resource == null || resource.info == null || resource.maxAmount <= 0.0)
                    return 0f;

                // KSP resource density is tonnes per resource unit.
                return Mathf.Max(0f, (float)(resource.maxAmount * resource.info.density));
            }
            catch
            {
                return 0f;
            }
        }

        private static float LogNormalize(float value, float minValue, float maxValue)
        {
            value = Mathf.Max(0.000001f, value);
            minValue = Mathf.Max(0.000001f, minValue);
            maxValue = Mathf.Max(minValue * 1.001f, maxValue);

            float minLog = Mathf.Log10(minValue);
            float maxLog = Mathf.Log10(maxValue);
            return Mathf.Clamp01(Mathf.InverseLerp(minLog, maxLog, Mathf.Log10(value)));
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
        private StockSmokeSuppressor _stockSmokeSuppressor;

        private readonly Dictionary<int, EngineEmitter> _emitters = new Dictionary<int, EngineEmitter>();
        private readonly HashSet<Part> _solidFuelParts = new HashSet<Part>();
        private readonly HashSet<int> _seenEmitters = new HashSet<int>();
        private readonly List<int> _emittersToRemove = new List<int>();

        private float _nextEngineScan;
        private float _nextDynamicMotion;
        private float _nextDebugLog;
        private float _nextStockSmokeRefresh;

        private bool _hasUniversalTime;
        private double _lastUniversalTime;
        private double _pendingDynamicGameTime;

        private bool _hasSurfaceReference;
        private float _surfaceReferenceAltitude;

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
                if (_settings.SuppressStockSmoke)
                    _stockSmokeSuppressor = new StockSmokeSuppressor();

                ScanEngines();

                _lastUniversalTime = Planetarium.GetUniversalTime();
                _hasUniversalTime = true;
                _pendingDynamicGameTime = 0.0;

                Version version = typeof(PersistentSRBSmokeController).Assembly.GetName().Version;
                string versionText = version == null
                    ? "unknown"
                    : version.Major + "." + version.Minor + "." + version.Build;
                Debug.Log(
                    "[PersistentSRBSmoke] v" + versionText +
                    " initialized with bounded Waterfall analytic volumes, cached wind, time-sliced light volume, dynamic LOD, UT time-warp sync, pad hold and stock-smoke suppression.");
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
                _nextEngineScan = now + _settings.EngineScanInterval;
            }

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            float activeEmitterWeight = 0f;
            foreach (EngineEmitter emitter in _emitters.Values)
            {
                if (emitter.Engine != null && IsProducingThrust(emitter.Engine))
                    activeEmitterWeight += Mathf.Clamp(emitter.Profile.EmitterShare, 0.05f, 1f);
            }

            float emitterBudgetScale = activeEmitterWeight <= 0f
                ? 1f
                : Mathf.Clamp(
                    _settings.FullDensityEmitterBudget / activeEmitterWeight,
                    _settings.MinimumEmitterDensityScale,
                    1f);

            float occupancy = _smoke.ParticleCount / (float)Mathf.Max(1, _settings.MaxParticles);
            float capacityScale = Mathf.Lerp(
                1f,
                0.55f,
                Mathf.SmoothStep(0.85f, 0.98f, occupancy));
            float globalEmissionScale = emitterBudgetScale * capacityScale;
            float opticalDepthScale = 1f / Mathf.Max(0.25f, globalEmissionScale);
            foreach (EngineEmitter emitter in _emitters.Values)
                UpdateEmitter(emitter, dt, globalEmissionScale, opticalDepthScale);
        }

        private void Update()
        {
            if (_smoke == null || !HighLogic.LoadedSceneIsFlight)
                return;

            double universalTime = Planetarium.GetUniversalTime();
            if (!_hasUniversalTime)
            {
                _lastUniversalTime = universalTime;
                _hasUniversalTime = true;
                return;
            }

            double rawGameDt = universalTime - _lastUniversalTime;
            _lastUniversalTime = universalTime;

            if (double.IsNaN(rawGameDt) || double.IsInfinity(rawGameDt) || rawGameDt < 0.0)
            {
                _pendingDynamicGameTime = 0.0;
                return;
            }

            float gameDt = (float)Math.Min(rawGameDt, 100000.0);
            float unityDt = Mathf.Max(0f, Time.deltaTime);

            if (_settings.FollowUniversalTime && gameDt > 0f)
                _pendingDynamicGameTime += gameDt;
            else if (!_settings.FollowUniversalTime)
                _pendingDynamicGameTime += unityDt;

            // Advance the authoritative Shuriken simulation before taking the next volumetric
            // snapshot. Both paths then observe the same Universal Time during rails/physics warp.
            if (_settings.FollowUniversalTime)
                _smoke.AdvanceUniversalTime(gameDt, unityDt);

            float now = Time.realtimeSinceStartup;
            // EVE obtains much of its speed by time-slicing work that is not currently visible.
            // Apply the same safe principle to simulation: off-screen smoke keeps Unity velocity
            // integration, while expensive wind/flow reevaluation runs at a lower cadence.
            float dynamicHz = _smoke.IsVisible
                ? _settings.DynamicMotionHz
                : Mathf.Min(_settings.DynamicMotionHz, _settings.OffscreenDynamicMotionHz);
            float dynamicInterval = 1f / Mathf.Max(0.1f, dynamicHz);
            if (now >= _nextDynamicMotion && _pendingDynamicGameTime > 0.0)
            {
                Vessel activeVessel = FlightGlobals.ActiveVessel;
                if (activeVessel != null && activeVessel.mainBody != null)
                {
                    float dynamicDt = (float)Math.Min(_pendingDynamicGameTime, 10000.0);
                    _smoke.UpdateDynamicMotion(
                        activeVessel.mainBody,
                        _wind,
                        universalTime,
                        dynamicDt,
                        _hasSurfaceReference,
                        _surfaceReferenceAltitude);
                    _pendingDynamicGameTime = 0.0;
                }

                _nextDynamicMotion = now + dynamicInterval;
            }

            if (_settings.DebugLogging && now >= _nextDebugLog)
            {
                float warpRatio = unityDt > 0.0001f ? gameDt / unityDt : 0f;
                Debug.Log(
                    "[PersistentSRBSmoke] SRB emitters=" + _emitters.Count +
                    " particles=" + _smoke.ParticleCount +
                    " UTdt=" + gameDt.ToString("F2") +
                    " effectiveWarp=" + warpRatio.ToString("F1") + "x");
                _nextDebugLog = now + 5f;
            }
        }

        private void LateUpdate()
        {
            if (_smoke != null && HighLogic.LoadedSceneIsFlight)
                _smoke.LateUpdateVolumetrics();

            if (_stockSmokeSuppressor == null || !_settings.SuppressStockSmoke || !HighLogic.LoadedSceneIsFlight)
                return;

            float now = Time.realtimeSinceStartup;
            if (now >= _nextStockSmokeRefresh)
            {
                foreach (Part part in _solidFuelParts)
                    _stockSmokeSuppressor.RefreshPart(part);

                _stockSmokeSuppressor.ForgetMissing(_solidFuelParts);
                _nextStockSmokeRefresh = now + _settings.StockSmokeRefreshInterval;
            }

            // Engine FX controllers can re-enable their emitters during Update/FixedUpdate. Applying
            // the cached suppression in LateUpdate ensures the stock smoke is still off at render time.
            _stockSmokeSuppressor.SuppressCached(_solidFuelParts);
        }

        private void UpdateEmitter(
            EngineEmitter emitter,
            float dt,
            float globalEmissionScale,
            float opticalDepthScale)
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
            TryCaptureSurfaceReference(vessel);

            float atmosphere = GetAtmosphereFactor(vessel);
            if (atmosphere <= 0.001f)
                return;

            float thrustFactor = GetThrustFactor(engine);
            EngineSmokeProfile profile = emitter.Profile;

            float effectiveSpacing = _settings.MaxParticleSpacing
                * Mathf.Lerp(_settings.HighAltitudeSpacingMultiplier, 1.0f, atmosphere)
                * profile.SpacingMultiplier;

            // One continuous distance accumulator replaces the old max(timeCount, ceil(spacing))
            // switch. Ceil changed count in whole particles at particular velocities, producing
            // the visible bands reported during acceleration. Once moving, every metre now receives
            // the same density regardless of frame rate or vessel speed. Time emission exists only
            // near standstill to build the launch-pad cloud and fades with a smoothstep.
            float speed = travel / Mathf.Max(0.001f, dt);
            float fadeT = Mathf.Clamp01(speed / Mathf.Max(1f, _settings.TimeEmissionFadeSpeed));
            float stationaryBlend = 1f - Mathf.SmoothStep(0f, 1f, fadeT);
            float distanceDensity = Mathf.Max(
                _settings.ParticlesPerMeter,
                1f / Mathf.Max(0.25f, effectiveSpacing));
            float desired = (
                    _settings.BaseEmissionRate * dt * stationaryBlend
                    + travel * distanceDensity)
                * thrustFactor
                * atmosphere
                * profile.EmissionMultiplier
                * Mathf.Clamp01(globalEmissionScale);
            emitter.EmissionAccumulator += desired;
            int accumulatedCount = Mathf.FloorToInt(emitter.EmissionAccumulator);
            int count = Mathf.Min(_settings.MaxEmitPerFrame, accumulatedCount);
            if (count <= 0)
                return;

            emitter.EmissionAccumulator -= count;

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
            float heightAboveGround = GetHeightAboveGround(vessel);
            Vector3 exhaustDirection = NearNozzleSmokeLayer.ResolveExhaustDirection(engine, exhaust, vessel);
            float altitudeExpansion = Mathf.Lerp(
                _settings.HighAltitudeSizeMultiplier,
                1f,
                Mathf.Clamp01(atmosphere));
            float birthDiameter = _settings.StartSize
                * scale
                * profile.SizeMultiplier
                * altitudeExpansion;
            float nozzleOffset = birthDiameter * _settings.NozzleOffsetDiameters
                + _settings.NozzleClearance;

            for (int i = 0; i < count; i++)
            {
                // Deposit samples throughout a real cross-section instead of keeping every
                // cloudlet on a pencil-thin centre line. Dense spacing and low per-sample opacity
                // make these lobes merge into one billowing volume rather than isolated beads.
                float slotJitter = UnityEngine.Random.Range(-0.018f, 0.018f);
                float t = count == 1 ? 1f : ((i + 0.5f + slotJitter) / count);
                Vector3 point = Vector3.Lerp(previousPosition, currentPosition, Mathf.Clamp01(t));

                // The particle mesh is centred on its position. Move that centre far enough down
                // the real exhaust axis that its upper edge cannot overlap the nozzle or vehicle.
                point += exhaustDirection * nozzleOffset;

                float radialJitter = _settings.StartSize * scale * profile.SizeMultiplier * 0.24f;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(UnityEngine.Random.value) * radialJitter;
                point += tangentA * (Mathf.Cos(angle) * radius) + tangentB * (Mathf.Sin(angle) * radius);

                _smoke.Emit(
                    point,
                    up,
                    wind,
                    atmosphere,
                    scale,
                    profile,
                    heightAboveGround,
                    opticalDepthScale);
            }
        }

        private void ScanEngines()
        {
            _seenEmitters.Clear();
            _solidFuelParts.Clear();

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

                    bool hasSolidEngine = false;
                    for (int m = 0; m < part.Modules.Count; m++)
                    {
                        ModuleEngines engine = part.Modules[m] as ModuleEngines;
                        if (engine == null || !UsesSolidFuel(engine))
                            continue;

                        hasSolidEngine = true;
                        if (engine.thrustTransforms != null && engine.thrustTransforms.Count > 0)
                        {
                            float share = 1f / engine.thrustTransforms.Count;
                            for (int t = 0; t < engine.thrustTransforms.Count; t++)
                                RegisterEmitter(engine, engine.thrustTransforms[t], _seenEmitters, share);
                        }
                        else
                        {
                            RegisterEmitter(engine, part.transform, _seenEmitters, 1f);
                        }
                    }

                    if (hasSolidEngine)
                        _solidFuelParts.Add(part);
                }
            }

            _emittersToRemove.Clear();
            foreach (KeyValuePair<int, EngineEmitter> pair in _emitters)
            {
                if (!_seenEmitters.Contains(pair.Key))
                    _emittersToRemove.Add(pair.Key);
            }

            for (int i = 0; i < _emittersToRemove.Count; i++)
                _emitters.Remove(_emittersToRemove[i]);
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
                    " solidFuelMass=" + profile.SolidFuelMass.ToString("F3") + "t" +
                    " partMass=" + profile.PartMass.ToString("F3") + "t" +
                    " motorScale=" + profile.Strength.ToString("F2") +
                    " emission=" + profile.EmissionMultiplier.ToString("F2") +
                    " size=" + profile.SizeMultiplier.ToString("F2") +
                    " lifetime=" + profile.LifetimeMultiplier.ToString("F2"));
            }
        }

        private void TryCaptureSurfaceReference(Vessel vessel)
        {
            if (_hasSurfaceReference || vessel == null)
                return;

            double height = vessel.heightFromTerrain;
            if (double.IsNaN(height) || double.IsInfinity(height) || height < 0.0)
                return;

            // Only capture the local surface while the rocket is actually close to it. This avoids
            // treating an in-flight quickload at several kilometres as a new "launch surface".
            double captureLimit = Math.Max(150.0, _settings.NearGroundHoldHeight * 3.0);
            if (height > captureLimit)
                return;

            _surfaceReferenceAltitude = (float)(vessel.altitude - height);
            _hasSurfaceReference = true;

            if (_settings.DebugLogging)
            {
                Debug.Log(
                    "[PersistentSRBSmoke] Captured launch surface altitude=" +
                    _surfaceReferenceAltitude.ToString("F1") + "m ASL");
            }
        }

        private static float GetHeightAboveGround(Vessel vessel)
        {
            if (vessel == null)
                return -1f;

            double height = vessel.heightFromTerrain;
            if (double.IsNaN(height) || double.IsInfinity(height) || height < 0.0)
                return -1f;

            return Mathf.Max(0f, (float)height);
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

            if (_stockSmokeSuppressor != null)
            {
                _stockSmokeSuppressor.Clear();
                _stockSmokeSuppressor = null;
            }

            _wind = null;
            _solidFuelParts.Clear();
            _seenEmitters.Clear();
            _emittersToRemove.Clear();
            _emitters.Clear();
        }
    }
}
