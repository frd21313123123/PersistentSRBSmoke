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
        private readonly ParticleSystemRenderer _renderer;
        private readonly ParticleSystem.Particle[] _particleBuffer;
        private readonly Material _material;
        private readonly Texture2D _texture;
        private readonly Mesh _cloudletMesh;
        private readonly PadCloudDensityField _padCloud;
        private readonly SmokeLightVolume _lightVolume;
        private readonly WaterfallVolumetricLayer _waterfallVolumes;
        private readonly bool _usesEveVolumetricShader;
        private bool _floatingOriginRegistered;
        private int _dynamicUpdateIndex;
        private float _appliedParticleShellOpacity = -1f;

        public int ParticleCount { get { return _system == null ? 0 : _system.particleCount; } }
        public bool IsVisible
        {
            get
            {
                bool particlesVisible = _renderer != null && _renderer.enabled && _renderer.isVisible;
                bool volumesVisible = _waterfallVolumes != null && _waterfallVolumes.IsVisible;
                return particlesVisible || volumesVisible;
            }
        }

        public SmokeParticlePool(SmokeSettings settings)
        {
            _settings = settings;
            _particleBuffer = new ParticleSystem.Particle[Mathf.Max(1, settings.MaxParticles)];
            _padCloud = new PadCloudDensityField(settings);
            _lightVolume = new SmokeLightVolume(settings);

            _gameObject = new GameObject("PersistentSRBSmoke.ParticlePool");
            UnityEngine.Object.DontDestroyOnLoad(_gameObject);

            _system = _gameObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_system);

            _renderer = _gameObject.GetComponent<ParticleSystemRenderer>();
            _texture = CreateSmokeTexture(160);
            string renderStatus;
            Material volumetricMaterial;
            if (EveVolumetricMaterial.TryCreate(_texture, _settings, out volumetricMaterial, out renderStatus))
            {
                _material = volumetricMaterial;
                _usesEveVolumetricShader = true;
            }
            else
            {
                _material = CreateParticleMaterial(_texture);
                _usesEveVolumetricShader = false;
            }
            _cloudletMesh = CreateCloudletMesh(_settings.CloudletPlanes);

            _renderer.material = _material;
            _renderer.renderMode = ParticleSystemRenderMode.Mesh;
            _renderer.mesh = _cloudletMesh;
            _renderer.sortMode = _settings.SortParticles
                ? ParticleSystemSortMode.Distance
                : ParticleSystemSortMode.None;
            if (SystemInfo.supportsInstancing)
                _material.enableInstancing = true;
            ConfigureCheapRendererFeatures(_renderer);

            _waterfallVolumes = new WaterfallVolumetricLayer(_settings);
            UpdateVolumetricPresentation();

            Debug.Log(
                "[PersistentSRBSmoke] Trail renderer: " +
                (_usesEveVolumetricShader ? renderStatus : "procedural cloudlet fallback (" + renderStatus + ")"));
            if (_settings.WaterfallVolumetricEnabled)
                Debug.Log("[PersistentSRBSmoke] Analytic volume backend: " + _waterfallVolumes.Status);

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
            float heightAboveGround,
            float opticalDepthScale)
        {
            if (_system == null || atmosphericFactor <= 0f)
                return;

            Vector3 tangentA = Vector3.Cross(up, Vector3.up);
            if (tangentA.sqrMagnitude < 0.001f)
                tangentA = Vector3.Cross(up, Vector3.right);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;

            // Nearby samples read the same smooth world-space direction field. Independent random
            // angles made adjacent density samples fly apart immediately and revealed the particle
            // lattice; coherent motion is much closer to advection of EVE's cloud density volume.
            float coherentAngle = Mathf.Sin(position.x * 0.013f + position.y * 0.009f + position.z * 0.006f) * 2.15f
                + Mathf.Sin(position.x * -0.005f + position.y * 0.011f + position.z * 0.017f) * 0.95f;
            float angle = coherentAngle + UnityEngine.Random.Range(-0.10f, 0.10f);
            float smallEngineScatter = Mathf.Lerp(1.18f, 1.0f, profile.Strength);
            float drift = UnityEngine.Random.Range(0.94f, 1.06f)
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

            // Broader tonal variation lets overlapping lobes read as illuminated bulges and
            // recessed folds. The texture adds stable fine relief on top of this macro variation.
            float localBrightness = UnityEngine.Random.Range(0.88f, 1.08f);
            Color smokeColor = new Color(
                Mathf.Clamp01(profile.BaseColor.r * localBrightness),
                Mathf.Clamp01(profile.BaseColor.g * localBrightness),
                Mathf.Clamp01(profile.BaseColor.b * localBrightness),
                1f);

            float targetOpacity = Mathf.Clamp01(
                _settings.Opacity
                * profile.OpacityMultiplier
                * opacityFactor
                * UnityEngine.Random.Range(0.98f, 1.02f));

            // When many overlapping boosters share the deposition budget, preserve their optical
            // mass instead of merely making every trail fainter. Beer-Lambert optical depth is
            // additive, so one retained sample can faithfully represent several omitted samples.
            float densityCompensation = Mathf.Clamp(opticalDepthScale, 1f, 4f);
            targetOpacity = 1f - Mathf.Pow(1f - targetOpacity, densityCompensation);

            float altitudeExpansion = Mathf.Lerp(
                _settings.HighAltitudeSizeMultiplier,
                1f,
                Mathf.Clamp01(atmosphericFactor));

            float initialDiameter = _settings.StartSize
                * scale
                * profile.SizeMultiplier
                * altitudeExpansion;
            float depositionSpacing = _settings.MaxParticleSpacing
                * Mathf.Lerp(_settings.HighAltitudeSpacingMultiplier, 1f, atmosphericFactor)
                * profile.SpacingMultiplier;

            // Treat the crossed planes as samples through one density volume. Beer-Lambert style
            // integration includes both crossed planes and the overlapping samples deposited along
            // the trail. This is the particle equivalent of integrating a continuous cloud density
            // field: no individual sample is allowed to become an opaque white bead.
            float spatialOverlap = Mathf.Clamp(
                initialDiameter / Mathf.Max(0.25f, depositionSpacing) * 0.55f,
                1f,
                8f);

            // Crossed quads and neighbouring samples are correlated views of the same density
            // body, not dozens of independent full-opacity slabs. The old linear division made
            // the outer 80% of a large cloudlet effectively invisible. Sublinear normalization
            // retains continuous Beer-Lambert accumulation while exposing the full plume width.
            float opticalSamples = Mathf.Clamp(
                Mathf.Pow(spatialOverlap, 0.42f)
                * (_usesEveVolumetricShader
                    ? 1f
                    : Mathf.Pow(Mathf.Max(1, _settings.CloudletPlanes), 0.35f)),
                1f,
                5f);
            smokeColor.a = 1f - Mathf.Pow(1f - targetOpacity, 1f / opticalSamples);

            var emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.velocity = velocity;
            emit.startLifetime = _settings.Lifetime
                * profile.LifetimeMultiplier
                * UnityEngine.Random.Range(0.90f, 1.10f);
            float cloudletSize = _settings.StartSize
                * scale
                * profile.SizeMultiplier
                * altitudeExpansion
                * UnityEngine.Random.Range(0.78f, 1.30f);
            emit.startSize3D = new Vector3(
                cloudletSize * UnityEngine.Random.Range(0.82f, 1.22f),
                cloudletSize * UnityEngine.Random.Range(0.82f, 1.22f),
                cloudletSize * UnityEngine.Random.Range(0.82f, 1.22f));
            emit.startColor = smokeColor;
            emit.rotation3D = new Vector3(
                UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                UnityEngine.Random.Range(-0.35f, 0.35f));
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
                if (_waterfallVolumes != null)
                    _waterfallVolumes.Clear();
                UpdateVolumetricPresentation();
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
            {
                if (_waterfallVolumes != null)
                    _waterfallVolumes.Clear();
                UpdateVolumetricPresentation();
                return;
            }

            _dynamicUpdateIndex = (_dynamicUpdateIndex + 1) & 0x3FFFFFFF;

            Vector3 bodyCenter = body.transform.position;
            Vector3 bodyNorth = body.transform.up;
            float bodyRadius = (float)body.Radius;
            float responseRate = Mathf.Max(0f, _settings.DynamicWindResponse);

            if (windModel != null)
                windModel.Prepare(body, universalTime);

            Camera camera = Camera.main;
            bool hasCamera = camera != null;
            Vector3 cameraPosition = hasCamera ? camera.transform.position : Vector3.zero;
            float farDistanceSqr = _settings.DynamicFarDistance * _settings.DynamicFarDistance;
            Vector3 sunDirection;
            if (!SmokeLightModel.TryGetSunDirection(body, out sunDirection))
                sunDirection = Vector3.zero;

            // Build one coarse density field per dynamic update. Only cloudlets near the captured
            // launch surface enter the grid, so the long upper-atmosphere trail is unaffected.
            _padCloud.Rebuild(
                _particleBuffer,
                count,
                bodyCenter,
                bodyRadius,
                hasSurfaceReference,
                surfaceReferenceAltitude);

            // Like EVE's shared light volume, lighting is evaluated once per spatial cell and
            // refreshed in time slices. Particles only sample the cached direct/ambient result.
            _lightVolume.Rebuild(
                _particleBuffer,
                count,
                bodyCenter,
                sunDirection);

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];

                float age = particle.startLifetime <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);

                int updateStride = GetDynamicUpdateStride(age, particle.position, hasCamera, cameraPosition, farDistanceSqr);
                if (updateStride > 1)
                {
                    int phase = (int)(particle.randomSeed % (uint)updateStride);
                    if (((_dynamicUpdateIndex + phase) % updateStride) != 0)
                        continue;
                }

                // The majority of old/far particles leave through the stride check above. Delay
                // square roots and all surface-space work until a particle is actually scheduled.
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

                // Cheap continuous curl-like field. World position supplies the macro direction so
                // neighbouring samples advect together; the seed contributes only a little detail.
                // A fully seed-random direction turns a continuous trail back into isolated puffs.
                float seed = HashToUnit(particle.randomSeed);
                Vector3 fieldPosition = radial * 0.010f;
                float angle = Mathf.Sin(fieldPosition.x + fieldPosition.y * 0.73f + age * 2.1f) * 2.05f
                    + Mathf.Sin(fieldPosition.z * 1.31f - fieldPosition.x * 0.47f - age * 1.3f) * 1.10f
                    + (seed - 0.5f) * 0.26f;
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

                // Account for the dynamic ticks skipped by LOD so older/far particles converge to
                // the current flow field at approximately the same physical response rate.
                float effectiveDt = dt * updateStride;
                float response = 1f - Mathf.Exp(-responseRate * effectiveDt);
                particle.velocity = Vector3.Lerp(particle.velocity, desiredVelocity, response);

                if (_settings.LightVolumeEnabled)
                {
                    Vector3 viewDirection = hasCamera
                        ? cameraPosition - particle.position
                        : -sunDirection;
                    Color lighting = _lightVolume.SampleLighting(
                        particle.position,
                        bodyCenter,
                        bodyRadius,
                        viewDirection);
                    Color baseColor = EvaluateBaseSmokeColor(particle, sourceScale);
                    Color currentColor = particle.startColor;
                    float preservedAlpha = currentColor.a;
                    Color targetColor = new Color(
                        Mathf.Clamp01(baseColor.r * lighting.r),
                        Mathf.Clamp01(baseColor.g * lighting.g),
                        Mathf.Clamp01(baseColor.b * lighting.b),
                        preservedAlpha);
                    float lightResponse = 1f - Mathf.Exp(
                        -Mathf.Max(0.1f, _settings.LightResponse) * effectiveDt);
                    Color blendedColor = Color.Lerp(currentColor, targetColor, lightResponse);
                    blendedColor.a = preservedAlpha;
                    particle.startColor = blendedColor;
                }

                _particleBuffer[i] = particle;
            }

            _system.SetParticles(_particleBuffer, count);
            if (_waterfallVolumes != null)
                _waterfallVolumes.Capture(_particleBuffer, count, body);
            UpdateVolumetricPresentation();
        }

        public void LateUpdateVolumetrics()
        {
            if (_waterfallVolumes == null)
                return;

            _waterfallVolumes.LateUpdate();
            UpdateVolumetricPresentation();
        }

        private int GetDynamicUpdateStride(
            float normalizedAge,
            Vector3 position,
            bool hasCamera,
            Vector3 cameraPosition,
            float farDistanceSqr)
        {
            int stride = 1;
            if (normalizedAge >= _settings.DynamicOldAge)
                stride = Mathf.Max(1, _settings.DynamicOldStride);
            else if (normalizedAge >= _settings.DynamicMidAge)
                stride = Mathf.Max(1, _settings.DynamicMidStride);

            if (hasCamera && (position - cameraPosition).sqrMagnitude >= farDistanceSqr)
                stride *= Mathf.Max(1, _settings.DynamicFarStrideMultiplier);

            return Mathf.Clamp(stride, 1, 64);
        }

        private Color EvaluateBaseSmokeColor(ParticleSystem.Particle particle, float sourceScale)
        {
            float motorScale = Mathf.InverseLerp(0.68f, 1.05f, sourceScale);
            Color smallColor = new Color(0.82f, 0.82f, 0.80f, 1f);
            Color largeColor = new Color(0.95f, 0.95f, 0.94f, 1f);
            Color baseColor = Color.Lerp(smallColor, largeColor, motorScale);
            float localBrightness = Mathf.Lerp(
                0.90f,
                1.08f,
                HashToUnit(particle.randomSeed ^ 0xA511E9B3U));
            float brightness = _settings.SmokeBrightness * localBrightness;
            baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
            baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
            baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
            return baseColor;
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
            main.startSize3D = true;
            main.startRotation3D = true;
            main.gravityModifier = 0f;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled = false;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            float g = Mathf.Max(1f, _settings.SizeGrowth);
            AnimationCurve expansion = CreateSmoothExpansionCurve(g);
            size.size = new ParticleSystem.MinMaxCurve(1f, expansion);

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.98f, 0.98f, 0.97f), 0f),
                    new GradientColorKey(new Color(0.94f, 0.94f, 0.93f), 0.20f),
                    new GradientColorKey(new Color(0.88f, 0.89f, 0.89f), 0.65f),
                    new GradientColorKey(new Color(0.80f, 0.82f, 0.83f), 1f)
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

            // Four independently generated billow projections prevent the enlarged plume from
            // revealing a repeated circular sprite. Each particle keeps one random atlas frame.
            var textureSheet = system.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode = ParticleSystemAnimationMode.Grid;
            textureSheet.numTilesX = 2;
            textureSheet.numTilesY = 2;
            textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
            textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            textureSheet.cycleCount = 1;

            // DynamicMotion already supplies coherent turbulence and wind. Unity's per-particle
            // noise module repeats similar work every simulation frame for the full pool, so it is
            // deliberately disabled for the persistent layer.
            var noise = system.noise;
            noise.enabled = false;
        }

        private static AnimationCurve CreateSmoothExpansionCurve(float growth)
        {
            // A sampled exponential has a continuous value and derivative everywhere. The former
            // handful of hand-tuned keys changed slope abruptly; because particle age maps almost
            // directly to distance behind an accelerating rocket, those corners looked like
            // separate speed/height bands in a long trail.
            const int sampleCount = 25;
            const float response = 0.25f;
            const float birthScale = 0.60f;
            float denominator = 1f - Mathf.Exp(-1f / response);
            Keyframe[] keys = new Keyframe[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                float exponential = Mathf.Exp(-t / response);
                float normalized = (1f - exponential) / denominator;
                float value = birthScale + (growth - birthScale) * normalized;
                float tangent = (growth - birthScale) * exponential / (response * denominator);
                keys[i] = new Keyframe(t, value, tangent, tangent);
            }

            return new AnimationCurve(keys);
        }

        private static float EvaluateSmoothExpansion(float age, float growth)
        {
            const float response = 0.25f;
            const float birthScale = 0.60f;
            age = Mathf.Clamp01(age);
            growth = Mathf.Max(1f, growth);
            float denominator = 1f - Mathf.Exp(-1f / response);
            float normalized = (1f - Mathf.Exp(-age / response)) / denominator;
            return birthScale + (growth - birthScale) * normalized;
        }

        private static Mesh CreateCloudletMesh(int requestedPlanes)
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

            int planeCount = Mathf.Clamp(requestedPlanes, 2, normals.Length);
            var vertices = new List<Vector3>(planeCount * 4);
            var uvs = new List<Vector2>(planeCount * 4);
            var meshNormals = new List<Vector3>(planeCount * 4);
            var tangents = new List<Vector4>(planeCount * 4);
            var triangles = new List<int>(planeCount * 6);

            for (int i = 0; i < planeCount; i++)
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

                Vector4 tangent = new Vector4(axisA.x, axisA.y, axisA.z, 1f);
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    meshNormals.Add(normal);
                    tangents.Add(tangent);
                }

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
            mesh.SetNormals(meshNormals);
            mesh.SetTangents(tangents);
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

        private void UpdateVolumetricPresentation()
        {
            if (_renderer == null || _material == null)
                return;

            // Do not dim or replace the proven particle fallback until at least one analytic proxy
            // has passed camera culling in a supported exterior flight view.
            bool hasVolumes = _waterfallVolumes != null && _waterfallVolumes.CanDrivePresentation;
            _renderer.enabled = !(hasVolumes && _settings.WaterfallVolumetricReplaceParticles);

            // In overlay mode the alpha cloudlets provide extinction while Waterfall contributes
            // integrated depth, moving noise and Fresnel relief. Reduce only the shell opacity so
            // both layers do not add up to a flat white column. EVE's private material contract is
            // left untouched.
            float shellOpacity = hasVolumes
                ? Mathf.Clamp01(_settings.WaterfallParticleShellOpacity)
                : 1f;
            if (_usesEveVolumetricShader || !_material.HasProperty("_TintColor") ||
                Mathf.Abs(shellOpacity - _appliedParticleShellOpacity) <= 0.0001f)
            {
                return;
            }

            Color tint = _material.GetColor("_TintColor");
            tint.a = shellOpacity;
            _material.SetColor("_TintColor", tint);
            _appliedParticleShellOpacity = shellOpacity;
        }

        private static void ConfigureCheapRendererFeatures(ParticleSystemRenderer renderer)
        {
            if (renderer == null)
                return;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = true;

            // Mesh particles are submitted as one repeated cloudlet mesh. Compatible shaders can
            // use Unity's instanced path; incompatible legacy shaders safely retain normal batching.
            renderer.enableGPUInstancing = SystemInfo.supportsInstancing;
        }

        private static Texture2D CreateSmokeTexture(int tileSize)
        {
            const int tilesPerAxis = 2;
            int atlasSize = tileSize * tilesPerAxis;
            Texture2D texture = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false, true);
            texture.name = "PersistentSRBSmoke.RuntimeReliefAtlas";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Vector3 lightDirection = new Vector3(-0.46f, 0.66f, 0.59f).normalized;
            for (int tile = 0; tile < tilesPerAxis * tilesPerAxis; tile++)
            {
                float seedX = UnityEngine.Random.Range(10f, 1000f) + tile * 71.3f;
                float seedY = UnityEngine.Random.Range(10f, 1000f) + tile * 39.7f;
                float[,] density = new float[tileSize, tileSize];
                float[,] radius = new float[tileSize, tileSize];

                // First pass builds one continuous projected density body. Strong fBm changes its
                // silhouette and interior height without punching the transparent holes that made
                // the older trail disintegrate into beads.
                for (int y = 0; y < tileSize; y++)
                {
                    for (int x = 0; x < tileSize; x++)
                    {
                        float u = (x + 0.5f) / tileSize * 2f - 1f;
                        float v = (y + 0.5f) / tileSize * 2f - 1f;
                        float warpedRadius;
                        density[x, y] = EvaluateCloudDensity(u, v, seedX, seedY, out warpedRadius);
                        radius[x, y] = warpedRadius;
                    }
                }

                int tileX = (tile % tilesPerAxis) * tileSize;
                int tileY = (tile / tilesPerAxis) * tileSize;
                for (int y = 0; y < tileSize; y++)
                {
                    int ym = Mathf.Max(0, y - 1);
                    int yp = Mathf.Min(tileSize - 1, y + 1);
                    for (int x = 0; x < tileSize; x++)
                    {
                        int xm = Mathf.Max(0, x - 1);
                        int xp = Mathf.Min(tileSize - 1, x + 1);
                        float centre = density[x, y];
                        float left = density[xm, y];
                        float right = density[xp, y];
                        float down = density[x, ym];
                        float up = density[x, yp];

                        // Treat projected density as a height field. Its gradient supplies stable
                        // directional lighting and curvature darkens folds, producing visible
                        // cauliflower relief even with KSP's unlit fallback particle shader.
                        float dx = (right - left) * tileSize * 0.34f;
                        float dy = (up - down) * tileSize * 0.34f;
                        Vector3 normal = new Vector3(-dx, -dy, 1f).normalized;
                        float diffuse = Mathf.Max(0f, Vector3.Dot(normal, lightDirection));
                        float curvature = (centre * 4f - left - right - down - up) * tileSize * 0.45f;
                        float body = Mathf.Clamp01(centre / 1.15f);
                        float shade = Mathf.Clamp(
                            0.56f + diffuse * 0.39f + curvature * 0.10f + body * 0.07f,
                            0.48f,
                            1.00f);

                        float alpha = 1f - Mathf.Exp(-centre * 2.85f);
                        alpha *= 1f - Mathf.SmoothStep(0.88f, 1.06f, radius[x, y]);
                        texture.SetPixel(
                            tileX + x,
                            tileY + y,
                            new Color(shade, shade * 0.985f, shade * 0.95f, alpha));
                    }
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private static float EvaluateCloudDensity(
            float u,
            float v,
            float seedX,
            float seedY,
            out float warpedRadius)
        {
            float rawRadius = Mathf.Sqrt(u * u + v * v);
            float warpX = Mathf.PerlinNoise(seedX + u * 1.25f, seedY + v * 1.25f) - 0.5f;
            float warpY = Mathf.PerlinNoise(seedY + u * 1.25f, seedX - v * 1.25f) - 0.5f;
            float wu = u + warpX * 0.24f;
            float wv = v + warpY * 0.24f;

            float n1 = Mathf.PerlinNoise(seedX + wu * 1.75f, seedY + wv * 1.75f);
            float n2 = Mathf.PerlinNoise(seedX * 0.37f + wu * 4.6f, seedY * 0.37f + wv * 4.6f);
            float n3 = Mathf.PerlinNoise(seedX * 0.13f + wu * 10.8f, seedY * 0.13f + wv * 10.8f);
            float shapeNoise = Mathf.Clamp01(n1 * 0.58f + n2 * 0.30f + n3 * 0.12f);
            float detailNoise = Mathf.PerlinNoise(seedY * 0.23f + wu * 18.7f, seedX * 0.23f + wv * 18.7f);

            float boundaryWeight = Mathf.SmoothStep(0.18f, 1f, rawRadius);
            float billowOffset = (shapeNoise - 0.5f) * 0.38f * boundaryWeight;
            warpedRadius = Mathf.Max(0f, rawRadius + billowOffset);
            float sphereDepth = Mathf.Sqrt(Mathf.Clamp01(1f - warpedRadius * warpedRadius));
            float interiorDetail = Mathf.Lerp(0.58f, 1.28f, detailNoise);
            float macroDensity = Mathf.Lerp(0.82f, 1.20f, shapeNoise);
            return sphereDepth * interiorDetail * macroDensity;
        }

        public void Dispose()
        {
            if (_waterfallVolumes != null)
                _waterfallVolumes.Dispose();

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
