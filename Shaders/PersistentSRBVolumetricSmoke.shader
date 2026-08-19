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
        _DensityMultiplier ("Density", Float) = 1.48
        _Extinction ("Extinction", Float) = 2.35
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

            float softSphere(float3 p, float3 centre, float radius, float3 stretch)
            {
                float3 q = (p - centre) / max(stretch, float3(0.05,0.05,0.05));
                float x = saturate(1.0 - length(q) / max(0.04, radius));
                return x * x * (3.0 - 2.0 * x);
            }

            // Large-scale geometry of one smoke cloudlet. Real SRB exhaust forms rolling billows
            // with several attached lobes; a single sphere always reads like a chain of beads.
            float macroShape(float3 p, float seed)
            {
                float a = seed * 6.2831853;
                float3 d1 = normalize(float3(sin(a + 0.7), cos(a * 1.31 + 1.2), sin(a * 1.77 + 2.1)) + 0.001);
                float3 d2 = normalize(float3(cos(a * 1.13 + 2.8), sin(a * 1.59 + 0.4), cos(a * 0.83 + 1.7)) + 0.001);
                float3 d3 = normalize(cross(d1, d2) + float3(0.05, 0.02, 0.03));

                float shape = softSphere(p, float3(0,0,0), 0.49, float3(1.06, 0.98, 1.02));
                shape = max(shape, softSphere(p,  d1 * 0.17, 0.38, float3(1.04, 0.91, 1.10)) * 0.98);
                shape = max(shape, softSphere(p, -d1 * 0.15, 0.35, float3(0.93, 1.10, 1.00)) * 0.96);
                shape = max(shape, softSphere(p,  d2 * 0.20, 0.32, float3(1.12, 0.94, 0.92)) * 0.94);
                shape = max(shape, softSphere(p, -d2 * 0.18, 0.30, float3(0.94, 1.06, 1.10)) * 0.92);
                shape = max(shape, softSphere(p,  d3 * 0.21, 0.28, float3(1.06, 1.03, 0.91)) * 0.90);
                return saturate(shape);
            }

            float densityAt(float3 p, float age, float seed)
            {
                float macro = macroShape(p, seed);
                if (macro <= 0.001)
                    return 0;

                float3 warpSeed = float3(seed * 19.7, seed * 7.3, seed * 13.1);

                // Low-frequency domain warp bends the density boundary, while higher-frequency
                // erosion cuts cauliflower detail into the illuminated outer surface.
                float3 warp = float3(
                    noise3(p * 2.1 + warpSeed + 3.1),
                    noise3(p * 2.0 + warpSeed + 9.4),
                    noise3(p * 2.2 + warpSeed + 15.7)) - 0.5;
                float3 q = p + warp * 0.085;

                float n = fbm(q * 3.05 + warpSeed + age * float3(0.07, 0.14, 0.05));
                float detail = noise3(q * 10.2 + warpSeed * 0.37 + age * 0.27);
                float ridge = 1.0 - abs(detail * 2.0 - 1.0);
                ridge *= ridge;

                float eroded = smoothstep(0.18, 0.78, n + macro * 0.72 + ridge * 0.12 - detail * 0.19);
                float core = lerp(0.78, 1.12, macro);
                return saturate(macro * eroded * core * _DensityMultiplier);
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
                return exp(-optical * _Extinction * 1.25);
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
                    if (s >= steps || transmittance < 0.012) break;

                    float density = densityAt(p, age, seed);
                    if (density > 0.006)
                    {
                        float shadow = shadowTransmittance(p, sunDirLocal, age, seed);
                        float sun = _SunIntensity * _SunTransmittance * phase * shadow;

                        float upTerm = saturate(dot(normalize(p + 0.0001), normalize(_PlanetUp.xyz)) * 0.5 + 0.5);
                        float3 ambient = _SkyAmbientColor.rgb * _AmbientIntensity * (0.74 + 0.26 * upTerm);
                        ambient += _GroundBounceColor.rgb * _AmbientIntensity * (0.10 + 0.16 * (1.0 - upTerm));

                        float powder = 1.0 - exp(-density * _BeerPowder * 4.0);
                        float multiple = powder * _MultipleScattering * (0.30 + 0.20 * saturate(phase * 0.16));
                        float3 lighting = ambient + _SunColor.rgb * sun + multiple;

                        // Preserve a light grey / warm particulate albedo. Internal density creates
                        // relief through shadowing, while multiple scattering prevents black cores.
                        float3 albedo = lerp(smokeColor.rgb, float3(0.86, 0.83, 0.78), 0.48);
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
