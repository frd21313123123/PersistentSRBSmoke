Shader "PersistentSRBSmoke/VolumetricSmoke"
{
    Properties
    {
        _SunColor ("Sun Color", Color) = (1,1,1,1)
        _SkyAmbientColor ("Sky Ambient", Color) = (0.5,0.65,0.9,1)
        _GroundBounceColor ("Ground Bounce", Color) = (0.35,0.32,0.25,1)
        _SunIntensity ("Sun Intensity", Float) = 1.1
        _AmbientIntensity ("Ambient Intensity", Float) = 0.46
        _SunTransmittance ("Atmosphere Transmittance", Float) = 1
        _PhaseGForward ("HG Forward", Float) = 0.85
        _PhaseGBackward ("HG Backward", Float) = -0.35
        _MultipleScattering ("Multiple Scattering", Float) = 0.55
        _BeerPowder ("Beer Powder", Float) = 0.72
        _SoftDepthFactor ("Soft Depth", Float) = 1.65
        _RaySteps ("Ray Steps", Float) = 24
        _ShadowSteps ("Shadow Steps", Float) = 4
        _DensityMultiplier ("Density", Float) = 1.15
        _Extinction ("Extinction", Float) = 2.1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back
        ColorMask RGB

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _SunColor;
            float4 _SkyAmbientColor;
            float4 _GroundBounceColor;
            float4 _SunDir;
            float4 _PlanetUp;
            float _SunIntensity;
            float _AmbientIntensity;
            float _SunTransmittance;
            float _PhaseGForward;
            float _PhaseGBackward;
            float _MultipleScattering;
            float _BeerPowder;
            float _SoftDepthFactor;
            float _RaySteps;
            float _ShadowSteps;
            float _DensityMultiplier;
            float _Extinction;
            sampler2D _CameraDepthTexture;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeParams)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 projPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                return o;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash31(i + float3(0,0,0));
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float fbm(float3 p)
            {
                float value = 0;
                float amp = 0.55;
                value += noise3(p) * amp;
                p = p * 2.03 + 17.1; amp *= 0.50;
                value += noise3(p) * amp;
                p = p * 2.01 + 11.7; amp *= 0.50;
                value += noise3(p) * amp;
                p = p * 2.07 + 7.3; amp *= 0.50;
                value += noise3(p) * amp;
                return value;
            }

            float densityAt(float3 p, float age, float seed)
            {
                float radial = saturate(1.0 - length(p * 1.72));
                radial = radial * radial * (3.0 - 2.0 * radial);

                float3 warp = float3(seed * 19.7, seed * 7.3, seed * 13.1);
                float n = fbm(p * 3.35 + warp + age * float3(0.08, 0.16, 0.05));
                float detail = noise3(p * 11.0 + warp * 0.37 + age * 0.31);
                float eroded = smoothstep(0.25, 0.78, n + radial * 0.62 - detail * 0.17);
                return saturate(radial * eroded * _DensityMultiplier);
            }

            float hg(float cosTheta, float g)
            {
                g = clamp(g, -0.95, 0.95);
                float gg = g * g;
                return (1.0 - gg) / pow(max(0.0001, 1.0 + gg - 2.0 * g * cosTheta), 1.5);
            }

            float boxExitDistance(float3 p, float3 d)
            {
                float3 safeD = sign(d) * max(abs(d), 0.0001);
                float3 t1 = (-0.5 - p) / safeD;
                float3 t2 = ( 0.5 - p) / safeD;
                float3 tfar = max(t1, t2);
                return max(0.0, min(tfar.x, min(tfar.y, tfar.z)));
            }

            float shadowTransmittance(float3 p, float3 sunDirLocal, float age, float seed)
            {
                const int MAX_SHADOW_STEPS = 6;
                int requested = (int)clamp(_ShadowSteps, 1.0, (float)MAX_SHADOW_STEPS);
                float travel = boxExitDistance(p, sunDirLocal);
                float stepLength = travel / max(1, requested);
                float optical = 0;
                float3 samplePos = p + sunDirLocal * (stepLength * 0.5);

                [loop]
                for (int s = 0; s < MAX_SHADOW_STEPS; s++)
                {
                    if (s >= requested) break;
                    optical += densityAt(samplePos, age, seed) * stepLength;
                    samplePos += sunDirLocal * stepLength;
                }
                return exp(-optical * _Extinction * 1.35);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 smokeColor = UNITY_ACCESS_INSTANCED_PROP(Props, _SmokeColor);
                float4 smokeParams = UNITY_ACCESS_INSTANCED_PROP(Props, _SmokeParams);
                float age = smokeParams.x;
                float seed = smokeParams.y;

                float3 localCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                float3 rayDirLocal = normalize(i.localPos - localCam);
                float rayLength = boxExitDistance(i.localPos, rayDirLocal);
                if (rayLength <= 0.0001) discard;

                float3 rayDirWorld = normalize(mul((float3x3)unity_ObjectToWorld, rayDirLocal));
                float3 sunDirWorld = normalize(_SunDir.xyz);
                float3 sunDirLocal = normalize(mul((float3x3)unity_WorldToObject, sunDirWorld));
                float3 viewToCamera = -rayDirWorld;
                float cosTheta = clamp(dot(-sunDirWorld, viewToCamera), -1.0, 1.0);
                float phase = hg(cosTheta, _PhaseGForward) * 0.82 + hg(cosTheta, _PhaseGBackward) * 0.18;
                phase = clamp(phase, 0.10, 8.0);

                const int MAX_RAY_STEPS = 32;
                int steps = (int)clamp(_RaySteps, 8.0, (float)MAX_RAY_STEPS);
                float stepLength = rayLength / steps;
                float jitter = hash31(float3(i.pos.xy, seed * 991.0));
                float3 p = i.localPos + rayDirLocal * (stepLength * (0.18 + jitter * 0.64));

                float transmittance = 1.0;
                float3 accum = 0;
                float accumulatedAlpha = 0;

                [loop]
                for (int s = 0; s < MAX_RAY_STEPS; s++)
                {
                    if (s >= steps || transmittance < 0.015) break;

                    float density = densityAt(p, age, seed);
                    if (density > 0.008)
                    {
                        float shadow = shadowTransmittance(p, sunDirLocal, age, seed);
                        float sun = _SunIntensity * _SunTransmittance * phase * shadow;

                        float upTerm = saturate(dot(normalize(p + 0.0001), normalize(_PlanetUp.xyz)) * 0.5 + 0.5);
                        float3 ambient = _SkyAmbientColor.rgb * _AmbientIntensity * (0.68 + 0.32 * upTerm);
                        ambient += _GroundBounceColor.rgb * _AmbientIntensity * (0.08 + 0.14 * (1.0 - upTerm));

                        float powder = 1.0 - exp(-density * _BeerPowder * 4.0);
                        float multiple = powder * _MultipleScattering * (0.24 + 0.18 * saturate(phase * 0.16));
                        float3 lighting = ambient + _SunColor.rgb * sun + multiple;

                        // SRB smoke is a high-albedo particulate cloud. Preserve the engine-specific
                        // tint but keep the bulk albedo light enough that interiors become grey/tan,
                        // never coal-black merely because direct sunlight is weak.
                        float3 albedo = lerp(smokeColor.rgb, float3(0.82, 0.80, 0.76), 0.44);
                        float sampleAlpha = 1.0 - exp(-density * _Extinction * stepLength);
                        sampleAlpha *= smokeColor.a;

                        float contribution = transmittance * sampleAlpha;
                        accum += contribution * albedo * lighting;
                        accumulatedAlpha += contribution;
                        transmittance *= (1.0 - sampleAlpha);
                    }

                    p += rayDirLocal * stepLength;
                }

                float sceneZ = LinearEyeDepth(UNITY_SAMPLE_DEPTH(tex2Dproj(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos))));
                float partZ = i.projPos.z;
                float depthFade = saturate((sceneZ - partZ) / max(0.05, _SoftDepthFactor));

                accumulatedAlpha = saturate(accumulatedAlpha * depthFade);
                accum *= depthFade;
                return float4(accum, accumulatedAlpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
