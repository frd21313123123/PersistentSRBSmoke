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

                // v0.4.2 deliberately renders the optical volume wider than the raw Shuriken size.
                // Real SRB exhaust rapidly becomes a broad turbulent cloud, not a pencil-thin tube.
                float size = p.startSize * EvaluateSizeGrowth(age) * 1.34f;
                if (size <= 0.05f)
                    continue;

                Color color = p.startColor;
                float fade = EvaluateAlpha(age);
                color.a *= fade;
                if (color.a <= 0.002f)
                    continue;

                float seed = HashToUnit(p.randomSeed);
                float sx = 1.02f + seed * 0.24f;
                float sy = 1.00f + Mathf.Repeat(seed * 7.13f, 1f) * 0.26f;
                float sz = 1.02f + Mathf.Repeat(seed * 13.91f, 1f) * 0.30f;

                // Random stable orientation matters because the raymarch density now contains
                // asymmetric macro-lobes. Without rotation every cloudlet would repeat the same shape.
                Quaternion rotation = Quaternion.Euler(
                    Mathf.Repeat(seed * 733f, 1f) * 360f,
                    Mathf.Repeat(seed * 1291f, 1f) * 360f,
                    Mathf.Repeat(seed * 2053f, 1f) * 360f);

                _matrices[batchCount] = Matrix4x4.TRS(
                    p.position,
                    rotation,
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
            if (age <= 0.03f)
                return Mathf.Lerp(1.00f, 1.72f, Smooth01(age / 0.03f));
            if (age <= 0.10f)
                return Mathf.Lerp(1.72f, Mathf.Min(g, 4.2f), Smooth01((age - 0.03f) / 0.07f));
            if (age <= 0.24f)
                return Mathf.Lerp(Mathf.Min(g, 4.2f), Mathf.Min(g, 8.8f), Smooth01((age - 0.10f) / 0.14f));
            if (age <= 0.50f)
                return Mathf.Lerp(Mathf.Min(g, 8.8f), Mathf.Min(g, 14.5f), Smooth01((age - 0.24f) / 0.26f));
            return Mathf.Lerp(Mathf.Min(g, 14.5f), g, Smooth01((age - 0.50f) / 0.50f));
        }

        private static float EvaluateAlpha(float age)
        {
            if (age <= 0.08f)
                return Mathf.Lerp(1.00f, 0.94f, Smooth01(age / 0.08f));
            if (age <= 0.32f)
                return Mathf.Lerp(0.94f, 0.80f, Smooth01((age - 0.08f) / 0.24f));
            if (age <= 0.74f)
                return Mathf.Lerp(0.80f, 0.52f, Smooth01((age - 0.32f) / 0.42f));
            return Mathf.Lerp(0.52f, 0f, Smooth01((age - 0.74f) / 0.26f));
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
