Shader "PersistentSRBSmoke/VolumetricSmokeTemporal"
{
    Properties
    {
        _MainTex ("Current volume", 2D) = "black" {}
        _HistoryTex ("History", 2D) = "black" {}
        _HistoryDepthTex ("History depth", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _HistoryTex;
            sampler2D _HistoryDepthTex;
            sampler2D _CameraDepthTexture;
            float _HistoryBlend;
            float _HistoryValid;
            float _DepthThreshold;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float4 current = tex2D(_MainTex, input.uv);
                if (_HistoryValid < 0.5)
                    return current;
                float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.uv));
                float oldDepth = tex2D(_HistoryDepthTex, input.uv).r;
                float stable = step(abs(depth - oldDepth), _DepthThreshold);
                float historyWeight = _HistoryBlend * stable;
                float4 history = tex2D(_HistoryTex, input.uv);
                return lerp(current, history, historyWeight);
            }
            ENDCG
        }
    }
}
