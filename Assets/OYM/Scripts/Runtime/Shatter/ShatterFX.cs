using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM
{
    [AddComponentMenu("")]
    public class ShatterFX : MonoBehaviour
    {
        internal struct Src { public ShatterShards shards; public int source; public MeshRenderer renderer; }

        // ---------------------------------------------------------------- tuning
        const float CrackTime = 0.075f;     // hold + overload before the burst
        const float SwellFrac = 0.035f;     // body swells by this * radius during the crack
        const float BaseSpeed = 6.4f;       // m/s for a 1 m radius body at power 1 (scales with radius^0.3)
        const float PopDrag = 3.2f, DriftDrag = 0.6f, PopTime = 0.28f;
        const float CoolTime = 1.5f;        // heat e-folding time of an average piece (s)
        // 11-19 "적이 터지는거 완전 흰색되는거 그거 하지말고 울렁울렁으로": no white at all on a kill. No whole-surface
        // white-out (_Flash 0), and the glow never gets past orange: colour temperature x HeatCap (0.55 -> about 0.63 on the
        // ramp, orange) at moderate HDR, so the tonemapper can't push it to white. The punch is the refraction wave /
        // heat shimmer of the explosion plus HitScreenFX's wobble.
        const float HeatCap = 0.55f;        // colour temperature at full heat (1 = white-hot)
        const float InteriorGain = 2.2f;    // screen-relative HDR of the inside at full heat (was 7 + a white flash)
        const float EdgeGain = 3f;          // ShatterHeat break-edge intensity (material: 9)
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        const float LightsDieTime = 0.35f;
        const float ShrinkTime = 0.55f;

        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        class Piece
        {
            public GameObject go; public Transform t; public MeshFilter mf; public MeshRenderer mr; public TrailRenderer trail;
            public Vector3 vel, axis, restPos, radial, scale; public Quaternion rot;
            public float spin, radius, share, cool, seed;
            public int extCount; public Vector4[] extEmission;
            public bool grounded, hasTrail;
        }

        class Pop { public float at; public int piece; public bool done; }

        static readonly Stack<Piece> s_free = new Stack<Piece>();
        static readonly Stack<ShatterFX> s_freeFx = new Stack<ShatterFX>();
        static Transform s_root;
        static MaterialPropertyBlock s_mpb;
        static readonly Dictionary<Material, Vector4> s_emission = new Dictionary<Material, Vector4>();
        static readonly List<Material> s_mats = new List<Material>();

        readonly List<Piece> _pieces = new List<Piece>(24);
        readonly List<Pop> _pops = new List<Pop>(2);
        readonly List<HitFXInstance> _spawned = new List<HitFXInstance>(4);
        ShatterLibrary _lib;
        Vector3 _dir, _gravity, _center;
        float _power, _radius, _t, _life, _groundY = float.NegativeInfinity;
        bool _explosion, _burst, _manual, _running;
        int _seed;

        public bool Running => _running;
        public float Time01 => _life > 0f ? _t / _life : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_free.Clear(); s_freeFx.Clear(); s_root = null; s_emission.Clear(); EnemyShatter.Active.Clear(); }

        static Transform Root
        {
            get
            {
                if (s_root) return s_root;
                var go = new GameObject("[Shatter pool]");
                if (Application.isPlaying) DontDestroyOnLoad(go); else go.hideFlags = HideFlags.DontSave;
                s_root = go.transform;
                return s_root;
            }
        }

        internal static ShatterFX Get()
        {
            ShatterFX fx = null;
            while (s_freeFx.Count > 0 && !fx) fx = s_freeFx.Pop();
            if (!fx)
            {
                var go = new GameObject("ShatterFX");
                go.transform.SetParent(Root, false);
                if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                fx = go.AddComponent<ShatterFX>();
            }
            fx.gameObject.SetActive(true);
            return fx;
        }

        static Piece GetPiece()
        {
            Piece p = null;
            while (s_free.Count > 0 && (p == null || !p.go)) p = s_free.Pop();
            if (p == null || !p.go)
            {
                var go = new GameObject("ShatterPiece");
                go.transform.SetParent(Root, false);
                if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                p = new Piece { go = go, t = go.transform, mf = go.AddComponent<MeshFilter>(), mr = go.AddComponent<MeshRenderer>() };
                p.mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                p.mr.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            return p;
        }

        // Vectors, not colours: Material/MaterialPropertyBlock.SetColor gamma-convert in a linear project, which blew
        // HDR emission (thousands of nits) up by ^2.2 and flooded the frame pink / white.
        static Vector4 OriginalEmission(Material m)
        {
            if (!m) return Vector4.zero;
            if (s_emission.TryGetValue(m, out var c)) return c;
            c = m.HasProperty(EmissiveId) ? m.GetVector(EmissiveId) : Vector4.zero;
            s_emission[m] = c;
            return c;
        }

        // ---------------------------------------------------------------- start
        internal void Begin(List<Src> srcs, ShatterLibrary lib, Vector3 dir, float power, bool explosion, Vector3 gravity, Transform enemy)
        {
            _lib = lib; _dir = dir; _power = power; _explosion = explosion; _gravity = gravity;
            _t = 0f; _burst = false; _running = true; _manual = !Application.isPlaying;
            _seed = Random.Range(1, 1 << 20);
            _pieces.Clear(); _pops.Clear(); _spawned.Clear();
            if (s_mpb == null) s_mpb = new MaterialPropertyBlock();

            // model centre / radius (area-weighted over sources)
            Vector3 c = Vector3.zero; float rad = 0f; int n = 0;
            foreach (var s in srcs)
            {
                var src = s.shards.sources[s.source];
                var m = s.renderer.transform.localToWorldMatrix;
                c += m.MultiplyPoint3x4(src.center);
                rad = Mathf.Max(rad, src.radius * MaxScale(s.renderer.transform));
                n++;
            }
            _center = c / Mathf.Max(1, n);
            _radius = Mathf.Max(0.1f, rad);

            foreach (var s in srcs)
            {
                var src = s.shards.sources[s.source];
                var tr = s.renderer.transform;
                var m = tr.localToWorldMatrix;
                s.renderer.GetSharedMaterials(s_mats);
                int layer = s.renderer.gameObject.layer;
                foreach (var sp in s.shards.pieces)
                {
                    if (sp.source != s.source || !sp.mesh) continue;
                    var p = GetPiece();
                    p.go.layer = layer;
                    p.mf.sharedMesh = sp.mesh;
                    p.extCount = src.materialCount;
                    var mats = new Material[src.materialCount + 2];
                    if (p.extEmission == null || p.extEmission.Length < src.materialCount) p.extEmission = new Vector4[src.materialCount];
                    for (int i = 0; i < src.materialCount; i++)
                    {
                        mats[i] = s_mats.Count == 0 ? null : s_mats[Mathf.Min(i, s_mats.Count - 1)];
                        p.extEmission[i] = OriginalEmission(mats[i]);
                    }
                    mats[src.materialCount] = lib.interiorMaterial;
                    mats[src.materialCount + 1] = lib.heatMaterial;
                    p.mr.sharedMaterials = mats;
                    p.mr.shadowCastingMode = s.renderer.shadowCastingMode;
                    p.mr.enabled = true;

                    p.restPos = m.MultiplyPoint3x4(sp.center);
                    p.rot = tr.rotation;
                    p.scale = tr.lossyScale;
                    p.radius = sp.radius * MaxScale(tr);
                    p.share = Mathf.Max(1e-4f, sp.share);
                    p.radial = p.restPos - _center;
                    p.seed = Hash(_seed + _pieces.Count * 7919);
                    p.cool = CoolTime * Mathf.Lerp(0.7f, 1.45f, Mathf.Clamp01(p.share * 4f)) * (0.85f + 0.3f * p.seed) * Mathf.Clamp(0.75f + 0.25f * _radius, 0.8f, 1.4f);
                    p.vel = Vector3.zero; p.spin = 0f; p.axis = Vector3.up; p.grounded = false; p.hasTrail = false;
                    p.t.SetPositionAndRotation(p.restPos, p.rot);
                    p.t.localScale = p.scale;
                    p.go.SetActive(true);
                    if (p.trail) { p.trail.emitting = false; p.trail.Clear(); p.trail.enabled = false; }
                    _pieces.Add(p);
                }
            }

            float baseLife = gravity.sqrMagnitude > 0.01f ? 4.6f : 3.4f;
            _life = baseLife + 0.4f * Hash(_seed + 3);

            if (gravity.sqrMagnitude > 0.01f)
            {
                // floor under the body (ignores the enemy's own colliders, which are still up this frame)
                var hits = Physics.RaycastAll(_center + Vector3.up * 0.2f, gravity.normalized, 60f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var h in hits)
                    if (h.collider && (!enemy || !h.collider.transform.IsChildOf(enemy)) && h.distance < best) { best = h.distance; _groundY = h.point.y; }
                if (best == float.MaxValue) _groundY = float.NegativeInfinity;
            }
            else _groundY = float.NegativeInfinity;

            if (!EnemyShatter.Active.Contains(this)) EnemyShatter.Active.Add(this);
            Apply(0f);
        }

        static float MaxScale(Transform t) { var s = t.lossyScale; return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))); }
        static float Hash(int n) { n = (n << 13) ^ n; return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f; }
        static Vector3 HashDir(int n) => new Vector3(Hash(n) * 2f - 1f, Hash(n + 17) * 2f - 1f, Hash(n + 31) * 2f - 1f);

        // ---------------------------------------------------------------- burst
        void Burst()
        {
            _burst = true;
            bool grounded = _gravity.sqrMagnitude > 0.01f;
            float avgShare = 1f / Mathf.Max(1, _pieces.Count);
            float speed = BaseSpeed * _power * Mathf.Pow(Mathf.Max(0.35f, _radius), 0.3f);   // big bodies: pieces stay in view, tumbling
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                int h = (int)(p.seed * 100000f);
                Vector3 radial = p.radial.sqrMagnitude > 1e-6f ? p.radial.normalized : HashDir(h).normalized;
                float small = Mathf.Clamp(Mathf.Pow(avgShare / p.share, 0.3f), 0.7f, 1.6f);   // small bits fly faster
                Vector3 d = radial * (0.8f + 0.5f * Hash(h + 1)) + _dir * 0.55f + HashDir(h + 2) * 0.35f;
                if (grounded) d += -_gravity.normalized * 0.45f;
                p.vel = d.normalized * speed * small * (0.65f + 0.7f * Hash(h + 3));
                p.axis = HashDir(h + 4); if (p.axis.sqrMagnitude < 1e-4f) p.axis = Vector3.right; p.axis.Normalize();
                p.spin = (160f + 520f * Hash(h + 5)) * small;   // deg/s
            }

            // the biggest pieces drag ember streaks and may cook off
            var order = new List<int>(_pieces.Count);
            for (int i = 0; i < _pieces.Count; i++) order.Add(i);
            order.Sort((a, b) => _pieces[b].share.CompareTo(_pieces[a].share));
            int trails = Mathf.Min(_pieces.Count >= 6 ? 3 : 1, order.Count);
            for (int k = 0; k < trails; k++) StartTrail(_pieces[order[k]]);

            if (!_explosion) return;
            float scale = ExplosionScale();
            // fireball both ways so the burst reads as a sphere: the front (towards the shooter) carries the light and shake
            Spawn(HitKind.Explosion, _center, Vector3.Slerp(-_dir, Vector3.up, 0.3f), scale);
            Spawn(HitKind.Explosion, _center + _dir * _radius * 0.2f, _dir, scale * 0.55f);
            if (_pieces.Count >= 4)
            {
                int pops = _radius > 1.2f ? 2 : 1;
                for (int k = 0; k < pops && k < order.Count; k++)
                    _pops.Add(new Pop { piece = order[k], at = CrackTime + 0.17f + 0.16f * k + 0.12f * Hash(_seed + 50 + k) });
            }
        }

        /// Edit-mode capture hook: HitFX.Play's camera shake / flash does not run outside play mode.
        public static System.Action<Vector3, HitFXEntry, float, float> CaptureImpulse;   // (at, entry, scale, big)

        /// 0 for small bodies (WICK), 1 from ~2 m radius (PRISM) or heavy kills: drives screen white-out and hit-stop.
        float BigK() => Mathf.Clamp01((_radius - 0.7f) / 1.2f + (_power - 1f) * 0.8f);

        float ExplosionScale() => Mathf.Clamp(_radius / 2.1f, 0.35f, 1.8f) * Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(_power - 0.5f));

        /// The HitFX explosion's camera-facing shock ring reads as a drawn marker on a body breaking up: off here.
        /// Since 11-19 ("울렁울렁") the ring is pure refraction (its material has no glow), so it stays on and kills wobble
        /// the air like every other blast; it is only switched off again if the ring material ever gets a glow.
        static void NoRing(HitFXInstance inst, bool own)
        {
            foreach (var ps in inst.systems)
            {
                if (!ps || ps.name != "ShockRing") continue;
                var ringR = ps.GetComponent<ParticleSystemRenderer>();
                var ringMat = ringR ? ringR.sharedMaterial : null;
                if (ringMat && ringMat.HasProperty("_Glow") && ringMat.GetFloat("_Glow") <= 0f) continue;
                // own (capture) instance: switch it off for good (Tick() simulates stopped systems again);
                // pooled instance: just stop it, the next HitFX.Play() restarts it for other users
                if (own) ps.gameObject.SetActive(false);
                else ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void Spawn(HitKind kind, Vector3 at, Vector3 normal, float scale)
        {
            float big = BigK();
            if (!_manual)
            {
                var lib0 = HitFX.Library;
                var e0 = lib0 ? lib0.Get(kind) : null;
                if (e0 == null || !e0.prefab) return;
                var inst = HitFX.Spawn(e0.prefab);
                inst.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up));
                inst.transform.localScale = Vector3.one * scale;
                inst.Play();
                NoRing(inst, false);
                _spawned.Add(inst);
                // screenFlash is HitScreenFX's wobble now (no white-out): every kill wobbles, big bodies fully
                HitScreenFX.Impulse(at, e0.shake * scale, e0.shakeRange, e0.hitStop * big, e0.screenFlash * Mathf.Lerp(0.5f, 1f, big));
                return;
            }
            // edit mode / batch capture: own (deterministic) instance, ticked by Tick()
            var lib = HitFX.Library;
            var e = lib ? lib.Get(kind) : null;
            if (e == null || !e.prefab) return;
            var i2 = Instantiate(e.prefab);
            i2.gameObject.hideFlags = HideFlags.DontSave;
            i2.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up));
            i2.transform.localScale = Vector3.one * scale;
            uint seed = 977u + (uint)(_seed % 10000) + (uint)_spawned.Count * 131u;
            foreach (var ps in i2.systems) if (ps) { ps.useAutoRandomSeed = false; ps.randomSeed = seed + (uint)ps.name.Length; }
            NoRing(i2, true);
            i2.Play();
            _spawned.Add(i2);
            CaptureImpulse?.Invoke(at, e, scale, big);
        }

        void StartTrail(Piece p)
        {
            if (!_lib.trailMaterial) return;
            if (!p.trail)
            {
                p.trail = p.go.AddComponent<TrailRenderer>();
                p.trail.sharedMaterial = _lib.trailMaterial;
                p.trail.shadowCastingMode = ShadowCastingMode.Off;
                p.trail.receiveShadows = false;
                p.trail.minVertexDistance = 0.05f;
                p.trail.numCapVertices = 2;
                p.trail.alignment = LineAlignment.View;
                p.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.72f, 0.38f), 0f), new GradientColorKey(new Color(1f, 0.28f, 0.05f), 0.35f), new GradientColorKey(new Color(0.35f, 0.04f, 0.01f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
                p.trail.colorGradient = g;
            }
            p.trail.enabled = true;
            p.trail.time = 0.2f;
            p.trail.widthMultiplier = Mathf.Max(0.02f, p.radius * 0.32f);
            p.trail.Clear();
            p.trail.emitting = true;
            p.hasTrail = true;
        }

        // ---------------------------------------------------------------- update
        void Update() { if (_running && !_manual) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (!_running) return;
            _t += dt;
            if (!_burst && _t >= CrackTime) Burst();

            if (_burst) Integrate(dt);

            for (int i = 0; i < _pops.Count; i++)
            {
                var pop = _pops[i];
                if (pop.done || _t < pop.at) continue;
                pop.done = true;
                var p = _pieces[pop.piece];
                Spawn(HitKind.Explosion, p.t.position, p.vel.sqrMagnitude > 1e-4f ? p.vel.normalized : Vector3.up, ExplosionScale() * 0.36f);
                p.vel *= 1.15f; p.spin *= 1.4f;
            }

            if (_manual) for (int i = _spawned.Count - 1; i >= 0; i--) { var s = _spawned[i]; if (s && s.Playing) s.Tick(dt); }

            Apply(_t);
            if (_t >= _life) End();
        }

        void Integrate(float dt)
        {
            float age = _t - CrackTime;
            bool grav = _gravity.sqrMagnitude > 0.01f;
            float drag = age < PopTime ? PopDrag : DriftDrag;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (!p.grounded)
                {
                    p.vel *= Mathf.Exp(-drag * dt);
                    if (grav) p.vel += _gravity * dt;
                }
                Vector3 pos = p.t.position + p.vel * dt;
                float spinNow = p.spin * (grav ? 1f : Mathf.Lerp(0.55f, 1f, Mathf.Exp(-age * 0.8f)));
                var rot = Quaternion.AngleAxis(spinNow * dt, p.axis) * p.t.rotation;

                if (grav && !float.IsNegativeInfinity(_groundY))
                {
                    float rest = _groundY + p.radius * 0.45f;
                    if (pos.y < rest)
                    {
                        pos.y = rest;
                        if (p.vel.y < 0f) p.vel.y = -p.vel.y * 0.28f;
                        p.vel.x *= 0.55f; p.vel.z *= 0.55f;
                        p.spin *= 0.5f;
                        if (p.vel.sqrMagnitude < 0.25f) { p.grounded = true; p.vel = Vector3.zero; p.spin = 0f; }
                    }
                }
                p.t.SetPositionAndRotation(pos, rot);
            }
        }

        void Apply(float t)
        {
            var mpb = s_mpb;
            float age = t - CrackTime;
            float swell = age < 0f ? SwellFrac * (t / CrackTime) * (t / CrackTime) : 0f;
            // own sensor lights: overload surge + stutter during the crack, then dying flicker
            float flick = Mathf.PerlinNoise(_seed * 0.001f, t * 55f);
            float lights = age < 0f ? (flick > 0.5f ? 1f : 0.2f)
                                    : age < LightsDieTime ? Mathf.Pow(1f - age / LightsDieTime, 2f) * (flick > 0.45f ? 1f : 0.1f) : 0f;
            float shrinkStart = _life - ShrinkTime;

            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (age < 0f) p.t.position = p.restPos + p.radial * swell;
                float s = t > shrinkStart ? 1f - Smooth01((t - shrinkStart) / ShrinkTime) : 1f;
                p.t.localScale = p.scale * Mathf.Max(0.001f, s);

                float heat = age < 0f ? 1f : Mathf.Exp(-age / p.cool);
                if (p.hasTrail && heat < 0.62f) { p.trail.emitting = false; p.hasTrail = false; }

                for (int m = 0; m < p.extCount; m++)
                {
                    mpb.Clear();
                    mpb.SetVector(EmissiveId, p.extEmission[m] * lights);
                    p.mr.SetPropertyBlock(mpb, m);
                }
                mpb.Clear();
                mpb.SetVector(EmissiveId, (Vector4)(Blackbody(heat * HeatCap) * (InteriorGain * heat * Mathf.Sqrt(heat))));
                p.mr.SetPropertyBlock(mpb, p.extCount);
                mpb.Clear();
                mpb.SetFloat(HeatId, heat * HeatCap);
                mpb.SetFloat(FlashId, 0f);
                mpb.SetFloat(IntensityId, EdgeGain);
                p.mr.SetPropertyBlock(mpb, p.extCount + 1);
            }
        }

        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// Blackbody-ish colour (unit brightness) for heat 0..1: dark red -> orange -> yellow -> white.
        public static Color Blackbody(float h)
        {
            h = Mathf.Clamp01(h);
            if (h < 0.25f) return Color.Lerp(new Color(0f, 0f, 0f), new Color(0.55f, 0.03f, 0f), h / 0.25f);
            if (h < 0.5f) return Color.Lerp(new Color(0.55f, 0.03f, 0f), new Color(1f, 0.22f, 0.02f), (h - 0.25f) / 0.25f);
            if (h < 0.75f) return Color.Lerp(new Color(1f, 0.22f, 0.02f), new Color(1f, 0.58f, 0.16f), (h - 0.5f) / 0.25f);
            return Color.Lerp(new Color(1f, 0.58f, 0.16f), new Color(1f, 0.93f, 0.8f), (h - 0.75f) / 0.25f);
        }

        // ---------------------------------------------------------------- end
        public void End()
        {
            if (!_running) return;
            _running = false;
            foreach (var p in _pieces)
            {
                if (!p.go) continue;
                if (p.trail) { p.trail.emitting = false; p.trail.Clear(); p.trail.enabled = false; }
                p.hasTrail = false;
                if (s_mpb != null)
                {
                    s_mpb.Clear();
                    int mc = p.extCount + 2;
                    for (int m = 0; m < mc; m++) p.mr.SetPropertyBlock(s_mpb, m);
                }
                p.mf.sharedMesh = null;
                p.go.SetActive(false);
                s_free.Push(p);
            }
            _pieces.Clear();
            if (_manual)
                foreach (var s in _spawned) if (s) { if (Application.isPlaying) Destroy(s.gameObject); else DestroyImmediate(s.gameObject); }
            _spawned.Clear(); _pops.Clear();
            EnemyShatter.Active.Remove(this);
            if (this) { gameObject.SetActive(false); s_freeFx.Push(this); }
        }

        /// Destroys the pooled pieces (edit-mode capture cleanup).
        public static void DestroyPool()
        {
            EnemyShatter.StopAll();
            if (s_root) { if (Application.isPlaying) Destroy(s_root.gameObject); else DestroyImmediate(s_root.gameObject); }
            s_root = null; s_free.Clear(); s_freeFx.Clear();
        }
    }
}
