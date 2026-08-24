using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PersistentSRBSmoke.Assets.Editor
{
    /// <summary>Batch-mode entry point used by CI and scripts/build-volumetric-assets.ps1.</summary>
    public static class BuildVolumetricSmokeBundle
    {
        private const string AssetRoot = "Assets/VolumetricSmoke/";
        private const string NoisePath = AssetRoot + "Generated/VolumetricSmokeShapeNoise.asset";

        public static void BuildWindowsD3D11()
        {
            EnsureNoiseAsset();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string unityDirectory = Directory.GetParent(projectRoot).FullName;
            string repositoryRoot = Directory.GetParent(unityDirectory).FullName;
            string output = Environment.GetEnvironmentVariable("PERSISTENT_SRB_SMOKE_BUNDLE_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                output = Path.Combine(repositoryRoot, "GameData", "PersistentSRBSmoke", "PluginData");
            }
            Directory.CreateDirectory(output);

            AssetBundleBuild build = new AssetBundleBuild
            {
                assetBundleName = "VolumetricSmoke-WindowsD3D11.bundle",
                assetNames = new[]
                {
                    AssetRoot + "Shaders/VolumetricSmokeRaymarch.shader",
                    AssetRoot + "Shaders/VolumetricSmokeTemporal.shader",
                    AssetRoot + "Shaders/VolumetricSmokeComposite.shader",
                    AssetRoot + "Shaders/VolumetricSmokeDepthCopy.shader",
                    AssetRoot + "Shaders/VolumetricSmokeShadow.shader",
                    AssetRoot + "Shaders/VolumetricSmokeTileCull.compute",
                    NoisePath
                },
                addressableNames = new[]
                {
                    "VolumetricSmokeRaymarch",
                    "VolumetricSmokeTemporal",
                    "VolumetricSmokeComposite",
                    "VolumetricSmokeDepthCopy",
                    "VolumetricSmokeShadow",
                    "VolumetricSmokeTileCull",
                    "VolumetricSmokeShapeNoise"
                }
            };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                output,
                new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle
                    | BuildAssetBundleOptions.StrictMode
                    | BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("Unity did not produce the volumetric smoke AssetBundle.");

            string bundle = Path.Combine(output, build.assetBundleName);
            if (!File.Exists(bundle))
                throw new FileNotFoundException("Expected AssetBundle was not produced.", bundle);
            ValidateBundle(bundle, build.addressableNames);
            Debug.Log("[PersistentSRBSmoke] Built " + bundle);
        }

        private static void ValidateBundle(string bundlePath, string[] addresses)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
                throw new InvalidOperationException("The produced AssetBundle cannot be loaded: " + bundlePath);
            try
            {
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (bundle.LoadAsset(addresses[i]) == null)
                        throw new InvalidOperationException("The produced AssetBundle is missing " + addresses[i]);
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void EnsureNoiseAsset()
        {
            Texture3D existing = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
            if (existing != null)
                return;

            string directory = Path.GetDirectoryName(NoisePath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            const int size = 32;
            Color[] pixels = new Color[size * size * size];
            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float nx = x / (float)size;
                        float ny = y / (float)size;
                        float nz = z / (float)size;
                        float a = Mathf.PerlinNoise(nx * 5.13f + nz * 1.91f, ny * 5.73f + nx * 0.77f);
                        float b = Mathf.PerlinNoise(nx * 11.29f + ny * 2.17f, nz * 9.41f + ny * 1.39f);
                        float c = Mathf.PerlinNoise(nz * 17.11f + nx * 3.71f, ny * 15.61f + nz * 0.59f);
                        float value = Mathf.Clamp01(a * 0.55f + b * 0.30f + c * 0.15f);
                        pixels[x + size * (y + size * z)] = new Color(value, value, value, value);
                    }
                }
            }

            Texture3D noise = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            {
                name = "VolumetricSmokeShapeNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0
            };
            noise.SetPixels(pixels);
            noise.Apply(false, true);
            AssetDatabase.CreateAsset(noise, NoisePath);
            AssetDatabase.SaveAssets();
        }
    }
}
