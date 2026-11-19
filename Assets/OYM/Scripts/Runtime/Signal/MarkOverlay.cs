using UnityEngine;
using UnityEngine.UIElements;

namespace OYM.Signal
{
    /// Screen marks (UI Toolkit vector drawing, built in code), hidden during cinematics:
    ///   partner pilot  a small chevron over the head + callsign + range in the pilot's colour; off screen an arrow on
    ///                  the screen edge pointing at them
    ///   lock-on        soft: four faint corner ticks round the acquired target; hard: corner brackets that snap in
    ///                  from wide with a small overshoot ("쪼"), a centre pip and the range - hot pink on a CITADEL
    ///                  weak point; dimmed and blinking while the line of sight is blocked; edge arrow when off screen
    /// Corners, chevrons and arrows only: no rings or circles (taste rule).
    [DefaultExecutionOrder(110)]                      // after FlightCamera (100) set this frame's pose: no one-frame lag
    public class MarkOverlay : MonoBehaviour
    {
        static readonly Color LockColor = new Color(1f, 0.88f, 0.62f), WeakColor = new Color(1f, 0.34f, 0.46f), SoftColor = new Color(1f, 1f, 1f, 0.5f);

        UIDocument _doc;
        VisualElement _canvas;
        Label _mateName, _mateDist, _lockDist;

        struct Mark { public bool on, edge; public Vector2 pos, dir; public float size, scale, alpha; public Color color; }
        Mark _mate, _soft, _hard;
        float _blink;

        void Awake()
        {
            _doc = SignalUI.Document(transform, "marks", -1f);
            _canvas = SignalUI.Fill(new VisualElement());
            _canvas.generateVisualContent += Draw;
            _doc.rootVisualElement.Add(_canvas);
            _mateName = SignalUI.Text(15f, Color.white, 3, true);
            _mateDist = SignalUI.Text(13f, Color.white, 1);
            _lockDist = SignalUI.Text(14f, LockColor, 1);
            foreach (var l in new[] { _mateName, _mateDist, _lockDist })
            {
                l.style.position = Position.Absolute;
                l.style.display = DisplayStyle.None;
                _canvas.Add(l);
            }
        }

        void LateUpdate()
        {
            _mate.on = _soft.on = _hard.on = false;
            var e = HuskEncounter.Current;
            var fc = FlightCamera.Main;
            var cam = fc ? fc.Cam : null;
            var size = _canvas.layout.size;
            bool ready = cam && size.x > 1f && size.y > 1f && !float.IsNaN(size.x) && SignalInstall.PlayerView(e);
            if (ready)
            {
                Partner(e, fc, cam, size);
                Locks(cam, size);
            }
            Labels();
            _canvas.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------ projection
        /// panel position of `world`; off screen (or behind) it is pushed onto the inset screen border, `dir` = the way out
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

        void Partner(HuskEncounter e, FlightCamera fc, Camera cam, Vector2 size)
        {
            ZeroGFlight mate = null;
            var d = e ? e.director : FindAnyObjectByType<PlayTestDirector>();
            if (d && d.controllers != null) foreach (var c in d.controllers) if (c && c.transform != fc.target && c.gameObject.activeInHierarchy) mate = c;
            if (!mate || !fc.target) return;
            float dist = Vector3.Distance(mate.transform.position, fc.target.position);
            float a = Mathf.Clamp01((dist - 4f) / 5f);                            // close by: no mark needed
            if (a <= 0f) return;
            Vector3 head = mate.transform.position + Vector3.up * (mate.bodyCenter + 1.25f);
            _mate.on = true;
            _mate.edge = !Project(cam, head, size, out _mate.pos, out _mate.dir);
            _mate.color = SignalInstall.AccentOf(mate);
            _mate.alpha = a;
            _mateName.text = SignalInstall.NameOf(mate);
            _mateDist.text = dist >= 1000f ? $"{dist / 1000f:F1} km" : $"{dist:F0} m";
        }

        void Locks(Camera cam, Vector2 size)
        {
            var lk = LockOn.Current;
            if (!lk) return;
            float now = Time.unscaledTime;
            _blink += Time.unscaledDeltaTime;
            if (lk.HasSoft)
            {
                var t = lk.Soft;
                if (Project(cam, t.point, size, out _soft.pos, out _))
                {
                    _soft.on = true;
                    _soft.size = Mathf.Clamp(ScreenRadius(cam, t.point, t.radius, size, _soft.pos) * 2.4f, 26f, 150f);
                    _soft.color = t.kind == LockOn.Kind.WeakPoint ? new Color(WeakColor.r, WeakColor.g, WeakColor.b, 0.55f) : SoftColor;
                }
            }
            if (lk.Locked)
            {
                var t = lk.Hard;
                _hard.on = true;
                _hard.edge = !Project(cam, t.point, size, out _hard.pos, out _hard.dir);
                _hard.size = Mathf.Clamp(ScreenRadius(cam, t.point, t.radius, size, _hard.pos) * 2.4f + 10f, 34f, 180f);
                float k = lk.LockedFor;
                // "쪼": in from 2.1x fast, a small overshoot under size, then a hard hold
                _hard.scale = k < 0.14f ? Mathf.Lerp(2.1f, 0.9f, 1f - Mathf.Pow(1f - k / 0.14f, 3f))
                            : k < 0.26f ? Mathf.Lerp(0.9f, 1f, 1f - Mathf.Pow(1f - (k - 0.14f) / 0.12f, 2f)) : 1f;
                _hard.alpha = Mathf.Clamp01(k / 0.06f) * (lk.Blocked ? (Mathf.Repeat(_blink, 0.3f) < 0.15f ? 0.35f : 0.6f) : 1f);
                _hard.color = t.kind == LockOn.Kind.WeakPoint ? WeakColor : LockColor;
                float dist = Vector3.Distance(cam.transform.position, t.point);
                _lockDist.text = $"{dist:F0}";
                _lockDist.style.color = new Color(_hard.color.r, _hard.color.g, _hard.color.b, _hard.alpha * 0.9f);
            }
        }

        void Labels()
        {
            bool mate = _mate.on && !_mate.edge;
            _mateName.style.display = mate ? DisplayStyle.Flex : DisplayStyle.None;
            _mateDist.style.display = _mate.on ? DisplayStyle.Flex : DisplayStyle.None;
            if (_mate.on)
            {
                var c = new Color(_mate.color.r, _mate.color.g, _mate.color.b, 0.95f * _mate.alpha);
                _mateName.style.color = c;
                _mateDist.style.color = new Color(1f, 1f, 1f, 0.75f * _mate.alpha);
                if (mate)
                {
                    // callsign above the chevron, range under it (centred by an estimated width)
                    _mateName.style.left = _mate.pos.x - _mateName.text.Length * 5.6f; _mateName.style.top = _mate.pos.y - 34f;
                    _mateDist.style.left = _mate.pos.x - _mateDist.text.Length * 3.6f; _mateDist.style.top = _mate.pos.y + 10f;
                }
                else
                {
                    Vector2 at = _mate.pos - _mate.dir * 30f;
                    _mateDist.style.left = at.x - _mateDist.text.Length * 3.6f; _mateDist.style.top = at.y - 8f;
                }
            }
            bool lockLabel = _hard.on && !_hard.edge;
            _lockDist.style.display = lockLabel ? DisplayStyle.Flex : DisplayStyle.None;
            if (lockLabel)
            {
                float h = _hard.size * _hard.scale * 0.5f;
                _lockDist.style.left = _hard.pos.x + h + 8f; _lockDist.style.top = _hard.pos.y - h - 2f;
            }
        }

        // ------------------------------------------------------------------ painter
        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
            if (_mate.on)
            {
                var c = new Color(_mate.color.r, _mate.color.g, _mate.color.b, 0.95f * _mate.alpha);
                if (_mate.edge) Arrow(p, _mate.pos, _mate.dir, 13f, c);
                else Chevron(p, _mate.pos, 9f, c);
            }
            if (_soft.on) Corners(p, _soft.pos, _soft.size, 0.2f, 1.6f, _soft.color);
            if (_hard.on)
            {
                var c = new Color(_hard.color.r, _hard.color.g, _hard.color.b, _hard.alpha);
                if (_hard.edge) Arrow(p, _hard.pos, _hard.dir, 16f, c);
                else
                {
                    Corners(p, _hard.pos, _hard.size * _hard.scale, 0.26f, 2.6f, c);
                    Diamond(p, _hard.pos, 3.2f, c);
                }
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

        static void Chevron(Painter2D p, Vector2 c, float w, Color col)
        {
            p.lineWidth = 2.4f;
            p.strokeColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - w, c.y - w * 0.55f));
            p.LineTo(new Vector2(c.x, c.y + w * 0.45f));
            p.LineTo(new Vector2(c.x + w, c.y - w * 0.55f));
            p.Stroke();
        }

        static void Diamond(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - r)); p.LineTo(new Vector2(c.x + r, c.y));
            p.LineTo(new Vector2(c.x, c.y + r)); p.LineTo(new Vector2(c.x - r, c.y));
            p.ClosePath();
            p.Fill();
        }

        /// a solid arrowhead at `tip` pointing along `dir` (screen-edge marker)
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
