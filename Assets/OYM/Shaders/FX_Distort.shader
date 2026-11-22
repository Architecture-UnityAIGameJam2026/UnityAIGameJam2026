// Shockwave / heat-haze refraction + an optional faint additive glow (URP port).
//   radial: the image-gen shock ring (alpha) pushes the scene outward along the billboard's radius (camera-facing
//           particle, no rotation, so quad u/v = screen x/y);
//   turbulent: scrolling tileable noise wobbles the scene (heat shimmer over a laser burn, after a blast).
// Strength is in pixels at 1080p for a particle 10 m away (scaled with resolution and 1 / depth).
// Vertex colour alpha = strength over life (fade the ring as it expands).
// Particle vertex streams: Position, Color, UV  (TEXCOORD0.xy).
//
// URP has no distortion buffer: the "DistortionVectors" pass refracts directly, sampling the URP Opaque Texture
// (_CameraOpaqueTexture) at the displaced position and alpha-blending it over the pixel. Blur (HDRP: colour pyramid mip)
// is approximated with a 5-tap kernel. Requires "Opaque Texture" and "Depth Texture" on the URP asset / camera.
// Draw order inside the transparent queue, per particle: SRPDefaultUnlit (refraction) then UniversalForward (glow).
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
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
            float sceneZ = LinearEyeDepth(LoadSceneDepth(uint2(positionCS.xy)), _ZBufferParams);
            return saturate((sceneZ - positionCS.w) / max(1e-3, _SoftFade));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlow
            float4 FragGlow(Varyings i) : SV_Target
            {
                // refraction-only materials (_Glow 0: the shock rings, heat haze) add nothing here (11-19 particle pass)
                [branch] if (_Glow <= 0.0 || i.color.a <= 0.0) { discard; }
                float m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw).a;
                float3 c = SAMPLE_TEXTURE2D_LOD(_Ramp, sampler_Ramp, float2(saturate(m * 0.9), 0.5), 0).rgb;
                return float4(c * m * _Glow * i.color.rgb * i.color.a * SoftFade(i.positionCS), 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistortionVectors"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                // no push and no blur here (outside the ring / faded out): leave the pixel untouched
                if (m * i.color.a <= 0.0) discard;
                float2 radial = float2(i.uv.x - 0.5, 0.5 - i.uv.y);   // quad v is up, pixel y is down
                float rl = length(radial);
                radial = rl > 1e-4 ? radial / rl : float2(0.0, 0.0);
                float2 n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * _NoiseScale + _Time.y * _NoiseSpeed * float2(0.3, 1.0)).rg * 2.0 - 1.0;
                float resScale = _ScaledScreenParams.y / 1080.0;
                float px = resScale * saturate(_RefDepth / max(i.positionCS.w, 0.5)) * i.color.a * SoftFade(i.positionCS);
                float2 d = (radial * _Radial + n * _Turbulence) * m * px;   // pixels, y down (HDRP convention)
                float blur = saturate(_Blur * m * i.color.a);

                // URP normalised screen uv is y-up on every platform: flip the y-down pixel offset
                float2 invSize = rcp(_ScaledScreenParams.xy);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS) + float2(d.x, -d.y) * invSize;

                // HDRP picked colour-pyramid mip (lodCount - 1) * blur; approximate its footprint with 5 taps
                float rPx = min(0.5 * (exp2(blur * 10.0) - 1.0), 24.0) * resScale;
                float3 c = SampleSceneColor(suv) * 0.4;
                [branch] if (rPx > 0.25)
                {
                    float2 o = rPx * invSize;
                    c += SampleSceneColor(suv + float2( o.x,  o.y)) * 0.15;
                    c += SampleSceneColor(suv + float2(-o.x,  o.y)) * 0.15;
                    c += SampleSceneColor(suv + float2( o.x, -o.y)) * 0.15;
                    c += SampleSceneColor(suv + float2(-o.x, -o.y)) * 0.15;
                }
                else
                    c *= 2.5;

                // sub-pixel offsets fade in, so the ring's soft edge does not replace transparents behind it
                float alpha = saturate(max(length(d), rPx));
                if (alpha <= 0.0) discard;
                return float4(c, alpha);
            }
            ENDHLSL
        }
    }
}
