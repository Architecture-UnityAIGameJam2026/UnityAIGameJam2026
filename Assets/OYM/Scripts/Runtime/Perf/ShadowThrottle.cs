using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Spreads the shadow work of big point / spot lights over frames (no scene edits).
    ///
    /// The debris-field core light is a 900 m point light with shadows: six cube faces, each culling and drawing the
    /// whole field every frame. Here such lights switch to HDRP cached (on-demand) shadows and get
    /// <see cref="PointFacesPerFrame"/> faces re-rendered per frame, so the full cube refreshes every 3 frames
    /// (50 ms at 60 fps; the rocks move a few cm in that time). HDRP skips shadow culling for faces that are not
    /// requested, so this saves main-thread culling as well as GPU time. Small lights (< MinRange) are left alone.
    public sealed class ShadowThrottle : MonoBehaviour
    {
        public const float MinRange = 60f;
        public const int PointFacesPerFrame = 2;
        public const int SpotEveryNthFrame = 2;

        readonly List<HDAdditionalLightData> _point = new();
        readonly List<HDAdditionalLightData> _spot = new();
        int _face, _frame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymNoShadowThrottle") >= 0) return;
            var go = new GameObject("~ShadowThrottle") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<ShadowThrottle>();
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; Collect(); }
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        void OnSceneLoaded(Scene s, LoadSceneMode m) => Collect();

        void Collect()
        {
            _point.Clear(); _spot.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (l.shadows == LightShadows.None || l.range < MinRange) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                if (!l.TryGetComponent(out HDAdditionalLightData hd) || hd.shadowUpdateMode != ShadowUpdateMode.EveryFrame) continue;
                hd.shadowUpdateMode = ShadowUpdateMode.OnDemand;
                hd.RequestShadowMapRendering(); // full cube on the first frame
                (l.type == LightType.Point ? _point : _spot).Add(hd);
            }
        }

        void LateUpdate()
        {
            _frame++;
            for (int i = _point.Count - 1; i >= 0; i--)
            {
                var hd = _point[i];
                if (hd == null) { _point.RemoveAt(i); continue; }
                if (!hd.isActiveAndEnabled) continue;
                for (int k = 0; k < PointFacesPerFrame; k++) hd.RequestSubShadowMapRendering((_face + k) % 6);
            }
            _face = (_face + PointFacesPerFrame) % 6;

            if (_frame % SpotEveryNthFrame != 0) return;
            for (int i = _spot.Count - 1; i >= 0; i--)
            {
                var hd = _spot[i];
                if (hd == null) { _spot.RemoveAt(i); continue; }
                if (hd.isActiveAndEnabled) hd.RequestShadowMapRendering();
            }
        }
    }
}
