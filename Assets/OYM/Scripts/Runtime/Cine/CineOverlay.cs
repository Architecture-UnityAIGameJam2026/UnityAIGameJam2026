using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace OYM.Cine
{
    /// Cutscene screen layer (UI Toolkit, built in code): letterbox bars, a black fade, subtitles (speaker callsign in
    /// the pilot's accent colour + the line, typed on) and a thin hold-to-skip bar that only shows while a skip key is
    /// held. No other text on screen. Times are real seconds (GameFeel.RealDt: keeps running through hit-stops and
    /// follows the capture clock in recordings).
    public class CineOverlay : MonoBehaviour
    {
        public PanelSettings panel;
        [Tooltip("letterbox bar height, fraction of the screen height")]
        public float barHeight = 0.105f;
        public float charsPerSecond = 38f;
        public float textSize = 34f, nameSize = 19f;

        UIDocument _doc;
        VisualElement _root, _top, _bottom, _fade, _sub, _accent, _skip, _skipFill;
        Label _name, _text;
        float _bars, _barsT, _barsSpeed = 1f, _fadeA, _fadeT, _fadeSpeed = 1f, _subA, _subT;
        string _full = ""; float _typed; bool _typing;
        static FontDefinition? s_font;

        public bool Typing => _typing;

        public static CineOverlay Create(Transform parent)
        {
            var go = new GameObject("CineOverlay");
            if (parent) go.transform.SetParent(parent, false);
            return go.AddComponent<CineOverlay>();
        }

        void Awake()
        {
            if (!panel) panel = Resources.Load<PanelSettings>("OYM_Cine/OYM_CinePanel");
            if (!panel)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.name = "OYM_CinePanel (runtime)";
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.match = 0.5f;
                panel.sortingOrder = 50;
#if UNITY_EDITOR
                panel.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/OYM/Title/OYM_RuntimeTheme.tss");
#endif
            }
            // the document lives on an inactive child until its panel is set, so OnEnable builds the root on that panel
            var host = new GameObject("doc");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = panel;
            host.SetActive(true);
            Build();
        }

        void Build()
        {
            var r = _doc.rootVisualElement;
            r.pickingMode = PickingMode.Ignore;
            _root = new VisualElement { name = "cine", pickingMode = PickingMode.Ignore };
            Fill(_root);
            r.Add(_root);

            _top = Bar(true); _bottom = Bar(false);
            _root.Add(_top); _root.Add(_bottom);

            _sub = new VisualElement { pickingMode = PickingMode.Ignore };
            _sub.style.position = Position.Absolute;
            _sub.style.left = Length.Percent(12); _sub.style.right = Length.Percent(12);
            _sub.style.alignItems = Align.Center;
            _sub.style.opacity = 0f;
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginBottom = 10;
            _accent = new VisualElement { pickingMode = PickingMode.Ignore };
            _accent.style.width = 26; _accent.style.height = 3; _accent.style.marginRight = 12;
            _name = new Label { pickingMode = PickingMode.Ignore };
            _name.style.fontSize = nameSize; _name.style.letterSpacing = 6; _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            Shadow(_name);
            row.Add(_accent); row.Add(_name);
            _text = new Label { pickingMode = PickingMode.Ignore };
            _text.style.fontSize = textSize; _text.style.color = new Color(0.95f, 0.96f, 0.97f);
            _text.style.whiteSpace = WhiteSpace.Normal; _text.style.unityTextAlign = TextAnchor.UpperCenter;
            _text.style.letterSpacing = 1;
            Shadow(_text);
            _sub.Add(row); _sub.Add(_text);
            _root.Add(_sub);

            _skip = new VisualElement { pickingMode = PickingMode.Ignore };
            _skip.style.position = Position.Absolute; _skip.style.right = 64; _skip.style.width = 180; _skip.style.height = 2;
            _skip.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f); _skip.style.opacity = 0f;
            _skipFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _skipFill.style.height = 2; _skipFill.style.width = Length.Percent(0); _skipFill.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            _skip.Add(_skipFill);
            _root.Add(_skip);

            _fade = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill(_fade);
            _fade.style.backgroundColor = Color.black; _fade.style.opacity = 0f;
            _root.Add(_fade);

            var font = Font();
            if (font.HasValue) { _name.style.unityFontDefinition = font.Value; _text.style.unityFontDefinition = font.Value; }
            Apply();
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        static VisualElement Bar(bool top)
        {
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.style.position = Position.Absolute; b.style.left = 0; b.style.right = 0;
            if (top) b.style.top = 0; else b.style.bottom = 0;
            b.style.backgroundColor = Color.black;
            return b;
        }

        static void Shadow(Label l) => l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 10f, color = new Color(0f, 0f, 0f, 0.95f) };

        // ------------------------------------------------------------------------------------------------ API
        public void Letterbox(bool on, float seconds = 0.8f) { _barsT = on ? 1f : 0f; _barsSpeed = 1f / Mathf.Max(0.01f, seconds); if (seconds <= 0f) _bars = _barsT; }
        public void Fade(float alpha, float seconds = 0.5f) { _fadeT = Mathf.Clamp01(alpha); _fadeSpeed = 1f / Mathf.Max(0.01f, seconds); if (seconds <= 0f) _fadeA = _fadeT; Apply(); }
        public void SetSkip(float k)
        {
            if (_skip == null) return;
            _skip.style.opacity = k > 0.001f ? 1f : 0f;
            _skipFill.style.width = Length.Percent(Mathf.Clamp01(k) * 100f);
        }

        /// Show a line: callsign in the accent colour, the text typed on.
        public void Say(string speaker, string text, Color accent)
        {
            _name.text = speaker ?? "";
            _name.style.color = Color.Lerp(accent, Color.white, 0.15f);
            _accent.style.backgroundColor = accent;
            _full = text ?? ""; _typed = 0f; _typing = _full.Length > 0;
            _text.text = "";
            _subT = 1f;
        }

        public void FinishTyping() { _typed = _full.Length; _typing = false; _text.text = _full; }
        public void ClearLine() { _subT = 0f; }

        void Update() => Tick(GameFeel.RealDt);

        public void Tick(float dt)
        {
            if (_root == null) return;
            _bars = Mathf.MoveTowards(_bars, _barsT, dt * _barsSpeed);
            _fadeA = Mathf.MoveTowards(_fadeA, _fadeT, dt * _fadeSpeed);
            _subA = Mathf.MoveTowards(_subA, _subT, dt / (_subT > _subA ? 0.18f : 0.3f));
            if (_typing)
            {
                _typed += dt * charsPerSecond;
                int n = Mathf.Min(_full.Length, Mathf.FloorToInt(_typed));
                _text.text = _full.Substring(0, n);
                if (n >= _full.Length) _typing = false;
            }
            Apply();
        }

        void Apply()
        {
            if (_root == null) return;
            float e = _bars * _bars * (3f - 2f * _bars);
            float h = barHeight * 100f * e;
            _top.style.height = Length.Percent(h); _bottom.style.height = Length.Percent(h);
            _sub.style.bottom = Length.Percent(Mathf.Max(h, 4f) + 3.2f);
            _sub.style.opacity = _subA;
            _skip.style.bottom = Length.Percent(Mathf.Max(h, 2f) * 0.5f);
            _fade.style.opacity = _fadeA;
        }

        // ------------------------------------------------------------------------------------------------ recording
        /// Recordings: draw the panel into a texture (composited over the camera image by the recorder).
        public void SetTarget(RenderTexture rt)
        {
            if (!panel) return;
            if (rt && panel.name.EndsWith("(runtime)") == false) { panel = Instantiate(panel); _doc.panelSettings = panel; }
            panel.targetTexture = rt;
            panel.clearColor = rt != null;
            panel.colorClearValue = new Color(0f, 0f, 0f, 0f);
        }

        // ------------------------------------------------------------------------------------------------ font
        /// Bahnschrift (the title's face) with Malgun Gothic behind it for Hangul; built-in font if neither exists.
        static FontDefinition? Font()
        {
            if (s_font.HasValue) return s_font;
            FontAsset Make(string family)
            {
                if (OYM.Web.WebFonts.IsWeb) return OYM.Web.WebFonts.For(family);   // no OS fonts in a browser
                try { return FontAsset.CreateFontAsset(family, "Regular"); }
                catch (System.Exception e) { Debug.LogWarning($"[OYM-CINE] font {family}: {e.Message}"); return null; }
            }
            var latin = Make("Bahnschrift");
            var hangul = Make("Malgun Gothic");
            var primary = latin ? latin : hangul;
            if (primary)
            {
                if (latin && hangul)
                {
                    if (latin.fallbackFontAssetTable == null) latin.fallbackFontAssetTable = new List<FontAsset>();
                    latin.fallbackFontAssetTable.Add(hangul);
                }
                s_font = FontDefinition.FromSDFFont(primary);
                return s_font;
            }
            var builtin = Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
            if (builtin) s_font = FontDefinition.FromFont(builtin);
            return s_font;
        }
    }
}
