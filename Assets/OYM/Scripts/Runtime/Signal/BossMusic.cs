using UnityEngine;

namespace OYM.Signal
{
    /// The CITADEL's own music ("The Void Awakens", the user's Suno pick; Resources/OYM_Music, cut by
    /// pipeline/jevmark/music/cut_boss.py into intro / 32-bar loop / collapse ending, 120 BPM):
    ///   the level's "…too quiet" bark  -> the battle BGM fades out (BgmPlayer)
    ///   the entrance starts           -> the intro is scheduled so its first big hit lands on the roar (BossEntrance.TRoar)
    ///   the fight                     -> the intro runs on into the loop, sample-exact (PlayScheduled)
    ///   victory                       -> on the next bar line the collapse ending takes over, then the main theme returns
    /// Real time (dspTime): hit-stops and the entrance's slow-down don't drift it. Follows BgmPlayer's volume / mute.
    public class BossMusic : MonoBehaviour
    {
        [System.Serializable]
        class Meta { public float bpm = 120f, barPhase = 0.438f, hit = 8.48f, introLength, loopLength, endLength; }

        [Range(0f, 1f)] public float volume = 0.85f;
        public float themeVolume = 0.6f;

        enum State { Idle, Playing, Ending, Done }
        State _s;
        AudioClip _intro, _loop, _end;
        Meta _m;
        AudioSource _a, _b, _c;                                    // intro, loop, ending
        HuskEncounter _e;
        HuskLevel _level;
        double _startDsp, _switchDsp = -1, _themeDsp = -1;
        bool _ready;

        void Start()
        {
            _intro = Resources.Load<AudioClip>("OYM_Music/boss_citadel_intro");
            _loop = Resources.Load<AudioClip>("OYM_Music/boss_citadel_loop");
            _end = Resources.Load<AudioClip>("OYM_Music/boss_citadel_end");
            var meta = Resources.Load<TextAsset>("OYM_Music/boss_citadel");
            _m = meta ? JsonUtility.FromJson<Meta>(meta.text) : new Meta();
            _ready = _intro && _loop && _end;
            if (!_ready) { Debug.LogWarning("[OYM-MUSIC] Resources/OYM_Music boss clips missing: the boss keeps the level BGM"); return; }
            foreach (var c in new[] { _intro, _loop, _end }) c.LoadAudioData();   // ready before the entrance schedules them
            _a = Source(); _b = Source(); _c = Source();
        }

        AudioSource Source()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.priority = 0;
            s.bypassReverbZones = true; s.bypassListenerEffects = true; s.ignoreListenerPause = true;
            s.volume = 0f;
            return s;
        }

        static double Len(AudioClip c) => (double)c.samples / c.frequency;
        double Bar => 240.0 / Mathf.Max(1f, _m.bpm);

        void Update()
        {
            if (!_ready || _s == State.Done && _themeDsp < 0) return;
            if (!_e) { _e = HuskEncounter.Current; if (!_e) return; }
            if (!_level && _e.Level) { _level = _e.Level; _level.Radio += OnBark; }
            var be = _e.boss;
            double now = AudioSettings.dspTime;

            if (_s == State.Idle && be && be.Playing) Begin(be, now);
            if (_s == State.Playing && _level && _level.beat == HuskLevel.Beat.Victory) End(now);

            // volumes: BGM master / mute, the short crossfade into the ending
            float master = (BgmPlayer.Muted ? 0f : BgmPlayer.MasterVolume) * volume;
            float old = 1f, fresh = 1f;
            if (_switchDsp > 0)
            {
                old = Mathf.Clamp01((float)((_switchDsp + 0.1 - now) / 0.12));
                fresh = Mathf.Clamp01((float)((now - _switchDsp) / 0.03));
            }
            if (_a) _a.volume = master * old;
            if (_b) _b.volume = master * old;
            if (_c) _c.volume = master * fresh;

            if (_themeDsp > 0 && now >= _themeDsp)
            {
                _themeDsp = -1;
                var routes = Resources.Load<BgmRoutes>(BgmRoutes.ResourceName);
                var r = routes ? routes.Find(GameSession.TitleScene) : null;
                if (r != null && r.clip) BgmPlayer.Play(r.clip, themeVolume, 4f);
                Debug.Log("[OYM-MUSIC] boss music over -> main theme");
            }
        }

        void OnDestroy() { if (_level) _level.Radio -= OnBark; }

        /// the level's "…too quiet" beat before the CITADEL: let the battle music drain away
        void OnBark(string speaker, string line)
        {
            if (_s == State.Idle && _e && _e.config && line == _e.config.barkQuiet) BgmPlayer.FadeOut(2.4f);
        }

        void Begin(BossEntrance be, double now)
        {
            _s = State.Playing;
            BgmPlayer.FadeOut(1f);
            // the intro's first big hit (meta.hit) on the entrance's roar
            double lead = BossEntrance.TRoar - _m.hit - be.Clock;   // seconds from now until the intro must start
            double offset = 0;
            if (lead < 0.05) { offset = -lead + 0.05; lead = 0.05; }        // noticed late: start inside the intro
            _startDsp = now + lead - offset;
            _a.clip = _intro; _a.loop = false; _a.time = (float)offset;
            _a.PlayScheduled(now + lead);
            _b.clip = _loop; _b.loop = true;
            _b.PlayScheduled(_startDsp + Len(_intro));
            Debug.Log($"[OYM-MUSIC] CITADEL theme: intro in {lead:F2} s (offset {offset:F2}), hit on the roar at entrance {BossEntrance.TRoar:F1} s, loop at +{Len(_intro):F1} s");
        }

        void End(double now)
        {
            _s = State.Ending;
            double t = now - _startDsp;                                  // unwrapped track time (the loop is whole bars)
            double next = _m.barPhase + Bar * System.Math.Ceiling((t + 0.12 - _m.barPhase) / Bar);
            _switchDsp = _startDsp + next;
            _c.clip = _end; _c.loop = false;
            _c.PlayScheduled(_switchDsp);
            _a.SetScheduledEndTime(_switchDsp + 0.14);
            _b.SetScheduledEndTime(_switchDsp + 0.14);
            _themeDsp = _switchDsp + Len(_end) + 1.2;
            _s = State.Done;
            Debug.Log($"[OYM-MUSIC] victory: collapse ending on the bar at track {next:F2} s (in {_switchDsp - now:F2} s)");
        }
    }
}
