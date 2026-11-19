using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Signal
{
    /// Installs the focus highlight in the game scene on sceneLoaded (no scene edits, SignalInstall untouched): same
    /// rules as the signal layer - off in batch mode, with -oymNoFocus and while another session's recorder drives the
    /// scene. -oymFocusDemo runs the FocusDemo smoke test (the signal layer stands down under that flag, so the demo
    /// brings its own HUSK encounter, lock-on and marks).
    public static class FocusInstall
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymSignalDemo" && a != "-oymFocusDemo" && a != "-oymPerfHud") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != ShowcaseScene.Name || SignalInstall.Flag("-oymNoFocus") || OtherAutomation()) return;
            bool demo = SignalInstall.Flag("-oymFocusDemo");
            var go = new GameObject("Focus highlight");
            SceneManager.MoveGameObjectToScene(go, s);
            if (demo)
            {
                SignalDemo.InstallHusk(s);
                go.AddComponent<LockOn>();
                go.AddComponent<MarkOverlay>();
            }
            go.AddComponent<FocusHighlight>();
            if (demo) go.AddComponent<FocusDemo>();
        }
    }

    /// "Focused" highlight: the target your attack is on lights up - a contour hugging its visible silhouette with a
    /// soft halo, a thin rim on the hull and, on weak points, hot spots - so you see what the shot / the cut is going
    /// to land on. Drawn by a global HDRP custom pass (FocusOutlinePass, before post: bloom picks it up) from a mask of
    /// the target's own renderers: the targets' materials are never touched.
    ///   focus   hard lock (T / middle mouse) = always. No lock = while you attack (LMB, RMB aim, a KESTREL cut, the
    ///           RG-77 charging, and attackLinger s after the last one): what is under the crosshair, else the FCS soft
    ///           target (KESTREL: only within swordReach). Thinner and dimmer without a lock.
    ///   look    "쪼" on acquiring: a white-hot flash with the contour snapping in from wide, a dip under the hold
    ///           level, then a hard hold; every hit that lands on it pulses; letting go fades it out in fadeOut s
    ///   HUSK    warm amber; a charging PRISM's open lens (the weak point) burns hot pink
    ///   CITADEL per part: only the focused claw / head / launcher pod lights up, never the 300 m hull; on a claw or the
    ///           head the part's red sensor lights (the weak point) burn hot pink; a pod is amber-orange
    /// Light on the target only: no rings and no new screen markers (MarkOverlay's lock brackets stay as they are).
    [DefaultExecutionOrder(105)]                      // after LockOn (90) and FlightCamera (100): this frame's targets and pose
    public class FocusHighlight : MonoBehaviour
    {
        public static FocusHighlight Current { get; private set; }
        /// (target or null, hard lock) whenever the focus moves
        public static event System.Action<Component, bool> Changed;

        [Header("When")]
        [Tooltip("seconds the focus stays lit after the last attack input (between shots / cuts)")]
        public float attackLinger = 1.6f;
        [Tooltip("KESTREL without a lock: only a target this close (m) counts as the one the cut goes for")]
        public float swordReach = 32f;
        [Tooltip("ORBIT FRAME without a lock: crosshair range when the controller doesn't say")]
        public float railRange = 400f;

        [Header("Look (screen-relative HDR)")]
        public float hardLevel = 1f, attackBoost = 0.15f, softLevel = 0.6f;
        [Tooltip("hull: fresnel rim, its power, faint fill")]
        public float rim = 0.25f, rimPower = 4f, fill = 0.012f;
        [Tooltip("contour (x the colour) and its core width in px at 1080p: hard lock / attacking without a lock")]
        public float contour = 1.1f, contourPx = 2.2f, softContourPx = 1.4f;
        [Tooltip("soft halo outside the contour: sigma (px at 1080p), gain")]
        public float haloPx = 2.5f, haloGain = 0.35f;
        [Tooltip("\"쪼\": peak of the acquire flash (x the hold level)")]
        public float snapPeak = 2.3f;
        [Tooltip("hit landing on the focused target: extra light, decay (s)")]
        public float pulseGain = 1.4f, pulseDecay = 0.13f;
        public float fadeOut = 0.12f;
        public Color huskColor = new Color(1f, 0.62f, 0.26f), weakColor = new Color(1f, 0.22f, 0.36f), podColor = new Color(1f, 0.5f, 0.18f);
        [Tooltip("weak-point hot spots (CITADEL sensor lights, PRISM lens)")]
        public Color spotColor = new Color(1f, 0.32f, 0.46f);
        public float spotGain = 1.2f;

        /// the focused target right now (HuskEnemy / CitadelWeakPoint / HuskBossPart), null = none
        public Component Focus => _focus;
        public bool FocusHard => _focusHard;
        public bool Engaged => Time.unscaledTime < _engagedUntil;
        /// targets lit right now (the focus + any still fading out)
        public int Lit => _slots.Count;

        sealed class Slot
        {
            public Component owner;
            public LockOn.Kind kind;
            public Renderer[] renderers;
            public int[] subMeshes;
            public Material mat;
            public bool hard;
            public float level, goal, age = 99f, from, peak = 1f, pulse, lastHp = -1f;
        }

        readonly Dictionary<Component, Slot> _slots = new Dictionary<Component, Slot>();
        readonly List<Component> _drop = new List<Component>();
        Component _focus;
        bool _focusHard;
        float _engagedUntil = -99f, _width = 1f;
        Color _contourColor;
        Transform _pilot;
        ZeroGFlight _flight;

        static Material s_template;
        static bool s_warned;
        static readonly List<Renderer> s_rs = new List<Renderer>();
        static readonly int ColorId = Shader.PropertyToID("_FocColor"), RimId = Shader.PropertyToID("_FocRim"), RimPowerId = Shader.PropertyToID("_FocRimPower");
        static readonly int FillId = Shader.PropertyToID("_FocFill"), ContourId = Shader.PropertyToID("_FocContour");
        static readonly int SpotColorId = Shader.PropertyToID("_FocSpotColor"), SpotGainId = Shader.PropertyToID("_FocSpotGain");
        static readonly int[] SpotIds = { Shader.PropertyToID("_FocSpot0"), Shader.PropertyToID("_FocSpot1"), Shader.PropertyToID("_FocSpot2"), Shader.PropertyToID("_FocSpot3") };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; Changed = null; s_template = null; s_warned = false; }

        void Awake()
        {
            Current = this;
            Debug.Log($"[OYM-FOCUS] focus highlight installed ({(Template() ? Template().name : "NO MATERIAL")})");
        }

        void OnEnable() { FocusOutlinePass.Register(); }

        void OnDisable()
        {
            FocusOutlinePass.Unregister();
            foreach (var s in _slots.Values) if (s.mat) Destroy(s.mat);
            _slots.Clear();
            _focus = null; _focusHard = false;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        /// automation / demos: count as attacking for `seconds` (the focus lights up without a lock)
        public static void Engage(float seconds)
        {
            if (Current) Current._engagedUntil = Mathf.Max(Current._engagedUntil, Time.unscaledTime + seconds);
        }

        static Material Template()
        {
            if (s_template) return s_template;
            s_template = Resources.Load<Material>("OYM_FocusHighlight");
            if (!s_template)
            {
                var sh = Shader.Find("OYM/FX/FocusHighlight");
                if (sh) s_template = new Material(sh) { name = "OYM_FocusHighlight (runtime)", hideFlags = HideFlags.DontSave };
            }
            if (!s_template && !s_warned) { s_warned = true; Debug.LogWarning("[OYM-FOCUS] no OYM_FocusHighlight material / OYM/FX/FocusHighlight shader: highlight off"); }
            return s_template;
        }

        // ------------------------------------------------------------------ loop
        void LateUpdate()
        {
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);             // real time: a hit-stop doesn't freeze the snap
            var fc = FlightCamera.Main;
            var e = HuskEncounter.Current;
            Component want = null;
            bool hard = false, engaged = false;
            if (fc && fc.Cam && SignalInstall.PlayerView(e))
            {
                engaged = ReadEngage(fc);
                want = Pick(fc, engaged, out hard);
            }
            if (want != _focus || hard != _focusHard)
            {
                if (want)
                {
                    if (!_slots.TryGetValue(want, out var ns)) { ns = NewSlot(want); if (ns != null) _slots[want] = ns; }
                    if (ns != null && (want != _focus || hard))
                    {
                        // from nothing: the full "쪼"; the same target going soft -> hard lock (or one still fading): a smaller one
                        bool fresh = ns.level < 0.05f;
                        ns.age = 0f; ns.from = fresh ? 0f : 1f; ns.peak = fresh ? 1f : 0.5f;
                    }
                }
                bool moved = want != _focus;
                _focus = want; _focusHard = hard && want;
                if (moved) Changed?.Invoke(_focus, _focusHard);
            }

            float strength = _focusHard ? hardLevel + (engaged ? attackBoost : 0f) : softLevel;
            _drop.Clear();
            foreach (var kv in _slots)
            {
                var s = kv.Value;
                if (!Alive(s.owner)) { _drop.Add(kv.Key); continue; }    // killed / broken: its model is gone or torn off
                bool on = s.owner == _focus;
                s.goal = on ? strength : 0f;
                if (on) s.hard = _focusHard;
                Step(s, dt, on);
                if (!on && s.level <= 0f) _drop.Add(kv.Key);
            }
            foreach (var k in _drop) { var s = _slots[k]; if (s.mat) Destroy(s.mat); _slots.Remove(k); }
        }

        /// attack input this frame (mouse with the cursor captured, a KESTREL cut, the RG-77 charging) -> lit for attackLinger
        bool ReadEngage(FlightCamera fc)
        {
            float now = Time.unscaledTime;
            if (fc.target != _pilot) { _pilot = fc.target; _flight = _pilot ? _pilot.GetComponent<ZeroGFlight>() : null; }
            bool attack = false;
            if (Cursor.lockState == CursorLockMode.Locked && !fc.scripted)
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                attack = m != null && (m.leftButton.isPressed || m.rightButton.isPressed);
#else
                attack = Input.GetMouseButton(0) || Input.GetMouseButton(1);
#endif
            }
            if (_flight is KestrelController k && k.Attacking) attack = true;
            else if (_flight is OrbitFrameController o && ((o.railgun && o.railgun.Charging) || (o.fpRailgun && o.fpRailgun.Charging))) attack = true;
            if (attack) _engagedUntil = Mathf.Max(_engagedUntil, now + attackLinger);
            return now < _engagedUntil;
        }

        Component Pick(FlightCamera fc, bool engaged, out bool hard)
        {
            hard = false;
            var lk = LockOn.Current;
            if (lk && lk.Locked && Alive(lk.Hard.owner)) { hard = true; return lk.Hard.owner; }
            if (!engaged) return null;
            float reach = _flight is KestrelController ? swordReach : _flight is OrbitFrameController o ? o.range : railRange;
            var under = UnderCrosshair(fc, reach);
            if (under) return under;
            if (lk && lk.HasSoft && Alive(lk.Soft.owner))
            {
                var t = lk.Soft;
                if (Vector3.Distance(fc.transform.position, t.point) <= reach + t.radius) return t.owner;
            }
            return null;
        }

        /// the HUSK / red light / launcher pod the crosshair ray hits first (what an RG-77 shot would hit)
        static Component UnderCrosshair(FlightCamera fc, float reach)
        {
            fc.AimPoint(reach, fc.target, out var h, out bool did);
            if (!did || !h.collider) return null;
            var col = h.collider;
            var en = col.GetComponentInParent<HuskEnemy>();
            if (en) return en.Alive ? en : null;
            var wp = col.GetComponentInParent<CitadelWeakPoint>();
            if (wp) return !wp.Broken && wp.FacesPoint(fc.transform.position) ? wp : null;   // from behind it is inside the hull
            var pod = col.GetComponentInParent<HuskBossPart>();
            if (pod) return pod.Broken ? null : pod;
            return null;
        }

        // ------------------------------------------------------------------ one lit target
        void Step(Slot s, float dt, bool focus)
        {
            s.level = s.goal >= s.level ? s.goal : Mathf.MoveTowards(s.level, s.goal, dt / Mathf.Max(0.01f, fadeOut));
            if (s.level <= 0f) return;
            s.age += dt;
            float hp = Hp(s.owner);
            if (s.lastHp >= 0f && hp < s.lastHp - 0.01f) s.pulse = 1f;           // a hit landed on it
            s.lastHp = hp;
            s.pulse *= Mathf.Exp(-dt / Mathf.Max(0.01f, pulseDecay));
            float k = s.level * Snap(s.age, s.from, s.peak) + s.pulse * pulseGain * Mathf.Min(1f, s.level * 2f);
            Push(s, k);
            if (focus || _slots.Count == 1)
            {
                _contourColor = ColorOf(s.kind) * contour;
                _width = (s.hard ? contourPx : softContourPx) * SnapWidth(s.age, s.peak);
            }
        }

        Color ColorOf(LockOn.Kind k) => k == LockOn.Kind.WeakPoint ? weakColor : k == LockOn.Kind.Pod ? podColor : huskColor;

        void Push(Slot s, float k)
        {
            var m = s.mat;
            Color c = ColorOf(s.kind);
            m.SetVector(ColorId, new Vector4(c.r * k, c.g * k, c.b * k, 1f));    // vectors: SetColor would gamma-convert
            m.SetFloat(RimId, rim);
            m.SetFloat(RimPowerId, rimPower);
            m.SetFloat(FillId, fill);
            m.SetFloat(ContourId, k);

            // weak points: the part's red sensor lights burn hot (brightest first), a charging PRISM's open lens
            int n = 0;
            if (s.owner is CitadelWeakPoint wp)
            {
                float r = Mathf.Clamp(wp.Radius * 0.55f, 1.5f, 6f);
                for (int i = 0; i < Mathf.Min(4, wp.SpotCount); i++) { var p = wp.SpotWorld(i); m.SetVector(SpotIds[n++], new Vector4(p.x, p.y, p.z, r)); }
            }
            else if (s.owner is HuskEnemy en && en.kind == HuskEnemy.Kind.Prism && en.Charging && en.prism)
            {
                var p = en.prism.LensWorld;
                m.SetVector(SpotIds[n++], new Vector4(p.x, p.y, p.z, 0.9f));
            }
            for (int i = n; i < SpotIds.Length; i++) m.SetVector(SpotIds[i], Vector4.zero);
            float beat = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 2.2f);
            m.SetVector(SpotColorId, new Vector4(spotColor.r, spotColor.g, spotColor.b, 1f));
            m.SetFloat(SpotGainId, n > 0 ? spotGain * k * beat : 0f);
        }

        /// "쪼": in fast to a white-hot top, down past the hold level, then a hard hold
        float Snap(float t, float from, float peak)
        {
            float top = 1f + (snapPeak - 1f) * peak, dip = 1f - 0.18f * peak;
            if (t < 0.05f) return Mathf.Lerp(from, top, t / 0.05f);
            if (t < 0.14f) return Mathf.Lerp(top, dip, Ease((t - 0.05f) / 0.09f));
            if (t < 0.26f) return Mathf.Lerp(dip, 1f, Ease((t - 0.14f) / 0.12f));
            return 1f;
        }

        /// the contour comes in from wide (like the lock brackets), a hair under, then holds
        static float SnapWidth(float t, float peak)
        {
            float wide = 1f + 1.6f * peak;
            if (t < 0.12f) return Mathf.Lerp(wide, 0.85f, 1f - Mathf.Pow(1f - t / 0.12f, 3f));
            if (t < 0.24f) return Mathf.Lerp(0.85f, 1f, Ease((t - 0.12f) / 0.12f));
            return 1f;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

        // ------------------------------------------------------------------ for the custom pass
        /// the gameplay camera with something lit
        internal bool WantsCamera(Camera cam) =>
            _slots.Count > 0 && cam && cam.cameraType == CameraType.Game && FlightCamera.Main && cam == FlightCamera.Main.Cam;

        internal Vector4 ContourColor => new Vector4(_contourColor.r, _contourColor.g, _contourColor.b, 1f);

        /// x core width (px at 1080p), y halo sigma, z halo gain, w px scale of this camera
        internal Vector4 ContourShape(float height) => new Vector4(_width, haloPx, haloGain, Mathf.Max(0.25f, height / 1080f));

        /// draws the lit targets' renderers (every LOD: their union) into the bound mask; returns the draw count
        internal int DrawMasks(CommandBuffer cmd)
        {
            int n = 0;
            foreach (var s in _slots.Values)
            {
                if (s.level <= 0f || !s.mat) continue;
                for (int i = 0; i < s.renderers.Length; i++)
                {
                    var r = s.renderers[i];
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    for (int sm = 0; sm < s.subMeshes[i]; sm++) { cmd.DrawRenderer(r, s.mat, sm, 0); n++; }
                }
            }
            return n;
        }

        // ------------------------------------------------------------------ targets
        static bool Alive(Component c)
        {
            if (!c) return false;
            if (c is HuskEnemy en) return en.Alive;
            if (c is CitadelWeakPoint wp) return !wp.Broken && wp.isActiveAndEnabled;
            if (c is HuskBossPart pod) return !pod.Broken;
            return false;
        }

        static float Hp(Component c) => c is HuskEnemy en ? en.HP : c is CitadelWeakPoint wp ? wp.Hp : c is HuskBossPart pod ? pod.Hp : 0f;

        static LockOn.Kind KindOf(Component c) =>
            c is HuskEnemy en ? (en.kind == HuskEnemy.Kind.Prism ? LockOn.Kind.Prism : LockOn.Kind.Wick)
            : c is CitadelWeakPoint ? LockOn.Kind.WeakPoint : LockOn.Kind.Pod;

        Slot NewSlot(Component c)
        {
            var tpl = Template();
            if (!tpl) return null;
            var rs = RenderersOf(c);
            if (rs.Length == 0) { Debug.LogWarning($"[OYM-FOCUS] {c.name}: no mesh renderers to light"); return null; }
            var subs = new int[rs.Length];
            for (int i = 0; i < rs.Length; i++) { var mf = rs[i].GetComponent<MeshFilter>(); subs[i] = mf && mf.sharedMesh ? Mathf.Max(1, mf.sharedMesh.subMeshCount) : 1; }
            var mat = new Material(tpl) { name = "OYM_FocusHighlight (slot)", hideFlags = HideFlags.DontSave };
            return new Slot { owner = c, kind = KindOf(c), renderers = rs, subMeshes = subs, mat = mat };
        }

        /// the hull meshes of a HUSK (closed + open model, every LOD), of one CITADEL part, of a launcher pod
        static Renderer[] RenderersOf(Component c)
        {
            s_rs.Clear();
            if (c is HuskEnemy en)
            {
                GameObject a = en.wick ? en.wick.closedModel : en.prism ? en.prism.closedModel : null;
                GameObject b = en.wick ? en.wick.openModel : en.prism ? en.prism.openModel : null;
                if (a) Collect(a.transform, null);
                if (b && b != a) Collect(b.transform, null);
                if (s_rs.Count == 0) Collect(en.transform, null);
            }
            else if (c is CitadelWeakPoint wp) { if (wp.Part) Collect(wp.Part, wp.Part); }
            else if (c is HuskBossPart pod) Collect(pod.transform, null);
            return s_rs.ToArray();
        }

        /// mesh renderers under `root` (no FX batches / hit dummies); with `part`: only the ones of that boss part, not
        /// of another weak point's part or a launcher pod nested under it
        static void Collect(Transform root, Transform part)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r || r.name.StartsWith("[") || (r.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                if (part && !OwnedBy(r.transform, part)) continue;
                if (!s_rs.Contains(r)) s_rs.Add(r);
            }
        }

        static bool OwnedBy(Transform t, Transform part)
        {
            var w = CitadelWeakPoints.Current;
            for (var p = t; p && p != part; p = p.parent)
            {
                if (p.GetComponent<HuskBossPart>()) return false;
                if (w) foreach (var o in w.Points) if (o && o.Part == p) return false;
            }
            return true;
        }
    }

    /// Global HDRP custom pass (before post-processing) that draws FocusHighlight: the lit targets' renderers into a
    /// half-float mask (OYM/FX/FocusHighlight), then Hidden/OYM/FocusOutline adds the hull light + the contour/halo to
    /// the camera colour. Registered only while FocusHighlight lives (the game scene); costs nothing with nothing lit.
    [System.Serializable]
    public sealed class FocusOutlinePass : CustomPass
    {
        static FocusOutlinePass s_instance;
        Material _outline;
        RTHandle _mask;
        static readonly int MaskId = Shader.PropertyToID("_FocusMask"), ContourColorId = Shader.PropertyToID("_FocusContourColor"), ShapeId = Shader.PropertyToID("_FocusShape");

        public static void Register()
        {
            if (s_instance != null) return;
            s_instance = new FocusOutlinePass { name = "OYM Focus Outline" };
            CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforePostProcess, s_instance);
        }

        public static void Unregister()
        {
            if (s_instance == null) return;
            CustomPassVolume.UnregisterGlobalCustomPass(s_instance);
            s_instance.Cleanup();
            s_instance = null;
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var tpl = Resources.Load<Material>("OYM_FocusOutline");                // keeps the shader in player builds
            var sh = tpl ? tpl.shader : Shader.Find("Hidden/OYM/FocusOutline");
            if (sh && sh.isSupported) _outline = CoreUtils.CreateEngineMaterial(sh);   // a shader that failed to compile must not paint the screen
            else Debug.LogError("[OYM-FOCUS] Hidden/OYM/FocusOutline missing or not supported: no focus highlight");
            _mask = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "OYM_FocusMask");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            var h = FocusHighlight.Current;
            if (_outline == null || _mask == null || h == null || !h.WantsCamera(ctx.hdCamera.camera)) return;
            CoreUtils.SetRenderTarget(ctx.cmd, _mask, ClearFlag.Color, Color.clear);
            if (h.DrawMasks(ctx.cmd) == 0) return;
            var pb = ctx.propertyBlock;
            pb.Clear();
            pb.SetTexture(MaskId, _mask);
            pb.SetVector(ContourColorId, h.ContourColor);
            pb.SetVector(ShapeId, h.ContourShape(ctx.hdCamera.actualHeight));
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, _outline, pb, shaderPassId: 0);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_outline);
            _outline = null;
            _mask?.Release();
            _mask = null;
        }
    }
}
