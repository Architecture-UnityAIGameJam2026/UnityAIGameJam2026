using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OYM.Signal
{
    /// JEV, the AI mediator on the team radio (single-player Showcase). Watches the fight (HuskEncounter, HuskLevel,
    /// lock-on, the CITADEL weak points), turns what happens into event tags, and keeps the signal coming: on every
    /// urgent event and every few seconds while something is going on it picks one prerecorded line that answers the
    /// newest event - Jev on OpenRouter picks (or says none) when a key is present, otherwise a local ranking does.
    /// Lines: Resources/OYM_Radio (team mediator voice + ORBIT FRAME's m8b callouts when the partner is ORBIT FRAME).
    /// Holds while the level's own radio barks run and during the arrival / boss entrance cinematics.
    public class RadioMediator : MonoBehaviour
    {
        public static RadioMediator Current { get; private set; }
        public static event System.Action<RadioLine> Spoke;

        [Tooltip("seconds between evaluations while events are current")] public float decisionInterval = 6f;
        [Tooltip("silence after each line")] public float gap = 0.9f;
        [Tooltip("hold after one of the level's scripted barks")] public float barkHold = 3f;
        public float jevTimeout = 6f;
        public bool useJev = true;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("music level while a line plays (BgmPlayer master x this; -5 dB) - dialogue sits on top of the score")]
        [Range(0f, 1f)] public float musicDuck = 0.56f;

        float _duck = 1f, _musicSetting = 1f;
        bool _ducking;
        RadioCatalog _cat;
        RadioPanel _panel;
        AudioSource _src;
        HuskEncounter _e;
        HuskLevel _level;
        bool _hooked;

        readonly Dictionary<string, float> _events = new Dictionary<string, float>();
        readonly Dictionary<string, float> _said = new Dictionary<string, float>();
        string _lastId;
        float _nextDecision, _busyUntil, _holdUntil, _jevBackoff, _startT = -1f;
        bool _asking, _urgent;
        int _gen, _jevCalls, _jevPicks, _localPicks;

        // watchers (rising edges)
        bool _opened, _alive = true, _crit, _wasLow, _separated, _bossLive, _behind, _above, _fuse, _prismNear, _salvo, _contact, _firstClear;
        int _lastAwake, _lastPrism;
        float _fightSince = -1f, _weakHintT;
        readonly HashSet<HuskEnemy> _charging = new HashSet<HuskEnemy>();
        readonly Dictionary<HuskEnemy, float> _wokeAt = new Dictionary<HuskEnemy, float>();

        static readonly Dictionary<string, int> Urgency = new Dictionary<string, int>
        {
            { "down", 30 }, { "victory", 30 }, { "up", 28 }, { "weak_last", 26 }, { "weak_break", 24 }, { "behind", 22 }, { "wick_fuse", 22 },
            { "prism_charge", 20 }, { "salvo", 18 }, { "above", 18 }, { "boss_live", 16 }, { "hp_crit", 16 }, { "weak_hint", 14 },
            { "quiet", 15 }, { "open", 12 }, { "contact", 12 }, { "pack", 10 }, { "snipers", 10 }, { "clear_first", 10 }, { "last_one", 9 },
            { "clear", 8 }, { "prism_fired", 8 }, { "prism_near", 8 }, { "kill", 6 }, { "hp_ok", 6 }, { "up_calm", 6 },
            { "lock_weak", 5 }, { "lock_far", 5 }, { "los_blocked", 5 }, { "lock_lost_prism", 5 }, { "lock_lost", 4 }, { "relock", 4 },
            { "separated", 4 }, { "regroup", 4 }, { "lock", 3 }, { "two_targets", 3 },
        };
        const int UrgentAt = 14;
        static int U(string tag) => Urgency.TryGetValue(tag, out var u) ? u : 1;
        static float Window(string tag) => tag == "boss_live" || tag == "weak_hint" || tag == "open" || tag == "quiet" ? 9f : 5f;

        const string Instructions =
            "You are JEV, the AI mediator on the team radio of two pilots, KESTREL ('Sword', blade) and ORBIT FRAME ('Railgun', railgun), " +
            "fighting HUSK drones (WICK = kamikaze drones with a fuse, PRISM = lance snipers) and the CITADEL carrier in a zero-g debris field. " +
            "Only the pilot named in the state is fighting; the partner pilot holds back and listens. " +
            "Pick the single prerecorded line that best answers the NEWEST event in the state, or 'none' if nothing fits " +
            "or it would repeat what was just said. Lines addressed to 'you' speak to the fighting pilot.";

        // ------------------------------------------------------------------ lifecycle
        void Awake() { Current = this; }

        void Start()
        {
            _cat = RadioCatalog.Load();
            var pg = new GameObject("Radio panel");
            pg.transform.SetParent(transform, false);
            _panel = pg.AddComponent<RadioPanel>();
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false; _src.spatialBlend = 0f; _src.priority = 8;
            Debug.Log($"[OYM-RADIO] JEV mediator ready: {_cat.Lines.Count} lines, Jev " +
                      (JevLink.HasKey ? $"ON (key from {JevLink.KeySource})" : "OFF (no OpenRouter key: local picks keep the signal going)"));
        }

        void OnDestroy()
        {
            if (_ducking) BgmPlayer.MasterVolume = GameSettings.Music;     // never leave the music ducked
            LockOn.Signal -= Raise;
            CitadelWeakPoints.Broke -= OnWeakBroke;
            if (_level) { _level.BeatStarted -= OnBeat; _level.Radio -= OnBark; }
            if (Current == this) Current = null;
            if (_jevCalls + _localPicks > 0) Debug.Log($"[OYM-RADIO] session: {_jevPicks} Jev picks / {_jevCalls} calls, {_localPicks} local picks");
        }

        void Hook()
        {
            _hooked = true;
            _e.EnemyGone += (en, byPlayer) => { if (byPlayer) Raise("kill"); };
            LockOn.Signal += Raise;
            CitadelWeakPoints.Broke += OnWeakBroke;
        }

        public void Raise(string tag)
        {
            _events[tag] = Time.unscaledTime;
            if (U(tag) >= UrgentAt) _urgent = true;
        }

        static readonly HashSet<string> Story = new HashSet<string>
            { "victory", "boss_live", "weak_hint", "weak_break", "weak_last", "open", "contact", "clear_first", "down", "up" };
        readonly List<string> _keys = new List<string>();

        void KeepStory(float dt)
        {
            _keys.Clear();
            foreach (var k in _events.Keys) if (Story.Contains(k)) _keys.Add(k);
            foreach (var k in _keys) _events[k] += dt;
        }

        float Since(string tag) => _events.TryGetValue(tag, out var t) ? Time.unscaledTime - t : 1e9f;

        void OnBeat(HuskLevel.Beat b)
        {
            if ((b == HuskLevel.Beat.Contact || b == HuskLevel.Beat.Route) && !_contact) { _contact = true; Raise("contact"); }
            if (b == HuskLevel.Beat.Victory) Raise("victory");
        }

        void OnBark(string speaker, string line)
        {
            // "…too quiet" is text only and the CITADEL entrance follows 2.6 s later: answer it right away, no hold
            if (_e && _e.config && line == _e.config.barkQuiet) { Raise("quiet"); return; }
            _holdUntil = Time.unscaledTime + barkHold;
        }

        void OnWeakBroke(CitadelWeakPoint p, int left)
        {
            Raise("weak_break");
            if (left == 1) Raise("weak_last");
        }

        ZeroGFlight Partner()
        {
            var d = _e ? _e.director : null;
            var me = _e ? _e.Player : null;
            if (!d || d.controllers == null) return null;
            foreach (var c in d.controllers) if (c && c.transform != me) return c;
            return null;
        }

        // ------------------------------------------------------------------ loop
        /// the score dips under a radio line (fast down, slow back up) and returns exactly to the player's music setting
        void Duck()
        {
            float want = _src && _src.isPlaying ? musicDuck : 1f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _duck = Mathf.MoveTowards(_duck, want, dt / (want < _duck ? 0.15f : 0.7f));
            if (_duck >= 0.999f && !_ducking) return;
            if (!_ducking) { _ducking = true; _musicSetting = GameSettings.Music; }
            BgmPlayer.MasterVolume = _musicSetting * _duck;                 // BossMusic follows BgmPlayer's master too
            if (_duck >= 0.999f) { BgmPlayer.MasterVolume = _musicSetting; _ducking = false; }
        }

        void Update()
        {
            Duck();
            if (!_e) { _e = HuskEncounter.Current; if (!_e) return; }
            if (!_hooked) Hook();
            if (!_level && _e.Level) { _level = _e.Level; _level.BeatStarted += OnBeat; _level.Radio += OnBark; }
            float now = Time.unscaledTime;
            bool view = SignalInstall.PlayerView(_e);
            if (view) Watch(now);
            // the radio can't talk over a cinematic or a scripted bark: story events wait it out instead of expiring
            // (victory, the boss, a broken light...); tactical ones (behind you, fuse...) age normally - stale is wrong
            if (!view || now < _holdUntil) KeepStory(Time.unscaledDeltaTime);
            if (_asking || now < _busyUntil || now < _holdUntil || !view) return;
            if (_urgent || now >= _nextDecision)
            {
                _urgent = false;
                _nextDecision = now + decisionInterval;
                Decide(now);
            }
        }

        void Watch(float now)
        {
            if (!_opened)
            {
                if (_startT < 0f) _startT = now;
                if (now - _startT > 1.2f) { _opened = true; Raise("open"); }
            }
            var cam = _e.FlightCam ? _e.FlightCam.transform : null;
            Vector3 chest = _e.PlayerChest;
            Vector3 fwd = cam ? cam.forward : (_e.Player ? _e.Player.forward : Vector3.forward);
            int awake = 0, prism = 0;
            bool behind = false, above = false, fuse = false, prismNear = false;
            float nearest = 1e9f;
            foreach (var en in _e.Enemies)
            {
                if (!en) continue;
                if (!en.Alive || !en.IsAwake) { _charging.Remove(en); continue; }
                if (!_wokeAt.ContainsKey(en)) _wokeAt[en] = now;
                awake++;
                if (en.kind == HuskEnemy.Kind.Prism) prism++;
                Vector3 d = en.transform.position - chest;
                float dist = d.magnitude;
                nearest = Mathf.Min(nearest, dist);
                Vector3 dn = dist > 0.01f ? d / dist : fwd;
                if (dist < 28f && Vector3.Dot(dn, fwd) < -0.35f) behind = true;
                if (dist < 45f && d.y > 10f && Vector3.Dot(dn, fwd) < 0.5f) above = true;
                if (en.Latched) fuse = true;
                if (en.kind == HuskEnemy.Kind.Prism)
                {
                    if (dist < 55f) prismNear = true;
                    bool charging = en.Charging && dist < 110f;
                    if (charging) { if (_charging.Add(en)) Raise("prism_charge"); }
                    else if (_charging.Remove(en)) Raise("prism_fired");
                }
            }
            if (behind && !_behind) Raise("behind");
            if (above && !_above) Raise("above");
            if (fuse && !_fuse) Raise("wick_fuse");
            if (prismNear && !_prismNear) Raise("prism_near");
            _behind = behind; _above = above; _fuse = fuse; _prismNear = prismNear;

            int fresh = 0;
            foreach (var kv in _wokeAt) if (kv.Key && kv.Key.kind == HuskEnemy.Kind.Wick && now - kv.Value < 6f) fresh++;
            if (fresh >= 3 && Since("pack") > 20f) Raise("pack");
            if (prism >= 2 && _lastPrism < 2) Raise("snipers");
            if (awake > 0 && _fightSince < 0f) _fightSince = now;
            if (awake == 1 && _lastAwake >= 2) Raise("last_one");
            if (awake == 0 && _lastAwake > 0 && _fightSince >= 0f)
            {
                if (now - _fightSince > 6f) { Raise("clear"); if (!_firstClear) { _firstClear = true; Raise("clear_first"); } }
                _fightSince = -1f;
            }
            _lastAwake = awake; _lastPrism = prism;

            bool salvo = false;
            var darts = _e.Darts;
            if (darts != null) foreach (var dd in darts) if (dd && dd.Busy) { salvo = true; break; }
            if (salvo && !_salvo) Raise("salvo");
            _salvo = salvo;

            float hp = _e.PlayerHP01;
            bool alive = _e.PlayerAlive;
            if (!alive && _alive) Raise("down");
            if (alive && !_alive) { Raise("up"); if (nearest > 40f) Raise("up_calm"); }
            _alive = alive;
            if (alive && hp <= 0.25f && !_crit) { _crit = true; Raise("hp_crit"); }
            if (hp > 0.5f) _crit = false;
            if (hp <= 0.4f) _wasLow = true;
            if (alive && _wasLow && hp >= 0.9f) { _wasLow = false; Raise("hp_ok"); }

            var partner = Partner();
            if (partner && _e.Player)
            {
                float pd = Vector3.Distance(partner.transform.position, _e.Player.position);
                if (!_separated && pd > 150f) { _separated = true; Raise("separated"); }
                else if (_separated && pd < 30f) { _separated = false; Raise("regroup"); }
            }

            bool bossBeat = _level && _level.beat == HuskLevel.Beat.Boss;
            if (bossBeat && !_bossLive) { _bossLive = true; Raise("boss_live"); Raise("weak_hint"); _weakHintT = now; }
            var wp = CitadelWeakPoints.Current;
            if (bossBeat && wp && wp.Left > 0 && now - _weakHintT > 40f && Since("weak_break") > 30f) { _weakHintT = now; Raise("weak_hint"); }
        }

        // ------------------------------------------------------------------ decide
        void Decide(float now)
        {
            if (_cat == null || _cat.Lines.Count == 0) return;
            var current = new List<(string tag, float t)>();
            foreach (var kv in _events) if (now - kv.Value <= Window(kv.Key)) current.Add((kv.Key, kv.Value));
            if (current.Count == 0) return;
            current.Sort((a, b) => b.t.CompareTo(a.t));
            string role = SignalInstall.RoleOf(_e.PlayerFlight);
            bool partnerOrbit = Partner() is OrbitFrameController;
            var cands = new List<RadioLine>();
            foreach (var l in _cat.Lines)
            {
                if (l.role != "any" && l.role != role) continue;
                if (l.partner == "orbit" && !partnerOrbit) continue;
                if (l.id == _lastId) continue;
                if (_said.TryGetValue(l.id, out var t) && now - t < Mathf.Max(l.cooldown, 25f)) continue;
                foreach (var c in current) if (l.Has(c.tag)) { cands.Add(l); break; }
            }
            if (cands.Count == 0) return;
            if (useJev && JevLink.HasKey && now >= _jevBackoff) AskJev(cands, current, now);
            else { _localPicks++; Play(PickLocal(cands, current), "local"); }
        }

        RadioLine PickLocal(List<RadioLine> cands, List<(string tag, float t)> current)
        {
            float now = Time.unscaledTime;
            RadioLine best = null;
            float bs = float.MinValue;
            foreach (var l in cands)
            {
                float s = float.MinValue;
                foreach (var c in current) if (l.Has(c.tag)) s = Mathf.Max(s, U(c.tag) - (now - c.t) * 1.5f);
                s += l.priority * 4f + Random.value * 2f;
                if (s > bs) { bs = s; best = l; }
            }
            return best;
        }

        void AskJev(List<RadioLine> cands, List<(string tag, float t)> current, float now)
        {
            _asking = true;
            int gen = ++_gen;
            _jevCalls++;
            var crit = new Dictionary<string, string>();
            foreach (var l in cands)
                crit[l.id] = $"\"{l.english}\" ({l.korean}). Speaker: {(l.Jev ? "JEV, the AI mediator" : "the partner pilot ORBIT FRAME")}. Use when: {l.usage}";
            crit["none"] = "Nothing fits the newest event, or a line would repeat what was just said. Stay silent.";
            var fallback = PickLocal(cands, current);
            StartCoroutine(JevLink.Choose(State(current, now), Instructions, crit, jevTimeout, r =>
            {
                _asking = false;
                if (!this || gen != _gen) return;
                float late = Time.unscaledTime - now;
                if (!r.ok)
                {
                    bool hard = r.code == 401 || r.code == 402 || r.code == 403 || r.code == 404;
                    _jevBackoff = Time.unscaledTime + (hard ? 600f : 30f);
                    Debug.LogWarning($"[OYM-RADIO] Jev failed ({r.error}); local picks for {(hard ? 600 : 30)} s");
                    if (late < 2.5f) { _localPicks++; Play(fallback, "local (Jev failed)"); }
                    return;
                }
                if (r.choice == "none") { Debug.Log($"[OYM-RADIO] Jev: none ({r.seconds * 1000f:F0} ms)"); return; }
                if (!_cat.ById.TryGetValue(r.choice, out var line) || !cands.Contains(line))
                {
                    Debug.LogWarning("[OYM-RADIO] Jev picked an id outside the candidates: " + r.choice);
                    return;
                }
                if (late > 4f) return;                                       // the moment has passed
                _jevPicks++;
                Play(line, $"Jev {r.confidence:0.00}, {r.seconds * 1000f:F0} ms");
            }));
        }

        string State(List<(string tag, float t)> current, float now)
        {
            var sb = new StringBuilder(600);
            var me = _e.PlayerFlight;
            var partner = Partner();
            sb.Append("Fighting pilot: ").Append(SignalInstall.NameOf(me)).Append(me is KestrelController ? " (Sword, blade)" : " (Railgun)");
            sb.Append($". Health {Mathf.RoundToInt(_e.PlayerHP01 * 100f)}%").Append(_e.PlayerAlive ? "" : ", DOWN").Append(".\n");
            if (partner && _e.Player)
                sb.Append("Partner pilot: ").Append(SignalInstall.NameOf(partner)).Append($", {Vector3.Distance(partner.transform.position, _e.Player.position):F0} m away, holding back.\n");
            int wick = 0, prism = 0, charging = 0, latched = 0;
            foreach (var en in _e.Enemies)
            {
                if (!en || !en.Alive || !en.IsAwake) continue;
                if (en.kind == HuskEnemy.Kind.Wick) { wick++; if (en.Latched) latched++; }
                else { prism++; if (en.Charging) charging++; }
            }
            sb.Append($"Awake HUSK: {wick} WICK ({latched} fusing on the pilot), {prism} PRISM ({charging} charging a lance).\n");
            if (_level)
            {
                sb.Append("Stage: ").Append(_level.beat);
                var wp = CitadelWeakPoints.Current;
                if (_level.beat == HuskLevel.Beat.Boss && wp) sb.Append($", CITADEL glowing weak points left {wp.Left}/{wp.Total}");
                sb.Append(".\n");
            }
            var lk = LockOn.Current;
            sb.Append("Lock-on: ").Append(lk && lk.Locked ? lk.Describe() : "none").Append(".\n");
            sb.Append("Events, newest first: ");
            for (int i = 0; i < current.Count && i < 6; i++) sb.Append(i > 0 ? ", " : "").Append(current[i].tag).Append($" ({now - current[i].t:F1} s ago)");
            sb.Append(".\nRecently said: ");
            int n = 0;
            foreach (var kv in _said) if (now - kv.Value < 60f) { sb.Append(n++ > 0 ? ", " : "").Append(kv.Key).Append($" ({now - kv.Value:F0} s ago)"); }
            if (n == 0) sb.Append("nothing");
            sb.Append('.');
            return sb.ToString();
        }

        void Play(RadioLine l, string source)
        {
            if (l == null || !this) return;
            float now = Time.unscaledTime;
            if (now < _holdUntil && l.priority < 3) return;                  // a scripted bark started meanwhile
            var clip = l.Clip;
            _said[l.id] = now;
            _lastId = l.id;
            if (clip) { _src.Stop(); _src.clip = clip; _src.volume = volume; _src.Play(); }
            float len = clip ? clip.length : Mathf.Max(1.5f, l.seconds);
            _busyUntil = now + len + gap;
            foreach (var e in l.events) _events.Remove(e);                   // these events are answered
            var partner = Partner();
            string call = l.Jev ? "JEV" : SignalInstall.NameOf(partner);
            Color accent = l.Jev ? SignalInstall.JevAccent : SignalInstall.AccentOf(partner);
            if (_panel) _panel.Show(call, accent, l.korean, l.english, len, _src);
            Debug.Log($"[OYM-RADIO] {call} [{l.id}] via {source}: {l.english}");
            Spoke?.Invoke(l);
        }
    }
}
