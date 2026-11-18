// Shockwave / heat-haze refraction for HDRP's distortion buffer + an optional faint additive glow.
//   radial: the image-gen shock ring (alpha) pushes the scene outward along the billboard's radius (camera-facing
//           particle, no rotation, so quad u/v = screen x/y);
//   turbulent: scrolling tileable noise wobbles the scene (heat shimmer over a laser burn, after a blast).
// Strength is in pixels at 1080p for a particle 10 m away (scaled with resolution and 1 / depth).
// Vertex colour alpha = strength over life (fade the ring as it expands).
// Particle vertex streams: Position, Color, UV  (TEXCOORD0.xy).
Shader "OYM/FX/Distort"
{
    Properties
    {
        _MainTex ("Mask (A)", 2D) = "white" {}
        _NoiseTex ("Noise (tileable RGBA)", 2D) = "gray" {}
        _Ramp ("Glow gradient map", 2D) = "white" {}
        _Radial ("Radial push (px)", Float) = 28
        _Turbulence ("Turbulent wobble (px)", Float) = 0
        _NoiseScale ("Noise scale", Float) = 2
        _NoiseSpeed ("Noise scroll speed", Float) = 0.6
        _Blur ("Distortion blur (0..1)", Range(0, 1)) = 0.1
        _RefDepth ("Reference depth (m)", Float) = 10
        _Glow ("Additive glow intensity", Float) = 0
        _SoftFade ("Soft fade (m)", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

        TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
        TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
        TEXTURE2D(_Ramp);     SAMPLER(sampler_Ramp);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Radial, _Turbulence, _NoiseScale, _NoiseSpeed, _Blur, _RefDepth, _Glow, _SoftFade;
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

        float SoftFade(float4 positionCS)
        {
            float sceneZ = LinearEyeDepth(LoadCameraDepth(uint2(positionCS.xy)), _ZBufferParams);
            return saturate((sceneZ - positionCS.w) / max(1e-3, _SoftFade));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlow
            float4 FragGlow(Varyings i) : SV_Target
            {
                float m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw).a;
                float3 c = SAMPLE_TEXTURE2D_LOD(_Ramp, sampler_Ramp, float2(saturate(m * 0.9), 0.5), 0).rgb;
                return float4(c * m * _Glow * i.color.rgb * i.color.a * SoftFade(i.positionCS), 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistortionVectors"
            Tags { "LightMode" = "DistortionVectors" }
            // StencilUsage.DistortionVectors = 1 << 2 (HDRP 17): ApplyDistortion only runs where this bit is set
            Stencil { WriteMask 4 Ref 4 Comp Always Pass Replace }
            Blend One One, One One
            BlendOp Add, Add
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDistort
            float4 FragDistort(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
                float2 radial = float2(i.uv.x - 0.5, 0.5 - i.uv.y);   // quad v is up, pixel y is down
                float rl = length(radial);
                radial = rl > 1e-4 ? radial / rl : 0;
                float2 n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * _NoiseScale + _Time.y * _NoiseSpeed * float2(0.3, 1.0)).rg * 2.0 - 1.0;
                float px = (_ScreenSize.y / 1080.0) * saturate(_RefDepth / max(i.positionCS.w, 0.5)) * i.color.a * SoftFade(i.positionCS);
                float2 d = (radial * _Radial + n * _Turbulence) * m * px;
                // HDRP EncodeDistortion layout: (offset px xy, source valid, blur)
                return float4(d, 1.0, saturate(_Blur * m * i.color.a));
            }
            ENDHLSL
        }
    }
}
