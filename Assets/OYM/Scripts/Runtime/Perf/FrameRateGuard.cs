using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Keeps the game at or above 45 fps (title and gameplay) without touching any scene.
    ///
    /// 1. Dynamic resolution: every game camera gets HDRP dynamic resolution, upscaled by STP, and this guard feeds
    ///    the render scale from the measured GPU time. DLSS stays off (11-19: with it on, an RTX 4060 editor play
    ///    session went to a black game view about 4 s in, at full scale; STP has no such problem). -oymDlss turns
    ///    it back on for a test. Scale drops quickly when the
    ///    GPU frame goes over budget and climbs back slowly when there is headroom, so a still title renders at
    ///    full resolution and only heavy fights pay with a softer image.
    /// 2. Quality tiers: if the render scale is already at its floor and frames are still too slow, a top-priority
    ///    volume steps down the costliest HDRP effects (volumetric fog quality, contact shadows, SSAO), and steps
    ///    them back up once there is room again.
    ///
    /// Budget: 45 fps with headroom (18.9 ms); on a <=75 Hz vsynced display the budget is the refresh interval so
    /// vsync does not halve the rate. Command line: -oymNoGuard (off), -oymPerfHud (overlay, F8 toggles),
    /// -oymPerfLog <file> (one CSV line per second).
    [DefaultExecutionOrder(-1000)]
    public sealed class FrameRateGuard : MonoBehaviour
    {
        public const float TargetFps = 45f;
        public const float MinScale = 0.5f, MaxScale = 1f;

        public static FrameRateGuard Instance { get; private set; }

        /// current render scale (0.5..1) and quality tier (0 = full quality)
        public float Scale => _scale;
        public int Tier => _tier;
        public float GpuMs => _gpuEma;
        public float FrameMs => _frameEma;

        float _scale = MaxScale;
        int _tier;
        float _gpuEma, _frameEma, _cpuEma;
        float _overTime, _underTime, _nextAdjust;
        bool _hud, _gpuTimingOk;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        readonly HashSet<int> _setupCams = new();

        Volume _tierVolume;
        Fog _fog;
        ContactShadows _contact;
        ScreenSpaceAmbientOcclusion _ssao;

        StreamWriter _log;
        float _logT, _logFrames, _logFrameSum, _logGpuSum;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || Instance != null) return;
            if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset)) return;   // web build (URP): HDRP-only tuning
            if (HasArg("-oymNoGuard")) return;
            var go = new GameObject("~FrameRateGuard") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<FrameRateGuard>();
        }

        static bool HasArg(string a) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;
        static readonly bool s_dlss = HasArg("-oymDlss");

        static string ArgValue(string a)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, a);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Awake()
        {
            Instance = this;
            _hud = HasArg("-oymPerfHud");
            string logPath = ArgValue("-oymPerfLog");
            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    _log = new StreamWriter(logPath, false) { AutoFlush = true };
                    _log.WriteLine("t,scene,fps,frame_ms,gpu_ms,scale,tier");
                }
                catch (IOException e) { Debug.LogWarning("[FrameRateGuard] log: " + e.Message); }
            }
            BuildTierVolume();
        }

        void OnEnable()
        {
            DynamicResolutionHandler.SetDynamicResScaler(() => _scale * 100f, DynamicResScalePolicyType.ReturnsPercentage);
            RenderPipelineManager.beginContextRendering += OnBeginContext;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            DynamicResolutionHandler.SetDynamicResScaler(() => 100f, DynamicResScalePolicyType.ReturnsPercentage);
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _log?.Dispose();
            if (_tierVolume != null && _tierVolume.sharedProfile != null) Destroy(_tierVolume.sharedProfile);
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            _setupCams.Clear();
            // a new scene starts sharp; the controller lowers the scale again within a few frames if needed
            _overTime = _underTime = 0f;
        }

        // opt every on-screen game camera into dynamic resolution (scenes and prefabs stay untouched)
        void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cams)
        {
            for (int i = 0; i < cams.Count; i++)
            {
                var c = cams[i];
                if (c == null || c.cameraType != CameraType.Game || c.targetTexture != null) continue;
                if (_setupCams.Contains(c.GetInstanceID())) continue;
                // HDRP adds the additional data on a camera's first render; pick it up on the next frame
                if (!c.TryGetComponent(out HDAdditionalCameraData hd)) continue;
                _setupCams.Add(c.GetInstanceID());
                hd.allowDynamicResolution = true;
                // DLSS first in the HDRP assets' upscaler list turned the game view black on RTX (11-19): STP upscales
                hd.allowDeepLearningSuperSampling = s_dlss;
            }
        }

        float BudgetMs()
        {
            float budget = 1000f / TargetFps * 0.85f; // 18.9 ms, leaves room for spikes
            if (QualitySettings.vSyncCount > 0)
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                if (hz > 30 && hz <= 75) budget = Mathf.Min(budget, (float)(1000.0 / hz) * 0.94f);
            }
            return budget;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f) return; // loading hitch, not a steady-state frame

            FrameTimingManager.CaptureFrameTimings();
            float gpu = 0f, cpu = 0f;
            if (FrameTimingManager.GetLatestTimings(1, _ft) > 0)
            {
                gpu = (float)_ft[0].gpuFrameTime;
                cpu = (float)_ft[0].cpuMainThreadFrameTime;
            }
            float now = Time.unscaledTime;
            float frameMs = dt * 1000f;
            const float k = 0.1f;
            _frameEma = _frameEma <= 0 ? frameMs : Mathf.Lerp(_frameEma, frameMs, k);
            // drivers sometimes report empty GPU timings (~0 ms); an HDRP frame never costs that little, skip them
            if (gpu > 0.3f) { _gpuEma = _gpuEma <= 0 ? gpu : Mathf.Lerp(_gpuEma, gpu, k); _lastGpuValid = now; }
            if (cpu > 0.3f) { _cpuEma = _cpuEma <= 0 ? cpu : Mathf.Lerp(_cpuEma, cpu, k); _lastCpuValid = now; }
            _gpuTimingOk = now - _lastGpuValid < 1f;

            // GPU time drives the render scale. Without GPU timings, use the frame time only when the main thread is
            // clearly not the bottleneck; a CPU-bound frame keeps its resolution (lowering it would only blur).
            float load = -1f;
            if (_gpuTimingOk) load = _gpuEma;
            else if (now - _lastCpuValid < 1f && _cpuEma < _frameEma * 0.8f) load = _frameEma;
            float budget = BudgetMs();

            if (load > 0f && now >= _nextAdjust)
            {
                _nextAdjust = now + 0.25f;
                if (load > budget)
                {
                    // GPU cost ~ pixel count ~ scale^2: jump most of the way to the budget at once
                    float target = _scale * Mathf.Sqrt(budget * 0.95f / load);
                    _scale = Mathf.Clamp(Mathf.Min(target, _scale - 0.02f), MinScale, MaxScale);
                    _underTime = 0f;
                }
                else if (load < budget * 0.75f)
                {
                    _underTime += 0.25f;
                    if (_underTime >= 1f) _scale = Mathf.Min(MaxScale, _scale + 0.04f);
                }
                else _underTime = 0f;
            }

            // quality tiers once resolution alone is not enough (frame over the 45 fps line at the floor scale)
            bool slow = _frameEma > 1000f / TargetFps;
            if (slow && _scale <= MinScale + 0.001f) { _overTime += dt; } else _overTime = 0f;
            if (_overTime > 2f && _tier < 2) { SetTier(_tier + 1); _overTime = 0f; }
            if (_tier > 0 && _scale >= 0.85f && load > 0f && load < budget * 0.6f)
            {
                _underTimeTier += dt;
                if (_underTimeTier > 6f) { SetTier(_tier - 1); _underTimeTier = 0f; }
            }
            else _underTimeTier = 0f;

            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) _hud = !_hud;

            if (_log != null) LogTick(frameMs, gpu);
        }

        float _underTimeTier, _lastGpuValid = -10f, _lastCpuValid = -10f;

        void BuildTierVolume()
        {
            var go = new GameObject("~FrameRateGuardVolume") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _tierVolume = go.AddComponent<Volume>();
            _tierVolume.isGlobal = true;
            _tierVolume.priority = 10000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            _fog = profile.Add<Fog>();
            _contact = profile.Add<ContactShadows>();
            _ssao = profile.Add<ScreenSpaceAmbientOcclusion>();
            _tierVolume.sharedProfile = profile;
            SetTier(0);
        }

        void SetTier(int tier)
        {
            _tier = tier;
            // tier 1: low volumetric fog quality + no contact shadows; tier 2: also no SSAO
            _fog.quality.overrideState = tier >= 1;
            _fog.quality.value = (int)ScalableSettingLevelParameter.Level.Low;
            _contact.enable.overrideState = tier >= 1;
            _contact.enable.value = false;
            _ssao.intensity.overrideState = tier >= 2;
            _ssao.intensity.value = 0f;
            if (tier > 0) Debug.Log($"[FrameRateGuard] quality tier {tier} (scale {_scale:F2}, frame {_frameEma:F1} ms)");
        }

        void LogTick(float frameMs, float gpu)
        {
            _logFrames++; _logFrameSum += frameMs; _logGpuSum += gpu;
            if (Time.unscaledTime - _logT < 1f) return;
            float fps = _logFrames / Mathf.Max(0.001f, Time.unscaledTime - _logT);
            _log.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1},{1},{2:F1},{3:F2},{4:F2},{5:F2},{6}",
                Time.unscaledTime, SceneManager.GetActiveScene().name, fps, _logFrameSum / _logFrames, _logGpuSum / _logFrames, _scale, _tier));
            _logT = Time.unscaledTime; _logFrames = 0; _logFrameSum = 0; _logGpuSum = 0;
        }

        void OnGUI()
        {
            if (!_hud) return;
            float fps = _frameEma > 0 ? 1000f / _frameEma : 0;
            GUI.color = fps >= TargetFps ? Color.green : Color.red;
            GUI.Label(new Rect(12, 12, 420, 22),
                $"{fps:F0} fps  frame {_frameEma:F1}  gpu {(_gpuTimingOk ? _gpuEma.ToString("F1") : "-")}  cpu {_cpuEma:F1}  scale {_scale * 100f:F0}%  tier {_tier}");
        }
    }
}
