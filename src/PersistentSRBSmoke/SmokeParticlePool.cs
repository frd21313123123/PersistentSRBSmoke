using System;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class SmokeParticlePool : IDisposable
    {
        private readonly SmokeSettings _settings;
        private readonly GameObject _gameObject;
        private readonly ParticleSystem _system;
        private readonly ParticleSystem.Particle[] _particleBuffer;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private bool _floatingOriginRegistered;

        public int ParticleCount { get { return _system == null ? 0 : _system.particleCount; } }

        public SmokeParticlePool(SmokeSettings settings)
        {
            _settings = settings;
            _particleBuffer = new ParticleSystem.Particle[Mathf.Max(1, settings.MaxParticles)];

            _gameObject = new GameObject("PersistentSRBSmoke.ParticlePool");
            UnityEngine.Object.DontDestroyOnLoad(_gameObject);

            _system = _gameObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_system);

            ParticleSystemRenderer renderer = _gameObject.GetComponent<ParticleSystemRenderer>();
            _texture = CreateSmokeTexture(160);
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
                Debug.LogWarning("[PersistentSRBSmoke] Could not register particle system with FloatingOrigin: " + ex.Message);
            }

            _system.Play();
        }

        public void Emit(Vector3 position, Vector3 up, Vector3 wind, float atmosphericFactor, float scale)
        {
            if (_system == null || atmosphericFactor <= 0f)
                return;

            Vector3 tangentA = Vector3.Cross(up, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float drift = UnityEngine.Random.Range(0.45f, 1.15f) * _settings.DriftSpeed;
            Vector3 sideways = (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) * drift;
            Vector3 velocity = wind + sideways + up * _settings.Buoyancy;

            // SRB exhaust carries its own particulate mass. Ambient pressure affects how much the
            // cloud expands, but it should not become almost invisible merely because the vehicle
            // is high in the atmosphere.
            float opacityFactor = Mathf.Pow(Mathf.Clamp01(atmosphericFactor), 0.32f);
            Color smokeColor = Color.Lerp(
                new Color(0.54f, 0.52f, 0.49f, 1f),
                new Color(0.95f, 0.94f, 0.91f, 1f),
                UnityEngine.Random.value);
            smokeColor.a = Mathf.Clamp01(_settings.Opacity * opacityFactor * UnityEngine.Random.Range(0.88f, 1.08f));

            // Lower ambient pressure gives the fresh exhaust room to expand more aggressively.
            float altitudeExpansion = Mathf.Lerp(
                _settings.HighAltitudeSizeMultiplier,
                1f,
                Mathf.Clamp01(atmosphericFactor));

            var emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.velocity = velocity;
            emit.startLifetime = _settings.Lifetime * UnityEngine.Random.Range(0.90f, 1.10f);
            emit.startSize = _settings.StartSize * scale * altitudeExpansion * UnityEngine.Random.Range(0.82f, 1.24f);
            emit.startColor = smokeColor;
            emit.rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _system.Emit(emit, 1);
        }

        // Re-evaluates the velocity of already-existing smoke. Without this, each puff keeps only
        // the wind vector it received at birth and the entire old trail looks frozen. Updating at
        // a modest rate (configured in Settings.cfg) makes the plume shear, drift and broaden while
        // keeping the cost predictable even with tens of thousands of particles.
        public void UpdateDynamicMotion(CelestialBody body, WindModel windModel, double universalTime, float dt)
        {
            if (_system == null || body == null || dt <= 0f)
                return;

            int count = _system.GetParticles(_particleBuffer);
            if (count <= 0)
                return;

            Vector3 bodyCenter = body.transform.position;
            Vector3 bodyNorth = body.transform.up;
            float bodyRadius = (float)body.Radius;
            float response = 1f - Mathf.Exp(-Mathf.Max(0f, _settings.DynamicWindResponse) * dt);

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];

                Vector3 radial = particle.position - bodyCenter;
                float radialMagnitude = radial.magnitude;
                if (radialMagnitude < 1f)
                    continue;

                Vector3 up = radial / radialMagnitude;
                float altitude = Mathf.Max(0f, radialMagnitude - bodyRadius);
                Vector3 wind = windModel == null
                    ? Vector3.zero
                    : windModel.GetWind(body, up, altitude, universalTime);

                Vector3 tangentA = Vector3.Cross(up, bodyNorth);
                if (tangentA.sqrMagnitude < 0.001f)
                    tangentA = Vector3.Cross(up, Vector3.right);
                if (tangentA.sqrMagnitude < 0.001f)
                    tangentA = Vector3.Cross(up, Vector3.forward);
                tangentA.Normalize();
                Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

                float age = particle.startLifetime <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);

                // Each particle has a stable divergence direction derived from its random seed.
                // A slow Perlin wobble changes that direction with age, which keeps the plume from
                // expanding as a mathematically perfect cylinder.
                float seed = HashToUnit(particle.randomSeed);
                float baseAngle = seed * Mathf.PI * 2f;
                float wobble = (Mathf.PerlinNoise(seed * 19.7f + 3.1f, age * 2.2f + 7.4f) - 0.5f) * 1.35f;
                float angle = baseAngle + wobble;
                Vector3 divergenceDirection = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);

                float diffusion = _settings.DiffusionSpeed *
                    (0.55f + _settings.DiffusionGrowth * Mathf.SmoothStep(0f, 1f, age));
                Vector3 desiredVelocity = wind + divergenceDirection * diffusion + up * _settings.Buoyancy;

                particle.velocity = Vector3.Lerp(particle.velocity, desiredVelocity, response);
                _particleBuffer[i] = particle;
            }

            _system.SetParticles(_particleBuffer, count);
        }

        private void ConfigureParticleSystem(ParticleSystem system)
        {
            var main = system.main;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _settings.MaxParticles;
            main.startSpeed = 0f;
            main.startLifetime = _settings.Lifetime;
            main.startSize = _settings.StartSize;
            main.gravityModifier = 0f;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled = false;

            // Real SRB smoke entrains surrounding air very quickly. Most of the visible widening
            // therefore happens in the first 20-60 seconds, not only near the end of particle life.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            float g = Mathf.Max(1f, _settings.SizeGrowth);
            AnimationCurve expansion = new AnimationCurve(
                new Keyframe(0.00f, 1.00f, 5.0f, 5.0f),
                new Keyframe(0.04f, 1.55f, 7.0f, 7.0f),
                new Keyframe(0.10f, Mathf.Min(g, 3.4f), 10.0f, 10.0f),
                new Keyframe(0.22f, Mathf.Min(g, 7.0f), 9.0f, 9.0f),
                new Keyframe(0.45f, Mathf.Min(g, 12.0f), 5.0f, 5.0f),
                new Keyframe(1.00f, g, 0.5f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, expansion);

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.97f, 0.96f, 0.93f), 0.20f),
                    new GradientColorKey(new Color(0.88f, 0.88f, 0.86f), 0.65f),
                    new GradientColorKey(new Color(0.76f, 0.77f, 0.77f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.98f, 0f),
                    new GradientAlphaKey(0.90f, 0.08f),
                    new GradientAlphaKey(0.74f, 0.30f),
                    new GradientAlphaKey(0.48f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var noise = system.noise;
            noise.enabled = _settings.TurbulenceStrength > 0.001f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.TurbulenceStrength;
            noise.frequency = _settings.TurbulenceFrequency;
            noise.scrollSpeed = 0.10f;
            noise.damping = true;
            noise.octaveCount = 2;
        }

        private static float HashToUnit(uint value)
        {
            value ^= value >> 17;
            value *= 0xed5ad4bbU;
            value ^= value >> 11;
            value *= 0xac4c1b51U;
            value ^= value >> 15;
            return (value & 0x00FFFFFFU) / 16777215f;
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
            material.name = "PersistentSRBSmoke.RuntimeMaterial";
            material.mainTexture = texture;
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", Color.white);
            return material;
        }

        private static Texture2D CreateSmokeTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.name = "PersistentSRBSmoke.RuntimeTexture";
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
                    float radial = Mathf.Clamp01(1f - radius);
                    radial = radial * radial * (3f - 2f * radial);

                    float n1 = Mathf.PerlinNoise(seedX + u * 2.1f, seedY + v * 2.1f);
                    float n2 = Mathf.PerlinNoise(seedX * 0.37f + u * 5.3f, seedY * 0.37f + v * 5.3f);
                    float n3 = Mathf.PerlinNoise(seedX * 0.13f + u * 10.7f, seedY * 0.13f + v * 10.7f);
                    float noise = Mathf.Clamp01(n1 * 0.58f + n2 * 0.29f + n3 * 0.13f);
                    float alpha = Mathf.Pow(radial, 0.64f) * Mathf.Lerp(0.36f, 1f, noise);
                    alpha = Mathf.Clamp01((alpha - 0.018f) * 1.16f);

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        public void Dispose()
        {
            if (_system != null && _floatingOriginRegistered)
            {
                try { FloatingOrigin.UnregisterParticleSystem(_system); }
                catch { }
                _floatingOriginRegistered = false;
            }

            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            if (_gameObject != null) UnityEngine.Object.Destroy(_gameObject);
        }
    }
}
