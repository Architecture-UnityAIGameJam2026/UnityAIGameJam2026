// Directed-energy beam for a LineRenderer (textureMode = Tile: uv.x runs in metres along the beam, uv.y 0..1 across).
// A guaranteed gaussian white-hot core + the image-gen beam strip scrolled along the beam in two layers at different
// speeds (the sheath "flows" toward the target), broken up by noise, flickered, then gradient-mapped (HUSK magenta /
// player cyan). Vertex colour = tint, alpha = on/off envelope from BeamFX. Screen-relative HDR like OYM/FX/AdditiveHDR.
// Ported from HDRP (ForwardOnly) to URP (UniversalForward); maths unchanged.
Shader "OYM/FX/Beam"
{
    Properties
    {
        _MainTex ("Beam strip (A), tiles in u", 2D) = "white" {}
        _NoiseTex ("Noise (tileable RGBA)", 2D) = "gray" {}
        _Ramp ("Gradient map", 2D) = "white" {}
        _Intensity ("Intensity (screen-relative HDR)", Float) = 12
        _CoreWidth ("Core width (0..1 of beam)", Range(0.02, 0.6)) = 0.16
        _CoreBoost ("Core boost", Float) = 1.6
        _SheathAmount ("Sheath amount", Range(0, 2)) = 0.8
        _Tile ("Strip repeats per metre", Float) = 0.35
        _Scroll ("Scroll speed (strips / s)", Float) = 3.5
        _Flicker ("Flicker", Range(0, 1)) = 0.25
        _Pulse ("Travelling pulse (x amp, y freq per m, z speed m/s)", Vector) = (0.35, 0.6, 40, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_Ramp);     SAMPLER(sampler_Ramp);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Pulse;
                float _Intensity, _CoreWidth, _CoreBoost, _SheathAmount, _Tile, _Scroll, _Flicker;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS));
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float metres = i.uv.x;
                float v = i.uv.y;
                float across = abs(v - 0.5) * 2.0;
                float cw = across / _CoreWidth;
                float core = exp(-cw * cw);

                float u = metres * _Tile;
                float s1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u - t * _Scroll, v)).a;
                float s2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u * 0.61 - t * _Scroll * 1.7 + 0.37, 1.0 - v)).a;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 0.5 - t * _Scroll * 0.4, t * 0.9)).r;
                float sheath = (s1 * 0.6 + s2 * 0.5) * lerp(0.55, 1.45, n) * _SheathAmount;

                float pulse = 1.0 + _Pulse.x * sin((metres - t * _Pulse.z) * _Pulse.y * 6.2831853);
                float flick = 1.0 + _Flicker * (SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, float2(t * 7.3, 0.5), 0).g - 0.5) * 2.0;

                float heat = saturate(core * _CoreBoost * 0.6 + sheath * 0.7);
                float3 ramp = SAMPLE_TEXTURE2D_LOD(_Ramp, sampler_Ramp, float2(heat, 0.5), 0).rgb;
                float bright = (core * _CoreBoost + sheath) * pulse * flick;
                return float4(ramp * bright * _Intensity * i.color.rgb * i.color.a, 0);
            }
            ENDHLSL
        }
    }
}
