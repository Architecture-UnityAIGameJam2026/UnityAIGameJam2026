using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Background music that survives scene loads: two looping 2D AudioSources with an equal-power crossfade, timed in
    /// real time (hit-stop scales Time.timeScale). The track for a scene comes from Resources/OYM_BgmRoutes
    /// (MainTitle -> main theme, the debris-field game scenes -> battle); scenes without a route keep what is playing.
    /// Music on/off is remembered in PlayerPrefs (title screen MUSIC button).
    [DefaultExecutionOrder(-200)]
    public class BgmPlayer : MonoBehaviour
    {
        const string MuteKey = "oym.bgm.muted";

        public static BgmPlayer Instance { get; private set; }
        public static float MasterVolume = 1f;

        static int s_muted = -1;
        public static bool Muted
        {
            get { if (s_muted < 0) s_muted = PlayerPrefs.GetInt(MuteKey, 0); return s_muted == 1; }
            set { s_muted = value ? 1 : 0; PlayerPrefs.SetInt(MuteKey, s_muted); PlayerPrefs.Save(); }
        }

        readonly AudioSource[] _src = new AudioSource[2];
        readonly float[] _gain = new float[2], _target = new float[2], _rate = new float[2], _vol = new float[2];
        int _cur = -1;

        /// The clip that is playing or fading in (null when silent).
        public static AudioClip Current => Instance && Instance._cur >= 0 && Instance._target[Instance._cur] > 0f ? Instance._src[Instance._cur].clip : null;

        static BgmPlayer Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("BGM");
            DontDestroyOnLoad(go);
            return go.AddComponent<BgmPlayer>();                       // Awake sets Instance
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < 2; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.spatialBlend = 0f; s.priority = 0;
                s.bypassReverbZones = true; s.bypassListenerEffects = true; s.ignoreListenerPause = true;
                s.volume = 0f;
                _src[i] = s;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// Crossfade to `clip` (no-op if it is already the current track).
        public static void Play(AudioClip clip, float volume = 0.8f, float fade = 1.5f)
        {
            if (!clip) return;
            var p = Ensure();
            if (p._cur >= 0 && p._src[p._cur].clip == clip && p._target[p._cur] > 0f) { p._vol[p._cur] = volume; return; }
            int next = p._cur < 0 ? 0 : 1 - p._cur;
            if (p._cur >= 0) p.FadeTo(p._cur, 0f, fade);
            var s = p._src[next];
            s.Stop(); s.clip = clip; s.time = 0f; s.Play();
            p._gain[next] = 0f; p._vol[next] = volume;
            p.FadeTo(next, 1f, fade);
            p._cur = next;
            p.UpdateVolumes(0f);
        }

        /// Fade the current track out (e.g. before a scene change; the next scene's route fades its track in).
        public static void FadeOut(float seconds)
        {
            if (!Instance || Instance._cur < 0) return;
            Instance.FadeTo(Instance._cur, 0f, seconds);
        }

        void FadeTo(int i, float gain, float seconds) { _target[i] = gain; _rate[i] = seconds <= 0.001f ? 1e6f : 1f / seconds; }

        void Update() { UpdateVolumes(Time.unscaledDeltaTime); }

        void UpdateVolumes(float dt)
        {
            float master = Muted ? 0f : MasterVolume;
            for (int i = 0; i < 2; i++)
            {
                var s = _src[i]; if (!s) continue;
                _gain[i] = Mathf.MoveTowards(_gain[i], _target[i], _rate[i] * dt);
                s.volume = Mathf.Sin(_gain[i] * Mathf.PI * 0.5f) * _vol[i] * master;     // equal power
                if (_gain[i] <= 0f && _target[i] <= 0f && s.isPlaying) s.Stop();
            }
        }

        // ------------------------------------------------------------------------------------------------ routing
        static BgmRoutes s_routes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null; s_routes = null; s_muted = -1;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Application.isBatchMode) return;
            // a single load switches the music; any other load only starts it while nothing plays. Pressing Play in the
            // editor does not report the scene that is already open as a single load, which left the title silent (11-19)
            if (mode != LoadSceneMode.Single && Current) return;
            PlayRoute(scene);
        }

        /// The first scene, however its load was reported (editor Play, player start). Play() ignores a repeat.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartFirst()
        {
            if (Application.isBatchMode || Current) return;
            PlayRoute(SceneManager.GetActiveScene());
        }

        static void PlayRoute(Scene scene)
        {
            if (!s_routes) s_routes = Resources.Load<BgmRoutes>(BgmRoutes.ResourceName);
            var r = s_routes ? s_routes.Find(scene.name) : null;
            if (r != null && r.clip) Play(r.clip, r.volume, r.fade);
        }
    }
}
