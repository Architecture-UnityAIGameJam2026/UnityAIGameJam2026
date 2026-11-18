// Image-gen shape -> game FX. Grayscale shape texture (RGB = shade / heat source, A = density) is
//   * UV-distorted by scrolling tileable noise (the billow "boils"),
//   * eroded by a per-particle threshold (Custom1.x: 0 = full shape, 1 = gone) with a noise breakup, edges get
//     crisper as it erodes and the burning edge glows,
//   * gradient-mapped through a ramp (black body / HUSK magenta / player cyan / hot metal) by heat
//     = shade^contrast * Custom1.y (cools over the particle's life),
//   * soft-faded against scene depth and near the camera.
// Output is screen-relative HDR like OYM/FX/AdditiveHDR (1 = white on screen in any map, > 1 feeds bloom).
// Blend: additive (One One, _Opacity 0) for fire / flashes, premultiplied (One OneMinusSrcAlpha, _Opacity 1) for
// smoke / debris that also occlude (they add _BaseColor x shade, which stays dark in space).
//
// Particle vertex streams (set by OYMHitFX): Position, Color, UV, Custom1.xy, StableRandom.x
//   -> TEXCOORD0 = (uv.xy, erosion, heat), TEXCOORD1.x = seed.
Shader "OYM/FX/Erode"
{
    Properties
    {
        _MainTex ("Shape (RGB shade, A density)", 2D) = "white" {}
        _Ramp ("Gradient map (sRGB strip)", 2D) = "white" {}
        _NoiseTex ("Noise (tileable RGBA)", 2D) = "gray" {}
        _Intensity ("Emission intensity (screen-relative HDR)", Float) = 6
        _HeatContrast ("Heat from shade (power)", Float) = 1.4
        _HeatBias ("Heat bias", Range(-1, 1)) = 0
        _EmissionFloor ("Emission below this ramp coord is 0", Range(0, 0.5)) = 0.0
        _SoftStart ("Edge width at erosion 0 (soft)", Range(0.05, 1)) = 0.6
        _EdgeSoft ("Edge width while eroding (crisp)", Range(0.01, 0.5)) = 0.1
        _EdgeGlow ("Burning edge glow", Float) = 0.6
        _ErodeNoise ("Erosion noise breakup", Range(0, 1)) = 0.5
        _NoiseScale ("Noise scale", Float) = 1.3
        _NoiseScroll ("Noise scroll: erosion (xy) / distortion (zw)", Vector) = (0.04, 0.07, -0.05, 0.09)
        _Distort ("UV distortion", Range(0, 0.2)) = 0.03
        _BaseColor ("Unlit base (smoke / metal), screen-relative", Color) = (0, 0, 0, 1)
        _BaseShade ("Base colour follows shade", Range(0, 1)) = 1
        _Opacity ("Opacity (0 = pure additive)", Range(0, 1)) = 0
        _SoftFade ("Soft particle fade (m)", Float) = 0.4
        _NearFade ("Camera near fade: start, length (m)", Vector) = (0.25, 0.8, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_Ramp);     SAMPLER(sampler_Ramp);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseScroll;
                float4 _BaseColor;
                float4 _NearFade;
                float _Intensity, _HeatContrast, _HeatBias, _EmissionFloor;
                float _SoftStart, _EdgeSoft, _EdgeGlow, _ErodeNoise;
                float _NoiseScale, _Distort, _BaseShade, _Opacity, _SoftFade;
                float _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;   // uv, Custom1.x (erosion), Custom1.y (heat)
                float  seed : TEXCOORD1;  // StableRandom.x
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float  seed : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionRWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(positionRWS);
                o.color = v.color;
                o.uv0 = float4(v.uv0.xy * _MainTex_ST.xy + _MainTex_ST.zw, v.uv0.zw);
                o.seed = v.seed;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float erosion = i.uv0.z;
                float heat = i.uv0.w;
                float2 nuv = i.uv0.xy * _NoiseScale + i.seed * float2(3.71, 7.13);
                float t = _Time.y;

                // boil: distort the shape lookup with slow tileable noise
                float2 dn = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv + t * _NoiseScroll.zw).rg * 2.0 - 1.0;
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv0.xy + dn * _Distort);
                float dens = tex.a;
                float shade = tex.r;

                // erosion threshold with noise breakup (only once it starts eroding)
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv * 1.9 + t * _NoiseScroll.xy).b;
                float thr = erosion * (1.0 + _ErodeNoise * (n - 0.5) * 1.6);
                float w = lerp(_SoftStart, _EdgeSoft, saturate(erosion * 4.0));
                float a = saturate((dens - thr) / w);
                float edge = a * (1.0 - saturate((dens - thr) / (w * 2.5)));   // band right at the eroding edge

                // gradient map by heat
                float u = saturate(pow(shade, _HeatContrast) * heat + _HeatBias + edge * _EdgeGlow * heat);
                float3 ramp = SAMPLE_TEXTURE2D_LOD(_Ramp, sampler_Ramp, float2(u, 0.5), 0).rgb;
                float3 emission = ramp * _Intensity * saturate((u - _EmissionFloor) / max(1e-3, 1.0 - _EmissionFloor));

                // soft particles + near-camera fade
                float sceneZ = LinearEyeDepth(LoadCameraDepth(uint2(i.positionCS.xy)), _ZBufferParams);
                float fragZ = i.positionCS.w;
                float soft = saturate((sceneZ - fragZ) / max(1e-3, _SoftFade));
                float nearF = saturate((fragZ - _NearFade.x) / max(1e-3, _NearFade.y));

                float cov = a * i.color.a * soft * nearF;
                float3 baseC = _BaseColor.rgb * lerp(1.0, shade, _BaseShade);
                float3 rgb = (emission * i.color.rgb + baseC * _Opacity) * cov;
                return float4(rgb, cov * _Opacity);
            }
            ENDHLSL
        }
    }
}
