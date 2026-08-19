using System;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Owns the smoke material and bridges the lighting model to rendering.
    ///
    /// Preferred path: if a shader named PersistentSRBSmoke/VolumetricSmoke is loaded by the KSP
    /// environment (for example from a future Shabby asset bundle), all volumetric parameters are
    /// uploaded directly.
    ///
    /// Default path: KSP's stock particle shader is used with SOFTPARTICLES_ON. The CPU relights the
    /// procedural density texture using spherical pseudo-normals, dual-lobe HG phase response,
    /// Beer-Powder attenuation and an edge silver-lining term. This provides a robust volumetric-like
    /// appearance without making an external shader-loader a hard dependency.
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
        public bool UsingCustomShader { get; private set; }
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

            Shader shader = Shader.Find(CustomShaderName);
            UsingCustomShader = shader != null;

            if (shader == null) shader = Shader.Find("KSP/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");

            if (shader == null)
                throw new InvalidOperationException("No compatible transparent smoke shader was found.");

            Material = new Material(shader);
            Material.name = "PersistentSRBSmoke.VolumetricMaterial";
            Material.mainTexture = texture;

            ConfigureSoftParticles();

            // KSP/Particles/Alpha Blended multiplies the tint by 2. A neutral 0.5 tint therefore
            // preserves the authored particle colour instead of doubling it.
            if (Material.HasProperty("_TintColor"))
                Material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));

            Debug.Log(
                "[PersistentSRBSmoke] Volumetric renderer: shader=" + shader.name +
                " custom=" + UsingCustomShader +
                " softParticles=" + SoftParticlesActive);
        }

        public void UpdateFrame(
            CelestialBody body,
            Camera camera,
            Vector3 samplePosition,
            float atmosphericFactor)
        {
            if (Material == null || !_settings.VolumetricLightingEnabled)
                return;

            EnsureDepthTexture(camera);

            VolumetricLightingState state = _lighting.Evaluate(
                body,
                camera,
                samplePosition,
                atmosphericFactor);

            if (UsingCustomShader)
                UploadCustomShaderState(state);
            else
                UpdateFallbackState(state, camera);
        }

        private void ConfigureSoftParticles()
        {
            if (Material == null)
                return;

            if (Material.HasProperty("_InvFade"))
            {
                float fadeDistance = Mathf.Max(0.05f, _settings.VolumetricSoftDepthFactor);
                Material.SetFloat("_InvFade", 1f / fadeDistance);
                Material.EnableKeyword("SOFTPARTICLES_ON");
                Material.DisableKeyword("SOFTPARTICLES_OFF");
                SoftParticlesActive = true;
            }
        }

        private void EnsureDepthTexture(Camera camera)
        {
            if (!SoftParticlesActive)
                return;

            if (camera != null)
            {
                camera.depthTextureMode |= DepthTextureMode.Depth;
                _lastDepthCamera = camera;
                return;
            }

            // KSP can briefly switch cameras during map/flight transitions. Keep the last valid one
            // configured instead of globally forcing every UI/scaled-space camera to render depth.
            if (_lastDepthCamera != null)
                _lastDepthCamera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void UploadCustomShaderState(VolumetricLightingState state)
        {
            SetVectorIfPresent("_SunDir", state.SunDirection);
            SetVectorIfPresent("_ViewDir", state.ViewDirection);
            SetVectorIfPresent("_PlanetUp", state.UpDirection);

            SetColorIfPresent("_SunColor", state.SunColor);
            SetColorIfPresent("_SkyAmbientColor", state.SkyAmbientColor);
            SetColorIfPresent("_GroundBounceColor", state.GroundBounceColor);

            SetFloatIfPresent("_SunTransmittance", state.DirectTransmittance);
            SetFloatIfPresent("_SunIntensity", _settings.VolumetricSunIntensity);
            SetFloatIfPresent("_AmbientIntensity", _settings.VolumetricAmbientIntensity);
            SetFloatIfPresent("_PhaseGForward", _settings.VolumetricScatteringForward);
            SetFloatIfPresent("_PhaseGBackward", _settings.VolumetricScatteringBackward);
            SetFloatIfPresent("_ForwardPhase", state.ForwardPhase);
            SetFloatIfPresent("_BackwardPhase", state.BackwardPhase);
            SetFloatIfPresent("_CombinedPhase", state.CombinedPhase);
            SetFloatIfPresent("_MultipleScattering", _settings.VolumetricMultipleScattering);
            SetFloatIfPresent("_BeerPowder", _settings.VolumetricBeerPowderFactor);
            SetFloatIfPresent("_SoftDepthFactor", _settings.VolumetricSoftDepthFactor);
        }

        private void UpdateFallbackState(VolumetricLightingState state, Camera camera)
        {
            Color lightTint = _lighting.EvaluateFallbackTint(state);

            // Convert scene light into a conservative global particle multiplier. The dynamic
            // procedural texture supplies most of the local shading/silver lining.
            float luminance = lightTint.r * 0.2126f + lightTint.g * 0.7152f + lightTint.b * 0.0722f;
            float tintStrength = Mathf.Clamp(luminance, 0.42f, 1.62f);

            Color chroma = NormalizeColor(lightTint);
            Color tint = new Color(
                Mathf.Clamp(chroma.r * 0.5f * tintStrength, 0.18f, 0.92f),
                Mathf.Clamp(chroma.g * 0.5f * tintStrength, 0.18f, 0.92f),
                Mathf.Clamp(chroma.b * 0.5f * tintStrength, 0.18f, 0.92f),
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

            // 12.5 Hz maximum keeps the CPU fallback inexpensive. Immediate refresh is allowed for
            // large camera/sun changes so the lighting does not visibly lag when rotating the view.
            bool majorChange = directionChange > 8f || phaseChange > 0.35f;
            if (!majorChange && now < _nextTextureUpdate)
                return;

            RelightProceduralTexture(state, pseudoSun, lightTint);
            _lastPseudoSun = pseudoSun;
            _lastPhase = state.CombinedPhase;
            _hasLastLighting = true;
            _nextTextureUpdate = now + 0.08f;
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

            float phaseSilver = Mathf.Clamp01((state.ForwardPhase - 1.0f) * 0.12f);
            float phaseDirect = Mathf.Clamp(0.72f + state.CombinedPhase * 0.16f, 0.72f, 1.65f);
            float ambient = Mathf.Clamp(
                _settings.VolumetricAmbientIntensity * (0.42f + 0.58f * state.DayFactor),
                0.12f,
                1.25f);

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
                    Vector3 pseudoNormal = new Vector3(u, v, z);
                    if (pseudoNormal.sqrMagnitude > 0.001f)
                        pseudoNormal.Normalize();
                    else
                        pseudoNormal = Vector3.forward;

                    float ndotl = Mathf.Clamp01(Vector3.Dot(pseudoNormal, pseudoSun));
                    float diffuse = 0.34f + 0.66f * ndotl;

                    float radius = Mathf.Sqrt(Mathf.Clamp01(r2));
                    float edge = Mathf.SmoothStep(0.38f, 0.94f, radius);
                    float silver = phaseSilver
                        * edge
                        * Mathf.Pow(Mathf.Clamp01(ndotl + 0.15f), 0.65f)
                        * _settings.VolumetricSunIntensity;

                    float beerPowder = _lighting.EvaluateBeerPowder(density, state);
                    float direct = state.DirectTransmittance
                        * _settings.VolumetricSunIntensity
                        * phaseDirect
                        * diffuse;

                    float localLight = ambient
                        + direct
                        + silver * 0.90f
                        + state.MultipleScattering * density * 0.24f;
                    localLight *= beerPowder;

                    // Preserve coloured sunlight/sky in the texture while keeping the engine-specific
                    // particle colour as the dominant hue. This is deliberately subtle.
                    float sceneLum = sceneTint.r * 0.2126f + sceneTint.g * 0.7152f + sceneTint.b * 0.0722f;
                    Color sceneChroma = sceneLum > 0.001f
                        ? new Color(sceneTint.r / sceneLum, sceneTint.g / sceneLum, sceneTint.b / sceneLum, 1f)
                        : Color.white;

                    float red = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.r, 0.22f));
                    float green = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.g, 0.22f));
                    float blue = Mathf.Clamp01(localLight * Mathf.Lerp(1f, sceneChroma.b, 0.22f));

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

        private void SetFloatIfPresent(string property, float value)
        {
            if (Material.HasProperty(property))
                Material.SetFloat(property, value);
        }

        private void SetVectorIfPresent(string property, Vector3 value)
        {
            if (Material.HasProperty(property))
                Material.SetVector(property, new Vector4(value.x, value.y, value.z, 0f));
        }

        private void SetColorIfPresent(string property, Color value)
        {
            if (Material.HasProperty(property))
                Material.SetColor(property, value);
        }

        private static Color NormalizeColor(Color color)
        {
            float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
            if (luminance <= 0.001f)
                return Color.white;

            return new Color(
                Mathf.Clamp(color.r / luminance, 0.55f, 1.55f),
                Mathf.Clamp(color.g / luminance, 0.55f, 1.55f),
                Mathf.Clamp(color.b / luminance, 0.55f, 1.55f),
                1f);
        }

        public void Dispose()
        {
            if (Material != null)
            {
                UnityEngine.Object.Destroy(Material);
                Material = null;
            }
        }
    }
}
