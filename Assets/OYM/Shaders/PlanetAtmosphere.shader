// Atmosphere of the arena's giant planet, seen from outside (URP, unlit, premultiplied alpha).
// Draw it on a shell a few scale heights above the surface (front faces). The baked surface (OYMPlanet, HDRP/Unlit,
// pipeline/planet/bake_planet.py) carries everything view-independent; this shader adds what depends on the viewer:
//   * over the disc: aerial haze whose column grows toward the limb (tau / mu) - adds sky colour AND hides the
//     surface behind it (alpha = 1 - transmittance), lit by the sun at that point, warm along the terminator;
//   * off the disc: the limb glow, optical depth of the grazing ray ~ exp(-altitude / H);
//   * the sun's glint on open water (equirect mask from the bake), dimmed by the haze in front of it.
// Output is screen-relative HDR (1 = white on screen, above 1 blooms; URP has no exposure, values go out as-is).
// URP port: positions are made camera-relative by hand in the vertex shader (the maths below was written for HDRP
// camera-relative rendering and keeps float precision at planet scale this way).
Shader "OYM/Env/PlanetAtmosphere"
{
    Properties
    {
        _SkyColor ("Sky colour", Color) = (0.22, 0.58, 1.0, 1)
        _TwilightColor ("Twilight colour", Color) = (1.0, 0.42, 0.16, 1)
        _PlanetRadius ("Planet radius (m)", Float) = 31000
        _ScaleHeight ("Scale height (m)", Float) = 480
        _RimDensity ("Grazing optical depth at the surface", Float) = 3
        _RimIntensity ("Limb glow intensity", Float) = 1.8
        _HazeDepth ("Vertical haze optical depth", Float) = 0.07
        _HazeIntensity ("Haze brightness", Float) = 0.7
        _DiscTwilight ("Twilight in the disc haze", Float) = 0.18
        _TwilightWidth ("Twilight band width (cos)", Float) = 0.07
        _HazeOpacity ("Haze hides the surface", Range(0, 1)) = 0.8
        _NightFloor ("Night-side floor", Float) = 0.015
        _SunDir ("Direction to sun (world)", Vector) = (0, 0, 1, 0)
        _SunColor ("Sun colour", Color) = (1.0, 0.92, 0.8, 1)
        [NoScaleOffset] _GlintMap ("Open-water mask (equirect)", 2D) = "black" {}
        _GlintStrength ("Glint peak", Float) = 1.6
        _GlintPower ("Glint sharpness", Float) = 900
        _GlintSheen ("Broad sheen", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GlintMap);
            SAMPLER(sampler_GlintMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyColor;
                float4 _TwilightColor;
                float _PlanetRadius;
                float _ScaleHeight;
                float _RimDensity;
                float _RimIntensity;
                float _HazeDepth;
                float _HazeIntensity;
                float _DiscTwilight;
                float _TwilightWidth;
                float _HazeOpacity;
                float _NightFloor;
                float4 _SunDir;
                float4 _SunColor;
                float _GlintStrength;
                float _GlintPower;
                float _GlintSheen;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionRWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS);      // absolute in URP
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionRWS = positionWS - _WorldSpaceCameraPos.xyz;          // camera-relative, as in HDRP
                return o;
            }

            float OymSq(float x) { return x * x; }

            float4 Frag(Varyings i) : SV_Target
            {
                float4x4 m = GetObjectToWorldMatrix();
                float3 C = float3(m._m03, m._m13, m._m23) - _WorldSpaceCameraPos.xyz;   // planet centre, camera-relative
                float3 v = normalize(i.positionRWS);                            // camera -> pixel
                float3 s = normalize(_SunDir.xyz);
                float R = _PlanetRadius;
                float tca = dot(C, v);
                float3 P = v * tca - C;                                          // centre -> closest point of the ray
                float b = length(P);
                float3 col = 0;
                float alpha = 0;

                if (b < R)
                {
                    // ---- over the disc
                    float thc = sqrt(max(R * R - b * b, 0.0));
                    float3 n = (P - v * thc) / R;                                // surface normal where the ray lands
                    float mu = thc / R;                                          // cos of the view zenith there
                    float mus = dot(n, s);
                    float T = exp(-_HazeDepth / (mu + 0.035));
                    float a = 1.0 - T;
                    float day = smoothstep(-0.12, 0.35, mus);
                    float tw = exp(-OymSq((mus + 0.02) / _TwilightWidth));
                    float3 L = _SkyColor.rgb * (day + _NightFloor) + _TwilightColor.rgb * tw * _DiscTwilight;
                    col = L * a * _HazeIntensity;
                    alpha = a * _HazeOpacity;

                    // sun glint on open water (mask = ocean and no cloud, from the bake)
                    float3 lo = normalize(mul((float3x3)GetWorldToObjectMatrix(), n));
                    float2 uv = float2(frac(atan2(lo.z, lo.x) * (0.5 * INV_PI) + 1.0), 1.0 - acos(clamp(lo.y, -1.0, 1.0)) * INV_PI);
                    float water = SAMPLE_TEXTURE2D_LOD(_GlintMap, sampler_GlintMap, uv, 0).r;
                    float nh = saturate(dot(n, normalize(s - v)));
                    float g = pow(nh, _GlintPower) * _GlintStrength + pow(nh, _GlintPower * 0.03) * _GlintSheen;
                    col += _SunColor.rgb * g * water * saturate(mus * 5.0) * T;
                }
                else if (tca > 0.0)
                {
                    // ---- off the disc: limb glow of the grazing ray
                    float alt = b - R;
                    float tau = _RimDensity * exp(-alt / _ScaleHeight);
                    float g = 1.0 - exp(-tau);
                    float mus = dot(P / b, s);
                    float day = smoothstep(-0.28, 0.3, mus);
                    float tw = exp(-OymSq((mus + 0.05) / 0.13));
                    float fwd = pow(saturate(dot(v, s)), 20.0) * 3.0;            // looking toward the sun through the air
                    float3 L = _SkyColor.rgb * (day + _NightFloor) + _TwilightColor.rgb * (tw + fwd);
                    col = L * g * _RimIntensity;
                }
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
