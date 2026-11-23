using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OYM.Web
{
    /// Web build (URP) only: the game was authored for HDRP with physical light units (lumen / lux) and a fixed camera
    /// exposure (EV). URP lights have no units, so every light keeps its HDRP value as written by the scene or by code,
    /// and this adapter turns it into the URP intensity that gives the same picture: candela (or lux) / pi * exposure,
    /// exposure = 1 / (1.2 * 2^EV) with the EV of the scene's strongest global HDRP Exposure override. Code that writes
    /// light.intensity at runtime keeps working: a value that differs from the last one written here is taken as a new
    /// HDRP value. Under HDRP this does nothing.
    public sealed class WebLightAdapter : MonoBehaviour
    {
        /// exposure multiplier of the current scene (1 / (1.2 * 2^EV)); 1 under HDRP
        public static float Exposure { get; private set; } = 1f;
        /// EV used for materials / emission at conversion time (see WebPort); runtime emission writes use this
        public static float MaterialEV = 10f;
        public static float EmissionScale => 1f / (1.2f * Mathf.Pow(2f, MaterialEV));

        static WebLightAdapter _it;
        readonly Dictionary<int, float> _written = new Dictionary<int, float>();
        Light[] _lights = new Light[0];
        float _nextScan, _nextEv;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!IsUrp) return;
            var evText = Resources.Load<TextAsset>("oym_web_ev");
            if (evText && float.TryParse(evText.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ev)) MaterialEV = ev;
            var go = new GameObject("~WebLightAdapter") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _it = go.AddComponent<WebLightAdapter>();
        }

        public static bool IsUrp
        {
            get
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                return rp != null && rp.GetType().Name.Contains("Universal");
            }
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnScene;
            RenderPipelineManager.beginContextRendering += OnBegin;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnScene;
            RenderPipelineManager.beginContextRendering -= OnBegin;
        }

        void OnScene(Scene s, LoadSceneMode m)
        {
            _nextScan = 0f; _nextEv = 0f;
            DynamicGI.UpdateEnvironment();          // ambient from the scene's skybox (set by WebPort)
        }

        void OnBegin(ScriptableRenderContext ctx, List<Camera> cams)
        {
            Apply();
            for (int i = 0; i < cams.Count; i++)
            {
                var c = cams[i];
                if (!c || c.cameraType != CameraType.Game) continue;
                int id = c.GetInstanceID();
                if (_cams.Contains(id)) continue;
                _cams.Add(id);
                var d = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(c);
                d.renderPostProcessing = true;
                if (d.antialiasing == UnityEngine.Rendering.Universal.AntialiasingMode.None) d.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
            }
        }
        readonly HashSet<int> _cams = new HashSet<int>();

        void Apply()
        {
            float now = Time.unscaledTime;
            if (now >= _nextEv) { _nextEv = now + 0.5f; Exposure = 1f / (1.2f * Mathf.Pow(2f, SceneEV())); }
            if (now >= _nextScan) { _nextScan = now + 0.25f; _lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); }
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (!l) continue;
                int id = l.GetInstanceID();
                float cur = l.intensity;
                if (_written.TryGetValue(id, out float w) && Mathf.Approximately(w, cur)) continue;
                float urp = Convert(cur, l) * Exposure;
                l.intensity = urp;
                _written[id] = l.intensity;
            }
        }

        /// HDRP light value -> URP intensity before exposure (candela or lux, over pi)
        static float Convert(float v, Light l)
        {
            float cd;
            switch (l.lightUnit)
            {
                case LightUnit.Lux:
                    cd = l.type == LightType.Directional ? v : v * l.luxAtDistance * l.luxAtDistance; break;
                case LightUnit.Candela: cd = v; break;
                case LightUnit.Ev100: cd = Mathf.Pow(2f, v - 3f); break;
                case LightUnit.Lumen:
                    if (l.type == LightType.Spot)
                    {
                        float half = Mathf.Deg2Rad * l.spotAngle * 0.5f;
                        cd = v / (2f * Mathf.PI * (1f - Mathf.Cos(half)));
                    }
                    else cd = v / (4f * Mathf.PI);
                    break;
                default: cd = v; break;
            }
            return cd / Mathf.PI;
        }

        static readonly List<Volume> s_vols = new List<Volume>();

        /// the EV of the highest-priority enabled global volume that has an HDRP Exposure override (fixed mode, or the
        /// middle of the limits for automatic), else MaterialEV
        static float SceneEV()
        {
            float best = MaterialEV; float bestPrio = float.NegativeInfinity;
            var vols = FindObjectsByType<Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var v in vols)
            {
                if (!v.isGlobal || !v.enabled || v.weight <= 0f) continue;
                var p = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (!p) continue;
                if (!TryEV(p, out float ev)) continue;
                if (v.priority >= bestPrio) { bestPrio = v.priority; best = ev; }
            }
            return best;
        }

        public static bool TryEV(VolumeProfile p, out float ev)
        {
            ev = 0f;
            foreach (var c in p.components)
            {
                if (c is UnityEngine.Rendering.HighDefinition.Exposure e && c.active)
                {
                    float comp = e.compensation.overrideState ? e.compensation.value : 0f;
                    switch (e.mode.value)
                    {
                        case UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed:
                        case UnityEngine.Rendering.HighDefinition.ExposureMode.UsePhysicalCamera:
                            ev = e.fixedExposure.value - comp; return true;
                        default:
                            ev = 0.5f * (e.limitMin.value + e.limitMax.value) - comp; return true;
                    }
                }
            }
            return false;
        }
    }
}
