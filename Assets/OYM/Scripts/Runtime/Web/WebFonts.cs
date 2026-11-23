using System.Collections.Generic;
using UnityEngine;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace OYM.Web
{
    /// Fonts bundled for the web build. A browser does not hand its fonts to Unity, so the OS fonts the game asks for
    /// (Malgun Gothic for Hangul, Bahnschrift for the title face) are missing there. Noto Sans KR (SIL OFL) stands in
    /// for Malgun Gothic and Rajdhani (SIL OFL, already in the project's HUD) for Bahnschrift.
    public static class WebFonts
    {
        static Font s_kr, s_latin;
        static FontAsset s_krAsset, s_latinAsset;

        public static Font Korean => s_kr ? s_kr : (s_kr = Resources.Load<Font>("OYMWeb_NotoSansKR"));
        public static Font Latin => s_latin ? s_latin : (s_latin = Resources.Load<Font>("OYMWeb_Latin"));

        public static FontAsset KoreanAsset
        {
            get
            {
                if (!s_krAsset && Korean) s_krAsset = FontAsset.CreateFontAsset(Korean);
                return s_krAsset;
            }
        }

        /// Latin face with the Hangul face behind it
        public static FontAsset LatinAsset
        {
            get
            {
                if (!s_latinAsset && Latin)
                {
                    s_latinAsset = FontAsset.CreateFontAsset(Latin);
                    var kr = KoreanAsset;
                    if (s_latinAsset && kr)
                    {
                        if (s_latinAsset.fallbackFontAssetTable == null) s_latinAsset.fallbackFontAssetTable = new List<FontAsset>();
                        s_latinAsset.fallbackFontAssetTable.Add(kr);
                    }
                }
                return s_latinAsset ? s_latinAsset : KoreanAsset;
            }
        }

        /// stand-in for an OS font family the game asked for
        public static FontAsset For(string family) =>
            family == "Malgun Gothic" || family == "맑은 고딕" ? KoreanAsset : LatinAsset;

        /// web: every UI Toolkit text settings and TextMeshPro get Noto Sans KR as a fallback, so Hangul in any label
        /// (HUD, subtitles, radio) renders even when the label's own face has no Hangul
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallFallbacks()
        {
            if (!IsWeb) return;
            Hook();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Hook();
        }

        static TMPro.TMP_FontAsset s_tmpKr;
        static ImguiFont s_imgui;

        /// IMGUI debug / radio overlays: the default skin's font gets Hangul (the skin is shared by every OnGUI)
        sealed class ImguiFont : MonoBehaviour
        {
            bool _done;
            void OnGUI()
            {
                if (_done || GUI.skin == null) return;
                GUI.skin.font = Korean;
                _done = true;
            }
        }

        static void Hook()
        {
            var kr = KoreanAsset;
            if (kr)
                foreach (var ts in Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.TextSettings>())
                {
                    if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new List<FontAsset>();
                    if (!ts.fallbackFontAssets.Contains(kr)) ts.fallbackFontAssets.Add(kr);
                }
            if (!s_tmpKr && Korean) s_tmpKr = TMPro.TMP_FontAsset.CreateFontAsset(Korean);
            if (s_tmpKr && TMPro.TMP_Settings.instance != null)
            {
                var list = TMPro.TMP_Settings.fallbackFontAssets;
                if (list != null && !list.Contains(s_tmpKr)) list.Add(s_tmpKr);
            }
            if (!s_imgui && Korean)
            {
                var go = new GameObject("~WebImguiFont") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
                Object.DontDestroyOnLoad(go);
                s_imgui = go.AddComponent<ImguiFont>();
            }
        }

        public static bool IsWeb
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
    }
}
