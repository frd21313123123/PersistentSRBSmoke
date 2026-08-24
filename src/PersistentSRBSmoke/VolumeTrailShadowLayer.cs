using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Projects integrated segment density towards the Sun. This retains the terrain query cache
    /// from the previous shadow layer, but consumes volume records directly instead of particles.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class VolumeTrailShadowLayer : MonoBehaviour
    {
        private sealed class TerrainCacheEntry
        {
            public double SurfaceAltitude;
            public int LastUsedFrame;
        }

        private SmokeSettings _settings;
        private VolumetricSmokeSystem _system;
        private GameObject _shadowObject;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material _material;
        private CelestialBody _parentBody;
        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Vector2> _uvs = new List<Vector2>(4096);
        private readonly List<Color32> _colors = new List<Color32>(4096);
        private readonly List<int> _triangles = new List<int>(6144);
        private readonly Dictionary<long, TerrainCacheEntry> _terrainCache = new Dictionary<long, TerrainCacheEntry>();
        private float _nextUpdate;
        private int _frameIndex;
        private int _terrainQueriesRemaining;

        private void Start()
        {
            _settings = SmokeSettings.Load();
            if (!_settings.Enabled || !_settings.ShadowsEnabled || _settings.ShadowOpacity <= 0.001f)
            {
                enabled = false;
                return;
            }
            CreateRenderer();
        }

        private void LateUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || _shadowObject == null)
                return;
            if (_system == null || !_system.IsAvailable)
                _system = VolumetricSmokeRegistry.Current;
            if (_system == null || !_system.IsAvailable)
            {
                ClearMesh();
                return;
            }
            if (_material == null)
            {
                _material = GetShadowMaterial(_system);
                if (_material == null)
                    return;
                _meshRenderer.sharedMaterial = _material;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextUpdate)
                return;
            _nextUpdate = now + 1f / Mathf.Max(0.1f, _settings.ShadowUpdateHz);

            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel == null ? null : vessel.mainBody;
            CelestialBody sun = FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0
                ? null
                : FlightGlobals.Bodies[0];
            if (body == null || sun == null || body == sun)
            {
                ClearMesh();
                return;
            }

            _frameIndex = (_frameIndex + 1) & 0x3FFFFFFF;
            _terrainQueriesRemaining = _settings.ShadowTerrainQueriesPerFrame;
            UpdateShadowMesh(body, sun, _system.GetShadowSamples());
        }

        private static Material GetShadowMaterial(VolumetricSmokeSystem system)
        {
            // The renderer owns the generated soft texture and returns a separate stock-material
            // instance for this mesh, so shadow-layer cleanup cannot invalidate smoke rendering.
            return system == null ? null : system.CreateShadowMaterial();
        }

        private void CreateRenderer()
        {
            _shadowObject = new GameObject("PersistentSRBSmoke.VolumeTrailShadowLayer");
            DontDestroyOnLoad(_shadowObject);
            _meshFilter = _shadowObject.AddComponent<MeshFilter>();
            _meshRenderer = _shadowObject.AddComponent<MeshRenderer>();
            _mesh = new Mesh { name = "PersistentSRBSmoke.VolumeTrailShadowMesh" };
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private void UpdateShadowMesh(CelestialBody body, CelestialBody sun, IList<VolumeShadowSample> samples)
        {
            if (_mesh == null || samples == null || samples.Count == 0)
            {
                ClearMesh();
                return;
            }

            EnsureParentBody(body);
            Vector3 bodyCenter = body.transform.position;
            float bodyRadius = (float)body.Radius;
            float maximumRadius = bodyRadius + _settings.ShadowMaxAltitude;
            Vector3 lightTravelDirection = bodyCenter - sun.transform.position;
            if (lightTravelDirection.sqrMagnitude < 1f)
            {
                ClearMesh();
                return;
            }
            lightTravelDirection.Normalize();

            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();
            int emitted = 0;
            float sampleProbability = Mathf.Clamp01((_settings.ShadowMaxQuads * 1.25f) / Mathf.Max(1f, samples.Count));
            uint threshold = sampleProbability >= 0.9999f ? uint.MaxValue : (uint)(sampleProbability * uint.MaxValue);

            for (int i = 0; i < samples.Count && emitted < _settings.ShadowMaxQuads; i++)
            {
                VolumeShadowSample sample = samples[i];
                if (sample.Body != body || SrbSmokeMath.Hash(sample.Seed ^ 0xB5297A4DU) > threshold)
                    continue;
                Vector3 radial = sample.WorldCenter - bodyCenter;
                float radialMagnitude = radial.magnitude;
                if (radialMagnitude <= bodyRadius || radialMagnitude > maximumRadius || sample.Opacity <= 0.005f)
                    continue;

                Vector3 groundPoint;
                Vector3 groundNormal;
                float sunDot;
                if (!TryProjectToSurface(body, sample.WorldCenter, lightTravelDirection,
                    out groundPoint, out groundNormal, out sunDot) || sunDot <= 0.055f)
                    continue;

                float altitude = radialMagnitude - bodyRadius;
                float altitudeFade = 1f - Mathf.SmoothStep(0.62f, 1f,
                    Mathf.Clamp01(altitude / Mathf.Max(1f, _settings.ShadowMaxAltitude)));
                float opacity = Mathf.Clamp01(_settings.ShadowOpacity * sample.Opacity * altitudeFade
                    * Mathf.Lerp(0.65f, 1f, Mathf.Sqrt(sunDot)));
                if (opacity <= 0.003f)
                    continue;

                Vector3 along = Vector3.ProjectOnPlane(lightTravelDirection, groundNormal);
                if (along.sqrMagnitude < 0.001f)
                    along = Vector3.ProjectOnPlane(sample.Direction, groundNormal);
                if (along.sqrMagnitude < 0.001f)
                    along = Vector3.Cross(groundNormal, Vector3.right);
                along.Normalize();
                Vector3 across = Vector3.Cross(groundNormal, along).normalized;
                float stretch = Mathf.Clamp(1f / Mathf.Max(0.16f, sunDot), 1f, 3.2f);
                float halfWidth = Mathf.Max(0.5f, sample.Radius * _settings.ShadowSizeMultiplier * 0.5f);
                float halfLength = Mathf.Max(halfWidth, sample.Length * _settings.ShadowLengthMultiplier * stretch * 0.5f);
                AddShadowQuad(body, groundPoint + groundNormal * _settings.ShadowSurfaceOffset,
                    along, across, halfLength, halfWidth,
                    (byte)Mathf.Clamp(Mathf.RoundToInt(opacity * 255f), 0, 255));
                emitted++;
            }

            _mesh.Clear();
            if (_vertices.Count == 0)
                return;
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
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

        private void AddShadowQuad(CelestialBody body, Vector3 center, Vector3 along, Vector3 across,
            float halfLength, float halfWidth, byte alpha)
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
            _colors.Add(color); _colors.Add(color); _colors.Add(color); _colors.Add(color);
            _triangles.Add(baseIndex); _triangles.Add(baseIndex + 1); _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex); _triangles.Add(baseIndex + 2); _triangles.Add(baseIndex + 3);
        }

        private bool TryProjectToSurface(CelestialBody body, Vector3 source, Vector3 lightTravelDirection,
            out Vector3 groundPoint, out Vector3 groundNormal, out float sunDot)
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
            float distance = -b - root;
            if (distance <= 0f) distance = -b + root;
            if (distance <= 0f) return false;

            Vector3 roughPoint = source + lightTravelDirection * distance;
            double latitude = body.GetLatitude((Vector3d)roughPoint);
            double longitude = body.GetLongitude((Vector3d)roughPoint);
            double surfaceAltitude;
            if (!TryGetSurfaceAltitude(body, latitude, longitude, out surfaceAltitude))
                return false;
            groundPoint = body.GetWorldSurfacePosition(latitude, longitude, surfaceAltitude);
            groundNormal = ((Vector3)body.GetSurfaceNVector(latitude, longitude)).normalized;
            sunDot = Mathf.Clamp01(Vector3.Dot(groundNormal, -lightTravelDirection));
            return sunDot > 0f;
        }

        private bool TryGetSurfaceAltitude(CelestialBody body, double latitude, double longitude, out double surfaceAltitude)
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
            if (_terrainQueriesRemaining <= 0)
                return false;
            _terrainQueriesRemaining--;
            surfaceAltitude = QuerySurfaceAltitude(body, latitude, longitude);
            if (_terrainCache.Count >= _settings.ShadowTerrainCacheCapacity)
                TrimTerrainCache();
            _terrainCache[key] = new TerrainCacheEntry { SurfaceAltitude = surfaceAltitude, LastUsedFrame = _frameIndex };
            return true;
        }

        private double QuerySurfaceAltitude(CelestialBody body, double latitude, double longitude)
        {
            double latRadians = latitude * Math.PI / 180.0;
            double lonRadians = longitude * Math.PI / 180.0;
            Vector3d radial = new Vector3d(Math.Cos(latRadians) * Math.Cos(lonRadians),
                Math.Sin(latRadians), Math.Cos(latRadians) * Math.Sin(lonRadians));
            double altitude = body.pqsController.GetSurfaceHeight(radial) - body.pqsController.radius;
            if (double.IsNaN(altitude) || double.IsInfinity(altitude) || (body.ocean && altitude < 0.0))
                altitude = 0.0;
            return altitude;
        }

        private void GetTerrainCell(CelestialBody body, double latitude, double longitude, out int latCell, out int lonCell)
        {
            double degrees = Math.Max(1e-7, _settings.ShadowTerrainCacheMeters / Math.Max(1.0, body.Radius) * 180.0 / Math.PI);
            while (longitude < -180.0) longitude += 360.0;
            while (longitude >= 180.0) longitude -= 360.0;
            latCell = (int)Math.Floor((latitude + 90.0) / degrees);
            lonCell = (int)Math.Floor((longitude + 180.0) / degrees);
        }

        private static long PackTerrainKey(int latCell, int lonCell)
        {
            return ((long)latCell << 32) ^ (uint)lonCell;
        }

        private void TrimTerrainCache()
        {
            int staleFrame = _frameIndex - 600;
            List<long> staleKeys = new List<long>();
            foreach (KeyValuePair<long, TerrainCacheEntry> pair in _terrainCache)
                if (pair.Value.LastUsedFrame < staleFrame)
                    staleKeys.Add(pair.Key);
            for (int i = 0; i < staleKeys.Count; i++)
                _terrainCache.Remove(staleKeys[i]);
            if (_terrainCache.Count >= _settings.ShadowTerrainCacheCapacity)
                _terrainCache.Clear();
        }

        private void ClearMesh()
        {
            if (_mesh != null)
                _mesh.Clear();
        }

        private void OnDestroy()
        {
            _terrainCache.Clear();
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_shadowObject != null) Destroy(_shadowObject);
        }
    }
}
