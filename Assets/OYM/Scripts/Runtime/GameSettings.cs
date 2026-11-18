using UnityEngine;

namespace OYM
{
    /// Player settings and small profile values kept in PlayerPrefs (title OPTION panel, remembered pilot, friend code).
    /// Volumes are applied at startup so every scene starts with the saved mix.
    public static class GameSettings
    {
        const string KMaster = "oym.audio.master", KMusic = "oym.audio.music", KSens = "oym.input.sensitivity";
        const string KFull = "oym.display.fullscreen", KPilot = "oym.title.pilot", KCode = "oym.coop.friendcode";
        /// friend code characters: no 0 / O / 1 / I, so a code read aloud or off a screenshot is unambiguous
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 8;

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

        /// This install's co-op friend code, 8 characters without the dash (made once, then kept).
        public static string FriendCode
        {
            get
            {
                string c = PlayerPrefs.GetString(KCode, "");
                if (IsValidCode(c)) return c;
                var rnd = new System.Random(System.Guid.NewGuid().GetHashCode());
                var chars = new char[CodeLength];
                for (int i = 0; i < CodeLength; i++) chars[i] = CodeAlphabet[rnd.Next(CodeAlphabet.Length)];
                c = new string(chars);
                PlayerPrefs.SetString(KCode, c);
                PlayerPrefs.Save();
                return c;
            }
        }

        public static bool IsValidCode(string c)
        {
            if (c == null || c.Length != CodeLength) return false;
            foreach (char ch in c) if (CodeAlphabet.IndexOf(ch) < 0) return false;
            return true;
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
