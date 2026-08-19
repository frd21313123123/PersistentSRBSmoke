using System;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Owns both rendering paths:
    /// 1) a dependency-free KSP particle material used by the native 3D slice-volume fallback;
    /// 2) an optional true raymarch material loaded as PersistentSRBSmoke/VolumetricSmoke.
    ///
    /// The fallback deliberately preserves the v0.3.x smoke albedo. v0.4.0 accidentally applied
    /// scene-light attenuation twice (material tint + texture relighting), which could drive the
    /// whole trail nearly black. Here lighting only adds modest local contrast; it never replaces
    /// the engine-specific base colour with a dark global multiplier.
    /// </summary>
    internal sealed class VolumetricSmokeShader : IDisposable
    {
        private const string CustomShaderName = "PersistentSRBSmoke/VolumetricSmoke";

        private readonly SmokeSettings _settings;
        private readonly Texture2D _texture;
        private readonly VolumetricLightingModel _lighting;
        private readonly Color32[] _basePixels;
        private readonly Color32[] _workingPixels;

        private float _nextTextureUpdate;
        private Vector3 _lastPseudoSun;
        private float _lastPhase = -100f;
        private bool _hasLastLighting;
        private Camera _lastDepthCamera;

        public Material Material { get; private set; }
        public Material RaymarchMaterial { get; private set; }
        public bool UsingCustomShader { get { return RaymarchMaterial != null; } }
        public bool SoftParticlesActive { get; private set; }

        public VolumetricSmokeShader(Texture2D texture, SmokeSettings settings)
        {
            if (texture == null)
                throw new ArgumentNullException("texture");

            _texture = texture;
            _settings = settings;
            _lighting = new VolumetricLightingModel(settings);
            _basePixels = texture.GetPixels32();
            _workingPixels = new Color32[_basePixels.Length];

            Shader fallback = Shader.Find("KSP/Particles/Alpha Blended");
            if (fallback == null) fallback = Shader.Find("Particles/Alpha Blended");
            if (fallback == null) fallback = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (fallback == null) fallback = Shader.Find("Unlit/Transparent");
            if (fallback == null)
                throw new InvalidOperationException("No compatible transparent smoke shader was found.");

            Material = new Material(fallback);
            Material.name = "PersistentSRBSmoke.SliceVolumeMaterial";
            Material.mainTexture = texture;

            ConfigureSoftParticles(Material);

            // KSP/Particles/Alpha Blended multiplies by 2, so 0.5 is neutral. Keep the fallback
            // around neutral rather than using lighting as a second darkening pass.
            if (Material.HasProperty("_TintColor"))
                Material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));

            if (_settings.RaymarchedVolumetricEnabled)
            {
                Shader custom = Shader.Find(CustomShaderName);
                if (custom != null)
                {
                    RaymarchMaterial = new Material(custom);
                    RaymarchMaterial.name = "PersistentSRBSmoke.RaymarchMaterial";
                    RaymarchMaterial.enableInstancing = true;
                }
            }

            Debug.Log(
                "[PersistentSRBSmoke] renderer fallback=" + fallback.name +
                " raymarch=" + (RaymarchMaterial != null) +
                " softParticles=" + SoftParticlesActive);
        }

        public void UpdateFrame(
            CelestialBody body,
            Camera camera,
            Vector3 samplePosition,
            float atmosphericFactor)
        {
            if (!_settings.VolumetricLightingEnabled)
                return;

            EnsureDepthTexture(camera);

            VolumetricLightingState state = _lighting.Evaluate(
                body,
                camera,
                samplePosition,
                atmosphericFactor);

            UpdateFallbackState(state, camera);
            if (RaymarchMaterial != null)
                UploadRaymarchState(state);
        }

        private void ConfigureSoftParticles(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty("_InvFade"))
            {
                float fadeDistance = Mathf.Max(0.05f, _settings.VolumetricSoftDepthFactor);
                material.SetFloat("_InvFade", 1f / fadeDistance);
                material.EnableKeyword("SOFTPARTICLES_ON");
                material.DisableKeyword("SOFTPARTICLES_OFF");
                SoftParticlesActive = true;
            }
        }

        private void EnsureDepthTexture(Camera camera)
        {
            if (!SoftParticlesActive && RaymarchMaterial == null)
                return;

            if (camera != null)
            {
                camera.depthTextureMode |= DepthTextureMode.Depth;
                _lastDepthCamera = camera;
                return;
            }

            if (_lastDepthCamera != null)
                _lastDepthCamera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void UploadRaymarchState(VolumetricLightingState state)
        {
            SetVectorIfPresent(RaymarchMaterial, "_SunDir", state.SunDirection);
            SetVectorIfPresent(RaymarchMaterial, "_PlanetUp", state.UpDirection);
            SetColorIfPresent(RaymarchMaterial, "_SunColor", state.SunColor);
            SetColorIfPresent(RaymarchMaterial, "_SkyAmbientColor", state.SkyAmbientColor);
            SetColorIfPresent(RaymarchMaterial, "_GroundBounceColor", state.GroundBounceColor);

            SetFloatIfPresent(RaymarchMaterial, "_SunTransmittance", state.DirectTransmittance);
            SetFloatIfPresent(RaymarchMaterial, "_SunIntensity", _settings.VolumetricSunIntensity);
            SetFloatIfPresent(RaymarchMaterial, "_AmbientIntensity", _settings.VolumetricAmbientIntensity);
            SetFloatIfPresent(RaymarchMaterial, "_PhaseGForward", _settings.VolumetricScatteringForward);
            SetFloatIfPresent(RaymarchMaterial, "_PhaseGBackward", _settings.VolumetricScatteringBackward);
            SetFloatIfPresent(RaymarchMaterial, "_MultipleScattering", _settings.VolumetricMultipleScattering);
            SetFloatIfPresent(RaymarchMaterial, "_BeerPowder", _settings.VolumetricBeerPowderFactor);
            SetFloatIfPresent(RaymarchMaterial, "_SoftDepthFactor", _settings.VolumetricSoftDepthFactor);
            SetFloatIfPresent(RaymarchMaterial, "_RaySteps", _settings.RaymarchSteps);
            SetFloatIfPresent(RaymarchMaterial, "_ShadowSteps", _settings.RaymarchShadowSteps);
            SetFloatIfPresent(RaymarchMaterial, "_DensityMultiplier", _settings.RaymarchDensityMultiplier);
            SetFloatIfPresent(RaymarchMaterial, "_Extinction", _settings.RaymarchExtinction);
        }

        private void UpdateFallbackState(VolumetricLightingState state, Camera camera)
        {
            if (Material == null)
                return;

            Color lightTint = _lighting.EvaluateFallbackTint(state);
            float luminance = lightTint.r * 0.2126f + lightTint.g * 0.7152f + lightTint.b * 0.0722f;
            Color chroma = NormalizeColor(lightTint);

            // Neutral stock-particle multiplier is 0.5. Limit lighting modulation to roughly +/-12%
            // so the particle's own realistic grey/tan albedo stays dominant.
            float brightness = Mathf.Clamp(0.96f + (luminance - 0.70f) * 0.10f, 0.88f, 1.12f);
            Color tint = new Color(
                0.5f * brightness * Mathf.Lerp(1f, chroma.r, 0.10f),
                0.5f * brightness * Mathf.Lerp(1f, chroma.g, 0.10f),
                0.5f * brightness * Mathf.Lerp(1f, chroma.b, 0.10f),
                0.5f);

            if (Material.HasProperty("_TintColor"))
                Material.SetColor("_TintColor", tint);

            if (camera == null || camera.transform == null)
                return;

            Vector3 viewFacing = -camera.transform.forward;
            Vector3 pseudoSun = new Vector3(
                Vector3.Dot(state.SunDirection, camera.transform.right),
                Vector3.Dot(state.SunDirection, camera.transform.up),
                Vector3.Dot(state.SunDirection, viewFacing));
            if (pseudoSun.sqrMagnitude < 0.001f)
                pseudoSun = Vector3.forward;
            else
                pseudoSun.Normalize();

            float now = Time.realtimeSinceStartup;
            float directionChange = _hasLastLighting
                ? Vector3.Angle(_lastPseudoSun, pseudoSun)
                : 180f;
            float phaseChange = Mathf.Abs(state.CombinedPhase - _lastPhase);
            bool majorChange = directionChange > 8f || phaseChange > 0.35f;
            if (!majorChange && now < _nextTextureUpdate)
                return;

            RelightProceduralTexture(state, pseudoSun, lightTint);
            _lastPseudoSun = pseudoSun;
            _lastPhase = state.CombinedPhase;
            _hasLastLighting = true;
            _nextTextureUpdate = now + 0.10f;
        }

        private void RelightProceduralTexture(
            VolumetricLightingState state,
            Vector3 pseudoSun,
            Color sceneTint)
        {
            int width = _texture.width;
            int height = _texture.height;
            if (width <= 0 || height <= 0 || _basePixels.Length != width * height)
                return;

            float phaseSilver = Mathf.Clamp01((state.ForwardPhase - 1f) * 0.10f);
            float sceneLum = sceneTint.r * 0.2126f + sceneTint.g * 0.7152f + sceneTint.b * 0.0722f;
            Color sceneChroma = sceneLum > 0.001f
                ? new Color(sceneTint.r / sceneLum, sceneTint.g / sceneLum, sceneTint.b / sceneLum, 1f)
                : Color.white;

            for (int y = 0; y < height; y++)
            {
                float v = ((y + 0.5f) / height) * 2f - 1f;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 source = _basePixels[index];
                    float density = source.a / 255f;
                    float u = ((x + 0.5f) / width) * 2f - 1f;
                    float r2 = u * u + v * v;
                    float z = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(1f, r2)));
                    Vector3 pseudoNormal = new Vector3(u, v, z).normalized;

                    float ndotl = Mathf.Clamp01(Vector3.Dot(pseudoNormal, pseudoSun));
                    float radius = Mathf.Sqrt(Mathf.Clamp01(r2));
                    float edge = Mathf.SmoothStep(0.42f, 0.96f, radius);
                    float silver = phaseSilver * edge * Mathf.Pow(ndotl, 0.55f);

                    // Keep the entire fallback texture in a high-albedo range. Density only adds
                    // mild core shadowing, while direct light/silver lining restores highlights.
                    float coreShadow = density * _settings.FallbackCoreShadow;
                    float direct = state.DirectTransmittance
                        * _settings.VolumetricSunIntensity
                        * (0.12f + 0.18f * ndotl);
                    float localLight = 0.93f - coreShadow + direct + silver * 0.22f;
                    localLight += state.MultipleScattering * density * 0.06f;
                    localLight = Mathf.Clamp(localLight, _settings.FallbackMinimumLight, 1.12f);

                    float red = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.r, 0.08f));
                    float green = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.g, 0.08f));
                    float blue = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.b, 0.08f));

                    _workingPixels[index] = new Color32(
                        (byte)Mathf.RoundToInt(red * 255f),
                        (byte)Mathf.RoundToInt(green * 255f),
                        (byte)Mathf.RoundToInt(blue * 255f),
                        source.a);
                }
            }

            _texture.SetPixels32(_workingPixels);
            _texture.Apply(false, false);
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void SetVectorIfPresent(Material material, string property, Vector3 value)
        {
            if (material != null && material.HasProperty(property))
                material.SetVector(property, new Vector4(value.x, value.y, value.z, 0f));
        }

        private static void SetColorIfPresent(Material material, string property, Color value)
        {
            if (material != null && material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static Color NormalizeColor(Color color)
        {
            float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
            if (luminance <= 0.001f)
                return Color.white;

            return new Color(
                Mathf.Clamp(color.r / luminance, 0.65f, 1.40f),
                Mathf.Clamp(color.g / luminance, 0.65f, 1.40f),
                Mathf.Clamp(color.b / luminance, 0.65f, 1.40f),
                1f);
        }

        public void Dispose()
        {
            if (RaymarchMaterial != null)
            {
                UnityEngine.Object.Destroy(RaymarchMaterial);
                RaymarchMaterial = null;
            }

            if (Material != null)
            {
                UnityEngine.Object.Destroy(Material);
                Material = null;
            }
        }
    }
}
