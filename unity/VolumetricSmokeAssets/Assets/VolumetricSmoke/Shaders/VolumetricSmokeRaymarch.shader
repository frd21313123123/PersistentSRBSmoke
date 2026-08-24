Shader "PersistentSRBSmoke/VolumetricSmokeRaymarch"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
        _ShapeNoise ("Shape noise", 3D) = "gray" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            struct SegmentGpuData
            {
                float4 StartRadius;
                float4 EndRadius;
                float4 StartTangentMass;
                float4 EndTangentTemperature;
                float4 VelocityAge;
                float4 Color;
                float4 Metadata;
                float4 Bounds;
            };

            StructuredBuffer<SegmentGpuData> _SegmentData;
            StructuredBuffer<uint> _TileCounts;
            StructuredBuffer<uint> _TileIndices;
            sampler2D _CameraDepthTexture;
            sampler3D _ShapeNoise;
            float4 _ShapeNoise_ST;
            float4 _RaymarchResolution;
            float4 _SunDirection;
            float4 _SunTint;
            int _SegmentCount;
            int _TileSize;
            int _MaxTileCandidates;
            int _TileColumns;
            int _TileRows;
            int _NearViewSamples;
            int _MidViewSamples;
            int _FarViewSamples;
            int _SunShadowSamples;
            float _Extinction;
            float _Scattering;
            float _AmbientLight;
            float _SunLight;
            float _NoiseScale;
            float _NoiseStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 CameraRay(float2 uv)
            {
                float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
                float4 view = mul(unity_CameraInvProjection, clip);
                view.xyz /= max(0.0001, view.w);
                return normalize(mul((float3x3)unity_CameraToWorld, view.xyz));
            }

            float CapsuleDistance(float3 position, float3 start, float3 end, float startRadius, float endRadius)
            {
                float3 axis = end - start;
                float axisLengthSquared = max(0.0001, dot(axis, axis));
                float t = saturate(dot(position - start, axis) / axisLengthSquared);
                float radius = lerp(startRadius, endRadius, t);
                return length(position - lerp(start, end, t)) - radius;
            }

            float3 HermitePosition(SegmentGpuData segment, float t)
            {
                float t2 = t * t;
                float t3 = t2 * t;
                float h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
                float h10 = t3 - 2.0 * t2 + t;
                float h01 = -2.0 * t3 + 3.0 * t2;
                float h11 = t3 - t2;
                return h00 * segment.StartRadius.xyz
                    + h10 * segment.StartTangentMass.xyz
                    + h01 * segment.EndRadius.xyz
                    + h11 * segment.EndTangentTemperature.xyz;
            }

            float HermiteDistance(SegmentGpuData segment, float3 position)
            {
                // Four capsules form a conservative distance approximation to a cubic Hermite
                // centreline. This keeps curve continuity without expanding trail topology into
                // visual billboards or requiring a per-pixel root solve.
                float minimumDistance = 100000.0;
                float3 previous = HermitePosition(segment, 0.0);
                [unroll]
                for (int piece = 1; piece <= 4; piece++)
                {
                    float t0 = (piece - 1) * 0.25;
                    float t1 = piece * 0.25;
                    float3 current = HermitePosition(segment, t1);
                    minimumDistance = min(minimumDistance, CapsuleDistance(position, previous, current,
                        lerp(segment.StartRadius.w, segment.EndRadius.w, t0),
                        lerp(segment.StartRadius.w, segment.EndRadius.w, t1)));
                    previous = current;
                }
                return minimumDistance;
            }

            float SegmentDensity(SegmentGpuData segment, float3 position)
            {
                float distance = HermiteDistance(segment, position);
                float radius = max(0.05, max(segment.StartRadius.w, segment.EndRadius.w));
                float edge = saturate(1.0 - max(0.0, distance) / radius);
                edge *= edge * (3.0 - 2.0 * edge);
                float lengthValue = max(1.0, length(segment.EndRadius.xyz - segment.StartRadius.xyz));
                float massDensity = segment.StartTangentMass.w / max(1.0, radius * radius * lengthValue);
                float noise = tex3D(_ShapeNoise, position * _NoiseScale + segment.Metadata.y * 0.00000006).r;
                noise = lerp(1.0 - _NoiseStrength, 1.0, noise);
                float ageFade = saturate(1.0 - segment.VelocityAge.w * segment.VelocityAge.w);
                return edge * massDensity * noise * ageFade;
            }

            float DensityAt(float3 position, uint tile, uint candidateCount)
            {
                float density = 0.0;
                [loop]
                for (uint candidate = 0; candidate < candidateCount; candidate++)
                {
                    uint index = _TileIndices[tile * _MaxTileCandidates + candidate];
                    if (index >= _SegmentCount)
                        continue;
                    density += SegmentDensity(_SegmentData[index], position);
                }
                return min(density, 8.0);
            }

            bool RaySphereInterval(float3 rayOrigin, float3 rayDirection, float3 center, float radius,
                out float entry, out float exit)
            {
                float3 offset = rayOrigin - center;
                float b = dot(offset, rayDirection);
                float c = dot(offset, offset) - radius * radius;
                float discriminant = b * b - c;
                if (discriminant < 0.0)
                {
                    entry = 0.0;
                    exit = 0.0;
                    return false;
                }
                float root = sqrt(discriminant);
                entry = -b - root;
                exit = -b + root;
                return exit > 0.0;
            }

            float TwoLobePhase(float cosine)
            {
                float g0 = 0.65;
                float g1 = -0.28;
                float p0 = (1.0 - g0 * g0) / pow(max(0.001, 1.0 + g0 * g0 - 2.0 * g0 * cosine), 1.5);
                float p1 = (1.0 - g1 * g1) / pow(max(0.001, 1.0 + g1 * g1 - 2.0 * g1 * cosine), 1.5);
                return p0 * 0.78 + p1 * 0.22;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.uv);
                float maxDistance = max(1.0, LinearEyeDepth(rawDepth));
                float3 ray = CameraRay(input.uv);
                float2 pixel = input.uv * _ScreenParams.xy;
                uint tileColumns = max(1u, (uint)_TileColumns);
                uint tileRows = max(1u, (uint)_TileRows);
                uint tileX = min(tileColumns - 1, (uint)(pixel.x / _TileSize));
                uint tileY = min(tileRows - 1, (uint)(pixel.y / _TileSize));
                uint tile = tileX + tileY * tileColumns;
                uint candidateCount = min((uint)_MaxTileCandidates, _TileCounts[tile]);
                if (candidateCount == 0)
                    return float4(0, 0, 0, 1);

                float firstVolume = maxDistance;
                float lastVolume = 0.0;
                [loop]
                for (uint candidate = 0; candidate < candidateCount; candidate++)
                {
                    uint index = _TileIndices[tile * _MaxTileCandidates + candidate];
                    if (index >= _SegmentCount)
                        continue;
                    float entry;
                    float exit;
                    SegmentGpuData segment = _SegmentData[index];
                    if (RaySphereInterval(_WorldSpaceCameraPos, ray, segment.Bounds.xyz, segment.Bounds.w, entry, exit))
                    {
                        firstVolume = min(firstVolume, max(0.0, entry));
                        lastVolume = max(lastVolume, min(maxDistance, exit));
                    }
                }
                if (lastVolume <= firstVolume)
                    return float4(0, 0, 0, 1);

                float marchedDistance = lastVolume - firstVolume;
                int samples = marchedDistance < 350.0 ? _NearViewSamples : (marchedDistance < 1800.0 ? _MidViewSamples : _FarViewSamples);
                samples = clamp(samples, 1, 24);
                float stepLength = marchedDistance / samples;
                float transmittance = 1.0;
                float3 scattering = 0.0;
                float phase = TwoLobePhase(dot(-ray, normalize(_SunDirection.xyz)));
                [loop]
                for (int step = 0; step < 24; step++)
                {
                    if (step >= samples || transmittance < 0.01)
                        break;
                    float jitter = frac(sin(dot(input.uv * _ScreenParams.xy + step, float2(12.9898, 78.233))) * 43758.5453);
                    float distance = firstVolume + (step + jitter) * stepLength;
                    float3 position = _WorldSpaceCameraPos + ray * distance;
                    float density = DensityAt(position, tile, candidateCount);
                    if (density <= 0.0001)
                        continue;
                    float shadowDensity = 0.0;
                    [loop]
                    for (int sunStep = 0; sunStep < 4; sunStep++)
                    {
                        if (sunStep >= _SunShadowSamples)
                            break;
                        shadowDensity += DensityAt(position + normalize(_SunDirection.xyz) * ((sunStep + 1) * 5.0), tile, candidateCount);
                    }
                    float sunVisibility = exp(-shadowDensity * _Extinction * 0.35);
                    float sigma = density * _Extinction;
                    float absorb = exp(-sigma * stepLength);
                    float3 localLight = _AmbientLight.xxx + _SunTint.rgb * (_SunLight * sunVisibility * phase);
                    scattering += transmittance * (1.0 - absorb) * localLight * _Scattering;
                    transmittance *= absorb;
                }
                return float4(scattering, transmittance);
            }
            ENDCG
        }
    }
}
