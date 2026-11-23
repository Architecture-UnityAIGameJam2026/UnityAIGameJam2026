// Focus highlight, step 2 (composite), drawn full screen by OYM.Signal.FocusOutlinePass before post-processing (bloom
// and tonemapping see it), additive onto the camera colour:
//   on the target  : the mask's rgb (thin rim, faint fill, weak-point hot spots)
//   off the target : a contour hugging the visible silhouette (constant width on screen, no gaps on hard-edged
//                    meshes) and a soft halo falling off a few pixels out - from the nearest masked pixel
//                    (8 directions x 6 radii), strength = the mask's alpha, colour = _FocusContourColor
// Light on the target only: no rings, no screen markers.
//
// URP port: a plain fullscreen pass (index 0, "FocusOutline") for a ScriptableRendererFeature. Draw it with
// CoreUtils.DrawFullScreen / cmd.DrawProcedural(3 verts) / Blitter.BlitTexture: the vertex shader builds the fullscreen
// triangle from SV_VertexID. The mask is read with integer pixel loads at the target's pixel position, so the mask RT
// must have the same pixel size as the colour target it is composited onto (_ScreenSize.xy is the clamp bound).
Shader "Hidden/OYM/FocusOutline"
{
    Properties
    {
        _FocusMask ("Mask", 2DArray) = "black" {}
    }

    HLSLINCLUDE

    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        return output;
    }

    // TEXTURE2D_X = Texture2D when XR single-pass instancing is off (always on WebGL), Texture2DArray otherwise.
    TEXTURE2D_X(_FocusMask);
    float4 _FocusContourColor;   // rgb contour colour (linear HDR, screen-relative)
    float4 _FocusShape;          // x contour core (px at 1080p), y halo sigma (px at 1080p), z halo gain, w px scale (height / 1080)

    float4 Outline(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        int2 p = int2(input.positionCS.xy);
        float4 m = LOAD_TEXTURE2D_X(_FocusMask, p);
        if (m.a > 0.0) return float4(m.rgb, 0.0);                       // on the target: the hull light

        const float2 dirs[8] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                                 float2(0.7071, 0.7071), float2(-0.7071, 0.7071), float2(0.7071, -0.7071), float2(-0.7071, -0.7071) };
        const float radii[6] = { 1.0, 2.0, 3.0, 4.5, 6.5, 9.0 };       // px at 1080p
        int2 hi = int2(_ScreenSize.xy) - 1;
        float best = 0.0;
        [unroll] for (int r = 0; r < 6; r++)
        {
            float d = radii[r];
            float h = max(0.0, d - _FocusShape.x);
            float w = d <= _FocusShape.x + 0.5 ? 1.0 : _FocusShape.z * exp(-h * h / (2.0 * _FocusShape.y * _FocusShape.y));
            float px = max(1.0, d * _FocusShape.w);
            [unroll] for (int k = 0; k < 8; k++)
            {
                int2 q = clamp(p + int2(round(dirs[k] * px)), int2(0, 0), hi);
                best = max(best, LOAD_TEXTURE2D_X(_FocusMask, q).a * w);
            }
        }
        return float4(_FocusContourColor.rgb * best, 0.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "FocusOutline"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Outline
            ENDHLSL
        }
    }
    Fallback Off
}
