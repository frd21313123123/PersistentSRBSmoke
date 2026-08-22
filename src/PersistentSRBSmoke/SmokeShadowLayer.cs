using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Soft projected shadow for the long-lived SRB smoke trail.
    ///
    /// The visible geometry is rebuilt in LateUpdate so it follows the particle trail every rendered
    /// frame. Expensive PQS terrain-height queries are cached in coarse surface cells and only a small
    /// number of new cells are resolved per frame. This keeps motion smooth without turning a long
    /// shadow into hundreds of PQS calls every frame.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SmokeShadowLayer : MonoBehaviour
    {
        private sealed class ShadowSettings
        {
            public bool Enabled = true;
            public float UpdateHz = 8f;
            public int MaxQuads = 900;
            public int SampleStride = 12;
            public float MaxAltitude = 14000f;
            public float Opacity = 0.18f;
            public float SizeMultiplier = 1.55f;
            public float LengthMultiplier = 1.15f;
            public float MaxStretch = 3.2f;
            public float SurfaceOffset = 4.0f;
            public float MinSourceAlpha = 0.025f;
            public float MinSunDot = 0.055f;
            public int TerrainQueriesPerFrame = 16;
            public float TerrainCacheMeters = 220f;
            public int TerrainCacheCapacity = 12000;
            public float SizeGrowth = 14.0f;
            public int SourceMaxParticles = 48000;
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
                    settings.TerrainQueriesPerFrame = ReadInt(node, "smokeShadowTerrainQueriesPerFrame", settings.TerrainQueriesPerFrame, 0, 512);
                    settings.TerrainCacheMeters = ReadFloat(node, "smokeShadowTerrainCacheMeters", settings.TerrainCacheMeters, 20f, 5000f);
                    settings.TerrainCacheCapacity = ReadInt(node, "smokeShadowTerrainCacheCapacity", settings.TerrainCacheCapacity, 256, 100000);
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

        private sealed class TerrainCacheEntry
        {
            public double SurfaceAltitude;
            public int LastUsedFrame;
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
        private readonly Dictionary<long, TerrainCacheEntry> _terrainCache = new Dictionary<long, TerrainCacheEntry>();

        private float _nextUpdate;
        private float _nextSourceSearch;
        private float _nextDebugLog;
        private int _frameIndex;
        private int _terrainQueriesRemaining;
        private int _terrainQueriesUsedThisFrame;

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

            _frameIndex = (_frameIndex + 1) & 0x3FFFFFFF;
            _terrainQueriesRemaining = Mathf.Max(0, _settings.TerrainQueriesPerFrame);
            _terrainQueriesUsedThisFrame = 0;
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
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
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
            float bodyRadius = (float)body.Radius;
            float maximumSourceRadius = bodyRadius + _settings.MaxAltitude;
            float bodyRadiusSqr = bodyRadius * bodyRadius;
            float maximumSourceRadiusSqr = maximumSourceRadius * maximumSourceRadius;
            Vector3 sunPosition = sun.transform.position;
            Vector3 lightTravelDirection = bodyCenter - sunPosition;
            if (lightTravelDirection.sqrMagnitude < 1f)
            {
                ClearMesh();
                return;
            }
            lightTravelDirection.Normalize();

            // Seed-based sampling remains stable as the trail grows, avoiding a full visual reshuffle
            // whenever an index-based dynamic stride changes by one.
            float strideProbability = 1f / Mathf.Max(1, _settings.SampleStride);
            float capacityProbability = (_settings.MaxQuads * 1.35f) / Mathf.Max(1f, count);
            float sampleProbability = Mathf.Clamp01(Mathf.Min(strideProbability, capacityProbability));
            uint sampleThreshold = sampleProbability >= 0.999999f
                ? uint.MaxValue
                : (uint)(sampleProbability * uint.MaxValue);

            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();

            int emitted = 0;
            int candidates = 0;
            for (int i = 0; i < count && emitted < _settings.MaxQuads; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];
                if (Hash32(particle.randomSeed ^ 0xB5297A4DU) > sampleThreshold)
                    continue;
                candidates++;

                if (particle.remainingLifetime <= 0f || particle.startLifetime <= 0.001f)
                    continue;

                Vector3 radial = particle.position - bodyCenter;
                float radialSqrMagnitude = radial.sqrMagnitude;
                if (radialSqrMagnitude <= bodyRadiusSqr || radialSqrMagnitude > maximumSourceRadiusSqr)
                    continue;

                float radialMagnitude = radial.magnitude;
                float altitude = radialMagnitude - bodyRadius;

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
                    " candidates=" + candidates +
                    " sourceParticles=" + count +
                    " terrainCache=" + _terrainCache.Count +
                    " pqsQueries=" + _terrainQueriesUsedThisFrame);
                _nextDebugLog = Time.realtimeSinceStartup + 5f;
            }
        }

        private void EnsureParentBody(CelestialBody body)
        {
            if (_parentBody == body)
                return;

            _parentBody = body;
            _terrainCache.Clear();
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

            double surfaceAltitude;
            if (!TryGetSurfaceAltitude(body, latitude, longitude, out surfaceAltitude))
                return false;

            Vector3d worldSurface = body.GetWorldSurfacePosition(latitude, longitude, surfaceAltitude);
            Vector3d normalD = body.GetSurfaceNVector(latitude, longitude);
            groundPoint = worldSurface;
            groundNormal = ((Vector3)normalD).normalized;

            Vector3 toSun = -lightTravelDirection;
            sunDot = Mathf.Clamp01(Vector3.Dot(groundNormal, toSun));
            return sunDot > 0f;
        }

        private bool TryGetSurfaceAltitude(
            CelestialBody body,
            double latitude,
            double longitude,
            out double surfaceAltitude)
        {
            surfaceAltitude = 0.0;
            if (body == null || body.pqsController == null)
                return true;

            int latCell;
            int lonCell;
            GetTerrainCell(body, latitude, longitude, out latCell, out lonCell);
            long key = PackTerrainKey(latCell, lonCell);

            TerrainCacheEntry entry;
            if (_terrainCache.TryGetValue(key, out entry))
            {
                entry.LastUsedFrame = _frameIndex;
                surfaceAltitude = entry.SurfaceAltitude;
                return true;
            }

            TerrainCacheEntry neighbour = null;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0)
                        continue;
                    long neighbourKey = PackTerrainKey(latCell + y, lonCell + x);
                    if (_terrainCache.TryGetValue(neighbourKey, out neighbour))
                    {
                        neighbour.LastUsedFrame = _frameIndex;
                        surfaceAltitude = neighbour.SurfaceAltitude;
                        break;
                    }
                }
                if (neighbour != null)
                    break;
            }

            if (_terrainQueriesRemaining <= 0)
                return neighbour != null;

            _terrainQueriesRemaining--;
            _terrainQueriesUsedThisFrame++;
            surfaceAltitude = QuerySurfaceAltitude(body, latitude, longitude);

            if (_terrainCache.Count >= _settings.TerrainCacheCapacity)
                TrimTerrainCache();

            _terrainCache[key] = new TerrainCacheEntry
            {
                SurfaceAltitude = surfaceAltitude,
                LastUsedFrame = _frameIndex
            };
            return true;
        }

        private double QuerySurfaceAltitude(CelestialBody body, double latitude, double longitude)
        {
            double latRadians = latitude * Math.PI / 180.0;
            double lonRadians = longitude * Math.PI / 180.0;
            Vector3d surfaceRadial = new Vector3d(
                Math.Cos(latRadians) * Math.Cos(lonRadians),
                Math.Sin(latRadians),
                Math.Cos(latRadians) * Math.Sin(lonRadians));

            double surfaceHeight = body.pqsController.GetSurfaceHeight(surfaceRadial);
            double surfaceAltitude = surfaceHeight - body.pqsController.radius;
            if (double.IsNaN(surfaceAltitude) || double.IsInfinity(surfaceAltitude))
                surfaceAltitude = 0.0;
            if (body.ocean && surfaceAltitude < 0.0)
                surfaceAltitude = 0.0;
            return surfaceAltitude;
        }

        private void GetTerrainCell(
            CelestialBody body,
            double latitude,
            double longitude,
            out int latCell,
            out int lonCell)
        {
            double radius = Math.Max(1.0, body.Radius);
            double cellDegrees = Math.Max(
                1e-7,
                _settings.TerrainCacheMeters / radius * 180.0 / Math.PI);

            double normalizedLongitude = longitude;
            while (normalizedLongitude < -180.0) normalizedLongitude += 360.0;
            while (normalizedLongitude >= 180.0) normalizedLongitude -= 360.0;

            latCell = (int)Math.Floor((latitude + 90.0) / cellDegrees);
            lonCell = (int)Math.Floor((normalizedLongitude + 180.0) / cellDegrees);
        }

        private static long PackTerrainKey(int latCell, int lonCell)
        {
            return ((long)latCell << 32) ^ (uint)lonCell;
        }

        private void TrimTerrainCache()
        {
            int staleFrame = _frameIndex - 600;
            var staleKeys = new List<long>();
            foreach (KeyValuePair<long, TerrainCacheEntry> pair in _terrainCache)
            {
                if (pair.Value.LastUsedFrame < staleFrame)
                    staleKeys.Add(pair.Key);
            }

            for (int i = 0; i < staleKeys.Count; i++)
                _terrainCache.Remove(staleKeys[i]);

            if (_terrainCache.Count >= _settings.TerrainCacheCapacity)
                _terrainCache.Clear();
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
            const float response = 0.25f;
            const float birthScale = 0.60f;
            float denominator = 1f - Mathf.Exp(-1f / response);
            float normalized = (1f - Mathf.Exp(-age / response)) / denominator;
            return birthScale + (growth - birthScale) * normalized;
        }

        private static uint Hash32(uint value)
        {
            value ^= value >> 17;
            value *= 0xed5ad4bbU;
            value ^= value >> 11;
            value *= 0xac4c1b51U;
            value ^= value >> 15;
            return value;
        }

        private static float HashToUnit(uint value)
        {
            return (Hash32(value) & 0x00FFFFFFU) / 16777215f;
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
            _terrainCache.Clear();

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
