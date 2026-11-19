using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Title screen sound layer (user 11-19: "메인 타이틀에 효과음이랑 BGM이 안나와"). The title had no sound effects at all;
    /// the title scripts are being edited by other sessions, so this layer only listens and is not called by them:
    ///   * TitleCharacter.SlotChanged / Struck (the same hooks TitleFX uses): a pilot snapping to the front fires its
    ///     jets, the strike lands (KESTREL's blade / ORBIT FRAME's RG-77), the deploy launches
    ///   * TitleUI's menu cursor and mode, read by reflection every frame (the layer goes quiet, with one warning, if
    ///     those private fields are ever renamed) - cursor ticks, open / back clicks, the friend-code radio chirp
    ///   * OPTION master / music changes tick
    /// Clips: Resources/OYM_TitleSfx (picked from the game sound collection for the team's AudioEffect set). 2D one-shots,
    /// so OPTION > MASTER (AudioListener.volume) scales them; music stays with BgmPlayer. Installs itself in the title.
    public class TitleSfx : MonoBehaviour
    {
        const string Dir = "OYM_TitleSfx/";

        AudioSource _tick, _sel, _back, _fx, _fx2;
        AudioClip _click, _ignite, _transform, _boost, _zoom, _chirp;
        AudioClip[] _swing, _hit, _rail;
        int _lastSwing = -1, _lastHit = -1, _lastRail = -1;

        TitleDirector _dir;
        TitleUI _ui;
        TitleCharacter[] _pilots;
        FieldInfo _fMode, _fMenu, _fOpt, _fExit;
        object _mode;
        int _menu = int.MinValue, _opt = int.MinValue, _exit = int.MinValue;
        float _t0, _master, _music, _nextTick;
        bool _introPlayed, _reported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (Application.isBatchMode || s.name != GameSession.TitleScene) return;
            if (FindAnyObjectByType<TitleSfx>()) return;
            var go = new GameObject("Title SFX");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<TitleSfx>();
        }

        void Awake()
        {
            _tick = Src(0.9f); _sel = Src(0.95f); _back = Src(0.8f); _fx = Src(1f); _fx2 = Src(1f);
            _click = L("ui_click"); _ignite = L("jet_ignite"); _transform = L("weapon_transform");
            _boost = L("boost"); _zoom = L("zoom_focus"); _chirp = L("radio_chirp");
            _swing = new[] { L("sword_swing_1"), L("sword_swing_2"), L("sword_swing_3") };
            _hit = new[] { L("sword_hit_1"), L("sword_hit_2"), L("sword_hit_3") };
            _rail = new[] { L("railgun_1"), L("railgun_2") };
            _t0 = Time.unscaledTime;
        }

        AudioSource Src(float pitch)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.pitch = pitch; s.priority = 40;
            s.ignoreListenerPause = true; s.bypassReverbZones = true;
            return s;
        }

        static AudioClip L(string n) => Resources.Load<AudioClip>(Dir + n);

        void Start()
        {
            _dir = FindAnyObjectByType<TitleDirector>();
            _ui = FindAnyObjectByType<TitleUI>();
            _pilots = _dir ? _dir.pilots : null;
            if (_pilots != null)
                foreach (var p in _pilots) if (p) { p.SlotChanged += OnSlot; p.Struck += OnStrike; }
            if (_ui)
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(TitleUI);
                _fMode = t.GetField("_mode", bf); _fMenu = t.GetField("_menuSel", bf);
                _fOpt = t.GetField("_optSel", bf); _fExit = t.GetField("_exitSel", bf);
                if (_fMode == null || _fMenu == null) Debug.LogWarning("[OYM-TitleSfx] TitleUI cursor fields not found - menu sounds off");
            }
            _master = GameSettings.Master; _music = GameSettings.Music;
            Debug.Log($"[OYM-TitleSfx] ready: director {(_dir ? "yes" : "no")}, ui {(_ui ? "yes" : "no")}, pilots {(_pilots != null ? _pilots.Length : 0)}, clips {(_click && _ignite && _hit[0] && _rail[0] ? "ok" : "MISSING")}");
        }

        void OnDestroy()
        {
            if (_pilots != null) foreach (var p in _pilots) if (p) { p.SlotChanged -= OnSlot; p.Struck -= OnStrike; }
        }

        static void Play(AudioSource s, AudioClip c, float vol) { if (s && c) s.PlayOneShot(c, vol); }

        AudioClip Pick(AudioClip[] set, ref int last)
        {
            int n = set.Length, i = Random.Range(0, n);
            if (n > 1 && i == last) i = (i + 1 + Random.Range(0, n - 1)) % n;          // never the same one twice
            last = i;
            return set[i];
        }

        bool IsBlade(TitleCharacter p) => p && p.displayName != null && p.displayName.ToUpperInvariant().Contains("KESTREL");

        void OnSlot(TitleCharacter p, TitleCharacter.Slot s)
        {
            switch (s)
            {
                case TitleCharacter.Slot.Front:
                    Play(_fx, _ignite, 0.55f);
                    Play(_sel, _transform, 0.4f);
                    if (IsBlade(p)) Play(_fx2, Pick(_swing, ref _lastSwing), 0.35f);
                    break;
                case TitleCharacter.Slot.Back:
                    Play(_back, _ignite, 0.18f);                                   // retro kick, pushed back
                    break;
                case TitleCharacter.Slot.Launch:
                    Play(_fx, _boost, 0.85f);
                    Play(_fx2, _ignite, 0.95f);
                    break;
            }
        }

        void OnStrike(TitleCharacter p)
        {
            if (IsBlade(p))
            {
                Play(_fx, Pick(_hit, ref _lastHit), 0.8f);
                Play(_fx2, Pick(_swing, ref _lastSwing), 0.5f);
            }
            else Play(_fx, Pick(_rail, ref _lastRail), 0.75f);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (!_introPlayed && _dir && now - _t0 > _dir.uiIntro) { _introPlayed = true; Play(_fx2, _zoom, 0.45f); }
            if (!_reported && now - _t0 > 4f) { _reported = true; ReportMusic(); }

            if (_ui && _fMode != null)
            {
                object mode = _fMode.GetValue(_ui);
                if (_mode != null && !mode.Equals(_mode)) ModeChanged(_mode.ToString(), mode.ToString());
                _mode = mode;
                Track(_fMenu, ref _menu);
                Track(_fOpt, ref _opt);
                Track(_fExit, ref _exit);
            }

            float m = GameSettings.Master, mu = GameSettings.Music;                  // OPTION sliders / MUSIC toggle
            if (Mathf.Abs(m - _master) > 1e-3f || Mathf.Abs(mu - _music) > 1e-3f) { Tick(0.4f); _master = m; _music = mu; }
        }

        /// one line in the Console / Editor.log 4 s into the title: is the theme actually playing, and how loud
        void ReportMusic()
        {
            var sb = new System.Text.StringBuilder("[OYM-TitleSfx] audio check: ");
            sb.Append($"listener volume {AudioListener.volume:F2} pause {AudioListener.pause}, listeners {FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}, ");
            sb.Append($"bgm current {(BgmPlayer.Current ? BgmPlayer.Current.name : "none")}, muted {BgmPlayer.Muted}, master {BgmPlayer.MasterVolume:F2}");
            if (BgmPlayer.Instance)
                foreach (var s in BgmPlayer.Instance.GetComponents<AudioSource>())
                    sb.Append($" | src {(s.clip ? s.clip.name + " " + s.clip.loadState : "-")} playing {s.isPlaying} vol {s.volume:F2} t {s.time:F1}");
            else sb.Append(" | no BGM player");
            Debug.Log(sb.ToString());
        }

        void Track(FieldInfo f, ref int last)
        {
            if (f == null) return;
            int v = (int)f.GetValue(_ui);
            if (last != int.MinValue && v != last) Tick(0.4f);
            last = v;
        }

        void Tick(float vol)
        {
            if (Time.unscaledTime < _nextTick) return;                              // a held key repeats fast: no machine gun
            _nextTick = Time.unscaledTime + 0.05f;
            Play(_tick, _click, vol);
        }

        void ModeChanged(string from, string to)
        {
            if (from == "Intro") return;                                            // the intro has its own whoosh
            switch (to)
            {
                case "Main": Play(_back, _click, 0.55f); break;                      // back out of a panel
                case "Friend": Play(_sel, _click, 0.7f); Play(_fx2, _chirp, 0.5f); break;
                case "Deploy": Play(_sel, _click, 0.8f); break;
                default: Play(_sel, _click, 0.7f); break;                           // OPTION / EXIT opened
            }
        }
    }
}
