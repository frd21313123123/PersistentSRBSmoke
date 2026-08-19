using UnityEngine;

namespace PersistentSRBSmoke
{
    internal struct VolumetricLightingState
    {
        public Vector3 SunDirection;
        public Vector3 ViewDirection;
        public Vector3 UpDirection;

        public Color SunColor;
        public Color SkyAmbientColor;
        public Color GroundBounceColor;

        public float SunElevation;
        public float DayFactor;
        public float DirectTransmittance;
        public float ForwardPhase;
        public float BackwardPhase;
        public float CombinedPhase;
        public float MultipleScattering;
        public float AtmosphericFactor;
    }

    /// <summary>
    /// CPU-side approximation of the lighting terms normally evaluated by a volumetric cloud shader.
    /// The same state can be uploaded to a custom shader when one is available, while the stock-KSP
    /// fallback uses it to relight the procedural density texture and material tint.
    /// </summary>
    internal sealed class VolumetricLightingModel
    {
        private readonly SmokeSettings _settings;

        public VolumetricLightingModel(SmokeSettings settings)
        {
            _settings = settings;
        }

        public VolumetricLightingState Evaluate(
            CelestialBody body,
            Camera camera,
            Vector3 samplePosition,
            float atmosphericFactor)
        {
            Vector3 up = ResolveUp(body, samplePosition);
            Vector3 sunDirection = ResolveSunDirection(samplePosition, up);
            Vector3 viewDirection = ResolveViewDirection(camera, samplePosition, sunDirection);

            float sunElevation = Mathf.Clamp(Vector3.Dot(up, sunDirection), -1f, 1f);
            float dayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.28f, sunElevation));

            // Kasten-Young-like cheap air-mass approximation. It deliberately clamps close to the
            // horizon so a low Kerbol strongly warms/dims the direct term without numerical spikes.
            float positiveElevation = Mathf.Max(0f, sunElevation);
            float airMass = positiveElevation > 0.0001f
                ? 1f / Mathf.Max(0.075f, positiveElevation + 0.11f * (1f - positiveElevation))
                : 14f;

            float opticalDepth = Mathf.Lerp(0.08f, 0.72f, Mathf.Clamp01(atmosphericFactor));
            float directTransmittance = sunElevation <= -0.08f
                ? 0f
                : Mathf.Exp(-opticalDepth * airMass) * dayFactor;

            float horizonWarmth = 1f - Mathf.Clamp01((sunElevation + 0.02f) / 0.55f);
            Color sunColor = Color.Lerp(
                new Color(1.00f, 0.48f, 0.24f, 1f),
                new Color(1.00f, 0.97f, 0.91f, 1f),
                1f - horizonWarmth);

            Color skyAmbient = Color.Lerp(
                new Color(0.08f, 0.10f, 0.16f, 1f),
                new Color(0.48f, 0.64f, 0.88f, 1f),
                dayFactor);

            Color groundBounce = Color.Lerp(
                new Color(0.08f, 0.075f, 0.07f, 1f),
                new Color(0.46f, 0.42f, 0.34f, 1f),
                dayFactor);

            // Incoming light travels from the sun toward the smoke (-sunDirection). The outgoing
            // direction is from the smoke toward the camera. Forward scattering peaks when they align.
            float cosTheta = Mathf.Clamp(Vector3.Dot(-sunDirection, viewDirection), -1f, 1f);
            float forwardPhase = HenyeyGreensteinRelative(cosTheta, _settings.VolumetricScatteringForward);
            float backwardPhase = HenyeyGreensteinRelative(cosTheta, _settings.VolumetricScatteringBackward);
            float combinedPhase = Mathf.Clamp(forwardPhase * 0.82f + backwardPhase * 0.18f, 0.04f, 12f);

            float multipleScattering = 1f - Mathf.Exp(
                -Mathf.Max(0f, _settings.VolumetricMultipleScattering)
                * Mathf.Lerp(0.55f, 1.15f, Mathf.Clamp01(atmosphericFactor)));

            return new VolumetricLightingState
            {
                SunDirection = sunDirection,
                ViewDirection = viewDirection,
                UpDirection = up,
                SunColor = sunColor,
                SkyAmbientColor = skyAmbient,
                GroundBounceColor = groundBounce,
                SunElevation = sunElevation,
                DayFactor = dayFactor,
                DirectTransmittance = directTransmittance,
                ForwardPhase = forwardPhase,
                BackwardPhase = backwardPhase,
                CombinedPhase = combinedPhase,
                MultipleScattering = multipleScattering,
                AtmosphericFactor = Mathf.Clamp01(atmosphericFactor)
            };
        }

        public Color EvaluateFallbackTint(VolumetricLightingState state)
        {
            float phaseBoost = Mathf.Clamp(0.58f + 0.30f * state.CombinedPhase, 0.48f, 2.25f);
            float direct = _settings.VolumetricSunIntensity
                * state.DirectTransmittance
                * phaseBoost;

            float sky = _settings.VolumetricAmbientIntensity
                * Mathf.Lerp(0.30f, 1.00f, state.DayFactor);
            float ground = _settings.VolumetricAmbientIntensity
                * Mathf.Lerp(0.04f, 0.22f, state.DayFactor);

            Color result = Scale(state.SunColor, direct);
            result += Scale(state.SkyAmbientColor, sky);
            result += Scale(state.GroundBounceColor, ground);

            float internalFill = state.MultipleScattering
                * _settings.VolumetricMultipleScattering
                * Mathf.Lerp(0.12f, 0.42f, state.DayFactor);
            result += new Color(internalFill, internalFill, internalFill, 0f);

            result.r = Mathf.Clamp(result.r, 0.16f, 1.80f);
            result.g = Mathf.Clamp(result.g, 0.16f, 1.80f);
            result.b = Mathf.Clamp(result.b, 0.18f, 1.80f);
            result.a = 1f;
            return result;
        }

        public float EvaluateBeerPowder(float density, VolumetricLightingState state)
        {
            density = Mathf.Clamp01(density);
            float extinction = Mathf.Max(0.001f, _settings.VolumetricBeerPowderFactor);

            float beer = Mathf.Exp(-density * extinction * 2.15f);
            float powder = 1f - Mathf.Exp(-density * extinction * 4.5f);
            float phaseFill = Mathf.Clamp01(state.CombinedPhase * 0.14f);
            float multiple = state.MultipleScattering * _settings.VolumetricMultipleScattering;

            // Beer keeps dense interiors from glowing uniformly; powder and multiple scattering
            // restore enough light to avoid pitch-black cores and produce a cloud-like soft interior.
            float response = beer
                + powder * (0.28f + 0.28f * phaseFill)
                + multiple * powder * 0.32f;
            return Mathf.Clamp(response, 0.22f, 1.35f);
        }

        private static Vector3 ResolveUp(CelestialBody body, Vector3 samplePosition)
        {
            if (body != null && body.transform != null)
            {
                Vector3 radial = samplePosition - body.transform.position;
                if (radial.sqrMagnitude > 0.001f)
                    return radial.normalized;
            }

            return Vector3.up;
        }

        private static Vector3 ResolveSunDirection(Vector3 samplePosition, Vector3 fallback)
        {
            try
            {
                Planetarium planetarium = Planetarium.fetch;
                CelestialBody sun = planetarium == null ? null : planetarium.Sun;
                if (sun != null && sun.transform != null)
                {
                    Vector3 direction = sun.transform.position - samplePosition;
                    if (direction.sqrMagnitude > 0.001f)
                        return direction.normalized;
                }
            }
            catch
            {
                // Keep the renderer alive on unusual planet packs / scene transitions.
            }

            return fallback.sqrMagnitude > 0.001f ? fallback.normalized : Vector3.up;
        }

        private static Vector3 ResolveViewDirection(Camera camera, Vector3 samplePosition, Vector3 sunDirection)
        {
            if (camera != null && camera.transform != null)
            {
                Vector3 direction = camera.transform.position - samplePosition;
                if (direction.sqrMagnitude > 0.001f)
                    return direction.normalized;
            }

            return -sunDirection;
        }

        /// <summary>
        /// HG phase normalized relative to isotropic scattering (g=0 => 1), avoiding the 1/(4*pi)
        /// factor because the result is used as a relative brightness multiplier.
        /// </summary>
        internal static float HenyeyGreensteinRelative(float cosTheta, float g)
        {
            g = Mathf.Clamp(g, -0.95f, 0.95f);
            float gg = g * g;
            float denominator = Mathf.Pow(
                Mathf.Max(0.0001f, 1f + gg - 2f * g * Mathf.Clamp(cosTheta, -1f, 1f)),
                1.5f);
            return (1f - gg) / denominator;
        }

        private static Color Scale(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }
    }
}
