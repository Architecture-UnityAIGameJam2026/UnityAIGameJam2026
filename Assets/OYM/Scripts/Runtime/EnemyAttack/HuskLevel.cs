using System.Collections;
using System.Collections.Generic;
using OYM.Cine;
using UnityEngine;

namespace OYM
{
    /// The encounter flow of the game scene, on top of the level layout (OYMLevel / HuskZones, the props session's:
    /// prop sets as landmarks + cover, sleeping HUSK on them, zones that wake when the player comes close).
    ///   Arrival   arrival cutscene, then a breath
    ///   Contact   the start-up pack (HuskEncounter's sleepers) wakes: cutaway - the pack from a clear angle, sensors
    ///             igniting down the line; reverse angle from the player as the lead pod bolts
    ///   Route     each layout zone that wakes gets a beat: the first one with a PRISM gets the sniper reveal (a lance
    ///             tears past the player, telephoto on the PRISM venting), the last one (the nest) a big reveal, the
    ///             others a radio line. Pacing is the player's: zones only wake when they get there.
    ///   Boss      the nest cleared (or a while after it woke): a quiet beat, then the CITADEL entrance (BossEntrance);
    ///             launcher-pod salvos with a tube-glow telegraph, WICK swarms out of the dorsal hatch, a PRISM escort.
    ///             Objective: break its red sensor lights one by one (CitadelWeakPoints: head + four claws; each flares,
    ///             dies and blows, claws tear off). The launcher pods (HuskBossPart) still break and stop their salvos.
    ///             Without weak points (no boss parts found) the old objective applies: both launcher pods.
    ///   Victory   the last light gone: blasts chain across the hull, it goes dark and sinks back into the debris,
    ///             the remaining HUSK burn out.
    /// Without layout zones in the scene it falls back to its own beats (snipers / ambush spawned along the path).
    /// Cutaways hold everyone (HuskEncounter.hold) and lock the controls; their cameras look for a clear line of sight.
    /// In first person (FlightCamera.firstPerson) there are no camera cutaways: the short over-the-shoulder shots read
    /// as the view dropping to third person for a moment (a dash into a zone set them off) - the barks, the wake-ups and
    /// the salvos play as without cutaways, and the victory plays from the pilot's own eyes.
    public class HuskLevel : MonoBehaviour
    {
        public enum Beat { Arrival, Contact, Snipers, Ambush, Route, Boss, Victory }

        public Beat beat = Beat.Arrival;
        public float BeatTime => _beatT;
        public bool InCutaway => _cut;
        public float Progress => _e ? Vector3.Dot(_e.PlayerChest - _origin, _fwd) : 0f;
        public Vector3 PathForward => _fwd;
        public IReadOnlyList<Transform> Stones => _stones;
        public int ZonesWoken => _wokenCount;
        public int ZonesTotal => _zones.Count;
        public int PodsLeft { get { int n = 0; foreach (var p in _parts) if (p && !p.Broken) n++; return n; } }

        /// (beat) when a beat starts
        public event System.Action<Beat> BeatStarted;
        /// (speaker, line) on every radio bark
        public event System.Action<string, string> Radio;

        static readonly Color OrbitAccent = new Color(1f, 0.68f, 0.25f), KestrelAccent = new Color(0.45f, 0.85f, 1f);

        sealed class ZoneWatch
        {
            public HuskZones.Zone z;
            public int index, initial;
            public bool woken;
            public float wokenAt = -1f;
            public readonly List<HuskEnemy> enemies = new List<HuskEnemy>();
        }

        HuskEncounter _e;
        HuskEncounterConfig _c;
        Vector3 _origin, _fwd = Vector3.forward;
        readonly List<Transform> _stones = new List<Transform>();
        readonly List<HuskEnemy> _zone = new List<HuskEnemy>();
        readonly List<HuskEnemy> _pack = new List<HuskEnemy>();
        readonly List<ZoneWatch> _zones = new List<ZoneWatch>();
        ZoneWatch _nest;
        int _wokenCount;
        bool _sniperShown;
        float _beatT, _controlT, _bossT, _salvoT, _hatchT, _escortT, _barkClear = -1f, _nestClearT = -1f;
        bool _cut, _bossLive, _firstSalvo, _salvoing, _bossQueued;
        int _pod;
        CineCamera _cine;
        CineOverlay _ui;
        HuskFXCanvas _fx;
        readonly List<Vector3> _hot = new List<Vector3>();
        float _hotT = -1f, _hotEnd = -1f;
        readonly Transform[] _pods = new Transform[2];
        readonly List<HuskBossPart> _parts = new List<HuskBossPart>();
        CitadelWeakPoints _weak;                                   // the red sensor lights: the objective when present
        bool[] _wasScripted;
        bool _camWasScripted;
        static readonly RaycastHit[] s_hits = new RaycastHit[24];

        // ---------------------------------------------------------------- setup
        public void Init(HuskEncounter e)
        {
            _e = e; _c = e.config;
            var path = GameObject.Find("Debris Path (spawn fragment + stepping stones)");
            if (path)
                foreach (Transform t in path.transform)
                    if (t.name.StartsWith("PathStone_")) _stones.Add(t);
            _stones.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var spawn = path ? path.transform.Find("SpawnFragment") : null;
            _origin = spawn ? new Vector3(spawn.position.x, 0f, spawn.position.z) : (_e.Player ? _e.Player.position : Vector3.zero);
            if (_stones.Count >= 2)
            {
                Vector3 f = _stones[_stones.Count - 1].position - _origin; f.y = 0f;
                if (f.sqrMagnitude > 1f) _fwd = f.normalized;
            }
            foreach (var en in _e.Enemies) if (en && en.Dormant) _pack.Add(en);

            var hz = FindAnyObjectByType<HuskZones>();
            if (hz)
                for (int i = 0; i < hz.zones.Count; i++)
                {
                    var z = hz.zones[i];
                    if (z == null || !z.sleepers) continue;
                    int n = 0; foreach (Transform ch in z.sleepers) if (ch.gameObject.activeSelf) n++;
                    if (n == 0) continue;
                    var w = new ZoneWatch { z = z, index = i, initial = n };
                    _zones.Add(w);
                    if (z.name != null && z.name.ToLowerInvariant().Contains("nest")) _nest = w;
                }
            if (_nest == null && _zones.Count > 0) _nest = _zones[_zones.Count - 1];
            _ui = CineOverlay.Create(transform);
            Debug.Log($"[OYM-HUSK] level: {_stones.Count} path stones, start-up pack {_pack.Count}, layout zones {_zones.Count}" +
                      (_nest != null ? $" (finale: {_nest.z.name})" : " - no layout zones: own beats along the path"));
        }

        void OnDisable() { if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); } }
        void OnDestroy()
        {
            if (_fx != null) { _fx.Destroy(); _fx = null; }
            CitadelWeakPoints.Broke -= WeakBroke;
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (!_e) return;
            float dt = Time.deltaTime;
            if (_barkClear > 0f && Time.unscaledTime >= _barkClear) { _barkClear = -1f; if (_ui) _ui.ClearLine(); }
            DrawTelegraph(dt);
            if (!_e.Gate) return;                                 // arrival cutscene, boss entrance
            _beatT += dt;
            switch (beat)
            {
                case Beat.Arrival:
                    _controlT += dt;
                    if (_controlT >= _c.contactAfter || NearPack()) StartContact();
                    break;
                case Beat.Contact:
                case Beat.Route:
                    if (_zones.Count > 0) RouteZones();
                    else if (!_cut && beat == Beat.Contact && (Cleared() || Progress >= _c.snipersProgress || _beatT >= _c.snipersTimeout)) StartSnipers();
                    break;
                case Beat.Snipers:
                    if (!_cut && (Cleared() || Progress >= _c.ambushProgress || _beatT >= _c.ambushTimeout)) StartAmbush();
                    break;
                case Beat.Ambush:
                    if (!_cut && (Cleared() || Progress >= _c.bossProgress || _beatT >= _c.bossTimeout)) StartBoss();
                    break;
                case Beat.Boss:
                    BossPhase(dt);
                    break;
            }
        }

        bool NearPack()
        {
            if (_pack.Count == 0) return false;
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var p in _pack) if (p) { c += p.transform.position; n++; }
            return n > 0 && Vector3.Distance(_e.NearestPilotChest(c / n), c / n) < _c.contactRange;   // either pilot (co-op)
        }

        bool Cleared()
        {
            foreach (var z in _zone) if (z && z.Alive) return false;
            return _beatT > 4f;
        }

        void Enter(Beat b)
        {
            beat = b; _beatT = 0f;
            _zone.RemoveAll(z => !z || !z.Alive);
            Debug.Log($"[OYM-HUSK] beat {b} (progress {Progress:F0} m)");
            BeatStarted?.Invoke(b);
        }

        // ---------------------------------------------------------------- first contact
        void StartContact()
        {
            _e.Engaged = true;                                    // also lets HuskZones wake the layout zones from now on
            Enter(_zones.Count > 0 ? Beat.Route : Beat.Contact);
            var pack = new List<HuskEnemy>();
            foreach (var en in _pack) if (en && en.Alive && en.Dormant) pack.Add(en);
            Vector3 chest = _e.PlayerChest;
            pack.Sort((a, b) => Vector3.Distance(a.transform.position, chest).CompareTo(Vector3.Distance(b.transform.position, chest)));
            _zone.AddRange(pack);
            if (pack.Count > 0) StartCoroutine(RevealCut(pack, _c.barkContactBy, _c.barkContact, false));
        }

        /// the sleepers from a clear angle, sensors igniting down the line; then the reverse angle from the player as
        /// the lead one bolts (nest = no bolt, all of them ping, a longer hold)
        IEnumerator RevealCut(List<HuskEnemy> pack, string who, string line, bool nest)
        {
            if (pack.Count == 0) yield break;
            if (!Cutaways)
            {
                Say(who, line, 2.6f);
                if (!nest) for (int i = 0; i < pack.Count; i++) pack[i].WakeIn(i * _c.wakeStagger * 0.6f);
                yield break;
            }
            var lead = pack[0];
            Vector3 chest = _e.PlayerChest;
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var p in pack) if (p) { c += p.transform.position; n++; }
            c /= Mathf.Max(1, n);
            float spread = 0f; foreach (var p in pack) if (p) spread = Mathf.Max(spread, Vector3.Distance(p.transform.position, c));
            float dist = Mathf.Clamp(spread * 1.6f + 6f, 8f, 28f);
            BeginCut();
            if (FindView(c, chest - c, dist, 1.2f + dist * 0.12f, out var eye0) && FindView(c, chest - c, dist * 0.78f, 1.0f + dist * 0.1f, out var eye1))
            {
                var shot = CineShot.Rail(eye0, eye1, nest ? 2.6f : 1.9f, null, nest ? 46f : 42f).Handheld(0.15f);
                shot.lookPoint = c;
                _cine.Play(shot, 0.3f);
            }
            else _cine.Play(CineShot.FixedAt(ShoulderEye(chest, c), c, 34f), 0.3f);
            yield return Wait(0.35f);
            float gap = nest ? 0.09f : 0.17f;
            for (int i = pack.Count - 1; i >= 0; i--) { if (pack[i]) pack[i].PingOnly(); yield return Wait(gap); }
            yield return Wait(nest ? 0.6f : 0.35f);
            if (!nest && lead)
            {
                // reverse angle: over the player's shoulder at the lead pod, it bolts at the camera
                _cine.Play(CineShot.FixedAt(ShoulderEye(chest, lead.transform.position), lead.transform.position, 30f).Handheld(0.25f), 0f);
                yield return Wait(0.45f);
                lead.scripted = true;                               // bolts while everyone else still holds
                lead.Go();
            }
            Say(who, line, 2.8f);
            yield return Wait(nest ? 0.9f : 0.6f);
            EndCut(0.5f);
            if (lead) lead.scripted = false;
            if (!nest) for (int i = 1; i < pack.Count; i++) if (pack[i]) pack[i].WakeIn(0.25f + (i - 1) * _c.wakeStagger * 0.6f);
        }

        // ---------------------------------------------------------------- layout zones (HuskZones wakes them, this reacts)
        void RouteZones()
        {
            foreach (var w in _zones)
            {
                if (w.woken || !w.z.sleepers) continue;
                bool any = false;
                foreach (Transform ch in w.z.sleepers) if (ch.gameObject.activeSelf) { any = true; break; }
                if (!any) React(w);
            }
            // the finale: the nest cleared (or long enough after it woke) -> quiet -> the CITADEL
            if (_bossQueued || _nest == null || !_nest.woken || _cut) return;
            bool clear = true;
            foreach (var en in _nest.enemies) if (en && en.Alive) { clear = false; break; }
            if (clear && _nestClearT < 0f) _nestClearT = Time.time;
            bool timeout = Time.time - _nest.wokenAt > _c.bossNestTimeout;
            if ((clear && Time.time - _nestClearT > _c.bossAfterNest) || timeout) { _bossQueued = true; StartCoroutine(QuietThenBoss()); }
        }

        void React(ZoneWatch w)
        {
            w.woken = true; w.wokenAt = Time.time; _wokenCount++;
            foreach (var en in _e.Enemies)
                if (en && en.Alive && en.Dormant && Vector3.Distance(en.transform.position, w.z.center) < w.z.radius + 40f) w.enemies.Add(en);
            _zone.AddRange(w.enemies);
            int k = _zones.IndexOf(w);
            string who = Pick(_c.zoneBarkBy, k, "ORBIT FRAME"), line = Pick(_c.zoneBarks, k, null);
            Debug.Log($"[OYM-HUSK] zone '{w.z.name}' woke ({w.enemies.Count} HUSK)");
            Enter(Beat.Route);
            if (w == _nest)
            {
                var wicks = w.enemies.FindAll(en => en.kind == HuskEnemy.Kind.Wick);
                Vector3 chest = _e.PlayerChest;
                wicks.Sort((a, b) => Vector3.Distance(a.transform.position, chest).CompareTo(Vector3.Distance(b.transform.position, chest)));
                if (wicks.Count > 0) StartCoroutine(RevealCut(wicks, who, line ?? "둥지다!", true));
                else Say(who, line, 2.6f);
                return;
            }
            var prisms = w.enemies.FindAll(en => en.kind == HuskEnemy.Kind.Prism);
            if (!_sniperShown && prisms.Count > 0) { _sniperShown = true; StartCoroutine(SniperCut(prisms, who, line, false)); return; }
            Say(who, line, 2.6f);
        }

        /// camera cutaways only in third person (first person keeps the pilot's own view)
        bool Cutaways => _c.cutaways && !FirstPerson;
        bool FirstPerson => _e && _e.FlightCam && _e.FlightCam.firstPerson;

        static string Pick(string[] a, int i, string fallback) => a != null && i >= 0 && i < a.Length && !string.IsNullOrEmpty(a[i]) ? a[i] : fallback;

        IEnumerator QuietThenBoss()
        {
            Say(_c.barkQuietBy, _c.barkQuiet, 2.4f);
            yield return Wait(2.6f);
            StartBoss();
        }

        // ---------------------------------------------------------------- the sniper reveal
        IEnumerator SniperCut(List<HuskEnemy> snipers, string who, string line, bool wakeOthers)
        {
            Vector3 chest = _e.PlayerChest;
            snipers.Sort((a, b) => Vector3.Distance(a.transform.position, chest).CompareTo(Vector3.Distance(b.transform.position, chest)));
            var p = snipers[0];
            Vector3 lens = p.prism.LensWorld;
            Vector3 dir = (chest - lens).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up); side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            if (Cutaways)
            {
                // the sighting beam settles beside the player, then the lance tears past
                p.scripted = true;
                p.ScriptedLance(chest + side * 1.8f + Vector3.up * 0.5f, 1.25f);
                _e.hold = true;
                yield return Wait(1.32f);
                BeginCut();
                lens = p.prism.LensWorld;
                Vector3 eye = SafeEye(_e.PlayerChest, _e.PlayerChest - dir * 1.2f - side * 1.1f + Vector3.up * 0.3f);
                _cine.Play(CineShot.FixedAt(eye, lens, 11f).Zoom(8.5f, 1.7f).Handheld(0.4f), 0f);
                Say(who, line, 3.2f);
                yield return Wait(1.7f);
                EndCut(0.45f);
            }
            else Say(who, line, 3.2f);
            if (wakeOthers) for (int i = 0; i < snipers.Count; i++) if (snipers[i] && snipers[i].Dormant) snipers[i].WakeIn(0.2f + i * 1.5f);
        }

        // ---------------------------------------------------------------- fallback beats (no layout zones)
        void StartSnipers()
        {
            Enter(Beat.Snipers);
            var snipers = new List<HuskEnemy>();
            foreach (var en in _e.Enemies) if (en && en.Alive && en.kind == HuskEnemy.Kind.Prism) snipers.Add(en);
            if (snipers.Count == 0) snipers.Add(_e.Spawn(HuskEnemy.Kind.Prism, Ahead(70f, 12f, 0f), Quaternion.identity, -1f));
            for (int i = 0; i < snipers.Count; i++) { snipers[i].stationDistance = 27f + 4f * i; snipers[i].stationHeight = 7f + 3f * i; }
            _zone.AddRange(snipers);
            StartCoroutine(SniperThenFlank(snipers));
        }

        IEnumerator SniperThenFlank(List<HuskEnemy> snipers)
        {
            yield return SniperCut(snipers, _c.barkSnipersBy, _c.barkSnipers, true);
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 fl = Quaternion.Euler(0f, k * Random.Range(55f, 75f), 0f) * _fwd;
                var w = _e.Spawn(HuskEnemy.Kind.Wick, _e.PlayerChest + fl * 60f + Vector3.up * Random.Range(-4f, 8f), Quaternion.LookRotation(-fl), -1f);
                w.Inbound(-fl * 20f);
                _zone.Add(w);
                yield return Wait(1.1f);
            }
        }

        void StartAmbush()
        {
            Enter(Beat.Ambush);
            var ambush = new List<HuskEnemy>();
            foreach (var s in AmbushSpots(5)) ambush.Add(_e.Spawn(HuskEnemy.Kind.Wick, s, Quaternion.LookRotation(Vector3.up), -1f));
            _zone.AddRange(ambush);
            StartCoroutine(AmbushBurst(ambush));
        }

        List<Vector3> AmbushSpots(int n)
        {
            var list = new List<Vector3>();
            float prog = Progress;
            foreach (var st in _stones)
            {
                float d = Vector3.Dot(st.position - _origin, _fwd) - prog;
                if (d < 12f || d > 75f) continue;
                var rs = st.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Vector3 off = new Vector3(Random.Range(-0.4f, 0.4f) * b.extents.x, 0f, Random.Range(-0.4f, 0.4f) * b.extents.z);
                list.Add(new Vector3(b.center.x, b.min.y - 1.6f, b.center.z) + off);
                if (list.Count >= n) break;
            }
            while (list.Count < n)
            {
                float a = (list.Count - n * 0.5f) * 12f;
                list.Add(_e.PlayerChest + Quaternion.Euler(0f, a, 0f) * _fwd * Random.Range(22f, 38f) + Vector3.down * Random.Range(7f, 11f));
            }
            return list;
        }

        IEnumerator AmbushBurst(List<HuskEnemy> ambush)
        {
            yield return Wait(0.6f);
            Say(_c.barkAmbushBy, _c.barkAmbush, 2.2f);
            if (_e.FlightCam) _e.FlightCam.Shake(0.35f);
            foreach (var w in ambush)
            {
                if (!w || !w.Alive) continue;
                Vector3 to = (_e.PlayerChest - w.transform.position).normalized;
                w.Inbound(Vector3.up * 13f + to * 9f);
                yield return Wait(0.22f);
            }
            yield return Wait(1.5f);
            var p = _e.Spawn(HuskEnemy.Kind.Prism, Ahead(75f, 14f, Random.Range(-20f, 20f)), Quaternion.identity, -1f);
            p.stationDistance = 26f; p.stationHeight = 9f;
            p.Inbound(-_fwd * 12f);
            _zone.Add(p);
        }

        // ---------------------------------------------------------------- the CITADEL
        void StartBoss()
        {
            Enter(Beat.Boss);
            _bossLive = false; _bossT = 0f; _salvoT = 0f; _hatchT = 0f; _escortT = 0f; _firstSalvo = false;
            var be = _e.boss;
            if (!be || !be.boss) { Debug.LogWarning("[OYM-HUSK] no BossEntrance: boss phase without the entrance"); return; }
            var hero = _e.Player;
            Transform partner = null;
            if (_e.director && _e.director.controllers != null)
                foreach (var c in _e.director.controllers) if (c && c.transform != hero) partner = c.transform;
            if (hero) be.actors = partner && Vector3.Distance(partner.position, hero.position) < 25f ? new[] { hero, partner } : new[] { hero };
            be.Play();
        }

        void BossPhase(float dt)
        {
            var be = _e.boss;
            _bossT += dt;
            if (!_bossLive)
            {
                _bossLive = true; _bossT = 0f;
                FindPods();
                SetupParts();
                _weak = CitadelWeakPoints.Install(_e, be ? be.boss : null);
                if (_weak && _weak.Total > 0) { _weak.AllBroken += WeakPointsDown; CitadelWeakPoints.Broke += WeakBroke; }
                else _weak = null;
                Say(_c.barkBossBy, _c.barkBoss, 2.6f);
            }
            if (_cut) return;
            if (!_firstSalvo && _bossT >= 2.2f) { _firstSalvo = true; _salvoT = 0f; StartCoroutine(LaunchCut()); return; }
            if (_firstSalvo && !_salvoing && PodsLeft > 0)
            {
                _salvoT += dt;
                if (_salvoT >= _c.bossSalvoEvery) { _salvoT = 0f; StartCoroutine(Salvo(NextPod())); }
            }
            _hatchT += dt;
            if (_hatchT >= _c.bossHatchEvery && _e.Count(HuskEnemy.Kind.Wick) < 7) { _hatchT = 0f; StartCoroutine(Hatch()); }
            if (_e.Count(HuskEnemy.Kind.Prism) < 1)
            {
                _escortT += dt;
                if (_escortT >= 14f)
                {
                    _escortT = 0f;
                    Vector3 from = be && be.boss ? BossBounds().center : Ahead(120f, 30f, 0f);
                    var p = _e.Spawn(HuskEnemy.Kind.Prism, from, Quaternion.identity, -1f);
                    p.stationDistance = 24f; p.stationHeight = 8f;
                    p.Inbound((_e.PlayerChest - from).normalized * 20f);
                }
            }
            else _escortT = 0f;
        }

        int NextPod()
        {
            for (int k = 0; k < 2; k++)
            {
                int i = (_pod + k) % 2;
                var part = i < _parts.Count ? _parts[i] : null;
                if (_pods[i] && (!part || !part.Broken)) { _pod = i + 1; return i; }
            }
            return -1;
        }

        void FindPods()
        {
            var be = _e.boss;
            if (!be || !be.boss) return;
            foreach (var t in be.boss.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "citadel_launcher_L") _pods[0] = t;
                else if (t.name == "citadel_launcher_R") _pods[1] = t;
            }
        }

        void SetupParts()
        {
            _parts.Clear();
            for (int k = 0; k < 2; k++)
            {
                var pod = _pods[k];
                if (!pod) { _parts.Add(null); continue; }
                var part = pod.GetComponent<HuskBossPart>();
                if (!part)
                {
                    part = pod.gameObject.AddComponent<HuskBossPart>();
                    GameObject twin = null;
                    if (pod.parent) { var tw = pod.parent.Find(pod.name + "_damaged"); if (tw) twin = tw.gameObject; }
                    part.Setup(_e, k == 0 ? "L" : "R", _c.podHP, twin);
                    part.OnBroken += PodBroken;
                }
                _parts.Add(part);
            }
        }

        void PodBroken(HuskBossPart p)
        {
            int left = PodsLeft;
            Debug.Log($"[OYM-HUSK] launcher pod {p.label} broken, {left} left");
            if (_weak)
            {
                // the red lights decide the fight; a pod is a bonus (no more salvos from it)
                if (!_weak.Finished) Say(_c.barkPodBy, left > 0 ? _c.barkPodFirst : _c.barkPodSecond, 2.2f);
                return;
            }
            if (left > 0) Say(_c.barkPodBy, _c.barkPodFirst, 2.4f);
            else { Say(_c.barkPodBy, _c.barkPodSecond, 2.0f); StartCoroutine(Victory()); }
        }

        // ---------------------------------------------------------------- the CITADEL's red lights (CitadelWeakPoints)
        void WeakBroke(CitadelWeakPoint p, int left)
        {
            if (beat != Beat.Boss || left <= 0) return;
            Say(Mate(), left == 1 ? "마지막 하나! 붉은 빛 하나 남았어!" : $"약점 하나 깨졌다! 남은 붉은 빛 {left}개.", 2.4f);
        }

        void WeakPointsDown()
        {
            if (beat == Beat.Victory) return;
            Say(Mate(), "다 깨졌다…! 터진다!", 2.2f);
            StartCoroutine(Victory());
        }

        void Objective()
        {
            if (_weak && _weak.Left > 0) Say(Mate(), "붉게 빛나는 데가 약점이야. 하나씩 깨!", 3f);
            else if (PodsLeft > 0) Say(_c.barkObjectiveBy, _c.barkObjective, 3f);
        }

        /// the other pilot (the one the player is not flying) for the new barks
        string Mate()
        {
            var me = _e.Player;
            if (_e.director && _e.director.controllers != null)
                foreach (var c in _e.director.controllers)
                    if (c && c.transform != me) return c is KestrelController ? "KESTREL" : "ORBIT FRAME";
            return "ORBIT FRAME";
        }

        Bounds BossBounds()
        {
            var rs = _e.boss.boss.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(_e.boss.boss.position, Vector3.one);
            bool any = false;
            foreach (var r in rs)
            {
                if (!r.enabled || r.name.Contains("_LOD1") || r.name.Contains("_LOD2")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// tube face of a launcher pod: +Z end in the pod's own space (follows the pod when it swings), 4 x 2 tube grid
        bool PodTubes(Transform pod, List<Vector3> tubes, out Vector3 launch)
        {
            launch = Vector3.up;
            tubes.Clear();
            if (!pod) return false;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var mf in pod.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var q = pod.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
                }
            }
            if (mn.x > mx.x) return false;
            Vector3 face = new Vector3((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, mx.z);
            Vector3 e = (mx - mn) * 0.5f;
            for (int gy = 0; gy < 2; gy++)
                for (int gx = 0; gx < 4; gx++)
                    tubes.Add(pod.TransformPoint(face + new Vector3((gx - 1.5f) / 1.5f * e.x * 0.55f, (gy - 0.5f) * e.y * 0.6f, 0f)));
            launch = (pod.forward + Vector3.up * 0.35f).normalized;
            return true;
        }

        IEnumerator Salvo(int podIndex)
        {
            if (podIndex < 0) yield break;
            _salvoing = true;
            var tubes = new List<Vector3>();
            Vector3 launch;
            if (!PodTubes(_pods[podIndex], tubes, out launch))
            {
                Vector3 o = _e.PlayerChest + _fwd * 150f + Vector3.up * 60f;
                for (int i = 0; i < 8; i++) tubes.Add(o + Random.insideUnitSphere * 6f);
                launch = (Vector3.up + (_e.PlayerChest - o).normalized * 0.5f).normalized;
            }
            _hot.Clear(); _hot.AddRange(tubes); _hotT = 0f; _hotEnd = -1f;                 // telegraph: the tube mouths heat up
            yield return Wait(0.9f);
            var part = podIndex < _parts.Count ? _parts[podIndex] : null;
            int n = Mathf.Min(_c.bossSalvoDarts, tubes.Count);
            for (int i = 0; i < n; i++)
            {
                if (part && part.Broken) break;                    // shot off mid-salvo
                float dist = Vector3.Distance(tubes[i], _e.PlayerChest);
                Vector3 off = i == 0 ? Vector3.zero : Random.insideUnitSphere * 3.5f;
                _e.LaunchDart(tubes[i], launch, Mathf.Clamp(dist / 44f, 2.2f, 6f) + Random.Range(0f, 0.5f), off, 0.85f, 0.9f);
                yield return Wait(0.085f);
            }
            _hotEnd = _hotT;
            _salvoing = false;
        }

        IEnumerator LaunchCut()
        {
            int pod = NextPod();
            var tubes = new List<Vector3>();
            if (Cutaways && pod >= 0 && PodTubes(_pods[pod], tubes, out _))
            {
                BeginCut();
                Vector3 chest = _e.PlayerChest;
                Vector3 face = Vector3.zero; foreach (var t in tubes) face += t; face /= tubes.Count;
                _cine.Play(CineShot.FixedAt(ShoulderEye(chest, face), face, 30f).Zoom(24f, 2.6f).Handheld(0.3f), 0.35f);
                StartCoroutine(Salvo(pod));
                yield return Wait(0.6f);
                Say(_c.barkSalvoBy, _c.barkSalvo, 2.4f);
                yield return Wait(1.9f);
                EndCut(0.6f);
                yield return Wait(2.6f);
                Objective();
            }
            else
            {
                yield return Salvo(pod);
                Objective();
            }
            _salvoT = 0f;
        }

        IEnumerator Hatch()
        {
            if (!_e.boss || !_e.boss.boss) yield break;
            var b = BossBounds();
            var bt = _e.boss.boss;
            Vector3 hatch = b.center + Vector3.up * b.extents.y * 0.85f - bt.forward * b.extents.z * 0.2f;
            for (int i = 0; i < _c.bossHatchWicks; i++)
            {
                Vector3 p = hatch + Random.insideUnitSphere * 8f;
                var w = _e.Spawn(HuskEnemy.Kind.Wick, p, Quaternion.LookRotation(Vector3.up), -1f);
                Vector3 to = (_e.PlayerChest - p).normalized;
                w.Inbound(Vector3.up * 16f + to * 18f + Random.insideUnitSphere * 4f);
                yield return Wait(0.3f);
            }
        }

        // ---------------------------------------------------------------- victory
        IEnumerator Victory()
        {
            Enter(Beat.Victory);
            yield return Wait(1.2f);
            var be = _e.boss;
            Transform bt = be ? be.boss : null;
            bool shot = !FirstPerson;
            Vector3 chest = _e.PlayerChest;
            Vector3 c = bt ? BossBounds().center : chest + _fwd * 150f;
            if (shot)
            {
                BeginCut();
                _cine.Play(CineShot.FixedAt(ShoulderEye(chest, c), c, 44f).Zoom(36f, 7f).Handheld(0.2f), 0.7f);
            }
            Say(_c.barkVictoryBy, _weak ? "무너진다…!" : _c.barkVictory, 2.6f);
            // the rest of the swarm burns out with its carrier pulling back
            var left = new List<HuskEnemy>();
            foreach (var en in _e.Enemies) if (en && en.Alive) left.Add(en);
            StartCoroutine(BurnOut(left, c));
            if (be) be.enabled = false;                              // stop its idle float: we move it now
            Vector3 p0 = bt ? bt.position : Vector3.zero;
            Quaternion r0 = bt ? bt.rotation : Quaternion.identity;
            Vector3 away = bt ? -Vector3.ProjectOnPlane(bt.forward, Vector3.up) : _fwd;
            away = away.sqrMagnitude > 1e-3f ? away.normalized : (c - chest).normalized;
            float t = 0f;
            bool released = false, said = false;
            while (t < 16f)
            {
                t += Time.deltaTime;
                if (bt)
                {
                    float d = 0.5f * 3.2f * t * t;                  // a heavy start, then it really goes
                    bt.SetPositionAndRotation(p0 + (away * 0.55f + Vector3.down * 0.85f).normalized * d,
                                              r0 * Quaternion.Euler(Mathf.Min(14f, t * 1.4f), 0f, Mathf.Sin(t * 0.4f) * 2f));
                }
                if (!said && t > 3.4f) { said = true; Say(_c.barkVictory2By, _c.barkVictory2, 3f); }
                if (!released && t > 6.5f) { released = true; if (shot) EndCut(1.2f); }
                yield return null;
            }
            if (bt) foreach (var r in bt.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            Debug.Log($"[OYM-HUSK] level complete: kills {_e.Kills}");
        }

        IEnumerator BurnOut(List<HuskEnemy> left, Vector3 from)
        {
            yield return Wait(1.0f);
            foreach (var en in left)
            {
                if (!en || !en.Alive) continue;
                en.TakeDamage(9999f, en.transform.position - from, 1.1f);
                yield return Wait(0.18f);
            }
        }

        // ---------------------------------------------------------------- telegraph
        /// tube-mouth glow before a salvo (diegetic telegraph), fading after the launch
        void DrawTelegraph(float dt)
        {
            if (_hotT < 0f) return;
            if (_fx == null || !_fx.Valid) _fx = new HuskFXCanvas(transform, _c.wickPrefab.materials);
            _hotT += dt;
            float k = _hotEnd < 0f ? HuskFX.Smooth(0f, 0.9f, _hotT) : 1f - HuskFX.Smooth(_hotEnd, _hotEnd + 0.8f, _hotT);
            _fx.Begin();
            if (k > 0.001f)
                for (int i = 0; i < _hot.Count; i++)
                {
                    float flick = 1f + 0.2f * HuskFX.N(_hotT * 17f, i);
                    _fx.dot.Sprite(_hot[i], 1.2f + 0.8f * k, HuskFX.Heat(0.5f + 0.35f * k) * ((0.4f + 3f * k * k) * flick));
                    _fx.glow.Sprite(_hot[i], 5f + 2f * k, HuskFX.Heat(0.58f) * (0.35f * k * flick));
                }
            _fx.End();
            if (_hotEnd >= 0f && _hotT > _hotEnd + 0.9f) _hotT = -1f;
        }

        // ---------------------------------------------------------------- cutaway cameras
        Vector3 Ahead(float dist, float up, float yaw) => _e.PlayerChest + Quaternion.Euler(0f, yaw, 0f) * _fwd * dist + Vector3.up * up;

        /// over the player's shoulder toward `look` (the player is in open space: a safe place for a camera)
        Vector3 ShoulderEye(Vector3 chest, Vector3 look)
        {
            Vector3 d = look - chest; d.y = 0f; d = d.sqrMagnitude > 1e-3f ? d.normalized : _fwd;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            return SafeEye(chest, chest - d * 2.4f + side * 1.1f + Vector3.up * 0.55f);
        }

        /// `want` pulled back toward `from` when something solid is in between
        Vector3 SafeEye(Vector3 from, Vector3 want)
        {
            Vector3 d = want - from;
            float m = d.magnitude;
            if (m < 1e-3f) return want;
            return RaycastWorld(from, d / m, m, out var h) ? h.point - d / m * 0.6f : want;
        }

        /// a camera spot `dist` from `subject` with a clear line of sight to it, preferring the side toward `prefer`
        bool FindView(Vector3 subject, Vector3 prefer, float dist, float up, out Vector3 eye)
        {
            prefer.y = 0f;
            prefer = prefer.sqrMagnitude > 1e-3f ? prefer.normalized : -_fwd;
            float[] angles = { 25f, -25f, 55f, -55f, 0f, 85f, -85f, 120f, -120f };
            foreach (float a in angles)
            {
                eye = subject + Quaternion.AngleAxis(a, Vector3.up) * prefer * dist + Vector3.up * up;
                if (Physics.CheckSphere(eye, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 d = subject - eye;
                if (RaycastWorld(eye, d.normalized, d.magnitude - 1.2f, out _)) continue;
                return true;
            }
            eye = subject + prefer * dist + Vector3.up * up;
            return false;
        }

        bool Ignore(Collider c)
        {
            if (!c || c.isTrigger) return true;
            if (c.GetComponentInParent<HuskEnemy>()) return true;
            var p = _e.Player;
            return p && c.transform.IsChildOf(p);
        }

        bool RaycastWorld(Vector3 o, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            if (dist <= 0f) return false;
            int n = Physics.RaycastNonAlloc(o, dir, s_hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Ignore(s_hits[i].collider) || s_hits[i].distance >= bd) continue;
                bd = s_hits[i].distance; best = s_hits[i]; any = true;
            }
            return any;
        }

        static WaitForSeconds Wait(float s) => new WaitForSeconds(s);

        void BeginCut()
        {
            _cut = true;
            _e.hold = true;
            var cam = _e.FlightCam ? _e.FlightCam.GetComponent<Camera>() : Camera.main;
            _cine = CineCamera.Ensure(cam);
            var d = _e.director;
            if (d && d.controllers != null)
            {
                _wasScripted = new bool[d.controllers.Length];
                for (int i = 0; i < d.controllers.Length; i++)
                {
                    var c = d.controllers[i]; if (!c) continue;
                    _wasScripted[i] = c.scripted; c.scripted = true; c.scriptedInput = default;
                }
            }
            if (_e.FlightCam) { _camWasScripted = _e.FlightCam.scripted; _e.FlightCam.scripted = true; }
            if (_ui) _ui.Letterbox(true, 0.25f);
        }

        void EndCut(float blend)
        {
            if (_cine) _cine.Release(blend);
            if (_ui) _ui.Letterbox(false, 0.5f);
            var d = _e.director;
            if (d && d.controllers != null && _wasScripted != null)
                for (int i = 0; i < d.controllers.Length && i < _wasScripted.Length; i++)
                    if (d.controllers[i]) { d.controllers[i].scripted = _wasScripted[i]; d.controllers[i].scriptedInput = default; }
            if (_e.FlightCam) _e.FlightCam.scripted = _camWasScripted;
            _e.hold = false;
            _cut = false;
        }

        void Say(string speaker, string line, float hold)
        {
            if (!_ui || string.IsNullOrEmpty(line)) return;
            _ui.Say(speaker, line, speaker == "KESTREL" ? KestrelAccent : OrbitAccent);
            _barkClear = Time.unscaledTime + hold;
            Debug.Log($"[OYM-HUSK] radio {speaker}: {line}");
            Radio?.Invoke(speaker, line);
        }
    }
}
