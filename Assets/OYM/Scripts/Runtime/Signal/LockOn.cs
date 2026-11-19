using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Signal
{
    /// Lock-on for the zero-g pilots (game scene only).
    ///   soft lock (FCS): the awake HUSK / CITADEL weak point / launcher pod nearest the crosshair inside a small cone,
    ///                    in range and in sight, is acquired - thin marks only, the mouse stays free
    ///   auto lock:       rest the crosshair on that target (autoDwell s) and it hard-locks by itself; the camera follows
    ///                    it a little more gently than a manual lock. A flick away (> autoBreak deg off it) lets go
    ///                    quietly; swinging the crosshair clearly nearer another target moves the lock onto that one.
    ///   hard lock:       T or the middle mouse button on the soft target; the camera tracks it (RG-77 shots land on it,
    ///                    KESTREL's cuts face it). Out of sight ~1.5 s or swung away = lost; a kill or a broken weak point
    ///                    hands the lock to the next target near it.
    ///   release:         the middle mouse button (or T) while locked. The auto lock then leaves that target alone until
    ///                    the crosshair has been off it for a moment, and everything else for releaseHold s.
    /// MarkOverlay draws the marks; RadioMediator hears the lock events through Signal (auto locks come all the time:
    /// they only say lock_weak, and an auto lock slipping off says nothing).
    [DefaultExecutionOrder(90)]                       // before FlightCamera (100) composes this frame's view
    public class LockOn : MonoBehaviour
    {
        public enum Kind { Wick, Prism, WeakPoint, Pod }

        public struct Target
        {
            public Component owner;
            public Kind kind;
            public Vector3 point;
            public float radius;
            public bool Exists => owner;
        }

        public static LockOn Current { get; private set; }
        /// event tags for the radio: lock, lock_weak, lock_far, relock, lock_lost, lock_lost_prism, los_blocked, two_targets
        public static event System.Action<string> Signal;

        public float range = 420f, softCone = 9f, handoffCone = 22f, keepCone = 80f, assistRate = 9f, lostAfter = 1.5f;
        [Tooltip("rest the crosshair on a target (inside softCone) and it locks by itself")]
        public bool autoLock = true;
        [Tooltip("auto lock: seconds the crosshair rests on it, camera follow rate, flick-away angle (deg) that lets go, how much nearer (deg) another target must be to take the lock, seconds without auto locks after a manual release")]
        public float autoDwell = 0.15f, autoAssistRate = 6f, autoBreak = 22f, switchMargin = 3f, releaseHold = 1.2f;

        public bool Locked => _locked && _hard.owner;
        /// the current lock came from the auto lock (crosshair rested on it), not T / the middle button / a script
        public bool AutoLocked => Locked && _auto;
        public Target Hard => _hard;
        public Target Soft => _soft;
        public bool HasSoft => _soft.owner && !(_hard.owner && _soft.owner == _hard.owner);
        public float LockedFor => Time.unscaledTime - _lockedAt;
        public bool Blocked => _blockedT > 0.25f;
        public int InCone { get; private set; }

        Target _hard, _soft;
        float _lockedAt, _blockedT, _lostAt = -99f, _twoT = -99f;
        Component _lastLost;
        bool _saidBlocked, _locked, _auto;
        // auto lock: the target the crosshair rests on and for how long, the one let go by hand, the pause after it
        Component _cand, _letGo;
        float _candT, _letGoOff, _autoHoldUntil = -99f;
        readonly List<Target> _all = new List<Target>();
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }

        static void Emit(string tag) => Signal?.Invoke(tag);

        // ------------------------------------------------------------------ targets
        static bool Alive(Component c)
        {
            if (!c) return false;
            if (c is HuskEnemy en) return en.Alive && en.IsAwake;
            if (c is CitadelWeakPoint wp) return !wp.Broken && wp.isActiveAndEnabled;
            if (c is HuskBossPart pod) return !pod.Broken;
            return false;
        }

        static Target Make(Component c)
        {
            var t = new Target { owner = c };
            if (c is HuskEnemy en)
            {
                t.kind = en.kind == HuskEnemy.Kind.Prism ? Kind.Prism : Kind.Wick;
                t.point = en.kind == HuskEnemy.Kind.Prism && en.prism ? en.prism.LensWorld : en.transform.position;
                t.radius = en.kind == HuskEnemy.Kind.Prism ? 1.6f : 0.8f;
            }
            else if (c is CitadelWeakPoint wp) { t.kind = Kind.WeakPoint; t.point = wp.AimPoint; t.radius = wp.Radius; }
            else if (c is HuskBossPart pod) { t.kind = Kind.Pod; t.point = pod.Center; t.radius = 9f; }
            return t;
        }

        void Collect() => Gather(_all);

        static void Gather(List<Target> into)
        {
            into.Clear();
            var e = HuskEncounter.Current;
            if (e) foreach (var en in e.Enemies) if (Alive(en)) into.Add(Make(en));
            var w = CitadelWeakPoints.Current;
            if (w)
            {
                foreach (var p in w.Points) if (Alive(p)) into.Add(Make(p));
                foreach (var pod in w.Pods) if (Alive(pod)) into.Add(Make(pod));
            }
        }

        // ------------------------------------------------------------------ shared with OrbitSalvo (ORBIT FRAME E multi-lock)
        /// the same targets, aim points and sight rule as this lock-on, for the E salvo's seeker
        public static bool IsLockable(Component c) => Alive(c);
        public static Target TargetOf(Component c) => Make(c);
        public static bool InSight(in Target t, Vector3 eye, Transform player) => Visible(t, eye, player);
        /// every lockable target right now (clears `into` first)
        public static void Targets(List<Target> into) => Gather(into);

        static bool Visible(in Target t, Vector3 eye, Transform player)
        {
            // the hull has no colliders: a light on the far side is only "visible" through the boss - not lockable
            if (t.owner is CitadelWeakPoint wp && !wp.FacesPoint(eye)) return false;
            Vector3 d = t.point - eye;
            float dist = d.magnitude - t.radius * 0.8f;
            if (dist <= 0.1f) return true;
            int n = Physics.RaycastNonAlloc(eye, d.normalized, s_hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_hits[i].collider;
                if (!col) continue;
                if (player && col.transform.IsChildOf(player)) continue;
                if (col.transform.IsChildOf(t.owner.transform)) continue;
                if (col.GetComponentInParent<HuskEnemy>() || col.GetComponentInParent<CitadelWeakPoint>() || col.GetComponentInParent<HuskBossPart>()) continue;
                return false;
            }
            return true;
        }

        bool Best(Vector3 eye, Vector3 fwd, float cone, Transform player, out Target best, Component skip = null)
        {
            best = default;
            float bs = float.MaxValue;
            int inCone = 0;
            foreach (var t in _all)
            {
                if (t.owner == skip) continue;
                Vector3 d = t.point - eye;
                float dist = d.magnitude;
                if (dist > range || dist < 0.5f) continue;
                float ang = Vector3.Angle(fwd, d) - Mathf.Atan2(t.radius, dist) * Mathf.Rad2Deg;
                if (ang > cone) continue;
                if (!Visible(t, eye, player)) continue;
                inCone++;
                float s = ang + dist * 0.004f;
                if (s < bs) { bs = s; best = t; }
            }
            if (skip == null) InCone = inCone;
            return best.owner;
        }

        public string Describe()
        {
            if (!_hard.owner) return "none";
            float d = FlightCamera.Main ? Vector3.Distance(FlightCamera.Main.transform.position, _hard.point) : 0f;
            switch (_hard.kind)
            {
                case Kind.Prism: return $"PRISM sniper at {d:F0} m";
                case Kind.Wick: return $"WICK drone at {d:F0} m";
                case Kind.WeakPoint: return $"CITADEL glowing weak point ({((CitadelWeakPoint)_hard.owner).part}) at {d:F0} m";
                default: return $"CITADEL launcher pod at {d:F0} m";
            }
        }

        // ------------------------------------------------------------------ loop
        void LateUpdate()
        {
            var fc = FlightCamera.Main;
            var e = HuskEncounter.Current;
            bool view = SignalInstall.PlayerView(e);
            if (!fc || !fc.Cam || !view)
            {
                _soft = default;
                return;
            }
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);
            Transform cam = fc.transform, player = fc.target;
            Vector3 eye = cam.position, fwd = cam.forward;
            Collect();

            // soft lock + the two-under-the-sight note
            Best(eye, fwd, softCone, player, out _soft);
            if (InCone >= 2 && Time.unscaledTime - _twoT > 25f) { _twoT = Time.unscaledTime; Emit("two_targets"); }

            if (Pressed() && fc.InputLive)
            {
                if (_locked) LetGo();                                        // middle button / T: release
                else if (_soft.owner) Lock(_soft, true, false);
            }
            else if (autoLock && fc.InputLive) Auto(eye, fwd, player, dt);

            if (_locked)
            {
                if (!Alive(_hard.owner))
                {
                    // killed / broken: hand the lock to the next target near it, else drop it quietly
                    Vector3 last = _hard.point;
                    if (Best(eye, (last - eye).normalized, handoffCone, player, out var next, _hard.owner)) Lock(next, false, _auto);
                    else { _hard = default; _locked = false; }
                }
                else
                {
                    _hard = Make(_hard.owner);
                    Vector3 d = _hard.point - eye;
                    bool vis = Visible(_hard, eye, player);
                    _blockedT = vis ? 0f : _blockedT + dt;
                    if (_blockedT > 0.6f && !_saidBlocked) { _saidBlocked = true; Emit("los_blocked"); }
                    float off = Vector3.Angle(fwd, d);
                    if (d.magnitude > range * 1.15f || off > keepCone || _blockedT > lostAfter) Release(_hard.owner);
                    else if (_auto && off > autoBreak && fc.InputLive)
                    {
                        // flicked away from an auto lock: let go quietly, no instant re-grab
                        _autoHoldUntil = Time.unscaledTime + 0.4f;
                        Release(null);
                    }
                    else if (!fc.scripted && fc.InputLive)
                    {
                        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                        float k = 1f - Mathf.Exp(-(_auto ? autoAssistRate : assistRate) * dt);
                        fc.AddLook(Mathf.DeltaAngle(fc.Yaw, yaw) * k, Mathf.DeltaAngle(fc.Pitch, pitch) * k);
                    }
                }
            }
        }

        /// the crosshair rests on a target -> it locks by itself; auto-locked and the crosshair swings clearly nearer
        /// another target -> the lock moves onto that one
        void Auto(Vector3 eye, Vector3 fwd, Transform player, float dt)
        {
            // the target let go by hand stays alone until the crosshair has been off it for a moment
            if (_letGo)
            {
                _letGoOff = _soft.owner == _letGo ? 0f : _letGoOff + dt;
                if (_letGoOff > 0.35f) _letGo = null;
            }
            Component cand = null;
            if (!_locked) cand = _soft.owner;
            else if (_auto && Best(eye, fwd, softCone, player, out var other, _hard.owner)
                     && Vector3.Angle(fwd, other.point - eye) + switchMargin < Vector3.Angle(fwd, _hard.point - eye))
                cand = other.owner;
            if (!cand || cand == _letGo || Time.unscaledTime < _autoHoldUntil) { _cand = null; _candT = 0f; return; }
            if (cand != _cand) { _cand = cand; _candT = 0f; }
            _candT += dt;
            if (_candT < autoDwell) return;
            _cand = null; _candT = 0f;
            Lock(Make(cand), true, true);
        }

        /// release by hand (middle button / T): no auto lock on that target until the crosshair leaves it, none at all
        /// for releaseHold s
        void LetGo()
        {
            _letGo = _hard.owner; _letGoOff = 0f;
            _autoHoldUntil = Time.unscaledTime + releaseHold;
            _cand = null; _candT = 0f;
            Release(null);
        }

        /// automation / scripted moments: hard-lock this HUSK, weak point or pod (false when it is not lockable now)
        public bool ForceLock(Component target)
        {
            if (!Alive(target)) return false;
            Lock(Make(target), true, false);
            return true;
        }

        /// automation: release the lock as the middle button would (the auto lock leaves that target alone for a moment)
        public void ForceRelease() { if (_locked) LetGo(); }

        void Lock(in Target t, bool announce, bool auto)
        {
            _hard = t;
            _locked = true;
            _auto = auto;
            _lockedAt = Time.unscaledTime;
            _blockedT = 0f; _saidBlocked = false;
            if (!announce) return;
            bool relock = _lastLost && _lastLost == t.owner && Time.unscaledTime - _lostAt < 8f;
            if (!auto) Emit(relock ? "relock" : "lock");                  // auto locks come all the time: only the notable ones
            if (t.kind == Kind.WeakPoint) Emit("lock_weak");
            var fc = FlightCamera.Main;
            if (!auto && fc && fc.target && fc.target.GetComponent<KestrelController>() && Vector3.Distance(fc.target.position, t.point) > 80f) Emit("lock_far");
        }

        void Release(Component lost)
        {
            if (lost)
            {
                _lastLost = lost; _lostAt = Time.unscaledTime;
                if (!_auto)                                                  // an auto lock slipping off is no news
                {
                    Emit("lock_lost");
                    if (_hard.kind == Kind.Prism) Emit("lock_lost_prism");
                }
            }
            _hard = default;
            _locked = false;
        }

        /// T or the middle mouse button (Q is ZeroGFlight's alt: KESTREL's stance toggle)
        static bool Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            return (k != null && k.tKey.wasPressedThisFrame) || (m != null && m.middleButton.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.T) || Input.GetMouseButtonDown(2);
#endif
        }
    }
}
