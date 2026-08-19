using System;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Draws active Shuriken particles as instanced proxy cubes for the custom volumetric shader.
    /// The ParticleSystem remains the single source of truth for lifetime/motion/time-warp physics;
    /// this class only replaces the visual representation when the raymarch shader is available.
    /// </summary>
    internal sealed class RaymarchedSmokeRenderer : IDisposable
    {
        private const int BatchSize = 1023;

        private readonly SmokeSettings _settings;
        private readonly ParticleSystem _system;
        private readonly Material _material;
        private readonly Mesh _proxyCube;
        private readonly ParticleSystem.Particle[] _particles;
        private readonly Matrix4x4[] _matrices = new Matrix4x4[BatchSize];
        private readonly Vector4[] _colors = new Vector4[BatchSize];
        private readonly Vector4[] _params = new Vector4[BatchSize];
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

        public bool Active { get { return _material != null && _proxyCube != null; } }

        public RaymarchedSmokeRenderer(
            ParticleSystem system,
            Material material,
            SmokeSettings settings)
        {
            _system = system;
            _material = material;
            _settings = settings;
            _proxyCube = VolumetricCloudletMesh.CreateProxyCube();
            _particles = new ParticleSystem.Particle[Mathf.Max(1, settings.MaxParticles)];

            if (_material != null)
                _material.enableInstancing = true;
        }

        public void Render(Camera camera)
        {
            if (!Active || _system == null || camera == null)
                return;

            int count = _system.GetParticles(_particles);
            if (count <= 0)
                return;

            int limit = Mathf.Min(count, Mathf.Max(1, _settings.RaymarchMaxCloudlets));
            int stride = count > limit ? Mathf.CeilToInt(count / (float)limit) : 1;
            int batchCount = 0;
            int rendered = 0;

            for (int i = 0; i < count && rendered < limit; i += stride)
            {
                ParticleSystem.Particle p = _particles[i];
                if (p.remainingLifetime <= 0f || p.startLifetime <= 0f)
                    continue;

                float age = Mathf.Clamp01(1f - p.remainingLifetime / p.startLifetime);
                float size = p.startSize * EvaluateSizeGrowth(age);
                if (size <= 0.05f)
                    continue;

                Color color = p.startColor;
                float fade = EvaluateAlpha(age);
                color.a *= fade;
                if (color.a <= 0.002f)
                    continue;

                // A small deterministic anisotropy breaks perfect spheres while retaining a stable
                // world-space shape through time warp and camera motion.
                float seed = HashToUnit(p.randomSeed);
                float sx = 0.90f + seed * 0.22f;
                float sy = 0.92f + Mathf.Repeat(seed * 7.13f, 1f) * 0.20f;
                float sz = 0.90f + Mathf.Repeat(seed * 13.91f, 1f) * 0.24f;

                _matrices[batchCount] = Matrix4x4.TRS(
                    p.position,
                    Quaternion.identity,
                    new Vector3(size * sx, size * sy, size * sz));
                _colors[batchCount] = new Vector4(color.r, color.g, color.b, color.a);
                _params[batchCount] = new Vector4(age, seed, size, 0f);

                batchCount++;
                rendered++;

                if (batchCount >= BatchSize)
                {
                    DrawBatch(batchCount, camera);
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
                DrawBatch(batchCount, camera);
        }

        private void DrawBatch(int count, Camera camera)
        {
            _properties.Clear();
            _properties.SetVectorArray("_SmokeColor", _colors);
            _properties.SetVectorArray("_SmokeParams", _params);

            // The simple overload is intentionally used for KSP/Unity API compatibility. The
            // material is transparent and has shadows disabled in the shader itself.
            Graphics.DrawMeshInstanced(
                _proxyCube,
                0,
                _material,
                _matrices,
                count,
                _properties);
        }

        private float EvaluateSizeGrowth(float age)
        {
            float g = Mathf.Max(1f, _settings.SizeGrowth);
            if (age <= 0.04f)
                return Mathf.Lerp(1.00f, 1.55f, Smooth01(age / 0.04f));
            if (age <= 0.10f)
                return Mathf.Lerp(1.55f, Mathf.Min(g, 3.4f), Smooth01((age - 0.04f) / 0.06f));
            if (age <= 0.22f)
                return Mathf.Lerp(Mathf.Min(g, 3.4f), Mathf.Min(g, 7.0f), Smooth01((age - 0.10f) / 0.12f));
            if (age <= 0.45f)
                return Mathf.Lerp(Mathf.Min(g, 7.0f), Mathf.Min(g, 12.0f), Smooth01((age - 0.22f) / 0.23f));
            return Mathf.Lerp(Mathf.Min(g, 12.0f), g, Smooth01((age - 0.45f) / 0.55f));
        }

        private static float EvaluateAlpha(float age)
        {
            if (age <= 0.08f)
                return Mathf.Lerp(0.98f, 0.90f, Smooth01(age / 0.08f));
            if (age <= 0.30f)
                return Mathf.Lerp(0.90f, 0.74f, Smooth01((age - 0.08f) / 0.22f));
            if (age <= 0.72f)
                return Mathf.Lerp(0.74f, 0.48f, Smooth01((age - 0.30f) / 0.42f));
            return Mathf.Lerp(0.48f, 0f, Smooth01((age - 0.72f) / 0.28f));
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
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

        public void Dispose()
        {
            if (_proxyCube != null)
                UnityEngine.Object.Destroy(_proxyCube);
        }
    }
}
