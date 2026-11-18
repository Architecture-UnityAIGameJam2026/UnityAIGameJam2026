using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OYM
{
    /// Vector icons for the title overlay, drawn with Painter2D so they stay sharp at any panel scale.
    /// Discord: the Discord mark (brand asset, used only on the button that opens the invite).
    /// Friends: two pilot silhouettes (the co-op / friend code button).
    public class TitleIcon : VisualElement
    {
        public enum Kind { Discord, Friends }

        readonly Kind _kind;
        Color _tint = new Color(0.93f, 0.95f, 0.96f, 1f);

        public Color Tint { get => _tint; set { if (_tint == value) return; _tint = value; MarkDirtyRepaint(); } }

        public TitleIcon(Kind kind)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1f;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 1f || r.height < 1f) return;
            var p = ctx.painter2D;
            if (_kind == Kind.Discord) Discord(p, r); else Friends(p, r);
        }

        // ------------------------------------------------------------------------------------------------ Discord mark
        // The official mark's outline (viewBox 127.14 x 96.36): circular SVG arcs are sampled into short lines, the eyes
        // are holes (odd-even fill).
        const float VW = 127.14f, VH = 96.36f;

        void Discord(Painter2D p, Rect r)
        {
            float s = Mathf.Min(r.width / VW, r.height / VH);
            Vector2 o = new Vector2(r.x + (r.width - VW * s) * 0.5f, r.y + (r.height - VH * s) * 0.5f);
            Vector2 cur = Vector2.zero;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
            void M(float x, float y) { cur = new Vector2(x, y); p.MoveTo(P(x, y)); }
            void C(float x1, float y1, float x2, float y2, float x, float y) { p.BezierCurveTo(P(x1, y1), P(x2, y2), P(x, y)); cur = new Vector2(x, y); }
            void A(float rad, bool large, bool sweep, float x, float y)
            {
                foreach (var q in ArcPoints(cur, new Vector2(x, y), rad, large, sweep, 10)) p.LineTo(P(q.x, q.y));
                cur = new Vector2(x, y);
            }

            p.fillColor = _tint;
            p.BeginPath();
            M(107.7f, 8.07f);
            A(105.15f, false, false, 81.47f, 0f);
            A(72.06f, false, false, 78.11f, 6.83f);
            A(97.68f, false, false, 49f, 6.83f);
            A(72.37f, false, false, 45.64f, 0f);
            A(105.89f, false, false, 19.39f, 8.09f);
            C(2.79f, 32.65f, -1.71f, 56.6f, 0.54f, 80.21f);
            A(105.73f, false, false, 32.71f, 96.36f);
            A(77.7f, false, false, 39.6f, 85.25f);
            A(68.42f, false, true, 28.75f, 80.07f);
            C(29.66f, 79.41f, 30.55f, 78.73f, 31.41f, 78.07f);
            A(75.57f, false, false, 95.73f, 78.07f);
            C(96.6f, 78.78f, 97.49f, 79.46f, 98.39f, 80.07f);
            A(68.68f, false, true, 87.52f, 85.26f);
            A(77f, false, false, 94.41f, 96.36f);
            A(105.25f, false, false, 126.6f, 80.22f);
            C(129.24f, 52.84f, 122.09f, 29.11f, 107.7f, 8.07f);
            p.ClosePath();
            M(42.45f, 65.69f);
            C(36.18f, 65.69f, 31f, 60f, 31f, 53f);
            C(31f, 46f, 36f, 40.26f, 42.43f, 40.26f);
            C(48.86f, 40.26f, 54f, 46f, 53.89f, 53f);
            C(53.78f, 60f, 48.84f, 65.69f, 42.45f, 65.69f);
            p.ClosePath();
            M(84.69f, 65.69f);
            C(78.41f, 65.69f, 73.25f, 60f, 73.25f, 53f);
            C(73.25f, 46f, 78.25f, 40.26f, 84.69f, 40.26f);
            C(91.13f, 40.26f, 96.23f, 46f, 96.12f, 53f);
            C(96.01f, 60f, 91.08f, 65.69f, 84.69f, 65.69f);
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        /// Points along an SVG circular arc from a to b (endpoint parameterisation, SVG 1.1 F.6.5), excluding a.
        static IEnumerable<Vector2> ArcPoints(Vector2 a, Vector2 b, float r, bool large, bool sweep, int n)
        {
            Vector2 h = (a - b) * 0.5f;
            float d2 = h.sqrMagnitude;
            if (d2 < 1e-8f) yield break;
            float r2 = r * r;
            if (d2 > r2) { r = Mathf.Sqrt(d2); r2 = d2; }
            float k = Mathf.Sqrt(Mathf.Max(0f, (r2 - d2) / d2));
            if (large == sweep) k = -k;
            Vector2 cp = new Vector2(k * h.y, -k * h.x);
            Vector2 c = cp + (a + b) * 0.5f;
            Vector2 u = (h - cp) / r, v = (-h - cp) / r;
            float t1 = Mathf.Atan2(u.y, u.x);
            float dt = Mathf.Atan2(u.x * v.y - u.y * v.x, Vector2.Dot(u, v));
            if (!sweep && dt > 0f) dt -= 2f * Mathf.PI;
            else if (sweep && dt < 0f) dt += 2f * Mathf.PI;
            for (int i = 1; i <= n; i++)
            {
                float t = t1 + dt * i / n;
                yield return c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
            }
        }

        // ------------------------------------------------------------------------------------------------ friends
        void Friends(Painter2D p, Rect r)
        {
            float s = Mathf.Min(r.width / 30f, r.height / 24f);
            Vector2 o = new Vector2(r.x + (r.width - 30f * s) * 0.5f, r.y + (r.height - 24f * s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
            void Dot(Vector2 c, float rad) { p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill(); }
            void Dome(Vector2 c, float rad) { p.BeginPath(); p.Arc(c, rad, Angle.Degrees(180f), Angle.Degrees(360f)); p.ClosePath(); p.Fill(); }

            var back = _tint; back.a *= 0.5f;
            p.fillColor = back;                                                  // the friend, behind
            Dot(P(20.5f, 6.8f), 4.4f * s);
            Dome(P(20.5f, 22.5f), 8.6f * s);
            p.fillColor = _tint;                                                 // you, in front
            Dot(P(10.5f, 7.6f), 5.2f * s);
            Dome(P(10.5f, 24f), 10f * s);
        }
    }
}
