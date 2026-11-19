// Lit billow smoke for cutscene / landing bursts (no HDRP lighting: a cheap, art-directed lighting model so the smoke
// reads the same in any of the dark maps).
//   * shape: 2x2 grayscale smoke atlas (T_FX_Smoke_2x2, RGB = shade, A = density), the cell picked per particle from
//     StableRandom, boiled by scrolling tileable noise and eroded by Custom1.x (0 = full, 1 = gone)
//   * light: a sphere normal built from the billboard basis (Normal + Tangent streams) against the sun direction set
//     by script (_SunDir, wrap diffuse), self-shadow by sampling the density toward the light, forward scattering
//     when the camera looks into the sun (backlit rims), and an ambient floor
//   * glow: thruster wash lighting the cloud from inside right after the burst: vertex colour RGB x Custom1.y
//   * premultiplied alpha (One OneMinusSrcAlpha), soft against scene depth and near the camera
// Output is screen-relative like OYM/FX/AdditiveHDR / FX_Erode (1 = white on screen in any map).
//
// Particle vertex streams (LandingSmoke): Position, Normal, Color, UV, Custom1.xy, StableRandom.x, Tangent
//   -> TEXCOORD0 = (uv, erosion, glow), TEXCOORD1.x = seed, TANGENT = billboard right.
Shader "OYM/FX/SmokeLit"
{
    Properties
    {
        _MainTex ("Smoke atlas 2x2 (RGB shade, A density)", 2D) = "white" {}
        _NoiseTex ("Noise (tileable RGBA)", 2D) = "gray" {}
        _Albedo ("Smoke albedo (screen-relative)", Color) = (0.62, 0.64, 0.68, 1)
        _SunColor ("Sun light (screen-relative)", Color) = (0.9, 0.86, 0.8, 1)
        _Ambient ("Ambient (screen-relative)", Color) = (0.07, 0.08, 0.1, 1)
        _SunDir ("Direction toward the sun (world)", Vector) = (0.4, 0.6, -0.7, 0)
        _Wrap ("Wrap diffuse", Range(0, 1)) = 0.45
        _ShadowStep ("Self-shadow step (uv)", Range(0, 0.25)) = 0.09
        _Shadow ("Self-shadow strength", Range(0, 1)) = 0.6
        _Scatter ("Forward scatter", Float) = 1.4
        _ScatterPow ("Forward scatter power", Float) = 6
        _Glow ("Inner glow intensity", Float) = 5
        _Density ("Density multiplier", Range(0, 3)) = 1.15
        _NoiseScale ("Noise scale", Float) = 1.1
        _NoiseScroll ("Noise scroll: erosion (xy) / distortion (zw)", Vector) = (0.03, 0.05, -0.04, 0.06)
        _Distort ("UV distortion", Range(0, 0.2)) = 0.045
        _ErodeNoise ("Erosion noise breakup", Range(0, 1)) = 0.6
        _EdgeSoft ("Erosion edge width", Range(0.02, 1)) = 0.35
        _SoftFade ("Soft particle fade (m)", Float) = 0.8
        _NearFade ("Camera near fade: start, length (m)", Vector) = (0.4, 1.6, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One OneMinusSrcAlpha
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
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Albedo, _SunColor, _Ambient, _SunDir;
                float4 _NoiseScroll, _NearFade;
                float _Wrap, _ShadowStep, _Shadow, _Scatter, _ScatterPow, _Glow, _Density;
                float _NoiseScale, _Distort, _ErodeNoise, _EdgeSoft, _SoftFade;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;   // uv, Custom1.x (erosion), Custom1.y (glow)
                float  seed : TEXCOORD1;  // StableRandom.x
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float  seed : TEXCOORD1;
                float3 nW : TEXCOORD2;
                float3 tW : TEXCOORD3;
                float3 bW : TEXCOORD4;
                float3 posRWS : TEXCOORD5;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionRWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(positionRWS);
                o.posRWS = positionRWS;
                o.color = v.color;
                o.uv0 = v.uv0;
                o.seed = v.seed;
                float3 n = normalize(TransformObjectToWorldDir(v.normalOS));
                float3 t = normalize(TransformObjectToWorldDir(v.tangentOS.xyz));
                o.nW = n; o.tW = t;
                o.bW = cross(n, t) * (v.tangentOS.w < 0 ? -1.0 : 1.0);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                // Invisible fragments leave first (11-19 particle pass): faded out near the camera or by the particle's
                // alpha. Premultiplied output (lit * cov, cov) is 0 there, which keeps the target unchanged, so
                // discarding before the four texture fetches and the lighting is pixel-identical.
                float fragZ = i.positionCS.w;
                float nearF = saturate((fragZ - _NearFade.x) / max(1e-3, _NearFade.y));
                float vis = i.color.a * nearF;
                [branch] if (vis <= 0.0) { discard; }

                float erosion = i.uv0.z;
                float glow = i.uv0.w;
                float t = _Time.y;
                // atlas cell from the seed
                float cell = floor(frac(i.seed * 7.31) * 4.0);
                float2 cellOff = float2(fmod(cell, 2.0), floor(cell * 0.5)) * 0.5;
                float2 uv = i.uv0.xy;
                float2 nuv = uv * _NoiseScale + i.seed * float2(3.71, 7.13);

                float2 dn = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv + t * _NoiseScroll.zw).rg * 2.0 - 1.0;
                float2 suv = saturate(uv + dn * _Distort);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, cellOff + suv * 0.5);
                float dens = saturate(tex.a * _Density);
                float shade = tex.r;

                // erosion with a noise breakup: the cloud thins out from its wispy edges
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv * 1.7 + t * _NoiseScroll.xy).b;
                float thr = erosion * (1.0 + _ErodeNoise * (n - 0.5) * 1.6);
                float a = saturate((dens - thr) / _EdgeSoft);

                // sphere normal from the billboard basis
                float2 c = uv * 2.0 - 1.0;
                float r2 = saturate(dot(c, c));
                float3 N = normalize(i.tW * c.x + i.bW * c.y + i.nW * sqrt(1.0 - r2) * 1.2);
                float3 L = normalize(_SunDir.xyz);
                float diff = saturate((dot(N, L) + _Wrap) / (1.0 + _Wrap));

                // self-shadow: density a step toward the light (in the billboard plane)
                float2 lDir = float2(dot(L, i.tW), dot(L, i.bW));
                float2 shuv = saturate(suv + lDir * _ShadowStep);
                float dl = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, cellOff + shuv * 0.5).a * _Density;
                float occl = 1.0 - _Shadow * saturate(dl - dens * 0.35);

                // forward scattering toward the camera when it looks into the light (thin parts glow most)
                float3 V = normalize(-i.posRWS);                  // camera-relative rendering: camera at the origin
                float fwd = pow(saturate(dot(-V, L)), _ScatterPow) * (1.0 - dens * 0.7);

                float3 lit = _Albedo.rgb * (_Ambient.rgb + _SunColor.rgb * diff * occl) * lerp(0.75, 1.15, shade)
                           + _SunColor.rgb * fwd * _Scatter * 0.25;
                // thruster wash lighting the cloud from inside (strongest in the dense core)
                lit += i.color.rgb * glow * _Glow * (0.35 + shade) * dens;

                // soft particles (the near-camera fade is in `vis` above)
                float sceneZ = LinearEyeDepth(LoadCameraDepth(uint2(i.positionCS.xy)), _ZBufferParams);
                float soft = saturate((sceneZ - fragZ) / max(1e-3, _SoftFade));
                float cov = a * vis * soft;
                // outside the (eroded) cloud: nothing to blend, skip the read-modify-write of the colour buffer
                if (cov <= 0.0) discard;
                return float4(lit * cov, cov);
            }
            ENDHLSL
        }
    }
}
