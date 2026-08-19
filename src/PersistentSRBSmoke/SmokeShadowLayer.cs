using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Cheap projected shadow for the long-lived SRB smoke trail.
    ///
    /// Transparent Shuriken particles do not produce a useful soft world shadow in KSP, so this
    /// layer samples the existing persistent smoke, projects a subset of cloudlets along sunlight
    /// onto the active body's terrain surface and builds a soft shadow mesh synchronized with the
    /// visible particle trail.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SmokeShadowLayer : MonoBehaviour
    {
        private sealed class ShadowSettings
        {
            public bool Enabled = true;
            // 0 = rebuild once per rendered frame. Positive values are an optional FPS cap for
            // users who prefer cheaper shadows on slow hardware.
            public float UpdateHz = 0f;
            public int MaxQuads = 1800;
            public int SampleStride = 8;
            public float MaxAltitude = 14000f;
            public float Opacity = 0.18f;
            public float SizeMultiplier = 1.55f;
            public float LengthMultiplier = 1.15f;
            public float MaxStretch = 3.2f;
            public float SurfaceOffset = 4.0f;
            public float MinSourceAlpha = 0.025f;
            public float MinSunDot = 0.055f;
            public float SizeGrowth = 18.0f;
            public int SourceMaxParticles = 36000;
            public bool DebugLogging = false;

            public static ShadowSettings Load()
            {
                var settings = new ShadowSettings();
                try
                {
                    string path = KSPUtil.ApplicationRootPath + "GameData/PersistentSRBSmoke/PluginData/Settings.cfg";
                    ConfigNode file = ConfigNode.Load(path);
                    ConfigNode node = file == null ? null : file.GetNode("PERSISTENT_SRB_SMOKE");
                    if (node == null)
                        return settings;

                    settings.Enabled = ReadBool(node, "enabled", true)
                        && ReadBool(node, "smokeShadowEnabled", settings.Enabled);
                    settings.UpdateHz = ReadFloat(node, "smokeShadowUpdateHz", settings.UpdateHz, 0f, 240f);
                    settings.MaxQuads = ReadInt(node, "smokeShadowMaxQuads", settings.MaxQuads, 64, 10000);
                    settings.SampleStride = ReadInt(node, "smokeShadowSampleStride", settings.SampleStride, 1, 64);
                    settings.MaxAltitude = ReadFloat(node, "smokeShadowMaxAltitude", settings.MaxAltitude, 100f, 100000f);
                    settings.Opacity = ReadFloat(node, "smokeShadowOpacity", settings.Opacity, 0f, 0.8f);
                    settings.SizeMultiplier = ReadFloat(node, "smokeShadowSizeMultiplier", settings.SizeMultiplier, 0.2f, 8f);
                    settings.LengthMultiplier = ReadFloat(node, "smokeShadowLengthMultiplier", settings.LengthMultiplier, 0.2f, 8f);
                    settings.MaxStretch = ReadFloat(node, "smokeShadowMaxStretch", settings.MaxStretch, 1f, 12f);
                    settings.SurfaceOffset = ReadFloat(node, "smokeShadowSurfaceOffset", settings.SurfaceOffset, 0.1f, 50f);
                    settings.MinSourceAlpha = ReadFloat(node, "smokeShadowMinSourceAlpha", settings.MinSourceAlpha, 0f, 1f);
                    settings.MinSunDot = ReadFloat(node, "smokeShadowMinSunDot", settings.MinSunDot, 0f, 0.5f);
                    settings.SizeGrowth = ReadFloat(node, "sizeGrowth", settings.SizeGrowth, 1f, 40f);
                    settings.SourceMaxParticles = ReadInt(node, "maxParticles", settings.SourceMaxParticles, 1000, 150000);
                    settings.DebugLogging = ReadBool(node, "debugLogging", settings.DebugLogging);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PersistentSRBSmoke] Smoke shadow settings fallback: " + ex.Message);
                }
                return settings;
            }

            private static float ReadFloat(ConfigNode node, string key, float fallback, float min, float max)
            {
                string raw = node.GetValue(key);
                float value;
                if (raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return Mathf.Clamp(value, min, max);
                return fallback;
            }

            private static int ReadInt(ConfigNode node, string key, int fallback, int min, int max)
            {
                string raw = node.GetValue(key);
                int value;
                if (raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return Math.Max(min, Math.Min(max, value));
                return fallback;
            }

            private static bool ReadBool(ConfigNode node, string key, bool fallback)
            {
                string raw = node.GetValue(key);
                bool value;
                if (raw != null && bool.TryParse(raw, out value))
                    return value;
                return fallback;
            }
        }

        private ShadowSettings _settings;
        private ParticleSystem _sourceSystem;
        private ParticleSystem.Particle[] _particleBuffer;

        private GameObject _shadowObject;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private Texture2D _texture;
        private CelestialBody _parentBody;

        private readonly List<Vector3> _vertices = new List<Vector3>(8192);
        private readonly List<Vector2> _uvs = new List<Vector2>(8192);
        private readonly List<Color32> _colors = new List<Color32>(8192);
        private readonly List<int> _triangles = new List<int>(12288);

        private float _nextUpdate;
        private float _nextSourceSearch;
        private float _nextDebugLog;

        private void Start()
        {
            _settings = ShadowSettings.Load();
            if (!_settings.Enabled || _settings.Opacity <= 0.001f)
            {
                enabled = false;
                return;
            }

            try
            {
                _particleBuffer = new ParticleSystem.Particle[Mathf.Max(1000, _settings.SourceMaxParticles)];
                CreateRenderer();
                TryFindSourceSystem();
            }
            catch (Exception ex)
            {
                Debug.LogError("[PersistentSRBSmoke] Smoke shadow initialization failed: " + ex);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || _shadowObject == null)
                return;

            float now = Time.realtimeSinceStartup;
            if (_sourceSystem == null && now >= _nextSourceSearch)
            {
                TryFindSourceSystem();
                _nextSourceSearch = now + 1.0f;
            }

            if (_sourceSystem == null)
                return;

            // Frame-synchronised is the default. A positive smokeShadowUpdateHz remains available
            // as an explicit performance cap, but zero follows the visible Shuriken trail every
            // rendered frame and therefore cannot visibly jump between 3 Hz mesh rebuilds.
            if (_settings.UpdateHz > 0.001f)
            {
                if (now < _nextUpdate)
                    return;
                _nextUpdate = now + 1f / Mathf.Max(1f, _settings.UpdateHz);
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel == null ? null : vessel.mainBody;
            if (body == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
            {
                ClearMesh();
                return;
            }

            CelestialBody sun = FlightGlobals.Bodies[0];
            if (sun == null || sun == body)
            {
                ClearMesh();
                return;
            }

            UpdateShadowMesh(body, sun);
        }

        private void TryFindSourceSystem()
        {
            GameObject sourceObject = GameObject.Find("PersistentSRBSmoke.ParticlePool");
            _sourceSystem = sourceObject == null ? null : sourceObject.GetComponent<ParticleSystem>();

            if (_settings.DebugLogging && _sourceSystem != null)
                Debug.Log("[PersistentSRBSmoke] Smoke shadow linked to persistent particle pool.");
        }

        private void CreateRenderer()
        {
            _shadowObject = new GameObject("PersistentSRBSmoke.ProjectedShadow");
            UnityEngine.Object.DontDestroyOnLoad(_shadowObject);

            _meshFilter = _shadowObject.AddComponent<MeshFilter>();
            _meshRenderer = _shadowObject.AddComponent<MeshRenderer>();

            _mesh = new Mesh();
            _mesh.name = "PersistentSRBSmoke.ProjectedShadowMesh";
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;

            _texture = CreateShadowTexture(96);
            _material = CreateShadowMaterial(_texture);
            _meshRenderer.sharedMaterial = _material;
        }

        private void UpdateShadowMesh(CelestialBody body, CelestialBody sun)
        {
            if (_sourceSystem == null || _mesh == null)
                return;

            int count = _sourceSystem.GetParticles(_particleBuffer);
            if (count <= 0)
            {
                ClearMesh();
                return;
            }

            EnsureParentBody(body);

            Vector3 bodyCenter = body.transform.position;
            Vector3 sunPosition = sun.transform.position;
            Vector3 lightTravelDirection = bodyCenter - sunPosition;
            if (lightTravelDirection.sqrMagnitude < 1f)
            {
                ClearMesh();
                return;
            }
            lightTravelDirection.Normalize();

            int dynamicStride = Mathf.Max(
                _settings.SampleStride,
                Mathf.CeilToInt(count / (float)Mathf.Max(1, _settings.MaxQuads)));

            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();

            int emitted = 0;
            for (int i = 0; i < count && emitted < _settings.MaxQuads; i += dynamicStride)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];
                if (particle.remainingLifetime <= 0f || particle.startLifetime <= 0.001f)
                    continue;

                Vector3 radial = particle.position - bodyCenter;
                float radialMagnitude = radial.magnitude;
                if (radialMagnitude <= body.Radius)
                    continue;

                float altitude = radialMagnitude - (float)body.Radius;
                if (altitude > _settings.MaxAltitude)
                    continue;

                float age = Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);
                Color32 startColor = particle.startColor;
                float sourceAlpha = (startColor.a / 255f) * EvaluateTrailAlpha(age);
                if (sourceAlpha < _settings.MinSourceAlpha)
                    continue;

                Vector3 groundPoint;
                Vector3 groundNormal;
                float sunDot;
                if (!TryProjectToSurface(
                    body,
                    particle.position,
                    lightTravelDirection,
                    out groundPoint,
                    out groundNormal,
                    out sunDot))
                {
                    continue;
                }

                if (sunDot < _settings.MinSunDot)
                    continue;

                float altitudeFade = 1f - Mathf.SmoothStep(
                    0.62f,
                    1f,
                    Mathf.Clamp01(altitude / Mathf.Max(1f, _settings.MaxAltitude)));
                if (altitudeFade <= 0.001f)
                    continue;

                float normalizedDensity = Mathf.Clamp01(
                    (sourceAlpha - _settings.MinSourceAlpha) /
                    Mathf.Max(0.05f, 0.42f - _settings.MinSourceAlpha));

                float seed = HashToUnit(particle.randomSeed);
                float alpha = _settings.Opacity
                    * Mathf.Lerp(0.72f, 1.08f, seed)
                    * normalizedDensity
                    * altitudeFade
                    * Mathf.Lerp(0.65f, 1f, Mathf.Sqrt(Mathf.Clamp01(sunDot)));
                alpha = Mathf.Clamp01(alpha);
                if (alpha <= 0.003f)
                    continue;

                float size = particle.startSize
                    * EvaluateTrailSize(age, _settings.SizeGrowth)
                    * _settings.SizeMultiplier
                    * Mathf.Lerp(0.80f, 1.22f, HashToUnit(particle.randomSeed ^ 0x9E3779B9U));
                size = Mathf.Max(0.5f, size);

                Vector3 along = Vector3.ProjectOnPlane(lightTravelDirection, groundNormal);
                if (along.sqrMagnitude < 0.001f)
                {
                    along = Vector3.Cross(groundNormal, body.transform.up);
                    if (along.sqrMagnitude < 0.001f)
                        along = Vector3.Cross(groundNormal, Vector3.right);
                }
                along.Normalize();
                Vector3 across = Vector3.Cross(groundNormal, along).normalized;

                float stretch = Mathf.Clamp(
                    1f / Mathf.Max(0.16f, sunDot),
                    1f,
                    _settings.MaxStretch);
                float halfWidth = size * 0.5f;
                float halfLength = size * 0.5f * _settings.LengthMultiplier * stretch;

                AddShadowQuad(
                    body,
                    groundPoint + groundNormal * _settings.SurfaceOffset,
                    along,
                    across,
                    halfLength,
                    halfWidth,
                    (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
                emitted++;
            }

            _mesh.Clear();
            if (_vertices.Count > 0)
            {
                _mesh.SetVertices(_vertices);
                _mesh.SetUVs(0, _uvs);
                _mesh.SetColors(_colors);
                _mesh.SetTriangles(_triangles, 0);
                _mesh.RecalculateBounds();
            }

            if (_settings.DebugLogging && Time.realtimeSinceStartup >= _nextDebugLog)
            {
                Debug.Log(
                    "[PersistentSRBSmoke] Shadow quads=" + emitted +
                    " sourceParticles=" + count +
                    " stride=" + dynamicStride);
                _nextDebugLog = Time.realtimeSinceStartup + 5f;
            }
        }

        private void EnsureParentBody(CelestialBody body)
        {
            if (_parentBody == body)
                return;

            _parentBody = body;
            _shadowObject.transform.SetParent(body.transform, false);
            _shadowObject.transform.localPosition = Vector3.zero;
            _shadowObject.transform.localRotation = Quaternion.identity;
            _shadowObject.transform.localScale = Vector3.one;
        }

        private void AddShadowQuad(
            CelestialBody body,
            Vector3 center,
            Vector3 along,
            Vector3 across,
            float halfLength,
            float halfWidth,
            byte alpha)
        {
            Vector3 w0 = center - along * halfLength - across * halfWidth;
            Vector3 w1 = center + along * halfLength - across * halfWidth;
            Vector3 w2 = center + along * halfLength + across * halfWidth;
            Vector3 w3 = center - along * halfLength + across * halfWidth;

            int baseIndex = _vertices.Count;
            _vertices.Add(body.transform.InverseTransformPoint(w0));
            _vertices.Add(body.transform.InverseTransformPoint(w1));
            _vertices.Add(body.transform.InverseTransformPoint(w2));
            _vertices.Add(body.transform.InverseTransformPoint(w3));

            _uvs.Add(new Vector2(0f, 0f));
            _uvs.Add(new Vector2(1f, 0f));
            _uvs.Add(new Vector2(1f, 1f));
            _uvs.Add(new Vector2(0f, 1f));

            Color32 color = new Color32(8, 10, 12, alpha);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);

            // Two-sided quad. Terrain/camera orientation can flip across the curved body.
            _triangles.Add(baseIndex + 0);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 0);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 3);

            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 0);
            _triangles.Add(baseIndex + 3);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 0);
        }

        private bool TryProjectToSurface(
            CelestialBody body,
            Vector3 source,
            Vector3 lightTravelDirection,
            out Vector3 groundPoint,
            out Vector3 groundNormal,
            out float sunDot)
        {
            groundPoint = Vector3.zero;
            groundNormal = Vector3.up;
            sunDot = 0f;

            Vector3 center = body.transform.position;
            Vector3 oc = source - center;
            float radius = (float)body.Radius;
            float b = Vector3.Dot(oc, lightTravelDirection);
            float c = oc.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0f)
                return false;

            float root = Mathf.Sqrt(discriminant);
            float t = -b - root;
            if (t <= 0f)
                t = -b + root;
            if (t <= 0f)
                return false;

            Vector3 roughPoint = source + lightTravelDirection * t;
            Vector3d roughPointD = roughPoint;
            double latitude = body.GetLatitude(roughPointD);
            double longitude = body.GetLongitude(roughPointD);

            double surfaceAltitude = 0.0;
            if (body.pqsController != null)
            {
                double latRadians = latitude * Math.PI / 180.0;
                double lonRadians = longitude * Math.PI / 180.0;
                Vector3d surfaceRadial = new Vector3d(
                    Math.Cos(latRadians) * Math.Cos(lonRadians),
                    Math.Sin(latRadians),
                    Math.Cos(latRadians) * Math.Sin(lonRadians));

                double surfaceHeight = body.pqsController.GetSurfaceHeight(surfaceRadial);
                surfaceAltitude = surfaceHeight - body.pqsController.radius;
                if (double.IsNaN(surfaceAltitude) || double.IsInfinity(surfaceAltitude))
                    surfaceAltitude = 0.0;
                if (body.ocean && surfaceAltitude < 0.0)
                    surfaceAltitude = 0.0;
            }

            Vector3d worldSurface = body.GetWorldSurfacePosition(latitude, longitude, surfaceAltitude);
            Vector3d normalD = body.GetSurfaceNVector(latitude, longitude);
            groundPoint = worldSurface;
            groundNormal = ((Vector3)normalD).normalized;

            Vector3 toSun = -lightTravelDirection;
            sunDot = Mathf.Clamp01(Vector3.Dot(groundNormal, toSun));
            return sunDot > 0f;
        }

        private static float EvaluateTrailAlpha(float age)
        {
            age = Mathf.Clamp01(age);
            if (age <= 0.08f)
                return Mathf.Lerp(0.98f, 0.90f, Mathf.SmoothStep(0f, 1f, age / 0.08f));
            if (age <= 0.30f)
                return Mathf.Lerp(0.90f, 0.74f, Mathf.SmoothStep(0f, 1f, (age - 0.08f) / 0.22f));
            if (age <= 0.72f)
                return Mathf.Lerp(0.74f, 0.48f, Mathf.SmoothStep(0f, 1f, (age - 0.30f) / 0.42f));
            return Mathf.Lerp(0.48f, 0f, Mathf.SmoothStep(0f, 1f, (age - 0.72f) / 0.28f));
        }

        private static float EvaluateTrailSize(float age, float growth)
        {
            age = Mathf.Clamp01(age);
            growth = Mathf.Max(1f, growth);
            float k1 = Mathf.Min(growth, 3.4f);
            float k2 = Mathf.Min(growth, 7.0f);
            float k3 = Mathf.Min(growth, 12.0f);

            if (age <= 0.04f)
                return Mathf.Lerp(1.00f, 1.55f, Mathf.SmoothStep(0f, 1f, age / 0.04f));
            if (age <= 0.10f)
                return Mathf.Lerp(1.55f, k1, Mathf.SmoothStep(0f, 1f, (age - 0.04f) / 0.06f));
            if (age <= 0.22f)
                return Mathf.Lerp(k1, k2, Mathf.SmoothStep(0f, 1f, (age - 0.10f) / 0.12f));
            if (age <= 0.45f)
                return Mathf.Lerp(k2, k3, Mathf.SmoothStep(0f, 1f, (age - 0.22f) / 0.23f));
            return Mathf.Lerp(k3, growth, Mathf.SmoothStep(0f, 1f, (age - 0.45f) / 0.55f));
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

        private static Material CreateShadowMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("KSP/Particles/Alpha Blended");
            if (shader == null)
                throw new InvalidOperationException("No compatible alpha-blended shadow shader was found.");

            Material material = new Material(shader);
            material.name = "PersistentSRBSmoke.ProjectedShadowMaterial";
            material.mainTexture = texture;

            // Terrain/opaque geometry is already drawn, while the smoke particle materials normally
            // live at Transparent (3000). Rendering the projected shadow just before them prevents
            // the dark mesh from being composited on top of the smoke itself.
            material.renderQueue = 2990;
            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", Color.white);
            return material;
        }

        private static Texture2D CreateShadowTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.name = "PersistentSRBSmoke.ProjectedShadowTexture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float seedX = UnityEngine.Random.Range(20f, 600f);
            float seedY = UnityEngine.Random.Range(20f, 600f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float radial = Mathf.Clamp01(1f - r);
                    radial = radial * radial * (3f - 2f * radial);

                    float macro = Mathf.PerlinNoise(seedX + u * 1.7f, seedY + v * 1.7f);
                    float detail = Mathf.PerlinNoise(seedX * 0.33f + u * 4.9f, seedY * 0.33f + v * 4.9f);
                    float breakup = Mathf.Lerp(0.72f, 1f, macro * 0.68f + detail * 0.32f);
                    float alpha = Mathf.Pow(radial, 0.72f) * breakup;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private void ClearMesh()
        {
            if (_mesh != null)
                _mesh.Clear();
        }

        private void OnDestroy()
        {
            _sourceSystem = null;
            _parentBody = null;
            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();

            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            if (_shadowObject != null) UnityEngine.Object.Destroy(_shadowObject);

            _mesh = null;
            _material = null;
            _texture = null;
            _shadowObject = null;
            _meshFilter = null;
            _meshRenderer = null;
        }
    }
}
