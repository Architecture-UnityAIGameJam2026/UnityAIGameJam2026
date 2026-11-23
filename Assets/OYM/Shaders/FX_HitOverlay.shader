// "Got hit" overlay drawn as an extra material on a character / enemy renderer (HitFlash adds it only while a hit is
// alive). Additive, screen-relative HDR, inflated a hair along the normal so it sits on the opaque surface.
//   * flash   : whole-body HDR flash weighted to the silhouette (fresnel) - the AC6 "white-out" on impact
//   * per hit : up to 4 hits (_HitN = world pos xyz, age s; _HitColN = HDR linear colour rgb, size m)
//       - hot spot at the hit point that whitens at the centre and cools away (molten / scorch glow)
//       - a ring wave that runs over the hull from the hit point, carried by a triplanar hex grid + plasma detail
//         (image-gen plasma texture), so energy hits read as a shield / armour field reacting
Shader "OYM/FX/HitOverlay"
{
    Properties
    {
        _NoiseTex ("Noise (tileable RGBA)", 2D) = "gray" {}
        _PlasmaTex ("Plasma detail (tileable, A)", 2D) = "black" {}
        _Intensity ("Intensity", Float) = 1
        _Flash ("Flash (0..1, script)", Float) = 0
        _FlashColor ("Flash colour (HDR, screen-relative)", Color) = (1, 1, 1, 1)
        _RimPower ("Flash rim power", Float) = 2.5
        _WaveSpeed ("Wave speed (m/s per m of hit size)", Float) = 5
        _WaveWidth ("Wave width (per m of hit size)", Float) = 0.12
        _Life ("Hit life (s)", Float) = 0.6
        _SpotRadius ("Hot spot radius (per m of hit size)", Float) = 0.3
        _SpotDecay ("Hot spot cooling time (s)", Float) = 0.35
        _SpotGain ("Hot spot gain", Float) = 1.5
        _WaveGain ("Wave gain", Float) = 1
        _HexScale ("Hex cells per metre", Float) = 7
        _HexAmount ("Hex / plasma amount on the wave", Range(0, 1)) = 0.7
        _Inflate ("Inflate along normal (m)", Float) = 0.004
        _Hit0 ("Hit 0", Vector) = (0, 0, 0, -1)
        _Hit1 ("Hit 1", Vector) = (0, 0, 0, -1)
        _Hit2 ("Hit 2", Vector) = (0, 0, 0, -1)
        _Hit3 ("Hit 3", Vector) = (0, 0, 0, -1)
        _HitCol0 ("Hit colour 0", Vector) = (1, 1, 1, 1)
        _HitCol1 ("Hit colour 1", Vector) = (1, 1, 1, 1)
        _HitCol2 ("Hit colour 2", Vector) = (1, 1, 1, 1)
        _HitCol3 ("Hit colour 3", Vector) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
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

            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_PlasmaTex); SAMPLER(sampler_PlasmaTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _FlashColor;
                float4 _Hit0, _Hit1, _Hit2, _Hit3;
                float4 _HitCol0, _HitCol1, _HitCol2, _HitCol3;
                float _Intensity, _Flash, _RimPower, _WaveSpeed, _WaveWidth, _Life;
                float _SpotRadius, _SpotDecay, _SpotGain, _WaveGain, _HexScale, _HexAmount, _Inflate;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;  // absolute world space in URP (name kept from HDRP)
                float3 normalWS : TEXCOORD1;
                float3 hexPos : TEXCOORD2;    // object space x object scale (hex sticks to the hull)
                float3 normalOS : TEXCOORD3;
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
                o.hexPos = v.positionOS * scale;
                o.normalOS = v.normalOS;
                return o;
            }

            float HSq(float x) { return x * x; }

            // 1 on the hex cell borders, 0 inside (p in cell units)
            float HexLine(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 hc = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 h = float4(p - hc.xy * s, p - (hc.zw + 0.5) * s);
                float2 q = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                q = abs(q);
                float d = max(dot(q, s * 0.5), q.x);          // 0 centre .. 0.5 border
                return 1.0 - smoothstep(0.0, 0.07, 0.5 - d);
            }

            float Triplanar(float3 p, float3 n, out float plasma)
            {
                float3 w = pow(abs(n), 4.0);
                w /= max(1e-4, w.x + w.y + w.z);
                float hx = HexLine(p.yz) * w.x + HexLine(p.xz) * w.y + HexLine(p.xy) * w.z;
                float t = _Time.y * 0.15;
                plasma = SAMPLE_TEXTURE2D(_PlasmaTex, sampler_PlasmaTex, p.yz * 0.13 + t).a * w.x
                       + SAMPLE_TEXTURE2D(_PlasmaTex, sampler_PlasmaTex, p.xz * 0.13 - t).a * w.y
                       + SAMPLE_TEXTURE2D(_PlasmaTex, sampler_PlasmaTex, p.xy * 0.13 + t.xx * float2(-1, 1)).a * w.z;
                return hx;
            }

            float3 HitTerm(float4 hit, float4 col, float3 pRWS, float hex, float plasma)
            {
                float age = hit.w;
                float alive = step(0.0, age) * step(age, _Life);
                float size = max(col.a, 0.05);
                float d = distance(pRWS, hit.xyz);                    // URP: world space is absolute
                float life = saturate(1.0 - age / _Life);
                float r = age * _WaveSpeed * size;
                float ring = exp(-HSq((d - r) / (_WaveWidth * size))) * life * life;
                ring *= lerp(1.0, hex * 1.8 + plasma * 0.8, _HexAmount);
                float spot = exp(-HSq(d / (_SpotRadius * size))) * exp(-age / max(1e-3, _SpotDecay));
                float peak = max(col.r, max(col.g, col.b));
                float3 spotCol = lerp(col.rgb, peak.xxx, saturate(spot * spot));   // white-hot centre
                return alive * (col.rgb * ring * _WaveGain + spotCol * spot * _SpotGain);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 vdir = GetWorldSpaceNormalizeViewDir(i.positionRWS);
                float fres = pow(1.0 - saturate(dot(n, vdir)), _RimPower);

                float plasma;
                float hex = Triplanar(i.hexPos * _HexScale, normalize(i.normalOS), plasma);

                float3 c = _FlashColor.rgb * _Flash * (0.22 + 0.78 * fres);
                c += HitTerm(_Hit0, _HitCol0, i.positionRWS, hex, plasma);
                c += HitTerm(_Hit1, _HitCol1, i.positionRWS, hex, plasma);
                c += HitTerm(_Hit2, _HitCol2, i.positionRWS, hex, plasma);
                c += HitTerm(_Hit3, _HitCol3, i.positionRWS, hex, plasma);
                return float4(c * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
