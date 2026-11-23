using System.Collections.Generic;
using OnYourMark.UI;
using OYM.Net;
using UnityEngine;
using UnityEngine.UI;

namespace OYM.Shell
{
    /// The visor HUD (the SenseLink HUD CoopHud runs) in the two pilots' colours (user 11-19: "레이아웃 색 파랑색
    /// 주황색, 서로 다른 색을 가져야 함 글리치 였을 때"): KESTREL blue, ORBIT FRAME orange. Each module - vitals,
    /// minimap, reserves, skills - is painted in the colour of the pilot whose data it shows: while the link is crossed
    /// (the glitch) the modules with the partner's numbers wear the partner's colour, and what a relay gave back wears
    /// your own. It repaints the layout's lavender / lime roles (primary, dim, accent, muted) after the HUD drew its frame;
    /// white text, the danger red, the dark panels and the live map image keep their colours. The swap glitch copies a
    /// module's colours into its ghost when a swap starts, so a swap tears from one pilot's colour to the other's.
    [DefaultExecutionOrder(500)]                          // after the HUD, its skill panel and CoopHud painted this frame
    public sealed class HudPilotColors : MonoBehaviour
    {
        public static readonly Color Kestrel = new Color(0.45f, 0.85f, 1f), Orbit = new Color(1f, 0.68f, 0.25f);
        static readonly string[] Modules = { "Vitals", "Spatial", "Reserves", HudSkillPanel.ModuleName };

        enum Role { None, Primary, Dim, Accent, Muted }

        static readonly (Color c, Role r)[] Palette =
        {
            (Hex(0xB7A6F6), Role.Primary), (Hex(0x716A91), Role.Dim), (Hex(0xD5EF86), Role.Accent), (Hex(0xB7B0C8), Role.Muted),
            (Hex(0xF3EEDD), Role.None), (Hex(0xF78E72), Role.None), (Hex(0x141322), Role.None), (Hex(0x252239), Role.None),
        };

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        OnYourMarkHud _hud;
        HudSkillPanel _skills;
        readonly Transform[] _roots = new Transform[4];
        readonly List<Graphic>[] _graphics = { new List<Graphic>(), new List<Graphic>(), new List<Graphic>(), new List<Graphic>() };
        readonly Dictionary<Graphic, (Role role, Color applied)> _state = new Dictionary<Graphic, (Role, Color)>();
        float _lookT, _cacheT;
        bool _logged;

        /// the pilot's HUD colour (KESTREL blue, ORBIT FRAME orange)
        public static Color ColorOf(ZeroGFlight f) => f is KestrelController ? Kestrel : Orbit;

        /// the colour module m (0 vitals, 1 minimap, 2 reserves, 3 skills) wears now
        public Color ModuleColor(int m)
        {
            var me = Me();
            bool own = Own(m);
            bool kestrel = me is KestrelController;
            return own == kestrel ? Kestrel : Orbit;
        }

        bool Own(int m) => m < 3 ? (_hud && _hud.returned != null && m < _hud.returned.Length && _hud.returned[m]) : (_skills && _skills.Own);

        static ZeroGFlight Me()
        {
            var fc = FlightCamera.Main;
            var f = fc && fc.target ? fc.target.GetComponent<ZeroGFlight>() : null;
            if (f) return f;
            var g = CoopGame.Current;
            return g ? g.LocalFlight : null;
        }

        void Find()
        {
            _hud = FindAnyObjectByType<OnYourMarkHud>();
            if (!_hud) return;
            _skills = _hud.GetComponent<HudSkillPanel>();
            Cache();
        }

        void Cache()
        {
            for (int m = 0; m < Modules.Length; m++)
            {
                _roots[m] = _hud.transform.Find(Modules[m]);
                _graphics[m].Clear();
                if (_roots[m]) _roots[m].GetComponentsInChildren(true, _graphics[m]);
            }
            _cacheT = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (!_hud)
            {
                if (Time.unscaledTime < _lookT) return;
                _lookT = Time.unscaledTime + 0.5f;
                Find();
                if (!_hud) return;
            }
            if (Time.unscaledTime - _cacheT > 2f || !_roots[3]) Cache();                // the skill panel comes up a little later
            var me = Me();
            if (!me) return;
            bool kestrel = me is KestrelController;
            for (int m = 0; m < Modules.Length; m++)
            {
                if (!_roots[m]) continue;
                Color pilot = Own(m) == kestrel ? Kestrel : Orbit;
                var list = _graphics[m];
                for (int i = 0; i < list.Count; i++) Paint(list[i], pilot);
            }
            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[OYM-HUD] pilot colours on the visor: this pilot {(kestrel ? "KESTREL blue" : "ORBIT FRAME orange")}, the partner's data in {(kestrel ? "orange" : "blue")}");
            }
        }

        void Paint(Graphic g, Color pilot)
        {
            if (!g) return;
            Color cur = g.color;
            Role role;
            if (_state.TryGetValue(g, out var st) && Same(cur, st.applied)) role = st.role;     // still our colour from last frame
            else role = Classify(cur);                                                     // the HUD set a fresh one
            if (role == Role.None) { _state.Remove(g); return; }
            Color target = Tone(role, pilot);
            target.a = cur.a;
            if (!Same(cur, target)) g.color = target;
            _state[g] = (role, target);
        }

        static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        static Role Classify(Color c)
        {
            Role best = Role.None; float bd = 0.07f * 0.07f;
            foreach (var (pc, r) in Palette)
            {
                float d = (pc.r - c.r) * (pc.r - c.r) + (pc.g - c.g) * (pc.g - c.g) + (pc.b - c.b) * (pc.b - c.b);
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        /// the layout's roles in a pilot's hue
        static Color Tone(Role r, Color p)
        {
            switch (r)
            {
                case Role.Dim: return Color.Lerp(p, new Color(0.07f, 0.07f, 0.11f), 0.55f);
                case Role.Accent: return Color.Lerp(p, Color.white, 0.38f);
                case Role.Muted: return Color.Lerp(p, new Color(0.74f, 0.74f, 0.78f), 0.5f);
                default: return p;
            }
        }
    }
}
