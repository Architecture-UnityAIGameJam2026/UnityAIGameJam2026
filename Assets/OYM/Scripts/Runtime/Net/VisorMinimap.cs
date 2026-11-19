using System.Collections.Generic;
using System.Reflection;
using OnYourMark.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OYM.Net
{
    /// The visor HUD's round minimap (the SenseLink "Spatial" module, top left), live and heading-up around the pilot
    /// the map belongs to: this pilot ("내 기준") - or, in co-op while the crossed link still holds the map channel
    /// (OnYourMarkHud.returned[1] false), the partner, as the team's design has it ("반환 전에는 파트너 주변").
    /// Single-player always shows this pilot.
    ///   centre   the pilot: an arrow that always points up (= where the view looks), lime when it is this pilot,
    ///            lavender when it is the partner; a faint wedge = the camera's horizontal field of view
    ///   HUSK     coral triangles pointing where each one is heading (asleep: dim). Past `rangeMeters` they sit on the
    ///            rim pointing out. Well above / below the pilot (elevationMark): a small ^ / v caret beside it
    ///   CITADEL  a big hollow triangle while its fight is on (CitadelWeakPoints lights left; host: the Boss beat)
    ///   partner  an arrow in the other pilot's HUD colour, on the rim when far
    ///   N        world +Z on the inside of the rim (the map turns with the view; this keeps one fixed reference)
    /// The garam mock map lines (streets, routes, fake ally / contact, the fixed north caret) are switched off; the disc,
    /// rim, ownership icon and signal bars stay. One graphic (rebuilt every frame), installed by itself when the game
    /// scene loads: under the module in the frame CoopHud makes the visor HUD - before HudSwapGlitch / HudBreak collect
    /// the module's graphics - so their glitches tear it with the rest; their ghost copies freeze on what the live map
    /// showed when they appeared. -noVisorMap = off.
    [DefaultExecutionOrder(120)]                       // after FlightCamera (100) set this frame's view: no lag
    [RequireComponent(typeof(CanvasRenderer))]         // this UGUI's Graphic does not add one (HudGeometry does the same)
    public sealed class VisorMinimap : MaskableGraphic
    {
        public const string ModuleName = "Spatial";

        [Tooltip("world metres from the pilot to the rim")] public float rangeMeters = 90f;
        [Tooltip("circle in module space (1920x1080 HUD px, top-left origin, y down)")]
        public Vector2 circleCenter = new Vector2(120f, 125f);
        public float circleRadius = 116f;
        [Tooltip("markers stay this far inside the rim (px)")] public float rimInset = 9f;
        [Tooltip("a HUSK this far above / below the pilot gets a ^ / v caret (m)")] public float elevationMark = 10f;
        [Tooltip("HUSK triangle: tip-to-base length, half width (px)")] public float blipLength = 10f, blipHalfWidth = 4.4f;
        public float bossScale = 2.2f;
        [Range(0f, 0.5f)] public float coneAlpha = 0.13f;

        /// names (or name starts) of the garam mock map content under the module that the live map replaces
        static readonly string[] MockNames =
            { "Street ", "District ", "Route ", "Ally", "Destination", "Contact", "North arrow" };

        const int MaxBlips = 64;
        const byte KHusk = 0, KBoss = 1;

        struct Blip { public Vector2 p, dir; public float len, half; public Color32 c; public byte kind; public sbyte elev; }
        struct Pilot { public bool on; public Vector2 p, dir; public float len; public Color32 c; }

        static VisorMinimap s_live;
        /// true = the map on a nested canvas of its own (its per-frame rebuild would not re-batch the whole HUD); off by
        /// default: a plain child, the way the disc and rim HudArrangement adds at runtime are drawn
        public static bool NestedCanvas = false;

        readonly List<Blip> _blips = new List<Blip>(MaxBlips);
        Pilot _center, _other;
        Vector2 _north = Vector2.up;
        float _coneHalf = 44f;
        Color32 _coneColor, _northColor;
        bool _show, _bossOn;
        string _owner = "";

        bool _live;                                    // not serialized: the glitch ghosts' copies stay frozen
        OnYourMarkHud _hud;
        Color32 _primary = new Color32(0xB7, 0xA6, 0xF6, 255), _accent = new Color32(0xD5, 0xEF, 0x86, 255),
                _danger = new Color32(0xF7, 0x8E, 0x72, 255), _ink = new Color32(0x14, 0x13, 0x22, 255);
        Transform _meT;
        ZeroGFlight _me;
        PlayTestDirector _dir;
        float _dirLook;

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_live = null; SceneManager.sceneLoaded -= OnLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        /// Self-installing in the game scene: the visor HUD (CoopHud) is made in the same sceneLoaded, so the map goes
        /// under it right away or, if this handler ran first, in the first Update - before the first LateUpdate, where
        /// HudBreak starts collecting module graphics (HudSwapGlitch collects at the cross, seconds later).
        /// -noVisorMap = off (not -oym*: other modules read those as automation).
        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != GameSession.GameScene) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noVisorMap") >= 0) return;
            if (TryInstall()) return;
            var go = new GameObject("Visor minimap (installer)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<Installer>();
        }

        static bool TryInstall()
        {
            if (s_live) return true;
            var hud = FindAnyObjectByType<OnYourMarkHud>();
            if (!hud) return false;
            return Install(hud);
        }

        sealed class Installer : MonoBehaviour
        {
            int _frames;
            void Update() { if (TryInstall() || ++_frames > 300) Destroy(gameObject); }
        }

        /// The live map under `hud`'s Spatial module.
        public static VisorMinimap Install(OnYourMarkHud hud)
        {
            if (!hud) return null;
            var module = hud.transform.Find(ModuleName) as RectTransform;
            if (!module) { Debug.LogWarning("[OYM-MAP] the visor HUD has no Spatial module: no live minimap"); return null; }
            int off = 0;
            foreach (Transform ch in module)
                if (IsMock(ch.name) && ch.gameObject.activeSelf) { ch.gameObject.SetActive(false); off++; }

            Vector2 size = module.rect.size;
            var rt = OnYourMarkHud.Rect("Live map (OYM)", module, 0f, 0f, size.x, size.y);
            rt.gameObject.layer = module.gameObject.layer;
            rt.SetAsLastSibling();
            if (NestedCanvas) rt.gameObject.AddComponent<Canvas>();   // its per-frame mesh out of the HUD canvas batch
            var map = rt.gameObject.AddComponent<VisorMinimap>();
            map._hud = hud;
            map._live = true;
            map.raycastTarget = false;
            map.material = hud.hologramMaterial;
            map.ReadLayout(hud, size);
            s_live = map;
            Debug.Log($"[OYM-MAP] live minimap on the visor: circle r {map.circleRadius:F0} px = {map.rangeMeters:F0} m, {off} mock map pieces off");
            if (VisorMinimapTest.Asked)
            {
                var t = new GameObject("Minimap test");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(t, hud.gameObject.scene);
                t.AddComponent<VisorMinimapTest>();
            }
            return map;
        }

        static bool IsMock(string n)
        {
            for (int i = 0; i < MockNames.Length; i++) if (n.StartsWith(MockNames[i], System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// palette from the HUD layout; the circle from HudArrangement (the round-map layout) when it is there
        void ReadLayout(OnYourMarkHud hud, Vector2 size)
        {
            var colors = hud.layoutAsset ? JsonUtility.FromJson<HudLayout>(hud.layoutAsset.text)?.colors : null;
            if (colors != null)
            {
                _primary = Parse(colors.primary, _primary); _accent = Parse(colors.accent, _accent);
                _danger = Parse(colors.danger, _danger); _ink = Parse(colors.ink, _ink);
            }
            // HudArrangement (the hud-break layout) by name: the map does not need it to compile
            MonoBehaviour arr = null;
            foreach (var c in hud.GetComponents<MonoBehaviour>()) if (c && c.GetType().Name == "HudArrangement") { arr = c; break; }
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var at = arr ? arr.GetType() : null;
            bool round = at != null && at.GetField("circularMap", F)?.GetValue(arr) is bool b && b;
            if (round)
            {
                if (at.GetField("mapCenter", F)?.GetValue(arr) is Vector2 c) circleCenter = c;
                if (at.GetField("mapRadius", F)?.GetValue(arr) is float r) circleRadius = r;
            }
            else
            {
                circleCenter = size * 0.5f;
                circleRadius = Mathf.Min(size.x, size.y) * 0.5f - 6f;
            }
        }

        static Color32 Parse(string html, Color32 fallback) =>
            !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var c) ? (Color32)c : fallback;

        // ------------------------------------------------------------------------------------------------ test probes
        public bool Live => _live;
        public bool Showing => _show;
        public bool BossShown => _bossOn;
        /// "self" / "partner" (whose surroundings the map shows), "" before there is a pilot
        public string Owner => _owner;
        public int Count => _blips.Count;
        /// map position (module space, y up, relative to the circle centre, px) of blip i; true = a HUSK / CITADEL mark
        public Vector2 Offset(int i) => _blips[i].p - Center;
        public Vector2 Direction(int i) => _blips[i].dir;
        public bool IsBoss(int i) => _blips[i].kind == KBoss;
        public sbyte Elevation(int i) => _blips[i].elev;
        public int LastVerts { get; private set; }
        public string Diagnose()
        {
            var cv = canvas; var cr = canvasRenderer;
            var root = cv ? cv.rootCanvas : null;
            var cam = root ? root.worldCamera : null;
            Vector3 w = rectTransform.TransformPoint(new Vector3(circleCenter.x, -circleCenter.y, 0f));
            Vector3 sp = cam ? cam.WorldToScreenPoint(w) : Vector3.zero;
            string mat = cr && cr.materialCount > 0 && cr.GetMaterial(0) ? cr.GetMaterial(0).name : "-";
            return $"canvas '{(cv ? cv.name : "-")}' nested {(cv ? !cv.isRootCanvas : false)} on {(cv ? cv.isActiveAndEnabled : false)} root on {(root ? root.isActiveAndEnabled : false)} | " +
                   $"renderer cull {(cr ? cr.cull : true)} alpha {(cr ? cr.GetInheritedAlpha() : 0f):F2} mats {(cr ? cr.materialCount : 0)} '{mat}' depth {(cr ? cr.absoluteDepth : -1)} | " +
                   $"verts {LastVerts} | centre world {w.ToString("F3")} screen {sp.ToString("F0")} | tint {color} | enabled {isActiveAndEnabled}";
        }
        public float PixelsPerMeter => (circleRadius - rimInset) / Mathf.Max(5f, rangeMeters);
        public float RimRadius => circleRadius - rimInset;
        Vector2 Center => new Vector2(circleCenter.x, -circleCenter.y);

        // ------------------------------------------------------------------------------------------------ loop
        protected override void OnEnable()
        {
            base.OnEnable();
            if (!_live && s_live && s_live != this) Freeze(s_live);   // a glitch ghost: what the live map shows now
        }

        protected override void OnDestroy()
        {
            if (s_live == this) s_live = null;
            base.OnDestroy();
        }

        void Freeze(VisorMinimap src)
        {
            _blips.Clear(); _blips.AddRange(src._blips);
            _center = src._center; _other = src._other; _north = src._north; _coneHalf = src._coneHalf;
            _coneColor = src._coneColor; _northColor = src._northColor; _show = src._show; _bossOn = src._bossOn;
            circleCenter = src.circleCenter; circleRadius = src.circleRadius; rimInset = src.rimInset;
            SetVerticesDirty();
        }

        void LateUpdate()
        {
            if (!_live) return;
            var root = canvas ? canvas.rootCanvas : null;
            if (root && !root.isActiveAndEnabled) return;  // the visor is off (cutscene letterbox, cutaway)
            Collect();
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------------------------------------ who
        PlayTestDirector Director()
        {
            if (_dir) return _dir;
            var e = HuskEncounter.Current;
            if (e && e.director) return _dir = e.director;
            if (Time.unscaledTime < _dirLook) return null;
            _dirLook = Time.unscaledTime + 2f;
            return _dir = FindAnyObjectByType<PlayTestDirector>();
        }

        ZeroGFlight Me(FlightCamera fc)
        {
            Transform t = fc ? fc.target : null;
            if (t != _meT) { _meT = t; _me = t ? t.GetComponent<ZeroGFlight>() : null; }
            if (_me) return _me;
            var g = CoopGame.Current;
            if (g && g.LocalFlight) return g.LocalFlight;
            var d = Director();
            return d && d.controllers != null && d.controllers.Length > 0 ? d.controllers[0] : null;
        }

        ZeroGFlight Mate(ZeroGFlight me)
        {
            var g = CoopGame.Current;
            if (g && g.RemoteFlight) return g.RemoteFlight;
            var d = Director();
            if (d && d.controllers != null)
                foreach (var c in d.controllers) if (c && c != me && c.gameObject.activeInHierarchy) return c;
            return null;
        }

        static Vector3 Chest(ZeroGFlight f) => f.transform.position + Vector3.up * (f.bodyCenter + 0.15f);

        // ------------------------------------------------------------------------------------------------ collect
        void Collect()
        {
            _blips.Clear();
            _show = false; _bossOn = false; _center.on = false; _other.on = false;
            var fc = FlightCamera.Main;
            var me = Me(fc);
            var mate = Mate(me);
            var g = CoopGame.Current;
            bool coop = g && !g.Lost;
            bool own = !coop || !mate || _hud == null || _hud.returned == null || _hud.returned.Length < 2 || _hud.returned[1];
            var subject = own ? me : mate;
            if (!subject) { _owner = ""; return; }
            _owner = own ? "self" : "partner";
            _show = true;

            Vector3 origin = Chest(subject);
            float yaw;
            Camera cam = fc ? fc.Cam : null;
            if (own && fc) yaw = fc.Yaw;
            else
            {
                Vector3 f = Vector3.ProjectOnPlane(subject.transform.forward, Vector3.up);
                yaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            }
            float rad = yaw * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            float rim = RimRadius, k = PixelsPerMeter;
            _north = new Vector2(-sn, cs);                 // world +Z in map space (map y up = view forward)

            Color32 ownCol = own ? _accent : _primary, otherCol = own ? _primary : _accent;
            float hfov = 90f;
            if (own && cam) hfov = 2f * Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
            _coneHalf = Mathf.Clamp(hfov * 0.5f, 20f, 75f);
            _coneColor = ownCol;
            _northColor = _primary;
            _center = new Pilot { on = true, p = Center, dir = Vector2.up, len = 13f, c = ownCol };

            // the other pilot
            var other = own ? mate : me;
            if (other && other != subject && other.gameObject.activeInHierarchy)
            {
                Vector2 m = Map(Chest(other) - origin, cs, sn) * k;
                Vector3 of = other.transform.forward;
                Vector2 dir = Map(of, cs, sn);
                float a = 1f;
                if (m.sqrMagnitude > rim * rim) { m = m.normalized * rim; dir = m.normalized; a = 0.75f; }
                _other = new Pilot { on = true, p = Center + m, dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up, len = 10f, c = Fade(otherCol, a) };
            }

            var e = HuskEncounter.Current;
            if (!e) return;
            var list = e.Enemies;
            for (int i = 0; i < list.Count && _blips.Count < MaxBlips; i++)
            {
                var en = list[i];
                if (!en || !en.Alive || !en.gameObject.activeInHierarchy) continue;
                Vector3 heading = en.Velocity.sqrMagnitude > 2.25f ? en.Velocity : en.transform.forward;
                Add(KHusk, en.transform.position - origin, heading, cs, sn, k, rim, _danger, en.Dormant ? 0.45f : 1f, 1f);
            }

            // the CITADEL while its fight is on (host: the Boss beat; both: sensor lights left)
            var be = e.boss;
            var weak = CitadelWeakPoints.Current;
            var lv = e.Level;
            _bossOn = be && be.boss && be.boss.gameObject.activeInHierarchy && !be.Playing &&
                      ((weak && weak.Left > 0) || (lv && lv.beat == HuskLevel.Beat.Boss && be.Done));
            if (_bossOn) Add(KBoss, be.boss.position - origin, be.boss.forward, cs, sn, k, rim, _danger, 1f, bossScale);
        }

        /// world vector -> map axes (x right, y = view forward), metres
        static Vector2 Map(Vector3 d, float cs, float sn) => new Vector2(d.x * cs - d.z * sn, d.x * sn + d.z * cs);

        void Add(byte kind, Vector3 d, Vector3 heading, float cs, float sn, float k, float rim, Color32 col, float alpha, float scale)
        {
            Vector2 m = Map(d, cs, sn) * k;
            Vector2 hd = Map(heading, cs, sn);
            bool edge = m.sqrMagnitude > rim * rim;
            if (edge) { m = m.normalized * rim; hd = m; scale *= 0.8f; alpha *= 0.72f; }   // out of range: on the rim, pointing out
            hd = hd.sqrMagnitude > 1e-4f ? hd.normalized : Vector2.up;
            float vy = Mathf.Abs(d.y);
            if (vy > 20f) alpha *= Mathf.Lerp(1f, 0.6f, (vy - 20f) / 60f);
            sbyte elev = edge || kind == KBoss ? (sbyte)0 : d.y > elevationMark ? (sbyte)1 : d.y < -elevationMark ? (sbyte)-1 : (sbyte)0;
            _blips.Add(new Blip { p = Center + m, dir = hd, len = blipLength * scale, half = blipHalfWidth * scale, c = Fade(col, alpha), kind = kind, elev = elev });
        }

        static Color32 Fade(Color32 c, float a) { c.a = (byte)Mathf.Clamp(c.a * a, 0f, 255f); return c; }

        // ------------------------------------------------------------------------------------------------ mesh
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_show) return;
            Color tint = color;
            if (_center.on) Cone(vh, Center, _coneHalf, RimRadius + 4f, Tint(_coneColor, coneAlpha, tint));
            North(vh, Center + _north * (circleRadius - 13f), Tint(_northColor, 0.8f, tint));
            for (int i = 0; i < _blips.Count; i++)
            {
                var b = _blips[i];
                var c = Tint(b.c, 1f, tint);
                if (b.kind == KBoss) Hollow(vh, b.p, b.dir, b.len, b.half, 2f, c, Tint(_ink, 0.55f, tint));
                else Triangle(vh, b.p, b.dir, b.len, b.half, c);
                if (b.elev != 0) Caret(vh, b.p + new Vector2(b.len * 0.55f + 4f, 0f), b.elev > 0, c);
            }
            if (_other.on) Dart(vh, _other.p, _other.dir, _other.len, Tint(_other.c, 1f, tint));
            if (_center.on) Dart(vh, _center.p, _center.dir, _center.len, Tint(_center.c, 1f, tint));
            LastVerts = vh.currentVertCount;
        }

        static Color32 Tint(Color32 c, float a, Color tint) => new Color32(
            (byte)(c.r * tint.r), (byte)(c.g * tint.g), (byte)(c.b * tint.b), (byte)Mathf.Clamp(c.a * a * tint.a, 0f, 255f));

        static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, col, Vector2.zero); vh.AddVert(b, col, Vector2.zero); vh.AddVert(c, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color32 col)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, col, Vector2.zero); vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero); vh.AddVert(b - n, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        /// a HUSK: an isosceles triangle, tip along dir, centred on p
        static void Triangle(VertexHelper vh, Vector2 p, Vector2 dir, float len, float half, Color32 col)
        {
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 tip = p + dir * (len * 0.6f), back = p - dir * (len * 0.4f);
            Tri(vh, tip, back + side * half, back - side * half, col);
        }

        /// the CITADEL: a thick outline round a dark core
        static void Hollow(VertexHelper vh, Vector2 p, Vector2 dir, float len, float half, float w, Color32 col, Color32 core)
        {
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 tip = p + dir * (len * 0.6f), back = p - dir * (len * 0.4f);
            Vector2 l = back + side * half, r = back - side * half;
            Tri(vh, tip, l, r, core);
            Line(vh, tip, l, w, col); Line(vh, l, r, w, col); Line(vh, r, tip, w, col);
            Triangle(vh, p, dir, len * 0.34f, half * 0.34f, col);
        }

        /// a pilot: an arrowhead with a notch
        static void Dart(VertexHelper vh, Vector2 p, Vector2 dir, float len, Color32 col)
        {
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 tip = p + dir * (len * 0.6f), notch = p - dir * (len * 0.12f);
            Vector2 back = p - dir * (len * 0.4f), w = side * (len * 0.42f);
            Tri(vh, tip, back + w, notch, col);
            Tri(vh, tip, notch, back - w, col);
        }

        /// ^ or v beside a HUSK well above / below
        static void Caret(VertexHelper vh, Vector2 p, bool up, Color32 col)
        {
            float s = up ? 1f : -1f;
            Line(vh, p + new Vector2(-2.6f, -1.3f * s), p + new Vector2(0f, 1.5f * s), 1.4f, col);
            Line(vh, p + new Vector2(0f, 1.5f * s), p + new Vector2(2.6f, -1.3f * s), 1.4f, col);
        }

        /// an upright N: three strokes
        static void North(VertexHelper vh, Vector2 p, Color32 col)
        {
            const float w = 2.6f, h = 3.8f, t = 1.3f;
            Vector2 bl = p + new Vector2(-w, -h), tl = p + new Vector2(-w, h), br = p + new Vector2(w, -h), tr = p + new Vector2(w, h);
            Line(vh, bl, tl, t, col); Line(vh, tl, br, t, col); Line(vh, br, tr, t, col);
        }

        /// the view wedge: brightest at the pilot, gone at the rim
        static void Cone(VertexHelper vh, Vector2 c, float halfDeg, float r, Color32 col)
        {
            if (col.a == 0) return;
            const int n = 16;
            var edge = col; edge.a = 0;
            int first = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            for (int i = 0; i <= n; i++)
            {
                float a = (90f - halfDeg + 2f * halfDeg * i / n) * Mathf.Deg2Rad;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, edge, Vector2.zero);
            }
            for (int i = 1; i <= n; i++) vh.AddTriangle(first, first + i, first + i + 1);
        }
    }
}
