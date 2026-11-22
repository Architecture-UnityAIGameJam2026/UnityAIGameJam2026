using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace OYM.Signal
{
    /// The combat signal layer of the showcase scene (ShowcaseScene.Name): JEV radio mediator, lock-on, marks.
    /// Installs itself on sceneLoaded like HuskEncounter (no scene edits). Off in batch mode, with -oymNoSignal, and
    /// while another session's recorder drives the scene (any other -oym... flag).
    public static class SignalInstall
    {
        public static string RoleOf(ZeroGFlight f) => f is KestrelController ? "sword" : "rail";
        public static string NameOf(ZeroGFlight f) => f is KestrelController ? "KESTREL" : f is OrbitFrameController ? "ORBIT FRAME" : (f ? f.name : "");
        public static readonly Color KestrelAccent = new Color(0.45f, 0.85f, 1f), OrbitAccent = new Color(1f, 0.68f, 0.25f);
        public static readonly Color JevAccent = new Color(0.3f, 1f, 0.86f);
        public static Color AccentOf(ZeroGFlight f) => f is KestrelController ? KestrelAccent : OrbitAccent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        public static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// another session's recorder / demo drives the scene (switch flags like -oymNoRadio / -oymNoFogCull and the perf
        /// HUD are not automation)
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymSignalDemo" && a != "-oymPerfHud") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != ShowcaseScene.Name || Flag("-oymNoSignal") || OtherAutomation()) return;
            bool demo = Flag("-oymSignalDemo");
            if (demo) SignalDemo.InstallHusk(s);                        // HuskEncounter stands down under -oym flags
            var go = new GameObject("Signal (JEV radio, lock-on, marks)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<LockOn>();
            go.AddComponent<MarkOverlay>();
            if (!Flag("-oymNoRadio")) go.AddComponent<RadioMediator>();
            if (!Flag("-oymNoBossMusic")) go.AddComponent<BossMusic>();
            go.AddComponent<SignalDevKeys>();
            if (demo) go.AddComponent<SignalDemo>();
        }

        /// the player has the controls: no arrival cutscene, no boss entrance
        public static bool PlayerView(HuskEncounter e)
        {
            if (!e) return !HuskEncounter.CutsceneRunning();
            return e.Gate && !e.hold;
        }
    }

    /// UI Toolkit plumbing shared by the radio panel and the marks: the cine panel settings and the pilot font.
    public static class SignalUI
    {
        static FontDefinition? s_font;
        static PanelSettings s_panel;

        public static UIDocument Document(Transform parent, string name, float order)
        {
            if (!s_panel) s_panel = Resources.Load<PanelSettings>("OYM_Cine/OYM_CinePanel");
            if (!s_panel)
            {
                s_panel = ScriptableObject.CreateInstance<PanelSettings>();
                s_panel.name = "OYM_SignalPanel (runtime)";
                s_panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                s_panel.referenceResolution = new Vector2Int(1920, 1080);
                s_panel.match = 0.5f;
                s_panel.sortingOrder = 50;
#if UNITY_EDITOR
                s_panel.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/OYM/Title/OYM_RuntimeTheme.tss");
#endif
            }
            // the document lives on an inactive child until its panel is set (same as CineOverlay)
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = s_panel;
            doc.sortingOrder = order;
            host.SetActive(true);
            var r = doc.rootVisualElement;
            r.pickingMode = PickingMode.Ignore;
            return doc;
        }

        public static VisualElement Fill(VisualElement v)
        {
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0; v.style.top = 0; v.style.bottom = 0;
            v.pickingMode = PickingMode.Ignore;
            return v;
        }

        public static Label Text(float size, Color c, int spacing = 0, bool bold = false)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = c; l.style.letterSpacing = spacing;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 8f, color = new Color(0f, 0f, 0f, 0.9f) };
            var f = Font();
            if (f.HasValue) l.style.unityFontDefinition = f.Value;
            return l;
        }

        /// Bahnschrift with Malgun Gothic as the Hangul fallback (the cine subtitles' font)
        public static FontDefinition? Font()
        {
            if (s_font.HasValue) return s_font;
            FontAsset Make(string family)
            {
                if (OYM.Web.WebFonts.IsWeb) return OYM.Web.WebFonts.For(family);   // no OS fonts in a browser
                try { return FontAsset.CreateFontAsset(family, "Regular"); }
                catch (System.Exception e) { Debug.LogWarning($"[OYM-SIGNAL] font {family}: {e.Message}"); return null; }
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
