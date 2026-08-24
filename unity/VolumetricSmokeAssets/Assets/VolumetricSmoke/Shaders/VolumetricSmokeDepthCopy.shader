Shader "PersistentSRBSmoke/VolumetricSmokeDepthCopy"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _CameraDepthTexture;
            fixed4 frag(v2f_img input) : SV_Target
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.uv);
                return Linear01Depth(rawDepth).xxxx;
            }
            ENDCG
        }
    }
}
