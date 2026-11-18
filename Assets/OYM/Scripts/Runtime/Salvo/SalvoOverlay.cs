using OYM.Signal;
using UnityEngine;
using UnityEngine.UIElements;

namespace OYM
{
    /// Screen marks of the ORBIT FRAME E salvo (UI Toolkit vector drawing like MarkOverlay, built in code), hidden during
    /// cinematics. Amber = ORBIT FRAME; hot pink on a CITADEL weak point (MarkOverlay's colours).
    ///   lock box      while E is held: four corner ticks round the crosshair at the lock cone's size, snapping in from
    ///                 wide ("쪼"); under it one diamond per lock slot (filled = locked)
    ///   seeking       diamond corners (corners on the points, not square brackets: the T / MMB lock owns those) closing
    ///                 in on the target being locked
    ///   locked        the same diamond, snapped onto the target ("쪼") + its lock number; an edge arrow when off screen
    ///   inbound       after the release the marks blink on their targets until the last missile lands
    ///   cooldown      a short bar under the crosshair with the seconds left (E, 10 s); flashes white and snaps away
    ///                 when ready, shakes red on a denied press
    /// Corners, diamonds, bars and arrows only: no rings or circles (taste rule).
    [DefaultExecutionOrder(112)]                      // after FlightCamera (100) set this frame's pose
    public class SalvoOverlay : MonoBehaviour
    {
        static readonly Color Amber = new Color(1f, 0.72f, 0.3f), Weak = new Color(1f, 0.34f, 0.46f), Deny = new Color(1f, 0.32f, 0.22f);

        UIDocument _doc;
        VisualElement _canvas;
        Label _secs, _key;
        readonly Label[] _nums = new Label[4];

        struct M { public bool on, edge; public Vector2 pos, dir; public float size, scale, alpha; public Color color; public int number; }
        readonly M[] _marks = new M[8];
        int _markCount;
        M _seek;
        bool _box, _bar;
        float _boxHalf, _boxScale, _boxAlpha, _barFill, _barAlpha, _barShake, _barFlash;
        int _slots, _filled;
        Vector2 _center;

        void Awake()
        {
            _doc = SignalUI.Document(transform, "salvo marks", -0.5f);
            _canvas = SignalUI.Fill(new VisualElement());
            _canvas.generateVisualContent += Draw;
            _doc.rootVisualElement.Add(_canvas);
            for (int i = 0; i < _nums.Length; i++) _nums[i] = Abs(SignalUI.Text(13f, Amber, 1, true));
            _secs = Abs(SignalUI.Text(14f, Amber, 1, true));
            _key = Abs(SignalUI.Text(12f, Amber, 2, true));
            _key.text = "E";
        }

        Label Abs(Label l)
        {
            l.style.position = Position.Absolute;
            l.style.display = DisplayStyle.None;
            _canvas.Add(l);
            return l;
        }

        void LateUpdate()
        {
            _markCount = 0; _seek.on = false; _box = false; _bar = false;
            var s = OrbitSalvo.Active;
            var fc = FlightCamera.Main;
            var cam = fc ? fc.Cam : null;
            var size = _canvas.layout.size;
            bool ready = s && cam && size.x > 1f && size.y > 1f && !float.IsNaN(size.x) && SignalInstall.PlayerView(HuskEncounter.Current);
            if (ready) Build(s, cam, size);
            Labels(s);
            _canvas.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------ state -> marks
        void Build(OrbitSalvo s, Camera cam, Vector2 size)
        {
            float now = Time.unscaledTime;
            _center = size * 0.5f;
            if (s.Locking)
            {
                _box = true;
                float k = s.LockingFor;
                float half = Mathf.Tan(s.lockCone * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * size.y * 0.5f;
                _boxHalf = Mathf.Clamp(half, 70f, size.y * 0.42f);
                _boxScale = Mathf.LerpUnclamped(1.35f, 1f, HuskFX.Snap(Mathf.Clamp01(k / 0.18f), 0.06f));
                _slots = Mathf.Clamp(s.maxLocks, 1, 6);
                _filled = s.Locks.Count;
                bool full = _filled >= s.maxLocks;
                _boxAlpha = Mathf.Clamp01(k / 0.05f) * (full ? 0.9f : 0.6f);

                foreach (var m in s.Locks) Add(cam, size, m.point, m.radius, m.kind, m.number, now - m.at, 1f);
                if (s.HasSeek)
                {
                    var t = s.Seek;
                    _seek.on = true;
                    _seek.edge = !Project(cam, t.point, size, out _seek.pos, out _seek.dir);
                    float r = Mathf.Clamp(ScreenRadius(cam, t.point, t.radius, size, _seek.pos) * 2.6f + 12f, 30f, 170f);
                    float p = s.Seek01;
                    _seek.size = r;
                    _seek.scale = Mathf.Lerp(2.3f, 1.05f, 1f - (1f - p) * (1f - p));
                    _seek.alpha = 0.35f + 0.45f * p;
                    _seek.color = t.kind == LockOn.Kind.WeakPoint ? Weak : Amber;
                }
            }
            else
            {
                // the salvo in the air: blinking marks on what it goes for
                bool blink = Mathf.Repeat(now * 8f, 1f) < 0.55f;
                foreach (var m in s.Inbound) Add(cam, size, m.point, m.radius, m.kind, m.number, 9f, blink ? 1f : 0.35f);
            }

            // cooldown bar under the crosshair (and its ready flash / deny shake)
            float left = s.CooldownLeft;
            float sinceReady = now - s.ReadyAt, sinceDeny = now - s.DeniedAt;
            _barShake = sinceDeny < 0.28f ? Mathf.Sin(sinceDeny * 90f) * 5f * (1f - sinceDeny / 0.28f) : 0f;
            _barFlash = sinceDeny < 0.28f ? -1f : 0f;
            if (!s.Locking && left > 0f)
            {
                _bar = true;
                _barFill = s.Cooldown01;
                _barAlpha = 0.85f;
            }
            else if (!s.Locking && sinceReady < 0.7f)
            {
                // ready: full bar flashes white, snaps a little wider and goes
                _bar = true;
                _barFill = 1f;
                _barFlash = Mathf.Clamp01(1f - sinceReady / 0.35f);
                _barAlpha = 1f - HuskFX.Smooth(0.35f, 0.7f, sinceReady);
            }
        }

        void Add(Camera cam, Vector2 size, Vector3 point, float radius, LockOn.Kind kind, int number, float age, float alpha)
        {
            if (_markCount >= _marks.Length) return;
            ref var m = ref _marks[_markCount++];
            m.on = true;
            m.edge = !Project(cam, point, size, out m.pos, out m.dir);
            m.size = Mathf.Clamp(ScreenRadius(cam, point, radius, size, m.pos) * 2.6f + 12f, 30f, 170f);
            // "쪼": in from 1.8x fast, a small overshoot under size, then a hard hold
            m.scale = age < 0.12f ? Mathf.Lerp(1.8f, 0.9f, 1f - Mathf.Pow(1f - age / 0.12f, 3f))
                    : age < 0.24f ? Mathf.Lerp(0.9f, 1f, 1f - Mathf.Pow(1f - (age - 0.12f) / 0.12f, 2f)) : 1f;
            m.alpha = Mathf.Clamp01(age / 0.04f) * alpha;
            m.color = kind == LockOn.Kind.WeakPoint ? Weak : Amber;
            m.number = number;
        }

        void Labels(OrbitSalvo s)
        {
            for (int i = 0; i < _nums.Length; i++)
            {
                bool on = i < _markCount && _marks[i].on && !_marks[i].edge && _marks[i].number > 0 && s && s.Locking;
                var l = _nums[i];
                l.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                var m = _marks[i];
                string txt = m.number.ToString();
                if (l.text != txt) l.text = txt;
                float h = m.size * m.scale * 0.5f;
                l.style.left = m.pos.x - h * 0.55f - 12f; l.style.top = m.pos.y - h * 0.55f - 16f;   // top-left: MarkOverlay's range is top-right
                l.style.color = new Color(m.color.r, m.color.g, m.color.b, 0.95f * m.alpha);
            }
            _secs.style.display = _bar && s && s.CooldownLeft > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _key.style.display = _bar ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_bar || !s) return;
            Color c = BarColor();
            float y = _center.y + BarY - 9f;
            _key.style.left = _center.x - BarW * 0.5f - 18f + _barShake; _key.style.top = y;
            _key.style.color = new Color(c.r, c.g, c.b, 0.8f * _barAlpha);
            if (s.CooldownLeft > 0f)
            {
                string txt = Mathf.CeilToInt(s.CooldownLeft).ToString("00");
                if (_secs.text != txt) _secs.text = txt;
                _secs.style.left = _center.x + BarW * 0.5f + 8f + _barShake; _secs.style.top = y - 1f;
                _secs.style.color = new Color(c.r, c.g, c.b, 0.9f * _barAlpha);
            }
        }

        const float BarW = 64f, BarY = 46f;

        Color BarColor() => _barFlash < 0f ? Deny : Color.Lerp(Amber, Color.white, Mathf.Clamp01(_barFlash));

        // ------------------------------------------------------------------ projection (MarkOverlay's)
        static bool Project(Camera cam, Vector3 world, Vector2 size, out Vector2 pos, out Vector2 dir)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            bool behind = v.z < 0f;
            if (behind) { v.x = 1f - v.x; v.y = 1f - v.y; }
            bool on = !behind && v.x > 0.03f && v.x < 0.97f && v.y > 0.04f && v.y < 0.96f;
            Vector2 c = new Vector2(v.x - 0.5f, v.y - 0.5f);
            if (!on)
            {
                if (c.sqrMagnitude < 1e-6f) c = new Vector2(0f, -0.5f);
                float s = Mathf.Max(Mathf.Abs(c.x) / 0.45f, Mathf.Abs(c.y) / 0.43f);
                c /= Mathf.Max(s, 1e-4f);
            }
            pos = new Vector2((c.x + 0.5f) * size.x, (0.5f - c.y) * size.y);
            dir = new Vector2(c.x * size.x, -c.y * size.y).normalized;
            return on;
        }

        static float ScreenRadius(Camera cam, Vector3 world, float radius, Vector2 size, Vector2 at)
        {
            Vector3 v = cam.WorldToViewportPoint(world + cam.transform.right * radius);
            var p = new Vector2(v.x * size.x, (1f - v.y) * size.y);
            return Vector2.Distance(p, at);
        }

        // ------------------------------------------------------------------ painter
        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
            if (_box)
            {
                var c = new Color(Amber.r, Amber.g, Amber.b, _boxAlpha);
                float h = _boxHalf * _boxScale;
                Corners(p, _center, h * 2f, 0.12f, 1.6f, c);
                float y = _center.y + h + 16f;
                for (int i = 0; i < _slots; i++)
                {
                    var at = new Vector2(_center.x + (i - (_slots - 1) * 0.5f) * 16f, y);
                    if (i < _filled) DiamondFill(p, at, 4.2f, new Color(Amber.r, Amber.g, Amber.b, Mathf.Max(0.85f, _boxAlpha)));
                    else DiamondLine(p, at, 4.2f, 1.2f, new Color(Amber.r, Amber.g, Amber.b, _boxAlpha * 0.8f));
                }
            }
            if (_seek.on)
            {
                var c = new Color(_seek.color.r, _seek.color.g, _seek.color.b, _seek.alpha);
                if (_seek.edge) Arrow(p, _seek.pos, _seek.dir, 11f, c);
                else DiamondCorners(p, _seek.pos, _seek.size * _seek.scale, 0.18f, 1.4f, c);
            }
            for (int i = 0; i < _markCount; i++)
            {
                var m = _marks[i];
                if (!m.on) continue;
                var c = new Color(m.color.r, m.color.g, m.color.b, m.alpha);
                if (m.edge) { Arrow(p, m.pos, m.dir, 15f, c); continue; }
                DiamondCorners(p, m.pos, m.size * m.scale, 0.24f, 2.4f, c);
                DiamondFill(p, m.pos, 2.6f, c);
            }
            if (_bar)
            {
                Color c = BarColor();
                float w = BarW * (1f + 0.12f * Mathf.Clamp01(_barFlash));
                var a = new Vector2(_center.x - w * 0.5f + _barShake, _center.y + BarY);
                Bar(p, a, w, 2.4f, new Color(c.r, c.g, c.b, 0.22f * _barAlpha));
                Bar(p, a, w * Mathf.Clamp01(_barFill), 2.4f, new Color(c.r, c.g, c.b, _barAlpha));
            }
        }

        static void Corners(Painter2D p, Vector2 c, float size, float arm, float width, Color col)
        {
            float h = size * 0.5f, a = size * arm;
            p.lineWidth = width;
            p.strokeColor = col;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                var k = new Vector2(c.x + sx * h, c.y + sy * h);
                p.BeginPath();
                p.MoveTo(new Vector2(k.x, k.y - sy * a));
                p.LineTo(k);
                p.LineTo(new Vector2(k.x - sx * a, k.y));
                p.Stroke();
            }
        }

        /// a diamond's four points only: each vertex with short arms along its two edges
        static void DiamondCorners(Painter2D p, Vector2 c, float size, float arm, float width, Color col)
        {
            float h = size * 0.5f;
            p.lineWidth = width;
            p.strokeColor = col;
            var v = new[] { new Vector2(c.x, c.y - h), new Vector2(c.x + h, c.y), new Vector2(c.x, c.y + h), new Vector2(c.x - h, c.y) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 k = v[i], prev = v[(i + 3) % 4], next = v[(i + 1) % 4];
                p.BeginPath();
                p.MoveTo(Vector2.Lerp(k, prev, arm));
                p.LineTo(k);
                p.LineTo(Vector2.Lerp(k, next, arm));
                p.Stroke();
            }
        }

        static void DiamondFill(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - r)); p.LineTo(new Vector2(c.x + r, c.y));
            p.LineTo(new Vector2(c.x, c.y + r)); p.LineTo(new Vector2(c.x - r, c.y));
            p.ClosePath();
            p.Fill();
        }

        static void DiamondLine(Painter2D p, Vector2 c, float r, float width, Color col)
        {
            p.lineWidth = width;
            p.strokeColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - r)); p.LineTo(new Vector2(c.x + r, c.y));
            p.LineTo(new Vector2(c.x, c.y + r)); p.LineTo(new Vector2(c.x - r, c.y));
            p.ClosePath();
            p.Stroke();
        }

        static void Bar(Painter2D p, Vector2 a, float w, float h, Color col)
        {
            if (w <= 0.5f) return;
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(a); p.LineTo(new Vector2(a.x + w, a.y));
            p.LineTo(new Vector2(a.x + w, a.y + h)); p.LineTo(new Vector2(a.x, a.y + h));
            p.ClosePath();
            p.Fill();
        }

        static void Arrow(Painter2D p, Vector2 tip, Vector2 dir, float len, Color col)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 back = tip - dir * len;
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(back + side * len * 0.55f);
            p.LineTo(tip - dir * len * 0.62f);
            p.LineTo(back - side * len * 0.55f);
            p.ClosePath();
            p.Fill();
        }
    }
}
