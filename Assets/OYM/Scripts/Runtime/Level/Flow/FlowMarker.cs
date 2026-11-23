using OYM.Signal;
using UnityEngine;
using UnityEngine.UIElements;

namespace OYM.Flow
{
    /// The waypoint of the level flow (user 11-19: "목적(가야할곳)도 표기해서 길 안잃게"): where the current objective
    /// is - a hollow diamond over the spot with its name and range, and an arrow on the screen edge (with the range) when
    /// it is off screen or behind. Fades out once the pilot is there, goes quieter while a fight is on, hidden during
    /// cinematics. LevelFlow feeds it (target, label, show). Same vector style as the lock / partner marks
    /// (OYM.Signal.MarkOverlay: diamonds, chevrons, arrows - no rings), in a warm white so it reads apart from the
    /// pilots' blue / orange and the HUSK pink.
    [DefaultExecutionOrder(111)]                         // after FlightCamera (100) placed this frame's camera
    public sealed class FlowMarker : MonoBehaviour
    {
        public static readonly Color Ink = new Color(0.97f, 0.94f, 0.82f);

        public bool show;
        public Vector3 target;
        public string label;
        [Tooltip("the mark fades out within this range of the target (m)")] public float hereRange = 45f;

        UIDocument _doc;
        VisualElement _canvas;
        Label _name, _dist;
        bool _on, _edge;
        Vector2 _pos, _dir;
        float _a;
        int _distShown = -1;

        public bool Visible => _on;
        public bool OnEdge => _edge;

        void Awake()
        {
            _doc = SignalUI.Document(transform, "flow waypoint", -0.5f);
            _canvas = SignalUI.Fill(new VisualElement());
            _canvas.generateVisualContent += Draw;
            _doc.rootVisualElement.Add(_canvas);
            _name = SignalUI.Text(15f, Ink, 3, true);
            _dist = SignalUI.Text(13f, Ink, 1);
            foreach (var l in new[] { _name, _dist })
            {
                l.style.position = Position.Absolute;
                l.style.display = DisplayStyle.None;
                l.style.translate = new Translate(Length.Percent(-50), 0);
                _canvas.Add(l);
            }
        }

        void LateUpdate()
        {
            var e = HuskEncounter.Current;
            var fc = FlightCamera.Main;
            var cam = fc ? fc.Cam : null;
            var size = _canvas.layout.size;
            bool ready = show && cam && fc.target && size.x > 1f && size.y > 1f && !float.IsNaN(size.x) && SignalInstall.PlayerView(e);
            float goal = 0f;
            if (ready)
            {
                float d = Vector3.Distance(fc.target.position, target);
                goal = Mathf.Clamp01((d - hereRange) / 25f);
                var near = ZeroGFlight.EnemiesNear;
                if (near != null && near(fc.target.position, 90f)) goal *= 0.45f;          // a fight on: quieter
                _edge = !Project(cam, target, size, out _pos, out _dir);
                int d10 = Mathf.RoundToInt(d / 10f) * 10;
                if (d10 != _distShown) { _distShown = d10; _dist.text = d >= 1000f ? $"{d / 1000f:F1} km" : $"{d10} m"; }
                string want = label ?? "";
                if (_name.text != want) _name.text = want;
            }
            _a = Mathf.MoveTowards(_a, goal, Time.unscaledDeltaTime / 0.25f);
            _on = ready && _a > 0.01f;
            Labels();
            _canvas.MarkDirtyRepaint();
        }

        /// panel position of `world`; off screen (or behind) it is pushed onto the inset screen border, `dir` = the way out
        static bool Project(Camera cam, Vector3 world, Vector2 size, out Vector2 pos, out Vector2 dir)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            bool behind = v.z < 0f;
            if (behind) { v.x = 1f - v.x; v.y = 1f - v.y; }
            bool on = !behind && v.x > 0.04f && v.x < 0.96f && v.y > 0.06f && v.y < 0.9f;
            Vector2 c = new Vector2(v.x - 0.5f, v.y - 0.5f);
            if (!on)
            {
                if (c.sqrMagnitude < 1e-6f) c = new Vector2(0f, -0.5f);
                float s = Mathf.Max(Mathf.Abs(c.x) / 0.44f, Mathf.Abs(c.y) / 0.4f);
                c /= Mathf.Max(s, 1e-4f);
            }
            pos = new Vector2((c.x + 0.5f) * size.x, (0.5f - c.y) * size.y);
            dir = new Vector2(c.x * size.x, -c.y * size.y).normalized;
            return on;
        }

        void Labels()
        {
            bool on = _on;
            _name.style.display = on && !_edge ? DisplayStyle.Flex : DisplayStyle.None;
            _dist.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            var c = new Color(Ink.r, Ink.g, Ink.b, 0.95f * _a);
            _name.style.color = c; _dist.style.color = c;
            if (_edge)
            {
                Vector2 at = _pos - _dir * 34f;                                             // inside, behind the arrow
                _dist.style.left = at.x; _dist.style.top = at.y - 8f;
            }
            else
            {
                _name.style.left = _pos.x; _name.style.top = _pos.y - 34f;
                _dist.style.left = _pos.x; _dist.style.top = _pos.y + 12f;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (!_on) return;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
            var c = new Color(Ink.r, Ink.g, Ink.b, 0.95f * _a);
            if (_edge) { Arrow(p, _pos, _dir, 15f, c); return; }
            // a hollow diamond with a small solid core, a short tick above it (the "go here")
            p.lineWidth = 2.2f;
            p.strokeColor = c;
            float r = 8f;
            p.BeginPath();
            p.MoveTo(new Vector2(_pos.x, _pos.y - r)); p.LineTo(new Vector2(_pos.x + r, _pos.y));
            p.LineTo(new Vector2(_pos.x, _pos.y + r)); p.LineTo(new Vector2(_pos.x - r, _pos.y));
            p.ClosePath();
            p.Stroke();
            p.fillColor = c;
            float k = 2.4f;
            p.BeginPath();
            p.MoveTo(new Vector2(_pos.x, _pos.y - k)); p.LineTo(new Vector2(_pos.x + k, _pos.y));
            p.LineTo(new Vector2(_pos.x, _pos.y + k)); p.LineTo(new Vector2(_pos.x - k, _pos.y));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(new Vector2(_pos.x, _pos.y - r - 4f)); p.LineTo(new Vector2(_pos.x, _pos.y - r - 11f));
            p.Stroke();
        }

        /// a solid arrowhead at `tip` pointing along `dir` (screen-edge mark, like MarkOverlay's)
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
