using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Atmospheric-perspective "focus" settings: the further a pixel is from the camera, the more it is squashed,
    /// desaturated, darkened and pulled toward a haze colour, so the busy far debris field (glowing hull cuts at
    /// 0.9-2.6 km, reactor-core glow at ~1 km) recedes and the fight in front reads first.
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

        [Header("Look at full fade")]
        [Tooltip("0 = keep colour, 1 = greyscale.")]
        public ClampedFloatParameter desaturation = new ClampedFloatParameter(0.75f, 0f, 1f);
        [Tooltip("Exposure stops taken away.")]
        public ClampedFloatParameter darkenEV = new ClampedFloatParameter(1.5f, 0f, 8f);
        [Tooltip("Luminance ceiling (pre-exposed, 1 ~ white) that far highlights are softly squashed toward. Kills the glow and bloom of far emissive edges and far light glows.")]
        public MinFloatParameter highlightLimit = new MinFloatParameter(0.5f, 0.01f);
        [Tooltip("How much of the highlight squash is applied.")]
        public ClampedFloatParameter highlightCompress = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Colour that far geometry dissolves into (pre-exposed; the swatch is sRGB like any material colour, 1 ~ white).")]
        public ColorParameter hazeColor = new ColorParameter(new Color(0.19f, 0.22f, 0.26f), true, false, true);
        [Tooltip("How far toward the haze colour far geometry is pulled.")]
        public ClampedFloatParameter haze = new ClampedFloatParameter(0.4f, 0f, 1f);

        [Header("Debug")]
        [Tooltip("Show the fade amount as greyscale (black = untouched, white = full fade).")]
        public BoolParameter debugView = new BoolParameter(false);

        public bool IsActive() => intensity.value > 0f;

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
        RTHandle m_Copy;

        static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int ID_Range = Shader.PropertyToID("_FadeRange");
        static readonly int ID_Look = Shader.PropertyToID("_FadeLook");
        static readonly int ID_Haze = Shader.PropertyToID("_HazeColor");
        static readonly int ID_Misc = Shader.PropertyToID("_FadeMisc");

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

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Shader.Find(DepthFocusFade.ShaderName);
            if (shader != null && shader.isSupported)             // a shader that failed to compile must not paint the screen
                m_Material = CoreUtils.CreateEngineMaterial(shader);
            else
                Debug.LogError($"[OYM] DepthFocusFade: shader '{DepthFocusFade.ShaderName}' {(shader == null ? "not found (add it to Always Included Shaders for player builds)" : "not supported / failed to compile")}.");

            m_Copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OYM_DepthFocusFadeSource");
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
            pb.SetTexture(ID_MainTex, m_Copy);

            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, m_Copy);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, m_Material, pb, shaderPassId: 0);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
            m_Copy?.Release();
            m_Copy = null;
        }
    }
}
