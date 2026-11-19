using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// One CITADEL homing dart (~2 m), as a pure function of its flight clock:
    ///   EJECT   0 .. eject   cold launch: a pop at the tube mouth, the dart slides out slowing down and rolling
    ///   IGNITE  at eject     the motor lights with a white flash + glint (AC6 ignition), then a hot plume
    ///   FLY     .. T         accelerating along a curve that leaves along the tube and comes in on the target; the
    ///                        seeker tip blinks; an ionised trail glows behind it and cools (vacuum: no smoke)
    ///   IMPACT  at T         library Explosion (scaled) on the target; the trail fades out where it flew
    /// Launch() once; SetTarget() every frame to home onto a moving target. Interceptable: Intercept() blows it up
    /// in flight.
    public class HomingDartFX : MonoBehaviour
    {
        public Transform model;
        public Light motorLight;
        public HuskFXMaterials materials = new HuskFXMaterials();
        public Vector3 nozzleLocal = new Vector3(0f, -0.12f, -0.98f);
        public Vector3 seekerLocal = new Vector3(0f, -0.11f, 0.95f);

        [Header("Flight")]
        public float ejectTime = 0.24f;
        public float ejectSpeed = 9f;
        [Tooltip("how wide the curve swings out (fraction of the distance)")] public float swing = 0.28f;
        public float trailSeconds = 0.6f;
        public float blastScale = 0.55f;

        /// (position, normal) at the impact
        public event System.Action<Vector3, Vector3> Impacted;

        float _t = -1f, _T = 2f, _endT = -1f;
        Vector3 _p0, _d0, _target, _targetN, _side;
        Collider _targetCol;
        int _seed;
        bool _ended, _intercepted;
        HuskFXCanvas _fx;
        readonly List<HitFXInstance> _manual = new List<HitFXInstance>();

        public bool Busy => _t >= 0f;
        public float Clock => _t;
        public float FlightTime => _T;

        void OnDisable()
        {
            HuskFX.ClearManual(_manual);
            if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }   // destroying children while deactivating is not allowed
            HuskFX.SetLight(motorLight, 0f, HuskFX.LightMagenta);
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        /// tubePos / tubeDir: launch tube mouth and axis; target: where it hits; flightTime: launch -> impact (s).
        public void Launch(Vector3 tubePos, Vector3 tubeDir, Vector3 target, float flightTime, int seed, Collider targetCol = null, Vector3 targetNormal = default)
        {
            _p0 = tubePos; _d0 = tubeDir.sqrMagnitude > 1e-6f ? tubeDir.normalized : Vector3.forward;
            _target = target; _targetCol = targetCol;
            _targetN = targetNormal.sqrMagnitude > 1e-6f ? targetNormal.normalized : -_d0;
            _T = Mathf.Max(ejectTime + 0.3f, flightTime);
            _seed = seed;
            _t = 0f; _ended = false; _intercepted = false; _endT = -1f;
            float a = HuskFX.H(seed, 1) * Mathf.PI * 2f;
            Vector3 u = Vector3.Cross(_d0, Mathf.Abs(_d0.y) > 0.9f ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(_d0, u);
            _side = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * Mathf.Lerp(0.4f, 1f, HuskFX.H(seed, 2));
            if (model) model.gameObject.SetActive(true);
            HuskFX.ClearManual(_manual);
        }

        public void SetTarget(Vector3 target) { _target = target; }

        /// Shot down in flight: blows up where it is.
        public void Intercept()
        {
            if (_t < 0f || _ended) return;
            _intercepted = true;
            End(Position(_t), -Forward(_t));
        }

        // ---------------------------------------------------------------- path (pure)
        Vector3 EjectEnd => _p0 + _d0 * (ejectSpeed * ejectTime * 0.5f);

        /// flight progress 0..1 at clock t (accelerating), -1 while ejecting
        float S(float t)
        {
            if (t < ejectTime) return -1f;
            float u = Mathf.Clamp01((t - ejectTime) / (_T - ejectTime));
            return Mathf.Pow(u, 1.7f) * 0.7f + u * 0.3f;
        }

        void Ctrl(out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d)
        {
            a = EjectEnd; d = _target;
            float D = Vector3.Distance(a, d);
            b = a + _d0 * (D * 0.4f) + _side * (D * swing);
            Vector3 inDir = (d - b).normalized;
            c = d - Vector3.Lerp(inDir, _targetN * -1f, 0.0f) * (D * 0.25f) + _side * (D * swing * 0.35f);
        }

        public Vector3 Position(float t)
        {
            if (t <= 0f) return _p0;
            if (t < ejectTime)
            {
                float x = t / ejectTime;
                return _p0 + _d0 * (ejectSpeed * ejectTime * (x - 0.5f * x * x));
            }
            Ctrl(out var a, out var b, out var c, out var d);
            float s = S(t), m = 1f - s;
            return m * m * m * a + 3f * m * m * s * b + 3f * m * s * s * c + s * s * s * d;
        }

        public Vector3 Forward(float t)
        {
            if (t < ejectTime) return _d0;
            Ctrl(out var a, out var b, out var c, out var d);
            float s = Mathf.Min(S(t), 0.999f), m = 1f - s;
            Vector3 dv = 3f * m * m * (b - a) + 6f * m * s * (c - b) + 3f * s * s * (d - c);
            Vector3 f = dv.sqrMagnitude > 1e-8f ? dv.normalized : _d0;
            float blend = Mathf.Clamp01((t - ejectTime) / 0.12f);                 // turns off the tube axis smoothly
            return Vector3.Slerp(_d0, f, blend);
        }

        // ---------------------------------------------------------------- clock
        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (!Application.isPlaying) HuskFX.TickManual(_manual, dt);
            if (_t < 0f) return;
            _t += dt;
            if (!_ended && _t >= _T) End(_target, _targetN);
            Evaluate(_t);
            if (_ended && _t - _endT > trailSeconds + 0.1f && _manual.Count == 0)
            {
                _t = -1f;
                if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }
                HuskFX.SetLight(motorLight, 0f, HuskFX.LightMagenta);
            }
        }

        void End(Vector3 p, Vector3 n)
        {
            _ended = true; _endT = _t;
            if (model) model.gameObject.SetActive(false);
            HuskFX.PlayHit(HitKind.Explosion, p, n, _intercepted ? null : _targetCol, _intercepted ? blastScale * 0.7f : blastScale, _manual);
            Impacted?.Invoke(p, n);
        }

        public void Evaluate(float t)
        {
            if (_fx == null || !_fx.Valid) _fx = new HuskFXCanvas(transform, materials);
            _fx.Begin();
            float flightEnd = _ended ? _endT : t;
            Vector3 pos = Position(Mathf.Min(t, flightEnd));
            Vector3 fwd = Forward(Mathf.Min(t, flightEnd));
            float roll = (t * 380f + HuskFX.H(_seed, 3) * 360f) * (t < ejectTime ? 0.35f : 1f);
            var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);
            if (t < ejectTime) rot = rot * Quaternion.Euler(HuskFX.N(t * 6f, _seed) * 6f, HuskFX.N(t * 6f, _seed + 1) * 6f, 0f);   // loose tumble
            transform.SetPositionAndRotation(pos, rot);
            float lumen = 0f;

            // tube pop
            float tp = Mathf.Exp(-t / 0.05f);
            if (tp > 0.01f)
            {
                _fx.dot.Sprite(_p0, 0.7f, HuskFX.Heat(0.85f) * (4f * tp));
                _fx.glow.Sprite(_p0, 1.8f, HuskFX.Heat(0.6f) * (0.6f * tp));
                lumen += 3000f * tp;
            }

            if (!_ended)
            {
                Vector3 nozzle = transform.TransformPoint(nozzleLocal);
                Vector3 back = -transform.forward;
                float ig = t - ejectTime;
                if (ig >= 0f)
                {
                    // ignition flash, then the plume
                    float flash = Mathf.Exp(-ig / 0.04f);
                    _fx.dot.Sprite(nozzle, 0.4f + 0.7f * flash, HuskFX.Heat(0.9f) * (2.2f + 6f * flash));
                    _fx.glow.Sprite(nozzle, 1.0f + 1.8f * flash, HuskFX.Heat(0.6f) * (0.35f + 1.1f * flash));
                    _fx.glint.Flare(nozzle, 1.5f + 6f * flash, 0.12f + 0.25f * flash, HuskFX.Heat(0.75f) * (0.5f + 3f * flash));
                    float flick = 1f + 0.2f * HuskFX.N(t * 37f, _seed + 2);
                    float grow = HuskFX.Smooth(0f, 0.12f, ig);
                    float len = (1.3f + 0.6f * HuskFX.Smooth(0f, 1f, ig)) * flick * grow;
                    _fx.streak.Streak(nozzle + back * len, nozzle, 0.42f * grow, Color.clear, HuskFX.Heat(0.6f) * (1.1f * grow));
                    _fx.streak.Streak(nozzle + back * (len * 0.55f), nozzle, 0.15f * grow, Color.clear, HuskFX.Heat(0.88f) * (3.2f * grow * flick));
                    lumen += 1800f * grow * flick + 9000f * flash;
                }
                // seeker tip blink
                Vector3 seeker = transform.TransformPoint(seekerLocal);
                float blink = Mathf.Repeat(t * 6f + HuskFX.H(_seed, 4), 1f) < 0.25f ? 1f : 0.3f;
                _fx.dot.Sprite(seeker, 0.12f, HuskFX.Heat(0.8f) * (2.5f * blink));
                _fx.glow.Sprite(seeker, 0.5f, HuskFX.Heat(0.6f) * (0.4f * blink));
            }

            // ionised trail: the path it flew over the last trailSeconds, hot near the dart, cooling behind
            float t0 = Mathf.Max(ejectTime, t - trailSeconds), t1 = Mathf.Min(t, flightEnd);
            if (t1 > t0 + 1e-3f)
            {
                const int Seg = 22;
                Vector3 prev = Position(t0);
                for (int i = 1; i <= Seg; i++)
                {
                    float tt = Mathf.Lerp(t0, t1, i / (float)Seg);
                    Vector3 p = Position(tt);
                    float ageA = (t - Mathf.Lerp(t0, t1, (i - 1) / (float)Seg)) / trailSeconds, ageB = (t - tt) / trailSeconds;
                    Color ca = HuskFX.Heat(0.7f - 0.4f * ageA) * (1.1f * (1f - ageA) * (1f - ageA));
                    Color cb = HuskFX.Heat(0.7f - 0.4f * ageB) * (1.1f * (1f - ageB) * (1f - ageB));
                    float w = 0.12f + 0.25f * Mathf.Clamp01(ageB);
                    _fx.streak.Streak(prev, p, w, ca, cb);
                    prev = p;
                }
            }

            _fx.End();
            if (motorLight)
            {
                motorLight.transform.position = transform.TransformPoint(nozzleLocal + Vector3.back * 0.4f);
                HuskFX.SetLight(motorLight, _ended ? 0f : lumen, HuskFX.LightMagenta);
            }
        }
    }
}
