using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Optional runtime bridge to Waterfall's analytic volumetric proxy renderer.
    ///
    /// VolumetricVaporCones is a configuration-only mod: its volume is provided by the
    /// already-installed Waterfall shader and model. This class never redistributes those assets.
    /// It condenses the authoritative Shuriken smoke simulation into a bounded set of spatial
    /// cells and renders one analytic proxy for each cell. The particle system remains alive for
    /// motion, projected shadows and a completely safe fallback.
    /// </summary>
    internal sealed class WaterfallVolumetricLayer : IDisposable
    {
        private const string ShaderName = "Waterfall/Additive (Volumetric)";
        private const string ModelPath = "Waterfall/FX/fx-volumetric-simple";
        private const string NoiseTexturePath = "Waterfall/FX/fx-noise-1";

        private struct CellKey : IEquatable<CellKey>
        {
            public int X;
            public int Y;
            public int Z;

            public CellKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(CellKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey && Equals((CellKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X * 73856093;
                    hash ^= Y * 19349663;
                    hash ^= Z * 83492791;
                    return hash;
                }
            }
        }

        private struct CellAccumulator
        {
            public CellKey Key;
            public Vector3 RelativePositionSum;
            public Vector3 VelocitySum;
            public Vector3 ColorSum;
            public float BirthDiameterSum;
            public float RemainingLifetimeSum;
            public float StartLifetimeSum;
            public float Weight;
            public float AlphaSum;
            public int Count;
            public uint Seed;
        }

        private sealed class VolumeSlot
        {
            public GameObject GameObject;
            public Transform Transform;
            public Renderer Renderer;
            public MaterialPropertyBlock Properties;
            public CelestialBody Body;
            public Vector3 BodyRelativePosition;
            public Vector3 RelativeVelocity;
            public Quaternion Rotation;
            public double SnapshotClock;
            public float RemainingLifetime;
            public float StartLifetime;
            public float BirthDiameter;
            public float SizeMultiplier;
            public CellKey Key;
            public bool Active;
        }

        private readonly SmokeSettings _settings;
        private readonly Dictionary<CellKey, CellAccumulator> _cells =
            new Dictionary<CellKey, CellAccumulator>(512);
        private readonly List<CellAccumulator> _orderedCells = new List<CellAccumulator>(512);
        // Slots are deliberately retained by cell key.  Reassigning a proxy by list index makes
        // Waterfall's noise seed jump between unrelated parts of the plume every snapshot.
        private readonly Dictionary<CellKey, VolumeSlot> _slotsByKey =
            new Dictionary<CellKey, VolumeSlot>(512);
        private readonly HashSet<CellKey> _selectedKeys = new HashSet<CellKey>();

        private GameObject _root;
        private Material _material;
        private VolumeSlot[] _slots;
        private bool _initialized;
        private bool _disposed;
        private float _nextInitializationAttempt;
        private int _activeCount;
        private float _stableCellSize;
        private bool _cameraModeAllowsRendering;
        private bool _hasConfirmedVisibleVolume;
        private string _status = "not initialized";

        public bool IsAvailable { get { return _initialized && !_disposed; } }
        public bool HasActiveVolumes { get { return IsAvailable && _activeCount > 0; } }
        public bool CanDrivePresentation
        {
            get
            {
                return HasActiveVolumes && _cameraModeAllowsRendering && _hasConfirmedVisibleVolume;
            }
        }
        public int ActiveVolumeCount { get { return _activeCount; } }
        public string Status { get { return _status; } }

        public bool IsVisible
        {
            get
            {
                if (!HasActiveVolumes || !_cameraModeAllowsRendering || _slots == null)
                    return false;

                for (int i = 0; i < _activeCount; i++)
                {
                    Renderer renderer = _slots[i].Renderer;
                    if (renderer != null && renderer.enabled && renderer.isVisible)
                        return true;
                }

                return false;
            }
        }

        public WaterfallVolumetricLayer(SmokeSettings settings)
        {
            _settings = settings;
            if (_settings.WaterfallVolumetricEnabled)
                TryInitialize();
            else
                _status = "disabled in Settings.cfg";
        }

        public bool Capture(
            ParticleSystem.Particle[] particles,
            int count,
            CelestialBody body)
        {
            if (_disposed || !_settings.WaterfallVolumetricEnabled)
                return false;

            if (!_initialized)
            {
                float now = Time.realtimeSinceStartup;
                if (now < _nextInitializationAttempt || !TryInitialize())
                    return false;
            }

            if (particles == null || count <= 0 || body == null)
            {
                Clear();
                return true;
            }

            int maximumVolumes = Mathf.Max(8, _settings.WaterfallVolumetricMaxVolumes);
            float cellSize = SelectStableCellSize(count, maximumVolumes);
            BuildCells(particles, count, body, cellSize);

            _orderedCells.Clear();
            foreach (KeyValuePair<CellKey, CellAccumulator> pair in _cells)
                _orderedCells.Add(pair.Value);

            if (_orderedCells.Count > maximumVolumes)
            {
                _orderedCells.Sort(CompareCellImportance);
                _orderedCells.RemoveRange(maximumVolumes, _orderedCells.Count - maximumVolumes);
            }

            _selectedKeys.Clear();
            for (int i = 0; i < _orderedCells.Count; i++)
                _selectedKeys.Add(_orderedCells[i].Key);

            // Expire cells first so a newly selected cell can claim a free slot.  Swapping slots
            // in the dense active range is safe: the dictionary stores object references, not
            // array indices.
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                VolumeSlot slot = _slots[i];
                if (!_selectedKeys.Contains(slot.Key))
                {
                    _slotsByKey.Remove(slot.Key);
                    RemoveActiveSlotAt(i);
                }
            }

            double snapshotClock = GetSimulationClock();
            for (int i = 0; i < _orderedCells.Count; i++)
            {
                CellAccumulator cell = _orderedCells[i];
                VolumeSlot slot;
                if (!_slotsByKey.TryGetValue(cell.Key, out slot) || !slot.Active)
                {
                    _slotsByKey.Remove(cell.Key);
                    if (_activeCount >= _slots.Length)
                        break;

                    slot = _slots[_activeCount++];
                    slot.Key = cell.Key;
                    _slotsByKey[cell.Key] = slot;
                }

                ApplyCellToSlot(cell, slot, body, cellSize, snapshotClock);
            }
            PrepareFlightCamera();
            return true;
        }

        public void LateUpdate()
        {
            if (!HasActiveVolumes || _slots == null)
                return;

            _cameraModeAllowsRendering = IsSupportedCameraMode();
            SetActiveRenderersEnabled(_cameraModeAllowsRendering);
            if (!_cameraModeAllowsRendering)
            {
                _hasConfirmedVisibleVolume = false;
                return;
            }

            PrepareFlightCamera();
            double now = GetSimulationClock();
            int i = 0;
            while (i < _activeCount)
            {
                VolumeSlot slot = _slots[i];
                if (!slot.Active || slot.Body == null || slot.Transform == null)
                {
                    RemoveActiveSlotAt(i);
                    continue;
                }

                float elapsed = (float)Math.Max(0.0, Math.Min(10000.0, now - slot.SnapshotClock));
                float remaining = slot.RemainingLifetime - elapsed;
                if (remaining <= 0f)
                {
                    RemoveActiveSlotAt(i);
                    continue;
                }

                // Shuriken smoke is world-space. Follow only the body's FloatingOrigin translation;
                // applying the body's rotation here would make analytic cells slide across the
                // authoritative particle shell between 4-Hz snapshots.
                Vector3 centre = slot.Body.transform.position
                    + slot.BodyRelativePosition
                    + slot.RelativeVelocity * elapsed;
                float age = slot.StartLifetime <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(1f - remaining / slot.StartLifetime);
                float diameter = Mathf.Max(
                    1f,
                    slot.BirthDiameter
                    * EvaluateTrailExpansion(age, _settings.SizeGrowth)
                    * slot.SizeMultiplier
                    * _settings.WaterfallVolumetricSizeMultiplier);

                // fx-volumetric-simple spans local y=0..-1 and has a unit radial extent. Put its
                // centre on the aggregated smoke cell and scale it into a rounded capsule.
                float radius = diameter * 0.5f;
                float length = diameter * 0.95f;
                Vector3 headOffset = slot.Rotation * Vector3.up * (length * 0.5f);
                slot.Transform.position = centre + headOffset;
                slot.Transform.rotation = slot.Rotation;
                slot.Transform.localScale = new Vector3(radius, length, radius);
                if (slot.Renderer != null && slot.Renderer.isVisible)
                    _hasConfirmedVisibleVolume = true;
                i++;
            }
        }

        public void Clear()
        {
            if (_slots != null)
            {
                for (int i = 0; i < _activeCount; i++)
                    DisableSlot(_slots[i]);
            }

            _activeCount = 0;
            _cameraModeAllowsRendering = false;
            _hasConfirmedVisibleVolume = false;
            _cells.Clear();
            _orderedCells.Clear();
            _slotsByKey.Clear();
            _selectedKeys.Clear();
            _stableCellSize = 0f;
        }

        private bool TryInitialize()
        {
            if (_initialized || _disposed)
                return _initialized;

            _nextInitializationAttempt = Time.realtimeSinceStartup + 2f;

            try
            {
                Shader shader = FindWaterfallShader();
                if (shader == null)
                {
                    _status = "Waterfall volumetric shader is not loaded";
                    return false;
                }

                if (GameDatabase.Instance == null || !GameDatabase.Instance.ExistsModel(ModelPath))
                {
                    _status = "Waterfall volumetric proxy model is missing";
                    return false;
                }

                GameObject prefab = GameDatabase.Instance.GetModelPrefab(ModelPath);
                Texture2D noise = GameDatabase.Instance.GetTexture(NoiseTexturePath, false);
                if (prefab == null || noise == null)
                {
                    _status = "Waterfall volumetric model or noise texture is unavailable";
                    return false;
                }

                _material = new Material(shader);
                _material.name = "PersistentSRBSmoke.WaterfallAnalyticVolume";
                if (!_material.HasProperty("_MainTex") || !_material.HasProperty("_Brightness"))
                    throw new InvalidOperationException("Unexpected Waterfall volumetric shader revision.");

                _material.SetTexture("_MainTex", noise);
                SetFloat(_material, "_ExpandLinear", 0f);
                SetFloat(_material, "_ExpandSquare", 0.18f);
                SetFloat(_material, "_FadeIn", 0.08f);
                SetFloat(_material, "_FadeOut", 0.34f);
                SetFloat(_material, "_Falloff", 0.16f);
                SetFloat(_material, "_FalloffStart", -0.46f);
                SetFloat(_material, "_Fresnel", 0.58f);
                SetFloat(_material, "_FresnelFadeIn", 0.14f);
                SetFloat(_material, "_FresnelInvert", 0.16f);
                SetFloat(_material, "_TintFalloff", 0.26f);
                SetFloat(_material, "_TintFresnel", 0.34f);
                SetFloat(_material, "_Noise", _settings.WaterfallVolumetricNoise);
                SetFloat(_material, "_NoiseFresnel", 1.75f);
                SetFloat(_material, "_SpeedX", 4.5f);
                SetFloat(_material, "_SpeedY", 18f);
                SetFloat(_material, "_TileX", 0.72f);
                SetFloat(_material, "_TileY", 1.35f);
                SetFloat(_material, "_LengthBrightness", 0.82f);
                SetFloat(_material, "_ClipBrightness", 0.92f);
                _material.renderQueue = 3001;

                _root = new GameObject("PersistentSRBSmoke.WaterfallVolumes");
                UnityEngine.Object.DontDestroyOnLoad(_root);

                int slotCount = Mathf.Clamp(_settings.WaterfallVolumetricMaxVolumes, 8, 1024);
                _slots = new VolumeSlot[slotCount];
                for (int i = 0; i < slotCount; i++)
                    _slots[i] = CreateSlot(prefab, i);

                _initialized = true;
                _status = "Waterfall analytic volume bridge";
                Debug.Log(
                    "[PersistentSRBSmoke] Waterfall volumetric layer ready: " + slotCount +
                    " bounded analytic proxies; Waterfall assets are referenced at runtime only.");
                return true;
            }
            catch (Exception ex)
            {
                _status = "Waterfall volumetric initialization failed: " + ex.GetType().Name;
                Debug.LogWarning("[PersistentSRBSmoke] " + _status + ": " + ex.Message);
                ReleaseUnityObjects();
                return false;
            }
        }

        private VolumeSlot CreateSlot(GameObject prefab, int index)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "PersistentSRBSmoke.WaterfallVolume." + index;
            instance.transform.SetParent(_root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            SetLayerRecursive(instance.transform, 1);
            instance.SetActive(true);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                throw new InvalidOperationException("Waterfall proxy model has no renderer.");

            Renderer primary = renderers[0];
            primary.sharedMaterial = _material;
            ConfigureCheapRendererFeatures(primary);
            for (int i = 1; i < renderers.Length; i++)
                renderers[i].enabled = false;
            primary.enabled = false;

            return new VolumeSlot
            {
                GameObject = instance,
                Transform = instance.transform,
                Renderer = primary,
                Properties = new MaterialPropertyBlock()
            };
        }

        private void BuildCells(
            ParticleSystem.Particle[] particles,
            int count,
            CelestialBody body,
            float cellSize)
        {
            _cells.Clear();
            Vector3 bodyCenter = body.transform.position;
            float inverseCellSize = 1f / Mathf.Max(1f, cellSize);

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = particles[i];
                if (particle.remainingLifetime <= 0f || particle.startLifetime <= 0.001f)
                    continue;

                float age = Mathf.Clamp01(1f - particle.remainingLifetime / particle.startLifetime);
                Color32 startColor32 = particle.startColor;
                Color startColor = startColor32;
                float alpha = Mathf.Clamp01(startColor.a * EvaluateTrailAlpha(age));
                if (alpha <= 0.004f)
                    continue;

                Vector3 relative = particle.position - bodyCenter;
                CellKey key = new CellKey(
                    Mathf.FloorToInt(relative.x * inverseCellSize),
                    Mathf.FloorToInt(relative.y * inverseCellSize),
                    Mathf.FloorToInt(relative.z * inverseCellSize));

                CellAccumulator cell;
                if (!_cells.TryGetValue(key, out cell))
                {
                    cell = new CellAccumulator
                    {
                        Key = key,
                        Seed = HashKey(key)
                    };
                }

                float weight = Mathf.Max(0.02f, alpha);
                cell.RelativePositionSum += relative * weight;
                cell.VelocitySum += particle.velocity * weight;
                cell.ColorSum += new Vector3(startColor.r, startColor.g, startColor.b) * weight;
                cell.BirthDiameterSum += Mathf.Max(0.5f, particle.startSize) * weight;
                cell.RemainingLifetimeSum += particle.remainingLifetime * weight;
                cell.StartLifetimeSum += particle.startLifetime * weight;
                cell.Weight += weight;
                cell.AlphaSum += alpha;
                cell.Count++;
                _cells[key] = cell;
            }
        }

        private float SelectStableCellSize(int particleCount, int maximumVolumes)
        {
            float baseCellSize = Mathf.Max(12f, _settings.WaterfallVolumetricCellSize);
            float occupancy = particleCount / Mathf.Max(1f, maximumVolumes * 80f);
            float desired = baseCellSize * Mathf.Clamp(occupancy, 1f, 4f);

            if (_stableCellSize <= 0f)
            {
                _stableCellSize = QuantizeCellSize(baseCellSize, desired);
            }
            else if (desired > _stableCellSize * 1.25f || desired < _stableCellSize * 0.70f)
            {
                // LOD changes are intentionally hysteretic and quantized.  A continuously
                // changing grid was re-binning the entire trail four times per second.
                float nextSize = QuantizeCellSize(baseCellSize, desired);
                if (!Mathf.Approximately(nextSize, _stableCellSize))
                {
                    _stableCellSize = nextSize;
                    ClearSlotAssignments();
                }
            }

            return _stableCellSize;
        }

        private static float QuantizeCellSize(float baseCellSize, float desired)
        {
            float ratio = Mathf.Max(1f, desired / Mathf.Max(0.001f, baseCellSize));
            // Half-octave steps retain a useful trail length while avoiding constant remapping.
            float halfOctaves = Mathf.Round(Mathf.Log(ratio, 2f) * 2f) * 0.5f;
            return baseCellSize * Mathf.Pow(2f, Mathf.Clamp(halfOctaves, 0f, 2f));
        }

        private void ClearSlotAssignments()
        {
            for (int i = 0; i < _activeCount; i++)
                DisableSlot(_slots[i]);

            _activeCount = 0;
            _slotsByKey.Clear();
        }

        private void ApplyCellToSlot(
            CellAccumulator cell,
            VolumeSlot slot,
            CelestialBody body,
            float cellSize,
            double snapshotClock)
        {
            float inverseWeight = 1f / Mathf.Max(0.001f, cell.Weight);
            Vector3 relativePosition = cell.RelativePositionSum * inverseWeight;
            Vector3 velocity = cell.VelocitySum * inverseWeight;
            Vector3 rgb = cell.ColorSum * inverseWeight;
            float averageAlpha = cell.AlphaSum / Mathf.Max(1, cell.Count);

            slot.Body = body;
            slot.BodyRelativePosition = relativePosition;
            slot.RelativeVelocity = velocity;
            slot.SnapshotClock = snapshotClock;
            slot.RemainingLifetime = cell.RemainingLifetimeSum * inverseWeight;
            slot.StartLifetime = cell.StartLifetimeSum * inverseWeight;
            slot.BirthDiameter = cell.BirthDiameterSum * inverseWeight;

            float occupancy = Mathf.Max(1f, cell.Count);
            float cellCoverage = Mathf.Clamp01(
                slot.BirthDiameter * slot.BirthDiameter /
                Mathf.Max(1f, cellSize * cellSize));
            slot.SizeMultiplier = Mathf.Clamp(
                0.82f + Mathf.Pow(occupancy, 0.18f) * 0.24f + cellCoverage * 0.16f,
                0.90f,
                1.85f);
            slot.Rotation = CreateStableRotation(cell.Seed);

            float integratedDensity = 1f - Mathf.Exp(-cell.AlphaSum * 0.11f);
            float brightness = _settings.WaterfallVolumetricBrightness
                * Mathf.Lerp(averageAlpha, 1f, integratedDensity)
                * Mathf.Lerp(0.62f, 1f, integratedDensity);
            Color litColor = new Color(
                Mathf.Clamp01(rgb.x),
                Mathf.Clamp01(rgb.y),
                Mathf.Clamp01(rgb.z),
                1f);
            Color coreColor = new Color(
                litColor.r * 0.70f,
                litColor.g * 0.72f,
                litColor.b * 0.75f,
                1f);

            slot.Properties.Clear();
            slot.Properties.SetFloat("_Seed", HashToUnit(cell.Seed) * 20f - 10f);
            slot.Properties.SetFloat("_Brightness", Mathf.Clamp(brightness, 0.01f, 2f));
            slot.Properties.SetColor("_StartTint", litColor);
            slot.Properties.SetColor("_EndTint", coreColor);
            slot.Renderer.SetPropertyBlock(slot.Properties);
            slot.Renderer.enabled = _cameraModeAllowsRendering;
            slot.Active = true;
        }

        private static int CompareCellImportance(CellAccumulator a, CellAccumulator b)
        {
            float aScore = a.AlphaSum * Mathf.Sqrt(Mathf.Max(1, a.Count));
            float bScore = b.AlphaSum * Mathf.Sqrt(Mathf.Max(1, b.Count));
            int scoreOrder = bScore.CompareTo(aScore);
            if (scoreOrder != 0)
                return scoreOrder;
            return HashKey(a.Key).CompareTo(HashKey(b.Key));
        }

        private static Shader FindWaterfallShader()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                return shader;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (!string.Equals(assembly.GetName().Name, "Waterfall", StringComparison.OrdinalIgnoreCase))
                    continue;

                Type loader = assembly.GetType("Waterfall.ShaderLoader", false);
                if (loader == null)
                    continue;

                MethodInfo getShader = loader.GetMethod(
                    "GetShader",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                if (getShader != null)
                    return getShader.Invoke(null, new object[] { ShaderName }) as Shader;
            }

            return null;
        }

        private static Quaternion CreateStableRotation(uint seed)
        {
            float x = HashToUnit(seed ^ 0xA511E9B3U) * 360f;
            float y = HashToUnit(seed ^ 0x63D83595U) * 360f;
            float z = HashToUnit(seed ^ 0xB5297A4DU) * 360f;
            return Quaternion.Euler(x, y, z);
        }

        private double GetSimulationClock()
        {
            if (!_settings.FollowUniversalTime)
                return Time.time;

            try
            {
                return Planetarium.GetUniversalTime();
            }
            catch
            {
                return Time.time;
            }
        }

        private static bool IsSupportedCameraMode()
        {
            if (!HighLogic.LoadedSceneIsFlight || MapView.MapIsEnabled)
                return false;

            try
            {
                CameraManager manager = CameraManager.Instance;
                if (manager == null)
                    return true;

                CameraManager.CameraMode mode = manager.currentCameraMode;
                return mode != CameraManager.CameraMode.IVA &&
                    mode != CameraManager.CameraMode.Internal &&
                    mode != CameraManager.CameraMode.Map;
            }
            catch
            {
                return true;
            }
        }

        private void SetActiveRenderersEnabled(bool enabled)
        {
            if (_slots == null)
                return;

            for (int i = 0; i < _activeCount; i++)
            {
                VolumeSlot slot = _slots[i];
                if (slot != null && slot.Renderer != null)
                    slot.Renderer.enabled = enabled && slot.Active;
            }
        }

        private static void PrepareFlightCamera()
        {
            try
            {
                Camera camera = FlightCamera.fetch == null ? null : FlightCamera.fetch.mainCamera;
                if (camera == null)
                    camera = Camera.main;
                if (camera != null)
                    camera.depthTextureMode |= DepthTextureMode.Depth;
            }
            catch
            {
                Camera camera = Camera.main;
                if (camera != null)
                    camera.depthTextureMode |= DepthTextureMode.Depth;
            }
        }

        private static void ConfigureCheapRendererFeatures(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = true;
        }

        private static void SetLayerRecursive(Transform transform, int layer)
        {
            if (transform == null)
                return;
            transform.gameObject.layer = layer;
            for (int i = 0; i < transform.childCount; i++)
                SetLayerRecursive(transform.GetChild(i), layer);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
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

        private static float EvaluateTrailExpansion(float age, float growth)
        {
            const float response = 0.25f;
            const float birthScale = 0.60f;
            age = Mathf.Clamp01(age);
            growth = Mathf.Max(1f, growth);
            float denominator = 1f - Mathf.Exp(-1f / response);
            float normalized = (1f - Mathf.Exp(-age / response)) / denominator;
            return birthScale + (growth - birthScale) * normalized;
        }

        private static uint HashKey(CellKey key)
        {
            unchecked
            {
                uint value = (uint)(key.X * 73856093);
                value ^= (uint)(key.Y * 19349663);
                value ^= (uint)(key.Z * 83492791);
                value ^= value >> 16;
                value *= 0x7FEB352DU;
                value ^= value >> 15;
                return value;
            }
        }

        private static float HashToUnit(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352DU;
                value ^= value >> 15;
                value *= 0x846CA68BU;
                value ^= value >> 16;
                return (value & 0x00FFFFFFU) / 16777215f;
            }
        }

        private static void DisableSlot(VolumeSlot slot)
        {
            if (slot == null)
                return;
            slot.Active = false;
            slot.Body = null;
            if (slot.Renderer != null)
                slot.Renderer.enabled = false;
        }

        private void RemoveActiveSlotAt(int index)
        {
            if (_slots == null || index < 0 || index >= _activeCount)
                return;

            VolumeSlot removedSlot = _slots[index];
            _slotsByKey.Remove(removedSlot.Key);
            DisableSlot(removedSlot);
            int last = _activeCount - 1;
            _activeCount = last;
            if (index < last)
            {
                VolumeSlot removed = _slots[index];
                _slots[index] = _slots[last];
                _slots[last] = removed;
            }
        }

        private void ReleaseUnityObjects()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
            }
            _slots = null;
            _activeCount = 0;
            _slotsByKey.Clear();
            _selectedKeys.Clear();
            _initialized = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Clear();
            ReleaseUnityObjects();
        }
    }
}
