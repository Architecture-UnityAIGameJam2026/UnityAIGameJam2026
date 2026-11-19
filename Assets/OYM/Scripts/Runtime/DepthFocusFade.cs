using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Atmospheric-perspective "focus" settings: the further a pixel is from the camera, the more it is squashed,
    /// desaturated, darkened and dissolved into the blurred nebula behind it (EVE-style aerial perspective), so the busy far debris field (glowing hull cuts at
    /// 0.9-2.6 km, reactor-core glow at ~1 km) recedes and the fight in front reads first.
    ///
    /// Fog (11-19): on top of that, a luminous dust fog fills the arena. Its opacity grows exponentially with distance,
    /// so far debris is reduced to soft shapes ("shape only") while the fight in front stays crisp; the sky and the planet
    /// get a thinner veil. The fog colour is a flat colour tinted by the (very blurred) nebula in each direction, plus
    /// sunlight scattered forward (brighter toward the sun). FogCulling uses the same opacity on the CPU: objects deep in
    /// the fog are held at their lowest LOD and stop being drawn once they are too small and faint to see.
    ///
    /// The volume component only carries the (blendable) settings; DepthFocusFadePass renders it as an HDRP
    /// *global* custom pass at Before Pre-Refraction, i.e. after opaques, sky and the volumetric fog composite,
    /// before any transparent. Transparent FX (hit sparks, beams, thruster plumes) therefore stay at full strength
    /// wherever they are, and since it runs before bloom, far emissive edges also stop blooming.
    /// Distance ramps on a log scale between start and end distance.
    [Serializable, VolumeComponentMenu("Post-processing/OYM/Depth Focus Fade")]
    public sealed class DepthFocusFade : VolumeComponent
    {
        public const string ShaderName = "Hidden/OYM/DepthFocusFade";

        [Tooltip("Master strength. 0 = off.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Header("Distance (log ramp)")]
        [Tooltip("Metres from the camera where fading starts. Everything closer is untouched.")]
        public MinFloatParameter startDistance = new MinFloatParameter(80f, 0.5f);
        [Tooltip("Metres from the camera where the fade is complete.")]
        public MinFloatParameter endDistance = new MinFloatParameter(1600f, 1f);
        [Tooltip("Fade amount applied to sky pixels (stars, nebula), which have no distance.")]
        public ClampedFloatParameter skyWeight = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Geometry beyond this distance is a backdrop layer (the giant planet at 33+ km), not far debris.")]
        public MinFloatParameter skyLayerDistance = new MinFloatParameter(15000f, 1f);
        [Tooltip("Fade amount for backdrop-layer pixels. 0 keeps the planet as authored (it is baked for this look).")]
        public ClampedFloatParameter skyLayerWeight = new ClampedFloatParameter(0f, 0f, 1f);

        [Header("Look at full fade")]
        [Tooltip("0 = keep colour, 1 = greyscale.")]
        public ClampedFloatParameter desaturation = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("Exposure stops taken away.")]
        public ClampedFloatParameter darkenEV = new ClampedFloatParameter(1f, 0f, 8f);
        [Tooltip("Luminance ceiling (pre-exposed, 1 ~ white) that far highlights are softly squashed toward. Kills the glow and bloom of far emissive edges and far light glows.")]
        public MinFloatParameter highlightLimit = new MinFloatParameter(0.5f, 0.01f);
        [Tooltip("How much of the highlight squash is applied.")]
        public ClampedFloatParameter highlightCompress = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Colour that far geometry dissolves into (pre-exposed; the swatch is sRGB like any material colour, 1 ~ white).")]
        public ColorParameter hazeColor = new ColorParameter(new Color(0.19f, 0.22f, 0.26f), true, false, true);
        [Tooltip("How far far geometry is dissolved into the haze (and how much the sky is softened toward its blurred self).")]
        public ClampedFloatParameter haze = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("0 = haze is the flat Haze Color, 1 = haze is the blurred sky (nebula) right behind each pixel.")]
        public ClampedFloatParameter hazeSkyMix = new ClampedFloatParameter(0.85f, 0f, 1f);
        [Tooltip("Blur of the sampled sky (mip of HDRP's convolved sky cubemap, 0 sharp .. 6 flat).")]
        public ClampedFloatParameter hazeSkyBlur = new ClampedFloatParameter(3f, 0f, 6f);
        [Tooltip("Brightness of the sampled sky haze relative to the sky itself. Above 1 the far haze glows a little brighter than the nebula behind it.")]
        public ClampedFloatParameter hazeSkyGain = new ClampedFloatParameter(1.25f, 0f, 8f);

        [Header("Depth glow")]
        [Tooltip("Light of the backdrop (planet, nebula) and the far layers, blurred wide and laid over each far pixel in proportion to its distance: the further a layer, the more the glow behind it eats into it. 0 = off (skips the blur passes).")]
        public ClampedFloatParameter glow = new ClampedFloatParameter(0.6f, 0f, 2f);
        [Tooltip("Glow spread as a fraction of the screen height.")]
        public ClampedFloatParameter glowSize = new ClampedFloatParameter(0.08f, 0.01f, 0.25f);
        [Tooltip("Luminance cap (pre-exposed, 1 ~ white) of what feeds the glow, so the sun disc and flares do not dominate it.")]
        public MinFloatParameter glowSourceLimit = new MinFloatParameter(3f, 0.05f);

        [Header("Fog (luminous dust in the arena)")]
        [Tooltip("Master fog amount (also scaled by Intensity). 0 = no fog.")]
        public ClampedFloatParameter fog = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Metres from the camera where the fog starts.")]
        public MinFloatParameter fogStart = new MinFloatParameter(20f, 0f);
        [Tooltip("Fog length in metres: beyond the start, opacity reaches 63 % of the maximum at this distance and 86 % at twice it.")]
        public MinFloatParameter fogDistance = new MinFloatParameter(1200f, 1f);
        [Tooltip("Maximum fog opacity on geometry. Below 1 the giant far hulls stay readable as shapes.")]
        public ClampedFloatParameter fogMaxOpacity = new ClampedFloatParameter(0.9f, 0f, 1f);
        [Tooltip("Fog veil over the sky (stars, nebula).")]
        public ClampedFloatParameter fogSkyOpacity = new ClampedFloatParameter(0.35f, 0f, 1f);
        [Tooltip("Fog veil over the backdrop layer (the giant planet beyond Sky Layer Distance).")]
        public ClampedFloatParameter fogBackdropOpacity = new ClampedFloatParameter(0.2f, 0f, 1f);
        [Tooltip("Flat fog colour (pre-exposed; the swatch is sRGB like a material colour, 1 ~ white).")]
        public ColorParameter fogColor = new ColorParameter(new Color(0.3f, 0.36f, 0.42f), true, false, true);
        [Tooltip("How much the fog takes on the colour of the very blurred nebula in each direction.")]
        public ClampedFloatParameter fogNebulaTint = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Blur of the nebula tint (mip of the convolved sky cubemap).")]
        public ClampedFloatParameter fogNebulaBlur = new ClampedFloatParameter(5f, 0f, 6f);
        [Tooltip("Brightness of the nebula tint relative to the sky itself.")]
        public ClampedFloatParameter fogNebulaGain = new ClampedFloatParameter(2f, 0f, 16f);
        [Tooltip("Sunlight scattered by the fog, relative to a white surface lit by the sun (the main directional light).")]
        public ClampedFloatParameter fogSunScatter = new ClampedFloatParameter(0.04f, 0f, 1f);
        [Tooltip("Forward scattering of the sunlight (Henyey-Greenstein g): higher = the fog glows more toward the sun.")]
        public ClampedFloatParameter fogSunAnisotropy = new ClampedFloatParameter(0.6f, 0f, 0.95f);
        [Tooltip("What fogged geometry converges to: 0 = the fog colour (pale shapes), 1 = the fogged sky right behind it (shapes dissolve into the backdrop without turning lighter than the sky).")]
        public ClampedFloatParameter fogSilhouette = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Header("Fog culling (optimisation, OYM.FogCulling)")]
        [Tooltip("Objects deep in the fog are held at their lowest LOD and are not drawn once they are too small and faint to see.")]
        public BoolParameter fogCulling = new BoolParameter(false);
        [Tooltip("Fog opacity from which LOD groups are held at their lowest LOD (shape only).")]
        public ClampedFloatParameter cullSilhouetteOpacity = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("An object is not drawn when its on-screen radius (pixels) times its remaining contrast (1 - fog opacity) drops below this.")]
        public ClampedFloatParameter cullPixels = new ClampedFloatParameter(1.5f, 0f, 16f);

        [Header("Debug")]
        [Tooltip("Show the fade amount (red) and the fog opacity (green); black = untouched.")]
        public BoolParameter debugView = new BoolParameter(false);

        public bool IsActive() => intensity.value > 0f;

        /// Fog opacity on geometry at a distance (m) - the same curve the shader uses.
        public float FogOpacity(float distance)
        {
            float amount = fog.value * intensity.value * fogMaxOpacity.value;
            if (amount <= 0f) return 0f;
            return amount * (1f - Mathf.Exp(-Mathf.Max(0f, distance - fogStart.value) / Mathf.Max(1f, fogDistance.value)));
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            DepthFocusFadePass.EnsureRegistered();
        }
    }

    /// Global custom pass that renders DepthFocusFade from each camera's volume stack. Registered once per domain
    /// (editor load, play mode, player start), so no scene object is needed.
    [Serializable]
    public sealed class DepthFocusFadePass : CustomPass
    {
        public const CustomPassInjectionPoint InjectionPoint = CustomPassInjectionPoint.BeforePreRefraction;

        static DepthFocusFadePass s_Instance;

        Material m_Material;
        RTHandle m_Copy;                     // full-res copy of the camera colour (the pass reads it, writes the camera)
        RTHandle m_GlowSrc, m_GlowTmp, m_Glow;   // half res: weighted glow source, blur scratch, blurred glow

        static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int ID_Range = Shader.PropertyToID("_FadeRange");
        static readonly int ID_Look = Shader.PropertyToID("_FadeLook");
        static readonly int ID_Haze = Shader.PropertyToID("_HazeColor");
        static readonly int ID_Misc = Shader.PropertyToID("_FadeMisc");
        static readonly int ID_Sky = Shader.PropertyToID("_FadeSkyLayer");
        static readonly int ID_HazeSky = Shader.PropertyToID("_HazeSky");
        static readonly int ID_Glow = Shader.PropertyToID("_Glow");
        static readonly int ID_GlowTex = Shader.PropertyToID("_GlowTex");
        static readonly int ID_FogA = Shader.PropertyToID("_OYMFogA");
        static readonly int ID_FogB = Shader.PropertyToID("_OYMFogB");
        static readonly int ID_FogColor = Shader.PropertyToID("_OYMFogColor");
        static readonly int ID_FogSun = Shader.PropertyToID("_OYMFogSun");
        static readonly int ID_FogSunDir = Shader.PropertyToID("_OYMFogSunDir");
        const int PassFade = 0, PassGlowPrep = 1;

        static Light s_Sun;
        static float s_NextSunSearch;

        public static void EnsureRegistered()
        {
            if (s_Instance != null) return;
            s_Instance = new DepthFocusFadePass { name = "OYM Depth Focus Fade" };
            CustomPassVolume.RegisterUniqueGlobalCustomPass(InjectionPoint, s_Instance);
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Unregister;
#endif
        }

        static void Unregister()
        {
            if (s_Instance == null) return;
            CustomPassVolume.UnregisterGlobalCustomPass(s_Instance);
            s_Instance.Cleanup();
            s_Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeInit() => EnsureRegistered();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit() => EnsureRegistered();
#endif

        /// The sun the fog scatters: HDRP's main directional light, else RenderSettings.sun, else the brightest
        /// directional light (searched at most once a second).
        static Light FindSun()
        {
            if (s_Sun != null && s_Sun.isActiveAndEnabled && s_Sun.type == LightType.Directional) return s_Sun;
            s_Sun = null;
            var hd = RenderPipelineManager.currentPipeline as HDRenderPipeline;
            var main = hd != null ? hd.GetMainLight() : null;
            if (main != null && main.isActiveAndEnabled) return s_Sun = main;
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return s_Sun = RenderSettings.sun;
            float now = Time.realtimeSinceStartup;
            if (now < s_NextSunSearch) return null;
            s_NextSunSearch = now + 1f;
            float best = 0f;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > best) { best = l.intensity; s_Sun = l; }
            return s_Sun;
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Shader.Find(DepthFocusFade.ShaderName);
            if (shader != null && shader.isSupported)             // a shader that failed to compile must not paint the screen
                m_Material = CoreUtils.CreateEngineMaterial(shader);
            else
                Debug.LogError($"[OYM] DepthFocusFade: shader '{DepthFocusFade.ShaderName}' {(shader == null ? "not found (add it to Always Included Shaders for player builds)" : "not supported / failed to compile")}.");

            m_Copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OYM_DepthFocusFadeSource");
            // HDRP's custom pass Gaussian blur only accepts 0.5 / 1.0 scaled scratch targets, so the glow lives at half res
            RTHandle Half(string n) => RTHandles.Alloc(Vector2.one * 0.5f, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: n);
            m_GlowSrc = Half("OYM_DepthGlowSource");
            m_GlowTmp = Half("OYM_DepthGlowBlurTemp");
            m_Glow = Half("OYM_DepthGlow");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (m_Material == null || m_Copy == null) return;
            var type = ctx.hdCamera.camera.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;   // not in reflection probes / previews
            var s = ctx.hdCamera.volumeStack?.GetComponent<DepthFocusFade>();
            if (s == null || !s.IsActive()) return;

            float start = Mathf.Max(0.5f, s.startDistance.value);
            float end = Mathf.Max(start * 1.01f, s.endDistance.value);
            var pb = ctx.propertyBlock;                                // copied at record time: safe per camera
            pb.Clear();
            pb.SetVector(ID_Range, new Vector4(start, 1f / Mathf.Log(end / start), s.skyWeight.value, s.intensity.value));
            pb.SetVector(ID_Look, new Vector4(s.desaturation.value, Mathf.Pow(2f, -s.darkenEV.value),
                Mathf.Max(0.01f, s.highlightLimit.value), s.highlightCompress.value));
            pb.SetVector(ID_Haze, s.hazeColor.value.linear);
            pb.SetVector(ID_Misc, new Vector4(s.haze.value, s.debugView.value ? 1f : 0f, 0f, 0f));
            pb.SetVector(ID_Sky, new Vector4(Mathf.Max(end, s.skyLayerDistance.value), s.skyLayerWeight.value, 0f, 0f));
            pb.SetVector(ID_HazeSky, new Vector4(s.hazeSkyMix.value, s.hazeSkyBlur.value, s.hazeSkyGain.value, 0f));
            pb.SetTexture(ID_MainTex, m_Copy);

            // fog: opacities pre-multiplied by the master amounts, so 0 means off in the shader
            float fogAmt = s.fog.value * s.intensity.value;
            pb.SetVector(ID_FogA, new Vector4(fogAmt * s.fogMaxOpacity.value, s.fogStart.value,
                1f / Mathf.Max(1f, s.fogDistance.value), s.fogSilhouette.value));
            pb.SetVector(ID_FogB, new Vector4(fogAmt * s.fogSkyOpacity.value, fogAmt * s.fogBackdropOpacity.value,
                s.fogNebulaTint.value, s.fogNebulaBlur.value));
            var fc = s.fogColor.value.linear;
            pb.SetVector(ID_FogColor, new Vector4(fc.r, fc.g, fc.b, s.fogNebulaGain.value));
            Vector4 sunRad = Vector4.zero, sunDir = new Vector4(0f, 1f, 0f, 0f);
            Light sun = fogAmt > 0f && s.fogSunScatter.value > 0f ? FindSun() : null;
            if (sun != null)
            {
                // radiance a white Lambert surface would reflect under this sun (lux / pi), times the scatter amount;
                // the shader multiplies by the camera exposure and the phase function
                Color c = sun.color.linear * sun.intensity;
                if (sun.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature);
                c *= s.fogSunScatter.value / Mathf.PI;
                sunRad = new Vector4(c.r, c.g, c.b, s.fogSunAnisotropy.value);
                Vector3 toSun = -sun.transform.forward;
                sunDir = new Vector4(toSun.x, toSun.y, toSun.z, 1f);
            }
            pb.SetVector(ID_FogSun, sunRad);
            pb.SetVector(ID_FogSunDir, sunDir);

            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, m_Copy);

            bool glowOn = s.glow.value > 0f && !s.debugView.value && m_Glow != null;
            pb.SetVector(ID_Glow, new Vector4(s.glow.value, Mathf.Max(0.05f, s.glowSourceLimit.value), glowOn ? 1f : 0f, 0f));
            if (glowOn)
            {
                // weighted far colour at half res -> wide gaussian (radius in half-res pixels, 1-pixel tap spacing)
                CoreUtils.SetRenderTarget(ctx.cmd, m_GlowSrc, ClearFlag.None);
                CoreUtils.DrawFullScreen(ctx.cmd, m_Material, pb, shaderPassId: PassGlowPrep);
                float radius = Mathf.Clamp(s.glowSize.value * 0.5f * ctx.hdCamera.actualHeight, 2f, 64f);
                int samples = Mathf.Clamp(Mathf.CeilToInt(radius) * 2 + 1, 9, 129);
                CustomPassUtils.GaussianBlur(ctx, m_GlowSrc, m_Glow, m_GlowTmp, samples, radius, downSample: false);
                pb.SetTexture(ID_GlowTex, m_Glow);
            }
            else
                pb.SetTexture(ID_GlowTex, m_Copy);                      // bound but unused

            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, m_Material, pb, shaderPassId: PassFade);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
            m_Copy?.Release(); m_Copy = null;
            m_GlowSrc?.Release(); m_GlowSrc = null;
            m_GlowTmp?.Release(); m_GlowTmp = null;
            m_Glow?.Release(); m_Glow = null;
        }
    }
}
