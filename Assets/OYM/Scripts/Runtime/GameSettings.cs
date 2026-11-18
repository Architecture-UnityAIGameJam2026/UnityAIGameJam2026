using UnityEngine;

namespace OYM
{
    /// Player settings kept in PlayerPrefs (title OPTION panel and remembered pilot).
    /// Volumes are applied at startup so every scene starts with the saved mix.
    public static class GameSettings
    {
        const string KMaster = "oym.audio.master", KMusic = "oym.audio.music", KSens = "oym.input.sensitivity";
        const string KFull = "oym.display.fullscreen", KPilot = "oym.title.pilot";

        public static float Master
        {
            get => PlayerPrefs.GetFloat(KMaster, 1f);
            set { float v = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KMaster, v); AudioListener.volume = v; }
        }

        public static float Music
        {
            get => PlayerPrefs.GetFloat(KMusic, 1f);
            set { float v = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KMusic, v); BgmPlayer.MasterVolume = v; }
        }

        /// mouse look multiplier (stored for the flight controllers; 1 = default)
        public static float Sensitivity
        {
            get => PlayerPrefs.GetFloat(KSens, 1f);
            set => PlayerPrefs.SetFloat(KSens, Mathf.Clamp(value, 0.1f, 5f));
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(KFull, 1) == 1;
            set { PlayerPrefs.SetInt(KFull, value ? 1 : 0); if (!Application.isEditor) Screen.fullScreen = value; }
        }

        /// pilot index the title opens on (0 KESTREL, 1 ORBIT FRAME)
        public static int TitlePilot
        {
            get => PlayerPrefs.GetInt(KPilot, 1);
            set => PlayerPrefs.SetInt(KPilot, value);
        }

        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            AudioListener.volume = Master;
            BgmPlayer.MasterVolume = Music;
            if (!Application.isEditor && Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
        }
    }
}
