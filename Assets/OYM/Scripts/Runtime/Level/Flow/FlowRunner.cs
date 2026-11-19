using System.Collections.Generic;
using UnityEngine;

namespace OYM.Flow
{
    /// What the flow needs from the game: the live HUSK encounter on the host (LevelFlow), or the editor's timing
    /// simulation (OYMLevelFlow). Handles are small ints for the HUSK the flow keeps an eye on.
    public interface IFlowWorld
    {
        float Now { get; }
        /// the player has the controls (no arrival cutscene, no boss entrance): breathers and timeouts run only then
        bool Running { get; }
        /// HuskLevel called first contact (the start-up pack wakes)
        bool ContactStarted { get; }
        void ContactPack(List<int> into);
        bool HasZone(string zone);
        /// the zone stops waking by proximity until UnlockZone
        void LockZone(string zone);
        void UnlockZone(string zone);
        /// wake a zone now, wherever the pilots are (its HUSK come for them); false = not yet (asked again later)
        bool WakeZone(string zone);
        /// true once the zone woke (all its sleepers swapped for live HUSK); `into` gets their handles
        bool ZoneWoken(string zone, List<int> into);
        int Spawn(FlowSpawn s);
        bool Alive(int handle);
        bool AnyPilotWithin(Vector3 p, float r);
        void Say(string who, string line);
        /// hold / release HuskLevel's finale (the CITADEL comes after the nest zone is cleared)
        void HoldBoss(bool hold);
        void Log(string msg);
    }

    /// The sector / wave state machine of a LevelFlowPlan. No Unity objects in here: the same code runs in the game and
    /// in the editor's timing simulation.
    ///   sector   Locked until `unlockAfter` s after the sector before it is clear (the first one at once), then Active,
    ///            then Clear when every wave has started and none of their HUSK is left (or `clearTimeout` s after the
    ///            last wave started: stragglers keep fighting, the route goes on)
    ///   wave     in order: the first when its sector activates, each next one when the HUSK of the waves before it are
    ///            down to `whenAliveAtMost` (or `orAfter` s after the wave before it started), then `delay` s. A zone wave
    ///            unlocks its HuskZones zone and starts when the zone wakes (a pilot came close, or `forceWakeAfter`);
    ///            '@contact' starts when HuskLevel calls first contact; a spawn wave sends its HUSK in 0.4 s apart.
    ///   finale   HuskLevel's CITADEL is held from the start and released `bossBreather` s after the last sector is clear.
    public sealed class FlowRunner
    {
        public const string Contact = "@contact";
        public const float SpawnGap = 0.4f;
        public enum SectorState { Locked, Active, Clear }

        sealed class WaveRun
        {
            public bool triggered, unlocked, started;
            public float triggerAt = -1f, unlockAt = -1f, startAt = -1f;
            public int next;                                    // spawn waves: the next spawn point to send in
            public readonly List<int> members = new List<int>();
        }

        sealed class SectorRun
        {
            public SectorState state;
            public float unlockAt, activeAt = -1f, clearAt = -1f;
            public int leftAtClear;
            public WaveRun[] waves;
        }

        readonly LevelFlowPlan _plan;
        readonly IFlowWorld _w;
        readonly SectorRun[] _s;
        readonly List<int> _tmp = new List<int>();
        int _cur;
        bool _released;
        float _releaseAt = -1f;

        /// (sector) it unlocked: it is the target now
        public event System.Action<int> SectorActivated;
        /// (sector) all its HUSK are down (or its clear timeout ran out)
        public event System.Action<int> SectorCleared;
        /// (sector, wave)
        public event System.Action<int, int> WaveStarted;
        public event System.Action BossReleased;

        public LevelFlowPlan Plan => _plan;
        /// index of the sector in play (== Count once all are clear)
        public int Current => _cur;
        public int Count => _s.Length;
        public bool Finished => _cur >= _s.Length;
        public bool Released => _released;
        public float ReleaseAt => _releaseAt;
        public SectorState StateOf(int i) => _s[i].state;
        public float ActiveAt(int i) => _s[i].activeAt;
        public float ClearAt(int i) => _s[i].clearAt;
        public int LeftAtClear(int i) => _s[i].leftAtClear;
        public float WaveStartAt(int i, int w) => _s[i].waves[w].startAt;
        public int WaveMembers(int i, int w) => _s[i].waves[w].members.Count;

        public FlowRunner(LevelFlowPlan plan, IFlowWorld world)
        {
            _plan = plan; _w = world;
            _s = new SectorRun[plan.sectors.Count];
            for (int i = 0; i < _s.Length; i++)
            {
                var sp = plan.sectors[i];
                _s[i] = new SectorRun { waves = new WaveRun[sp.waves.Count] };
                for (int k = 0; k < sp.waves.Count; k++) _s[i].waves[k] = new WaveRun();
            }
            // every zone of the plan waits for its turn
            foreach (var sp in plan.sectors)
                foreach (var wv in sp.waves)
                {
                    if (string.IsNullOrEmpty(wv.zone) || wv.zone == Contact) continue;
                    if (_w.HasZone(wv.zone)) _w.LockZone(wv.zone);
                    else _w.Log($"wave '{wv.name}': zone '{wv.zone}' is not in the scene - skipped (run OYM/Level/Flow/Build again after a level rebuild)");
                }
            if (_s.Length > 0) _w.HoldBoss(true);
        }

        /// HUSK still up in the waves [0, upTo) of sector i (spawn points not sent in yet count as up)
        public int Alive(int i, int upTo)
        {
            var sr = _s[i];
            var sp = _plan.sectors[i];
            int n = 0;
            for (int k = 0; k < upTo && k < sr.waves.Length; k++)
            {
                var wr = sr.waves[k];
                foreach (int h in wr.members) if (_w.Alive(h)) n++;
                if (wr.started) n += Mathf.Max(0, sp.waves[k].spawns.Count - wr.next);
            }
            return n;
        }

        public void Tick()
        {
            if (!_w.Running) return;
            float now = _w.Now;
            if (Finished)
            {
                if (!_released && _releaseAt >= 0f && now >= _releaseAt)
                {
                    _released = true;
                    _w.HoldBoss(false);
                    _w.Log("finale released: HuskLevel's quiet beat, then the CITADEL");
                    BossReleased?.Invoke();
                }
                return;
            }

            var sr = _s[_cur];
            var sp = _plan.sectors[_cur];
            if (sr.state == SectorState.Locked)
            {
                if (now < sr.unlockAt) return;
                sr.state = SectorState.Active; sr.activeAt = now;
                _w.Log($"sector {_cur} '{sp.name}' active");
                if (!string.IsNullOrEmpty(sp.startBark)) _w.Say(sp.startBy, sp.startBark);
                SectorActivated?.Invoke(_cur);
            }

            for (int k = 0; k < sr.waves.Length; k++)
            {
                var wr = sr.waves[k];
                var wp = sp.waves[k];
                if (!wr.triggered)
                {
                    if (k > 0)
                    {
                        var prev = sr.waves[k - 1];
                        if (!prev.started) break;
                        if (Alive(_cur, k) > wp.whenAliveAtMost && now - prev.startAt < wp.orAfter) break;
                    }
                    wr.triggered = true; wr.triggerAt = now;
                }
                if (!wr.started)
                {
                    if (now < wr.triggerAt + wp.delay) break;
                    if (!Begin(wr, wp, sp, now)) break;
                    wr.started = true; wr.startAt = now;
                    _w.Log($"sector {_cur} '{sp.name}' wave {k} '{wp.name}' starts: {Describe(wp, wr)}");
                    if (!string.IsNullOrEmpty(wp.bark)) _w.Say(wp.barkBy, wp.bark);
                    WaveStarted?.Invoke(_cur, k);
                }
                // a spawn wave streams in
                while (wr.next < wp.spawns.Count && now >= wr.startAt + wr.next * SpawnGap)
                    wr.members.Add(_w.Spawn(wp.spawns[wr.next++]));
            }

            // clear: every wave started and nothing left of them (or the timeout after the last one)
            float last = 0f;
            foreach (var wr in sr.waves)
            {
                if (!wr.started) return;
                last = Mathf.Max(last, wr.startAt);
            }
            int left = Alive(_cur, sr.waves.Length);
            if (left > 0 && now - last < sp.clearTimeout) return;
            sr.state = SectorState.Clear; sr.clearAt = now; sr.leftAtClear = left;
            _w.Log($"sector {_cur} '{sp.name}' clear after {now - sr.activeAt:F0} s" + (left > 0 ? $" (timeout: {left} HUSK still up)" : ""));
            if (!string.IsNullOrEmpty(sp.clearBark)) _w.Say(sp.clearBy, sp.clearBark);
            int done = _cur;
            _cur++;
            if (_cur < _s.Length) _s[_cur].unlockAt = now + _plan.sectors[_cur].unlockAfter;
            else _releaseAt = now + _plan.bossBreather;
            SectorCleared?.Invoke(done);
        }

        bool Begin(WaveRun wr, FlowWave wp, FlowSector sp, float now)
        {
            if (wp.zone == Contact)
            {
                if (!_w.ContactStarted) return false;
                _w.ContactPack(wr.members);
                return true;
            }
            if (!string.IsNullOrEmpty(wp.zone))
            {
                if (!_w.HasZone(wp.zone)) return true;                          // not in the scene: skipped (logged above)
                if (!wr.unlocked)
                {
                    wr.unlocked = true; wr.unlockAt = now;
                    _w.UnlockZone(wp.zone);
                    _w.Log($"zone '{wp.zone}' unlocked: it wakes when a pilot comes within its radius");
                }
                else if (wp.forceWakeAfter > 0f && now - wr.unlockAt >= wp.forceWakeAfter && _w.WakeZone(wp.zone))
                {
                    wr.unlockAt = float.MaxValue;                               // once
                    _w.Log($"zone '{wp.zone}': nobody came in {wp.forceWakeAfter:F0} s - woken from afar");
                }
                _tmp.Clear();
                if (!_w.ZoneWoken(wp.zone, _tmp)) return false;
                wr.members.AddRange(_tmp);
                return true;
            }
            return wp.nearAnchor <= 0f || _w.AnyPilotWithin(sp.anchor, wp.nearAnchor);
        }

        static string Describe(FlowWave wp, WaveRun wr)
        {
            if (wp.zone == Contact) return $"start-up pack ({wr.members.Count} HUSK)";
            if (!string.IsNullOrEmpty(wp.zone)) return $"zone '{wp.zone}' woke ({wr.members.Count} HUSK)";
            int p = 0; foreach (var s in wp.spawns) if (s.prism) p++;
            return $"{wp.spawns.Count - p} WICK + {p} PRISM inbound";
        }
    }
}
