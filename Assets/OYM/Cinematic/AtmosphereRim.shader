// Atmosphere limb glow for a planet seen from orbit (HDRP, additive, unlit). Draw it on a shell around the planet that
// reaches a few scale heights above the surface (any mesh covering the limb region works; front faces only).
// Per pixel the view ray's closest approach to the planet centre gives the altitude the ray grazes: above the limb the
// glow follows the air density exp(-alt / H) (a thin bright line), over the disc it is only a faint haze that fades
// inwards. Sunward limb bright, night side dim; warm forward scattering in a narrow lobe round the sun.
// Output is screen-relative HDR like OYM/FX/AdditiveHDR: 1 = white on screen, values above 1 feed bloom.
Shader "OYM/Cine/AtmosphereRim"
{
    Properties
    {
        _DayColor ("Day colour", Color) = (0.35, 0.62, 1.0, 1)
        _SunsetColor ("Sunrise colour", Color) = (1.0, 0.5, 0.2, 1)
        _Intensity ("Intensity", Float) = 1.2
        _PlanetRadius ("Planet radius (m)", Float) = 637100
        _ScaleHeight ("Scale height (m)", Float) = 2200
        _DiscHaze ("Haze over the disc", Float) = 0.3
        _ForwardPower ("Forward-scatter power", Float) = 40
        _ForwardBoost ("Forward-scatter boost", Float) = 2.5
        _SunLobePower ("Sun lobe power", Float) = 600
        _SunLobeBoost ("Sun lobe boost", Float) = 8
        _NightFloor ("Night-side floor", Float) = 0.03
        _SunDir ("Direction to sun (world)", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DayColor;
                float4 _SunsetColor;
                float _Intensity;
                float _PlanetRadius;
                float _ScaleHeight;
                float _DiscHaze;
                float _ForwardPower;
                float _ForwardBoost;
                float _SunLobePower;
                float _SunLobeBoost;
                float _NightFloor;
                float4 _SunDir;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionRWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionRWS = TransformObjectToWorld(v.positionOS);     // camera-relative in HDRP
                o.positionRWS = positionRWS;
                o.positionCS = TransformWorldToHClip(positionRWS);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4x4 m = GetObjectToWorldMatrix();
                float3 C = float3(m._m03, m._m13, m._m23);                      // planet centre, camera-relative
                float3 v = normalize(i.positionRWS);                            // camera -> pixel
                float3 P = v * dot(C, v) - C;                                    // closest point of the ray, from the centre
                float b = length(P);
                float alt = b - _PlanetRadius;                                   // altitude the ray grazes
                float dens = alt >= 0.0 ? exp(-alt / _ScaleHeight) : exp(alt / (_ScaleHeight * 3.0)) * _DiscHaze;
                float3 s = normalize(_SunDir.xyz);
                float lit = saturate(dot(P / max(b, 1.0), s) * 1.5 + 0.35);      // terminator wraps round the limb
                float day = _NightFloor + lit * lit;
                float cosSun = saturate(dot(v, s));
                float warm = pow(cosSun, _ForwardPower) * _ForwardBoost + pow(cosSun, _SunLobePower) * _SunLobeBoost;
                float3 c = dens * (_DayColor.rgb * day * _Intensity + _SunsetColor.rgb * warm);
                return float4(c, 0);
            }
            ENDHLSL
        }
    }
}
