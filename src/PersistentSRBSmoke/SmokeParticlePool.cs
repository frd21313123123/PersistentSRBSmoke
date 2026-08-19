using System;
using UnityEngine;

namespace PersistentSRBSmoke
{
    internal sealed class SmokeParticlePool : IDisposable
    {
        private readonly SmokeSettings _settings;
        private readonly GameObject _gameObject;
        private readonly ParticleSystem _system;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private bool _floatingOriginRegistered;

        public int ParticleCount { get { return _system == null ? 0 : _system.particleCount; } }

        public SmokeParticlePool(SmokeSettings settings)
        {
            _settings = settings;
            _gameObject = new GameObject("PersistentSRBSmoke.ParticlePool");
            UnityEngine.Object.DontDestroyOnLoad(_gameObject);

            _system = _gameObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_system);

            ParticleSystemRenderer renderer = _gameObject.GetComponent<ParticleSystemRenderer>();
            _texture = CreateSmokeTexture(128);
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

        public void Emit(Vector3 position, Vector3 up, float atmosphericFactor, float scale)
        {
            if (_system == null || atmosphericFactor <= 0f)
                return;

            Vector3 tangentA = Vector3.Cross(up, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float drift = UnityEngine.Random.Range(0.25f, 1f) * _settings.DriftSpeed;
            Vector3 sideways = (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) * drift;
            Vector3 velocity = sideways + up * _settings.Buoyancy;

            Color smokeColor = Color.Lerp(new Color(0.52f, 0.50f, 0.47f, 1f), new Color(0.88f, 0.87f, 0.84f, 1f), UnityEngine.Random.value);
            smokeColor.a = Mathf.Clamp01(_settings.Opacity * atmosphericFactor * UnityEngine.Random.Range(0.82f, 1.08f));

            var emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.velocity = velocity;
            emit.startLifetime = _settings.Lifetime * UnityEngine.Random.Range(0.88f, 1.12f);
            emit.startSize = _settings.StartSize * scale * UnityEngine.Random.Range(0.75f, 1.35f);
            emit.startColor = smokeColor;
            emit.rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _system.Emit(emit, 1);
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

            var size = system.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve expansion = new AnimationCurve(
                new Keyframe(0f, 0.82f, 2.0f, 2.0f),
                new Keyframe(0.04f, 1.15f, 2.2f, 2.2f),
                new Keyframe(0.25f, Mathf.Lerp(1.8f, _settings.SizeGrowth, 0.35f)),
                new Keyframe(1f, _settings.SizeGrowth, 0.15f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, expansion);

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.93f, 0.92f, 0.90f), 0.40f),
                    new GradientColorKey(new Color(0.78f, 0.78f, 0.77f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.92f, 0f),
                    new GradientAlphaKey(0.72f, 0.12f),
                    new GradientAlphaKey(0.38f, 0.60f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var noise = system.noise;
            noise.enabled = _settings.TurbulenceStrength > 0.001f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.TurbulenceStrength;
            noise.frequency = _settings.TurbulenceFrequency;
            noise.scrollSpeed = 0.06f;
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
                    float noise = Mathf.Clamp01(n1 * 0.72f + n2 * 0.28f);
                    float alpha = Mathf.Pow(radial, 0.72f) * Mathf.Lerp(0.42f, 1f, noise);
                    alpha = Mathf.Clamp01((alpha - 0.025f) * 1.12f);

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
