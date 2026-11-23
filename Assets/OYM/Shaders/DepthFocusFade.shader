// Depth focus fade (OYM.DepthFocusFade settings). HDRP: drawn by the global custom pass OYM.DepthFocusFadePass at
// Before Pre-Refraction. URP: draw it from a ScriptableRendererFeature after opaques + skybox, before transparents
// (RenderPassEvent.AfterRenderingSkybox / BeforeRenderingTransparents), with the camera Depth Texture available.
// Far opaque geometry, the sky and the fog in front of them are squashed, desaturated, darkened and dissolved into the
// blurred nebula behind them, then a luminous dust fog (exponential in distance, thinner veil on the sky and the planet,
// tinted by the nebula, brighter toward the sun) turns far debris into soft shapes, and a depth glow (the luminous
// backdrop - planet, nebula, far layers - blurred wide) washes over the far layers in proportion to their distance, so
// each depth layer is eaten a little more by the light behind it (EVE-style layering) while the foreground stays crisp.
// Works on the HDR colour buffer after opaques and the skybox, before transparents and bloom.
//   pass 0  DepthFocusFade  full res, _BlitTexture = copy of the camera colour, _GlowTex = blurred glow (pass 1 output)
//   pass 1  GlowPrep        half res, _BlitTexture = copy of the camera colour (full res),
//                           writes (far colour * source weight, source weight) for the blur
// Both passes draw a fullscreen triangle from SV_VertexID (Blitter.BlitTexture / BlitCameraTexture, RenderGraph
// AddBlitPass, or cmd.DrawProcedural(.., Triangles, 3)). They only use pixel positions, never the blit uv, so
// _BlitScaleBias does not matter. Source pixels are LOADed: the source copy must match the camera depth texture size.
// URP port: no exposure (multiplier 1), sky = URP's reflection cubemap (_GlossyEnvironmentCubeMap: the convolved
// skybox / custom reflection) instead of HDRP's sky texture, world space is absolute (no camera-relative rendering).
Shader "Hidden/OYM/DepthFocusFade"
{
    Properties
    {
        _OYMSkyCube ("Sky cubemap (web)", Cube) = "black" {}
        // kept for compatibility (HDRP bound the source here); the URP passes read _BlitTexture
        _MainTex("Main Texture", 2D) = "grey" {}
    }

    HLSLINCLUDE

    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    // Attributes / Varyings / Vert (SV_VertexID fullscreen triangle), _BlitTexture, sampler_LinearClamp
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D_X(_GlowTex);
    float4 _FadeRange;    // x start distance (m), y 1/ln(end/start), z sky weight, w master intensity
    float4 _FadeLook;     // x desaturation, y darken multiplier (2^-EV), z highlight limit, w highlight compress
    float4 _HazeColor;    // rgb flat haze colour (linear)
    float4 _FadeMisc;     // x haze amount, y debug view
    float4 _FadeSkyLayer; // x backdrop-layer distance (m), y fade amount there (planet: baked look kept)
    float4 _HazeSky;      // x sky mix (0 flat colour .. 1 blurred sky behind the pixel), y sky mip, z sky haze gain
    float4 _Glow;         // x glow on far layers, y glow-source luminance cap, z 1 = glow texture valid
    float4 _OYMFogA;      // x max fog opacity on geometry (0 = fog off), y fog start (m), z 1 / fog length (1/m),
                          // w silhouette (0 = converge to the fog colour .. 1 = to the fogged sky behind the pixel)
    float4 _OYMFogB;      // x sky veil, y backdrop (planet) veil, z nebula tint, w nebula tint mip
    float4 _OYMFogColor;  // rgb flat fog colour (linear), w nebula tint gain
    float4 _OYMFogSun;    // rgb sunlight scattered by the fog (linear), w anisotropy g
    float4 _OYMFogSunDir; // xyz direction toward the sun (world)

    struct FadeInfo
    {
        float t;      // overall fade (highlight squash, haze, glow)
        float tGeo;   // desaturate / darken amount (0 on the sky so the nebula keeps its colour)
        float src;    // weight as a glow source: sky + backdrop fully, far geometry by distance, near nothing
        float fog;    // fog opacity
        float isGeo;  // 1 = arena geometry (fog converges toward the fogged sky behind it), 0 = sky / backdrop
    };

    // full-res camera size in pixels (the depth texture's), valid in the half-res GlowPrep pass too
    float2 FullResSize()    { return _CameraDepthTexture_TexelSize.zw; }
    float2 FullResInvSize() { return _CameraDepthTexture_TexelSize.xy; }

    // world position of a full-res pixel (positionSS = pixel centre) at a raw depth-buffer value
    float3 WorldPosAt(float2 positionSS, float deviceDepth)
    {
        float2 uv = positionSS * FullResInvSize();
    #if !UNITY_REVERSED_Z
        deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);   // GL / WebGL: depth texture 0..1 -> clip -1..1
    #endif
        return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
    }

    // camera -> pixel direction
    float3 ViewDirWS(float2 positionSS)
    {
        float3 p = WorldPosAt(positionSS, 0.5);
        return normalize(p - GetCameraPositionWS());
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
            float dist = length(WorldPosAt(positionSS, deviceDepth) - GetCameraPositionWS());
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

    // the blurred sky (nebula) in a direction. HDRP sampled its GGX-convolved sky texture x exposure; URP: the
    // convolved reflection cubemap of the skybox (Lighting > Environment), no exposure.
    TEXTURECUBE(_OYMSkyCube); SAMPLER(sampler_OYMSkyCube);
    float4 _OYMSkyCube_HDR;
    float4 _OYMSkyParams;   // x mip bias (cube size vs HDRP's sky texture), y rotation (deg), z on, w unused
    float4 _OYMSkyMul;      // rgb skybox tint x exposure

    float3 BlurredSky(float3 V, float mip)
    {
        if (_OYMSkyParams.z > 0.5)
        {
            // the scene's own skybox (what HDRP sampled), turned like Skybox/Cubemap's _Rotation
            float a = radians(-_OYMSkyParams.y), sn = sin(a), cs = cos(a);
            float3 d = float3(cs * V.x - sn * V.z, V.y, sn * V.x + cs * V.z);
            float4 e = SAMPLE_TEXTURECUBE_LOD(_OYMSkyCube, sampler_OYMSkyCube, d, mip + _OYMSkyParams.x);
            return DecodeHDREnvironment(e, _OYMSkyCube_HDR) * _OYMSkyMul.rgb;
        }
        float4 enc = SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, V, mip);
        return DecodeHDREnvironment(enc, _GlossyEnvironmentCubeMap_HDR);
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
        col += _OYMFogSun.rgb * ((1.0 - g * g) * rsqrt(denom * denom * denom));
        return col;
    }

    float4 DepthFocusFade(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 positionSS = (uint2)input.positionCS.xy;
        float4 src = LOAD_TEXTURE2D_X(_BlitTexture, positionSS);
        FadeInfo f = GetFade(input.positionCS.xy, LoadSceneDepth(positionSS));
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
            float2 uv = min(input.positionCS.xy * FullResInvSize(), 1.0 - FullResInvSize());
            float4 g = SAMPLE_TEXTURE2D_X_LOD(_GlowTex, sampler_LinearClamp, uv, 0);
            c += g.rgb / max(g.a, 0.02) * (_Glow.x * f.t);     // weight-normalised: near objects leave no dark halo
        }

        return float4(c, src.a);
    }

    // half-res glow source: 2x2 full-res pixels, shaped like the fade (far emissive cuts stay squashed), luminance
    // capped so the sun / flares do not dominate, weighted by how far / how "backdrop" each pixel is
    float4 GlowPrep(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        uint2 base = (uint2)input.positionCS.xy * 2u;
        uint2 maxPx = (uint2)FullResSize() - 1u;
        float4 acc = 0.0;
        [unroll]
        for (uint i = 0u; i < 4u; i++)
        {
            uint2 p = min(base + uint2(i & 1u, i >> 1u), maxPx);
            float3 c = LOAD_TEXTURE2D_X(_BlitTexture, p).rgb;
            FadeInfo f = GetFade((float2)p + 0.5, LoadSceneDepth(p));
            c = ShapeFar(c, f.t, f.tGeo);
            c *= min(1.0, _Glow.y / max(Luminance(c), 1e-4));
            acc += float4(c * f.src, f.src);
        }
        return acc * 0.25;
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
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
