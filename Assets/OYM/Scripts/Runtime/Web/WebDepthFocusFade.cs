using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace OYM.Web
{
    /// Web build (URP): the game's depth haze / space fog (OYM.DepthFocusFade volume settings), which HDRP draws with
    /// the global custom pass DepthFocusFadePass. Same shader (Hidden/OYM/DepthFocusFade, ported), same parameters,
    /// enqueued on every game camera before transparents: copy colour -> optional far glow (pass 1 at quarter res, blurred
    /// by a bilinear down/up chain) -> pass 0 into the camera colour.
    public sealed class WebDepthFocusFade : MonoBehaviour
    {
        /// full-res pixels per glow-source pixel (shader _Glow.w): quarter res on the web, where fill rate is short
        const int GlowDownscale = 4;

        static WebDepthFocusFade _it;
        Material _mat;
        FadePass _pass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!WebLightAdapter.IsUrp || _it) return;
            var sh = Shader.Find(DepthFocusFade.ShaderName);
            if (!sh || !sh.isSupported) { Debug.LogWarning("[OYM-WEB] DepthFocusFade shader missing / unsupported"); return; }
            var go = new GameObject("~WebDepthFocusFade") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _it = go.AddComponent<WebDepthFocusFade>();
            _it._mat = CoreUtils.CreateEngineMaterial(sh);
            _it._pass = new FadePass(_it._mat);
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnCam;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnCam;
        void OnDestroy() { if (_mat) CoreUtils.Destroy(_mat); }

        static Light s_sun; static float s_nextSun;
        static readonly bool s_debug = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymFadeDebug") >= 0;

        static Light FindSun()
        {
            if (s_sun && s_sun.isActiveAndEnabled && s_sun.type == LightType.Directional) return s_sun;
            s_sun = null;
            if (RenderSettings.sun && RenderSettings.sun.isActiveAndEnabled) return s_sun = RenderSettings.sun;
            if (Time.realtimeSinceStartup < s_nextSun) return null;
            s_nextSun = Time.realtimeSinceStartup + 1f;
            float best = 0f;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > best) { best = l.intensity; s_sun = l; }
            return s_sun;
        }

        void OnCam(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_mat || (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)) return;
            var s = VolumeManager.instance.stack?.GetComponent<DepthFocusFade>();
            if (s == null || !s.IsActive()) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType == CameraRenderType.Overlay) return;

            float start = Mathf.Max(0.5f, s.startDistance.value);
            float end = Mathf.Max(start * 1.01f, s.endDistance.value);
            var m = _mat;
            m.SetVector("_FadeRange", new Vector4(start, 1f / Mathf.Log(end / start), s.skyWeight.value, s.intensity.value));
            m.SetVector("_FadeLook", new Vector4(s.desaturation.value, Mathf.Pow(2f, -s.darkenEV.value), Mathf.Max(0.01f, s.highlightLimit.value), s.highlightCompress.value));
            m.SetVector("_HazeColor", s.hazeColor.value.linear);
            m.SetVector("_FadeMisc", new Vector4(s.haze.value, (s.debugView.value || s_debug) ? 1f : 0f, 0f, 0f));
            m.SetVector("_FadeSkyLayer", new Vector4(Mathf.Max(end, s.skyLayerDistance.value), s.skyLayerWeight.value, 0f, 0f));
            m.SetVector("_HazeSky", new Vector4(s.hazeSkyMix.value, s.hazeSkyBlur.value, s.hazeSkyGain.value, 0f));
            float fogAmt = s.fog.value * s.intensity.value;
            m.SetVector("_OYMFogA", new Vector4(fogAmt * s.fogMaxOpacity.value, s.fogStart.value, 1f / Mathf.Max(1f, s.fogDistance.value), s.fogSilhouette.value));
            m.SetVector("_OYMFogB", new Vector4(fogAmt * s.fogSkyOpacity.value, fogAmt * s.fogBackdropOpacity.value, s.fogNebulaTint.value, s.fogNebulaBlur.value));
            var fc = s.fogColor.value.linear;
            m.SetVector("_OYMFogColor", new Vector4(fc.r, fc.g, fc.b, s.fogNebulaGain.value));
            Vector4 sunRad = Vector4.zero, sunDir = new Vector4(0f, 1f, 0f, 0f);
            var sun = fogAmt > 0f && s.fogSunScatter.value > 0f ? FindSun() : null;
            if (sun)
            {
                // the adapter already turned the sun into URP units (lux / pi * exposure): radiance = that * scatter
                Color c = sun.color.linear * sun.intensity;
                if (sun.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature);
                c *= s.fogSunScatter.value;
                sunRad = new Vector4(c.r, c.g, c.b, s.fogSunAnisotropy.value);
                Vector3 toSun = -sun.transform.forward;
                sunDir = new Vector4(toSun.x, toSun.y, toSun.z, 1f);
            }
            m.SetVector("_OYMFogSun", sunRad);
            m.SetVector("_OYMFogSunDir", sunDir);
            // the sky the haze dissolves into: the scene's skybox cubemap (URP's reflection cubemap is a grey default
            // in these unbaked scenes, which turned every far shape pale)
            var sky = RenderSettings.skybox;
            if (sky && sky.HasProperty("_Tex") && sky.GetTexture("_Tex") is Cubemap cube)
            {
                m.SetTexture("_OYMSkyCube", cube);
                float exp = sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f;
                Color tint = sky.HasProperty("_Tint") ? sky.GetColor("_Tint").linear * 2f : Color.white;   // 0.5 grey = x1
                m.SetVector("_OYMSkyMul", new Vector4(tint.r * exp, tint.g * exp, tint.b * exp, 1f));
                float bias = Mathf.Max(0f, Mathf.Log(cube.width / 256f, 2f));                          // HDRP sky texture ~256
                m.SetVector("_OYMSkyParams", new Vector4(bias, sky.HasProperty("_Rotation") ? sky.GetFloat("_Rotation") : 0f, 1f, 0f));
            }
            else m.SetVector("_OYMSkyParams", Vector4.zero);
            bool glowOn = s.glow.value > 0f && !s.debugView.value;
            m.SetVector("_Glow", new Vector4(s.glow.value, Mathf.Max(0.05f, s.glowSourceLimit.value), glowOn ? 1f : 0f, GlowDownscale));
            _pass.glow = glowOn;
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        sealed class FadePass : ScriptableRenderPass
        {
            readonly Material _mat;
            public bool glow;
            static readonly int GlowTex = Shader.PropertyToID("_GlowTex");

            public FadePass(Material m)
            {
                _mat = m;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
                requiresIntermediateTexture = true;
            }

            class Data { public TextureHandle src, glow; public Material mat; public int pass; public bool bindGlow; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var color = res.activeColorTexture;
                var desc = rg.GetTextureDesc(color);
                desc.name = "OYM_DFF_Copy"; desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None;
                desc.depthBufferBits = DepthBits.None;
                var copy = rg.CreateTexture(desc);
                rg.AddBlitPass(color, copy, Vector2.one, Vector2.zero, passName: "OYM DFF copy");

                TextureHandle glowTex = copy;
                if (glow)
                {
                    // far glow source at quarter res (pass 1), then a soft blur: down 2 steps, back up to quarter res.
                    // Same deepest level (1/16) as the HDRP pass's half-res + 3 steps, at a quarter of the fill cost.
                    var hd = desc; hd.width = Mathf.Max(1, desc.width / GlowDownscale); hd.height = Mathf.Max(1, desc.height / GlowDownscale);
                    hd.name = "OYM_DFF_Glow"; hd.colorFormat = GraphicsFormat.R16G16B16A16_SFloat; hd.filterMode = FilterMode.Bilinear;
                    var half = rg.CreateTexture(hd);
                    using (var b = rg.AddRasterRenderPass<Data>("OYM DFF glow prep", out var d))
                    {
                        d.src = copy; d.mat = _mat; d.pass = 1;
                        b.UseTexture(copy);
                        if (res.cameraDepthTexture.IsValid()) b.UseTexture(res.cameraDepthTexture);
                        b.SetRenderAttachment(half, 0);
                        b.AllowGlobalStateModification(true);
                        b.SetRenderFunc((Data x, RasterGraphContext c) => Blitter.BlitTexture(c.cmd, x.src, new Vector4(1, 1, 0, 0), x.mat, x.pass));
                    }
                    var chain = new List<TextureHandle> { half };
                    var cd = hd;
                    for (int i = 0; i < 2; i++)
                    {
                        cd.width = Mathf.Max(1, cd.width / 2); cd.height = Mathf.Max(1, cd.height / 2); cd.name = "OYM_DFF_Down" + i;
                        var t = rg.CreateTexture(cd);
                        rg.AddBlitPass(chain[chain.Count - 1], t, Vector2.one, Vector2.zero, passName: "OYM DFF down");
                        chain.Add(t);
                    }
                    var up = chain[chain.Count - 1];
                    for (int i = chain.Count - 2; i >= 0; i--)
                    {
                        var ud = rg.GetTextureDesc(chain[i]); ud.name = "OYM_DFF_Up" + i;
                        var t = rg.CreateTexture(ud);
                        rg.AddBlitPass(up, t, Vector2.one, Vector2.zero, passName: "OYM DFF up");
                        up = t;
                    }
                    glowTex = up;
                }

                using (var b = rg.AddRasterRenderPass<Data>("OYM DepthFocusFade", out var d))
                {
                    d.src = copy; d.glow = glowTex; d.mat = _mat; d.pass = 0; d.bindGlow = true;
                    b.UseTexture(copy);
                    if (glowTex.Equals(copy) == false) b.UseTexture(glowTex);
                    if (res.cameraDepthTexture.IsValid()) b.UseTexture(res.cameraDepthTexture);
                    b.SetRenderAttachment(color, 0);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((Data x, RasterGraphContext c) =>
                    {
                        x.mat.SetTexture(GlowTex, (RTHandle)x.glow);
                        Blitter.BlitTexture(c.cmd, x.src, new Vector4(1, 1, 0, 0), x.mat, x.pass);
                    });
                }
            }
        }
    }
}
