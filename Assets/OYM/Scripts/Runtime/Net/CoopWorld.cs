using System.Collections.Generic;
using OYM.Cine;
using UnityEngine;

namespace OYM.Net
{
    /// The HUSK, the level beats and the CITADEL for two PCs. The host's game runs as in single-player (its HUSK go for
    /// the nearest pilot, HuskEncounter.Remote) and this sends what the guest needs to show the same fight:
    ///   HUSK      spawns (numbered), 20 Hz poses + states (+ a PRISM's aim), one-shot looks (ping / charge / fire /
    ///             cancel / blast), every hit that lands (the guest's copies take the same damage and die the same way)
    ///   level     beat, engagement and radio barks (bottom subtitles)
    ///   CITADEL   the entrance start, weak points + pods installed, its pose, every hit on a light or a pod, victory
    ///   darts     each launcher-pod dart (tube, flight, target pilot, seed): the guest flies the same dart
    /// The guest's HuskEncounter is a Mirror (no level flow, no spawns of its own); its pilot's hits on HUSK, lights and
    /// pods are routed to the host (HuskEncounter.HitRouter), which applies them and sends the result back.
    [DefaultExecutionOrder(210)]                       // after FlightCamera (100), CineCamera (150), BossEntrance (200)
    public sealed class CoopWorld : MonoBehaviour
    {
        CoopSession _s;
        CoopGame _g;
        HuskEncounter _e;
        bool _host;

        // ---- host
        int _nextId = 1;
        readonly Dictionary<int, HuskEnemy> _byId = new Dictionary<int, HuskEnemy>();
        float _snapT, _levelT, _bossT;
        int _levelSig = -1;
        bool _bossPlaying, _bossLiveSent, _victorySent;
        HuskLevel _barkLevel;

        // ---- guest
        /// a guest copy eases from where it is to the host's latest pose over one snapshot interval
        sealed class Track
        {
            public HuskEnemy en;
            public Vector3 p0, p1; public Quaternion r0, r1;
            public float t0, t1, seen;
        }
        readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        readonly List<Transform> _decor = new List<Transform>();
        CineOverlay _ui;
        float _barkClear = -1f, _hideBossAt = -1f;
        bool _victory, _bossLive;
        Vector3 _bossP0, _bossP1; Quaternion _bossR0, _bossR1; float _bossT0 = -1f, _bossT1 = -1f;
        float _lastSnap = -1f;
        const float SnapEvery = 0.05f, BossEvery = 0.066f;
        readonly List<int> _drop = new List<int>();

        public bool BossFightStarted => _bossLive;
        public int Beat { get; private set; }

        static readonly Color OrbitAccent = new Color(1f, 0.68f, 0.25f), KestrelAccent = new Color(0.45f, 0.85f, 1f);

        public void Init(CoopGame g, CoopSession s, HuskEncounter e)
        {
            _g = g; _s = s; _e = e; _host = s.IsHost;
            if (_host)
            {
                HuskEnemy.Damaged += OnEnemyDamaged;
                HuskEnemy.NetFx += OnEnemyFx;
                HuskEncounter.DartLaunched += OnDart;
                CitadelWeakPoint.Damaged += OnWeakDamaged;
                HuskBossPart.Damaged += OnPodDamaged;
            }
            else
            {
                HuskEncounter.HitRouter = Route;
                _ui = CineOverlay.Create(transform);
                var root = GameObject.Find("HUSK Enemies (test)");
                if (root)
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t != root.transform && (t.name.StartsWith("WICK") || t.name.StartsWith("PRISM")) && t.gameObject.activeSelf) _decor.Add(t);
                // the zones wake on the host only
                foreach (var z in FindObjectsByType<HuskZones>(FindObjectsSortMode.None)) z.enabled = false;
            }
        }

        void OnDestroy()
        {
            HuskEnemy.Damaged -= OnEnemyDamaged;
            HuskEnemy.NetFx -= OnEnemyFx;
            HuskEncounter.DartLaunched -= OnDart;
            CitadelWeakPoint.Damaged -= OnWeakDamaged;
            HuskBossPart.Damaged -= OnPodDamaged;
            if (_barkLevel) _barkLevel.Radio -= OnBark;
            if (HuskEncounter.HitRouter == (System.Func<Component, float, Vector3, Vector3, float, bool>)Route) HuskEncounter.HitRouter = null;
        }

        bool Ready => _s && _s.Connected && _g && _g.PartnerInGame;
        void Send(byte[] m) { if (Ready) _s.Send(m); }

        // ================================================================================================ host
        int EnsureId(HuskEnemy en)
        {
            if (!en) return -1;
            if (en.netId < 0)
            {
                en.netId = _nextId++;
                _byId[en.netId] = en;
                SendSpawn(en);
            }
            return en.netId;
        }

        void SendSpawn(HuskEnemy en) =>
            Send(new CoopMsg.W(CoopMsg.Spawn).U16(en.netId).U8((int)en.kind).V3(en.transform.position).Q(en.transform.rotation).Done());

        /// the guest is in the scene: everything that exists so far
        public void SendRoster()
        {
            if (!_host || !_e) return;
            foreach (var en in _e.Enemies)
            {
                if (!en || !en.Alive) continue;
                if (en.netId < 0) EnsureId(en); else SendSpawn(en);
            }
            _levelSig = -1;
            Debug.Log($"[OYM-COOP] roster sent: {_byId.Count} HUSK");
        }

        void OnEnemyDamaged(HuskEnemy en, float dmg, Vector3 dir, float power)
        {
            int id = EnsureId(en);
            if (id >= 0) Send(new CoopMsg.W(CoopMsg.EnemyDamage).U16(id).F(dmg).V3(dir).F(power).Done());
        }

        void OnEnemyFx(HuskEnemy en, int fx, Vector3 a, Vector3 b)
        {
            int id = EnsureId(en);
            if (id >= 0) Send(new CoopMsg.W(CoopMsg.EnemyFx).U16(id).U8(fx).V3(a).V3(b).Done());
        }

        void OnDart(Vector3 tube, Vector3 dir, Transform target, float flight, Vector3 off, int seed, float blast, float trail)
        {
            int who = _e && _e.IsRemote(target) ? 1 : 0;
            Send(new CoopMsg.W(CoopMsg.Dart).V3(tube).V3(dir).U8(who).F(flight).V3(off).I32(seed).F(blast).F(trail).Done());
        }

        void OnBark(string speaker, string line) => Send(new CoopMsg.W(CoopMsg.Bark).Str(speaker).Str(line).Done());

        void OnWeakDamaged(CitadelWeakPoint p, float dmg, Vector3 at, Vector3 n)
        {
            var w = CitadelWeakPoints.Current;
            int i = w ? w.Points.IndexOf(p) : -1;
            if (i >= 0) Send(new CoopMsg.W(CoopMsg.WeakHit).U8(i).F(dmg).V3(at).V3(n).Done());
        }

        void OnPodDamaged(HuskBossPart p, float dmg, Vector3 at, Vector3 n)
        {
            int k = PodIndex(p);
            if (k >= 0) Send(new CoopMsg.W(CoopMsg.PodHit).U8(k).F(dmg).V3(at).V3(n).Done());
        }

        void HostTick()
        {
            if (!_e) return;
            var lv = _e.Level;
            if (lv && _barkLevel != lv) { if (_barkLevel) _barkLevel.Radio -= OnBark; _barkLevel = lv; lv.Radio += OnBark; }
            if (!Ready) return;
            float now = Time.unscaledTime;

            // HUSK poses + states, 20 Hz
            if (now >= _snapT)
            {
                _snapT = now + SnapEvery;
                var w = new CoopMsg.W(CoopMsg.EnemySnap);
                int n = 0;
                foreach (var en in _e.Enemies) if (en) n++;
                w.U16(n);
                foreach (var en in _e.Enemies)
                {
                    if (!en) continue;
                    int id = EnsureId(en);
                    w.U16(id).U8(en.NetState).V3(en.transform.position).Q(en.transform.rotation).V3(en.NetAim);
                }
                Send(w.Done());
            }

            // level flags (on change, else 5 Hz)
            int beat = lv ? (int)lv.beat : 0;
            int flags = (_e.Engaged ? 1 : 0) | (_e.hostileEnabled ? 8 : 0);
            int sig = beat * 16 + flags;
            if (sig != _levelSig || now >= _levelT)
            {
                _levelSig = sig; _levelT = now + 0.2f;
                Send(new CoopMsg.W(CoopMsg.Level).U8(beat).U8(flags).Done());
            }
            Beat = beat;

            // the CITADEL
            var be = _e.boss;
            if (be && be.boss)
            {
                if (be.Playing && !_bossPlaying)
                {
                    int mask = 0;
                    if (be.actors != null)
                        foreach (var a in be.actors)
                        {
                            if (!a) continue;
                            if (_e.IsRemote(a)) mask |= 2; else if (a == _e.Player) mask |= 1;
                        }
                    Send(new CoopMsg.W(CoopMsg.BossStart).U8(mask).Done());
                }
                _bossPlaying = be.Playing;
                if (!_bossLiveSent && CitadelWeakPoints.Current) { _bossLiveSent = true; Send(new[] { CoopMsg.BossLive }); }
                bool victory = lv && lv.beat == HuskLevel.Beat.Victory;
                if (victory && !_victorySent) { _victorySent = true; Send(new CoopMsg.W(CoopMsg.Victory).U8(0).Done()); }
                if ((be.Done || victory) && now >= _bossT)
                {
                    _bossT = now + BossEvery;
                    Send(new CoopMsg.W(CoopMsg.BossPose).V3(be.boss.position).Q(be.boss.rotation).Done());
                }
            }
        }

        // the guest's hits arrive here and are applied as if this PC's pilot made them (their results go back)
        void HostReceive(CoopMsg.R r)
        {
            switch (r.Type)
            {
                case CoopMsg.HitEnemy:
                {
                    int id = r.U16(); float dmg = r.F(); Vector3 dir = r.V3(); float power = r.F();
                    if (_byId.TryGetValue(id, out var en) && en && en.Alive) en.TakeDamage(dmg, dir, power);
                    break;
                }
                case CoopMsg.HitWeak:
                {
                    int i = r.U8(); float dmg = r.F(); Vector3 p = r.V3(), n = r.V3();
                    var w = CitadelWeakPoints.Current;
                    if (w && i < w.Points.Count && w.Points[i] && !w.Points[i].Broken) w.Points[i].Hit(dmg, p, n);
                    break;
                }
                case CoopMsg.HitPod:
                {
                    int k = r.U8(); float dmg = r.F(); Vector3 p = r.V3(), n = r.V3();
                    var pod = Pod(k);
                    if (pod && !pod.Broken) pod.Hit(dmg, p, n);
                    break;
                }
            }
        }

        // ================================================================================================ guest
        bool Route(Component target, float dmg, Vector3 point, Vector3 dir, float power)
        {
            if (!_s || !_s.Connected) return true;                       // no host: nothing lands
            switch (target)
            {
                case HuskEnemy en:
                    if (en.netId >= 0) _s.Send(new CoopMsg.W(CoopMsg.HitEnemy).U16(en.netId).F(dmg).V3(dir).F(power).Done());
                    return true;
                case CitadelWeakPoint wp:
                {
                    var w = CitadelWeakPoints.Current;
                    int i = w ? w.Points.IndexOf(wp) : -1;
                    if (i >= 0) _s.Send(new CoopMsg.W(CoopMsg.HitWeak).U8(i).F(dmg).V3(point).V3(dir).Done());
                    return true;
                }
                case HuskBossPart pod:
                {
                    int k = PodIndex(pod);
                    if (k >= 0) _s.Send(new CoopMsg.W(CoopMsg.HitPod).U8(k).F(dmg).V3(point).V3(dir).Done());
                    return true;
                }
            }
            return false;
        }

        void GuestReceive(CoopMsg.R r)
        {
            float now = Time.unscaledTime;
            switch (r.Type)
            {
                case CoopMsg.Spawn:
                {
                    int id = r.U16(); int kind = r.U8(); Vector3 p = r.V3(); Quaternion q = r.Q();
                    if (_tracks.ContainsKey(id) || !_e) break;
                    HideDecor(p);
                    var en = _e.Spawn((HuskEnemy.Kind)kind, p, q, -1f);
                    en.MakeMirror(id);
                    _tracks[id] = new Track { en = en, p0 = p, p1 = p, r0 = q, r1 = q, t0 = now, t1 = now + SnapEvery, seen = now };
                    break;
                }
                case CoopMsg.EnemySnap:
                {
                    int n = r.U16();
                    _lastSnap = now;
                    for (int k = 0; k < n; k++)
                    {
                        int id = r.U16(); int st = r.U8(); Vector3 p = r.V3(); Quaternion q = r.Q(); Vector3 aim = r.V3();
                        if (!_tracks.TryGetValue(id, out var tr) || !tr.en) continue;
                        tr.p0 = tr.en.transform.position; tr.r0 = tr.en.transform.rotation; tr.t0 = now;
                        tr.p1 = p; tr.r1 = q; tr.t1 = now + SnapEvery; tr.seen = now;
                        tr.en.MirrorState(st);
                        tr.en.MirrorAim(aim);
                    }
                    break;
                }
                case CoopMsg.EnemyFx:
                {
                    int id = r.U16(); int fx = r.U8(); Vector3 a = r.V3(), b = r.V3();
                    if (_tracks.TryGetValue(id, out var tr) && tr.en) tr.en.MirrorFx(fx, a, b);
                    break;
                }
                case CoopMsg.EnemyDamage:
                {
                    int id = r.U16(); float dmg = r.F(); Vector3 dir = r.V3(); float power = r.F();
                    if (_tracks.TryGetValue(id, out var tr) && tr.en) tr.en.MirrorDamage(dmg, dir, power);
                    break;
                }
                case CoopMsg.Dart:
                {
                    Vector3 tube = r.V3(), dir = r.V3(); int who = r.U8(); float flight = r.F(); Vector3 off = r.V3();
                    int seed = r.I32(); float blast = r.F(), trail = r.F();
                    if (!_e) break;
                    Transform target = who == 1 ? _e.Player : (_e.Remote != null ? _e.Remote.t : _e.Player);
                    _e.LaunchDartAt(tube, dir, target, flight, off, seed, blast, trail);
                    break;
                }
                case CoopMsg.Level:
                {
                    int beat = r.U8(), flags = r.U8();
                    Beat = beat;
                    if (_e)
                    {
                        _e.Engaged = (flags & 1) != 0;
                        _e.hostileEnabled = (flags & 8) != 0;
                    }
                    break;
                }
                case CoopMsg.Bark:
                {
                    string who = r.Str(), line = r.Str();
                    if (_ui && !string.IsNullOrEmpty(line))
                    {
                        _ui.Say(who, line, who == "KESTREL" ? KestrelAccent : OrbitAccent);
                        _barkClear = now + Mathf.Clamp(1.6f + line.Length * 0.06f, 2.2f, 3.4f);
                    }
                    break;
                }
                case CoopMsg.BossStart:
                {
                    int mask = r.U8();
                    var be = _e ? _e.boss : null;
                    if (!be || !be.boss || be.Playing) break;
                    var list = new List<Transform>();
                    var hostPilot = _e.Remote != null ? _e.Remote.t : null;
                    if ((mask & 1) != 0 && hostPilot) list.Add(hostPilot);
                    if ((mask & 2) != 0 && _e.Player) list.Add(_e.Player);
                    if (list.Count == 0 && _e.Player) list.Add(_e.Player);
                    be.actors = list.ToArray();
                    be.Play();
                    Debug.Log("[OYM-COOP] CITADEL entrance (host's cue)");
                    break;
                }
                case CoopMsg.BossLive: BossLive(); break;
                case CoopMsg.BossPose:
                {
                    Vector3 p = r.V3(); Quaternion q = r.Q();
                    var bt = _e && _e.boss ? _e.boss.boss : null;
                    bool follow = bt && _bossT1 > 0f;
                    _bossP0 = follow ? bt.position : p; _bossR0 = follow ? bt.rotation : q;
                    _bossP1 = p; _bossR1 = q; _bossT0 = now; _bossT1 = now + BossEvery;
                    break;
                }
                case CoopMsg.WeakHit:
                {
                    int i = r.U8(); float dmg = r.F(); Vector3 p = r.V3(), n = r.V3();
                    if (!CitadelWeakPoints.Current) BossLive();
                    var w = CitadelWeakPoints.Current;
                    if (w && i < w.Points.Count && w.Points[i]) w.Points[i].Hit(dmg, p, n);
                    break;
                }
                case CoopMsg.PodHit:
                {
                    int k = r.U8(); float dmg = r.F(); Vector3 p = r.V3(), n = r.V3();
                    if (!_bossLive) BossLive();
                    var pod = Pod(k);
                    if (pod) pod.Hit(dmg, p, n);
                    break;
                }
                case CoopMsg.Victory:
                {
                    _victory = true;
                    var be = _e ? _e.boss : null;
                    if (be) be.enabled = false;                          // its idle float stops: the host's pose moves it
                    // Older hosts only announce the start of the sinking animation.
                    _hideBossAt = r.U8() == 1 ? now : now + 20f;
                    // whatever HUSK are left burn out on the host: their hits come in as usual
                    break;
                }
            }
        }

        void HideDecor(Vector3 p)
        {
            Transform best = null; float bd = 0.6f * 0.6f;
            foreach (var t in _decor)
            {
                if (!t || !t.gameObject.activeSelf) continue;
                float d = (t.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            if (best) best.gameObject.SetActive(false);
        }

        /// the host installed the CITADEL's lights and pods: the same ones here (positions come from the same bake)
        void BossLive()
        {
            if (_bossLive || !_e || !_e.boss || !_e.boss.boss) return;
            _bossLive = true;
            var boss = _e.boss.boss;
            for (int k = 0; k < 2; k++)
            {
                var pod = FindChild(boss, k == 0 ? "citadel_launcher_L" : "citadel_launcher_R");
                if (!pod || pod.GetComponent<HuskBossPart>()) continue;
                var part = pod.gameObject.AddComponent<HuskBossPart>();
                GameObject twin = null;
                if (pod.parent) { var tw = pod.parent.Find(pod.name + "_damaged"); if (tw) twin = tw.gameObject; }
                part.Setup(_e, k == 0 ? "L" : "R", _e.config.podHP, twin);
            }
            CitadelWeakPoints.Install(_e, boss);
            Debug.Log("[OYM-COOP] CITADEL weak points and pods installed (host's cue)");
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        HuskBossPart Pod(int k)
        {
            if (!_e || !_e.boss || !_e.boss.boss) return null;
            var t = FindChild(_e.boss.boss, k == 0 ? "citadel_launcher_L" : "citadel_launcher_R");
            return t ? t.GetComponent<HuskBossPart>() : null;
        }

        static int PodIndex(HuskBossPart p) => !p ? -1 : p.name == "citadel_launcher_L" ? 0 : p.name == "citadel_launcher_R" ? 1 : -1;

        void GuestTick()
        {
            float now = Time.unscaledTime;
            // the host's HUSK: each copy eases to the host's latest pose over one snapshot interval
            _drop.Clear();
            foreach (var kv in _tracks)
            {
                var tr = kv.Value;
                if (!tr.en) { _drop.Add(kv.Key); continue; }
                if (!tr.en.Alive) continue;
                float k = Mathf.Clamp01((now - tr.t0) / Mathf.Max(1e-3f, tr.t1 - tr.t0));
                tr.en.transform.SetPositionAndRotation(Vector3.Lerp(tr.p0, tr.p1, k), Quaternion.Slerp(tr.r0, tr.r1, k));
                // missing from the host's snapshots for a while (destroyed there) and no hit said so: let it go
                if (_lastSnap > 0f && _lastSnap - tr.seen > 1.5f) tr.en.MirrorGone();
            }
            foreach (int id in _drop) _tracks.Remove(id);

            if (_barkClear > 0f && now >= _barkClear) { _barkClear = -1f; if (_ui) _ui.ClearLine(); }
            if (_hideBossAt > 0f && now >= _hideBossAt && _e && _e.boss && _e.boss.boss)
            {
                _hideBossAt = -1f;
                foreach (var rr in _e.boss.boss.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
                ShowcaseEnding.Begin(_e);
            }
        }

        void GuestLate()
        {
            float now = Time.unscaledTime;
            // the CITADEL follows the host's pose once its own entrance is over (and through the victory)
            var be = _e ? _e.boss : null;
            if (be && be.boss && _bossT1 > 0f && (be.Done || _victory) && !be.Playing)
            {
                float k = Mathf.Clamp01((now - _bossT0) / Mathf.Max(1e-3f, _bossT1 - _bossT0));
                be.boss.SetPositionAndRotation(Vector3.Lerp(_bossP0, _bossP1, k), Quaternion.Slerp(_bossR0, _bossR1, k));
            }
        }

        // ================================================================================================ loop
        public void Receive(CoopMsg.R r)
        {
            if (_host) HostReceive(r); else GuestReceive(r);
        }

        void Update()
        {
            if (!_e) _e = HuskEncounter.Current;
            if (_host) HostTick(); else GuestTick();
        }

        void LateUpdate()
        {
            if (!_host) GuestLate();
        }
    }
}
