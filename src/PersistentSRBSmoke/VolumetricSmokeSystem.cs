using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// CPU side of the KSA-style trail architecture: fixed body-relative Hermite records are the
    /// sole source of smoke. There are no ParticleSystem, Waterfall or EVE presentation paths.
    /// </summary>
    internal sealed class VolumetricSmokeSystem : IDisposable
    {
        private struct RenderCandidate
        {
            public TrailSegment Segment;
            public float DistanceSquared;
        }

        private readonly SmokeSettings _settings;
        private readonly TrailSegment[] _segments;
        private readonly int[] _activeListIndex;
        private readonly List<int> _activeSlots;
        private readonly Stack<int> _freeSlots;
        private readonly Dictionary<int, int> _nozzleSlots;
        private readonly Dictionary<SegmentCellKey, int> _mergeCells;
        private readonly PadVolumeField _padField;
        private readonly List<RenderCandidate> _nearCandidates;
        private readonly List<RenderCandidate> _midCandidates;
        private readonly List<RenderCandidate> _farCandidates;
        private readonly List<TrailSegment> _renderSegments;
        private readonly List<VolumeShadowSample> _shadowSamples;
        private readonly VolumetricSmokeRenderer _renderer;
        private uint _seedCounter = 1U;

        public VolumetricSmokeSystem(SmokeSettings settings)
        {
            _settings = settings;
            _segments = new TrailSegment[settings.MaxStoredSegments];
            _activeListIndex = new int[settings.MaxStoredSegments];
            _activeSlots = new List<int>(settings.MaxStoredSegments);
            _freeSlots = new Stack<int>(settings.MaxStoredSegments);
            _nozzleSlots = new Dictionary<int, int>();
            _mergeCells = new Dictionary<SegmentCellKey, int>();
            _padField = new PadVolumeField(settings);
            _nearCandidates = new List<RenderCandidate>(settings.VisibleNearSegments + 16);
            _midCandidates = new List<RenderCandidate>(settings.VisibleMidSegments + 16);
            _farCandidates = new List<RenderCandidate>(settings.VisibleFarSegments + 16);
            _renderSegments = new List<TrailSegment>(settings.MaxVisibleSegments + settings.PadTileCount);
            _shadowSamples = new List<VolumeShadowSample>(settings.ShadowMaxQuads + settings.PadTileCount);

            for (int i = settings.MaxStoredSegments - 1; i >= 0; i--)
            {
                _activeListIndex[i] = -1;
                _freeSlots.Push(i);
            }

            string failure;
            _renderer = VolumetricSmokeRenderer.TryCreate(settings, out failure);
            InitializationError = failure;
            IsAvailable = _renderer != null;
            if (IsAvailable)
                VolumetricSmokeRegistry.Current = this;
        }

        public bool IsAvailable { get; private set; }
        public string InitializationError { get; private set; }
        public int SegmentCount { get { return _activeSlots.Count; } }
        public bool IsVisible { get { return IsAvailable && _renderer.IsVisible; } }

        public void Inject(SrbSmokeInjection injection)
        {
            if (!IsAvailable || injection.Body == null || injection.OpticalMass <= 0.0001f)
                return;

            Vector3 direction = injection.ExhaustDirection;
            if (direction.sqrMagnitude < 0.001f)
                direction = -injection.Up;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.down;
            direction.Normalize();

            Vector3 up = injection.Up;
            if (up.sqrMagnitude < 0.001f)
                up = (injection.CurrentWorldPosition - injection.Body.transform.position).normalized;
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.up;
            up.Normalize();

            injection.ExhaustDirection = direction;
            injection.Up = up;
            UpdateNozzle(injection);
            _padField.Inject(injection);

            float segmentLength = Mathf.Max(0.5f, _settings.SegmentLength * injection.Profile.SpacingMultiplier);
            // A stationary source still deposits mass in the first segment. This is the guard
            // against the early volumetric-trail failure where a held-down vehicle made no smoke.
            int count = VolumetricTrailRules.CalculateInsertionCount(
                injection.Travel, segmentLength, injection.StationaryBlend, _settings.MaxSegmentsPerInjection);
            if (count <= 0)
                return;

            float massPerSegment = injection.OpticalMass / count;
            float radius = Mathf.Max(0.1f, injection.Radius);
            Vector3 start = injection.PreviousWorldPosition + direction * _settings.NozzleOffset;
            Vector3 end = injection.CurrentWorldPosition + direction * _settings.NozzleOffset;
            Vector3 tangent = end - start;
            if (tangent.sqrMagnitude < 0.001f)
                tangent = direction * Mathf.Max(radius * 1.35f, segmentLength * 0.25f);

            for (int i = 0; i < count; i++)
            {
                float t0 = i / (float)count;
                float t1 = (i + 1) / (float)count;
                Vector3 segmentStart = Vector3.Lerp(start, end, t0);
                Vector3 segmentEnd = Vector3.Lerp(start, end, t1);
                if ((segmentEnd - segmentStart).sqrMagnitude < 0.001f)
                    segmentEnd = segmentStart + tangent;

                TrailSegment segment = new TrailSegment
                {
                    Active = true,
                    Body = injection.Body,
                    EmitterId = injection.EmitterId,
                    VesselId = injection.VesselId,
                    Kind = SmokeSegmentKind.Trail,
                    StartRelative = segmentStart - injection.Body.transform.position,
                    EndRelative = segmentEnd - injection.Body.transform.position,
                    StartTangent = tangent,
                    EndTangent = tangent,
                    Velocity = injection.EmitterVelocity + injection.Wind
                        + direction * Mathf.Lerp(22f, 52f, injection.Thrust),
                    StartRadius = radius,
                    EndRadius = radius * Mathf.Lerp(1.05f, 1.26f, 1f - injection.Atmosphere),
                    OpticalMass = massPerSegment,
                    Temperature = 0.18f,
                    Age = 0f,
                    Lifetime = Mathf.Max(1f, injection.Lifetime),
                    Color = injection.SmokeColor,
                    Seed = SrbSmokeMath.Hash(_seedCounter++)
                };
                AddSegment(segment);
            }
        }

        public void AdvanceUniversalTime(float gameDeltaTime, float unityDeltaTime)
        {
            if (!IsAvailable || gameDeltaTime <= 0f)
                return;

            // Unlike Shuriken, this pool has no hidden Unity lifetime simulation. Universal Time is
            // authoritative for the whole age delta, so rails warp cannot freeze an old trail.
            float ageDelta = Mathf.Max(0f, gameDeltaTime);
            if (ageDelta >= _settings.TrailLifetime)
            {
                Clear();
                return;
            }

            for (int active = 0; active < _activeSlots.Count;)
            {
                int slot = _activeSlots[active];
                TrailSegment segment = _segments[slot];
                segment.Age = VolumetricTrailRules.AdvanceAge(segment.Age, ageDelta);
                if (VolumetricTrailRules.IsExpired(segment.Age, segment.Lifetime, segment.OpticalMass))
                {
                    RemoveSlot(slot);
                    continue;
                }
                _segments[slot] = segment;
                active++;
            }
        }

        public void UpdateDynamicMotion(
            CelestialBody activeBody,
            WindModel wind,
            double universalTime,
            float deltaTime,
            bool hasSurfaceReference,
            float surfaceReferenceAltitude)
        {
            if (!IsAvailable || deltaTime <= 0f)
                return;

            if (wind != null && activeBody != null)
                wind.Prepare(activeBody, universalTime);

            int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / Mathf.Max(0.05f, _settings.MaxWarpSimulationStep)));
            float step = deltaTime / steps;
            for (int stepIndex = 0; stepIndex < steps; stepIndex++)
            {
                AdvanceMotionStep(wind, universalTime, step, hasSurfaceReference, surfaceReferenceAltitude);
                _padField.Advance(activeBody, wind, universalTime, step);
            }

            // When slots are scarce, coarsen old trail topology first. The visible nozzle/core is
            // never a merge candidate and records from different vessels have distinct cell keys.
            Coarsen(Mathf.Max(8, _activeSlots.Count / 48));
        }

        public void LateUpdateRenderer()
        {
            if (!IsAvailable)
                return;

            BuildRenderList();
            _renderer.UploadSegments(_renderSegments);
            _renderer.LateUpdate();
        }

        public IList<VolumeShadowSample> GetShadowSamples()
        {
            _shadowSamples.Clear();
            for (int i = 0; i < _activeSlots.Count; i++)
            {
                TrailSegment segment = _segments[_activeSlots[i]];
                if (!segment.Active || segment.Body == null || segment.Kind == SmokeSegmentKind.Nozzle)
                    continue;
                float visibility = Mathf.Clamp01(segment.OpticalMass / Mathf.Max(0.001f, 20f));
                if (visibility <= 0.005f)
                    continue;

                Vector3 direction = segment.EndRelative - segment.StartRelative;
                float length = direction.magnitude;
                if (length < 0.01f)
                    direction = segment.Velocity;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.right;
                direction.Normalize();
                _shadowSamples.Add(new VolumeShadowSample
                {
                    Body = segment.Body,
                    WorldCenter = segment.GetWorldCenter(),
                    Direction = direction,
                    Radius = segment.Radius,
                    Length = Mathf.Max(segment.Radius, length),
                    Opacity = visibility * (1f - segment.NormalizedAge),
                    Color = segment.Color,
                    Seed = segment.Seed
                });
            }
            _padField.AppendShadowSamples(_shadowSamples);
            return _shadowSamples;
        }

        public void InvalidateHistory()
        {
            if (_renderer != null)
                _renderer.InvalidateHistory();
        }

        public Material CreateShadowMaterial()
        {
            return _renderer == null ? null : _renderer.CreateShadowMaterial();
        }

        public void Dispose()
        {
            if (VolumetricSmokeRegistry.Current == this)
                VolumetricSmokeRegistry.Current = null;
            if (_renderer != null)
                _renderer.Dispose();
            Clear();
            IsAvailable = false;
        }

        private void UpdateNozzle(SrbSmokeInjection injection)
        {
            int slot;
            if (!_nozzleSlots.TryGetValue(injection.EmitterId, out slot)
                || slot < 0 || slot >= _segments.Length || !_segments[slot].Active)
            {
                slot = AllocateSlot();
                if (slot < 0)
                    return;
                _nozzleSlots[injection.EmitterId] = slot;
                AddActiveSlot(slot);
            }

            Vector3 start = injection.CurrentWorldPosition - injection.ExhaustDirection * Mathf.Max(0.35f, _settings.NozzleOffset * 0.38f);
            Vector3 end = injection.CurrentWorldPosition + injection.ExhaustDirection * _settings.NozzleLength;
            Color coreColor = Color.Lerp(new Color(1f, 0.50f, 0.17f, 1f), injection.SmokeColor, 0.62f);
            _segments[slot] = new TrailSegment
            {
                Active = true,
                Body = injection.Body,
                EmitterId = injection.EmitterId,
                VesselId = injection.VesselId,
                Kind = SmokeSegmentKind.Nozzle,
                StartRelative = start - injection.Body.transform.position,
                EndRelative = end - injection.Body.transform.position,
                StartTangent = injection.ExhaustDirection * _settings.NozzleLength,
                EndTangent = injection.ExhaustDirection * _settings.NozzleLength,
                Velocity = injection.EmitterVelocity + injection.ExhaustDirection * Mathf.Lerp(35f, 80f, injection.Thrust),
                StartRadius = _settings.NozzleRadius * injection.Profile.SizeMultiplier,
                EndRadius = _settings.NozzleRadius * injection.Profile.SizeMultiplier * 1.72f,
                OpticalMass = injection.OpticalMass * 0.72f,
                Temperature = Mathf.Lerp(0.55f, 1f, injection.Thrust),
                Age = 0f,
                Lifetime = _settings.NozzleLifetime,
                Color = coreColor,
                Seed = SrbSmokeMath.Hash((uint)injection.EmitterId ^ _seedCounter++)
            };
        }

        private void AdvanceMotionStep(
            WindModel wind,
            double universalTime,
            float deltaTime,
            bool hasSurfaceReference,
            float surfaceReferenceAltitude)
        {
            for (int active = 0; active < _activeSlots.Count;)
            {
                int slot = _activeSlots[active];
                TrailSegment segment = _segments[slot];
                if (!segment.Active || segment.Body == null)
                {
                    RemoveSlot(slot);
                    continue;
                }

                Vector3 center = segment.GetWorldCenter();
                Vector3 radial = center - segment.Body.transform.position;
                float radialMagnitude = radial.magnitude;
                Vector3 up = radialMagnitude < 1f ? Vector3.up : radial / radialMagnitude;
                float altitude = Mathf.Max(0f, radialMagnitude - (float)segment.Body.Radius);
                float nearGround = hasSurfaceReference
                    ? Mathf.Clamp01((altitude - surfaceReferenceAltitude) / Mathf.Max(1f, _settings.NearGroundHoldHeight))
                    : 1f;
                Vector3 air = wind == null ? Vector3.zero : wind.GetWind(segment.Body, up, altitude, universalTime);
                float windBlend = Mathf.Lerp(_settings.NearGroundWindMultiplier, 1f, nearGround);
                float diffusionBlend = Mathf.Lerp(_settings.NearGroundDiffusionMultiplier, 1f, nearGround);
                float buoyancyBlend = Mathf.Lerp(_settings.NearGroundBuoyancyMultiplier, 1f, nearGround);
                float response = 1f - Mathf.Exp(-deltaTime * Mathf.Max(0.01f, _settings.DynamicWindResponse));

                Vector3 tangentA = Vector3.Cross(up, Vector3.forward);
                if (tangentA.sqrMagnitude < 0.001f)
                    tangentA = Vector3.Cross(up, Vector3.right);
                tangentA.Normalize();
                Vector3 tangentB = Vector3.Cross(up, tangentA).normalized;
                float noiseTime = (float)universalTime * _settings.TurbulenceFrequency;
                float n1 = Mathf.PerlinNoise(segment.Seed * 0.00091f + noiseTime, center.x * 0.0017f + center.z * 0.0004f) * 2f - 1f;
                float n2 = Mathf.PerlinNoise(segment.Seed * 0.00173f - noiseTime, center.z * 0.0013f + center.y * 0.0006f) * 2f - 1f;
                Vector3 turbulence = (tangentA * n1 + tangentB * n2) * _settings.TurbulenceStrength;
                Vector3 desiredVelocity = air * windBlend + up * (_settings.Buoyancy * buoyancyBlend) + turbulence;
                if (segment.Kind == SmokeSegmentKind.Nozzle)
                    desiredVelocity += segment.Velocity * 0.28f;
                segment.Velocity = Vector3.Lerp(segment.Velocity, desiredVelocity, response);

                Vector3 displacement = segment.Velocity * deltaTime;
                segment.StartRelative += displacement;
                segment.EndRelative += displacement;
                float normalizedAge = segment.NormalizedAge;
                float growth = (_settings.DiffusionSpeed + segment.Radius * _settings.RadiusGrowth * 0.045f)
                    * diffusionBlend * Mathf.Lerp(0.35f, 1f, normalizedAge);
                segment.StartRadius += growth * deltaTime;
                segment.EndRadius += growth * deltaTime * 1.08f;
                segment.OpticalMass = VolumetricTrailRules.DissipateMass(
                    segment.OpticalMass, _settings.DissipationRate, deltaTime, segment.Lifetime);
                segment.Temperature *= Mathf.Exp(-deltaTime * 2.8f);
                if (segment.OpticalMass < 0.0001f)
                {
                    RemoveSlot(slot);
                    continue;
                }
                _segments[slot] = segment;
                active++;
            }
        }

        private void BuildRenderList()
        {
            _nearCandidates.Clear();
            _midCandidates.Clear();
            _farCandidates.Clear();
            _renderSegments.Clear();
            Camera camera = VolumetricSmokeRenderer.FindBestCamera();
            Vector3 cameraPosition = camera == null ? Vector3.zero : camera.transform.position;
            float nearSquared = _settings.NearDistance * _settings.NearDistance;
            float midSquared = _settings.MidDistance * _settings.MidDistance;
            float farSquared = _settings.FarDistance * _settings.FarDistance;

            for (int i = 0; i < _activeSlots.Count; i++)
            {
                TrailSegment segment = _segments[_activeSlots[i]];
                if (!segment.Active || segment.Body == null)
                    continue;
                float distanceSquared = (segment.GetWorldCenter() - cameraPosition).sqrMagnitude;
                RenderCandidate candidate = new RenderCandidate { Segment = segment, DistanceSquared = distanceSquared };
                int lod = VolumetricTrailRules.SelectLodBucket(distanceSquared, nearSquared, midSquared,
                    farSquared, segment.Kind == SmokeSegmentKind.Nozzle);
                if (lod == 0)
                    _nearCandidates.Add(candidate);
                else if (lod == 1)
                    _midCandidates.Add(candidate);
                else if (lod == 2)
                    _farCandidates.Add(candidate);
            }

            _nearCandidates.Sort(CompareCandidates);
            _midCandidates.Sort(CompareCandidates);
            _farCandidates.Sort(CompareCandidates);
            AppendCandidates(_nearCandidates, _settings.VisibleNearSegments);
            AppendCandidates(_midCandidates, _settings.VisibleMidSegments);
            AppendCandidates(_farCandidates, _settings.VisibleFarSegments);
            _padField.AppendSegments(_renderSegments);
        }

        private static int CompareCandidates(RenderCandidate left, RenderCandidate right)
        {
            return left.DistanceSquared.CompareTo(right.DistanceSquared);
        }

        private void AppendCandidates(List<RenderCandidate> source, int maximum)
        {
            int count = Mathf.Min(maximum, source.Count);
            for (int i = 0; i < count; i++)
                _renderSegments.Add(source[i].Segment);
        }

        private void AddSegment(TrailSegment segment)
        {
            int slot = AllocateSlot();
            if (slot < 0)
                return;
            _segments[slot] = segment;
            AddActiveSlot(slot);
        }

        private int AllocateSlot()
        {
            if (_freeSlots.Count == 0)
                Coarsen(1);
            if (_freeSlots.Count == 0)
                EvictOldestTrail();
            return _freeSlots.Count == 0 ? -1 : _freeSlots.Pop();
        }

        private void AddActiveSlot(int slot)
        {
            _activeListIndex[slot] = _activeSlots.Count;
            _activeSlots.Add(slot);
        }

        private void RemoveSlot(int slot)
        {
            int listIndex = slot < 0 || slot >= _activeListIndex.Length ? -1 : _activeListIndex[slot];
            if (listIndex < 0)
                return;
            TrailSegment removed = _segments[slot];
            int lastListIndex = _activeSlots.Count - 1;
            int lastSlot = _activeSlots[lastListIndex];
            _activeSlots[listIndex] = lastSlot;
            _activeListIndex[lastSlot] = listIndex;
            _activeSlots.RemoveAt(lastListIndex);
            _activeListIndex[slot] = -1;
            _segments[slot] = new TrailSegment();
            _freeSlots.Push(slot);
            if (removed.Kind == SmokeSegmentKind.Nozzle)
            {
                int current;
                if (_nozzleSlots.TryGetValue(removed.EmitterId, out current) && current == slot)
                    _nozzleSlots.Remove(removed.EmitterId);
            }
        }

        private void Coarsen(int desiredMerges)
        {
            if (desiredMerges <= 0 || _activeSlots.Count < 2)
                return;
            _mergeCells.Clear();
            int merges = 0;
            for (int active = 0; active < _activeSlots.Count && merges < desiredMerges;)
            {
                int slot = _activeSlots[active];
                TrailSegment segment = _segments[slot];
                if (!segment.Active || segment.Kind != SmokeSegmentKind.Trail || segment.Age < _settings.MergeMinAge)
                {
                    active++;
                    continue;
                }

                SegmentCellKey key = new SegmentCellKey(segment.Body, segment.VesselId,
                    segment.CenterRelative, _settings.MergeCellSize);
                int destinationSlot;
                if (_mergeCells.TryGetValue(key, out destinationSlot)
                    && destinationSlot != slot
                    && _segments[destinationSlot].Active)
                {
                    TrailSegment destination = _segments[destinationSlot];
                    // The key already enforces body/vessel identity. Keeping this explicit makes
                    // cross-vessel mixing impossible if the key is ever changed.
                    if (destination.Body == segment.Body && destination.VesselId == segment.VesselId)
                    {
                        SmokeSegmentMath.Merge(ref destination, segment);
                        _segments[destinationSlot] = destination;
                        RemoveSlot(slot);
                        merges++;
                        continue;
                    }
                }
                else
                {
                    _mergeCells[key] = slot;
                }
                active++;
            }
        }

        private void EvictOldestTrail()
        {
            int oldestSlot = -1;
            float oldestAge = float.MinValue;
            for (int i = 0; i < _activeSlots.Count; i++)
            {
                int slot = _activeSlots[i];
                TrailSegment segment = _segments[slot];
                if (segment.Kind == SmokeSegmentKind.Nozzle || segment.Age <= oldestAge)
                    continue;
                oldestAge = segment.Age;
                oldestSlot = slot;
            }
            if (oldestSlot >= 0)
                RemoveSlot(oldestSlot);
        }

        private void Clear()
        {
            _activeSlots.Clear();
            _freeSlots.Clear();
            _nozzleSlots.Clear();
            for (int i = _segments.Length - 1; i >= 0; i--)
            {
                _segments[i] = new TrailSegment();
                _activeListIndex[i] = -1;
                _freeSlots.Push(i);
            }
            _padField.Clear();
        }
    }
}
