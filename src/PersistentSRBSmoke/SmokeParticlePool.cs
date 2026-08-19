using System;
using System.Collections.Generic;
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
        private readonly Mesh _cloudletMesh;
        private readonly PadCloudDensityField _padCloud;
        private bool _floatingOriginRegistered;

        public int ParticleCount { get { return _system == null ? 0 : _system.particleCount; } }

        public SmokeParticlePool(SmokeSettings settings)
        {
            _settings = settings;
            _particleBuffer = new ParticleSystem.Particle[Mathf.Max(1, settings.MaxParticles)];
            _padCloud = new PadCloudDensityField(settings);

            _gameObject = new GameObject("PersistentSRBSmoke.ParticlePool");
            UnityEngine.Object.DontDestroyOnLoad(_gameObject);

            _system = _gameObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_system);

            ParticleSystemRenderer renderer = _gameObject.GetComponent<ParticleSystemRenderer>();
            _texture = CreateSmokeTexture(160);
            _material = CreateParticleMaterial(_texture);
            _cloudletMesh = CreateCloudletMesh();

            renderer.material = _material;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _cloudletMesh;
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

        public void Emit(
            Vector3 position,
            Vector3 up,
            Vector3 wind,
            float atmosphericFactor,
            float scale,
            EngineSmokeProfile profile,
            float heightAboveGround)
        {
            if (_system == null || atmosphericFactor <= 0f)
                return;

            Vector3 tangentA = Vector3.Cross(up, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float smallEngineScatter = Mathf.Lerp(1.18f, 1.0f, profile.Strength);
            float drift = UnityEngine.Random.Range(0.45f, 1.15f)
                * _settings.DriftSpeed
                * smallEngineScatter;
            Vector3 sideways = (tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle)) * drift;

            // Around the launch pad the whole cloud should not simply translate with the wind.
            // Ordinary wind/diffusion remain weak here; PadCloudDensityField supplies a separate
            // density-driven pressure flow once enough exhaust has accumulated in the same volume.
            float groundBlend = GetGroundBlend(heightAboveGround);
            float windScale = Mathf.Lerp(_settings.NearGroundWindMultiplier, 1f, groundBlend);
            float diffusionScale = Mathf.Lerp(_settings.NearGroundDiffusionMultiplier, 1f, groundBlend);
            float buoyancyScale = Mathf.Lerp(_settings.NearGroundBuoyancyMultiplier, 1f, groundBlend);

            Vector3 velocity = wind * windScale
                + sideways * diffusionScale
                + up * (_settings.Buoyancy * buoyancyScale);

            float opacityFactor = Mathf.Pow(Mathf.Clamp01(atmosphericFactor), 0.32f);

            float localBrightness = UnityEngine.Random.Range(0.82f, 1.08f);
            Color smokeColor = new Color(
                Mathf.Clamp01(profile.BaseColor.r * localBrightness),
                Mathf.Clamp01(profile.BaseColor.g * localBrightness),
                Mathf.Clamp01(profile.BaseColor.b * localBrightness),
                1f);

            smokeColor.a = Mathf.Clamp01(
                _settings.Opacity
                * 0.48f
                * profile.OpacityMultiplier
                * opacityFactor
                * UnityEngine.Random.Range(0.88f, 1.08f));

            float altitudeExpansion = Mathf.Lerp(
                _settings.HighAltitudeSizeMultiplier,
                1f,
                Mathf.Clamp01(atmosphericFactor));

            var emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.velocity = velocity;
            emit.startLifetime = _settings.Lifetime
                * profile.LifetimeMultiplier
                * UnityEngine.Random.Range(0.90f, 1.10f);
            emit.startSize = _settings.StartSize
                * scale
                * profile.SizeMultiplier
                * altitudeExpansion
                * UnityEngine.Random.Range(0.82f, 1.24f);
            emit.startColor = smokeColor;
            emit.rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _system.Emit(emit, 1);
        }

        /// <summary>
        /// Unity's particle clock does not reliably follow KSP's on-rails time warp. Advance only
        /// the difference between KSP universal time and the amount Unity already simulated this
        /// frame. This makes lifetime, size-over-lifetime, colour fade, noise and velocity motion
        /// all evolve at the same rate as the game clock.
        /// </summary>
        public void AdvanceUniversalTime(float gameDeltaTime, float unityDeltaTime)
        {
            if (_system == null || gameDeltaTime <= 0f)
                return;

            float extra = gameDeltaTime - Mathf.Max(0f, unityDeltaTime);
            if (extra <= 0.0005f)
                return;

            float largestLifetimeMultiplier = Mathf.Max(
                _settings.SmallEngineLifetimeMultiplier,
                _settings.LargeEngineLifetimeMultiplier);
            float maximumPossibleLifetime = _settings.Lifetime
                * Mathf.Max(1f, largestLifetimeMultiplier)
                * 1.15f;

            // A very large rails-warp jump means every existing particle is older than its maximum
            // possible lifetime. Clearing is both exact for our purposes and avoids hundreds of
            // expensive simulation substeps.
            if (extra >= maximumPossibleLifetime)
            {
                _system.Clear(true);
                return;
            }

            float stepLimit = Mathf.Max(0.25f, _settings.MaxWarpSimulationStep);
            float remaining = extra;
            int guard = 0;
            while (remaining > 0.0005f && guard < 1024)
            {
                float step = Mathf.Min(stepLimit, remaining);
                _system.Simulate(step, true, false, false);
                remaining -= step;
                guard++;
            }
        }

        public void UpdateDynamicMotion(
            CelestialBody body,
            WindModel windModel,
            double universalTime,
            float dt,
            bool hasSurfaceReference,
            float surfaceReferenceAltitude)
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

            // Build one coarse density field per dynamic update. Only cloudlets near the captured
            // launch surface enter the grid, so the long upper-atmosphere trail is unaffected.
            _padCloud.Rebuild(
                _particleBuffer,
                count,
                bodyCenter,
                bodyRadius,
                hasSurfaceReference,
                surfaceReferenceAltitude);

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

                float seed = HashToUnit(particle.randomSeed);
                float baseAngle = seed * Mathf.PI * 2f;
                float wobble = (Mathf.PerlinNoise(seed * 19.7f + 3.1f, age * 2.2f + 7.4f) - 0.5f) * 1.35f;
                float angle = baseAngle + wobble;
                Vector3 divergenceDirection = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);

                float sourceScale = Mathf.Clamp(
                    particle.startLifetime / Mathf.Max(0.01f, _settings.Lifetime),
                    0.30f,
                    1.10f);
                float diffusion = _settings.DiffusionSpeed
                    * Mathf.Lerp(0.62f, 1f, sourceScale)
                    * (0.55f + _settings.DiffusionGrowth * Mathf.SmoothStep(0f, 1f, age));

                float heightAboveSurface = hasSurfaceReference
                    ? Mathf.Max(0f, altitude - surfaceReferenceAltitude)
                    : -1f;
                float groundBlend = GetGroundBlend(heightAboveSurface);
                float windScale = Mathf.Lerp(_settings.NearGroundWindMultiplier, 1f, groundBlend);
                float diffusionScale = Mathf.Lerp(_settings.NearGroundDiffusionMultiplier, 1f, groundBlend);
                float buoyancyScale = Mathf.Lerp(_settings.NearGroundBuoyancyMultiplier, 1f, groundBlend);

                Vector3 padFlow = _padCloud.GetFlow(
                    particle,
                    up,
                    heightAboveSurface,
                    sourceScale,
                    age);

                Vector3 desiredVelocity = wind * windScale
                    + divergenceDirection * (diffusion * diffusionScale)
                    + up * (_settings.Buoyancy * buoyancyScale)
                    + padFlow;

                particle.velocity = Vector3.Lerp(particle.velocity, desiredVelocity, response);
                _particleBuffer[i] = particle;
            }

            _system.SetParticles(_particleBuffer, count);
        }

        private float GetGroundBlend(float heightAboveGround)
        {
            if (heightAboveGround < 0f || _settings.NearGroundHoldHeight <= 0.001f)
                return 1f;

            float t = Mathf.Clamp01(heightAboveGround / _settings.NearGroundHoldHeight);
            return Mathf.SmoothStep(0f, 1f, t);
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
                    new GradientColorKey(new Color(0.94f, 0.94f, 0.94f), 0f),
                    new GradientColorKey(new Color(0.90f, 0.90f, 0.89f), 0.20f),
                    new GradientColorKey(new Color(0.84f, 0.84f, 0.83f), 0.65f),
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

        private static Mesh CreateCloudletMesh()
        {
            Vector3[] normals =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(1f, 1f, 1f).normalized,
                new Vector3(-1f, 1f, 1f).normalized,
                new Vector3(1f, -1f, 1f).normalized
            };

            var vertices = new List<Vector3>(normals.Length * 4);
            var uvs = new List<Vector2>(normals.Length * 4);
            var triangles = new List<int>(normals.Length * 6);

            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 normal = normals[i];
                Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.88f
                    ? Vector3.right
                    : Vector3.up;

                Vector3 axisA = Vector3.Cross(normal, reference).normalized * 0.5f;
                Vector3 axisB = Vector3.Cross(normal, axisA).normalized * 0.5f;

                int baseIndex = vertices.Count;
                vertices.Add(-axisA - axisB);
                vertices.Add(axisA - axisB);
                vertices.Add(axisA + axisB);
                vertices.Add(-axisA + axisB);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
            }

            Mesh mesh = new Mesh();
            mesh.name = "PersistentSRBSmoke.RuntimeCloudlet";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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

            if (_cloudletMesh != null) UnityEngine.Object.Destroy(_cloudletMesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            if (_gameObject != null) UnityEngine.Object.Destroy(_gameObject);
        }
    }
}
