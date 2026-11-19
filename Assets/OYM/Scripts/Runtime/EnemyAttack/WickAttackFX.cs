using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// WICK's attack:
    ///   RUSH      (loop while chasing) closed pod; vector thruster plume + glow behind it, a faint motion streak,
    ///             erratic jinks with side-thruster puffs, the sensor slit lit
    ///   ARM       (Arm(fuse)) retro-burn as it brakes; the shell bursts open under a pop (closed -> open model,
    ///             halves spring apart with the "쪼"); the core blinks faster and faster (chirp) going red -> white,
    ///             its light pulses over everything near; shiver + spin-up + swell, sparks spitting from the seam
    ///   INHALE    the last 0.1 s everything goes dark and tight (the beat)
    ///   DETONATE  white-magenta flash + glint + light on top of the library Explosion (fire, smoke, refraction shock,
    ///             screen white-out / shake / hit-stop when close); the pod is gone
    /// Gameplay: SetRush(true) while the Rusher chases, Arm(fuseTime) on ShowFuseRpc, Detonate() on ShowBlastRpc
    /// (or autoDetonate at the end of the fuse). Only `pivot` moves; the root belongs to the AI.
    public class WickAttackFX : MonoBehaviour
    {
        [Header("Rig")]
        public Transform pivot;
        public GameObject closedModel, openModel;
        public Vector3 sensorLocal = new Vector3(0.008f, -0.037f, 0.44f);
        public Vector3 thrusterLocal = new Vector3(0f, 0f, -0.52f);
        public Vector3 coreLocal = new Vector3(0f, -0.03f, 0.05f);
        public Light coreLight, thrusterLight;
        public HuskFXMaterials materials = new HuskFXMaterials();

        [Header("Timing")]
        public float fuseTime = 1.5f;
        public float blastRadius = 3.5f;
        public bool autoDetonate = true;
        [Tooltip("hide the pod at the blast (the shards / wreck are someone else's)")] public bool hideOnBlast = true;
        [Tooltip("call OYM.EnemyShatter.Detonate(openModel, dir, power, false) at the blast when that type exists")]
        public bool shatterOnBlast = true;

        /// (position, radius) at the blast
        public event System.Action<Vector3, float> Detonated;

        // It goes off latched at the player's chest, ~3 m from the camera: the blast lights (library Explosion +
        // core light below) at 1/100 - about 3M -> 30k lm in the EV 13 maps (11-19 "눈뽕"). The library Explosion light
        // itself went 90000 -> 9000 later on 11-19 ("조명이 너무 쎄서 눈아파"), so 0.1 on it keeps that same 1/100.
        const float BlastLight = 0.1f;
        const float CoreLight = 0.01f;

        // 11-19 "섬광 작게": the blast's own white dot / magenta halo / glint are smaller and dimmer, and never cover
        // more than this share of the screen height (at ~3 m the old 2 / 6 / 9 m sprites flooded the whole view)
        const float FlashDotView = 0.12f, FlashGlowView = 0.35f, FlashGlintView = 0.4f;

        // ---------------------------------------------------------------- state
        bool _rush;
        float _rushK, _rushClock;
        float _alert, _alertT, _pingT = -1f;
        float _a = -1f;            // fuse clock (s since Arm), < 0 not armed
        float _blastAt = -1f;      // fuse clock at the blast
        bool _blasted;
        Vector3 _vel, _lastPos, _blastPos, _blastN;
        bool _havePos;
        int _seed;

        HuskFXCanvas _fx;
        HuskFX.Emission _emOpen, _emClosed;
        readonly List<HitFXInstance> _manual = new List<HitFXInstance>();
        Vector3 _openScale = Vector3.one, _closedScale = Vector3.one, _pivotPos;
        Quaternion _pivotRot = Quaternion.identity;
        bool _inited;

        public bool Armed => _a >= 0f;
        public bool Blasted => _blasted;
        /// still drawing something (fuse, blast, or the thruster winding down)
        public bool Busy => _a >= 0f || _rushK > 0.001f || _manual.Count > 0;

        void Init()
        {
            if (_inited) return;
            _inited = true;
            if (!pivot) pivot = transform;
            if (openModel) _openScale = openModel.transform.localScale;
            if (closedModel) _closedScale = closedModel.transform.localScale;
            _pivotPos = pivot == transform ? Vector3.zero : pivot.localPosition;
            _pivotRot = pivot == transform ? Quaternion.identity : pivot.localRotation;
            _emOpen = new HuskFX.Emission(openModel ? openModel.transform : null);
            _emClosed = new HuskFX.Emission(closedModel ? closedModel.transform : null);
            _seed = Mathf.Abs(GetInstanceID()) % 9973;
        }

        void OnEnable() { Init(); }

        void OnDisable()
        {
            HuskFX.ClearManual(_manual);
            if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }   // destroying children while deactivating is not allowed
            HuskFX.SetLight(coreLight, 0f, HuskFX.LightMagenta);
            HuskFX.SetLight(thrusterLight, 0f, HuskFX.LightMagenta);
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        // ---------------------------------------------------------------- game API
        public void SetRush(bool on) { Init(); _rush = on; }

        /// Asleep -> awake: the sensor slit ignites with a pop and stays lit (0 = dark, 1 = lit) while it is not rushing.
        /// The wake-up beat of a reveal: call Ping() on each pod one after another.
        public void SetAlert(float a) { Init(); _alertT = Mathf.Clamp01(a); }
        public void Ping() { Init(); _pingT = 0f; _alertT = 1f; }

        public void Arm(float fuse)
        {
            Init();
            if (_a >= 0f) return;
            fuseTime = Mathf.Max(0.3f, fuse);
            _a = 0f; _blastAt = -1f; _blasted = false; _rush = false;
        }

        /// Blast now (ShowBlastRpc). `normal`: which way the fireball opens (default: toward the camera).
        public void Detonate(Vector3 normal = default)
        {
            Init();
            if (_blasted) return;
            if (_a < 0f) { _a = fuseTime; }
            _blastAt = _a;
            _blastN = normal;
        }

        /// Back to a fresh pod (pool reuse).
        public void ResetState()
        {
            Init();
            _rush = false; _rushK = 0f; _rushClock = 0f; _a = -1f; _blastAt = -1f; _blasted = false;
            _alert = _alertT = 0f; _pingT = -1f;
            _vel = Vector3.zero; _havePos = false;
            HuskFX.ClearManual(_manual);
            RestoreShattered();
            if (closedModel) { closedModel.SetActive(true); closedModel.transform.localScale = _closedScale; }
            if (openModel) { openModel.SetActive(false); openModel.transform.localScale = _openScale; }
            if (pivot && pivot != transform) pivot.SetLocalPositionAndRotation(_pivotPos, _pivotRot);
            _emOpen?.Set(1f); _emClosed?.Set(1f);
            Evaluate();
        }

        // ---------------------------------------------------------------- clock
        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            Init();
            if (!Application.isPlaying) HuskFX.TickManual(_manual, dt);
            if (dt <= 0f) { Evaluate(); return; }
            Vector3 pos = transform.position;
            if (_havePos)
            {
                Vector3 v = (pos - _lastPos) / dt;
                _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt / 0.08f));
            }
            _lastPos = pos; _havePos = true;
            _rushK = Mathf.MoveTowards(_rushK, _rush && _a < 0f ? 1f : 0f, dt / (_rush ? 0.12f : 0.06f));
            _alert = Mathf.MoveTowards(_alert, _alertT, dt / 0.15f);
            if (_pingT >= 0f) { _pingT += dt; if (_pingT > 0.6f) _pingT = -1f; }
            _rushClock += dt;
            if (_a >= 0f && !_blasted)
            {
                _a += dt;
                if (_blastAt < 0f && autoDetonate && _a >= fuseTime) _blastAt = fuseTime;
                if (_blastAt >= 0f && _a >= _blastAt) Blast();
            }
            else if (_blasted) _a += dt;
            Evaluate();
            if (_blasted && _a - _blastAt > 0.4f && _manual.Count == 0) _a = -1f;   // done; the pool can take it back
        }

        void Blast()
        {
            _blasted = true;
            _blastPos = pivot.TransformPoint(coreLocal);
            var cam = FxBatch.ViewCamera ? FxBatch.ViewCamera : Camera.main;
            Vector3 n = _blastN.sqrMagnitude > 1e-6f ? _blastN : (cam ? (cam.transform.position - _blastPos).normalized : Vector3.up);
            _blastN = n;
            if (shatterOnBlast) TryShatter(n);
            HuskFX.PlayHit(HitKind.Explosion, _blastPos, n, null, Mathf.Max(0.5f, blastRadius / 3.2f), _manual, BlastLight);
            if (hideOnBlast)
            {
                if (closedModel) closedModel.SetActive(false);
                if (openModel) openModel.SetActive(false);
            }
            Detonated?.Invoke(_blastPos, blastRadius);
        }

        /// World height the view camera sees at `p`'s depth (vertical FOV); large when there is no camera.
        static float ViewHeight(Vector3 p)
        {
            var c = HuskFX.ViewCam;
            if (!c) return 1e4f;
            if (c.orthographic) return 2f * c.orthographicSize;
            float d = Vector3.Dot(p - c.transform.position, c.transform.forward);
            return 2f * Mathf.Max(0.05f, d) * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        static System.Reflection.MethodInfo s_shatter;
        static bool s_shatterLooked;

        /// Optional hand-off to the destruction FX (OYM.EnemyShatter, built in another session): flying shards,
        /// glowing cut faces, embers - without its own explosion. Silently skipped when it is not in the project.
        void TryShatter(Vector3 dir)
        {
            if (!s_shatterLooked)
            {
                s_shatterLooked = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("OYM.EnemyShatter");
                    if (type == null) continue;
                    s_shatter = type.GetMethod("Detonate", new[] { typeof(GameObject), typeof(Vector3), typeof(float), typeof(bool) });
                    break;
                }
            }
            if (s_shatter == null) return;
            var target = openModel && openModel.activeInHierarchy ? openModel : (closedModel ? closedModel : gameObject);
            try { s_shatter.Invoke(null, new object[] { target, dir, 1f, false }); }
            catch (System.Exception e) { Debug.LogWarning("[WickAttackFX] EnemyShatter.Detonate failed: " + e.Message); }
        }

        static System.Reflection.MethodInfo s_restore;
        static bool s_restoreLooked;

        /// EnemyShatter hides the source renderers (ShatterHidden) until the object is deactivated; a pod reused in
        /// place (demo loop) has to be shown again explicitly.
        void RestoreShattered()
        {
            if (!s_restoreLooked)
            {
                s_restoreLooked = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("OYM.EnemyShatter");
                    if (type == null) continue;
                    s_restore = type.GetMethod("Restore", new[] { typeof(GameObject) });
                    break;
                }
            }
            if (s_restore == null) return;
            foreach (var go in new[] { closedModel, openModel })
                if (go) { try { s_restore.Invoke(null, new object[] { go }); } catch { } }
        }

        // ---------------------------------------------------------------- evaluation
        /// Fuse phase (cycles) of the accelerating blink: f0 at arm -> f1 at the end of the fuse.
        float Phase(float a)
        {
            const float f0 = 2.2f, f1 = 15f;
            float k = (f1 - f0) / Mathf.Max(0.3f, fuseTime);
            return f0 * a + 0.5f * k * a * a;
        }

        public void Evaluate()
        {
            Init();
            if (_fx == null || !_fx.Valid) _fx = new HuskFXCanvas(transform, materials);
            _fx.Begin();
            float r = _rushClock, k = _rushK;
            int seed = _seed;
            Vector3 off = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            float scale = 1f;
            float coreLumen = 0f, thrLumen = 0f;
            Color coreCol = HuskFX.LightMagenta;
            bool armed = _a >= 0f;
            float a = armed ? _a : -1f;
            float F = fuseTime;

            // ---- models
            const float SwapAt = 0.05f;
            bool open = armed && a >= SwapAt && !(_blasted && hideOnBlast);
            bool closed = !armed || (a < SwapAt && !_blasted);
            if (_blasted && hideOnBlast) { open = false; closed = false; }
            if (closedModel) closedModel.SetActive(closed);
            if (openModel) openModel.SetActive(open);

            // ---- RUSH
            if (k > 0.001f)
            {
                off += new Vector3(HuskFX.N(r * 1.3f, seed + 1) * 0.16f, HuskFX.N(r * 1.7f, seed + 2) * 0.11f, 0f) * k;
                rot = Quaternion.AngleAxis(HuskFX.N(r * 0.8f, seed + 3) * 40f * k, Vector3.forward);
                Vector3 nozzle = pivot.TransformPoint(thrusterLocal);
                Vector3 back = -pivot.forward;
                float flick = 1f + 0.18f * HuskFX.N(r * 30f, seed + 4) + 0.1f * HuskFX.N(r * 71f, seed + 5);
                float len = (0.55f + 0.45f * k) * flick;
                _fx.streak.Streak(nozzle + back * len, nozzle, 0.36f * k, HuskFX.Heat(0.45f) * 0.0f, HuskFX.Heat(0.6f) * (1.0f * k));
                _fx.streak.Streak(nozzle + back * (len * 0.6f), nozzle, 0.13f * k, HuskFX.Heat(0.8f) * 0.0f, HuskFX.Heat(0.85f) * (3f * k * flick));
                _fx.dot.Sprite(nozzle, 0.22f, HuskFX.Heat(0.85f) * (2.5f * k * flick));
                _fx.glow.Sprite(nozzle + back * 0.15f, 1.1f, HuskFX.Heat(0.58f) * (0.35f * k * flick));
                thrLumen = 1200f * k * flick;
                // motion streak behind it
                float sp = _vel.magnitude;
                if (sp > 0.5f)
                {
                    Vector3 vd = _vel / sp;
                    float tl = Mathf.Min(3.5f, sp * 0.16f);
                    _fx.streak.Streak(nozzle - vd * tl, nozzle, 0.22f, Color.clear, HuskFX.Heat(0.55f) * (0.45f * k));
                }
                // side-thruster puffs on the jinks: where the lateral motion pushes hardest
                float dx = HuskFX.N((r + 0.02f) * 1.3f, seed + 1) - HuskFX.N(r * 1.3f, seed + 1);
                float dy = HuskFX.N((r + 0.02f) * 1.7f, seed + 2) - HuskFX.N(r * 1.7f, seed + 2);
                int kick = Mathf.FloorToInt(r / 0.55f);
                float kp = r - kick * 0.55f;
                if (kp < 0.12f && HuskFX.H(kick, seed + 6) > 0.35f)
                {
                    Vector3 side = new Vector3(-Mathf.Sign(dx), -Mathf.Sign(dy) * 0.4f, 0f).normalized;
                    Vector3 p = pivot.TransformPoint(side * 0.5f);
                    Vector3 pd = pivot.TransformDirection(side);
                    float pk = Mathf.Exp(-kp / 0.04f) * k;
                    _fx.streak.Streak(p, p + pd * 0.45f, 0.14f, HuskFX.Heat(0.85f) * (2.5f * pk), HuskFX.Heat(0.6f) * 0f);
                    _fx.glow.Sprite(p, 0.6f, HuskFX.Heat(0.7f) * (0.8f * pk));
                }
                // sensor slit
                if (closed)
                {
                    Vector3 sensor = pivot.TransformPoint(sensorLocal);
                    _fx.dot.Sprite(sensor, 0.1f, HuskFX.Heat(0.8f) * (2.5f * k * flick));
                    _fx.glow.Sprite(sensor, 0.55f, HuskFX.Heat(0.55f) * (0.35f * k));
                }
            }

            // ---- awake but not rushing: the sensor slit stays lit; the ping is the moment it ignites
            if (closed && (_alert > 0.001f || _pingT >= 0f) && k < 0.999f)
            {
                Vector3 sensor = pivot.TransformPoint(sensorLocal);
                float flick = 1f + 0.15f * HuskFX.N(r * 13f, seed + 21);
                float pop = _pingT >= 0f ? Mathf.Exp(-_pingT / 0.07f) : 0f;
                float lit = _alert * (1f - k);
                _fx.dot.Sprite(sensor, 0.1f + 0.25f * pop, HuskFX.Heat(0.78f + 0.1f * pop) * (2f * lit * flick + 6f * pop));
                _fx.glow.Sprite(sensor, 0.5f + 0.9f * pop, HuskFX.Heat(0.58f) * (0.25f * lit + 0.8f * pop));
                if (pop > 0.01f) _fx.glint.Flare(sensor, 2.2f * pop + 0.3f, 0.12f, HuskFX.Heat(0.75f) * (2f * pop));
                coreLumen += 600f * lit + 3500f * pop;
            }

            // ---- ARM / fuse
            if (armed && !_blasted)
            {
                float h = Mathf.Clamp01(a / F);
                Vector3 core = HuskFX.TowardCam(pivot.TransformPoint(coreLocal), 0.26f);
                float inhale = HuskFX.Smooth(F - 0.12f, F - 0.04f, a);            // 0 -> 1 in the last beat
                float live = 1f - 0.88f * inhale;

                // retro-burn as it brakes onto the target
                float rb = a < 0.25f ? Mathf.Exp(-a / 0.07f) : 0f;
                if (rb > 0.01f)
                {
                    Vector3 front = pivot.TransformPoint(new Vector3(0f, 0f, 0.5f));
                    Vector3 fd = pivot.forward;
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Vector3 p = front + pivot.right * (0.22f * i);
                        _fx.streak.Streak(p, p + (fd + pivot.right * (0.35f * i)).normalized * (0.8f * rb + 0.2f), 0.18f, HuskFX.Heat(0.85f) * (3f * rb), Color.clear);
                    }
                    thrLumen += 1500f * rb;
                }

                // the shell bursts open (pop + the snap)
                float pop = a >= SwapAt ? Mathf.Exp(-(a - SwapAt) / 0.05f) : 0f;
                float spring = HuskFX.Snap((a - SwapAt) / 0.14f, 0.16f);
                if (openModel && open)
                {
                    var s = new Vector3(Mathf.Lerp(0.9f, 1f, spring), Mathf.LerpUnclamped(0.55f, 1f, spring), Mathf.Lerp(0.9f, 1f, spring));
                    openModel.transform.localScale = Vector3.Scale(_openScale, s);
                }
                if (pop > 0.005f)
                {
                    _fx.dot.Sprite(core, 1.0f, HuskFX.Heat(0.9f) * (6f * pop));
                    _fx.glow.Sprite(core, 2.0f, HuskFX.Heat(0.62f) * (0.9f * pop));
                    _fx.glint.Flare(core, 2.6f, 0.18f, HuskFX.Heat(0.75f) * (2f * pop));
                    coreLumen += 7000f * pop;
                }

                // the chirp
                float ph = Phase(a);
                float pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ph * Mathf.PI * 2f), 4f);
                float heat = Mathf.Min(0.92f, 0.45f + 0.4f * h + 0.15f * pulse);
                float I = (1f + 4.5f * pulse * (0.35f + 0.65f * h)) * (1f + 0.8f * h) * live;
                if (open)
                {
                    _fx.dot.Sprite(core, (0.3f + 0.22f * pulse) * (1f + 0.3f * h) * (1f - 0.3f * inhale), HuskFX.Heat(heat) * I);
                    _fx.glow.Sprite(core, 0.8f + 1.3f * pulse * h, HuskFX.Heat(0.5f + 0.2f * heat) * ((0.2f + 0.8f * pulse * h) * live));
                }
                coreLumen += (400f + 4000f * pulse * (0.3f + 0.7f * h)) * live;
                coreCol = Color.Lerp(HuskFX.LightMagenta, new Color(1f, 0.82f, 0.9f), h * pulse);
                _emOpen?.Set((1f + 2.5f * pulse * (0.3f + 0.7f * h)) * live);

                // shiver, spin-up, swell; tight and still in the inhale
                int fr = Mathf.FloorToInt(a * 50f);
                off += HuskFX.Dir(fr, seed + 7) * ((0.003f + 0.035f * h * h) * (1f - inhale));
                float spin = 260f * h * h * h;
                rot = Quaternion.AngleAxis(spin, Vector3.forward) * rot;
                scale = (1f + 0.07f * h + 0.04f * pulse * h) * (1f - 0.1f * inhale);

                // sparks spitting from the seam (the halves split along the equator)
                const int Sparks = 18;
                for (int i = 0; i < Sparks; i++)
                {
                    float life = Mathf.Lerp(0.2f, 0.4f, HuskFX.H(i, seed + 8));
                    float cyc = a / life + HuskFX.H(i, seed + 9);
                    int gen = Mathf.FloorToInt(cyc);
                    float u = cyc - gen;
                    if (HuskFX.H(gen, seed + 10 + i) > h * 1.1f || inhale > 0.5f) continue;
                    float ang = HuskFX.H(gen * 31 + i, seed + 11) * Mathf.PI * 2f;
                    Vector3 dl = new Vector3(Mathf.Cos(ang), (HuskFX.H(gen, seed + 12 + i) - 0.5f) * 0.3f, Mathf.Sin(ang)).normalized;
                    Vector3 d = pivot.TransformDirection(dl);
                    Vector3 p0 = core + d * 0.45f;
                    float travel = 4.5f * life * (1f - Mathf.Exp(-3f * u)) / 3f;
                    Vector3 p = p0 + d * travel;
                    Color c = HuskFX.Heat(1f - 0.5f * u) * (3.5f * (1f - u));
                    _fx.streak.Streak(p - d * Mathf.Min(travel, 0.18f), p, 0.018f, c * 0.1f, c);
                }
            }
            else if (!armed || _blasted) _emOpen?.Set(1f);

            // ---- DETONATE (the library Explosion carries fire / smoke / shock; this is the HUSK flash on top)
            if (_blasted)
            {
                float b = _a - _blastAt;
                float s = Mathf.Max(0.5f, blastRadius / 3.2f);
                float fl = Mathf.Exp(-b / 0.035f);
                float view = ViewHeight(_blastPos);
                // 11-19 "완전 흰색 하지 말고 울렁울렁": no white in the blast either - hot pink at most (heat <= 0.72)
                _fx.dot.Sprite(_blastPos, Mathf.Min(0.8f * s * (1f + b * 4f), FlashDotView * view), HuskFX.Heat(0.72f) * (3f * fl));
                _fx.glow.Sprite(_blastPos, Mathf.Min(2.6f * s, FlashGlowView * view), HuskFX.Heat(0.62f) * (1.2f * Mathf.Exp(-b / 0.1f)));
                float gw = Mathf.Min(4f * s, FlashGlintView * view);
                _fx.glint.Flare(_blastPos, gw, gw * 0.045f, HuskFX.Heat(0.7f) * (1.5f * Mathf.Exp(-b / 0.05f)));
                coreLumen = 40000f * CoreLight * Mathf.Exp(-b / 0.05f);
                coreCol = new Color(1f, 0.7f, 0.85f);
            }

            if (pivot != transform)
                pivot.SetLocalPositionAndRotation(_pivotPos + off, _pivotRot * rot);
            if (closedModel) closedModel.transform.localScale = _closedScale * scale;
            if (openModel && !(armed && open)) openModel.transform.localScale = _openScale;
            else if (openModel) openModel.transform.localScale *= scale;

            _fx.End();
            if (coreLight)
            {
                coreLight.transform.position = _blasted ? _blastPos : pivot.TransformPoint(coreLocal + Vector3.forward * 0.35f);
                HuskFX.SetLight(coreLight, coreLumen, coreCol);
            }
            if (thrusterLight)
            {
                thrusterLight.transform.position = pivot.TransformPoint(thrusterLocal + Vector3.back * 0.3f);
                HuskFX.SetLight(thrusterLight, thrLumen, HuskFX.LightMagenta);
            }
        }
    }
}
