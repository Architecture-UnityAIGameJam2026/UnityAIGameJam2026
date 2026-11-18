using System.Collections.Generic;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// ORBIT FRAME E - multi-lock micro-missile salvo.
    ///   hold E     the seeker opens a lock box round the crosshair (lockCone) and locks the HUSK / CITADEL weak points /
    ///              launcher pods inside it one after another (nearest the crosshair first, lockTime each; a target held
    ///              by the T / MMB hard lock goes first and faster), up to maxLocks. A lock holds while its target lives,
    ///              stays in sight and within keepCone of the aim; out of sight for lostAfter s it drops (slot frees up).
    ///   release    the salvo: salvoSize missiles ripple out of the shoulder pods (cold launch, then ignition) and curve
    ///              onto the locks round-robin (3 locks = 2 each, 1 lock = all on it); no lock = they fly at the crosshair.
    ///              A missile whose target died on the way takes another lock of the salvo. Then the cooldown (10 s);
    ///              E during the cooldown is a short deny blip; E held through the end of it opens the seeker.
    /// Hits go through the RG-77's doors (HuskEncounter.RouteHit for the co-op guest, then TakeDamage / Hit) plus a small
    /// splash on other HUSK; no friendly fire. Targets, aim points and sight are LockOn's rules (one source of truth).
    /// Self-installs on the ORBIT FRAME of every scene with a PlayTestDirector (no scene / prefab edits; -oymNoSalvo = off).
    /// Only the pilot the camera follows reads E; a scripted pilot (demo / check) is driven through scriptedHold.
    /// SalvoOverlay draws the lock box, marks and cooldown; SalvoAudio makes the lock tones and plays the clips.
    [DefaultExecutionOrder(95)]                       // after LockOn (90): this frame's hard lock; before FlightCamera (100)
    public class OrbitSalvo : MonoBehaviour
    {
        [Header("Lock (hold E)")]
        public int maxLocks = 3;
        [Tooltip("half-angle (deg) of the lock box round the crosshair")]
        public float lockCone = 12f;
        public float lockRange = 320f;
        [Tooltip("seconds the seeker dwells on a target before it locks; a target under the T / MMB hard lock: hardLockTime")]
        public float lockTime = 0.24f, hardLockTime = 0.1f;
        [Tooltip("a lock holds while its target stays within this angle (deg) of the aim")]
        public float keepCone = 75f;
        [Tooltip("seconds out of sight before a lock drops")]
        public float lostAfter = 0.9f;

        [Header("Salvo (release E)")]
        public float cooldown = 10f;
        public int salvoSize = 6;
        [Tooltip("seconds between missiles in the ripple")]
        public float ripple = 0.065f;
        [Tooltip("damage of one missile (WICK 30 hp, PRISM 90, weak point 200, pod 300; one RG-77 shot = 75)")]
        public float damage = 48f;
        [Tooltip("other HUSK within this radius (m) take splashFactor of the damage (half of that at the edge)")]
        public float splashRadius = 3.5f, splashFactor = 0.5f;
        [Tooltip("average missile speed (m/s): flight time = distance / speed + the eject, clamped")]
        public float missileSpeed = 90f;
        public float minFlight = 0.7f, maxFlight = 3.2f;
        [Tooltip("no lock: the salvo flies at the crosshair, this far at most (m)")]
        public float dumbRange = 260f;

        [Header("Sound")]
        [Range(0f, 1.5f)] public float toneVolume = 0.7f;
        [Range(0f, 1.5f)] public float launchVolume = 0.85f;
        [Range(0f, 1.5f)] public float blastVolume = 1f;

        [Header("Scripted (demo / check)")]
        [Tooltip("used instead of the E key while the pilot is scripted")]
        public bool scriptedHold;

        public struct Mark
        {
            public Component owner;
            public LockOn.Kind kind;
            public Vector3 point;
            public float radius;
            /// Time.unscaledTime it locked; seconds out of sight
            public float at, blocked;
            /// 1-based lock number
            public int number;
            /// missiles of the last salvo still on their way to it
            public int inbound;
        }

        /// the salvo of the pilot the camera follows (the overlay draws this one)
        public static OrbitSalvo Active { get; private set; }
        /// (salvo, tag): open, seek, lock, full, lost, cancel, fire, deny, ready
        public static event System.Action<OrbitSalvo, string> Event;

        public bool Locking => _locking;
        public float LockingFor => _locking ? Time.unscaledTime - _lockStart : 0f;
        public IReadOnlyList<Mark> Locks => _locks;
        /// targets of the salvo in the air (their marks stay until the last missile on them lands)
        public IReadOnlyList<Mark> Inbound => _inbound;
        public bool HasSeek => _seek.owner;
        public Mark Seek => _seek;
        public float Seek01 => _seek.owner ? Mathf.Clamp01(_seekT / Mathf.Max(0.01f, SeekTime(_seek.owner))) : 0f;
        public float CooldownLeft => _cooling ? Mathf.Max(0f, _readyAt - Time.time) : 0f;
        public float Cooldown01 => cooldown > 0f ? Mathf.Clamp01(1f - CooldownLeft / cooldown) : 1f;
        public bool Ready => !_cooling && _pending == 0;
        public float DeniedAt { get; private set; } = -99f;
        /// Time.unscaledTime the cooldown ran out / the last salvo fired (the overlay's flashes)
        public float ReadyAt { get; private set; } = -99f;
        public float FiredAt { get; private set; } = -99f;
        public int Salvos { get; private set; }
        public int InFlight { get; private set; }
        /// missiles launched / landed since the scene started (checks)
        public int Launched { get; private set; }
        public int Landed { get; private set; }
        public ZeroGFlight Flight => _flight;

        ZeroGFlight _flight;
        Transform _chest;
        SalvoAudio _audio;
        bool _locking, _wasHeld, _cooling, _dumb;
        float _lockStart, _readyAt, _seekT, _nextLaunch;
        int _pending, _index, _seed;
        Mark _seek;
        Vector3 _dumbPoint;
        Component _dumbTarget;
        readonly List<Mark> _locks = new List<Mark>(4);
        readonly List<Mark> _inbound = new List<Mark>(4);
        readonly List<Component> _salvo = new List<Component>(4);
        readonly List<LockOn.Target> _cands = new List<LockOn.Target>(32);
        readonly List<SalvoRocket> _pool = new List<SalvoRocket>(12);
        Transform _missileRoot;
        static readonly Collider[] s_cols = new Collider[32];

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Active = null; Event = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || SignalInstall.Flag("-oymNoSalvo")) return;
            int n = 0;
            foreach (var root in s.GetRootGameObjects())
                foreach (var d in root.GetComponentsInChildren<PlayTestDirector>(true))
                {
                    if (d.controllers == null) continue;
                    foreach (var c in d.controllers)
                        if (c is OrbitFrameController && !c.GetComponent<OrbitSalvo>()) { c.gameObject.AddComponent<OrbitSalvo>(); n++; }
                }
            if (n == 0) return;
            var go = new GameObject("Salvo overlay (ORBIT FRAME E)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<SalvoOverlay>();
            Debug.Log($"[OYM-SALVO] E multi-lock salvo on {n} ORBIT FRAME in {s.name}");
        }

        void Awake()
        {
            _flight = GetComponent<ZeroGFlight>();
            _seed = (GetInstanceID() & 0x7fff) + 11;
            var anim = _flight && _flight.animator ? _flight.animator : GetComponentInChildren<Animator>();
            if (anim && anim.isHuman)
            {
                _chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                if (!_chest) _chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            }
            _audio = new SalvoAudio(transform);
        }

        void OnDisable()
        {
            if (_locking) Cancel();
            if (Active == this) Active = null;
        }

        void OnDestroy()
        {
            if (_missileRoot) Destroy(_missileRoot.gameObject);
            _audio?.Dispose();
        }

        static void Emit(OrbitSalvo s, string tag) => Event?.Invoke(s, tag);

        // ------------------------------------------------------------------------------------------------ loop
        void LateUpdate()
        {
            var fc = FlightCamera.Main;
            bool mine = _flight && _flight.isActiveAndEnabled && fc && fc.target == transform;
            if (mine) Active = this;
            else if (Active == this) Active = null;
            _audio.Volumes(toneVolume, launchVolume, blastVolume);

            TickLaunch();                                                      // the ripple finishes whoever has the camera
            if (_cooling && Time.time >= _readyAt)
            {
                _cooling = false;
                ReadyAt = Time.unscaledTime;
                if (mine) _audio.Ready();
                Emit(this, "ready");
            }
            RefreshInbound();
            if (Time.timeScale <= 0f) return;                                  // paused

            var e = HuskEncounter.Current;
            bool canAct = mine && SignalInstall.PlayerView(e) && (!e || e.PlayerAlive);
            bool held = canAct && Held(fc);
            if (_locking)
            {
                if (!canAct) Cancel();                                         // cutscene / downed / Tab: no salvo, no cooldown
                else if (!held) Fire(fc);
                else TickLocks(fc);
            }
            else if (held && Ready) BeginLock();                               // also when E was held through the cooldown
            else if (held && !_wasHeld) Deny();
            _wasHeld = held;
        }

        bool Held(FlightCamera fc)
        {
            if (_flight.scripted) return scriptedHold;
            if (fc && !fc.InputLive) return false;                             // mouse not captured: no game input
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k.eKey.isPressed;
#else
            return Input.GetKey(KeyCode.E);
#endif
        }

        // ------------------------------------------------------------------------------------------------ seeker
        void BeginLock()
        {
            _locking = true;
            _lockStart = Time.unscaledTime;
            _locks.Clear();
            _seek = default; _seekT = 0f;
            _audio.Open();
            Emit(this, "open");
        }

        void Cancel()
        {
            _locking = false;
            _locks.Clear();
            _seek = default; _seekT = 0f;
            Emit(this, "cancel");
        }

        void Deny()
        {
            DeniedAt = Time.unscaledTime;
            _audio.Deny();
            Emit(this, "deny");
        }

        float SeekTime(Component c)
        {
            var lk = LockOn.Current;
            return lk && lk.Locked && lk.Hard.owner == c ? hardLockTime : lockTime;
        }

        bool Has(Component c)
        {
            for (int i = 0; i < _locks.Count; i++) if (_locks[i].owner == c) return true;
            return false;
        }

        void TickLocks(FlightCamera fc)
        {
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);
            Transform player = fc.target;
            Vector3 eye = fc.transform.position, fwd = fc.transform.forward;

            // keep or drop the locks (a kill frees the slot quietly, losing sight / swinging away says so)
            for (int i = _locks.Count - 1; i >= 0; i--)
            {
                var m = _locks[i];
                if (!LockOn.IsLockable(m.owner)) { _locks.RemoveAt(i); continue; }
                var t = LockOn.TargetOf(m.owner);
                m.point = t.point; m.radius = t.radius;
                m.blocked = LockOn.InSight(t, eye, player) ? 0f : m.blocked + dt;
                Vector3 d = t.point - eye;
                if (m.blocked > lostAfter || Vector3.Angle(fwd, d) > keepCone || d.magnitude > lockRange * 1.2f)
                {
                    _locks.RemoveAt(i);
                    _audio.Lost();
                    Emit(this, "lost");
                    continue;
                }
                _locks[i] = m;
            }
            for (int i = 0; i < _locks.Count; i++) { var m = _locks[i]; m.number = i + 1; _locks[i] = m; }
            if (_locks.Count >= maxLocks) { _seek = default; _seekT = 0f; return; }

            // the seeker: the hard-locked target first, then the one nearest the crosshair inside the lock box
            LockOn.Targets(_cands);
            var lk = LockOn.Current;
            Component hard = lk && lk.Locked ? lk.Hard.owner : null;
            LockOn.Target best = default;
            float bs = float.MaxValue;
            bool seekStill = false;
            foreach (var t in _cands)
            {
                if (Has(t.owner)) continue;
                Vector3 d = t.point - eye;
                float dist = d.magnitude;
                if (dist > lockRange || dist < 1f) continue;
                bool isHard = hard && t.owner == hard;
                float ang = Vector3.Angle(fwd, d) - Mathf.Atan2(t.radius, dist) * Mathf.Rad2Deg;
                if (ang > (isHard ? keepCone : lockCone)) continue;
                if (!LockOn.InSight(t, eye, player)) continue;
                if (t.owner == _seek.owner) seekStill = true;
                float s = isHard ? -1000f : ang + dist * 0.004f;
                if (s < bs) { bs = s; best = t; }
            }
            // stay on the target being locked while it is still in the box (no flicker between two close ones),
            // unless the hard lock just turned up
            bool hardArrived = hard && best.owner == hard && _seek.owner != hard;
            if (_seek.owner && seekStill && !hardArrived)
            {
                var cur = LockOn.TargetOf(_seek.owner);
                _seek.point = cur.point; _seek.radius = cur.radius;
            }
            else if (best.owner)
            {
                _seek = new Mark { owner = best.owner, kind = best.kind, point = best.point, radius = best.radius, at = Time.unscaledTime };
                _seekT = 0f;
                _audio.Seek();
                Emit(this, "seek");
            }
            else { _seek = default; _seekT = 0f; return; }

            _seekT += dt;
            if (_seekT >= SeekTime(_seek.owner))
            {
                var m = _seek;
                m.at = Time.unscaledTime; m.blocked = 0f; m.number = _locks.Count + 1;
                _locks.Add(m);
                _seek = default; _seekT = 0f;
                bool full = _locks.Count >= maxLocks;
                _audio.Lock(m.number, full);
                Emit(this, full ? "full" : "lock");
            }
        }

        // ------------------------------------------------------------------------------------------------ salvo
        void Fire(FlightCamera fc)
        {
            _locking = false;
            _seek = default; _seekT = 0f;
            _salvo.Clear();
            _inbound.Clear();
            foreach (var m in _locks)
            {
                if (!LockOn.IsLockable(m.owner)) continue;
                _salvo.Add(m.owner);
                var im = m; im.inbound = 0;
                _inbound.Add(im);
            }
            _locks.Clear();
            _dumb = _salvo.Count == 0;
            _dumbTarget = null;
            if (_dumb)
            {
                _dumbPoint = fc.AimPoint(dumbRange, transform, out var h, out bool did);
                if (did) _dumbTarget = LockableOf(h.collider);
            }
            Salvos++;
            _pending = Mathf.Max(1, salvoSize);
            _index = 0;
            _nextLaunch = Time.time;
            _cooling = true;
            _readyAt = Time.time + cooldown;
            FiredAt = Time.unscaledTime;
            _audio.Launch();
            Emit(this, "fire");
            TickLaunch();
        }

        static Component LockableOf(Collider c)
        {
            if (!c) return null;
            Component o = c.GetComponentInParent<HuskEnemy>();
            if (!o) o = c.GetComponentInParent<CitadelWeakPoint>();
            if (!o) o = c.GetComponentInParent<HuskBossPart>();
            return o && LockOn.IsLockable(o) ? o : null;
        }

        void TickLaunch()
        {
            while (_pending > 0 && Time.time >= _nextLaunch)
            {
                Launch(_index++);
                _pending--;
                _nextLaunch += ripple;
            }
        }

        void Launch(int i)
        {
            var r = Missile();
            if (!r) return;
            int side = (i & 1) == 0 ? 1 : -1;
            Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
            Vector3 chest = _chest ? _chest.position : transform.position + up * ((_flight ? _flight.bodyCenter : 1f) + 0.45f);
            Vector3 pod = chest + up * 0.3f + right * (side * 0.22f) - fwd * 0.22f;
            int seed = _seed * 131 + Salvos * 17 + i;
            // cold launch: up and out of the pod, a touch forward (never back over the camera), each tube splayed a bit
            // wider, a little scatter
            Vector3 tube = (up + right * (side * (0.35f + 0.1f * (i >> 1))) + fwd * 0.15f + HuskFX.Dir(seed, 5) * 0.1f).normalized;

            Component target;
            Vector3 end;
            if (_dumb)
            {
                target = _dumbTarget;
                // a loose cluster round the crosshair point (golden-angle spiral, wider further out)
                Vector3 aim = _dumbPoint - pod;
                Vector3 n = aim.sqrMagnitude > 1e-4f ? aim.normalized : fwd;
                Vector3 u = Vector3.Cross(n, Vector3.up);
                if (u.sqrMagnitude < 1e-4f) u = right;
                u.Normalize();
                Vector3 v = Vector3.Cross(n, u);
                float a = i * 2.39996f, rad = (0.5f + 0.3f * i) * Mathf.Clamp(aim.magnitude / 80f, 0.4f, 2f);
                end = target ? LockOn.TargetOf(target).point : _dumbPoint + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rad;
            }
            else
            {
                target = _salvo[i % _salvo.Count];
                if (LockOn.IsLockable(target)) AddInbound(target, 1);
                else
                {
                    // the earlier missiles already got it: this one takes another lock of the salvo (none: its last point)
                    var alt = Retarget(target, pod);
                    if (alt) target = alt;
                }
                end = LockOn.IsLockable(target) ? LockOn.TargetOf(target).point : MarkPoint(target, pod + fwd * 50f);
            }
            float T = Mathf.Clamp(Vector3.Distance(pod, end) / Mathf.Max(10f, missileSpeed) + r.ejectTime + 0.15f, minFlight, maxFlight);
            r.Launch(this, pod, tube, target, end, T, seed, side);
            InFlight++; Launched++;
            _audio.Pop(pod, i);

            var fc = FlightCamera.Main;
            if (fc && fc.target == transform)
            {
                if (i == 0) { fc.Shake(0.16f); fc.FovKick(_flight && _flight.FirstPerson ? 70f : 45f); }
                fc.Kick(12f, side * 9f);
            }
        }

        SalvoRocket Missile()
        {
            foreach (var r in _pool) if (r && !r.Busy) return r;
            if (_pool.Count >= 24) return null;
            if (!_missileRoot)
            {
                var root = new GameObject("Salvo missiles (" + name + ")");
                SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                _missileRoot = root.transform;
            }
            var nr = SalvoRocket.Create(_missileRoot, _pool.Count);
            _pool.Add(nr);
            return nr;
        }

        Vector3 MarkPoint(Component c, Vector3 fallback)
        {
            foreach (var m in _inbound) if (m.owner == c) return m.point;
            return fallback;
        }

        void AddInbound(Component target, int delta)
        {
            for (int i = 0; i < _inbound.Count; i++)
                if (_inbound[i].owner == target) { var m = _inbound[i]; m.inbound = Mathf.Max(0, m.inbound + delta); _inbound[i] = m; return; }
        }

        void RefreshInbound()
        {
            for (int i = _inbound.Count - 1; i >= 0; i--)
            {
                var m = _inbound[i];
                if (!LockOn.IsLockable(m.owner) || (m.inbound == 0 && _pending == 0)) { _inbound.RemoveAt(i); continue; }
                var t = LockOn.TargetOf(m.owner);
                m.point = t.point; m.radius = t.radius;
                _inbound[i] = m;
            }
        }

        // ------------------------------------------------------------------------------------------------ missiles call back
        /// where a missile on `target` flies now; false = the target is gone (killed / broken)
        internal bool Track(Component target, out Vector3 point)
        {
            if (target && LockOn.IsLockable(target)) { point = LockOn.TargetOf(target).point; return true; }
            point = default;
            return false;
        }

        /// another live target of this salvo for a missile whose target died on the way (the nearest one), or null
        internal Component Retarget(Component dead, Vector3 from)
        {
            Component best = null;
            float bd = float.MaxValue;
            foreach (var m in _inbound)
            {
                if (!m.owner || m.owner == dead || !LockOn.IsLockable(m.owner)) continue;
                float d = (m.point - from).sqrMagnitude;
                if (d < bd) { bd = d; best = m.owner; }
            }
            if (best) AddInbound(best, 1);
            return best;
        }

        internal void Impact(Component target, Vector3 p, Vector3 dir)
        {
            InFlight = Mathf.Max(0, InFlight - 1);
            Landed++;
            if (target) AddInbound(target, -1);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            Component direct = target && LockOn.IsLockable(target) ? target : null;
            if (direct) Damage(direct, damage, p, dir);

            // splash: other HUSK caught in the blast (no friendly fire)
            var e = HuskEncounter.Current;
            if (e && splashRadius > 0f && splashFactor > 0f)
            {
                var list = e.Enemies;
                for (int i = 0; i < list.Count; i++)
                {
                    var en = list[i];
                    if (!en || en == direct || !en.Alive) continue;
                    float d = Vector3.Distance(en.transform.position, p);
                    if (d < splashRadius) Damage(en, damage * splashFactor * (1f - 0.5f * d / splashRadius), p, en.transform.position - p);
                }
            }
            // loose debris and practice targets get shoved (never through a HUSK's or the boss's own ZeroGTarget: that
            // would count as an RG-77 hit)
            int n = Physics.OverlapSphereNonAlloc(p, splashRadius, s_cols, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_cols[i];
                if (!c || c.GetComponentInParent<ZeroGFlight>()) continue;
                if (c.GetComponentInParent<HuskEnemy>() || c.GetComponentInParent<CitadelWeakPoint>() || c.GetComponentInParent<HuskBossPart>()) continue;
                Vector3 at = c.bounds.ClosestPoint(p);
                Vector3 push = at - p;
                push = push.sqrMagnitude > 1e-4f ? push.normalized : dir;
                var tgt = c.GetComponentInParent<ZeroGTarget>();
                if (tgt) { tgt.Hit(at, push * 7f, SalvoRocket.Amber); continue; }
                var rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic) rb.AddForceAtPosition(push * 4f, at, ForceMode.VelocityChange);
            }
            _audio.Blast(p);
        }

        static void Damage(Component c, float dmg, Vector3 p, Vector3 dir)
        {
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            switch (c)
            {
                case HuskEnemy en:
                    if (en.Alive && !HuskEncounter.RouteHit(en, dmg, p, dir, 1.3f)) en.TakeDamage(dmg, dir, 1.3f);
                    break;
                case CitadelWeakPoint wp:
                    if (!wp.Broken && !HuskEncounter.RouteHit(wp, dmg, wp.AimPoint, -dir, 0f)) wp.Hit(dmg, wp.AimPoint, -dir);
                    break;
                case HuskBossPart pod:
                    if (!pod.Broken && !HuskEncounter.RouteHit(pod, dmg, p, -dir, 0f)) pod.Hit(dmg, p, -dir);
                    break;
            }
        }
    }
}
