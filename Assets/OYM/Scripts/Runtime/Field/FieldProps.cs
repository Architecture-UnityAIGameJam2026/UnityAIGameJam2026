using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.Field
{
    /// Colours of the field kit: the visor's own two (lavender = the partner's data / still crossed, lime = this pilot's
    /// own data / given back) and the heal packs' green beacon. Linear HDR multipliers for the additive FX batches.
    static class FieldColors
    {
        public static readonly Color Lavender = new Color(0.72f, 0.65f, 0.96f);
        public static readonly Color Lime = new Color(0.84f, 0.94f, 0.53f);
        public static readonly Color Green = new Color(0.22f, 1f, 0.42f);
        public static readonly Color Jet = new Color(0.62f, 0.8f, 1f);

        /// a short flash of `width` (0..1 of the cycle) starting at `at`, soft edges
        public static float Pulse(float phase, float at, float width)
        {
            float x = (phase - at) / width;
            return x < 0f || x > 1f ? 0f : Mathf.Sin(x * Mathf.PI);
        }

        public static Light MakeLight(Transform parent, string name, Vector3 at, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = range;
            l.shadows = LightShadows.None; l.enabled = false;
            if (hd) hd.affectsVolumetric = false;
            return l;
        }

        /// world bounds of everything a model renders
        public static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }

    /// One link relay on a stepping stone: the terminal box (debris_junction_box) with a beacon mast beside it
    /// (debris_beacon_post), the lamp on the mast drawn as an HDR glow + a small point light. No markers round it: the
    /// lamp is the read.
    ///   idle         a slow double blink in lavender (the link is still crossed)
    ///   calibrating  a pilot's chest within reach: the blink quickens, a crackling arc runs from the lamp to that chest,
    ///                lavender turning lime with the progress
    ///   done         a short lime swell, then a steady lime lamp that breathes (this part of the HUD is back)
    public sealed class LinkRelay : MonoBehaviour
    {
        public int index;
        public RelayStep step;
        /// the lamp on top of the mast (calibration reach is measured from here)
        public Vector3 lamp;
        public bool Done { get; private set; }
        /// 0..1, leaks away when nobody is in reach
        public float Progress;
        /// the pilot calibrating it this frame (this PC's or the partner's puppet), null = nobody
        public Transform Calibrator;
        public Vector3 CalibratorChest;

        Light _light;
        float _phase, _doneAt = -1f;
        readonly Vector3[] _arc = new Vector3[8];
        int _arcSeed = -1;

        public static LinkRelay Build(FieldKitConfig cfg, Transform parent, Vector3 at, Vector3 normal, Vector3 facing, int index, RelayStep step)
        {
            var go = new GameObject($"Link Relay {index}");
            go.transform.SetParent(parent, false);
            Vector3 up = Vector3.Slerp(Vector3.up, normal, 0.6f).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(facing, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd.normalized, up));
            var r = go.AddComponent<LinkRelay>();
            r.index = index; r.step = step;

            float boxHalf = 0.5f * cfg.relayBoxScale, boxTop = at.y + 0.6f;
            if (cfg.relayBox)
            {
                var box = Instantiate(cfg.relayBox, go.transform, false);
                box.name = "terminal (" + cfg.relayBox.name + ")";
                box.transform.localScale = Vector3.one * cfg.relayBoxScale;
                Seat(box, go.transform);
                var b = FieldColors.RenderBounds(box);
                boxHalf = Mathf.Max(b.extents.x, b.extents.z);
                boxTop = b.max.y;
            }
            // the mast beside the box, its foot on the rock there (the top of an uneven stone is not level)
            Vector3 mastFoot = at + go.transform.right * (boxHalf + 0.35f * cfg.relayMastScale);
            if (Physics.Raycast(mastFoot + up * 2f, -up, out var ground, 4f, ~0, QueryTriggerInteraction.Ignore)) mastFoot = ground.point;
            r.lamp = mastFoot + up * (0.8f * cfg.relayMastScale);
            if (cfg.relayMast)
            {
                var mast = Instantiate(cfg.relayMast, go.transform, false);
                mast.name = "mast (" + cfg.relayMast.name + ")";
                mast.transform.localScale = Vector3.one * cfg.relayMastScale;
                mast.transform.position = mastFoot;
                Seat(mast, go.transform);
                var b = FieldColors.RenderBounds(mast);
                r.lamp = new Vector3(b.center.x, b.max.y - 0.07f * cfg.relayMastScale, b.center.z);
            }
            else if (!cfg.relayBox) r.lamp = new Vector3(at.x, boxTop + 0.5f, at.z);
            r._light = FieldColors.MakeLight(go.transform, "relay lamp light", r.lamp + up * 0.2f, 7f);
            return r;
        }

        /// the model's bottom on the root's origin, a few centimetres sunk into the rock
        static void Seat(GameObject model, Transform root)
        {
            var b = FieldColors.RenderBounds(model);
            float below = model.transform.position.y - b.min.y;
            model.transform.position += root.up * (below - 0.04f);
        }

        public void Complete()
        {
            if (Done) return;
            Done = true; Progress = 1f; Calibrator = null;
            _doneAt = Time.time;
        }

        public void Tick(float dt)
        {
            float rate = Done ? 0f : Mathf.Lerp(1f / 1.6f, 5f, Progress);
            _phase = (_phase + dt * rate) % 1f;
        }

        public void Draw(HuskFXCanvas fx, float now, Vector3 cam)
        {
            bool far = (lamp - cam).sqrMagnitude > 120f * 120f;
            Vector3 p = HuskFX.TowardCam(lamp, 0.12f);
            float lumen;
            Color c;
            if (!Done)
            {
                float blink = FieldColors.Pulse(_phase, 0f, 0.08f) + FieldColors.Pulse(_phase, 0.17f, 0.08f);
                if (Progress > 0.01f) blink = Mathf.Max(blink, 0.25f + 0.4f * Progress);
                c = Color.Lerp(FieldColors.Lavender, FieldColors.Lime, Progress);
                if (fx != null)
                {
                    fx.dot.Sprite(p, 0.16f + 0.08f * Progress, c * (0.35f + 2.6f * blink));
                    fx.glow.Sprite(p, 0.9f + 0.8f * Progress, c * (0.05f + 0.28f * blink));
                    if (Calibrator && Progress > 0.01f) Arc(fx, now, c);
                }
                lumen = 90f + 700f * blink + 900f * Progress;
            }
            else
            {
                float swell = Mathf.Exp(-(now - _doneAt) / 0.35f);
                float breathe = 0.85f + 0.15f * Mathf.Sin(now * 2.2f + index * 1.3f);
                c = FieldColors.Lime;
                if (fx != null)
                {
                    fx.dot.Sprite(p, 0.15f + 0.22f * swell, c * (1.5f * breathe + 2.2f * swell));
                    fx.glow.Sprite(p, 0.8f + 2.0f * swell, c * (0.15f * breathe + 0.6f * swell));
                }
                lumen = 320f * breathe + 2200f * swell;
            }
            HuskFX.SetLight(_light, far ? 0f : lumen, c);
        }

        /// a jittery link from the lamp to the calibrating chest (re-rolled ~18 times a second)
        void Arc(HuskFXCanvas fx, float now, Color c)
        {
            Vector3 a = lamp, b = CalibratorChest;
            Vector3 ax = b - a;
            float len = ax.magnitude;
            if (len < 0.3f) return;
            int seed = Mathf.FloorToInt(now * 18f);
            if (seed != _arcSeed)
            {
                _arcSeed = seed;
                Vector3 s1 = Vector3.Cross(ax, Vector3.up); if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.Cross(ax, Vector3.right);
                s1.Normalize();
                Vector3 s2 = Vector3.Cross(ax / len, s1);
                int n = _arc.Length;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    float amp = Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.35f, len * 0.06f);
                    float j1 = HuskFX.H(seed * 31 + i, 1) * 2f - 1f, j2 = HuskFX.H(seed * 31 + i, 2) * 2f - 1f;
                    _arc[i] = a + ax * t + (s1 * j1 + s2 * j2) * amp;
                }
            }
            float flick = 0.65f + 0.35f * Mathf.PerlinNoise(now * 23f, index * 3.1f);
            Color head = c * ((0.5f + 0.7f * Progress) * flick), tail = head * 0.55f;
            for (int i = 0; i < _arc.Length - 1; i++)
                fx.arc.Streak(_arc[i], _arc[i + 1], 0.16f + 0.08f * Progress, i == 0 ? head : tail, tail);
            fx.dot.Sprite(b, 0.12f, c * (0.8f * flick));
        }
    }

    /// One floating heal pack (pk_medkit_closed) over a stepping stone, as on the pickup sheet's "floating" state: it
    /// drifts and turns slowly, a soft green beacon on top, tiny stabiliser jets under the corners. Taken: it snaps away
    /// with a green swell, and comes back after the respawn time with a small overshoot.
    public sealed class HealPack : MonoBehaviour
    {
        enum State { Ready, Taking, Gone, Arriving }

        public int index;
        /// hover centre
        public Vector3 home;
        public bool Available => _state == State.Ready;
        public Vector3 Center => _model ? _model.position : home;
        public float BackIn => _state == State.Gone ? Mathf.Max(0f, _backAt - Time.time) : 0f;

        State _state = State.Ready;
        float _stateT, _backAt, _takenAt = -99f, _arrivedAt = -99f, _scale = 1f, _halfH = 0.35f;
        Transform _model;
        Light _light;
        readonly Vector3[] _jets = new Vector3[4];

        public static HealPack Build(FieldKitConfig cfg, Transform parent, Vector3 hover, int index)
        {
            var go = new GameObject($"Heal Pack {index}");
            go.transform.SetParent(parent, false);
            go.transform.position = hover;
            var p = go.AddComponent<HealPack>();
            p.index = index; p.home = hover; p._scale = cfg.medkitScale;
            GameObject model;
            if (cfg.medkit)
            {
                model = Instantiate(cfg.medkit, go.transform, false);
                model.name = "medkit (" + cfg.medkit.name + ")";
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.SetParent(go.transform, false);
                model.transform.localScale = new Vector3(0.31f, 0.32f, 0.24f);
                model.name = "medkit (placeholder)";
            }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);   // a pickup: nothing to bump into
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            p._model = model.transform;
            // corners of the kit's underside (unscaled model space) for the jets, its half height for the beacon
            var mf = model.GetComponentInChildren<MeshFilter>();
            Bounds lb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.31f, 0.32f, 0.24f));
            if (mf && mf.transform != model.transform)
                lb = new Bounds(model.transform.InverseTransformPoint(mf.transform.TransformPoint(lb.center)), Vector3.Scale(lb.size, mf.transform.localScale));
            p._halfH = lb.extents.y;
            Vector3 e = lb.extents * 0.7f;
            p._jets[0] = lb.center + new Vector3(e.x, -lb.extents.y, e.z);
            p._jets[1] = lb.center + new Vector3(-e.x, -lb.extents.y, e.z);
            p._jets[2] = lb.center + new Vector3(e.x, -lb.extents.y, -e.z);
            p._jets[3] = lb.center + new Vector3(-e.x, -lb.extents.y, -e.z);
            p._light = FieldColors.MakeLight(go.transform, "heal pack light", hover + Vector3.up * 0.5f, 5f);
            p.Pose(0f, 1f);
            return p;
        }

        /// gone until `respawn` seconds from now (also re-arms the timer when the partner's PC took it again)
        public void Take(float respawn)
        {
            if (_state == State.Ready || _state == State.Arriving) { _state = State.Taking; _stateT = 0f; _takenAt = Time.time; }
            else if (_state != State.Taking) _state = State.Gone;
            _backAt = Time.time + Mathf.Max(1f, respawn);
        }

        /// scale 0..1+ of the model (the "snap": a small overshoot, then a hard stop)
        float Visible
        {
            get
            {
                switch (_state)
                {
                    case State.Taking: return _stateT < 0.04f ? Mathf.Lerp(1f, 1.12f, _stateT / 0.04f) : Mathf.Max(0f, 1.12f * (1f - (_stateT - 0.04f) / 0.1f));
                    case State.Gone: return 0f;
                    case State.Arriving: return HuskFX.Snap(Mathf.Clamp01(_stateT / 0.35f), 0.12f);
                    default: return 1f;
                }
            }
        }

        public void Tick(float dt, float now)
        {
            _stateT += dt;
            if (_state == State.Taking && _stateT >= 0.14f) { _state = State.Gone; _stateT = 0f; }
            if (_state == State.Gone && now >= _backAt) { _state = State.Arriving; _stateT = 0f; _arrivedAt = now; }
            if (_state == State.Arriving && _stateT >= 0.35f) { _state = State.Ready; _stateT = 0f; }
            Pose(now, Visible);
        }

        void Pose(float now, float vis)
        {
            if (!_model) return;
            _model.position = home + Vector3.up * (Mathf.Sin(now * 1.9f + index * 1.1f) * 0.09f);
            _model.rotation = Quaternion.Euler(4f * Mathf.Sin(now * 0.9f + index), now * 24f + index * 40f, 5f * Mathf.Sin(now * 0.7f + index * 2f));
            _model.localScale = Vector3.one * (_scale * Mathf.Max(0.0001f, vis));
            bool on = vis > 0.001f;
            if (_model.gameObject.activeSelf != on) _model.gameObject.SetActive(on);
        }

        public void Draw(HuskFXCanvas fx, float now, Vector3 cam)
        {
            bool far = (home - cam).sqrMagnitude > 90f * 90f;
            float vis = Mathf.Clamp01(Visible), lumen = 0f;
            var g = FieldColors.Green;
            if (vis > 0.01f && _model)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(now * 3.1f + index * 1.7f);
                Vector3 top = _model.position + _model.up * (_halfH * _scale + 0.05f);
                if (fx != null)
                {
                    fx.dot.Sprite(HuskFX.TowardCam(top, 0.06f), 0.11f * vis, g * (2.0f * pulse));
                    fx.glow.Sprite(top, 0.85f * vis, g * (0.2f * pulse));
                    for (int j = 0; j < _jets.Length; j++)
                    {
                        Vector3 q = _model.TransformPoint(_jets[j]);
                        float f = 0.5f + 0.5f * Mathf.PerlinNoise(now * 17f, j * 3.3f + index);
                        Vector3 tail = q - _model.up * ((0.12f + 0.08f * f) * _scale);
                        fx.streak.Streak(tail, q, 0.045f * _scale * vis, FieldColors.Jet * 0f, FieldColors.Jet * (0.35f * f * vis));
                        fx.dot.Sprite(q, 0.03f * _scale * vis, FieldColors.Jet * (0.6f * f));
                    }
                }
                lumen = 260f * pulse * vis;
            }
            // taken: a green swell where it was; back: a softer one
            float burst = Mathf.Exp(-(now - _takenAt) / 0.3f), back = Mathf.Exp(-(now - _arrivedAt) / 0.4f);
            if (fx != null)
            {
                if (burst > 0.02f) fx.glow.Sprite(home, 2.6f - 1.0f * burst, g * (0.7f * burst));
                if (back > 0.02f) fx.glow.Sprite(home, 1.6f, g * (0.3f * back));
            }
            lumen += 1600f * burst + 500f * back;
            HuskFX.SetLight(_light, far ? 0f : lumen, g);
        }
    }
}
