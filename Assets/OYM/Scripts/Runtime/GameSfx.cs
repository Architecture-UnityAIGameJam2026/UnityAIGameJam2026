using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// In-game sound layer (user 11-19: "인게임에서 레일건이나 효과음이 안들리는거같아"). The game scene had no weapon, hit or
    /// enemy sounds at all - only the E salvo, the radio, the BGM and the boss music. The gameplay scripts are edited by
    /// other sessions, so this layer only listens (public events, public state polled every frame) and nothing calls it.
    /// It installs itself in the showcase (ShowcaseScene.Name) when it has pilots or a HUSK encounter. Team repo: not in the
    /// co-op Arena, whose gameplay already plays the team's own sounds (_Project/Scripts/Audio).
    ///   pilots   every ZeroGFlight; the one FlightCamera.Main follows is "mine" (flat 2D), the others are positional
    ///     ORBIT FRAME  LMB: coil whine while the RailgunFX charges (started so its swell tops out on the shot), the shot
    ///                  (RailgunFX.Shot), a dull crack where the beam hits rock
    ///     KESTREL      blade whoosh each time the cut is drawn (BladeTrail.emitting) - Shift strike = dash burst + whoosh
    ///     both         dash ignition (Dashing, or the co-op puppet's dash count), cruise boost (Cruising), rock bump
    ///                  (the cruise ends with the speed gone in one frame)
    ///   hits     an HP drop on a HUSK, a CITADEL launcher pod or weak point: sword hit when a blade was out right next to
    ///            it, armour hit otherwise. My own hits are mostly flat, so a railgun hit at 300 m is still heard. (HP
    ///            works on the co-op guest too, where the host applies the damage.)
    ///   HUSK     PRISM charge (pitched to span its chargeTime) + lance (PrismAttackFX.Shot), WICK fuse beeps (pitched so
    ///            the last beep is the blast) + blast, kills (alive -> dead without its own blast)
    ///   CITADEL  darts leaving the launcher pods (HomingDartFX.Busy) and their impacts, pods / weak points breaking
    ///   player   damage (HuskEncounter.PlayerDamaged), downed alarm, back-up chime
    ///   lock-on  two-tone blip on a manual lock / relock, a faint tick on an auto lock (LockOn.Current.Hard)
    /// Clips: Resources/OYM_GameSfx (+ the title's OYM_TitleSfx and the salvo's OYM_Salvo) - the user's picks from the
    /// game sound collection for the team's AudioEffect set (pipeline/audio/gamesfx/copy_clips.py). Logs one
    /// [OYM-GameSfx] line at install, an audio check 3 s in, and a tally of what played every 60 s and when the scene
    /// ends: read Editor.log instead of asking whether it was heard.
    public sealed class GameSfx : MonoBehaviour
    {
        const string Dir = "OYM_GameSfx/", TitleDir = "OYM_TitleSfx/", SalvoDir = "OYM_Salvo/";
        const int PoolSize = 24, HeldSize = 10, Rate = 44100;
        const float ScanEvery = 1f, ReportEvery = 60f;

        /// scales every sound of this layer (there is no SFX slider yet; OPTION > MASTER scales it through the listener)
        public static float Volume = 1f;
        public static GameSfx Instance { get; private set; }

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Volume = 1f; s_lock = s_tick = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (!Application.isPlaying || Application.isBatchMode || s.name != ShowcaseScene.Name || Instance) return;
            if (!FindAnyObjectByType<ZeroGFlight>(FindObjectsInactive.Include) && !FindAnyObjectByType<HuskEncounter>(FindObjectsInactive.Include)) return;
            var go = new GameObject("Game SFX");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<GameSfx>();
        }

        // ------------------------------------------------------------------------------------------------ state
        struct Held { public int slot, gen; }
        static readonly Held None = new Held { slot = -1 };

        sealed class Pilot
        {
            public ZeroGFlight f;
            public KestrelController blade;
            public OrbitFrameController rifle;
            public Net.CoopPuppet puppet;
            public readonly List<RailgunFX> guns = new List<RailgunFX>();
            public bool mine, dash, cruise, charge, cut, seen;
            public int dashSeq;
            public float speed, lastShot = -99f, lastCut = -99f, lastSwing = -99f;
        }

        sealed class Body
        {
            public float hp;
            public bool alive, charging, armed, blasted;
            public float shotAt = -99f;
            public Held fx = None;
        }

        AudioClip[] _rail, _swing, _slash, _hit, _dartBlast;
        AudioClip _charge, _dash, _boost, _rock, _kill, _death, _fuse, _blast, _lance, _hurt, _downed, _revived, _pop;
        static AudioClip s_lock, s_tick;
        readonly int[] _last = { -1, -1, -1, -1, -1 };
        readonly List<string> _missing = new List<string>();

        readonly AudioSource[] _pool = new AudioSource[PoolSize];
        readonly AudioSource[] _held = new AudioSource[HeldSize];
        readonly Transform[] _heldTarget = new Transform[HeldSize];
        readonly int[] _heldGen = new int[HeldSize];
        readonly float[] _heldFade = new float[HeldSize];
        int _next, _nextHeld;

        readonly List<Pilot> _pilots = new List<Pilot>();
        readonly Dictionary<HuskEnemy, Body> _husk = new Dictionary<HuskEnemy, Body>();
        readonly Dictionary<HuskBossPart, Body> _pods = new Dictionary<HuskBossPart, Body>();
        readonly Dictionary<CitadelWeakPoint, Body> _weak = new Dictionary<CitadelWeakPoint, Body>();
        readonly Dictionary<HomingDartFX, bool> _darts = new Dictionary<HomingDartFX, bool>();
        readonly List<System.Action> _unhook = new List<System.Action>();
        static readonly Collider[] s_cols = new Collider[16];

        HuskEncounter _enc;
        Component _lockOwner;
        Held _downHeld = None;
        bool _down, _hpSeen, _checked, _dirty;
        float _t0, _nextScan, _nextReport, _lastHurt = -99f;
        int _hitsThisFrame;
        readonly Dictionary<string, int> _count = new Dictionary<string, int>();

        // ------------------------------------------------------------------------------------------------ setup
        void Awake()
        {
            Instance = this;
            for (int i = 0; i < PoolSize; i++) _pool[i] = NewSource("voice " + i);
            for (int i = 0; i < HeldSize; i++) _held[i] = NewSource("held " + i);

            _rail = Set(TitleDir, "railgun_1", "railgun_2");
            _swing = Set(TitleDir, "sword_swing_1", "sword_swing_2", "sword_swing_3");
            _slash = Set(TitleDir, "sword_hit_1", "sword_hit_2", "sword_hit_3");
            _hit = Set(Dir, "enemy_hit_1", "enemy_hit_2");
            _dartBlast = Set(SalvoDir, "salvo_impact_a", "salvo_impact_b");
            _dash = L(TitleDir + "jet_ignite"); _boost = L(TitleDir + "boost");
            _charge = L(Dir + "rail_charge"); _rock = L(Dir + "rock_hit");
            _kill = L(Dir + "kill_shatter"); _death = L(Dir + "enemy_death");
            _fuse = L(Dir + "wick_fuse"); _blast = L(Dir + "wick_blast"); _lance = L(Dir + "prism_lance");
            _hurt = L(Dir + "player_hit"); _downed = L(Dir + "downed"); _revived = L(Dir + "revived");
            _pop = L(SalvoDir + "salvo_pop");
            if (!s_lock) s_lock = Synth("lock", 0.12f, t => Blip(t, 0f, 0.045f, 1500f, 0.3f) + Blip(t, 0.06f, 0.05f, 2000f, 0.3f));
            if (!s_tick) s_tick = Synth("tick", 0.04f, t => Blip(t, 0f, 0.03f, 1700f, 0.22f));
            _t0 = Time.unscaledTime;
            _nextReport = _t0 + ReportEvery;
        }

        void Start()
        {
            Scan();
            string mine = "-";
            var fc = FlightCamera.Main;
            foreach (var p in _pilots) if (fc && fc.target == p.f.transform) mine = p.f.name;
            Debug.Log($"[OYM-GameSfx] ready in {gameObject.scene.name}: pilots {_pilots.Count} (mine {mine}), HUSK encounter {(_enc ? "yes" : "no")}, clips " +
                      (_missing.Count == 0 ? "ok" : "MISSING " + string.Join(", ", _missing)));
        }

        void OnDestroy()
        {
            foreach (var u in _unhook) { try { u(); } catch (System.Exception) { } }
            _unhook.Clear();
            if (_enc) _enc.PlayerDamaged -= OnHurt;
            if (_count.Count > 0) Report("scene end");
            if (Instance == this) Instance = null;
        }

        AudioSource NewSource(string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var s = g.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            return s;
        }

        AudioClip L(string path)
        {
            var c = Resources.Load<AudioClip>(path);
            if (!c) { _missing.Add(path); return null; }
            if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();     // no hitch on the first shot
            return c;
        }

        AudioClip[] Set(string dir, params string[] names)
        {
            var list = new List<AudioClip>(names.Length);
            foreach (var n in names) { var c = L(dir + n); if (c) list.Add(c); }
            return list.ToArray();
        }

        /// a variation, never the same one twice in a row
        AudioClip Pick(AudioClip[] set, int slot)
        {
            int n = set != null ? set.Length : 0;
            if (n == 0) return null;
            int i = Random.Range(0, n);
            if (n > 1 && i == _last[slot]) i = (i + 1 + Random.Range(0, n - 1)) % n;
            _last[slot] = i;
            return set[i];
        }

        // ------------------------------------------------------------------------------------------------ voices
        AudioSource Voice()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                int k = (_next + i) % PoolSize;
                if (!_pool[k].isPlaying) { _next = (k + 1) % PoolSize; return _pool[k]; }
            }
            var s = _pool[_next];                                              // all busy: the next in line gives way
            _next = (_next + 1) % PoolSize;
            return s;
        }

        static void Shape(AudioSource s, AudioClip c, float vol, float pitch, float spatial, float minDist, int priority)
        {
            s.clip = c;
            s.volume = Mathf.Clamp01(vol * Volume);
            s.pitch = pitch;
            s.spatialBlend = spatial;
            s.minDistance = minDist;
            s.maxDistance = Mathf.Max(500f, minDist * 30f);
            s.priority = priority;
        }

        /// one-shot at `at`. spatial 0 = flat (my pilot), 1 = fully positional; minDist = full volume inside, 1/d beyond.
        void Play(string tag, AudioClip c, Vector3 at, float vol, float pitch, float spatial, float minDist, int priority)
        {
            if (!c || vol <= 0f) return;
            var s = Voice();
            s.Stop();
            s.transform.position = at;
            Shape(s, c, vol, pitch, spatial, minDist, priority);
            s.Play();
            Count(tag);
        }

        /// a sound that rides on `target` (null = stays put) and can be cut short with Release
        Held PlayHeld(string tag, AudioClip c, Transform target, float vol, float pitch, float spatial, float minDist)
        {
            if (!c) return None;
            int k = -1;
            for (int i = 0; i < HeldSize; i++) { int j = (_nextHeld + i) % HeldSize; if (!_held[j].isPlaying) { k = j; break; } }
            if (k < 0) k = _nextHeld;
            _nextHeld = (k + 1) % HeldSize;
            var s = _held[k];
            s.Stop();
            _heldGen[k]++; _heldFade[k] = 0f; _heldTarget[k] = target;
            if (target) s.transform.position = target.position;
            Shape(s, c, vol, pitch, spatial, minDist, 64);
            s.Play();
            Count(tag);
            return new Held { slot = k, gen = _heldGen[k] };
        }

        void Release(ref Held h, float fade)
        {
            int k = h.slot, gen = h.gen;
            h = None;
            if (k < 0 || _heldGen[k] != gen || !_held[k].isPlaying) return;   // the slot went to another sound since
            if (fade <= 0f) { _held[k].Stop(); _heldTarget[k] = null; return; }
            _heldFade[k] = _held[k].volume / fade;                            // volume per second down to silence
        }

        bool Owns(in Held h) => h.slot >= 0 && _heldGen[h.slot] == h.gen;

        void Count(string tag)
        {
            if (tag == null) return;
            _count.TryGetValue(tag, out int n);
            _count[tag] = n + 1;
            _dirty = true;
        }

        // ------------------------------------------------------------------------------------------------ discovery
        void Scan()
        {
            foreach (var f in FindObjectsByType<ZeroGFlight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool known = false;
                foreach (var p in _pilots) if (p.f == f) { known = true; break; }
                if (!known) AddPilot(f);
            }
            _pilots.RemoveAll(p => !p.f);

            if (!_enc)
            {
                _enc = FindAnyObjectByType<HuskEncounter>();
                if (_enc) _enc.PlayerDamaged += OnHurt;
            }
            if (_enc)
            {
                foreach (var b in FindObjectsByType<HuskBossPart>(FindObjectsSortMode.None))
                    if (!_pods.ContainsKey(b)) _pods[b] = new Body { hp = b.Hp, alive = !b.Broken };
                foreach (var w in FindObjectsByType<CitadelWeakPoint>(FindObjectsSortMode.None))
                    if (!_weak.ContainsKey(w)) _weak[w] = new Body { hp = w.Hp, alive = !w.Broken };
            }
            Prune(_husk); Prune(_pods); Prune(_weak);
            if (_darts.Count > 0)
            {
                var dead = new List<HomingDartFX>();
                foreach (var d in _darts.Keys) if (!d) dead.Add(d);
                foreach (var d in dead) _darts.Remove(d);
            }
        }

        static void Prune<T>(Dictionary<T, Body> map) where T : Object
        {
            if (map.Count == 0) return;
            List<T> dead = null;
            foreach (var k in map.Keys) if (!k) (dead ??= new List<T>()).Add(k);
            if (dead != null) foreach (var k in dead) map.Remove(k);
        }

        void AddPilot(ZeroGFlight f)
        {
            var p = new Pilot { f = f, blade = f as KestrelController, rifle = f as OrbitFrameController };
            if (p.rifle)
                foreach (var g in new[] { p.rifle.railgun, p.rifle.fpRailgun })
                {
                    if (!g || p.guns.Contains(g)) continue;
                    p.guns.Add(g);
                    var gun = g;
                    System.Action<Vector3, Vector3> h = (muzzle, end) => OnRail(p, gun, muzzle, end);
                    gun.Shot += h;
                    _unhook.Add(() => { if (gun) gun.Shot -= h; });
                }
            _pilots.Add(p);
        }

        // ------------------------------------------------------------------------------------------------ frame
        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan) { _nextScan = now + ScanEvery; Scan(); }
            _hitsThisFrame = 0;

            var fc = FlightCamera.Main;
            Transform mine = fc ? fc.target : null;
            foreach (var p in _pilots) if (p.f) TickPilot(p, mine, now);

            if (_enc)
            {
                TickHusk(now);
                TickDarts();
                TickPlayer();
            }
            TickBoss(_pods, now);
            TickBoss(_weak, now);
            TickLock();

            if (!_checked && now - _t0 > 3f) { _checked = true; Check(); }
            if (now >= _nextReport) { _nextReport = now + ReportEvery; if (_dirty) Report("last 60 s"); }
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            for (int k = 0; k < HeldSize; k++)
            {
                var s = _held[k];
                if (!s.isPlaying) { _heldFade[k] = 0f; continue; }
                var t = _heldTarget[k];
                if (t) s.transform.position = t.position;
                if (_heldFade[k] > 0f)
                {
                    s.volume -= _heldFade[k] * dt;
                    if (s.volume <= 0.001f) { s.Stop(); _heldFade[k] = 0f; _heldTarget[k] = null; }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ pilots
        void PilotSound(string tag, Pilot p, AudioClip c, float vol, float pitch)
        {
            Vector3 at = p.f.transform.position + Vector3.up;
            if (p.mine) Play(tag, c, at, vol, pitch, 0f, 1f, 40);
            else Play(tag, c, at, vol, pitch, 0.85f, 12f, 100);
        }

        void TickPilot(Pilot p, Transform mine, float now)
        {
            var f = p.f;
            p.mine = mine && f.transform == mine;
            if (!p.puppet && !f.enabled) p.puppet = f.GetComponent<Net.CoopPuppet>();     // the co-op partner: no controller
            bool live = f.isActiveAndEnabled;

            bool dash = false, cruise = false, newDash;
            if (p.puppet && p.puppet.HasState)
            {
                var st = p.puppet.State;
                dash = st.Has(Net.PilotState.FlagDash);
                cruise = st.Has(Net.PilotState.FlagCruise);
                newDash = p.seen && st.dashSeq != p.dashSeq;
                p.dashSeq = st.dashSeq;
            }
            else
            {
                if (live) { dash = f.Dashing; cruise = f.Cruising; }
                newDash = dash && !p.dash;
            }
            float speed = f.Velocity.magnitude;
            if (p.seen)
            {
                if (newDash) PilotSound("dash", p, _dash, p.mine ? 0.55f : 0.6f, Random.Range(0.96f, 1.05f));
                if (cruise && !p.cruise) PilotSound("boost", p, _boost, p.mine ? 0.6f : 0.65f, 1f);
                // the cruise ended against rock: the speed is gone in one frame (a release / a brake bleeds it off)
                if (!cruise && p.cruise && live && p.speed > 12f && speed < p.speed * 0.6f) PilotSound("bump", p, _rock, 0.75f, Random.Range(0.95f, 1.05f));
            }
            p.dash = dash; p.cruise = cruise; p.speed = speed;

            if (p.rifle)
            {
                RailgunFX charging = null;
                foreach (var g in p.guns) if (g && g.isActiveAndEnabled && g.Charging) { charging = g; break; }
                if (charging && !p.charge && p.seen) Whine(p, charging);
                p.charge = charging;
            }
            if (p.blade)
            {
                bool cut = On(p.blade.trail) || On(p.blade.fpTrail);
                if (cut && !p.cut && p.seen && now - p.lastSwing > 0.08f)
                {
                    p.lastSwing = now;
                    PilotSound("swing", p, Pick(_swing, 2), p.mine ? 0.7f : 0.75f, Random.Range(0.95f, 1.08f));
                }
                p.cut = cut;
                if (cut || (live && (p.blade.Attacking || p.blade.Striking))) p.lastCut = now;
            }
            p.seen = true;
        }

        static bool On(BladeTrail t) => t && t.emitting;

        /// the coil whine: the clip swells to its top ~0.95 s in; pitched up and started late so the top lands on the shot
        void Whine(Pilot p, RailgunFX g)
        {
            if (!_charge) return;
            const float pitch = 1.8f;
            float start = Mathf.Clamp(0.95f - g.chargeTime * pitch, 0f, _charge.length - 0.1f);
            var h = PlayHeld("charge", _charge, p.f.transform, p.mine ? 0.4f : 0.5f, pitch, p.mine ? 0f : 0.85f, p.mine ? 1f : 10f);
            if (h.slot >= 0) _held[h.slot].time = start;
        }

        void OnRail(Pilot p, RailgunFX gun, Vector3 muzzle, Vector3 end)
        {
            p.lastShot = Time.unscaledTime;
            var c = Pick(_rail, 0);
            float pitch = Random.Range(0.97f, 1.03f);
            if (p.mine) Play("rail", c, muzzle, 0.95f, pitch, 0f, 1f, 24);
            else Play("rail", c, muzzle, 0.95f, pitch, 0.85f, 15f, 64);

            // where the beam ends: rock gets a dull crack here; HUSK / CITADEL hits are heard through their HP (Hit)
            int n = Physics.OverlapSphereNonAlloc(end, 0.6f, s_cols, ~0, QueryTriggerInteraction.Ignore);
            bool solid = false, target = false;
            for (int i = 0; i < n; i++)
            {
                var col = s_cols[i];
                if (!col || col.transform.IsChildOf(p.f.transform)) continue;
                solid = true;
                if (col.GetComponentInParent<ZeroGTarget>()) { target = true; break; }
            }
            if (solid && !target) Play("rail_rock", Pick(_hit, 1), end, p.mine ? 0.55f : 0.5f, Random.Range(0.6f, 0.7f), p.mine ? 0.6f : 1f, 25f, 140);
        }

        // ------------------------------------------------------------------------------------------------ hits / kills
        /// an HP drop at `pos`: a blade out within reach -> sword hit; a rifle that just fired -> its hit; else a
        /// positional armour hit (blasts, missiles). My own hits stay mostly flat.
        void Hit(Vector3 pos, bool heavy, float now)
        {
            if (_hitsThisFrame >= 3) return;                                   // a blast through a pack: three is plenty
            _hitsThisFrame++;
            Pilot by = null;
            bool blade = false;
            foreach (var p in _pilots)
                if (p.f && p.blade && now - p.lastCut < 0.4f && (p.f.transform.position - pos).sqrMagnitude < 144f) { by = p; blade = true; break; }
            if (by == null)
                foreach (var p in _pilots)
                    if (p.f && p.rifle && now - p.lastShot < 0.3f && (by == null || p.mine)) by = p;
            float pitch = (heavy ? 0.82f : 1f) * Random.Range(0.95f, 1.06f);
            if (blade) Play("hit_blade", Pick(_slash, 3), pos, 0.9f, pitch, by.mine ? 0.2f : 0.85f, 12f, 40);
            else if (by != null && by.mine) Play("hit_rail", Pick(_hit, 1), pos, 0.85f, pitch, 0.25f, 30f, 40);
            else Play("hit", Pick(_hit, 1), pos, 0.8f, pitch, 0.85f, 20f, 110);
        }

        void Kill(Vector3 pos, bool prism)
        {
            Play("kill", _kill, pos, 1f, prism ? Random.Range(0.9f, 0.97f) : Random.Range(1.05f, 1.15f), 0.55f, 25f, 56);
            if (prism) Play(null, _death, pos, 0.45f, Random.Range(0.95f, 1.05f), 0.7f, 20f, 120);
        }

        void Break(Vector3 pos)
        {
            Play("break", _kill, pos, 1f, Random.Range(0.68f, 0.76f), 0.4f, 60f, 32);
            Play(null, _blast, pos, 0.85f, Random.Range(0.72f, 0.8f), 0.4f, 60f, 40);
            Play(null, _death, pos, 0.5f, 0.8f, 0.5f, 60f, 100);
        }

        void TickHusk(float now)
        {
            var list = _enc.Enemies;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (!e) continue;
                if (!_husk.TryGetValue(e, out var b))
                {
                    b = new Body { hp = e.HP, alive = e.Alive, charging = e.Charging, armed = e.wick && e.wick.Armed, blasted = e.wick && e.wick.Blasted };
                    _husk[e] = b;
                    if (e.prism)
                    {
                        var pr = e.prism; var body = b;
                        System.Action<Vector3, Vector3, Vector3> h = (lens, end, n) => OnLance(body, lens);
                        pr.Shot += h;
                        _unhook.Add(() => { if (pr) pr.Shot -= h; });
                    }
                    continue;
                }
                Vector3 pos = e.transform.position;

                float hp = e.HP;
                if (hp < b.hp - 0.01f && b.alive) Hit(pos, false, now);
                b.hp = hp;

                // PRISM: the charge is pitched so the clip spans it (and ends on the shot); a cancel fades it out
                bool ch = e.prism && e.Charging;
                if (ch && !b.charging && _charge)
                {
                    Release(ref b.fx, 0f);
                    float ct = Mathf.Max(0.3f, e.prism.chargeTime);
                    b.fx = PlayHeld("prism_charge", _charge, e.transform, 0.85f, Mathf.Clamp(_charge.length / ct, 0.5f, 1.2f), 0.8f, 25f);
                }
                else if (!ch && b.charging && now - b.shotAt > 0.15f && Owns(b.fx)) Release(ref b.fx, 0.12f);
                b.charging = ch;

                // WICK: fuse beeps pitched so the last one is the blast, then the blast
                if (e.wick)
                {
                    bool armed = e.wick.Armed, blasted = e.wick.Blasted;
                    if (armed && !b.armed && !blasted)
                    {
                        Release(ref b.fx, 0f);
                        b.fx = PlayHeld("wick_fuse", _fuse, e.transform, 0.75f, Mathf.Clamp(1.36f / Mathf.Max(0.3f, e.wick.fuseTime), 0.8f, 1.7f), 0.8f, 12f);
                    }
                    if (blasted && !b.blasted)
                    {
                        if (Owns(b.fx)) Release(ref b.fx, 0f);
                        Play("wick_blast", _blast, pos, 1f, Random.Range(0.94f, 1.06f), 0.75f, 18f, 48);
                    }
                    b.armed = armed; b.blasted = blasted;
                }

                bool alive = e.Alive;
                if (!alive && b.alive)
                {
                    if (Owns(b.fx)) Release(ref b.fx, 0.05f);
                    if (!b.blasted) Kill(pos, e.kind == HuskEnemy.Kind.Prism);
                }
                b.alive = alive;
            }
        }

        void OnLance(Body b, Vector3 lens)
        {
            b.shotAt = Time.unscaledTime;
            if (Owns(b.fx)) Release(ref b.fx, 0.03f);
            Play("prism_lance", _lance, lens, 0.95f, Random.Range(0.96f, 1.04f), 0.7f, 30f, 48);
        }

        void TickBoss<T>(Dictionary<T, Body> map, float now) where T : Component
        {
            if (map.Count == 0) return;
            foreach (var kv in map)
            {
                var c = kv.Key;
                if (!c) continue;
                var b = kv.Value;
                float hp; bool broken; Vector3 at;
                if (c is HuskBossPart pod) { hp = pod.Hp; broken = pod.Broken; at = pod.Center; }
                else if (c is CitadelWeakPoint w) { hp = w.Hp; broken = w.Broken; at = w.AimPoint; }
                else continue;
                if (hp < b.hp - 0.01f && b.alive && !broken) Hit(at, true, now);
                if (broken && b.alive) Break(at);
                b.hp = hp; b.alive = !broken;
            }
        }

        void TickDarts()
        {
            var ds = _enc.Darts;
            if (ds == null) return;
            for (int i = 0; i < ds.Count; i++)
            {
                var d = ds[i];
                if (!d) continue;
                bool busy = d.Busy;
                if (!_darts.TryGetValue(d, out bool was))
                {
                    var dart = d;
                    System.Action<Vector3, Vector3> h = (p, n) => Play("dart_blast", Pick(_dartBlast, 4), p, 0.95f, Random.Range(0.9f, 1.05f), 0.75f, 25f, 64);
                    dart.Impacted += h;
                    _unhook.Add(() => { if (dart) dart.Impacted -= h; });
                    was = false;
                }
                if (busy && !was) Play("dart_launch", _pop, d.Position(Mathf.Max(0f, d.Clock)), 0.75f, Random.Range(0.8f, 0.9f), 0.8f, 30f, 120);
                _darts[d] = busy;
            }
        }

        // ------------------------------------------------------------------------------------------------ player
        void OnHurt(float amount, Vector3 from)
        {
            float now = Time.unscaledTime;
            if (now - _lastHurt < 0.08f) return;
            _lastHurt = now;
            Play("hurt", _hurt, transform.position, Mathf.Clamp(0.6f + amount / 60f, 0.6f, 1f), Random.Range(0.94f, 1.04f), 0f, 1f, 16);
        }

        void TickPlayer()
        {
            float hp01 = _enc.PlayerHP01;
            if (!_hpSeen) { _hpSeen = hp01 > 0.5f; return; }                   // before the encounter fills the bar
            if (!_down && hp01 <= 0.001f)
            {
                _down = true;
                _downHeld = PlayHeld("downed", _downed, null, 0.5f, 1f, 0f, 1f);
            }
            else if (_down && hp01 >= 0.999f)
            {
                _down = false;
                if (Owns(_downHeld)) Release(ref _downHeld, 0.3f);
                Play("revived", _revived, transform.position, 0.6f, 1f, 0f, 1f, 30);
            }
        }

        void TickLock()
        {
            var lo = Signal.LockOn.Current;
            Component hard = lo && lo.Locked ? lo.Hard.owner : null;
            if (hard && hard != _lockOwner)
            {
                if (lo.AutoLocked) Play("lock_auto", s_tick, transform.position, 0.22f, 1f, 0f, 1f, 50);
                else Play("lock", s_lock, transform.position, 0.45f, 1f, 0f, 1f, 50);
            }
            _lockOwner = hard;
        }

        // ------------------------------------------------------------------------------------------------ reports
        void Check()
        {
            int listeners = 0;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled) listeners++;
            string line = $"[OYM-GameSfx] audio check: listeners {listeners}, listener volume {AudioListener.volume:F2}, pause {AudioListener.pause}, " +
                          $"bgm {(BgmPlayer.Current ? BgmPlayer.Current.name : "none")} muted {BgmPlayer.Muted} master {BgmPlayer.MasterVolume:F2}, pilots {_pilots.Count}";
            if (listeners == 0 || AudioListener.volume < 0.05f || AudioListener.pause) Debug.LogWarning(line + " - nothing will be heard");
            else Debug.Log(line);
        }

        void Report(string why)
        {
            var sb = new System.Text.StringBuilder("[OYM-GameSfx] played (").Append(why).Append("): ");
            bool first = true;
            foreach (var kv in _count)
            {
                if (!first) sb.Append(", ");
                sb.Append(kv.Key).Append(' ').Append(kv.Value);
                first = false;
            }
            Debug.Log(sb.ToString());
            _dirty = false;
        }

        // ------------------------------------------------------------------------------------------------ lock tones
        static AudioClip Synth(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Rate * seconds));
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var c = AudioClip.Create("gamesfx " + name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// a blip: 2 ms attack, 12 ms release, a little 3rd harmonic (electronic, not harsh)
        static float Blip(float t, float start, float len, float hz, float amp)
        {
            float x = t - start;
            if (x < 0f || x > len) return 0f;
            float env = Mathf.Min(1f, x / 0.002f) * Mathf.Min(1f, (len - x) / 0.012f);
            float ph = 2f * Mathf.PI * hz * x;
            return amp * env * (Mathf.Sin(ph) + 0.22f * Mathf.Sin(3f * ph));
        }
    }
}
