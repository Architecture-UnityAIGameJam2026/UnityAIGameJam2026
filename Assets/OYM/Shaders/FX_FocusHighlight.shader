// Focus highlight, step 1 (mask). OYM.Signal.FocusOutlinePass (global HDRP custom pass, before post) draws the focused
// target's renderers with this - cmd.DrawRenderer on every LOD, their union - into a half-float mask; step 2
// (Hidden/OYM/FocusOutline) adds it to the picture before bloom:
//   rgb = the light on the hull: a thin fresnel rim + a faint fill + hot spots on the weak points (x the focus level)
//   a   = contour strength (x the focus level) wherever the target is visible; step 2 draws the contour from it
// Visible = nothing nearer in the camera depth, with a tolerance (the LOD drawn here may not be the one on screen).
// LightMode OYMFocusMask: HDRP never draws this pass itself, so the targets' own materials are never touched.
// Colours are Vectors (linear, screen-relative HDR, set with SetVector: SetColor would gamma-convert them).
Shader "OYM/FX/FocusHighlight"
{
    Properties
    {
        _FocColor ("Colour x level (linear HDR, screen-relative)", Vector) = (1, 0.62, 0.26, 1)
        _FocRim ("Rim", Float) = 0.25
        _FocRimPower ("Rim power", Float) = 4
        _FocFill ("Fill", Float) = 0.012
        _FocContour ("Contour strength (x level)", Float) = 1
        _FocDepthTol ("Depth tolerance (fraction of the distance)", Float) = 0.02
        _FocSpotColor ("Spot colour (linear HDR)", Vector) = (1, 0.32, 0.46, 1)
        _FocSpotGain ("Spot gain (x level)", Float) = 0
        _FocSpot0 ("Spot 0 (world xyz, radius m; w <= 0 off)", Vector) = (0, 0, 0, 0)
        _FocSpot1 ("Spot 1", Vector) = (0, 0, 0, 0)
        _FocSpot2 ("Spot 2", Vector) = (0, 0, 0, 0)
        _FocSpot3 ("Spot 3", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "FocusMask"
            Tags { "LightMode" = "OYMFocusMask" }
            ZWrite Off
            ZTest Always
            Cull Off                // mirrored claws (negative scale) keep their faces; the depth test drops the far side
            BlendOp Max
            Blend One One           // LODs / overlapping slots: the strongest wins

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FocColor, _FocSpotColor;
                float4 _FocSpot0, _FocSpot1, _FocSpot2, _FocSpot3;
                float _FocRim, _FocRimPower, _FocFill, _FocContour, _FocDepthTol, _FocSpotGain;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionRWS = TransformObjectToWorld(v.positionOS);    // camera-relative (UNITY_MATRIX_M applies it)
                o.positionCS = TransformWorldToHClip(o.positionRWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float FocusSpot(float4 s, float3 pRWS)
            {
                if (s.w <= 0.0) return 0.0;
                float d = distance(pRWS, GetCameraRelativePositionWS(s.xyz)) / s.w;
                return exp(-d * d);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                // hidden behind something nearer (a rock, the boss's own body): not part of the visible target
                float sceneEye = LinearEyeDepth(LoadCameraDepth(uint2(i.positionCS.xy)), _ZBufferParams);
                float myEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                if (myEye > sceneEye * (1.0 + _FocDepthTol) + 0.1) discard;

                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionRWS);
                float fres = pow(1.0 - abs(dot(n, v)), _FocRimPower);
                float3 c = _FocColor.rgb * (_FocFill + _FocRim * fres);
                float s = max(max(FocusSpot(_FocSpot0, i.positionRWS), FocusSpot(_FocSpot1, i.positionRWS)),
                              max(FocusSpot(_FocSpot2, i.positionRWS), FocusSpot(_FocSpot3, i.positionRWS)));
                float peak = max(_FocSpotColor.r, max(_FocSpotColor.g, _FocSpotColor.b));
                c += lerp(_FocSpotColor.rgb, peak.xxx, s * s) * (s * _FocSpotGain);   // white-hot at the centre of the light
                return float4(c, max(_FocContour, 1e-3));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
