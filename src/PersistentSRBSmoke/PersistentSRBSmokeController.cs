using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
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
        }

        private SmokeSettings _settings;
        private SmokeParticlePool _smoke;
        private WindModel _wind;
        private readonly Dictionary<int, EngineEmitter> _emitters = new Dictionary<int, EngineEmitter>();
        private float _nextEngineScan;
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
                Debug.Log("[PersistentSRBSmoke] v0.2 initialized with altitude wind shear.");
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

            if (Time.realtimeSinceStartup >= _nextEngineScan)
            {
                ScanEngines();
                _nextEngineScan = Time.realtimeSinceStartup + 1.0f;
            }

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            foreach (EngineEmitter emitter in _emitters.Values)
                UpdateEmitter(emitter, dt);

            if (_settings.DebugLogging && Time.realtimeSinceStartup >= _nextDebugLog)
            {
                Debug.Log("[PersistentSRBSmoke] SRB emitters=" + _emitters.Count + " particles=" + _smoke.ParticleCount);
                _nextDebugLog = Time.realtimeSinceStartup + 5f;
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

            // Keep the original continuous emission model for low-speed and stationary cases.
            float desired = (_settings.BaseEmissionRate * dt + travel * _settings.ParticlesPerMeter) * thrustFactor * atmosphere;
            emitter.EmissionAccumulator += desired;
            int accumulatedCount = Mathf.FloorToInt(emitter.EmissionAccumulator);

            // At high speed the old particles-per-meter accumulator could leave visible holes.
            // Enforce a maximum longitudinal distance between emitted particles. We relax it
            // slightly in thin atmosphere because the smoke is already much more transparent.
            float effectiveSpacing = _settings.MaxParticleSpacing * Mathf.Lerp(1.6f, 1.0f, atmosphere);
            int spacingCount = travel > 0.001f
                ? Mathf.CeilToInt(travel / Mathf.Max(0.25f, effectiveSpacing))
                : 0;

            int count = Mathf.Min(_settings.MaxEmitPerFrame, Mathf.Max(accumulatedCount, spacingCount));
            if (count <= 0)
                return;

            // Consume only particles that came from the accumulator. Spacing-driven particles
            // are additional samples whose only purpose is to keep the trail visually continuous.
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
            float scale = Mathf.Lerp(0.72f, 1.25f, Mathf.Sqrt(thrustFactor));

            for (int i = 0; i < count; i++)
            {
                // Stratified placement prevents particle bunching while still avoiding a perfectly regular pattern.
                float slotJitter = UnityEngine.Random.Range(-0.18f, 0.18f);
                float t = count == 1 ? 1f : ((i + 0.5f + slotJitter) / count);
                Vector3 point = Vector3.Lerp(previousPosition, currentPosition, Mathf.Clamp01(t));

                // Jitter only across the trail. The previous 3D jitter also moved particles along
                // the flight path and could re-open gaps that the interpolation had just filled.
                float radialJitter = _settings.StartSize * scale * 0.10f;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(UnityEngine.Random.value) * radialJitter;
                point += tangentA * (Mathf.Cos(angle) * radius) + tangentB * (Mathf.Sin(angle) * radius);

                _smoke.Emit(point, up, wind, atmosphere, scale);
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
                            for (int t = 0; t < engine.thrustTransforms.Count; t++)
                                RegisterEmitter(engine, engine.thrustTransforms[t], seen);
                        }
                        else
                        {
                            RegisterEmitter(engine, part.transform, seen);
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

        private void RegisterEmitter(ModuleEngines engine, Transform transform, HashSet<int> seen)
        {
            if (transform == null)
                return;

            int key = transform.GetInstanceID();
            seen.Add(key);
            if (_emitters.ContainsKey(key))
                return;

            _emitters.Add(key, new EngineEmitter
            {
                Engine = engine,
                Transform = transform,
                LastPosition = transform.position,
                HasLastPosition = true,
                EmissionAccumulator = 0f
            });
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

        private static float GetAtmosphereFactor(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.atmosphere)
                return 0f;

            if (vessel.altitude >= vessel.mainBody.atmosphereDepth)
                return 0f;

            double pressureKpa = vessel.staticPressurekPa;
            float normalized = Mathf.Clamp01((float)(pressureKpa / 101.325));
            return Mathf.Lerp(0.08f, 1f, Mathf.Sqrt(normalized));
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
