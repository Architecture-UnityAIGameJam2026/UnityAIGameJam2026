// Depth focus fade (OYM.DepthFocusFade settings, drawn by the HDRP global custom pass OYM.DepthFocusFadePass at
// Before Pre-Refraction). Far opaque geometry, the sky and the fog in front of them are squashed, desaturated,
// darkened and pulled toward a haze colour so the foreground fight reads first. Works on the pre-exposed HDR colour
// buffer after the opaque fog composite, before transparents and bloom. _MainTex is a copy of the camera colour.
Shader "Hidden/OYM/DepthFocusFade"
{
    Properties
    {
        // necessary so the source texture binds to _MainTex
        _MainTex("Main Texture", 2DArray) = "grey" {}
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    TEXTURE2D_X(_MainTex);
    float4 _FadeRange;   // x start distance (m), y 1/ln(end/start), z sky weight, w master intensity
    float4 _FadeLook;    // x desaturation, y darken multiplier (2^-EV), z highlight limit, w highlight compress
    float4 _HazeColor;   // rgb haze colour (linear, pre-exposed)
    float4 _FadeMisc;    // x haze amount, y debug view

    float FadeAmount(float2 positionSS, float deviceDepth)
    {
        if (deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)          // sky: no geometry
            return _FadeRange.z;

        PositionInputs posInput = GetPositionInput(positionSS, _ScreenSize.zw, deviceDepth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float dist = length(posInput.positionWS - GetPrimaryCameraPosition());   // camera-relative rendering: origin = camera
        float x = saturate(log(max(dist, 1e-3) / _FadeRange.x) * _FadeRange.y);
        return x * x * (3.0 - 2.0 * x);                        // smooth in and out of the ramp
    }

    float4 DepthFocusFade(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 positionSS = (uint2)input.positionCS.xy;
        float4 src = LOAD_TEXTURE2D_X(_MainTex, positionSS);
        float depth = LoadCameraDepth(positionSS);

        float t = FadeAmount(input.positionCS.xy, depth) * _FadeRange.w;
        if (_FadeMisc.y > 0.5)
            return float4(t.xxx, src.a);

        float3 c = src.rgb;
        float lum = Luminance(c);

        // 1. squash far highlights toward the limit (soft: lum / sqrt(1 + (lum/limit)^2)); kills far glow + bloom
        float r = lum / _FadeLook.z;
        float squash = lerp(1.0, rsqrt(1.0 + r * r), t * _FadeLook.w);
        c *= squash;
        lum *= squash;

        // 2. desaturate
        c = lerp(c, lum.xxx, t * _FadeLook.x);

        // 3. darken
        c *= lerp(1.0, _FadeLook.y, t);

        // 4. dissolve into haze (lifts far blacks, flattens contrast)
        c = lerp(c, _HazeColor.rgb, t * _FadeMisc.x);

        return float4(c, src.a);
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "DepthFocusFade"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment DepthFocusFade
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
