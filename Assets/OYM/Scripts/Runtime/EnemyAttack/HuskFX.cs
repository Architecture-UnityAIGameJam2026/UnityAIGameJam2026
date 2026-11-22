using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM
{
    /// Shared helpers for the HUSK attack FX (PRISM lance, WICK detonation, CITADEL homing dart).
    /// Every attack component is a pure function of its attack clock (Evaluate), so batch captures == play mode;
    /// one-shot moments (shot, blast, impact) fire when the clock crosses them.
    public static class HuskFX
    {
        /// Faction heat ramp (linear HDR, multiply by intensity): 0 dark ember -> 0.35 deep red-magenta ->
        /// 0.6 magenta -> 0.82 hot pink -> 1 white. Never cyan / amber (those are the players').
        public static Color Heat(float k)
        {
            k = Mathf.Clamp01(k);
            if (k < 0.35f) return Color.Lerp(new Color(0.10f, 0.0f, 0.02f), new Color(0.85f, 0.03f, 0.20f), k / 0.35f);
            if (k < 0.60f) return Color.Lerp(new Color(0.85f, 0.03f, 0.20f), new Color(1f, 0.10f, 0.40f), (k - 0.35f) / 0.25f);
            if (k < 0.82f) return Color.Lerp(new Color(1f, 0.10f, 0.40f), new Color(1f, 0.46f, 0.72f), (k - 0.60f) / 0.22f);
            return Color.Lerp(new Color(1f, 0.46f, 0.72f), new Color(1f, 0.94f, 0.97f), (k - 0.82f) / 0.18f);
        }

        public static readonly Color Magenta = new Color(1f, 0.10f, 0.40f);
        public static readonly Color LightMagenta = new Color(1f, 0.20f, 0.46f);

        /// Fast arrival, small overshoot, hard hold (the "쪼"): cubic ease-out into 1 + ov at 72 %, smoothstep back, then 1.
        public static float Snap(float x, float ov)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float a = 0.72f;
            if (x < a) { float u = 1f - x / a; return (1f + ov) * (1f - u * u * u); }
            float w = (x - a) / (1f - a);
            return 1f + ov * (1f - w * w * (3f - 2f * w));
        }

        public static float Smooth(float a, float b, float x)
        {
            x = Mathf.Clamp01((x - a) / (b - a));
            return x * x * (3f - 2f * x);
        }

        /// deterministic hash noise in [0, 1)
        public static float H(int i, int k)
        {
            uint x = (uint)(i * 73856093) ^ (uint)(k * 19349663) ^ 0x9e3779b9u;
            x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15; x *= 0x27d4eb2du; x ^= x >> 16;
            return (x & 0xffffffu) / 16777216f;
        }

        /// smooth noise in [-1, 1]
        public static float N(float x, int seed) => Mathf.PerlinNoise(x, seed * 7.31f + 0.5f) * 2f - 1f;

        /// deterministic unit vector
        public static Vector3 Dir(int i, int k)
        {
            float z = H(i, k) * 2f - 1f, a = H(i, k + 1) * Mathf.PI * 2f, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        public static Camera ViewCam => FxBatch.ViewCamera ? FxBatch.ViewCamera : Camera.main;

        /// `p` pulled `d` metres toward the camera: glow sprites on a recessed emitter (lens, core) stay in front of
        /// the emitter's own geometry from any angle (the batches depth-test).
        public static Vector3 TowardCam(Vector3 p, float d)
        {
            var c = ViewCam;
            if (!c) return p;
            Vector3 v = c.transform.position - p;
            float m = v.magnitude;
            return m > 1e-4f ? p + v * (Mathf.Min(d, m * 0.5f) / m) : p;
        }

        /// lumens at the reference exposure (EV 9) -> intensity for this scene's fixed exposure
        public static float LumenScale => Mathf.Pow(2f, (float.IsNaN(ExposureEV) ? HitFXInstance.SceneEV() : ExposureEV) - 9f);

        /// Fixed exposure to scale lights for (captures set it once; NaN = read the scene's global volume).
        public static float ExposureEV = float.NaN;

        /// Comfort clamp for FxBatch sprites (11-19 "저거 뒤에 완전히 하얗게 돼"). OYM/FX/AdditiveHDR is screen-relative
        /// (1 = white on screen in any map) and draws rgb x a, so `Heat(k) * s` shows s^2 bright: a WICK fuse blinking
        /// 1-2 m from the lens (~100x white, 2 m glow) whited the whole view out through bloom. Returns the colour with
        /// the alpha folded in (a = 1) and `size` capped to `maxView` x the view height at the sprite's depth. Brightness
        /// works as an energy budget (bloom spreads intensity x area): at that full size the brightest channel may be
        /// `maxI`, a sprite smaller on screen proportionally more (up to 8x), so far-off pings and glints look as before
        /// and only what covers the view gets tamed. Dimmed toward `nearFloor` as it reaches the lens (full from `near` m).
        public static Color Safe(Vector3 p, ref float size, Color col, float maxView, float maxI, float near = 3f, float nearFloor = 0.4f)
        {
            var c = new Color(col.r * col.a, col.g * col.a, col.b * col.a, 1f);
            var cam = ViewCam;
            float allowed = maxI, dim = 1f;
            if (cam)
            {
                Vector3 cp = cam.transform.position;
                float depth = Vector3.Dot(p - cp, cam.transform.forward);
                float view = cam.orthographic ? 2f * cam.orthographicSize
                                              : 2f * Mathf.Max(0.05f, depth) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                size = Mathf.Min(size, maxView * view);
                float frac = size / Mathf.Max(1e-4f, view);
                allowed = maxI * Mathf.Min(8f, maxView * maxView / Mathf.Max(1e-6f, frac * frac));
                dim = Mathf.Lerp(nearFloor, 1f, Smooth(0.5f, near, Vector3.Distance(p, cp)));
            }
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float k = (m > allowed ? allowed / m : 1f) * dim;
            c.r *= k; c.g *= k; c.b *= k;
            return c;
        }

        /// Sets a point light in lumens at the reference exposure. Same comfort cap as HitFXInstance (11-19 "섬광탄 그대로 /
        /// 계속 완전 흰색으로 가려"): the light may throw at most HitFXInstance.NearCameraLux at the view camera, so a WICK
        /// fuse blinking on your chest (or a dart / rocket passing the lens) glows instead of flooding the view pink-white.
        /// Call it after moving the light.
        public static void SetLight(Light l, float lumen, Color c)
        {
            if (!l) return;
            float k = LumenScale;
            float intensity = lumen * k;
            var cam = HitFXInstance.NearCameraLux > 0f ? ViewCam : null;
            if (cam)
            {
                float d = Mathf.Max(0.5f, Vector3.Distance(cam.transform.position, l.transform.position));
                intensity = Mathf.Min(intensity, HitFXInstance.NearCameraLux * k * 4f * Mathf.PI * d * d);
            }
            l.color = c;
            l.intensity = intensity;
            l.enabled = lumen > 2f;
        }

        /// Plays a hit FX from the library. Play mode: the pooled HitFX.Play path (Update-driven, camera shake,
        /// hit-stop). Edit mode (batch capture): a private instance added to `manual`, which the caller ticks by hand,
        /// plus the same distance-scaled shake / flash on HitScreenFX.Instance. `light` scales the effect's light for
        /// this play (a blast right at the player).
        public static HitFXInstance PlayHit(HitKind kind, Vector3 p, Vector3 n, Component target, float scale, List<HitFXInstance> manual,
                                            float light = 1f)
        {
            if (Application.isPlaying)
            {
                var pi = HitFX.Play(kind, p, n, target, scale);
                if (pi && light < 0.999f) pi.ScaleLight(light);
                return pi;
            }
            var lib = HitFX.Library;
            var e = lib ? lib.Get(kind) : null;
            if (e == null || !e.prefab) return null;
            var inst = Object.Instantiate(e.prefab);
            inst.gameObject.hideFlags = HideFlags.DontSave;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            inst.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n.normalized));
            inst.transform.localScale = Vector3.one * scale;
            foreach (var ps in inst.systems) if (ps) { ps.useAutoRandomSeed = false; ps.randomSeed = 4321u + (uint)ps.name.Length; }
            inst.Play();
            if (light < 0.999f) inst.ScaleLight(light);
            manual?.Add(inst);
            if (target)
            {
                var hf = target.GetComponentInParent<HitFlash>();
                if (hf) hf.AddHit(p, e.overlayColor, e.overlaySize * scale, e.overlayFlash);
            }
            var scr = HitScreenFX.Instance;
            if (scr)
            {
                float d = Vector3.Distance(scr.transform.position, p);
                float kd = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, e.shakeRange));
                scr.AddTrauma(e.shake * scale * kd);
                scr.AddFlash(e.screenFlash * kd);
            }
            return inst;
        }

        /// The hit reaction of a library entry without its particle prefab: the target's HitFlash overlay and the
        /// distance-scaled camera kick (shake / hit-stop / white-out). For hits whose look is drawn by the attack itself.
        public static void HitReaction(HitKind kind, Vector3 p, Component target, float scale, float flashMul = 1f)
        {
            var lib = HitFX.Library;
            var e = lib ? lib.Get(kind) : null;
            if (e == null) return;
            if (target)
            {
                var hf = target.GetComponentInParent<HitFlash>();
                if (hf) hf.AddHit(p, e.overlayColor * flashMul, e.overlaySize * scale, e.overlayFlash * flashMul);
            }
            if (Application.isPlaying) { HitScreenFX.Impulse(p, e.shake * scale, e.shakeRange, e.hitStop, e.screenFlash * flashMul); return; }
            var scr = HitScreenFX.Instance;
            if (!scr) return;
            float d = Vector3.Distance(scr.transform.position, p);
            float kd = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, e.shakeRange));
            scr.AddTrauma(e.shake * scale * kd);
            scr.AddFlash(e.screenFlash * kd * flashMul);
        }

        static System.Reflection.MethodInfo s_shatter;
        static bool s_shatterLooked;

        /// The destruction FX (OYM.EnemyShatter, another session's module): flying pieces, glowing cut faces, embers,
        /// optionally its own explosion. Looked up by reflection so this compiles and runs without it; false = not
        /// available / no baked pieces for these meshes (caller falls back to a plain explosion).
        public static bool Shatter(GameObject root, Vector3 dir, float power, bool withExplosion)
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
            if (s_shatter == null || !root) return false;
            try { return s_shatter.Invoke(null, new object[] { root, dir, power, withExplosion }) is bool b && b; }
            catch (System.Exception e) { Debug.LogWarning("[HuskFX] EnemyShatter.Detonate failed: " + e.Message); return false; }
        }

        /// Edit-mode ticking of the instances PlayHit added to `manual`; finished ones are destroyed.
        public static void TickManual(List<HitFXInstance> manual, float dt)
        {
            for (int i = manual.Count - 1; i >= 0; i--)
            {
                var m = manual[i];
                if (!m) { manual.RemoveAt(i); continue; }
                m.Tick(dt);
                if (!m.Playing) { Object.DestroyImmediate(m.gameObject); manual.RemoveAt(i); }
            }
        }

        public static void ClearManual(List<HitFXInstance> manual)
        {
            foreach (var m in manual) if (m) Object.DestroyImmediate(m.gameObject);
            manual.Clear();
        }

        /// Scales the HDRP Lit emission of every renderer under `root` (material slot 0 only, so a HitFlash overlay
        /// appended as an extra slot is left alone). `baseEmission` is read once from the shared material.
        public sealed class Emission
        {
            static readonly int EmissiveId = OYM.Web.Emissive.Id;
            readonly Renderer[] _r;
            readonly Color[] _base;
            readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
            float _last = -1f;

            public Emission(Transform root)
            {
                var list = new List<Renderer>();
                if (root) foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer) list.Add(r);
                _r = list.ToArray();
                _base = new Color[_r.Length];
                for (int i = 0; i < _r.Length; i++)
                {
                    var m = _r[i].sharedMaterial;
                    _base[i] = m && m.HasProperty(EmissiveId) ? m.GetColor(EmissiveId) : Color.black;
                }
            }

            public void Set(float mult)
            {
                if (Mathf.Abs(mult - _last) < 1e-4f) return;
                _last = mult;
                for (int i = 0; i < _r.Length; i++)
                {
                    if (!_r[i]) continue;
                    if (Mathf.Abs(mult - 1f) < 1e-4f) { _r[i].SetPropertyBlock(null, 0); continue; }
                    _mpb.Clear();
                    // SetVector, not SetColor: a property block converts every colour sRGB -> linear, but the Lit
                    // [HDR] _EmissiveColor is stored linear already (SetColor here raised it to ^2.2 = white-out)
                    _mpb.SetVector(EmissiveId, (Vector4)(_base[i] * mult));
                    _r[i].SetPropertyBlock(_mpb, 0);
                }
            }
        }
    }

    /// Dynamic mesh of camera-facing additive quads in world space (float vertex colours carry HDR intensity).
    /// One batch per material (OYM/FX/AdditiveHDR). Rebuilt every Evaluate; costs nothing when empty.
    public sealed class FxBatch
    {
        struct V { public Vector3 p; public Color c; public Vector2 uv; }
        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        const MeshUpdateFlags Fast = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        /// Camera the billboards face (batch captures set it; play mode falls back to Camera.main).
        public static Camera ViewCamera;

        readonly List<V> _v = new List<V>(512);
        readonly List<int> _i = new List<int>(1024);
        readonly Mesh _m;
        readonly GameObject _go;
        readonly MeshRenderer _mr;
        Vector3 _cp, _cr = Vector3.right, _cu = Vector3.up;
        bool _uploadedEmpty;

        public FxBatch(string name, Transform owner, Material mat)
        {
            _go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            _go.transform.SetParent(owner, false);
            _m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            _m.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _m;
            _mr = _go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = mat;
            _mr.shadowCastingMode = ShadowCastingMode.Off; _mr.receiveShadows = false;
            _mr.lightProbeUsage = LightProbeUsage.Off; _mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        public bool Valid => _go && _m;

        public void Destroy()
        {
            if (_m) { if (Application.isPlaying) Object.Destroy(_m); else Object.DestroyImmediate(_m); }
            if (_go) { if (Application.isPlaying) Object.Destroy(_go); else Object.DestroyImmediate(_go); }
        }

        public void Begin()
        {
            _v.Clear(); _i.Clear();
            var cam = ViewCamera ? ViewCamera : Camera.main;
            if (cam)
            {
                var t = cam.transform;
                _cp = t.position; _cr = t.right; _cu = t.up;
            }
        }

        public Vector3 CamPos => _cp;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb)
        {
            int n = _v.Count;
            _v.Add(new V { p = a, c = ca, uv = new Vector2(0f, 0f) });
            _v.Add(new V { p = b, c = cb, uv = new Vector2(1f, 0f) });
            _v.Add(new V { p = c, c = cb, uv = new Vector2(1f, 1f) });
            _v.Add(new V { p = d, c = ca, uv = new Vector2(0f, 1f) });
            _i.Add(n); _i.Add(n + 1); _i.Add(n + 2); _i.Add(n); _i.Add(n + 2); _i.Add(n + 3);
        }

        static bool Dark(Color c) => c.r + c.g + c.b < 1e-4f || c.a <= 0f;

        /// camera-facing square sprite, optional roll (degrees)
        public void Sprite(Vector3 c, float size, Color col, float rollDeg = 0f)
        {
            if (Dark(col) || size <= 0f) return;
            Vector3 r = _cr, u = _cu;
            if (rollDeg != 0f)
            {
                float a = rollDeg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                Vector3 r2 = r * cs + u * sn, u2 = u * cs - r * sn;
                r = r2; u = u2;
            }
            r *= size * 0.5f; u *= size * 0.5f;
            Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, col);
        }

        /// camera-aligned horizontal flare w x h (texture's U along the width)
        public void Flare(Vector3 c, float w, float h, Color col)
        {
            if (Dark(col)) return;
            Vector3 r = _cr * (w * 0.5f), u = _cu * (h * 0.5f);
            Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, col);
        }

        /// strip from a (tail) to b (head), turned to face the camera; U runs along the strip
        public void Streak(Vector3 a, Vector3 b, float width, Color tail, Color head)
        {
            if (Dark(tail) && Dark(head)) return;
            Vector3 ax = b - a;
            Vector3 side = Vector3.Cross(ax, (a + b) * 0.5f - _cp);
            if (side.sqrMagnitude < 1e-10f) side = _cu;
            side = side.normalized * (width * 0.5f);
            Quad(a - side, b - side, b + side, a + side, tail, head);
        }

        public void End()
        {
            if (!_go) return;
            // vertices are world space: keep the batch object at the world origin whatever its parent does
            var t = _go.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var ps = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / Mathf.Max(1e-4f, ps.x), 1f / Mathf.Max(1e-4f, ps.y), 1f / Mathf.Max(1e-4f, ps.z));
            if (_v.Count == 0)
            {
                _mr.enabled = false;
                if (_uploadedEmpty) return;
                _uploadedEmpty = true;
            }
            else { _mr.enabled = true; _uploadedEmpty = false; }
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

    /// The set of additive materials an attack draws with (built by OYM/FX/Enemy Attack: Build).
    [System.Serializable]
    public class HuskFXMaterials
    {
        [Tooltip("soft round glow (fx_glow)")] public Material glow;
        [Tooltip("hard hot dot (T_FX_Dot)")] public Material dot;
        [Tooltip("beam strip, U along the length (fx_beam)")] public Material streak;
        [Tooltip("thin anamorphic glint (T_FX_Glint)")] public Material glint;
        [Tooltip("crackling arc (T_FX_Arc)")] public Material arc;
    }

    /// The batches for one HuskFXMaterials set, owned by an attack component.
    public sealed class HuskFXCanvas
    {
        public FxBatch glow, dot, streak, glint, arc;

        public HuskFXCanvas(Transform owner, HuskFXMaterials m)
        {
            glow = new FxBatch("[fx glow]", owner, m.glow);
            dot = new FxBatch("[fx dot]", owner, m.dot);
            streak = new FxBatch("[fx streak]", owner, m.streak);
            glint = new FxBatch("[fx glint]", owner, m.glint);
            arc = new FxBatch("[fx arc]", owner, m.arc);
        }

        public bool Valid => glow != null && glow.Valid;

        public void Begin() { glow.Begin(); dot.Begin(); streak.Begin(); glint.Begin(); arc.Begin(); }
        public void End() { glow.End(); dot.End(); streak.End(); glint.End(); arc.End(); }
        public void Destroy() { glow.Destroy(); dot.Destroy(); streak.Destroy(); glint.Destroy(); arc.Destroy(); }
    }
}
