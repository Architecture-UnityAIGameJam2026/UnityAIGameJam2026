using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Live HUSK in the showcase scene (ShowcaseScene.Name = Showcase_DebrisField), single-player and local.
    /// Installs itself when the scene loads (no scene edits: survives OYMShowcase rebuilds), swaps the sleeping showcase
    /// pack ("HUSK Enemies (test)/WICK_*, PRISM_*") for live attack rigs on the same spots, and hands the flow to
    /// HuskLevel (zones, cutaways, the CITADEL). This class owns the shared pieces: the enemy list, spawning, darts,
    /// the player's HP and hit reactions, the hold during cutaways.
    /// The player: HP with regen; hits flash the suit, kick the camera and pulse the damage vignette; at 0 HP the
    /// pilot is "downed" for a moment and refills (no game over yet). Heal packs (OYM.Field.FieldKit) give HP back
    /// through Heal. F10 = enemies on / off. -oymNoHusk = off.
    /// Co-op (OYM.Net): the host's HUSK go for the nearest pilot that is up (this PC's or the partner's puppet, Remote)
    /// and hits on the partner are sent to its owner; the guest's encounter is a Mirror (no level flow, no own spawns:
    /// the host's HUSK are copied in by OYM.Net.CoopWorld).
    public class HuskEncounter : MonoBehaviour
    {
        public static HuskEncounter Current { get; private set; }

        public HuskEncounterConfig config;
        public PlayTestDirector director;
        public BossEntrance boss;
        [Tooltip("F10 toggles: HUSK hold position and stop attacking")] public bool hostileEnabled = true;
        [Tooltip("set by the level during cutaways: everyone holds (except enemies flagged scripted)")] public bool hold;

        /// (enemy, killed by the player)
        public event System.Action<HuskEnemy, bool> EnemyGone;
        /// damage taken by the player (amount, source position)
        public event System.Action<float, Vector3> PlayerDamaged;

        readonly List<HuskEnemy> _enemies = new List<HuskEnemy>();
        readonly Dictionary<HomingDartFX, Vector3> _dartAim = new Dictionary<HomingDartFX, Vector3>();
        readonly Dictionary<HomingDartFX, Transform> _dartTarget = new Dictionary<HomingDartFX, Transform>();
        readonly List<Transform> _pilots = new List<Transform>(2);
        int _dartTurn;
        readonly HashSet<HomingDartFX> _hooked = new HashSet<HomingDartFX>();
        FlightCamera _fc;
        DartSalvoFX _salvo;
        HuskLevel _level;
        float _hp, _sinceHit = 99f, _downT, _beat;
        bool _downed;
        int _kills, _spawned;

        public IReadOnlyList<HuskEnemy> Enemies => _enemies;
        public IReadOnlyList<HomingDartFX> Darts => _salvo ? _salvo.Darts : null;
        public HuskLevel Level => _level;
        public FlightCamera FlightCam => _fc;
        public Transform Player => _fc ? _fc.target : null;
        public ZeroGFlight PlayerFlight { get { var p = Player; return p ? p.GetComponent<ZeroGFlight>() : null; } }
        public Vector3 PlayerChest
        {
            get
            {
                var p = Player;
                if (!p) return transform.position;
                var f = PlayerFlight;
                return p.position + Vector3.up * (f ? f.bodyCenter + 0.15f : 1.15f);
            }
        }
        public Vector3 PlayerVelocity { get { var f = PlayerFlight; return f ? f.Velocity : Vector3.zero; } }
        public bool PlayerAlive => !_downed;
        public bool Engaged { get; set; }
        public float PlayerHP01 => config ? _hp / Mathf.Max(1f, config.playerHP) : 1f;
        public int Kills => _kills;
        /// the player has control: no cutscene, the director runs, the boss entrance is not playing
        public bool Gate => director && director.enabled && !CutsceneRunning() && !(boss && boss.Playing);
        public bool Hostile => Engaged && hostileEnabled && Gate && AnyPilotAlive && !hold;

        // ---------------------------------------------------------------- co-op (OYM.Net)
        /// Set by the co-op session before the game scene loads on the guest: its HUSK are copies the host drives.
        public static bool CoopGuest;
        /// this encounter only shows the host's (co-op guest): no level flow, no sleepers or zones of its own
        public bool Mirror { get; private set; }

        /// The other pilot when two people play: flown on the other PC, a network puppet here.
        public sealed class RemotePilot
        {
            public Transform t;
            public Vector3 velocity;
            public bool alive = true;
            public float bodyCenter = 1f;
            /// an enemy hit it on this PC: the co-op layer sends it to its owner (amount, source, knock)
            public System.Action<float, Vector3, float> damage;
        }
        public RemotePilot Remote;

        /// every new HUSK (the co-op host announces it)
        public static event System.Action<HuskEnemy> Spawned;
        /// (tube, dir, target pilot, flight, aim offset, seed, blast scale, trail) for each launcher-pod dart (co-op host)
        public static event System.Action<Vector3, Vector3, Transform, float, Vector3, int, float, float> DartLaunched;
        /// co-op guest: a hit by this PC's pilot goes to the host instead (target, damage, point, direction, power)
        public static System.Func<Component, float, Vector3, Vector3, float, bool> HitRouter;
        /// true = the hit was handed to the host (do not apply it here)
        public static bool RouteHit(Component target, float dmg, Vector3 point, Vector3 dir, float power) =>
            HitRouter != null && HitRouter(target, dmg, point, dir, power);

        bool RemoteUp => Remote != null && Remote.t && Remote.alive && Remote.t.gameObject.activeInHierarchy;
        public bool AnyPilotAlive => !_downed || RemoteUp;

        /// this PC's pilot, then the co-op partner's puppet (shared list: read it, do not keep it)
        public List<Transform> Pilots()
        {
            _pilots.Clear();
            var me = Player;
            if (me) _pilots.Add(me);
            if (Remote != null && Remote.t && Remote.t != me && Remote.t.gameObject.activeInHierarchy) _pilots.Add(Remote.t);
            return _pilots;
        }

        public bool IsRemote(Transform t) => t && Remote != null && t == Remote.t;

        public Vector3 ChestOf(Transform t)
        {
            if (!t || t == Player) return PlayerChest;
            if (IsRemote(t)) return t.position + Vector3.up * (Remote.bodyCenter + 0.15f);
            var f = t.GetComponent<ZeroGFlight>();
            return t.position + Vector3.up * (f ? f.bodyCenter + 0.15f : 1.15f);
        }

        public Vector3 VelocityOf(Transform t) => IsRemote(t) ? Remote.velocity : PlayerVelocity;
        public bool AliveOf(Transform t) => IsRemote(t) ? RemoteUp : PlayerAlive;

        /// a collider on either pilot (enemy line of sight and steering ignore both)
        public bool IsPilotCollider(Collider c)
        {
            if (!c) return false;
            var me = Player;
            return (me && c.transform.IsChildOf(me)) || (Remote != null && Remote.t && c.transform.IsChildOf(Remote.t));
        }

        /// The pilot an enemy at `from` goes for: the nearest one that is up; it sticks with `current` unless the other
        /// one is clearly closer (no flip-flopping between two pilots side by side).
        public Transform PickTarget(Vector3 from, Transform current)
        {
            var me = Player;
            if (!RemoteUp) return me;
            if (!me || !PlayerAlive) return Remote.t;
            float dm = (ChestOf(me) - from).sqrMagnitude, dr = (ChestOf(Remote.t) - from).sqrMagnitude;
            if (current == me) return dr < dm * 0.6f ? Remote.t : me;
            if (current == Remote.t) return dm < dr * 0.6f ? me : Remote.t;
            return dr < dm ? Remote.t : me;
        }

        /// Hurt a pilot: this PC's through DamagePlayer, the partner's is sent to the PC that flies it.
        public void DamagePilot(Transform t, float amount, Vector3 from, float knock)
        {
            if (IsRemote(t))
            {
                if (!Engaged || amount <= 0.01f || !RemoteUp) return;
                Remote.damage?.Invoke(amount, from, knock);
                return;
            }
            DamagePlayer(amount, from, knock);
        }

        /// A blast at p: every pilot inside radius, full damage within 1 m, falling off to the edge (the WICK fuse).
        public void BlastPilots(Vector3 p, float radius, float damage, float knock)
        {
            var list = Pilots();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                float d = Vector3.Distance(ChestOf(t), p);
                if (d >= radius) continue;
                float k = Mathf.Clamp01(1f - Mathf.Max(0f, d - 1f) / Mathf.Max(0.1f, radius - 1f));
                DamagePilot(t, damage * k, p, knock * k);
            }
        }

        /// either pilot within r of c (zones wake for whoever gets there first)
        public bool AnyPilotWithin(Vector3 c, float r, out Vector3 at)
        {
            at = default;
            foreach (var t in Pilots())
                if ((t.position - c).sqrMagnitude <= r * r) { at = t.position; return true; }
            return false;
        }

        /// the chest of the pilot closest to p (level triggers that should fire for either pilot)
        public Vector3 NearestPilotChest(Vector3 p)
        {
            Vector3 best = PlayerChest; float bd = float.MaxValue;
            foreach (var t in Pilots()) { var c = ChestOf(t); float d = (c - p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }

        // ---------------------------------------------------------------- install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnLoaded; Current = null;
            CoopGuest = false; Spawned = null; DartLaunched = null; HitRouter = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        public static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// another session's recorder / demo is driving the scene: leave it alone
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && a != "-oymHuskDemo" && a != "-oymAttackTakes") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || Flag("-oymNoHusk") || s.name != ShowcaseScene.Name) return;
            if (OtherAutomation()) return;
            PlayTestDirector d = null;
            BossEntrance be = null;
            foreach (var root in s.GetRootGameObjects())
            {
                if (!d) d = root.GetComponentInChildren<PlayTestDirector>(true);
                if (!be) be = root.GetComponentInChildren<BossEntrance>(true);
            }
            if (!d) return;
            var cfg = Resources.Load<HuskEncounterConfig>("OYM_HuskEncounter");
            if (!cfg || !cfg.wickPrefab || !cfg.prismPrefab) { Debug.LogWarning("[OYM-HUSK] Resources/OYM_HuskEncounter missing (OYM/FX/Enemy Attack: Build)"); return; }
            // the CITADEL arrives at the end of the level, not 4 s into the scene (sceneLoaded runs before its Start)
            if (be) be.playOnStart = false;
            var go = new GameObject("HUSK Encounter");
            SceneManager.MoveGameObjectToScene(go, s);
            var e = go.AddComponent<HuskEncounter>();
            e.config = cfg; e.director = d; e.boss = be;
            if (Flag("-oymHuskDemo")) go.AddComponent<HuskEncounterDemo>();
        }

        // ---------------------------------------------------------------- cutscene gate (OYM.Cine is another session's module)
        static PropertyInfo s_cineCurrent;
        static FieldInfo s_cineRunning;
        static bool s_cineLooked;

        public static bool CutsceneRunning()
        {
            if (!s_cineLooked)
            {
                s_cineLooked = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("OYM.Cine.ArrivalCutscene");
                    if (t == null) continue;
                    s_cineCurrent = t.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    s_cineRunning = t.GetField("_running", BindingFlags.NonPublic | BindingFlags.Instance);
                    break;
                }
            }
            if (s_cineCurrent == null || s_cineRunning == null) return false;
            var c = s_cineCurrent.GetValue(null) as Object;
            return c && (bool)s_cineRunning.GetValue(c);
        }

        // ---------------------------------------------------------------- lifecycle
        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }

        void Start()
        {
            Mirror = CoopGuest;
            _fc = director && director.flightCamera ? director.flightCamera : FlightCamera.Main;
            _hp = config.playerHP;
            var cam = _fc ? _fc.GetComponent<Camera>() : Camera.main;
            if (cam && !cam.GetComponent<HitScreenFX>()) cam.gameObject.AddComponent<HitScreenFX>();
            if (config.dartPrefab)
            {
                _salvo = new GameObject("HUSK Salvo").AddComponent<DartSalvoFX>();
                _salvo.transform.SetParent(transform, false);
                _salvo.dartPrefab = config.dartPrefab;
            }
            if (Mirror)
            {
                // co-op guest: the host's sleepers, zones and level flow are copied in (OYM.Net.CoopWorld)
                Debug.Log($"[OYM-HUSK] encounter ready as the co-op guest's mirror, boss {(boss ? "found" : "none")}");
                return;
            }
            ConvertSleepers();
            _level = gameObject.AddComponent<HuskLevel>();
            _level.Init(this);
            Debug.Log($"[OYM-HUSK] encounter ready: {_enemies.Count} sleeping HUSK, boss {(boss ? "found" : "none")}");
        }

        /// the showcase pack (static models) -> live rigs on the same spots, still asleep
        void ConvertSleepers()
        {
            var root = GameObject.Find("HUSK Enemies (test)");
            if (!root) return;
            foreach (Transform ch in root.transform)
            {
                if (!ch.gameObject.activeSelf) continue;
                if (ch.name.StartsWith("WICK_")) { Spawn(HuskEnemy.Kind.Wick, ch.position, ch.rotation, -1f).name = "WICK pack " + ch.name.Substring(5); ch.gameObject.SetActive(false); }
                else if (ch.name.StartsWith("PRISM_")) { Spawn(HuskEnemy.Kind.Prism, ch.position, ch.rotation, -1f).name = "PRISM sniper " + ch.name.Substring(6); ch.gameObject.SetActive(false); }
            }
        }

        public HuskEnemy Spawn(HuskEnemy.Kind kind, Vector3 pos, Quaternion rot, float wakeIn)
        {
            Component rig = kind == HuskEnemy.Kind.Wick ? (Component)config.wickPrefab : config.prismPrefab;
            var go = Instantiate(rig.gameObject, pos, rot, transform);
            go.name = (kind == HuskEnemy.Kind.Wick ? "WICK " : "PRISM ") + (_spawned++);
            var e = go.AddComponent<HuskEnemy>();
            e.Setup(this, kind, wakeIn);
            _enemies.Add(e);
            Spawned?.Invoke(e);
            return e;
        }

        public int Count(HuskEnemy.Kind k) { int n = 0; foreach (var e in _enemies) if (e && e.Alive && e.kind == k) n++; return n; }

        void Update()
        {
            float dt = Time.deltaTime;
            Keys();
            _enemies.RemoveAll(e => !e);
            if (!_fc) _fc = FlightCamera.Main;
            TrackDarts();
            if (!Gate || !Engaged) return;
            Vitals(dt);
        }

        void Keys()
        {
            bool toggle;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            toggle = k != null && k.f10Key.wasPressedThisFrame;
#else
            toggle = Input.GetKeyDown(KeyCode.F10);
#endif
            if (!toggle) return;
            hostileEnabled = !hostileEnabled;
            Debug.Log("[OYM-HUSK] enemies " + (hostileEnabled ? "ON" : "OFF (holding)"));
        }

        // ---------------------------------------------------------------- darts
        /// One homing dart from `tube` along `launchDir` onto the player's chest + aimOffset; homes on the player for
        /// the first 70 % of the flight (a late dash dodges), blasts on arrival.
        public HomingDartFX LaunchDart(Vector3 tube, Vector3 launchDir, float flight, Vector3 aimOffset, float blastScale = 0.9f, float trail = 0.9f)
        {
            if (!_salvo) return null;
            // co-op: a salvo splits between the two pilots, one dart each in turn
            var list = Pilots();
            Transform target = list.Count > 1 && RemoteUp ? list[_dartTurn++ % 2] : Player;
            int seed = Random.Range(0, 100000);
            var d = LaunchDartAt(tube, launchDir, target, flight, aimOffset, seed, blastScale, trail);
            DartLaunched?.Invoke(tube, launchDir, target, flight, aimOffset, seed, blastScale, trail);
            return d;
        }

        /// One homing dart onto `target` (a pilot) - also the co-op guest's copy of a host dart.
        public HomingDartFX LaunchDartAt(Vector3 tube, Vector3 launchDir, Transform target, float flight, Vector3 aimOffset, int seed, float blastScale, float trail)
        {
            if (!_salvo) return null;
            var d = _salvo.LaunchOne(tube, launchDir, ChestOf(target) + aimOffset, flight, seed);
            d.blastScale = blastScale; d.trailSeconds = trail;
            _dartAim[d] = aimOffset;
            _dartTarget[d] = target;
            return d;
        }

        void TrackDarts()
        {
            if (!_salvo) return;
            foreach (var d in _salvo.Darts)
            {
                if (!d || !d.Busy) continue;
                if (!_hooked.Contains(d)) { _hooked.Add(d); d.Impacted += (p, n) => DartHit(p); }
                if (d.Clock < d.FlightTime * 0.7f && _dartAim.TryGetValue(d, out var off))
                    d.SetTarget((_dartTarget.TryGetValue(d, out var tt) && tt ? ChestOf(tt) : PlayerChest) + off);
            }
        }

        void DartHit(Vector3 p)
        {
            if (Mirror) return;                                   // co-op guest: the host's dart does the damage
            var list = Pilots();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                float d = Vector3.Distance(p, ChestOf(t));
                if (d < config.salvoRadius) DamagePilot(t, config.salvoDamage * (1f - 0.6f * d / config.salvoRadius), p, 6f);
            }
        }

        // ---------------------------------------------------------------- the player
        static FieldInfo s_vel;

        /// The pilot's own hit glow (HitFlash). 11-19 WICK white-out pass: it was (7, 0.6, 2.4) x 0.9 m with a 0.55 body
        /// flash, and the overlay's white-hot centre is peak x SpotGain = ~10x white on the pilot's back, right in the middle
        /// of a third-person view, on every WICK / dart hit. Now a hot-pink scorch that stays under ~2x white.
        public static Color PlayerHitColor = new Color(1.3f, 0.11f, 0.45f);
        public static float PlayerHitSize = 0.6f, PlayerHitFlash = 0.2f;

        public void DamagePlayer(float amount, Vector3 from, float knock)
        {
            if (_downed || amount <= 0.01f || !Engaged) return;
            _hp = Mathf.Max(0f, _hp - amount);
            _sinceHit = 0f;
            HitScreenFX.PlayerHit(Mathf.Clamp01(0.25f + amount / 45f));
            if (_fc) _fc.Shake(Mathf.Clamp01(0.2f + amount / 50f));
            var p = Player;
            if (p)
            {
                var hf = p.GetComponent<HitFlash>();
                if (!hf)
                {
                    hf = p.gameObject.AddComponent<HitFlash>();
                    hf.renderers = p.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                }
                hf.AddHit(PlayerChest, PlayerHitColor, PlayerHitSize, PlayerHitFlash);
                Knock(from, knock);
            }
            PlayerDamaged?.Invoke(amount, from);
            if (_hp <= 0f) Down();
        }

        /// below full HP and up (a heal pack only goes to a pilot that needs it)
        public bool PlayerHurt => config && !_downed && _hp < config.playerHP - 0.5f;

        /// A heal pack (OYM.Field): up to `amount` HP back, never past the max, nothing while downed. Returns the HP
        /// it actually gave (0 = the pack should stay where it is).
        public float Heal(float amount)
        {
            if (!config || _downed || amount <= 0f) return 0f;
            float before = _hp;
            _hp = Mathf.Min(config.playerHP, _hp + amount);
            return _hp - before;
        }

        /// zero-g shove on the pilot (ZeroGFlight keeps its velocity protected; reflection keeps the controller untouched)
        public void Knock(Vector3 from, float speed)
        {
            var f = PlayerFlight;
            if (!f || speed <= 0f) return;
            if (s_vel == null) s_vel = typeof(ZeroGFlight).GetField("velocity", BindingFlags.NonPublic | BindingFlags.Instance);
            if (s_vel == null) return;
            Vector3 dir = PlayerChest - from;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : -f.transform.forward;
            s_vel.SetValue(f, (Vector3)s_vel.GetValue(f) + dir * speed);
        }

        void Down()
        {
            _downed = true; _downT = 0f;
            var scr = HitScreenFX.Instance;
            if (scr) { scr.AddDamage(1f); scr.AddFlash(0.7f); }
            GameFeel.HitStop(0.3f, 0.06f);
            foreach (var e in _enemies) if (e) e.OnPilotDown(Player);
            Debug.Log("[OYM-HUSK] pilot down");
        }

        void Vitals(float dt)
        {
            _sinceHit += dt;
            if (_downed)
            {
                _downT += dt;
                _hp = Mathf.Lerp(0f, config.playerHP, HuskFX.Smooth(0.6f, 2.6f, _downT));
                if (_downT >= 2.6f) { _downed = false; _hp = config.playerHP; }
                return;
            }
            if (_sinceHit > config.regenDelay) _hp = Mathf.Min(config.playerHP, _hp + config.regenRate * dt);
            // low HP: a heartbeat in the damage vignette
            float low = Mathf.Clamp01(1f - PlayerHP01 / 0.35f);
            if (low > 0f)
            {
                _beat += dt * Mathf.Lerp(1.1f, 1.8f, low);
                if (_beat >= 1f) { _beat = 0f; var scr = HitScreenFX.Instance; if (scr) scr.AddDamage(0.15f + 0.35f * low); }
            }
        }

        public void OnEnemyGone(HuskEnemy e, bool byPlayer)
        {
            if (byPlayer) _kills++;
            EnemyGone?.Invoke(e, byPlayer);
        }
    }
}
