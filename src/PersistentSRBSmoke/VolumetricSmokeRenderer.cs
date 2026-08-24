using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// A no-AssetBundle presentation path for the body-relative smoke simulation. It turns the
    /// selected trail records into soft, camera-facing crossed ribbons and uses KSP's built-in
    /// Particles/Alpha Blended material. No Unity editor project, compute shader, custom shader,
    /// Waterfall, EVE, or ParticleSystem is required at runtime.
    /// </summary>
    internal sealed class VolumetricSmokeRenderer : IDisposable
    {
        private const int RibbonsPerSegment = 2;
        private const int VerticesPerRibbon = 4;

        private struct RenderRecord
        {
            public TrailSegment Segment;
            public float DistanceSquared;
        }

        private readonly SmokeSettings _settings;
        private readonly List<TrailSegment> _segments;
        private readonly List<RenderRecord> _renderRecords;
        private readonly List<Vector3> _vertices;
        private readonly List<Vector2> _uvs;
        private readonly List<Color32> _colors;
        private readonly List<int> _triangles;
        private readonly GameObject _renderObject;
        private readonly Mesh _mesh;
        private readonly MeshFilter _meshFilter;
        private readonly MeshRenderer _meshRenderer;
        private readonly Texture2D _smokeTexture;
        private readonly Material _material;

        private Camera _camera;
        private int _emittedRibbonCount;
        private int _lastFrame = -1;
        private bool _disposed;

        private VolumetricSmokeRenderer(SmokeSettings settings, Shader shader)
        {
            _settings = settings;
            int segmentCapacity = Mathf.Max(8, settings.MaxVisibleSegments + settings.PadTileCount);
            _segments = new List<TrailSegment>(segmentCapacity);
            _renderRecords = new List<RenderRecord>(segmentCapacity);
            _vertices = new List<Vector3>(segmentCapacity * RibbonsPerSegment * VerticesPerRibbon);
            _uvs = new List<Vector2>(segmentCapacity * RibbonsPerSegment * VerticesPerRibbon);
            _colors = new List<Color32>(segmentCapacity * RibbonsPerSegment * VerticesPerRibbon);
            _triangles = new List<int>(segmentCapacity * RibbonsPerSegment * 6);

            _smokeTexture = CreateSmokeTexture();
            _material = CreateMaterial(shader, _smokeTexture);
            _renderObject = new GameObject("PersistentSRBSmoke.StockSmokeRenderer");
            UnityEngine.Object.DontDestroyOnLoad(_renderObject);
            _meshFilter = _renderObject.AddComponent<MeshFilter>();
            _meshRenderer = _renderObject.AddComponent<MeshRenderer>();
            _mesh = new Mesh { name = "PersistentSRBSmoke.StockSmokeMesh" };
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _meshRenderer.enabled = false;
        }

        public bool IsVisible
        {
            get { return _emittedRibbonCount > 0 && _camera != null && _camera.enabled; }
        }

        public static VolumetricSmokeRenderer TryCreate(SmokeSettings settings, out string failure)
        {
            failure = null;
            Shader shader = FindStockSmokeShader();
            if (shader == null)
            {
                failure = "KSP's built-in transparent particle shader was not found. Smoke is disabled.";
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }

            try
            {
                VolumetricSmokeRenderer renderer = new VolumetricSmokeRenderer(settings, shader);
                Debug.Log("[PersistentSRBSmoke] Using KSP stock smoke shader: " + shader.name
                    + ". No AssetBundle or Unity editor is required.");
                return renderer;
            }
            catch (Exception ex)
            {
                failure = "Failed to initialize stock smoke renderer: " + ex.Message;
                Debug.LogError("[PersistentSRBSmoke] " + failure);
                return null;
            }
        }

        public void UploadSegments(IList<TrailSegment> segments)
        {
            if (_disposed)
                return;

            _segments.Clear();
            if (segments == null)
                return;

            int maximum = Mathf.Min(_settings.MaxVisibleSegments + _settings.PadTileCount, segments.Count);
            for (int i = 0; i < maximum; i++)
            {
                TrailSegment segment = segments[i];
                if (segment.Active && segment.Body != null && segment.OpticalMass > 0.0001f)
                    _segments.Add(segment);
            }
        }

        public void LateUpdate()
        {
            if (_disposed || _lastFrame == Time.frameCount)
                return;
            _lastFrame = Time.frameCount;

            Camera camera = FindBestCamera();
            if (camera == null || !camera.enabled)
            {
                ClearMesh();
                return;
            }

            _camera = camera;
            if (_segments.Count == 0)
            {
                ClearMesh();
                return;
            }

            BuildMesh(camera);
        }

        public Material CreateShadowMaterial()
        {
            if (_disposed || _material == null)
                return null;
            return CreateMaterial(_material.shader, _smokeTexture);
        }

        public void InvalidateHistory()
        {
            // The stock path has no temporal history. It rebuilds the visible mesh from body-relative
            // records every frame, so Floating Origin shifts need no special resource reset.
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _segments.Clear();
            _renderRecords.Clear();
            ClearMesh();
            if (_renderObject != null)
                UnityEngine.Object.Destroy(_renderObject);
            if (_mesh != null)
                UnityEngine.Object.Destroy(_mesh);
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_smokeTexture != null)
                UnityEngine.Object.Destroy(_smokeTexture);
            _camera = null;
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

        private void BuildMesh(Camera camera)
        {
            _renderRecords.Clear();
            Vector3 cameraPosition = camera.transform.position;
            for (int i = 0; i < _segments.Count; i++)
            {
                TrailSegment segment = _segments[i];
                Vector3 center = segment.GetWorldCenter();
                _renderRecords.Add(new RenderRecord
                {
                    Segment = segment,
                    DistanceSquared = (center - cameraPosition).sqrMagnitude
                });
            }

            // Standard transparent materials need a stable back-to-front triangle order. Segment
            // selection/LOD has already happened in VolumetricSmokeSystem.
            _renderRecords.Sort(CompareBackToFront);
            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();

            Color lightTint = EvaluateLightingTint(camera);
            for (int i = 0; i < _renderRecords.Count; i++)
                AppendSegment(_renderRecords[i].Segment, camera, lightTint);

            _mesh.Clear();
            _emittedRibbonCount = _vertices.Count / VerticesPerRibbon;
            if (_emittedRibbonCount == 0)
            {
                _meshRenderer.enabled = false;
                return;
            }

            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _meshRenderer.enabled = true;
        }

        private void AppendSegment(TrailSegment segment, Camera camera, Color lightTint)
        {
            Vector3 start = segment.GetWorldStart();
            Vector3 end = segment.GetWorldEnd();
            Vector3 center = (start + end) * 0.5f;
            Vector3 axis = end - start;
            float length = axis.magnitude;
            if (length < 0.05f)
            {
                axis = segment.Velocity;
                if (axis.sqrMagnitude < 0.001f)
                    axis = camera.transform.up;
                axis.Normalize();
                length = Mathf.Max(0.35f, segment.Radius * 1.3f);
                start = center - axis * (length * 0.5f);
                end = center + axis * (length * 0.5f);
            }
            else
            {
                axis /= length;
            }

            Vector3 eyeDirection = camera.transform.position - center;
            if (eyeDirection.sqrMagnitude < 0.001f)
                eyeDirection = -camera.transform.forward;
            eyeDirection.Normalize();
            Vector3 side = Vector3.Cross(axis, eyeDirection);
            if (side.sqrMagnitude < 0.001f)
                side = Vector3.Cross(axis, camera.transform.up);
            if (side.sqrMagnitude < 0.001f)
                side = Vector3.Cross(axis, Vector3.right);
            side.Normalize();
            Vector3 diagonal = (side + Vector3.Cross(axis, side)).normalized;
            if (diagonal.sqrMagnitude < 0.001f)
                diagonal = side;

            float radius = Mathf.Max(0.15f, segment.Radius);
            float density = 1f - Mathf.Exp(-segment.OpticalMass / Mathf.Max(3f, 6f + radius * radius * 5f));
            float lifetimeFade = Mathf.Lerp(0.18f, 1f, 1f - segment.NormalizedAge);
            float kindOpacity = segment.Kind == SmokeSegmentKind.Nozzle ? 0.82f
                : segment.Kind == SmokeSegmentKind.Pad ? 0.42f : 0.58f;
            float alpha = Mathf.Clamp01(density * lifetimeFade * kindOpacity * segment.Color.a);
            if (alpha <= 0.002f)
                return;

            Color color = segment.Color;
            if (segment.Kind == SmokeSegmentKind.Nozzle)
            {
                Color hot = new Color(1f, 0.58f, 0.24f, 1f);
                color = Color.Lerp(color, hot, Mathf.Clamp01(segment.Temperature) * 0.46f);
            }
            color.r = Mathf.Clamp01(color.r * lightTint.r * _settings.SmokeBrightness);
            color.g = Mathf.Clamp01(color.g * lightTint.g * _settings.SmokeBrightness);
            color.b = Mathf.Clamp01(color.b * lightTint.b * _settings.SmokeBrightness);
            color.a = alpha;

            float asymmetry = 0.90f + (SrbSmokeMath.Hash(segment.Seed) & 0xFFU) / 255f * 0.20f;
            AppendRibbon(start, end, side,
                Mathf.Max(0.12f, segment.StartRadius) * asymmetry,
                Mathf.Max(0.12f, segment.EndRadius) * asymmetry,
                color);
            Color innerColor = color;
            innerColor.a *= 0.58f;
            AppendRibbon(start, end, diagonal,
                Mathf.Max(0.12f, segment.StartRadius) * 0.74f,
                Mathf.Max(0.12f, segment.EndRadius) * 0.74f,
                innerColor);
        }

        private void AppendRibbon(Vector3 start, Vector3 end, Vector3 side, float startRadius, float endRadius, Color color)
        {
            if (color.a <= 0.002f)
                return;

            int baseIndex = _vertices.Count;
            _vertices.Add(start - side * startRadius);
            _vertices.Add(start + side * startRadius);
            _vertices.Add(end + side * endRadius);
            _vertices.Add(end - side * endRadius);
            _uvs.Add(new Vector2(0f, 0f));
            _uvs.Add(new Vector2(0f, 1f));
            _uvs.Add(new Vector2(1f, 1f));
            _uvs.Add(new Vector2(1f, 0f));
            Color32 vertexColor = color;
            _colors.Add(vertexColor);
            _colors.Add(vertexColor);
            _colors.Add(vertexColor);
            _colors.Add(vertexColor);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 3);
        }

        private Color EvaluateLightingTint(Camera camera)
        {
            Vector3 sunDirection;
            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel == null ? null : vessel.mainBody;
            if (!SmokeLighting.TryGetSunDirection(body, out sunDirection))
                return Color.white;
            float elevation = Vector3.Dot(camera.transform.up, sunDirection);
            Color sunset = SmokeLighting.EvaluateSunTint(elevation, _settings.SunsetWarmth);
            return Color.Lerp(new Color(_settings.AmbientLight, _settings.AmbientLight, _settings.AmbientLight, 1f),
                sunset * _settings.SunLight, 0.72f);
        }

        private void ClearMesh()
        {
            _emittedRibbonCount = 0;
            if (_mesh != null)
                _mesh.Clear();
            if (_meshRenderer != null)
                _meshRenderer.enabled = false;
        }

        private static int CompareBackToFront(RenderRecord left, RenderRecord right)
        {
            return right.DistanceSquared.CompareTo(left.DistanceSquared);
        }

        private static Shader FindStockSmokeShader()
        {
            string[] candidates =
            {
                "Particles/Alpha Blended",
                "Legacy Shaders/Particles/Alpha Blended",
                "Unlit/Transparent",
                "Sprites/Default"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                Shader shader = Shader.Find(candidates[i]);
                if (shader != null)
                    return shader;
            }
            return null;
        }

        private static Material CreateMaterial(Shader shader, Texture2D texture)
        {
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_MainTex", texture);
            material.SetColor("_TintColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.renderQueue = 3000;
            return material;
        }

        private static Texture2D CreateSmokeTexture()
        {
            const int width = 128;
            const int height = 64;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "PersistentSRBSmoke.StockSmokeTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float edge = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v * 2f - 1f)), 0.62f);
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    float endFade = Mathf.Lerp(0.64f, 1f, Mathf.Clamp01(1f - Mathf.Abs(u * 2f - 1f)));
                    float noise = Mathf.Lerp(0.72f, 1f, Mathf.PerlinNoise(u * 8.7f, v * 5.3f));
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(edge * endFade * noise * 255f), 0, 255);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
