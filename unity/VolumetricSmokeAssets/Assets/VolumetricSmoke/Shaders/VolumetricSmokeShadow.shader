Shader "PersistentSRBSmoke/VolumetricSmokeShadow"
{
    Properties
    {
        _ShadowOpacity ("Shadow opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+90" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            float _ShadowOpacity;
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radial = saturate(1.0 - dot(centered, centered));
                radial = radial * radial * (3.0 - 2.0 * radial);
                return fixed4(0.02, 0.025, 0.03, input.color.a * radial * _ShadowOpacity);
            }
            ENDCG
        }
    }
}
