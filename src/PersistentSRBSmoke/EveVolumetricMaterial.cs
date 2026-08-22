using System;
using System.Reflection;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Optional adapter for the volumetric cloud-particle shader already loaded by EVE.
    /// No EVE binary or asset is linked or redistributed; reflection keeps EVE optional.
    /// </summary>
    internal static class EveVolumetricMaterial
    {
        private const string ShaderName = "EVE/GeometryCloudVolumeParticle";

        public static bool TryCreate(
            Texture2D smokeTexture,
            SmokeSettings settings,
            out Material material,
            out string status)
        {
            material = null;
            status = "disabled";

            if (!settings.PreferEveVolumetricShader)
                return false;

            try
            {
                // EVE's D3D11 variant writes cloud particles into a private off-screen target and
                // relies on DeferredRendererNotifier to register a MeshRenderer with its compositor.
                // A Shuriken ParticleSystemRenderer cannot be registered through that API: using
                // the forward material directly succeeds but produces a completely invisible trail.
                // Keep the integration for EVE's direct-render paths and use our visible procedural
                // fallback on the normal Windows/D3D11 KSP path.
                string graphicsDevice = SystemInfo.graphicsDeviceVersion ?? string.Empty;
                if (graphicsDevice.IndexOf("Direct3D 11", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    status = "EVE D3D11 compositor is incompatible with Shuriken";
                    return false;
                }

                Shader shader = FindShader();
                if (shader == null)
                {
                    status = "EVE shader not loaded";
                    return false;
                }

                material = new Material(shader);
                material.name = "PersistentSRBSmoke.EVEVolumeMaterial";

                // These two properties are the stable core of EVE's cloud-volume particle
                // material. Refuse an unexpected shader revision instead of showing pink or
                // opaque geometry and let the normal particle material take over. Unity 2019's
                // Shader API has no HasProperty, so query the temporary Material instead.
                if (!material.HasProperty("_Tex") || !material.HasProperty("_Opacity"))
                {
                    UnityEngine.Object.Destroy(material);
                    material = null;
                    status = "incompatible EVE shader revision";
                    return false;
                }

                SetTexture(material, "_Tex", smokeTexture);
                SetTexture(material, "_MainTex", smokeTexture);
                SetTexture(material, "_DetailTex", smokeTexture);

                // EVE's own cloud-volume defaults are used as the baseline, with density and
                // depth intersection exposed through this mod's settings.
                SetFloat(material, "_InvFade", settings.VolumetricSoftDepth);
                SetFloat(material, "_MinScatter", settings.VolumetricMinScatter);
                SetFloat(material, "_Opacity", settings.VolumetricDensity);
                SetFloat(material, "_QuadSize", 1f);
                SetFloat(material, "_MaxScale", 1f);
                SetFloat(material, "_DetailScale", 5.5f);
                SetFloat(material, "_DetailDist", 0.02f);
                SetFloat(material, "_DistFade", 1f);
                SetFloat(material, "_DistFadeVert", 0.000085f);
                SetFloat(material, "_UVNoiseScale", 0.015f);
                SetFloat(material, "_UVNoiseStrength", 0.003f);
                SetVector(material, "_MaxTrans", Vector4.zero);
                SetVector(material, "_NoiseScale", new Vector4(1.5f, 2.5f, 1.2f, 0f));
                SetColor(material, "_Color", Color.white * 255f);

                material.EnableKeyword("SOFT_DEPTH_ON");
                material.EnableKeyword("FLOWMAP_OFF");
                material.DisableKeyword("FLOWMAP_ON");
                material.EnableKeyword("NORMALMAP_OFF");
                material.DisableKeyword("NORMALMAP_ON");
                material.renderQueue = 3002;

                status = "EVE cloud-volume shader";
                return true;
            }
            catch (Exception ex)
            {
                if (material != null)
                    UnityEngine.Object.Destroy(material);
                material = null;
                status = "EVE adapter failed: " + ex.GetType().Name;
                return false;
            }
        }

        private static Shader FindShader()
        {
            // Shader.Find succeeds on some EVE versions after their AssetBundle is loaded.
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                return shader;

            // Other versions keep the shaders in their own registry. Avoid a hard assembly
            // reference so PersistentSRBSmoke remains fully usable without EVE.
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (!string.Equals(assembly.GetName().Name, "ShaderLoader", StringComparison.OrdinalIgnoreCase))
                    continue;

                Type loader = assembly.GetType("ShaderLoader.ShaderLoaderClass", false);
                if (loader == null)
                    continue;

                MethodInfo find = loader.GetMethod(
                    "FindShader",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                if (find == null)
                    continue;

                return find.Invoke(null, new object[] { ShaderName }) as Shader;
            }

            return null;
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void SetVector(Material material, string property, Vector4 value)
        {
            if (material.HasProperty(property))
                material.SetVector(property, value);
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
                material.SetTexture(property, texture);
        }
    }
}
