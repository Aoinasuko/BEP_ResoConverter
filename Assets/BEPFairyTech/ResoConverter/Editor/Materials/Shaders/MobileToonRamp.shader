Shader "Hidden/BEPFairyTech/ResoConverter/MobileToonRamp"
{
    Properties
    {
        _MainTex ("Ramp", 2D) = "white" {}
        _Color ("Base Tint", Color) = (1,1,1,1)
        _ShadowAlbedo ("Shadow Albedo Approximation", Range(0,1)) = 0.5
        _ShadowBoost ("Shadow Boost", Range(0,1)) = 0
        _Strength ("Shadow Strength", Range(0,1)) = 0.5
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _ShadowAlbedo, _ShadowBoost, _Strength;
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = float2(input.uv.x, 0.5) * _MainTex_ST.xy + _MainTex_ST.zw;
                float3 brightness = saturate(tex2D(_MainTex, uv).rgb + _ShadowBoost);
                // The source colors its shadows with per-pixel albedo. A ramp
                // cannot encode that dependency, so retain the base tint here.
                float3 shaded = lerp(_Color.rgb * _ShadowAlbedo, 1, brightness);
                return float4(lerp(1, shaded, _Strength), 1);
            }
            ENDCG
        }
    }
}
