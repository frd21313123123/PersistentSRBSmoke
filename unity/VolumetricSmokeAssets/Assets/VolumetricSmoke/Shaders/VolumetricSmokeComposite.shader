Shader "PersistentSRBSmoke/VolumetricSmokeComposite"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "white" {}
        _VolumeTex ("Volume", 2D) = "black" {}
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
            sampler2D _VolumeTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float4 scene = tex2D(_MainTex, input.uv);
                float4 volume = tex2D(_VolumeTex, input.uv);
                // Weighted blended OIT resolves to source scattering plus the unoccluded scene.
                return float4(volume.rgb + scene.rgb * saturate(volume.a), scene.a);
            }
            ENDCG
        }
    }
}
