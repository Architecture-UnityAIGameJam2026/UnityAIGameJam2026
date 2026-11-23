// Break-edge heat on shattered enemy pieces (ShatterFX). Drawn as the last submesh of a piece: the exterior
// triangles near the fracture, additive, screen-relative HDR, inflated a hair along the normal.
//   vertex colour a : 1 on the break edge, fading to 0 a few cm into the hull (baked by OYMShatter)
//   _Heat           : piece temperature 0..1 (script cools it) -> blackbody ramp; the glowing band shrinks and
//                     breaks up with noise as it cools, like a cut edge going from white-hot to dull red
//   _Flash          : whole-surface white-hot flash at the kill moment (fresnel weighted, AC6-style white-out)
Shader "OYM/FX/ShatterHeat"
{
    Properties
    {
        _RampTex ("Blackbody ramp (x: cold -> hot)", 2D) = "white" {}
        _NoiseTex ("Noise (tileable)", 2D) = "gray" {}
        _Intensity ("Edge intensity (screen-relative HDR)", Float) = 9
        _Heat ("Heat 0..1 (script)", Float) = 1
        _Flash ("Flash 0..1 (script)", Float) = 0
        _FlashColor ("Flash colour (HDR, screen-relative)", Color) = (2.2, 1.9, 1.6, 1)
        _RimPower ("Flash rim power", Float) = 2
        _NoiseScale ("Noise tiles per metre", Float) = 2.5
        _Inflate ("Inflate along normal (m)", Float) = 0.003
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+5" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_RampTex);  SAMPLER(sampler_RampTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _FlashColor;
                float _Intensity, _Heat, _Flash, _RimPower, _NoiseScale, _Inflate;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;  // absolute world space in URP (name kept from HDRP)
                float3 normalWS : TEXCOORD1;
                float3 noisePos : TEXCOORD2;   // object space x scale: the pattern sticks to the piece
                float3 normalOS : TEXCOORD3;
                float edge : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float3 pRWS = TransformObjectToWorld(v.positionOS) + nWS * _Inflate;
                o.positionCS = TransformWorldToHClip(pRWS);
                o.positionRWS = pRWS;
                o.normalWS = nWS;
                float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                o.noisePos = v.positionOS * scale * _NoiseScale;
                o.normalOS = v.normalOS;
                o.edge = v.color.a;
                return o;
            }

            float Noise3(float3 p, float3 n)
            {
                float3 w = pow(abs(n), 4.0);
                w /= max(1e-4, w.x + w.y + w.z);
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.yz).r * w.x
                     + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz).r * w.y
                     + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy).r * w.z;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 vdir = GetWorldSpaceNormalizeViewDir(i.positionRWS);
                float fres = pow(1.0 - saturate(dot(n, vdir)), _RimPower);

                float heat = saturate(_Heat);
                float nz = Noise3(i.noisePos, normalize(i.normalOS));
                float nz2 = Noise3(i.noisePos * 3.1 + 0.37, normalize(i.normalOS));
                // glowing band: wide + white while hot, a ragged thin red line as it cools
                float reach = lerp(0.2, 0.95, heat);                       // how far into the hull it still glows
                float e = i.edge - (1.0 - reach) - (nz - 0.5) * 0.45;
                float band = saturate(e * 3.0);
                band *= band;
                float temp = saturate(heat * (0.45 + 0.55 * i.edge) * (0.85 + 0.3 * nz2));
                float3 ramp = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(temp, 0.5)).rgb;

                float3 c = ramp * band * _Intensity * (0.25 + 0.75 * temp);
                c += _FlashColor.rgb * _Flash * (0.3 + 0.7 * fres);
                return float4(c, 0);
            }
            ENDHLSL
        }
    }
}
