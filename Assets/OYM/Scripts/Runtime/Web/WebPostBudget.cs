using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using U = UnityEngine.Rendering.Universal;

namespace OYM.Web
{
    /// Web build (URP) only: cheaper bloom. The HDRP bloom was mapped 1:1 (threshold 0, so every pixel feeds it), and
    /// URP's default starts the bloom chain at half resolution with up to 6 mips. Here every loaded volume profile's
    /// URP Bloom starts at quarter resolution with at most 5 mips: the wide, soft glow looks the same, the full-screen
    /// prefilter and the first mip cost a quarter. Applied on each scene load in the player only, so profile assets are
    /// never changed in the editor.
    public static class WebPostBudget
    {
        public const int BloomMaxIterations = 5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SceneManager.sceneLoaded -= OnScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !WebLightAdapter.IsUrp) return;
            SceneManager.sceneLoaded += OnScene;
            Tune();
        }

        static void OnScene(Scene s, LoadSceneMode m) => Tune();

        static void Tune()
        {
            int n = 0;
            foreach (var p in Resources.FindObjectsOfTypeAll<VolumeProfile>())
            {
                if (p == null || !p.TryGet<U.Bloom>(out var b)) continue;
                if (b.downscale.overrideState && b.downscale.value == U.BloomDownscaleMode.Quarter) continue;
                b.downscale.Override(U.BloomDownscaleMode.Quarter);
                b.maxIterations.Override(Mathf.Min(b.maxIterations.value, BloomMaxIterations));
                n++;
            }
            if (n > 0) Debug.Log($"[OYM-WEB] bloom at quarter res on {n} volume profiles");
        }
    }
}
