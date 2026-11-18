// OnYourMark - event zone prototype "MASS CONVERGENCE" (user idea 11-18): two big asteroids slam together in front of
// the player, HUSK pour out from behind the debris cloud, wreck fragments lock into a path, and a dash lane throws
// debris at the player. Design: design/EVENT_ASTEROID_COLLISION.md. Built by OYM.EditorTools.OYMCollisionEvent.
//
// Every visual is a pure function of the event clock: Evaluate(t, lane, cam) poses all pieces and rebuilds the FX
// billboards, so batch-mode stills show exactly what play mode shows. Play mode adds the player-facing parts on top:
// trigger zone, camera rumble / shake / hit-stop, the dash lane clock, debris hits, fail and clear.
// All positions are in this transform's local space (+Z = event forward). Motion ends use Snap(): fast arrival,
// small overshoot, hard hold (the user's "쪼").
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    [DefaultExecutionOrder(500)]
    public class CollisionEvent : MonoBehaviour
    {
        public enum Kind { Asteroid, Fragment, Enemy, PathPiece, LaneDebris }

        [System.Serializable]
        public class Piece
        {
            public Transform t;
            public Kind kind;
            [Tooltip("launch time (s, event clock); lane debris: seconds after the first wave")] public float t0;
            public float dur = 1f;
            [Tooltip("Snap overshoot as a fraction of the travel (0.05 - 0.15)")] public float overshoot = 0.08f;
            public Vector3 p0, p1, ctrl;
            public Quaternion r0 = Quaternion.identity, r1 = Quaternion.identity;
            public Vector3 spinAxis = Vector3.up;
            [Tooltip("degrees turned during the burst / degrees per second afterwards")] public float spinBurst, spin;
            [Tooltip("m/s after the burst (fragments)")] public Vector3 drift;
            public float bob, phase;
            [Tooltip("hit radius (lane debris) / size (enemy, fragment) / guide light height (path) in m")] public float radius = 1f;
            [Tooltip("ember glow strength (fragments)")] public float glow;
            [Tooltip("lane debris: offset across the lane (U, V) in m")] public Vector2 slot;
            [System.NonSerialized] public int hitCycle = -1;
            [System.NonSerialized] public bool shown = true, colOn = true;
            [System.NonSerialized] public Collider[] cols;
        }

        [Header("Timeline (s, event clock)")]
        public float approachFrom = 0.3f;
        public float impactTime = 3.8f;
        public float enemiesFrom = 4.7f, pathFrom = 10.2f;
        [Tooltip("last path piece locked (set by the builder)")] public float pathReadyTime = 13.5f;
        [Tooltip("no player (free camera) / captures: the lane runs from here")] public float laneAutoTime = 15f;
        [Tooltip("> 0: start by itself after this many seconds")] public float autoStartDelay;

        [Header("Places (local space, +Z = event forward)")]
        public Vector3 triggerCenter = new Vector3(0f, 1.5f, 16f);
        public float triggerRadius = 8f;
        public Vector3 viewPoint = new Vector3(0f, 1.5f, 12f);
        public Vector3 collisionPoint = new Vector3(0f, 22f, 175f);
        [Tooltip("player-side end / cloud-side end of the dash lane")] public Vector3 laneStart = new Vector3(34f, 8f, 84f), laneEnd = new Vector3(66f, 20f, 180f);
        public float laneRadius = 6.5f;

        [Header("Dash lane")]
        public float laneSpeed = 30f;
        public float wavePeriod = 1.7f, firstWave = 1.2f, preroll = 20f, postroll = 25f;
        public int waveCount = 10, maxHits = 3;
        public float playerRadius = 0.5f, bodyCenter = 1.0f;

        [Header("Flash (AC6-style)")]
        public Light flashLight;
        public float flashLumen = 7e9f, emberLumen = 3e8f;
        public float flashExposure = 1.1f, flashBloom = 1.0f;
        public int sparkCount = 220;
        public Material glowMat, beamMat;

        public List<Piece> pieces = new List<Piece>();

        static readonly Color Amber = new Color(1f, 0.62f, 0.22f), Pink = new Color(1f, 0.12f, 0.42f), Hot = new Color(1f, 0.42f, 0.12f),
                              Blue = new Color(0.72f, 0.86f, 1f), White = new Color(1f, 0.96f, 0.9f);

        // ---------------------------------------------------------------- state
        float _t = -1f, _lane = -1f, _idle, _invuln, _damage, _failTimer, _clearAt = -1f, _laneStopAt = -1f;
        bool _started, _laneOn, _laneLive, _cleared;
        int _hits;
        Vector3 _push;                                  // world m/s, decays (hit knock-back)
        Vector3 _cp, _cr, _cu, _lookTarget;            // camera position / right / up, enemy look target (local)
        Vector3 _laneDir = Vector3.forward, _laneU = Vector3.right, _laneV = Vector3.up;
        float _laneLen = 100f;
        Batch _glow, _beam;
        Volume _flashVol, _dmgVol;
        readonly List<(Vector3 p, float at)> _pops = new List<(Vector3, float)>();
        GUIStyle _big, _small;

        public float Clock => _t;
        public bool Started => _started;
        public bool LaneCleared => _cleared;
        public bool LaneFailing => _failTimer > 0f;
        public int LaneHitCount => _hits;
        public int TotalHits { get; private set; }
        public string LaneStatus => Phase();

        /// For autopilots / AI: the next lane wave ahead of `along` (m from the gate along the lane) and the open spot in it
        /// (lane cross-section U/V, m) that keeps the most clearance, preferring spots near `lat`.
        public bool NextWave(float along, Vector2 lat, out float dz, out Vector2 gap)
        {
            dz = 0f; gap = lat;
            if (!_laneOn) return false;
            Piece lead = null; int leadCycle = 0; float best = float.MaxValue;
            foreach (var p in pieces)
            {
                if (p.kind != Kind.LaneDebris || !LanePose(p, _lane, out var pos, out _, out int cyc)) continue;
                float z = Vector3.Dot(pos - laneStart, _laneDir) - along;
                if (z < -0.5f - p.radius || z >= best) continue;
                best = z; lead = p; leadCycle = cyc;
            }
            if (lead == null) return false;
            dz = best;
            float bestScore = float.MinValue;
            for (int c = 0; c < 13; c++)
            {
                Vector2 cand = c == 0 ? Vector2.zero : c <= 6
                    ? new Vector2(Mathf.Cos((90f - 60f * (c - 1)) * Mathf.Deg2Rad), Mathf.Sin((90f - 60f * (c - 1)) * Mathf.Deg2Rad)) * 3.4f
                    : new Vector2(Mathf.Cos((60f - 60f * (c - 7)) * Mathf.Deg2Rad), Mathf.Sin((60f - 60f * (c - 7)) * Mathf.Deg2Rad)) * 4.8f;
                float clear = float.MaxValue;
                foreach (var p in pieces)
                {
                    if (p.kind != Kind.LaneDebris || p.t0 != lead.t0 || !LanePose(p, _lane, out _, out _, out int cyc) || cyc != leadCycle) continue;
                    clear = Mathf.Min(clear, (cand - p.slot).magnitude - p.radius);
                }
                float score = Mathf.Min(clear, 3f) - 0.3f * (cand - lat).magnitude;
                if (score > bestScore) { bestScore = score; gap = cand; }
            }
            return true;
        }

        // ---------------------------------------------------------------- setup
        /// Creates the FX meshes and override volumes (play mode does it on enable; edit-mode captures call it by hand).
        public void Prepare()
        {
            if (_glow == null) _glow = new Batch("FX glow (runtime)", transform, glowMat);
            if (_beam == null) _beam = new Batch("FX beam (runtime)", transform, beamMat);
            if (!_flashVol) _flashVol = MakeVolume("FX flash volume (runtime)", p =>
            {
                var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(flashExposure);
                var b = p.Add<Bloom>(true); b.intensity.Override(flashBloom); b.scatter.Override(0.85f);
            });
            if (!_dmgVol) _dmgVol = MakeVolume("FX damage volume (runtime)", p =>
            {
                var v = p.Add<Vignette>(true); v.color.Override(new Color(1f, 0.1f, 0.14f)); v.intensity.Override(0.55f); v.smoothness.Override(0.6f);
                var c = p.Add<ChromaticAberration>(true); c.intensity.Override(1f);
            });
            foreach (var p in pieces)
            {
                if (!p.t) continue;
                p.shown = p.t.gameObject.activeSelf;
                p.cols = p.t.GetComponentsInChildren<Collider>(true);
                p.colOn = true;
            }
            LaneAxes();
        }

        /// removes the runtime-only FX objects again (edit-mode captures)
        public void Teardown()
        {
            if (_glow != null) { _glow.Destroy(); _glow = null; }
            if (_beam != null) { _beam.Destroy(); _beam = null; }
            if (_flashVol) { DestroyImmediate(_flashVol.sharedProfile); DestroyImmediate(_flashVol.gameObject); }
            if (_dmgVol) { DestroyImmediate(_dmgVol.sharedProfile); DestroyImmediate(_dmgVol.gameObject); }
            _flashVol = _dmgVol = null;
        }

        Volume MakeVolume(string n, System.Action<VolumeProfile> fill)
        {
            var go = new GameObject(n) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true; v.priority = 150f; v.weight = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            prof.hideFlags = HideFlags.DontSave;
            fill(prof);
            v.sharedProfile = prof;
            return v;
        }

        void LaneAxes()
        {
            Vector3 d = laneEnd - laneStart;
            _laneLen = Mathf.Max(1f, d.magnitude);
            _laneDir = d / _laneLen;
            _laneU = Vector3.Cross(Vector3.up, _laneDir).normalized;
            _laneV = Vector3.Cross(_laneDir, _laneU);
        }

        void OnEnable() { if (Application.isPlaying) Prepare(); }

        void OnDisable() { if (Application.isPlaying && Time.timeScale < 1f) Time.timeScale = 1f; }

        // ---------------------------------------------------------------- curves
        /// Fast arrival, small overshoot, hard hold: cubic ease-out into 1 + ov at 72 %, quick smoothstep back, then exactly 1.
        public static float Snap(float x, float ov)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float a = 0.72f;
            if (x < a) { float u = 1f - x / a; return (1f + ov) * (1f - u * u * u); }
            float w = (x - a) / (1f - a);
            return 1f + ov * (1f - w * w * (3f - 2f * w));
        }

        static Vector3 Bez(Vector3 a, Vector3 c, Vector3 b, float s) { float u = 1f - s; return u * u * a + 2f * u * s * c + s * s * b; }
        static Vector3 BezD(Vector3 a, Vector3 c, Vector3 b, float s) => 2f * (1f - s) * (c - a) + 2f * s * (b - c);

        /// deterministic hash noise in [0, 1)
        static float H(int i, int k)
        {
            uint x = (uint)(i * 73856093) ^ (uint)(k * 19349663) ^ 0x9e3779b9u;
            x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15; x *= 0x27d4eb2du; x ^= x >> 16;
            return (x & 0xffffffu) / 16777216f;
        }

        // ---------------------------------------------------------------- evaluation (pure function of time)
        /// Poses every piece and rebuilds the FX for event time t (s, &lt; 0 = not started) and lane clock (s, &lt; 0 = off).
        public void Evaluate(float t, float lane, Camera cam)
        {
            if (_glow == null) Prepare();
            LaneAxes();
            var ct = cam ? cam.transform : null;
            _cp = ct ? transform.InverseTransformPoint(ct.position) : viewPoint;
            Quaternion cr = ct ? Quaternion.Inverse(transform.rotation) * ct.rotation : Quaternion.identity;
            _cr = cr * Vector3.right; _cu = cr * Vector3.up;
            if (!Application.isPlaying) _lookTarget = viewPoint;

            _glow.Clear(); _beam.Clear();
            foreach (var p in pieces) if (p.t) PosePiece(p, t, lane);
            TriggerFX(t);
            ImpactFX(t);
            LaneFX(t);
            PopsFX();
            _glow.Upload(); _beam.Upload();
            if (_dmgVol) _dmgVol.weight = _damage;
        }

        void Show(Piece p, bool on)
        {
            if (p.shown == on) return;
            p.shown = on;
            p.t.gameObject.SetActive(on);
        }

        void Cols(Piece p, bool on)
        {
            if (!Application.isPlaying || p.cols == null || p.colOn == on) return;
            p.colOn = on;
            foreach (var c in p.cols) if (c) c.enabled = on;
        }

        void PosePiece(Piece p, float t, float lane)
        {
            Vector3 pos; Quaternion rot;
            switch (p.kind)
            {
                case Kind.Asteroid:
                {
                    bool on = t < impactTime;
                    Show(p, on);
                    if (!on) return;
                    float u = Mathf.Clamp01((t - p.t0) / p.dur);
                    pos = Vector3.LerpUnclamped(p.p0, p.p1, Mathf.Pow(u, 2.2f)) + p.ctrl * Mathf.Sin(Mathf.PI * u);
                    // spin only about the collision axis: the flat side keeps facing the other rock (hidden edge-on)
                    rot = Quaternion.AngleAxis(p.phase + p.spin * Mathf.Max(0f, t) * (1f + 2f * u * u), p.spinAxis) * p.r0;
                    break;
                }
                case Kind.Fragment:
                {
                    float d = t - impactTime;
                    Show(p, d >= 0f);
                    if (d < 0f) return;
                    float s = Snap(d / p.dur, p.overshoot);
                    pos = p.p0 + (p.p1 - p.p0) * s + p.drift * d;
                    rot = Quaternion.AngleAxis(p.spinBurst * s + p.spin * d, p.spinAxis) * p.r0;
                    if (p.glow > 0f)
                    {
                        float e = p.glow * Mathf.Exp(-d / 2.5f);
                        if (e > 0.02f) Sprite(_glow, pos, p.radius * 2.4f, Hot * e);
                    }
                    break;
                }
                case Kind.Enemy:
                {
                    float d = t - p.t0;
                    Show(p, d >= 0f);
                    if (d < 0f) return;
                    float x = d / p.dur, s = Snap(x, p.overshoot);
                    pos = Bez(p.p0, p.ctrl, p.p1, s);
                    float settle = Mathf.Clamp01((x - 1f) * 1.5f), ph = t + p.phase;
                    pos += new Vector3(Mathf.Sin(ph * 0.41f) * 1.2f, Mathf.Sin(ph * 1.3f) * p.bob, 0f) * settle;
                    Vector3 vel = BezD(p.p0, p.ctrl, p.p1, Mathf.Min(s, 1f));
                    Vector3 look = _lookTarget - pos;
                    float w = Mathf.SmoothStep(0f, 1f, (x - 0.45f) / 0.4f);
                    Vector3 f = Vector3.Slerp(vel.sqrMagnitude > 1e-4f ? vel.normalized : look.normalized, look.normalized, w);
                    rot = Quaternion.LookRotation(f, Vector3.up);
                    Sprite(_glow, pos + f * (p.radius * 0.95f), Mathf.Min(p.radius * 2.6f, 2.4f), Pink * 2.2f);           // sensor glint: reads at 40 m in the dark
                    if (d < 0.35f) Sprite(_glow, p.p0, p.radius * 9f, Pink * (9f * (1f - d / 0.35f)));        // launch flash
                    if (x < 1f)
                    {
                        Vector3 back = Bez(p.p0, p.ctrl, p.p1, Snap(Mathf.Max(0f, x - 0.09f / p.dur), p.overshoot));
                        Streak(back, pos, p.radius * 0.9f, new Color(Pink.r, Pink.g, Pink.b, 0f) , Pink * (5f * (1f - x * x)));
                    }
                    break;
                }
                case Kind.PathPiece:
                {
                    float d = t - p.t0;
                    Show(p, d >= 0f);
                    if (d < 0f) { Cols(p, false); return; }
                    float x = d / p.dur, s = Snap(x, p.overshoot);
                    pos = Bez(p.p0, p.ctrl, p.p1, s);
                    rot = Quaternion.SlerpUnclamped(p.r0, p.r1, s);
                    Cols(p, x >= 1f);
                    if (x >= 1f) pos += Vector3.up * (Mathf.Sin(t * 0.6f + p.phase) * p.bob * Mathf.Clamp01((x - 1f) * 0.8f));
                    float lockT = d - p.dur * 0.72f;                                                            // reaches the slot (Snap peak)
                    if (lockT >= 0f)
                    {
                        Vector3 g = pos + Vector3.up * p.radius;
                        Sprite(_glow, g, 5f, Amber * (5f * Mathf.Exp(-lockT / 0.12f)));                           // lock blink
                        Sprite(_glow, g, 1.8f, Amber * 2.4f);                                                      // guide light
                    }
                    else
                    {
                        Vector3 back = Bez(p.p0, p.ctrl, p.p1, Snap(Mathf.Max(0f, x - 0.08f / p.dur), p.overshoot));
                        Streak(back, pos, 1.2f, new Color(Amber.r, Amber.g, Amber.b, 0f), Amber * 1.5f);
                    }
                    break;
                }
                default:                                                                                            // lane debris
                {
                    if (lane < 0f || !LanePose(p, lane, out pos, out float lt, out _)) { Show(p, false); return; }
                    Show(p, true);
                    rot = Quaternion.AngleAxis(p.spin * lt, p.spinAxis) * p.r0;
                    // still hot from the collision: an ember halo behind the piece (its own body hides the middle -> glowing rim)
                    Sprite(_glow, pos, p.radius * 3.2f, Hot * 2.6f);
                    Streak(pos + _laneDir * (p.radius + 6f), pos, p.radius * 0.8f, new Color(Hot.r, Hot.g, Hot.b, 0f), Hot * 1.0f);
                    break;
                }
            }
            p.t.localPosition = pos;
            p.t.localRotation = rot;
        }

        /// lane debris position at lane clock `lane`; false while not flying (before its wave, past the player, hit)
        bool LanePose(Piece p, float lane, out Vector3 pos, out float lt, out int cycle)
        {
            pos = default; cycle = 0;
            lt = lane - firstWave - p.t0;
            if (lt < 0f) return false;
            float period = Mathf.Max(1f, waveCount * wavePeriod);
            cycle = Mathf.FloorToInt(lt / period);
            lt -= cycle * period;
            if (_laneStopAt >= 0f && firstWave + p.t0 + cycle * period > _laneStopAt) return false;           // lane cleared: no new waves
            if (p.hitCycle == cycle) return false;
            float z = _laneLen + preroll - laneSpeed * lt;
            if (z < -postroll) return false;
            pos = laneStart + _laneDir * z + _laneU * p.slot.x + _laneV * p.slot.y;
            return true;
        }

        void TriggerFX(float t)
        {
            float fade = t < 0f ? 1f : Mathf.Clamp01(1f - t / 0.4f);
            if (fade <= 0f) return;
            // six survey beacons around the zone entrance, a light chasing round them (no drawn ring)
            float clock = Application.isPlaying ? Time.time : 0f;
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f + Mathf.PI / 6f;
                Vector3 bp = triggerCenter + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (triggerRadius * 0.6f);
                float chase = Mathf.Exp(-Mathf.Repeat(clock * 1.2f - k / 6f, 1f) / 0.08f);
                Sprite(_glow, bp, 1.1f, Amber * ((1.8f + 4f * chase) * fade));
                if (t >= 0f) Sprite(_glow, bp, 5f, Amber * (5f * fade * fade));                                      // entered: one blink
            }
        }

        void ImpactFX(float t)
        {
            Vector3 C = collisionPoint;
            float pre = t - (impactTime - 0.35f), d = t - impactTime;
            float lum = 0f; Color lc = White;
            if (pre >= 0f && d < 0f)
            {
                float u = pre / 0.35f;                                                                              // the contact squeezes white-hot
                Sprite(_glow, C, 14f + 20f * u, White * (16f * u * u * u));
                lum = flashLumen * 0.03f * u * u;
            }
            if (d >= 0f)
            {
                Sprite(_glow, C, Mathf.Lerp(45f, 150f, 1f - Mathf.Exp(-d / 0.07f)), White * (30f * Mathf.Exp(-d / 0.12f)));   // white core
                Sprite(_glow, C, Mathf.Lerp(120f, 300f, 1f - Mathf.Exp(-d / 0.25f)), Hot * (5f * Mathf.Exp(-d / 0.4f)));   // hot halo
                Sprite(_glow, C, 110f, Hot * (2.2f * Mathf.Exp(-d / 2.2f)));                                     // afterglow
                Flare(C, 1100f, 20f, Blue * (26f * Mathf.Exp(-d / 0.12f)));                                       // anamorphic streak
                Flare(C, 420f, 55f, Blue * (6f * Mathf.Exp(-d / 0.2f)));
                float rr = 1f - Mathf.Exp(-d / 0.4f);
                Sprite(_glow, C, 80f + 340f * rr, Color.Lerp(Blue, Hot, rr) * (1.4f * Mathf.Exp(-d / 0.5f)));          // pressure wave: soft, no edge
                Sparks(d);
                lum = flashLumen * Mathf.Exp(-d / 0.2f) + emberLumen * Mathf.Exp(-d / 2.2f);
                lc = Color.Lerp(Hot, White, Mathf.Exp(-d / 0.3f));
            }
            if (flashLight)
            {
                flashLight.transform.localPosition = C;
                flashLight.intensity = lum; flashLight.color = lc;
                flashLight.enabled = lum > 1f;
            }
            if (_flashVol) _flashVol.weight = d < 0f ? 0f : d < 0.06f ? 1f : Mathf.Exp(-(d - 0.06f) / 0.3f);
        }

        void Sparks(float d)
        {
            const float k = 1.6f;
            for (int i = 0; i < sparkCount; i++)
            {
                float life = 0.5f + 1.1f * H(i, 1);
                if (d > life) continue;
                // ejecta fly out across the collision axis (local YZ plane), +-25 deg along it
                float az = H(i, 2) * 6.2831853f, el = (H(i, 3) - 0.5f) * 0.9f;
                Vector3 dir = new Vector3(Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az), Mathf.Cos(el) * Mathf.Sin(az));
                float v = 60f + 150f * H(i, 4);
                float d0 = Mathf.Max(0f, d - 0.06f);
                Vector3 a = collisionPoint + dir * (v * (1f - Mathf.Exp(-k * d0)) / k);
                Vector3 b = collisionPoint + dir * (v * (1f - Mathf.Exp(-k * d)) / k);
                float age = d / life;
                Color c = Color.Lerp(White * 14f, Hot * 5f, Mathf.Sqrt(age)) * (1f - age);
                Streak(a, b, 0.9f + 1.4f * H(i, 5), new Color(c.r, c.g, c.b, 0f), c);
            }
        }

        void LaneFX(float t)
        {
            float l0 = t - (pathReadyTime + 0.3f);
            if (l0 < 0f) return;
            float R = laneRadius;
            for (int k = 0; k < 6; k++)                                                                            // entrance gate
            {
                float a = k * Mathf.PI / 3f;
                Beacon(laneStart + (_laneU * Mathf.Cos(a) + _laneV * Mathf.Sin(a)) * R, l0 - k * 0.05f, 1.6f);
            }
            int rings = Mathf.FloorToInt(_laneLen / 17f);
            for (int r = 1; r <= rings; r++)                                                                      // rings along the lane
            {
                Vector3 c = laneStart + _laneDir * (r * 17f);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    Beacon(c + (_laneU * Mathf.Cos(a) + _laneV * Mathf.Sin(a)) * R, l0 - 0.3f - r * 0.12f, 1.2f);
                }
            }
            float g = l0 - 0.4f - rings * 0.12f;                                                                   // goal
            if (g >= 0f)
            {
                Sprite(_glow, laneEnd, 2.5f, White * 2.5f);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    Beacon(laneEnd + (_laneU * Mathf.Cos(a) + _laneV * Mathf.Sin(a)) * 3f, g - k * 0.05f, 1.3f);
                }
            }
            if (_clearAt >= 0f)
            {
                float a = Time.unscaledTime - _clearAt;
                Sprite(_glow, laneEnd, 40f, White * (10f * Mathf.Exp(-a / 0.2f)));
            }
        }

        void Beacon(Vector3 p, float tt, float size)
        {
            if (tt < 0f) return;
            Sprite(_glow, p, size, Amber * 2.2f);
            Sprite(_glow, p, size * 5f, Amber * (6f * Mathf.Exp(-tt / 0.1f)));
        }

        void PopsFX()
        {
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                float a = Time.unscaledTime - _pops[i].at;
                if (a > 0.6f) { _pops.RemoveAt(i); continue; }
                Sprite(_glow, _pops[i].p, 6f, Color.Lerp(White, Hot, a / 0.6f) * (10f * Mathf.Exp(-a / 0.12f)));
            }
        }

        // ---------------------------------------------------------------- billboards
        /// small sprites fade out right at the camera (lane beacons and debris pass within a few metres of it)
        float Near(Vector3 c, float size) => size >= 20f ? 1f : Mathf.Clamp01(((c - _cp).magnitude - 1.5f - size * 0.5f) / 10f);

        void Sprite(Batch b, Vector3 c, float size, Color col)
        {
            float nf = Near(c, size);
            if (nf <= 0f) return;
            col *= nf;
            Vector3 r = _cr * (size * 0.5f), u = _cu * (size * 0.5f);
            b.Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, col);
        }

        /// screen-aligned horizontal streak (beam sprite), bright in the middle, fading to both ends
        void Flare(Vector3 c, float w, float h, Color col)
        {
            Vector3 r = _cr * (w * 0.5f), u = _cu * (h * 0.5f);
            var c0 = new Color(col.r, col.g, col.b, 0f);
            _beam.Quad(c - r - u, c - u, c + u, c - r + u, c0, col);
            _beam.Quad(c - u, c + r - u, c + r + u, c + u, col, c0);
        }

        /// beam sprite stretched from tail a to head b, turned to face the camera; tail / head colours
        void Streak(Vector3 a, Vector3 b, float width, Color tail, Color head)
        {
            Vector3 ax = b - a;
            Vector3 side = Vector3.Cross(ax, (a + b) * 0.5f - _cp);
            if (side.sqrMagnitude < 1e-10f) side = _cu;
            side = side.normalized * (width * 0.5f);
            Vector3 ext = ax.sqrMagnitude > 1e-8f ? ax.normalized * (width * 0.5f) : Vector3.zero;
            Vector3 a2 = a - ext, b2 = b + ext;
            _beam.Quad(a2 - side, b2 - side, b2 + side, a2 + side, tail, head);
        }

        /// dynamic mesh of additive quads (float vertex colours carry the HDR intensity) under the event root
        sealed class Batch
        {
            struct V { public Vector3 p; public Color c; public Vector2 uv; }
            static readonly VertexAttributeDescriptor[] Layout =
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            };
            const MeshUpdateFlags Fast = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
            readonly List<V> _v = new List<V>(4096);
            readonly List<int> _i = new List<int>(8192);
            readonly Mesh _m;
            readonly GameObject _go;

            public Batch(string name, Transform parent, Material mat)
            {
                var go = _go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                _m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                _m.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = _m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            public void Destroy() { if (_m) DestroyImmediate(_m); if (_go) DestroyImmediate(_go); }

            public void Clear() { _v.Clear(); _i.Clear(); }
            public void Tri(int a, int b, int c) { _i.Add(a); _i.Add(b); _i.Add(c); }

            /// a, b, c, d = uv (0,0) (1,0) (1,1) (0,1); colour ca on the a/d edge, cb on the b/c edge
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb)
            {
                int n = _v.Count;
                _v.Add(new V { p = a, c = ca, uv = new Vector2(0f, 0f) });
                _v.Add(new V { p = b, c = cb, uv = new Vector2(1f, 0f) });
                _v.Add(new V { p = c, c = cb, uv = new Vector2(1f, 1f) });
                _v.Add(new V { p = d, c = ca, uv = new Vector2(0f, 1f) });
                Tri(n, n + 1, n + 2); Tri(n, n + 2, n + 3);
            }

            public void Upload()
            {
                _m.Clear();
                _m.SetVertexBufferParams(_v.Count, Layout);
                if (_v.Count > 0) _m.SetVertexBufferData(_v, 0, 0, _v.Count, 0, Fast);
                _m.SetIndexBufferParams(_i.Count, IndexFormat.UInt32);
                if (_i.Count > 0) _m.SetIndexBufferData(_i, 0, 0, _i.Count, Fast);
                _m.subMeshCount = 1;
                _m.SetSubMesh(0, new SubMeshDescriptor(0, _i.Count), Fast);
                _m.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            }
        }

        // ---------------------------------------------------------------- play mode
        Transform Player() { var fc = FlightCamera.Main; return fc ? fc.target : null; }

        void Update()
        {
            float dt = Time.deltaTime;
            var pl = Player();
            Vector3 body = pl ? transform.InverseTransformPoint(pl.position + Vector3.up * bodyCenter) : viewPoint;
            _lookTarget = body;
            Keys(pl);

            if (!_started)
            {
                _idle += dt;
                if ((pl && (body - triggerCenter).magnitude < triggerRadius) || (autoStartDelay > 0f && _idle >= autoStartDelay) || (!pl && _idle > 2f))
                    Begin();
            }
            else
            {
                float prev = _t;
                _t += dt;
                Cross(prev, _t, body, dt);
            }

            // dash lane: opens when the path is complete, runs once the player reaches the gate
            if (_started && !_laneOn && !_cleared && _failTimer <= 0f && _t >= pathReadyTime + 0.3f)
                if (pl ? (body - laneStart).magnitude < laneRadius + 1.5f : _t >= laneAutoTime) StartLane();
            if (_laneOn)
            {
                float prevLane = _lane;
                _lane += dt;
                if (pl && _laneLive) LaneHits(body, prevLane);
            }
            if (_failTimer > 0f)
            {
                _failTimer -= Time.unscaledDeltaTime;
                if (_failTimer <= 0f) RestartLane(pl);
            }
            _damage *= Mathf.Exp(-Time.unscaledDeltaTime / 0.35f);
            _invuln -= dt;
            if (pl && _push.sqrMagnitude > 1e-4f) { pl.position += _push * dt; _push *= Mathf.Exp(-dt / 0.18f); }
        }

        void LateUpdate() => Evaluate(_started ? _t : -1f, _laneOn ? _lane : -1f, Camera.main);

        void Keys(Transform pl)
        {
            bool f5 = false, f6 = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null) { f5 = k.f5Key.wasPressedThisFrame; f6 = k.f6Key.wasPressedThisFrame; }
#else
            f5 = Input.GetKeyDown(KeyCode.F5); f6 = Input.GetKeyDown(KeyCode.F6);
#endif
            if (f5) Replay(pl);
            if (f6) JumpToLane(pl);
        }

        void ResetLaneState()
        {
            _laneOn = _laneLive = _cleared = false;
            _lane = -1f; _laneStopAt = -1f; _clearAt = -1f; _failTimer = 0f; _hits = 0; _invuln = 0f;
            foreach (var p in pieces) p.hitCycle = -1;
        }

        void Begin() { _started = true; _t = 0f; ResetLaneState(); }

        /// F5: back to the viewing spot, event from the top
        public void Replay(Transform pl)
        {
            if (pl) Teleport(pl, viewPoint, collisionPoint - viewPoint);
            Begin();
        }

        /// F6: straight to the dash lane gate (path and lane already there)
        public void JumpToLane(Transform pl)
        {
            _started = true;
            _t = Mathf.Max(_t, pathReadyTime + 1.5f);
            ResetLaneState();
            if (pl) Teleport(pl, laneStart - _laneDir * 3f, _laneDir);
        }

        void StartLane()
        {
            ResetLaneState();
            _laneOn = _laneLive = true;
            _lane = 0f;
        }

        void RestartLane(Transform pl)
        {
            if (pl) Teleport(pl, laneStart - _laneDir * 3f, _laneDir);
            StartLane();
        }

        void Fail()
        {
            _laneOn = _laneLive = false;
            _failTimer = 1.2f;
        }

        void Clear()
        {
            _laneLive = false; _cleared = true;
            _laneStopAt = _lane; _clearAt = Time.unscaledTime;
            GameFeel.HitStop(0.12f, 0.15f);
        }

        void Teleport(Transform pl, Vector3 localBody, Vector3 localFwd)
        {
            pl.position = transform.TransformPoint(localBody) - Vector3.up * bodyCenter;
            var f = pl.GetComponent<ZeroGFlight>();
            if (f) f.ResetMotion();
            Vector3 wf = transform.TransformDirection(localFwd).normalized;
            float yaw = Mathf.Atan2(wf.x, wf.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(wf.y, -1f, 1f)) * Mathf.Rad2Deg;
            pl.rotation = Quaternion.Euler(0f, yaw, 0f);
            var cam = FlightCamera.Main;
            if (cam) { cam.SetLook(yaw, pitch); cam.SnapToTarget(); }
            _push = Vector3.zero;
        }

        /// one-shot effects when the clock crosses a beat (play mode only)
        void Cross(float prev, float t, Vector3 body, float dt)
        {
            var cam = FlightCamera.Main;
            if (t > approachFrom && t < impactTime && cam)                                                          // rumble builds
            {
                float u = Mathf.Clamp01((t - approachFrom) / (impactTime - approachFrom));
                cam.Shake(dt * 1.8f * 0.32f * u * u);
            }
            if (prev < impactTime && t >= impactTime)                                                             // 쾅
            {
                float dist = (body - collisionPoint).magnitude;
                if (cam) cam.Shake(0.85f * Mathf.Clamp01(1.3f - dist / 400f));
                GameFeel.HitStop(0.1f, 0.04f);
                if (dist < 70f)
                {
                    _damage = 1f;
                    _push = transform.TransformDirection((body - collisionPoint).normalized) * 14f;
                }
            }
        }

        void LaneHits(Vector3 body, float prevLane)
        {
            if ((body - laneEnd).magnitude < laneRadius + 1f) { Clear(); return; }
            if (_invuln > 0f) return;
            float r0 = playerRadius;
            foreach (var p in pieces)
            {
                if (p.kind != Kind.LaneDebris) continue;
                if (!LanePose(p, _lane, out var b, out _, out int cyc)) continue;
                // swept test (debris move ~0.5 - 2 m per frame): closest point on last frame's segment
                Vector3 a = LanePose(p, prevLane, out var pa, out _, out int cycA) && cycA == cyc ? pa : b;
                Vector3 ab = b - a;
                float s = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(body - a, ab) / ab.sqrMagnitude) : 0f;
                Vector3 q = a + ab * s;
                float rr = p.radius * 0.85f + r0;
                if ((q - body).sqrMagnitude > rr * rr) continue;
                p.hitCycle = cyc; _hits++; TotalHits++; _invuln = 0.7f; _damage = 1f;
                _pops.Add((q, Time.unscaledTime));
                var cam = FlightCamera.Main;
                if (cam) cam.Shake(0.45f);
                GameFeel.HitStop(0.07f, 0.05f);
                Vector3 away = body - q;
                away = (away.sqrMagnitude > 1e-4f ? away.normalized : _laneU) - _laneDir * 0.8f;
                _push = transform.TransformDirection(away.normalized) * 9f;
                if (_hits >= maxHits) Fail();
                break;
            }
        }

        string Phase()
        {
            if (!_started) return "EVENT ZONE  //  fly between the amber beacons ahead  (F5: start now)";
            if (_failTimer > 0f) return "LANE FAILED  //  back to the gate";
            if (_cleared) return "LANE CLEAR";
            if (_laneOn) return $"DASH LANE  //  dodge the debris   HULL {Mathf.Max(0, maxHits - _hits)}/{maxHits}";
            if (_t < impactTime) return "!!  MASS CONVERGENCE  !!";
            if (_t < impactTime + 1.2f) return "IMPACT";
            if (_t < enemiesFrom + 2.6f) return "HUSK CONTACT";
            if (_t < pathFrom) return "HUSK HOLDING  (prototype: timed combat window)";
            if (_t < pathReadyTime) return "WRECK PATH FORMING";
            return "DASH LANE OPEN  //  follow the path to the amber gate";
        }

        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.LowerCenter };
            }
            void Shadowed(Rect r, string s, GUIStyle st, Color c)
            {
                var o = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, st);
                GUI.color = c; GUI.Label(r, s, st);
                GUI.color = o;
            }
            Color col = _failTimer > 0f ? new Color(1f, 0.35f, 0.35f) : _cleared ? new Color(0.7f, 1f, 0.75f) : new Color(1f, 0.78f, 0.45f);
            Shadowed(new Rect(0f, 18f, Screen.width, 40f), Phase(), _big, col);
            Shadowed(new Rect(0f, Screen.height - 40f, Screen.width, 30f), "[F5] replay event    [F6] jump to dash lane    [Shift] dash    [Tab] switch character", _small, new Color(1f, 1f, 1f, 0.85f));
        }
    }
}
