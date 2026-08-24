using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// D3D11-only renderer backed by the project's own AssetBundle. It deliberately fails closed:
    /// an absent/invalid bundle never falls back to particles, Waterfall, EVE or billboard quads.
    /// </summary>
    internal sealed class VolumetricSmokeRenderer : IDisposable
    {
        private const string BundleFileName = "VolumetricSmoke-WindowsD3D11.bundle";
        private const int GpuSegmentFloat4Count = 8;
        private readonly SmokeSettings _settings;
        // Unity forwards AssetBundle to UnityEngine.AssetBundleModule. KSP skeleton references do
        // not distribute that module, so keep this runtime-only dependency reflective while still
        // loading the exact named bundle on a real KSP installation.
        private readonly object _bundle;
        private readonly Shader _raymarchShader;
        private readonly Shader _temporalShader;
        private readonly Shader _compositeShader;
        private readonly Shader _depthCopyShader;
        private readonly Shader _shadowShader;
        private readonly ComputeShader _tileCull;
        private readonly Texture3D _shapeNoise;
        private readonly Material _raymarchMaterial;
        private readonly Material _temporalMaterial;
        private readonly Material _compositeMaterial;
        private readonly Material _depthCopyMaterial;
        private readonly int _clearTilesKernel;
        private readonly int _cullSegmentsKernel;
        private readonly int _segmentCapacity;
        private readonly SegmentGpuData[] _gpuSegments;
        private ComputeBuffer _segmentBuffer;
        private ComputeBuffer _tileCountBuffer;
        private ComputeBuffer _tileIndexBuffer;
        private Camera _camera;
        private CommandBuffer _commandBuffer;
        private RenderTexture _history;
        private RenderTexture _historyDepth;
        private int _historyWidth;
        private int _historyHeight;
        private int _tileCount;
        private int _segmentCount;
        private bool _historyValid;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private int _lastFrame = -1;
        private bool _disposed;

        private readonly int _sceneCopyId = Shader.PropertyToID("_PersistentSrbSmokeSceneCopy");
        private readonly int _volumeId = Shader.PropertyToID("_PersistentSrbSmokeVolume");
        private readonly int _temporalId = Shader.PropertyToID("_PersistentSrbSmokeTemporal");

        private VolumetricSmokeRenderer(
            SmokeSettings settings,
            object bundle,
            Shader raymarchShader,
            Shader temporalShader,
            Shader compositeShader,
            Shader depthCopyShader,
            Shader shadowShader,
            ComputeShader tileCull,
            Texture3D shapeNoise)
        {
            _settings = settings;
            _bundle = bundle;
            _raymarchShader = raymarchShader;
            _temporalShader = temporalShader;
            _compositeShader = compositeShader;
            _depthCopyShader = depthCopyShader;
            _shadowShader = shadowShader;
            _tileCull = tileCull;
            _shapeNoise = shapeNoise;
            _raymarchMaterial = new Material(_raymarchShader) { hideFlags = HideFlags.HideAndDontSave };
            _temporalMaterial = new Material(_temporalShader) { hideFlags = HideFlags.HideAndDontSave };
            _compositeMaterial = new Material(_compositeShader) { hideFlags = HideFlags.HideAndDontSave };
            _depthCopyMaterial = new Material(_depthCopyShader) { hideFlags = HideFlags.HideAndDontSave };
            _clearTilesKernel = _tileCull.FindKernel("ClearTiles");
            _cullSegmentsKernel = _tileCull.FindKernel("CullSegments");
            _segmentCapacity = settings.MaxVisibleSegments + settings.PadTileCount;
            _gpuSegments = new SegmentGpuData[_segmentCapacity];
            _segmentBuffer = new ComputeBuffer(_segmentCapacity, sizeof(float) * 4 * GpuSegmentFloat4Count);
            ConfigureStaticShaderProperties();
        }

        public bool IsVisible
        {
            get { return _segmentCount > 0 && _camera != null && _camera.enabled; }
        }

        public static VolumetricSmokeRenderer TryCreate(SmokeSettings settings, out string failure)
        {
            failure = null;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                failure = "Volumetric SRB smoke requires Windows/D3D11; active device is "
                    + SystemInfo.graphicsDeviceType + ". The effect is disabled (no legacy fallback).";
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }
            if (!SystemInfo.supportsComputeShaders)
            {
                failure = "D3D11 compute shaders are unavailable. The volumetric effect is disabled (no legacy fallback).";
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }

            string bundlePath = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData/PersistentSRBSmoke/PluginData/" + BundleFileName);
            if (!File.Exists(bundlePath))
            {
                failure = "Missing " + BundleFileName + " at " + bundlePath
                    + ". The volumetric effect is disabled (no legacy fallback).";
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }

            object bundle = null;
            try
            {
                bundle = LoadBundle(bundlePath);
                if (bundle == null)
                    throw new InvalidOperationException("AssetBundle.LoadFromFile returned null");

                Shader raymarch = LoadBundleAsset<Shader>(bundle, "VolumetricSmokeRaymarch");
                Shader temporal = LoadBundleAsset<Shader>(bundle, "VolumetricSmokeTemporal");
                Shader composite = LoadBundleAsset<Shader>(bundle, "VolumetricSmokeComposite");
                Shader depthCopy = LoadBundleAsset<Shader>(bundle, "VolumetricSmokeDepthCopy");
                Shader shadow = LoadBundleAsset<Shader>(bundle, "VolumetricSmokeShadow");
                ComputeShader cull = LoadBundleAsset<ComputeShader>(bundle, "VolumetricSmokeTileCull");
                Texture3D noise = LoadBundleAsset<Texture3D>(bundle, "VolumetricSmokeShapeNoise");
                if (raymarch == null || temporal == null || composite == null || depthCopy == null
                    || shadow == null || cull == null || noise == null)
                    throw new InvalidOperationException("bundle is missing one or more required shader assets");

                VolumetricSmokeRenderer renderer = new VolumetricSmokeRenderer(
                    settings, bundle, raymarch, temporal, composite, depthCopy, shadow, cull, noise);
                Debug.Log("[PersistentSRBSmoke] Loaded standalone D3D11 volumetric bundle: " + bundlePath);
                return renderer;
            }
            catch (Exception ex)
            {
                UnloadBundle(bundle, true);
                failure = "Failed to initialize the D3D11 volumetric bundle: " + ex.Message
                    + ". The effect is disabled (no legacy fallback).";
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }
        }

        public void UploadSegments(IList<TrailSegment> segments)
        {
            if (_disposed)
                return;
            _segmentCount = Mathf.Min(_segmentCapacity, segments == null ? 0 : segments.Count);
            for (int i = 0; i < _segmentCount; i++)
                _gpuSegments[i] = ToGpuData(segments[i]);
            if (_segmentCount > 0)
                _segmentBuffer.SetData(_gpuSegments, 0, 0, _segmentCount);
        }

        public void LateUpdate()
        {
            if (_disposed || _lastFrame == Time.frameCount)
                return;
            _lastFrame = Time.frameCount;

            Camera camera = FindBestCamera();
            if (camera == null || !camera.enabled)
                return;
            EnsureCamera(camera);
            if (_segmentCount == 0)
                return;

            int width = Mathf.Max(1, (camera.pixelWidth + 1) / 2);
            int height = Mathf.Max(1, (camera.pixelHeight + 1) / 2);
            EnsureHistory(width, height);
            EnsureTileBuffers(camera.pixelWidth, camera.pixelHeight);
            DetectCameraCut(camera);
            ConfigureFrameShaderProperties(camera, width, height);
            BuildCommandBuffer(camera, width, height);
        }

        public Material CreateShadowMaterial()
        {
            if (_disposed || _shadowShader == null)
                return null;
            Material material = new Material(_shadowShader) { hideFlags = HideFlags.HideAndDontSave };
            // Per-quad alpha already contains the configurable integrated-density opacity.
            material.SetFloat("_ShadowOpacity", 1f);
            return material;
        }

        public void InvalidateHistory()
        {
            _historyValid = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DetachCommandBuffer();
            ReleaseBuffer(ref _segmentBuffer);
            ReleaseBuffer(ref _tileCountBuffer);
            ReleaseBuffer(ref _tileIndexBuffer);
            ReleaseRenderTexture(ref _history);
            ReleaseRenderTexture(ref _historyDepth);
            if (_raymarchMaterial != null)
                UnityEngine.Object.Destroy(_raymarchMaterial);
            if (_temporalMaterial != null)
                UnityEngine.Object.Destroy(_temporalMaterial);
            if (_compositeMaterial != null)
                UnityEngine.Object.Destroy(_compositeMaterial);
            if (_depthCopyMaterial != null)
                UnityEngine.Object.Destroy(_depthCopyMaterial);
            UnloadBundle(_bundle, false);
        }

        internal static Camera FindBestCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.enabled)
                return main;
            Camera[] cameras = Camera.allCameras;
            Camera best = null;
            float bestDepth = float.MinValue;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate == null || !candidate.enabled || candidate.orthographic)
                    continue;
                if (candidate.depth > bestDepth)
                {
                    best = candidate;
                    bestDepth = candidate.depth;
                }
            }
            return best;
        }

        private void ConfigureStaticShaderProperties()
        {
            _raymarchMaterial.SetTexture("_ShapeNoise", _shapeNoise);
            _raymarchMaterial.SetFloat("_Extinction", _settings.Extinction);
            _raymarchMaterial.SetFloat("_Scattering", _settings.Scattering);
            _raymarchMaterial.SetFloat("_AmbientLight", _settings.AmbientLight);
            _raymarchMaterial.SetFloat("_SunLight", _settings.SunLight);
            _raymarchMaterial.SetFloat("_NoiseScale", _settings.NoiseScale);
            _raymarchMaterial.SetFloat("_NoiseStrength", _settings.NoiseStrength);
            _raymarchMaterial.SetInt("_NearViewSamples", _settings.NearViewSamples);
            _raymarchMaterial.SetInt("_MidViewSamples", _settings.MidViewSamples);
            _raymarchMaterial.SetInt("_FarViewSamples", _settings.FarViewSamples);
            _raymarchMaterial.SetInt("_SunShadowSamples", _settings.SunShadowSamples);
            _temporalMaterial.SetFloat("_HistoryBlend", _settings.TemporalBlend);
            _temporalMaterial.SetFloat("_DepthThreshold", _settings.TemporalDepthThreshold);
        }

        private void EnsureCamera(Camera camera)
        {
            if (_camera == camera && _commandBuffer != null)
                return;
            DetachCommandBuffer();
            _camera = camera;
            _commandBuffer = new CommandBuffer { name = "PersistentSRBSmoke Volumetric Composite" };
            _camera.depthTextureMode |= DepthTextureMode.Depth;
            _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
            _historyValid = false;
        }

        private void EnsureHistory(int width, int height)
        {
            if (_history != null && _historyWidth == width && _historyHeight == height)
                return;
            ReleaseRenderTexture(ref _history);
            ReleaseRenderTexture(ref _historyDepth);
            _historyWidth = width;
            _historyHeight = height;
            _history = CreateHistoryTexture(width, height, RenderTextureFormat.ARGBHalf, "PersistentSRBSmoke History");
            _historyDepth = CreateHistoryTexture(width, height, RenderTextureFormat.RHalf, "PersistentSRBSmoke HistoryDepth");
            _historyValid = false;
        }

        private void EnsureTileBuffers(int screenWidth, int screenHeight)
        {
            int columns = Mathf.CeilToInt(screenWidth / (float)_settings.TileSize);
            int rows = Mathf.CeilToInt(screenHeight / (float)_settings.TileSize);
            int requestedTileCount = Mathf.Max(1, columns * rows);
            if (_tileCountBuffer != null && requestedTileCount == _tileCount)
                return;
            ReleaseBuffer(ref _tileCountBuffer);
            ReleaseBuffer(ref _tileIndexBuffer);
            _tileCount = requestedTileCount;
            _tileCountBuffer = new ComputeBuffer(_tileCount, sizeof(uint));
            _tileIndexBuffer = new ComputeBuffer(_tileCount * _settings.MaxTileCandidates, sizeof(uint));
        }

        private void DetectCameraCut(Camera camera)
        {
            if (!_historyValid)
            {
                _lastCameraPosition = camera.transform.position;
                _lastCameraRotation = camera.transform.rotation;
                return;
            }
            float positionJump = (camera.transform.position - _lastCameraPosition).sqrMagnitude;
            float rotationJump = Quaternion.Angle(camera.transform.rotation, _lastCameraRotation);
            // Floating Origin shifts and camera cuts both appear as a discontinuous camera pose.
            if (positionJump > 2500f || rotationJump > 20f)
                _historyValid = false;
            _lastCameraPosition = camera.transform.position;
            _lastCameraRotation = camera.transform.rotation;
        }

        private void ConfigureFrameShaderProperties(Camera camera, int width, int height)
        {
            Vector3 sunDirection;
            Color sunTint;
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            CelestialBody lightingBody = activeVessel == null ? null : activeVessel.mainBody;
            if (!SmokeLighting.TryGetSunDirection(lightingBody, out sunDirection))
                sunDirection = Vector3.up;
            float elevation = Vector3.Dot(camera.transform.up, sunDirection);
            sunTint = SmokeLighting.EvaluateSunTint(elevation, _settings.SunsetWarmth);
            _raymarchMaterial.SetBuffer("_SegmentData", _segmentBuffer);
            _raymarchMaterial.SetBuffer("_TileCounts", _tileCountBuffer);
            _raymarchMaterial.SetBuffer("_TileIndices", _tileIndexBuffer);
            _raymarchMaterial.SetInt("_SegmentCount", _segmentCount);
            _raymarchMaterial.SetInt("_TileSize", _settings.TileSize);
            _raymarchMaterial.SetInt("_MaxTileCandidates", _settings.MaxTileCandidates);
            _raymarchMaterial.SetInt("_TileColumns", Mathf.CeilToInt(camera.pixelWidth / (float)_settings.TileSize));
            _raymarchMaterial.SetInt("_TileRows", Mathf.CeilToInt(camera.pixelHeight / (float)_settings.TileSize));
            _raymarchMaterial.SetVector("_RaymarchResolution", new Vector4(width, height, 1f / width, 1f / height));
            _raymarchMaterial.SetVector("_SunDirection", new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            _raymarchMaterial.SetColor("_SunTint", sunTint);
            _temporalMaterial.SetTexture("_HistoryTex", _history);
            _temporalMaterial.SetTexture("_HistoryDepthTex", _historyDepth);
            _temporalMaterial.SetFloat("_HistoryValid", _historyValid && _settings.TemporalReconstruction ? 1f : 0f);
        }

        private void BuildCommandBuffer(Camera camera, int width, int height)
        {
            _commandBuffer.Clear();
            int screenWidth = Mathf.Max(1, camera.pixelWidth);
            int screenHeight = Mathf.Max(1, camera.pixelHeight);
            int tileColumns = Mathf.CeilToInt(screenWidth / (float)_settings.TileSize);
            int tileRows = Mathf.CeilToInt(screenHeight / (float)_settings.TileSize);

            _commandBuffer.SetComputeBufferParam(_tileCull, _clearTilesKernel, "_TileCounts", _tileCountBuffer);
            _commandBuffer.SetComputeBufferParam(_tileCull, _clearTilesKernel, "_TileIndices", _tileIndexBuffer);
            _commandBuffer.SetComputeIntParam(_tileCull, "_TileCount", _tileCount);
            _commandBuffer.SetComputeIntParam(_tileCull, "_MaxTileCandidates", _settings.MaxTileCandidates);
            _commandBuffer.DispatchCompute(_tileCull, _clearTilesKernel, Mathf.CeilToInt(_tileCount / 64f), 1, 1);
            _commandBuffer.SetComputeBufferParam(_tileCull, _cullSegmentsKernel, "_SegmentData", _segmentBuffer);
            _commandBuffer.SetComputeBufferParam(_tileCull, _cullSegmentsKernel, "_TileCounts", _tileCountBuffer);
            _commandBuffer.SetComputeBufferParam(_tileCull, _cullSegmentsKernel, "_TileIndices", _tileIndexBuffer);
            _commandBuffer.SetComputeIntParam(_tileCull, "_SegmentCount", _segmentCount);
            _commandBuffer.SetComputeIntParam(_tileCull, "_TileColumns", tileColumns);
            _commandBuffer.SetComputeIntParam(_tileCull, "_TileRows", tileRows);
            _commandBuffer.SetComputeIntParam(_tileCull, "_TileSize", _settings.TileSize);
            _commandBuffer.SetComputeMatrixParam(_tileCull, "_ViewProjection", camera.projectionMatrix * camera.worldToCameraMatrix);
            _commandBuffer.DispatchCompute(_tileCull, _cullSegmentsKernel, Mathf.CeilToInt(_segmentCount / 64f), 1, 1);

            _commandBuffer.GetTemporaryRT(_sceneCopyId, screenWidth, screenHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            _commandBuffer.GetTemporaryRT(_volumeId, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            _commandBuffer.GetTemporaryRT(_temporalId, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            _commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, _sceneCopyId);
            _commandBuffer.Blit(_sceneCopyId, _volumeId, _raymarchMaterial, 0);
            _commandBuffer.Blit(_volumeId, _temporalId, _temporalMaterial, 0);
            _commandBuffer.Blit(_temporalId, _history);
            _commandBuffer.Blit(_sceneCopyId, _historyDepth, _depthCopyMaterial, 0);
            _commandBuffer.SetGlobalTexture("_VolumeTex", _temporalId);
            _commandBuffer.Blit(_sceneCopyId, BuiltinRenderTextureType.CameraTarget, _compositeMaterial, 0);
            _commandBuffer.ReleaseTemporaryRT(_sceneCopyId);
            _commandBuffer.ReleaseTemporaryRT(_volumeId);
            _commandBuffer.ReleaseTemporaryRT(_temporalId);
            _historyValid = true;
        }

        private static SegmentGpuData ToGpuData(TrailSegment segment)
        {
            Vector3 start = segment.GetWorldStart();
            Vector3 end = segment.GetWorldEnd();
            Vector3 center = (start + end) * 0.5f;
            float length = (end - start).magnitude;
            float boundsRadius = segment.Radius + length * 0.5f;
            return new SegmentGpuData
            {
                StartRadius = new Vector4(start.x, start.y, start.z, segment.StartRadius),
                EndRadius = new Vector4(end.x, end.y, end.z, segment.EndRadius),
                StartTangentMass = new Vector4(segment.StartTangent.x, segment.StartTangent.y, segment.StartTangent.z, segment.OpticalMass),
                EndTangentTemperature = new Vector4(segment.EndTangent.x, segment.EndTangent.y, segment.EndTangent.z, segment.Temperature),
                VelocityAge = new Vector4(segment.Velocity.x, segment.Velocity.y, segment.Velocity.z, segment.NormalizedAge),
                Color = new Vector4(segment.Color.r, segment.Color.g, segment.Color.b, segment.Color.a),
                Metadata = new Vector4((float)segment.Kind, segment.Seed & 0x00FFFFFFU, segment.Lifetime, segment.Age),
                Bounds = new Vector4(center.x, center.y, center.z, boundsRadius)
            };
        }

        private void DetachCommandBuffer()
        {
            if (_camera != null && _commandBuffer != null)
                _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
            if (_commandBuffer != null)
                _commandBuffer.Release();
            _commandBuffer = null;
            _camera = null;
        }

        private static RenderTexture CreateHistoryTexture(int width, int height, RenderTextureFormat format, string name)
        {
            RenderTexture texture = new RenderTexture(width, height, 0, format)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
                return;
            buffer.Release();
            buffer = null;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private static object LoadBundle(string path)
        {
            Type type = FindAssetBundleType();
            if (type == null)
                throw new InvalidOperationException("UnityEngine.AssetBundleModule is not loaded by this KSP build.");
            MethodInfo load = type.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string) }, null);
            if (load == null)
                throw new MissingMethodException(type.FullName, "LoadFromFile(string)");
            return load.Invoke(null, new object[] { path });
        }

        private static T LoadBundleAsset<T>(object bundle, string address) where T : UnityEngine.Object
        {
            if (bundle == null)
                return null;
            MethodInfo load = bundle.GetType().GetMethod("LoadAsset", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(Type) }, null);
            if (load == null)
                throw new MissingMethodException(bundle.GetType().FullName, "LoadAsset(string, Type)");
            return load.Invoke(bundle, new object[] { address, typeof(T) }) as T;
        }

        private static void UnloadBundle(object bundle, bool unloadAllLoadedObjects)
        {
            if (bundle == null)
                return;
            try
            {
                MethodInfo unload = bundle.GetType().GetMethod("Unload", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(bool) }, null);
                if (unload != null)
                    unload.Invoke(bundle, new object[] { unloadAllLoadedObjects });
            }
            catch
            {
                // Shutdown must not turn a renderer cleanup failure into a KSP scene exception.
            }
        }

        private static Type FindAssetBundleType()
        {
            Type type = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule", false);
            if (type != null)
                return type;
            type = Type.GetType("UnityEngine.AssetBundle, UnityEngine", false);
            if (type != null)
                return type;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType("UnityEngine.AssetBundle", false);
                if (type != null)
                    return type;
            }
            return null;
        }
    }
}
