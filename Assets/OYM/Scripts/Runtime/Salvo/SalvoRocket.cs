using System.Collections.Generic;
using OYM.Signal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// One ORBIT FRAME micro-missile (~0.45 m), launched and pooled by OrbitSalvo, drawn from its flight clock:
    ///   EJECT   0 .. ejectTime   cold launch out of the shoulder pod: pops up and out along the tube, slowing, rolling
    ///   IGNITE  at ejectTime     a blue-white flash + glint floods the shoulders (the AC6 ignition), then the amber plume
    ///   FLY     .. T             accelerates along a curve that climbs, swings out to its own side and comes in on the
    ///                            target; the end point follows the target every frame. A target that dies on the way
    ///                            hands the missile to another lock of the salvo: the curve restarts where it is, at its
    ///                            speed (no jump); with none left it flies on to the last point.
    ///   IMPACT  at T             library Explosion; OrbitSalvo applies the damage; the burn trail cools and fades
    /// The trail is the path actually flown (sampled), hot amber at the missile cooling to a dark ember behind it.
    /// Amber / blue-white are the players' colours (HuskFX.Heat - the enemy darts - is magenta).
    public class SalvoRocket : MonoBehaviour
    {
        public static readonly Color Amber = new Color(1f, 0.68f, 0.25f);
        static readonly Color IgniteCol = new Color(0.78f, 0.9f, 1f);
        static readonly Color LightAmber = new Color(1f, 0.7f, 0.38f), LightIgnite = new Color(0.75f, 0.86f, 1f);

        public float ejectTime = 0.2f, ejectSpeed = 10f;
        [Tooltip("after the eject the curve climbs on (m: fraction of the distance, clamped) bending toward the target, and swings out to its side (m) before it comes in")]
        public float climb = 0.12f, climbMin = 2f, climbMax = 8f, swing = 0.07f, swingMin = 1f, swingMax = 6f;
        public float trailSeconds = 0.55f, blastScale = 0.62f;
        [Tooltip("body size (the mesh is 0.7 m): small beside a person-sized pilot and the third-person camera 3.6 m behind")]
        public const float BodyScale = 0.65f;
        public Vector3 nozzleLocal = new Vector3(0f, 0f, -0.34f * BodyScale);

        Transform _model;
        Light _light;
        HuskFXMaterials _mats;
        HuskFXCanvas _fx;
        OrbitSalvo _owner;
        Component _target;
        // eject: tube mouth + axis. fly segment: start point / tangent / speed at its start, clock at its start, length,
        // fresh (accelerating launch curve) or a restart (constant pace, keeps the speed)
        Vector3 _p0, _d0, _a, _da, _end, _side, _off;
        float _t = -1f, _segT0, _segLen, _v0, _endT;
        bool _fresh, _ended;
        int _seed;
        Vector3 _pos, _fwd = Vector3.forward, _lastPos;
        // the flown path for the trail
        const int Hist = 72;
        readonly Vector3[] _hp = new Vector3[Hist];
        readonly float[] _ht = new float[Hist];
        int _hn, _hh;

        public bool Busy => _t >= 0f;
        public float Clock => _t;
        public float ImpactTime => _segT0 + _segLen;
        public Vector3 Position => _pos;

        // ------------------------------------------------------------------------------------------------ build
        static Mesh s_mesh;
        static Material s_mat;
        static HuskFXMaterials s_mats;
        static bool s_matsLooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_matsLooked = false; s_mats = null; }

        public static SalvoRocket Create(Transform parent, int i)
        {
            var go = new GameObject("Salvo missile " + i);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SalvoRocket>();
            var m = new GameObject("body");
            m.transform.SetParent(go.transform, false);
            m.transform.localScale = Vector3.one * BodyScale;
            m.AddComponent<MeshFilter>().sharedMesh = Body();
            var mr = m.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BodyMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m.SetActive(false);
            r._model = m.transform;
            var lg = new GameObject("motor light");
            lg.transform.SetParent(go.transform, false);
            var hd = lg.AddHDLight(LightType.Point);
            var l = lg.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = 9f; l.color = LightAmber;
            l.shadows = LightShadows.None; l.enabled = false;
            if (hd) hd.affectsVolumetric = false;
            r._light = l;
            r._mats = Materials();
            return r;
        }

        static HuskFXMaterials Materials()
        {
            if (s_matsLooked) return s_mats;
            s_matsLooked = true;
            var cfg = Resources.Load<HuskEncounterConfig>("OYM_HuskEncounter");
            var dart = cfg ? cfg.dartPrefab : null;
            if (dart && dart.materials != null && dart.materials.glow && dart.materials.dot && dart.materials.streak && dart.materials.glint && dart.materials.arc)
                s_mats = dart.materials;
            else Debug.LogWarning("[OYM-SALVO] no additive FX materials (Resources/OYM_HuskEncounter dart prefab): missiles fly without plumes");
            return s_mats;
        }

        static Material BodyMaterial()
        {
            if (s_mat) return s_mat;
            var sh = Shader.Find(OYM.Web.WebLightAdapter.IsUrp ? "Universal Render Pipeline/Lit" : "HDRP/Lit");
            if (!sh) return null;
            s_mat = new Material(sh) { name = "OYM_SalvoMissile (runtime)" };
            s_mat.SetColor("_BaseColor", new Color(0.28f, 0.29f, 0.3f));
            s_mat.SetFloat("_Metallic", 0.8f);
            s_mat.SetFloat("_Smoothness", 0.52f);
            return s_mat;
        }

        /// slim body (r 5 cm) from the nozzle (z -0.32) to a pointed nose (z 0.38), four tail fins; +Z forward, metres
        static Mesh Body()
        {
            if (s_mesh) return s_mesh;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            const int seg = 10;
            const float r = 0.05f, z0 = -0.32f, z1 = 0.22f, tip = 0.38f;
            // tube
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(dir * r + Vector3.forward * z0); n.Add(dir);
                v.Add(dir * r + Vector3.forward * z1); n.Add(dir);
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
            }
            // nose cone
            int nb = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var nn = (dir * (tip - z1) + Vector3.forward * r).normalized;
                v.Add(dir * r + Vector3.forward * z1); n.Add(nn);
                v.Add(Vector3.forward * tip); n.Add(nn);
            }
            for (int i = 0; i < seg; i++) { int b = nb + i * 2; tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); }
            // base cap (the nozzle face)
            int cb = v.Count;
            v.Add(Vector3.forward * z0); n.Add(Vector3.back);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r + Vector3.forward * z0); n.Add(Vector3.back);
            }
            for (int i = 0; i < seg; i++) { tri.Add(cb); tri.Add(cb + 2 + i); tri.Add(cb + 1 + i); }
            // fins: thin double-sided plates, x / y crossed
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var side = Vector3.Cross(Vector3.forward, o);
                Vector3 p0 = o * r + Vector3.forward * z0, p1 = o * 0.125f + Vector3.forward * (z0 + 0.02f),
                        p2 = o * 0.11f + Vector3.forward * (z0 + 0.1f), p3 = o * r + Vector3.forward * (z0 + 0.17f);
                foreach (float s in new[] { 1f, -1f })
                {
                    int b = v.Count;
                    var nn = side * s;
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
                    if (s > 0f) { tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3); }
                    else { tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b); tri.Add(b + 3); tri.Add(b + 2); }
                }
            }
            s_mesh = new Mesh { name = "OYM_SalvoMissile" };
            s_mesh.SetVertices(v); s_mesh.SetNormals(n); s_mesh.SetTriangles(tri, 0);
            s_mesh.RecalculateBounds();
            return s_mesh;
        }

        void OnDisable()
        {
            if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }
            if (_light) _light.enabled = false;
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        // ------------------------------------------------------------------------------------------------ launch
        /// pod / tube: launch point and axis; target: the lock (null = a point); end: where it hits now; T: launch -> impact (s)
        public void Launch(OrbitSalvo owner, Vector3 pod, Vector3 tube, Component target, Vector3 end, float T, int seed, int side)
        {
            _owner = owner; _target = target; _seed = seed;
            _p0 = pod; _d0 = tube.sqrMagnitude > 1e-6f ? tube.normalized : Vector3.up;
            _end = end;
            // missiles on one target spread over it (inside a third of its size)
            _off = target ? HuskFX.Dir(seed, 9) * (LockOn.TargetOf(target).radius * 0.35f) : Vector3.zero;
            // each missile swings out on its own side (its pod's side, a little up / down)
            Vector3 toward = (end - pod).sqrMagnitude > 1e-4f ? (end - pod).normalized : Vector3.forward;
            Vector3 u = Vector3.Cross(toward, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.right;
            u.Normalize();
            Vector3 w = Vector3.Cross(u, toward);
            _side = (u * (-side) * Mathf.Lerp(0.55f, 1f, HuskFX.H(seed, 2)) + w * (HuskFX.H(seed, 3) * 2f - 1f) * 0.6f);
            _a = EjectEnd; _da = _d0; _v0 = 0f;
            _segT0 = ejectTime;
            _segLen = Mathf.Max(0.3f, T - ejectTime);
            _fresh = true;
            _t = 0f; _ended = false; _endT = -1f;
            _hn = 0; _hh = 0;
            _pos = _lastPos = pod; _fwd = _d0;
            if (_model) _model.gameObject.SetActive(true);
            Evaluate();
        }

        Vector3 EjectEnd => _p0 + _d0 * (ejectSpeed * ejectTime * 0.5f);

        // ------------------------------------------------------------------------------------------------ path
        float S(float u) => _fresh ? Mathf.Pow(u, 1.7f) * 0.7f + u * 0.3f : u;

        void Ctrl(out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d)
        {
            a = _a; d = _end;
            float D = Vector3.Distance(a, d);
            if (_fresh)
            {
                // pops up, bends toward the target within a few metres, a small spread to its side, then straight in
                Vector3 toward = D > 1e-3f ? (d - a) / D : _da;
                Vector3 up = (_da + toward * 0.8f).normalized;
                b = a + up * Mathf.Clamp(D * climb, climbMin, climbMax) + _side * Mathf.Clamp(D * swing, swingMin, swingMax);
                Vector3 inDir = (d - b).sqrMagnitude > 1e-6f ? (d - b).normalized : _da;
                c = d - inDir * (D * 0.2f) + _side * Mathf.Clamp(D * swing * 0.35f, 0.5f, 4f);
            }
            else
            {
                // a restart keeps the speed it had: dP/dt at the start = 3 (b - a) / length
                b = a + _da * (_v0 * _segLen / 3f);
                Vector3 inDir = (d - b).sqrMagnitude > 1e-6f ? (d - b).normalized : _da;
                c = d - inDir * (D * 0.25f);
            }
        }

        Vector3 Bez(float s, out Vector3 tangent)
        {
            Ctrl(out var a, out var b, out var c, out var d);
            float m = 1f - s;
            Vector3 dv = 3f * m * m * (b - a) + 6f * m * s * (c - b) + 3f * s * s * (d - c);
            tangent = dv.sqrMagnitude > 1e-8f ? dv.normalized : _da;
            return m * m * m * a + 3f * m * m * s * b + 3f * m * s * s * c + s * s * s * d;
        }

        void Pose(float t, out Vector3 pos, out Vector3 fwd)
        {
            if (t < ejectTime)
            {
                float x = Mathf.Max(0f, t) / ejectTime;
                pos = _p0 + _d0 * (ejectSpeed * ejectTime * (x - 0.5f * x * x));
                fwd = _d0;
                return;
            }
            float u = Mathf.Clamp01((t - _segT0) / _segLen);
            pos = Bez(S(u), out var tan);
            fwd = tan;
            if (_fresh) fwd = Vector3.Slerp(_d0, tan, Mathf.Clamp01((t - ejectTime) / 0.1f)).normalized;   // turns off the tube axis smoothly
        }

        // ------------------------------------------------------------------------------------------------ clock
        void Update()
        {
            if (_t < 0f) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _t += dt;
            if (!_ended)
            {
                Home();
                if (_t >= ImpactTime) { Pose(ImpactTime, out _pos, out _fwd); Hit(); }
                else { _lastPos = _pos; Pose(_t, out _pos, out _fwd); }
                if (_t >= ejectTime) Record(_pos, _t);
            }
            Evaluate();
            if (_ended && _t - _endT > trailSeconds + 0.1f)
            {
                _t = -1f;
                if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }
                if (_light) _light.enabled = false;
            }
        }

        /// the end point follows the target; a dead target hands the missile to another lock (curve restarts here)
        void Home()
        {
            if (!_owner || !_target) return;
            if (_owner.Track(_target, out var p)) { _end = p + _off; return; }
            var next = _owner.Retarget(_target, _pos);
            _target = next;
            if (!next || !_owner.Track(next, out var np)) return;            // none left: on to the last point
            _off = HuskFX.Dir(_seed, 9) * (LockOn.TargetOf(next).radius * 0.35f);
            if (_t > ejectTime + 0.02f) Restart(np + _off);
            else _end = np + _off;                                             // still ejecting: the launch curve aims elsewhere
        }

        void Restart(Vector3 end)
        {
            Vector3 vel = (_pos - _lastPos) / Mathf.Max(1e-4f, Time.deltaTime);
            float speed = Mathf.Clamp(vel.magnitude, 25f, 260f);
            _a = _pos; _da = _fwd; _v0 = speed;
            _end = end;
            _segT0 = _t;
            _segLen = Mathf.Clamp(Vector3.Distance(_a, end) / speed, 0.18f, 2.2f);
            _fresh = false;
        }

        void Record(Vector3 p, float t)
        {
            _hp[_hh] = p; _ht[_hh] = t;
            _hh = (_hh + 1) % Hist;
            if (_hn < Hist) _hn++;
        }

        void Hit()
        {
            _ended = true; _endT = _t;
            if (_model) _model.gameObject.SetActive(false);
            Component t = _target && _owner && _owner.Track(_target, out _) ? _target : null;
            HuskFX.PlayHit(HitKind.Explosion, _pos, -_fwd, t, blastScale, null);
            if (_owner) _owner.Impact(_target, _pos, _fwd);
        }

        // ------------------------------------------------------------------------------------------------ look
        static Color Burn(float k)
        {
            k = Mathf.Clamp01(k);
            if (k < 0.4f) return Color.Lerp(new Color(0.1f, 0.025f, 0f), new Color(1f, 0.3f, 0.05f), k / 0.4f);
            if (k < 0.72f) return Color.Lerp(new Color(1f, 0.3f, 0.05f), new Color(1f, 0.66f, 0.24f), (k - 0.4f) / 0.32f);
            return Color.Lerp(new Color(1f, 0.66f, 0.24f), new Color(1f, 0.96f, 0.9f), (k - 0.72f) / 0.28f);
        }

        void Evaluate()
        {
            float t = Mathf.Max(0f, _t);
            float roll = (t * 520f + HuskFX.H(_seed, 3) * 360f) * (t < ejectTime ? 0.4f : 1f);
            var rot = Quaternion.LookRotation(_fwd, Mathf.Abs(_fwd.y) > 0.98f ? Vector3.forward : Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);
            if (!_ended) transform.SetPositionAndRotation(_pos, rot);
            if (_mats == null) { if (_light) _light.enabled = false; return; }
            if (_fx == null || !_fx.Valid) _fx = new HuskFXCanvas(transform, _mats);
            _fx.Begin();
            float lumen = 0f, flash = 0f;
            var cam = HuskFX.ViewCam;

            // the pod's cold-gas pop
            float tp = Mathf.Exp(-t / 0.05f);
            if (tp > 0.01f)
            {
                _fx.glow.Sprite(_p0, 0.9f, new Color(0.7f, 0.75f, 0.8f) * (0.35f * tp));
                _fx.dot.Sprite(_p0, 0.35f, IgniteCol * (1.5f * tp));
            }

            Vector3 nozzle = transform.TransformPoint(nozzleLocal);
            if (!_ended)
            {
                float ig = t - ejectTime;
                if (ig >= 0f)
                {
                    // ignition: blue-white flash + glint (dimmed right at the camera: it glows, it does not white out the
                    // first-person view six times in a row), then the amber plume and its white-hot core
                    flash = Mathf.Exp(-ig / 0.045f);
                    float near = cam ? Mathf.Clamp((Vector3.Distance(cam.transform.position, nozzle) - 0.6f) / 2.5f, 0.3f, 1f) : 1f;
                    Vector3 back = -transform.forward;
                    _fx.dot.Sprite(nozzle, 0.22f + 0.55f * flash * near, Color.Lerp(Burn(0.95f), IgniteCol, flash) * (2.6f + 7f * flash * near));
                    _fx.glow.Sprite(nozzle, 0.7f + 1.8f * flash * near, Color.Lerp(Burn(0.7f), IgniteCol, flash) * (0.35f + 1.1f * flash * near));
                    _fx.glint.Flare(nozzle, 1f + 2.5f * flash * near, 0.07f + 0.14f * flash, IgniteCol * (0.35f + 2.6f * flash * near));
                    float flick = 1f + 0.22f * HuskFX.N(t * 41f, _seed + 2);
                    float grow = HuskFX.Smooth(0f, 0.1f, ig);
                    float len = (0.7f + 0.5f * HuskFX.Smooth(0f, 0.8f, ig)) * flick * grow;
                    _fx.streak.Streak(nozzle + back * len, nozzle, 0.26f * grow, Color.clear, Burn(0.7f) * (1.2f * grow));
                    _fx.streak.Streak(nozzle + back * (len * 0.5f), nozzle, 0.09f * grow, Color.clear, Burn(0.95f) * (3.4f * grow * flick));
                    lumen += 1400f * grow * flick + 6000f * flash * near;
                }
            }
            else
            {
                // the burn-out: a last hot puff where it struck fades with the trail
                float k = Mathf.Exp(-(t - _endT) / 0.08f);
                if (k > 0.01f) _fx.glow.Sprite(_pos, 1.6f, Burn(0.8f) * (0.8f * k));
            }

            // burn trail: the path flown over the last trailSeconds, hot at the missile, cooling and widening behind
            if (_hn > 1)
            {
                int start = (_hh - _hn + Hist) % Hist;
                Vector3 prev = _hp[start];
                float prevAge = (t - _ht[start]) / trailSeconds;
                for (int k = 1; k < _hn; k++)
                {
                    int i = (start + k) % Hist;
                    float age = (t - _ht[i]) / trailSeconds;
                    if (age < 1f && prevAge < 1f)
                    {
                        Color ca = Burn(0.74f - 0.45f * prevAge) * (1.05f * (1f - prevAge) * (1f - prevAge));
                        Color cb = Burn(0.74f - 0.45f * age) * (1.05f * (1f - age) * (1f - age));
                        _fx.streak.Streak(prev, _hp[i], 0.05f + 0.16f * Mathf.Clamp01(age), ca, cb);
                    }
                    prev = _hp[i]; prevAge = age;
                }
                if (!_ended && _t >= ejectTime)
                    _fx.streak.Streak(prev, nozzle, 0.06f, Burn(0.74f - 0.45f * Mathf.Clamp01(prevAge)) * 1.05f, Burn(0.8f) * 1.2f);
            }
            _fx.End();

            if (_light)
            {
                _light.transform.position = nozzle - transform.forward * 0.3f;
                HuskFX.SetLight(_light, _ended ? 0f : lumen, Color.Lerp(LightAmber, LightIgnite, flash));
            }
        }
    }
}
