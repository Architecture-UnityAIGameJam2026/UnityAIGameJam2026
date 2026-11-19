using System.Collections.Generic;
using System.Reflection;
using OYM.Field;
using OYM.Net;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OYM.Flow
{
    /// Plays the scene's LevelFlowPlan (baked by OYMLevelFlow) on top of the live HUSK encounter: the zones of the level
    /// layout open one after another instead of all at once, reinforcement waves come in as a zone thins out, breathers
    /// sit between the sectors, and HuskLevel's CITADEL waits until the last sector (the nest on the crashed bow) is
    /// clear. Also: the objective line at the top of the screen (+ distance, the cruise hint on long legs), and the
    /// flow's own heal packs (the field kit's sit on the stepping stones only).
    /// Installs itself when the game scene loads (like HuskEncounter / FieldKit); without a plan in the scene it does
    /// nothing and the old flow runs. -oymNoFlow = off.
    /// Only HuskEncounter's public API is used, plus two private members of HuskLevel by reflection (Say for the radio,
    /// so co-op mirrors the lines; _bossQueued to hold the finale) - HuskLevel / HuskZones files are not edited.
    /// Co-op: the host runs the flow (its HUSK are mirrored to the guest as usual) and sends MsgState (sector, finale
    /// flags, its gimmick clock) at 2 Hz; the guest shows the same objective and runs the dodge gimmicks on the host's
    /// clock (FlowClock), so a debris band or a beam sweep is where the partner sees it. Heal packs: each PC checks its
    /// own pilot (the HP is its own) and tells the other (MsgMed), like the field kit's.
    public sealed class LevelFlow : MonoBehaviour
    {
        public static LevelFlow Current { get; private set; }

        /// the game scene of the flow: the local project's GameSession.GameScene, the team repo's ShowcaseScene.Name
        public const string SceneName = "Showcase_DebrisField";

        /// co-op wire (CoopMsg uses 1-61): H  u8 sector, u8 flags (1 = finished, 2 = finale released), f gimmick clock
        public const byte MsgState = 70;
        /// co-op wire: B  u8 heal pack of the flow taken (gone on both PCs until it respawns)
        public const byte MsgMed = 71;

        static readonly Color Ink = new Color32(0xF3, 0xEE, 0xDD, 255), Amber = new Color32(0xFF, 0xC0, 0x6A, 255),
                              HealInk = new Color32(0x8C, 0xF5, 0xA4, 255), Dim = new Color32(0xB9, 0xB4, 0xA6, 255);

        LevelFlowPlan _plan;
        HuskEncounter _e;
        FlowRunner _run;
        LiveWorld _world;
        bool _ready, _guest;
        float _waitT;

        CoopSession _session;
        float _sendT;
        int _sentSig = -1, _hostSector = -1, _hostFlags;
        bool _clockSynced;

        FlowMarker _marker;
        HuskZones _zonesCache;
        bool _zonesLooked;

        FieldKitConfig _kit;
        readonly List<HealPack> _packs = new List<HealPack>();
        HuskFXCanvas _fx;
        AudioSource _one;

        UIDocument _doc;
        VisualElement _objBox;
        Label _obj, _objDist, _hint, _toast;
        string _objKey;
        int _distShown = -1;
        float _objT = -1f, _objA, _hintT = -1f, _toastT = -1f;
        readonly HashSet<int> _hinted = new HashSet<int>();

        public FlowRunner Runner => _run;
        public LevelFlowPlan Plan => _plan;
        public IReadOnlyList<HealPack> Packs => _packs;
        /// the sector the players are sent to (host: its runner, guest: the host's last state; -1 = not yet known)
        public int Sector => _run != null ? _run.Current : _hostSector;

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; FlowClock.Offset = 0f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// another session's recorder / demo drives the scene (switch flags -oymNo... and the perf HUD are not automation)
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymPerfHud" && a != "-oymCineDemo" && a != "-oymHudDemo" && a != "-oymFlowTest") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != SceneName || Flag("-oymNoFlow") || OtherAutomation()) return;
            LevelFlowPlan plan = null;
            foreach (var root in s.GetRootGameObjects())
            {
                plan = root.GetComponentInChildren<LevelFlowPlan>(true);
                if (plan) break;
            }
            if (!plan || plan.sectors.Count == 0) { Debug.Log("[OYM-FLOW] no LevelFlowPlan in the scene (OYM/Level/Flow/Build): the old level flow runs"); return; }
            var go = new GameObject("Level Flow (runtime)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<LevelFlow>()._plan = plan;
        }

        void Awake() { Current = this; }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_world != null) _world.Dispose();
            if (_session) _session.Message -= OnCoop;
            if (_fx != null) { _fx.Destroy(); _fx = null; }
        }

        // ------------------------------------------------------------------------------------------------ start
        void Init()
        {
            _ready = true;
            _guest = _e.Mirror;
            if (!_guest)
            {
                _world = new LiveWorld(_e);
                _run = new FlowRunner(_plan, _world);
            }
            _session = CoopSession.InGame ? CoopSession.Active : null;
            if (_session) _session.Message += OnCoop;
            BuildPacks();
            BuildUI();
            _marker = gameObject.AddComponent<FlowMarker>();
            float total = _plan.estimateArrival + _plan.estimateBoss;
            var parts = new List<string>();
            foreach (var sp in _plan.sectors) { total += sp.estimate; parts.Add($"{sp.name} {sp.estimate:F0}s/{sp.waves.Count} waves"); }
            Debug.Log($"[OYM-FLOW] level flow {(_guest ? "(co-op guest: the host's state)" : "running")}: {string.Join(", ", parts)}; " +
                      $"{_packs.Count} heal packs of its own; design estimate {total / 60f:F1} min incl. arrival + CITADEL");
        }

        void Update()
        {
            if (!_e)
            {
                _e = HuskEncounter.Current;
                if (!_e)
                {
                    _waitT += Time.unscaledDeltaTime;
                    if (_waitT > 8f) { Debug.Log("[OYM-FLOW] no HUSK encounter in this scene (-oymNoHusk?): the flow stands down"); enabled = false; }
                    return;
                }
            }
            if (!_ready)
            {
                if (!_e.Mirror && !_e.Level) return;                    // HuskLevel comes up in the encounter's Start
                Init();
            }
            _run?.Tick();
            Coop();
            TickPacks(Time.deltaTime);
            Hud(Time.deltaTime);
            Waypoint();
        }

        void LateUpdate()
        {
            if (!_ready || _packs.Count == 0) return;
            var cam = FlightCamera.Main && FlightCamera.Main.Cam ? FlightCamera.Main.Cam.transform.position : Camera.main ? Camera.main.transform.position : Vector3.zero;
            bool fx = _fx != null && _fx.Valid;
            if (fx) _fx.Begin();
            float now = Time.time;
            foreach (var p in _packs) if (p) p.Draw(fx ? _fx : null, now, cam);
            if (fx) _fx.End();
        }

        // ------------------------------------------------------------------------------------------------ co-op
        void Coop()
        {
            if (!_session || !_session.Connected || !CoopSession.InGame) return;
            if (_guest || _run == null) return;
            int flags = (_run.Finished ? 1 : 0) | (_run.Released ? 2 : 0);
            int sig = _run.Current * 4 + flags;
            float now = Time.unscaledTime;
            if (sig == _sentSig && now < _sendT) return;
            _sentSig = sig; _sendT = now + 0.5f;
            _session.Send(new CoopMsg.W(MsgState).U8(_run.Current).U8(flags).F(FlowClock.Now).Done());
        }

        void OnCoop(CoopMsg.R r)
        {
            if (r.Type == MsgState && _guest)
            {
                _hostSector = r.U8(); _hostFlags = r.U8();
                float target = r.F() - Time.time;
                // the guest's gimmicks run on the host's clock: snap the first time / after a hitch, then glide
                if (!_clockSynced || Mathf.Abs(target - FlowClock.Offset) > 1f) { FlowClock.Offset = target; _clockSynced = true; }
                else FlowClock.Offset = Mathf.Lerp(FlowClock.Offset, target, 0.2f);
            }
            else if (r.Type == MsgMed) TakePack(r.U8(), false, 0f);
        }

        void SendMed(int i)
        {
            if (_session && _session.Connected && CoopSession.InGame) _session.Send(new CoopMsg.W(MsgMed).U8(i).Done());
        }

        // ------------------------------------------------------------------------------------------------ heal packs
        void BuildPacks()
        {
            _kit = Resources.Load<FieldKitConfig>("OYM_FieldKit");
            if (!_kit) { Debug.LogWarning("[OYM-FLOW] Resources/OYM_FieldKit missing: no heal packs of the flow"); return; }
            var mats = Hazard.Materials();
            if (mats != null) _fx = new HuskFXCanvas(transform, mats);
            for (int i = 0; i < _plan.healSpots.Count; i++)
                _packs.Add(HealPack.Build(_kit, transform, _plan.healSpots[i], 100 + i));
            _one = gameObject.AddComponent<AudioSource>();
            _one.playOnAwake = false; _one.spatialBlend = 0f;
        }

        void TickPacks(float dt)
        {
            if (_packs.Count == 0) return;
            float now = Time.time;
            var me = FlightCamera.Main ? FlightCamera.Main.target : null;
            bool view = SignalInstall.PlayerView(_e);
            bool can = me && view && _e.PlayerAlive && _e.PlayerHurt;
            Vector3 chest = me ? _e.ChestOf(me) : Vector3.zero;
            float r2 = _kit.packRadius * _kit.packRadius;
            for (int i = 0; i < _packs.Count; i++)
            {
                var p = _packs[i];
                p.Tick(dt, now);
                if (!can || !p.Available || (chest - p.Center).sqrMagnitude > r2) continue;
                float got = _e.Heal(_kit.healAmount);
                if (got <= 0f) continue;
                TakePack(i, true, got);
                Glow(me, chest);
            }
        }

        void TakePack(int i, bool mine, float got)
        {
            if (i < 0 || i >= _packs.Count || !_kit) return;
            var p = _packs[i];
            bool wasThere = p.Available;
            p.Take(_kit.respawn);
            if (mine)
            {
                if (_kit.healTaken && _one) _one.PlayOneShot(_kit.healTaken, _kit.healVolume);
                if (_toast != null) { _toast.text = $"힐팩  +{Mathf.RoundToInt(got)}"; _toastT = 0f; }
                SendMed(i);
                Debug.Log($"[OYM-FLOW] heal pack {i} taken: +{got:F0} HP (now {_e.PlayerHP01 * 100f:F0}%), back in {_kit.respawn:F0} s");
            }
            else
            {
                if (wasThere && _kit.healTaken) AudioSource.PlayClipAtPoint(_kit.healTaken, p.Center, _kit.healVolume);
                Debug.Log($"[OYM-FLOW] heal pack {i} taken by the partner");
            }
        }

        /// a soft green swell over the suit (HitFlash, capped like every body flash) - the field kit's look
        static void Glow(Transform pilot, Vector3 chest)
        {
            var hf = pilot.GetComponent<HitFlash>();
            if (!hf)
            {
                hf = pilot.gameObject.AddComponent<HitFlash>();
                hf.renderers = pilot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }
            hf.AddHit(chest, new Color(0.45f, 3.6f, 1.2f), 0.9f, 0.2f);
        }

        // ------------------------------------------------------------------------------------------------ arena sections
        // The team repo's showcase puts the team arena in the middle of the route: HuskZones.Zone.section (Approach /
        // Arena / AfterArena) and HuskZones.reached gate the zones, arenaEntry / arenaExit mark the way through. The local
        // project has none of these: read by reflection, so the same file runs in both (no sections = always reached).
        static readonly FieldInfo s_section = typeof(HuskZones.Zone).GetField("section", BindingFlags.Public | BindingFlags.Instance);
        static readonly FieldInfo s_spawned = typeof(HuskZones.Zone).GetField("spawned", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo s_reached = typeof(HuskZones).GetField("reached", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo s_entry = typeof(HuskZones).GetField("arenaEntry", BindingFlags.Public | BindingFlags.Instance);
        static readonly FieldInfo s_exit = typeof(HuskZones).GetField("arenaExit", BindingFlags.Public | BindingFlags.Instance);

        static int SectionOf(HuskZones.Zone z) => z != null && s_section != null ? System.Convert.ToInt32(s_section.GetValue(z)) : 0;
        static int Reached(HuskZones hz) => hz && s_reached != null ? System.Convert.ToInt32(s_reached.GetValue(hz)) : int.MaxValue;
        static Transform ArenaGate(HuskZones hz, bool exit)
        {
            var f = exit ? s_exit : s_entry;
            return hz && f != null ? f.GetValue(hz) as Transform : null;
        }

        // ------------------------------------------------------------------------------------------------ waypoint
        /// The waypoint mark (FlowMarker) on where to go: the zone of the current sector that waits for the pilots (it
        /// wakes when they get there), else the sector's anchor. During a breather the next sector is already the
        /// target, so the mark shows the way on. Nothing once the last sector is done (the finale comes to them).
        void Waypoint()
        {
            if (!_marker) return;
            int s = Sector;
            int flags = _run != null ? (_run.Finished ? 1 : 0) : _hostFlags;
            if (s < 0 || s >= _plan.sectors.Count || (flags & 1) != 0) { _marker.show = false; return; }
            var sp = _plan.sectors[s];
            Vector3 p = sp.anchor;
            if (!_zonesLooked) { _zonesLooked = true; _zonesCache = FindAnyObjectByType<HuskZones>(); }
            if (_zonesCache)
                foreach (var wv in sp.waves)
                {
                    if (string.IsNullOrEmpty(wv.zone) || wv.zone == FlowRunner.Contact) continue;
                    var z = _zonesCache.zones.Find(q => q != null && q.name == wv.zone);
                    if (z == null || z.radius <= 0f || !z.sleepers) continue;
                    bool sleeping = false;
                    foreach (Transform ch in z.sleepers) if (ch.gameObject.activeSelf) { sleeping = true; break; }
                    if (!sleeping) continue;
                    int reached = Reached(_zonesCache);
                    var gate = SectionOf(z) > reached ? ArenaGate(_zonesCache, reached >= 1) : null;
                    if (gate)
                    {
                        // the zone lies past the team arena: show the way through it first
                        _marker.show = true;
                        _marker.target = gate.position;
                        _marker.label = reached >= 1 ? "아레나 출구" : "아레나 입구";
                        return;
                    }
                    p = z.center;
                    break;
                }
            _marker.show = true;
            _marker.target = p;
            _marker.label = string.IsNullOrEmpty(sp.marker) ? sp.objective : sp.marker;
        }

        // ------------------------------------------------------------------------------------------------ objective line
        void BuildUI()
        {
            _doc = SignalUI.Document(transform, "flow objective", 4.4f);
            var root = _doc.rootVisualElement;

            // top centre: "▸ 태양광 게이트 돌파   240 m"
            _objBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _objBox.style.position = Position.Absolute; _objBox.style.left = 0; _objBox.style.right = 0; _objBox.style.top = Length.Percent(6.5f);
            _objBox.style.flexDirection = FlexDirection.Row; _objBox.style.justifyContent = Justify.Center; _objBox.style.alignItems = Align.Center;
            _objBox.style.opacity = 0f;
            _obj = SignalUI.Text(21f, Ink, 2, true);
            _objDist = SignalUI.Text(17f, Dim, 1, false);
            _objDist.style.marginLeft = 14;
            _objBox.Add(_obj); _objBox.Add(_objDist);
            root.Add(_objBox);

            // cruise hint, once per long leg
            _hint = SignalUI.Text(19f, Amber, 2, true);
            _hint.style.position = Position.Absolute; _hint.style.left = 0; _hint.style.right = 0; _hint.style.top = Length.Percent(10.5f);
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter; _hint.style.opacity = 0f;
            root.Add(_hint);

            // heal pack of the flow taken (same spot and look as the field kit's)
            _toast = SignalUI.Text(30f, HealInk, 3, true);
            _toast.style.position = Position.Absolute; _toast.style.left = 0; _toast.style.right = 0; _toast.style.top = Length.Percent(55);
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter; _toast.style.opacity = 0f;
            root.Add(_toast);
        }

        /// the objective now, and where it points (anchor = false: no distance)
        string Objective(out Vector3 anchor, out bool hasAnchor, out bool cruise)
        {
            anchor = default; hasAnchor = false; cruise = false;
            int s = Sector;
            int flags = _run != null ? (_run.Finished ? 1 : 0) | (_run.Released ? 2 : 0) : _hostFlags;
            if (s < 0) return null;
            if (s >= _plan.sectors.Count || (flags & 1) != 0)
                return (flags & 2) != 0 ? _plan.bossObjective : _plan.breatherObjective;
            var sp = _plan.sectors[s];
            anchor = sp.anchor; hasAnchor = true; cruise = sp.cruiseHint;
            return sp.objective;
        }

        void Hud(float dt)
        {
            if (_objBox == null) return;
            bool view = SignalInstall.PlayerView(_e);
            var lv = _e.Level;
            bool boss = (lv && lv.beat >= HuskLevel.Beat.Boss) || (_e.boss && _e.boss.Playing);
            string text = Objective(out Vector3 anchor, out bool hasAnchor, out bool cruise);
            if (lv && lv.beat == HuskLevel.Beat.Victory) text = null;
            if (text != _objKey)
            {
                _objKey = text;
                if (!string.IsNullOrEmpty(text)) { _obj.text = "▸  " + text; _objT = 0f; _distShown = -1; }
            }
            var me = FlightCamera.Main ? FlightCamera.Main.target : null;
            float dist = hasAnchor && me ? Vector3.Distance(me.position, anchor) : 0f;
            int d10 = hasAnchor && dist > 60f && !boss ? Mathf.RoundToInt(dist / 10f) * 10 : 0;
            if (d10 != _distShown) { _distShown = d10; _objDist.text = d10 > 0 ? $"{d10} m" : ""; }

            // fades in with the "쪼" (fast, small overshoot, hard stop) whenever the objective changes; rests at 85 %
            bool on = view && !string.IsNullOrEmpty(_objKey);
            _objA = Mathf.MoveTowards(_objA, on ? 1f : 0f, dt / (on ? 0.2f : 0.3f));
            float y = 0f;
            if (_objT >= 0f)
            {
                _objT += dt;
                y = -12f * (1f - HuskFX.Snap(Mathf.Clamp01(_objT / 0.3f), 0.18f));
                _obj.style.color = Color.Lerp(Amber, Ink, Mathf.Clamp01((_objT - 0.8f) / 1.2f));
                if (_objT > 2.5f) _objT = -1f;
            }
            _objBox.style.opacity = _objA * (_objT >= 0f ? 1f : 0.85f);
            _objBox.style.translate = new Translate(0f, y);

            // cruise hint: once per long leg, out of combat
            int s = Sector;
            var threat = ZeroGFlight.EnemiesNear;
            if (cruise && view && hasAnchor && me && !_hinted.Contains(s) && dist > _plan.cruiseHintRange && (threat == null || !threat(me.position, 180f)))
            {
                _hinted.Add(s);
                _hint.text = _plan.cruiseHint;
                _hintT = 0f;
            }
            if (_hintT >= 0f)
            {
                _hintT += dt;
                float a = _hintT < 0.25f ? _hintT / 0.25f : _hintT > 4.5f ? 1f - (_hintT - 4.5f) / 0.6f : 1f;
                _hint.style.opacity = view ? Mathf.Clamp01(a) : 0f;
                if (_hintT > 5.1f) { _hintT = -1f; _hint.style.opacity = 0f; }
            }
            if (_toastT >= 0f)
            {
                _toastT += dt;
                float a = _toastT < 0.1f ? _toastT / 0.1f : _toastT > 1.2f ? 1f - (_toastT - 1.2f) / 0.5f : 1f;
                _toast.style.opacity = view ? Mathf.Clamp01(a) : 0f;
                _toast.style.translate = new Translate(0f, 14f * (1f - HuskFX.Snap(Mathf.Clamp01(_toastT / 0.22f), 0.15f)));
                if (_toastT > 1.7f) { _toastT = -1f; _toast.style.opacity = 0f; }
            }
        }

        // ================================================================================================ the live world
        /// The flow's view of the running game: HuskEncounter (spawns, pilots), HuskZones (lock = radius 0 until the
        /// zone's turn), HuskLevel (first contact, the radio, the finale hold).
        sealed class LiveWorld : IFlowWorld
        {
            sealed class ZoneInfo
            {
                public HuskZones.Zone z;
                public float radius;
                public readonly List<Vector3> spots = new List<Vector3>();
                public readonly List<int> members = new List<int>();
            }

            static readonly MethodInfo s_say = typeof(HuskLevel).GetMethod("Say", BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                                           new[] { typeof(string), typeof(string), typeof(float) }, null);
            static readonly FieldInfo s_bossQueued = typeof(HuskLevel).GetField("_bossQueued", BindingFlags.NonPublic | BindingFlags.Instance);

            readonly HuskEncounter _e;
            readonly HuskZones _hz;
            readonly List<HuskEnemy> _h = new List<HuskEnemy>();
            readonly Dictionary<string, ZoneInfo> _zones = new Dictionary<string, ZoneInfo>();
            bool _holdWarned;

            public LiveWorld(HuskEncounter e)
            {
                _e = e;
                var hz = _hz = Object.FindAnyObjectByType<HuskZones>();
                if (hz)
                    foreach (var z in hz.zones)
                    {
                        if (z == null || string.IsNullOrEmpty(z.name) || _zones.ContainsKey(z.name)) continue;
                        var info = new ZoneInfo { z = z, radius = z.radius };
                        if (z.sleepers) foreach (Transform ch in z.sleepers) if (ch.gameObject.activeSelf) info.spots.Add(ch.position);
                        _zones[z.name] = info;
                    }
                HuskEncounter.Spawned += OnSpawned;
            }

            public void Dispose() { HuskEncounter.Spawned -= OnSpawned; }

            int Handle(HuskEnemy en) { _h.Add(en); return _h.Count - 1; }

            /// a zone waking (HuskZones or WakeZone) spawns each HUSK on its sleeper's spot: that is how they are told apart
            void OnSpawned(HuskEnemy en)
            {
                if (!en) return;
                Vector3 p = en.transform.position;
                foreach (var info in _zones.Values)
                    foreach (var s in info.spots)
                        if ((s - p).sqrMagnitude < 0.36f) { info.members.Add(Handle(en)); return; }
            }

            public float Now => Time.time;
            public bool Running => _e && _e.Gate;
            public bool ContactStarted => _e && _e.Level && _e.Level.beat != HuskLevel.Beat.Arrival;

            public void ContactPack(List<int> into)
            {
                foreach (var en in _e.Enemies)
                    if (en && en.Alive && (en.name.StartsWith("WICK pack") || en.name.StartsWith("PRISM sniper"))) into.Add(Handle(en));
            }

            public bool HasZone(string zone) => _zones.ContainsKey(zone);
            public void LockZone(string zone) { if (_zones.TryGetValue(zone, out var i)) i.z.radius = 0f; }
            public void UnlockZone(string zone) { if (_zones.TryGetValue(zone, out var i)) i.z.radius = i.radius; }

            /// HuskZones.Wake from wherever the pilots are: nearest first, WICKs 0.7 s apart, PRISMs from 1.6 s every 3.5 s.
            /// Not for a zone past a section the pilots have not reached (team arena): false, the runner asks again later.
            public bool WakeZone(string zone)
            {
                if (!_zones.TryGetValue(zone, out var info) || !info.z.sleepers) return true;
                if (SectionOf(info.z) > Reached(_hz)) return false;
                var made = new List<HuskEnemy>();
                Vector3 from = _e.PlayerChest;
                var list = new List<Transform>();
                foreach (Transform ch in info.z.sleepers) if (ch.gameObject.activeSelf) list.Add(ch);
                list.Sort((a, b) => (a.position - from).sqrMagnitude.CompareTo((b.position - from).sqrMagnitude));
                int w = 0, pr = 0;
                foreach (var ch in list)
                {
                    bool wick = ch.name.StartsWith("WICK");
                    if (!wick && !ch.name.StartsWith("PRISM")) continue;
                    float delay = wick ? w++ * 0.7f : 1.6f + pr++ * 3.5f;
                    made.Add(_e.Spawn(wick ? HuskEnemy.Kind.Wick : HuskEnemy.Kind.Prism, ch.position, ch.rotation, delay));
                    ch.gameObject.SetActive(false);
                }
                // team repo: its HuskZones / HuskLevel track a woken zone's HUSK in Zone.spawned
                if (s_spawned != null && s_spawned.FieldType.IsAssignableFrom(made.GetType())) s_spawned.SetValue(info.z, made);
                Debug.Log($"[OYM-FLOW] zone '{zone}' woken from afar: {w} WICK, {pr} PRISM");
                return true;
            }

            public bool ZoneWoken(string zone, List<int> into)
            {
                if (!_zones.TryGetValue(zone, out var info) || !info.z.sleepers) return true;
                foreach (Transform ch in info.z.sleepers) if (ch.gameObject.activeSelf) return false;
                into.AddRange(info.members);
                return true;
            }

            public int Spawn(FlowSpawn s)
            {
                var en = _e.Spawn(s.prism ? HuskEnemy.Kind.Prism : HuskEnemy.Kind.Wick, s.position, s.rotation, -1f);
                en.name = (s.prism ? "PRISM flow " : "WICK flow ") + _h.Count;
                en.Inbound(s.inbound);
                return Handle(en);
            }

            public bool Alive(int h) => h >= 0 && h < _h.Count && _h[h] && _h[h].Alive;
            public bool AnyPilotWithin(Vector3 p, float r) => _e.AnyPilotWithin(p, r, out _);

            public void Say(string who, string line)
            {
                var lv = _e ? _e.Level : null;
                float hold = Mathf.Clamp(1.6f + line.Length * 0.06f, 2.4f, 3.8f);
                if (lv && s_say != null) s_say.Invoke(lv, new object[] { who, line, hold });
                else Debug.Log($"[OYM-FLOW] radio {who}: {line} (HuskLevel.Say not found: not shown)");
            }

            public void HoldBoss(bool hold)
            {
                var lv = _e ? _e.Level : null;
                if (!lv || s_bossQueued == null)
                {
                    if (!_holdWarned) Debug.LogWarning("[OYM-FLOW] HuskLevel._bossQueued not found: the CITADEL comes when HuskLevel's nest is cleared, not after the flow");
                    _holdWarned = true;
                    return;
                }
                if (!hold && lv.beat >= HuskLevel.Beat.Boss) return;
                s_bossQueued.SetValue(lv, hold);
            }

            public void Log(string msg) => Debug.Log("[OYM-FLOW] " + msg);
        }
    }
}
