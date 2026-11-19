// Depth focus fade (OYM.DepthFocusFade settings, drawn by the HDRP global custom pass OYM.DepthFocusFadePass at
// Before Pre-Refraction). Far opaque geometry, the sky and the fog in front of them are squashed, desaturated,
// darkened and dissolved into the blurred nebula behind them, then a luminous dust fog (exponential in distance,
// thinner veil on the sky and the planet, tinted by the nebula, brighter toward the sun) turns far debris into soft
// shapes, and a depth glow (the luminous backdrop - planet, nebula, far layers - blurred wide) washes over the far
// layers in proportion to their distance, so each depth layer is eaten a little more by the light behind it
// (EVE-style layering) while the foreground fight stays crisp.
// Works on the pre-exposed HDR colour buffer after the opaque fog composite, before transparents and bloom.
//   pass 0  DepthFocusFade  full res, _MainTex = copy of the camera colour, _GlowTex = blurred glow (pass 1 output)
//   pass 1  GlowPrep        half res, writes (far colour * source weight, source weight) for the blur
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
    TEXTURE2D_X(_GlowTex);
    float4 _FadeRange;    // x start distance (m), y 1/ln(end/start), z sky weight, w master intensity
    float4 _FadeLook;     // x desaturation, y darken multiplier (2^-EV), z highlight limit, w highlight compress
    float4 _HazeColor;    // rgb flat haze colour (linear, pre-exposed)
    float4 _FadeMisc;     // x haze amount, y debug view
    float4 _FadeSkyLayer; // x backdrop-layer distance (m), y fade amount there (planet: baked look kept)
    float4 _HazeSky;      // x sky mix (0 flat colour .. 1 blurred sky behind the pixel), y sky mip, z sky haze gain
    float4 _Glow;         // x glow on far layers, y glow-source luminance cap, z 1 = glow texture valid
    float4 _OYMFogA;         // x max fog opacity on geometry (0 = fog off), y fog start (m), z 1 / fog length (1/m),
                          // w silhouette (0 = converge to the fog colour .. 1 = to the fogged sky behind the pixel)
    float4 _OYMFogB;         // x sky veil, y backdrop (planet) veil, z nebula tint, w nebula tint mip
    float4 _OYMFogColor;     // rgb flat fog colour (linear, pre-exposed), w nebula tint gain
    float4 _OYMFogSun;       // rgb sunlight scattered by the fog (linear, absolute: x exposure here), w anisotropy g
    float4 _OYMFogSunDir;    // xyz direction toward the sun (world)

    struct FadeInfo
    {
        float t;      // overall fade (highlight squash, haze, glow)
        float tGeo;   // desaturate / darken amount (0 on the sky so the nebula keeps its colour)
        float src;    // weight as a glow source: sky + backdrop fully, far geometry by distance, near nothing
        float fog;    // fog opacity
        float isGeo;  // 1 = arena geometry (fog converges toward the fogged sky behind it), 0 = sky / backdrop
    };

    // camera -> pixel direction (camera-relative rendering: the camera sits at the origin)
    float3 ViewDirWS(float2 positionSS)
    {
        float3 p = ComputeWorldSpacePosition(positionSS * _ScreenSize.zw, 0.5, UNITY_MATRIX_I_VP);
        return normalize(p - GetPrimaryCameraPosition());
    }

    float FogOpacity(float dist)
    {
        return _OYMFogA.x * (1.0 - exp(-max(dist - _OYMFogA.y, 0.0) * _OYMFogA.z));
    }

    FadeInfo GetFade(float2 positionSS, float deviceDepth)
    {
        FadeInfo f;
        if (deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)          // sky: no geometry
        {
            f.t = _FadeRange.z; f.tGeo = 0.0; f.src = 1.0;
            f.fog = _OYMFogB.x; f.isGeo = 0.0;
        }
        else
        {
            PositionInputs posInput = GetPositionInput(positionSS, _ScreenSize.zw, deviceDepth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            float dist = length(posInput.positionWS - GetPrimaryCameraPosition());
            if (_FadeSkyLayer.x > 0.0 && dist > _FadeSkyLayer.x)
            {
                // backdrop (planet): kept as baked, but it lights the haze
                f.t = _FadeSkyLayer.y; f.tGeo = f.t; f.src = 1.0;
                f.fog = _OYMFogB.y; f.isGeo = 0.0;
            }
            else
            {
                float x = saturate(log(max(dist, 1e-3) / _FadeRange.x) * _FadeRange.y);
                f.t = x * x * (3.0 - 2.0 * x);                 // smooth in and out of the ramp
                f.tGeo = f.t; f.src = f.t;
                f.fog = FogOpacity(dist); f.isGeo = 1.0;
            }
        }
        f.t *= _FadeRange.w; f.tGeo *= _FadeRange.w; f.src *= _FadeRange.w;
        return f;
    }

    // 1. squash far highlights toward the limit (soft: lum / sqrt(1 + (lum/limit)^2)); kills far glow + bloom, and on
    //    the sky tames the harshest stars;  2. desaturate;  3. darken  (2 and 3 geometry only)
    float3 ShapeFar(float3 c, float t, float tGeo)
    {
        float lum = Luminance(c);
        float r = lum / _FadeLook.z;
        float squash = lerp(1.0, rsqrt(1.0 + r * r), t * _FadeLook.w);
        c *= squash;
        lum *= squash;
        c = lerp(c, lum.xxx, tGeo * _FadeLook.x);
        c *= lerp(1.0, _FadeLook.y, tGeo);
        return c;
    }

    // the blurred sky (nebula) in a direction, pre-exposed. _SkyTexture is HDRP's GGX-convolved sky cubemap (absolute
    // radiance), the same one the sky-colour fog samples.
    float3 BlurredSky(float3 V, float mip)
    {
        return SampleSkyTexture(V, mip, 0).rgb * GetCurrentExposureMultiplier();
    }

    // Light the fog sends to the eye along V: flat colour tinted by the very blurred nebula in that direction, plus
    // sunlight scattered toward the eye (Henyey-Greenstein, normalised so isotropic = 1: brighter toward the sun).
    float3 FogInscatter(float3 V)
    {
        float3 col = _OYMFogColor.rgb;
        if (_OYMFogB.z > 0.0)
            col = lerp(col, BlurredSky(V, _OYMFogB.w) * _OYMFogColor.w, _OYMFogB.z);
        float g = _OYMFogSun.w;
        float denom = max(1.0 + g * g - 2.0 * g * dot(V, _OYMFogSunDir.xyz), 1e-4);
        col += _OYMFogSun.rgb * (GetCurrentExposureMultiplier() * (1.0 - g * g) * rsqrt(denom * denom * denom));
        return col;
    }

    float4 DepthFocusFade(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 positionSS = (uint2)input.positionCS.xy;
        float4 src = LOAD_TEXTURE2D_X(_MainTex, positionSS);
        FadeInfo f = GetFade(input.positionCS.xy, LoadCameraDepth(positionSS));
        if (_FadeMisc.y > 0.5)
            return float4(f.t, f.fog, 0.0, src.a);
        if (f.t <= 0.0 && f.fog <= 0.0)
            return src;

        float3 V = ViewDirWS(input.positionCS.xy);
        float3 c = ShapeFar(src.rgb, f.t, f.tGeo);

        // 4. dissolve into the haze: far hulls melt into the nebula behind them, the sky gets a softer cloud layer
        float3 skyBehind = BlurredSky(V, _HazeSky.y);
        if (f.t > 0.0)
            c = lerp(c, lerp(_HazeColor.rgb, skyBehind * _HazeSky.z, _HazeSky.x), f.t * _FadeMisc.x);

        // 5. fog: geometry converges (by its opacity) toward the fog light, or - with silhouette - toward the fogged
        //    sky right behind it, so a far shape fades into its own backdrop instead of turning lighter than the sky;
        //    the sky and the planet just get their veil
        if (f.fog > 0.0)
        {
            float3 F = FogInscatter(V);
            float3 target = F;
            if (f.isGeo > 0.5)
                target = lerp(F, lerp(skyBehind, F, _OYMFogB.x), _OYMFogA.w);
            c = lerp(c, target, f.fog);
        }

        // 6. depth glow: the light of the backdrop and the far layers, blurred wide, washes over each far pixel in
        //    proportion to its distance - the further a layer, the more the glow behind it eats into its edges
        if (_Glow.z > 0.5 && f.t > 0.0)
        {
            float2 uv = min(input.positionCS.xy * _ScreenSize.zw, 1.0 - _ScreenSize.zw) * _RTHandleScale.xy;
            float4 g = SAMPLE_TEXTURE2D_X_LOD(_GlowTex, s_linear_clamp_sampler, uv, 0);
            c += g.rgb / max(g.a, 0.02) * (_Glow.x * f.t);     // weight-normalised: near objects leave no dark halo
        }

        return float4(c, src.a);
    }

    // half-res glow source: 2x2 full-res pixels, shaped like the fade (far emissive cuts stay squashed), luminance
    // capped so the sun / flares do not dominate, weighted by how far / how "backdrop" each pixel is
    float4 GlowPrep(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 base = (uint2)input.positionCS.xy * 2;
        uint2 maxPx = (uint2)_ScreenSize.xy - 1;
        float4 acc = 0.0;
        [unroll]
        for (uint i = 0; i < 4; i++)
        {
            uint2 p = min(base + uint2(i & 1, i >> 1), maxPx);
            float3 c = LOAD_TEXTURE2D_X(_MainTex, p).rgb;
            FadeInfo f = GetFade((float2)p + 0.5, LoadCameraDepth(p));
            c = ShapeFar(c, f.t, f.tGeo);
            c *= min(1.0, _Glow.y / max(Luminance(c), 1e-4));
            acc += float4(c * f.src, f.src);
        }
        return acc * 0.25;
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
        Pass
        {
            Name "GlowPrep"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment GlowPrep
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
