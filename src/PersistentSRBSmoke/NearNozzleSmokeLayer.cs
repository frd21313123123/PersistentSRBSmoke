using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Short-lived, high-detail smoke immediately behind active SRB nozzles.
    ///
    /// The persistent trail intentionally stays in SmokeParticlePool. This layer is a separate
    /// billboard system whose particles inherit most of the nozzle world velocity. That keeps the
    /// close plume visually attached to a fast rocket instead of leaving a chain of isolated puffs.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class NearNozzleSmokeLayer : MonoBehaviour
    {
        private sealed class LayerSettings
        {
            public bool Enabled = true;
            public int MaxParticles = 6500;
            public float EmissionRate = 110f;
            public float ParticlesPerMeter = 0.70f;
            public int MaxEmitPerFrame = 96;
            public float Lifetime = 0.90f;
            public float StartSize = 1.35f;
            public float SizeGrowth = 5.30f;
            public float Opacity = 0.72f;
            public float InitialSpeed = 12.0f;
            public float VelocityInheritance = 0.97f;
            public float SpreadSpeed = 1.10f;
            public float Offset = 0.25f;
            public float RadialJitter = 0.12f;
            public float Turbulence = 0.52f;
            public float TurbulenceFrequency = 0.12f;
            public float HighAltitudeSizeMultiplier = 1.45f;
            public float Brightness = 0.80f;
            public float Warmth = 0.035f;
            public float EngineMinThrust = 8f;
            public float EngineMaxThrust = 800f;
            public float TeleportDistance = 750f;
            public float ScanInterval = 1.50f;
            public bool DebugLogging = false;

            public static LayerSettings Load()
            {
                var settings = new LayerSettings();
                try
                {
                    string path = KSPUtil.ApplicationRootPath + "GameData/PersistentSRBSmoke/PluginData/Settings.cfg";
                    ConfigNode file = ConfigNode.Load(path);
                    ConfigNode node = file == null ? null : file.GetNode("PERSISTENT_SRB_SMOKE");
                    if (node == null)
                        return settings;

                    settings.Enabled = ReadBool(node, "enabled", true)
                        && ReadBool(node, "nearNozzleEnabled", settings.Enabled);
                    settings.MaxParticles = ReadInt(node, "nearNozzleMaxParticles", settings.MaxParticles, 250, 30000);
                    settings.EmissionRate = ReadFloat(node, "nearNozzleEmissionRate", settings.EmissionRate, 0f, 500f);
                    settings.ParticlesPerMeter = ReadFloat(node, "nearNozzleParticlesPerMeter", settings.ParticlesPerMeter, 0f, 5f);
                    settings.MaxEmitPerFrame = ReadInt(node, "nearNozzleMaxEmitPerFrame", settings.MaxEmitPerFrame, 1, 256);
                    settings.Lifetime = ReadFloat(node, "nearNozzleLifetime", settings.Lifetime, 0.10f, 10f);
                    settings.StartSize = ReadFloat(node, "nearNozzleStartSize", settings.StartSize, 0.05f, 50f);
                    settings.SizeGrowth = ReadFloat(node, "nearNozzleSizeGrowth", settings.SizeGrowth, 1f, 12f);
                    settings.Opacity = ReadFloat(node, "nearNozzleOpacity", settings.Opacity, 0.01f, 1f);
                    settings.InitialSpeed = ReadFloat(node, "nearNozzleInitialSpeed", settings.InitialSpeed, 0f, 80f);
                    settings.VelocityInheritance = ReadFloat(node, "nearNozzleVelocityInheritance", settings.VelocityInheritance, 0f, 1f);
                    settings.SpreadSpeed = ReadFloat(node, "nearNozzleSpreadSpeed", settings.SpreadSpeed, 0f, 30f);
                    settings.Offset = ReadFloat(node, "nearNozzleOffset", settings.Offset, 0f, 20f);
                    settings.RadialJitter = ReadFloat(node, "nearNozzleRadialJitter", settings.RadialJitter, 0f, 3f);
                    settings.Turbulence = ReadFloat(node, "nearNozzleTurbulence", settings.Turbulence, 0f, 10f);
                    settings.TurbulenceFrequency = ReadFloat(node, "nearNozzleTurbulenceFrequency", settings.TurbulenceFrequency, 0.001f, 2f);
                    settings.HighAltitudeSizeMultiplier = ReadFloat(node, "nearNozzleHighAltitudeSizeMultiplier", settings.HighAltitudeSizeMultiplier, 1f, 5f);
                    settings.Brightness = ReadFloat(node, "nearNozzleBrightness", settings.Brightness, 0.2f, 1.3f);
                    settings.Warmth = ReadFloat(node, "nearNozzleWarmth", settings.Warmth, 0f, 0.35f);
                    settings.EngineMinThrust = ReadFloat(node, "engineMinThrust", settings.EngineMinThrust, 0.1f, 5000f);
                    settings.EngineMaxThrust = ReadFloat(node, "engineMaxThrust", settings.EngineMaxThrust, 0.2f, 20000f);
                    settings.TeleportDistance = ReadFloat(node, "teleportDistance", settings.TeleportDistance, 10f, 10000f);
                    settings.ScanInterval = ReadFloat(node, "engineScanInterval", settings.ScanInterval, 0.25f, 30f);
                    settings.DebugLogging = ReadBool(node, "debugLogging", settings.DebugLogging);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PersistentSRBSmoke] Near-nozzle settings fallback: " + ex.Message);
                }

                if (settings.EngineMaxThrust <= settings.EngineMinThrust)
                    settings.EngineMaxThrust = settings.EngineMinThrust + 1f;
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

        private sealed class EmitterState
        {
            public ModuleEngines Engine;
            public Transform Transform;
            public float EmitterShare;
            public float Strength;
            public Vector3 LastPosition;
            public bool HasLastPosition;
            public float Accumulator;
        }

        private LayerSettings _settings;
        private GameObject _particleObject;
        private ParticleSystem _system;
        private Material _material;
        private Texture2D _texture;
        private bool _floatingOriginRegistered;
        private float _nextScan;

        private readonly Dictionary<int, EmitterState> _emitters = new Dictionary<int, EmitterState>();
        private readonly HashSet<int> _seenEmitters = new HashSet<int>();
        private readonly List<int> _emittersToRemove = new List<int>();

        private void Start()
        {
            _settings = LayerSettings.Load();
            if (!_settings.Enabled)
            {
                enabled = false;
                return;
            }

            try
            {
                CreateParticleSystem();
                ScanEngines();
                if (_settings.DebugLogging)
                {
                    Debug.Log(
                        "[PersistentSRBSmoke] Near-nozzle layer enabled: maxParticles=" + _settings.MaxParticles +
                        " lifetime=" + _settings.Lifetime.ToString("F2") +
                        " velocityInheritance=" + _settings.VelocityInheritance.ToString("F3"));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[PersistentSRBSmoke] Near-nozzle layer initialization failed: " + ex);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            if (_system == null || !HighLogic.LoadedSceneIsFlight)
                return;

            float now = Time.realtimeSinceStartup;
            if (now >= _nextScan)
            {
                ScanEngines();
                _nextScan = now + Mathf.Max(0.25f, _settings.ScanInterval);
            }

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            foreach (EmitterState emitter in _emitters.Values)
                UpdateEmitter(emitter, dt);
        }

        private void UpdateEmitter(EmitterState emitter, float dt)
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
                emitter.Accumulator = 0f;
                return;
            }

            if (!IsProducingThrust(engine))
            {
                emitter.Accumulator = 0f;
                return;
            }

            Vessel vessel = engine.vessel;
            float atmosphere = GetAtmosphereFactor(vessel);
            if (atmosphere <= 0.001f)
                return;

            Vector3 emitterVelocity = travelVector / dt;

            float thrustFactor = GetThrustFactor(engine);
            float engineWeight = Mathf.Lerp(0.72f, 1.28f, Mathf.SmoothStep(0f, 1f, emitter.Strength));
            float desired = (_settings.EmissionRate * dt + travel * _settings.ParticlesPerMeter)
                * thrustFactor
                * Mathf.Lerp(0.58f, 1f, atmosphere)
                * engineWeight
                * emitter.EmitterShare;

            emitter.Accumulator += desired;
            int count = Mathf.Min(_settings.MaxEmitPerFrame, Mathf.FloorToInt(emitter.Accumulator));
            if (count <= 0)
                return;
            emitter.Accumulator -= count;

            // Stock and modded engines are not perfectly consistent about the sign of their thrust
            // transforms. Resolve the direction against the physical nozzle side of the part so the
            // smoke always exits away from the motor casing rather than occasionally through it.
            Vector3 exhaustDirection = ResolveExhaustDirection(engine, exhaust, vessel);

            Vector3 up = vessel.upAxis;
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.up;
            up.Normalize();

            Vector3 tangentA = Vector3.Cross(exhaustDirection, up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(exhaustDirection, Vector3.right);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(exhaustDirection, Vector3.forward);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(exhaustDirection, tangentA).normalized;

            float thinAir = 1f - Mathf.Clamp01(atmosphere);
            float strengthCurve = Mathf.SmoothStep(0f, 1f, emitter.Strength);
            float sizeScale = Mathf.Lerp(0.52f, 1.18f, strengthCurve)
                * Mathf.Lerp(1f, _settings.HighAltitudeSizeMultiplier, thinAir);

            for (int i = 0; i < count; i++)
            {
                float slotJitter = UnityEngine.Random.Range(-0.08f, 0.08f);
                float t = count == 1 ? 1f : Mathf.Clamp01((i + 0.5f + slotJitter) / count);
                Vector3 position = Vector3.Lerp(previousPosition, currentPosition, t);

                position += exhaustDirection * (_settings.Offset * sizeScale);
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(UnityEngine.Random.value)
                    * _settings.RadialJitter
                    * _settings.StartSize
                    * sizeScale;
                Vector3 radialDirection = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                position += radialDirection * radius;

                float lateralSpeed = _settings.SpreadSpeed
                    * Mathf.Lerp(0.82f, 1.12f, thinAir)
                    * UnityEngine.Random.Range(0.35f, 1.00f);
                float jetSpeed = _settings.InitialSpeed
                    * Mathf.Lerp(0.76f, 1.10f, thrustFactor)
                    * Mathf.Lerp(1f, 1.16f, thinAir)
                    * UnityEngine.Random.Range(0.88f, 1.10f);

                // Relative to the nozzle every puff now has a guaranteed positive component along
                // exhaustDirection. Turbulence may widen the cone but cannot throw smoke into the
                // opposite hemisphere.
                Vector3 relativeVelocity = exhaustDirection * jetSpeed
                    + radialDirection * lateralSpeed;
                Vector3 velocity = emitterVelocity * _settings.VelocityInheritance + relativeVelocity;

                float localBrightness = _settings.Brightness * UnityEngine.Random.Range(0.94f, 1.03f);
                float warmth = _settings.Warmth;
                Color color = new Color(
                    Mathf.Clamp01(localBrightness * (1f + warmth * 0.08f)),
                    Mathf.Clamp01(localBrightness * (1f - warmth * 0.03f)),
                    Mathf.Clamp01(localBrightness * (1f - warmth * 0.12f)),
                    Mathf.Clamp01(_settings.Opacity
                        * Mathf.Lerp(0.68f, 1f, atmosphere)
                        * UnityEngine.Random.Range(0.88f, 1f)));

                var emit = new ParticleSystem.EmitParams();
                emit.position = position;
                emit.velocity = velocity;
                emit.startLifetime = _settings.Lifetime
                    * Mathf.Lerp(1.0f, 0.82f, thinAir)
                    * UnityEngine.Random.Range(0.90f, 1.08f);
                emit.startSize = _settings.StartSize
                    * sizeScale
                    * UnityEngine.Random.Range(0.86f, 1.14f);
                emit.startColor = color;
                emit.rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                _system.Emit(emit, 1);
            }
        }

        private static Vector3 ResolveExhaustDirection(ModuleEngines engine, Transform exhaust, Vessel vessel)
        {
            Vector3 fallback = vessel == null ? Vector3.down : -vessel.upAxis;
            if (fallback.sqrMagnitude < 0.001f)
                fallback = Vector3.down;
            fallback.Normalize();

            if (exhaust == null)
                return fallback;

            Vector3 forward = exhaust.forward;
            if (forward.sqrMagnitude < 0.001f)
                return fallback;
            forward.Normalize();

            if (engine == null || engine.part == null)
                return -forward;

            // Prefer the centre of the complete nozzle cluster. For multi-nozzle engines the vector
            // from the part origin to one individual transform can point sideways, while the cluster
            // centre still identifies which end of the motor contains the exits.
            Vector3 outwardHint = Vector3.zero;
            if (engine.thrustTransforms != null && engine.thrustTransforms.Count > 0)
            {
                Vector3 clusterCenter = Vector3.zero;
                int valid = 0;
                for (int i = 0; i < engine.thrustTransforms.Count; i++)
                {
                    Transform transform = engine.thrustTransforms[i];
                    if (transform == null)
                        continue;
                    clusterCenter += transform.position;
                    valid++;
                }

                if (valid > 0)
                    outwardHint = clusterCenter / valid - engine.part.transform.position;
            }

            if (outwardHint.sqrMagnitude < 0.01f)
                outwardHint = exhaust.position - engine.part.transform.position;

            if (outwardHint.sqrMagnitude >= 0.01f)
            {
                outwardHint.Normalize();
                float alignment = Vector3.Dot(forward, outwardHint);
                if (Mathf.Abs(alignment) >= 0.20f)
                    return alignment >= 0f ? forward : -forward;
            }

            // Preserve KSP's usual convention when geometry is too ambiguous to distinguish a side.
            return -forward;
        }

        private void ScanEngines()
        {
            _seenEmitters.Clear();
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
                                RegisterEmitter(engine, engine.thrustTransforms[t], share);
                        }
                        else
                        {
                            RegisterEmitter(engine, part.transform, 1f);
                        }
                    }
                }
            }

            _emittersToRemove.Clear();
            foreach (KeyValuePair<int, EmitterState> pair in _emitters)
            {
                if (!_seenEmitters.Contains(pair.Key))
                    _emittersToRemove.Add(pair.Key);
            }
            for (int i = 0; i < _emittersToRemove.Count; i++)
                _emitters.Remove(_emittersToRemove[i]);
        }

        private void RegisterEmitter(ModuleEngines engine, Transform transform, float emitterShare)
        {
            if (transform == null)
                return;

            int key = transform.GetInstanceID();
            _seenEmitters.Add(key);
            if (_emitters.ContainsKey(key))
                return;

            _emitters.Add(key, new EmitterState
            {
                Engine = engine,
                Transform = transform,
                EmitterShare = Mathf.Clamp(emitterShare, 0.02f, 1f),
                Strength = GetEngineStrength(engine),
                LastPosition = transform.position,
                HasLastPosition = true,
                Accumulator = 0f
            });
        }

        private float GetEngineStrength(ModuleEngines engine)
        {
            if (engine == null)
                return 1f;
            float thrust = Mathf.Max(0.1f, engine.maxThrust);
            float minLog = Mathf.Log10(Mathf.Max(0.1f, _settings.EngineMinThrust));
            float maxLog = Mathf.Log10(Mathf.Max(_settings.EngineMinThrust + 0.1f, _settings.EngineMaxThrust));
            return Mathf.Clamp01(Mathf.InverseLerp(minLog, maxLog, Mathf.Log10(thrust)));
        }

        private static bool UsesSolidFuel(ModuleEngines engine)
        {
            if (engine == null || engine.propellants == null)
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
            return engine != null && engine.EngineIgnited && !engine.flameout && engine.finalThrust > 0.01f;
        }

        private static float GetThrustFactor(ModuleEngines engine)
        {
            if (engine == null || engine.maxThrust <= 0.001f)
                return 1f;
            return Mathf.Clamp01(engine.finalThrust / engine.maxThrust);
        }

        private static float GetAtmosphereFactor(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.atmosphere)
                return 0f;

            float atmosphereDepth = Mathf.Max(1f, (float)vessel.mainBody.atmosphereDepth);
            float altitude = Mathf.Max(0f, (float)vessel.altitude);
            if (altitude >= atmosphereDepth)
                return 0f;

            float altitudeRatio = Mathf.Clamp01(altitude / atmosphereDepth);
            float edgeT = Mathf.Clamp01((altitudeRatio - 0.90f) / 0.10f);
            float edgeFade = 1f - Mathf.SmoothStep(0f, 1f, edgeT);
            double pressureKpa = vessel.staticPressurekPa;
            float normalizedPressure = Mathf.Clamp01((float)(pressureKpa / 101.325));
            float pressureResponse = Mathf.Pow(normalizedPressure, 0.16f);
            return Mathf.Clamp01(Mathf.Lerp(0.45f, 1f, pressureResponse) * edgeFade);
        }

        private void CreateParticleSystem()
        {
            _particleObject = new GameObject("PersistentSRBSmoke.NearNozzle");
            UnityEngine.Object.DontDestroyOnLoad(_particleObject);

            _system = _particleObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_system);

            ParticleSystemRenderer renderer = _particleObject.GetComponent<ParticleSystemRenderer>();
            _texture = CreateDenseSmokeTexture(128);
            _material = CreateParticleMaterial(_texture);
            renderer.material = _material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            try
            {
                FloatingOrigin.RegisterParticleSystem(_system);
                _floatingOriginRegistered = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersistentSRBSmoke] Could not register near-nozzle particles with FloatingOrigin: " + ex.Message);
            }
            _system.Play();
        }

        private void ConfigureParticleSystem(ParticleSystem system)
        {
            var main = system.main;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _settings.MaxParticles;
            main.startLifetime = _settings.Lifetime;
            main.startSize = _settings.StartSize;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            float growth = Mathf.Max(1f, _settings.SizeGrowth);
            AnimationCurve expansion = new AnimationCurve(
                new Keyframe(0.00f, 0.72f, 4.5f, 4.5f),
                new Keyframe(0.08f, 1.05f, 4.5f, 4.5f),
                new Keyframe(0.22f, Mathf.Min(growth, 1.80f), 5.0f, 5.0f),
                new Keyframe(0.50f, Mathf.Min(growth, 3.20f), 4.0f, 4.0f),
                new Keyframe(1.00f, growth, 1.0f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, expansion);

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.90f, 0.89f, 0.86f), 0.00f),
                    new GradientColorKey(new Color(0.86f, 0.86f, 0.84f), 0.16f),
                    new GradientColorKey(new Color(0.81f, 0.82f, 0.82f), 0.48f),
                    new GradientColorKey(new Color(0.75f, 0.77f, 0.78f), 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(0.82f, 0.00f),
                    new GradientAlphaKey(0.76f, 0.12f),
                    new GradientAlphaKey(0.55f, 0.42f),
                    new GradientAlphaKey(0.24f, 0.72f),
                    new GradientAlphaKey(0.00f, 1.00f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var noise = system.noise;
            noise.enabled = _settings.Turbulence > 0.001f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.Turbulence;
            noise.frequency = _settings.TurbulenceFrequency;
            noise.scrollSpeed = 0.14f;
            noise.damping = true;
            noise.octaveCount = 2;
        }

        private static Material CreateParticleMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("KSP/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                throw new InvalidOperationException("No compatible transparent particle shader was found.");

            Material material = new Material(shader);
            material.name = "PersistentSRBSmoke.NearNozzleMaterial";
            material.mainTexture = texture;
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", Color.white);
            return material;
        }

        private static Texture2D CreateDenseSmokeTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.name = "PersistentSRBSmoke.NearNozzleDenseTexture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float seedX = UnityEngine.Random.Range(10f, 1000f);
            float seedY = UnityEngine.Random.Range(10f, 1000f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float macro = Mathf.PerlinNoise(seedX + u * 1.7f, seedY + v * 1.7f);
                    float billow = Mathf.PerlinNoise(seedX * 0.41f + u * 4.2f, seedY * 0.41f + v * 4.2f);
                    float detail = Mathf.PerlinNoise(seedX * 0.17f + u * 8.4f, seedY * 0.17f + v * 8.4f);

                    float edgeWarp = (macro - 0.5f) * 0.28f + (billow - 0.5f) * 0.12f;
                    float radial = Mathf.Clamp01(1f - (radius + edgeWarp));
                    radial = Mathf.Pow(radial, 0.62f);
                    float interior = Mathf.Clamp01(macro * 0.44f + billow * 0.39f + detail * 0.17f);
                    float alpha = radial * Mathf.Lerp(0.38f, 0.88f, interior);
                    alpha = Mathf.Clamp01((alpha - 0.020f) * 1.08f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (_system != null && _floatingOriginRegistered)
            {
                try { FloatingOrigin.UnregisterParticleSystem(_system); }
                catch { }
                _floatingOriginRegistered = false;
            }

            _emitters.Clear();
            _seenEmitters.Clear();
            _emittersToRemove.Clear();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            if (_particleObject != null) UnityEngine.Object.Destroy(_particleObject);
            _material = null;
            _texture = null;
            _system = null;
            _particleObject = null;
        }
    }
}
