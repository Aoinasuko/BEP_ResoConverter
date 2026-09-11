// Alpha/emission/metallic mappings adapted from Modular Avatar Resonite and lilToon.
// Copyright (c) 2025 bd_; Copyright (c) 2020-present lilxyzw. MIT license; see ../COPYING.md.
Shader "Hidden/BEPFairyTech/ResoConverter/Bake"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Mask ("Mask", 2D) = "white" {}
        _AlphaMode ("Alpha Mode", Float) = 0
        _AlphaScale ("Alpha Scale", Float) = 1
        _AlphaValue ("Alpha Value", Float) = 0
        _EmissionBlend ("Emission Blend", Float) = 1
        _NormalScale ("Normal Strength", Float) = 1
        _NormalPacked ("Normal Packed", Float) = 1
        _Smoothness ("Smoothness", 2D) = "white" {}
        _Metallic ("Metallic", 2D) = "white" {}
        _Reflection ("Reflection", 2D) = "white" {}
        _ReflectionColor ("Reflection Color", Color) = (1,1,1,1)
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _Mask, _Smoothness, _Metallic, _Reflection;
        float4 _MainTex_ST, _Mask_ST, _Smoothness_ST, _Metallic_ST, _Reflection_ST;
        float _AlphaMode, _AlphaScale, _AlphaValue, _EmissionBlend, _NormalScale, _NormalPacked;
        float4 _ReflectionColor, _Tint;
        float4 Alpha(v2f_img i) : SV_Target
        {
            float4 color = tex2D(_MainTex, i.uv);
            float mask = saturate(tex2D(_Mask, TRANSFORM_TEX(i.uv, _Mask)).r * _AlphaScale + _AlphaValue);
            if (_AlphaMode < 1.5) color.a = mask;
            else if (_AlphaMode < 2.5) color.a *= mask;
            else if (_AlphaMode < 3.5) color.a = saturate(color.a + mask);
            else color.a = saturate(color.a - mask);
            return color;
        }
        float4 Emission(v2f_img i) : SV_Target
        {
            float4 color = tex2D(_MainTex, TRANSFORM_TEX(i.uv, _MainTex));
            float4 mask = tex2D(_Mask, TRANSFORM_TEX(i.uv, _Mask));
            return float4(color.rgb * color.a * mask.rgb * mask.a * _EmissionBlend, 1);
        }
        float4 Normal(v2f_img i) : SV_Target
        {
            float4 sample = tex2D(_MainTex, i.uv);
            float3 n = _NormalPacked > 0.5 ? UnpackNormal(sample) : sample.xyz * 2 - 1;
            n.xy *= _NormalScale;
            n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
            return float4(normalize(n) * 0.5 + 0.5, 1);
        }
        float4 Metallic(v2f_img i) : SV_Target
        {
            float smoothness = tex2D(_Smoothness, TRANSFORM_TEX(i.uv, _Smoothness)).r;
            float metallic = tex2D(_Metallic, TRANSFORM_TEX(i.uv, _Metallic)).r;
            float4 reflection = tex2D(_Reflection, TRANSFORM_TEX(i.uv, _Reflection)) * _ReflectionColor;
            return float4(metallic, 1, 1, smoothness * Luminance(reflection.rgb) * reflection.a);
        }
        float4 Tint(v2f_img i) : SV_Target
        {
            return tex2D(_MainTex, i.uv) * _Tint;
        }
        float4 MultiplyOpacity(v2f_img i) : SV_Target
        {
            float4 color = tex2D(_MainTex, i.uv);
            return float4(lerp(1, color.rgb, color.a), 1);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Alpha
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Emission
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Normal
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Metallic
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Tint
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment MultiplyOpacity
            ENDCG
        }
    }
    Fallback Off
}
