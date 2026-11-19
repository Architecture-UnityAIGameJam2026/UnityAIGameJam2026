using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OYM.Net
{
    /// One pilot on the wire: pose, body lean (the LeanPivot spring), animator values, the one-shot beats as counters
    /// (a counter that moved = the beat happened, so nothing is lost between two packets) and the HP the partner's
    /// visor shows (the HUDs are crossed).
    public struct PilotState
    {
        public Vector3 pos, vel, aim;
        public Quaternion rot, lean;
        public float speed, slash, upper, thrust, hp;
        /// KESTREL: the slash in progress (0..2), -1 none
        public int attack;
        /// FlagCombat / FlagAim / FlagTrail / FlagDash / FlagCruise / FlagDown
        public int flags;
        /// ORBIT FRAME: +1 per railgun shot; any pilot: +1 per dash
        public int shotSeq, dashSeq;

        public const int FlagCombat = 1, FlagAim = 2, FlagTrail = 4, FlagDash = 8, FlagCruise = 16, FlagDown = 32;
        public bool Has(int f) => (flags & f) != 0;

        public byte[] Write() => new CoopMsg.W(CoopMsg.Pilot)
            .V3(pos).Q(rot).Q(lean).V3(vel).V3(aim)
            .F(speed).F(slash).F(upper).F(thrust).F(hp)
            .U8(attack + 1).U8(flags).U8(shotSeq & 255).U8(dashSeq & 255).Done();

        public static PilotState Read(CoopMsg.R r)
        {
            var s = new PilotState
            {
                pos = r.V3(), rot = r.Q(), lean = r.Q(), vel = r.V3(), aim = r.V3(),
                speed = r.F(), slash = r.F(), upper = r.F(), thrust = r.F(), hp = r.F()
            };
            s.attack = r.U8() - 1; s.flags = r.U8(); s.shotSeq = r.U8(); s.dashSeq = r.U8();
            return s;
        }

        /// continuous values blended, discrete ones (beats, flags) from b
        public static PilotState Blend(in PilotState a, in PilotState b, float t)
        {
            var s = b;
            s.pos = Vector3.LerpUnclamped(a.pos, b.pos, t);
            s.rot = Quaternion.Slerp(a.rot, b.rot, Mathf.Clamp01(t));
            s.lean = Quaternion.Slerp(a.lean, b.lean, Mathf.Clamp01(t));
            s.vel = Vector3.Lerp(a.vel, b.vel, t);
            s.aim = Vector3.Lerp(a.aim, b.aim, Mathf.Clamp01(t));
            s.speed = Mathf.Lerp(a.speed, b.speed, t);
            s.upper = Mathf.Lerp(a.upper, b.upper, t);
            s.thrust = Mathf.Lerp(a.thrust, b.thrust, t);
            return s;
        }
    }

    static class PilotAnim
    {
        public static readonly int Speed = Animator.StringToHash("Speed");
        public static readonly int Combat = Animator.StringToHash("Combat");
        public static readonly int Attack = Animator.StringToHash("Attack");
        public static readonly int AttackType = Animator.StringToHash("AttackType");
        public static readonly int SlashSpeed = Animator.StringToHash("SlashSpeed");
        public static readonly int Aim = Animator.StringToHash("Aim");

        /// parameters this controller really has (a Get/Set on a missing one logs every frame)
        public static HashSet<int> Params(Animator a)
        {
            var set = new HashSet<int>();
            if (a && a.runtimeAnimatorController) foreach (var p in a.parameters) set.Add(p.nameHash);
            return set;
        }
    }

    /// Reads this PC's pilot for the wire (CoopGame sends it ~30 times a second).
    public sealed class PilotSampler
    {
        // team repo port: ZeroGFlight.Cruising (the hold-Shift cruise boost) is on feature/cruise-boost, not on main -
        // read it when it is there, otherwise the puppet never shows a cruise
        static readonly System.Func<ZeroGFlight, bool> s_cruising = CruisingGetter();
        static System.Func<ZeroGFlight, bool> CruisingGetter()
        {
            var g = typeof(ZeroGFlight).GetProperty("Cruising", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
            return g != null && g.ReturnType == typeof(bool)
                ? (System.Func<ZeroGFlight, bool>)System.Delegate.CreateDelegate(typeof(System.Func<ZeroGFlight, bool>), g) : null;
        }

        readonly ZeroGFlight _f;
        readonly Transform _lean;
        readonly KestrelController _k;
        readonly OrbitFrameController _o;
        HashSet<int> _params;
        RuntimeAnimatorController _paramsFor;
        bool _wasDash, _wasCharge;
        int _dashSeq, _shotSeq;
        static FieldInfo s_jetTarget;

        public ZeroGFlight Flight => _f;

        public PilotSampler(ZeroGFlight f)
        {
            _f = f;
            _lean = f.transform.Find("LeanPivot");
            _k = f as KestrelController;
            _o = f as OrbitFrameController;
            if (s_jetTarget == null) s_jetTarget = typeof(JetpackFX).GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public PilotState Sample(HuskEncounter e)
        {
            var s = new PilotState { attack = -1, lean = Quaternion.identity };
            var t = _f.transform;
            s.pos = t.position; s.rot = t.rotation;
            if (_lean) s.lean = _lean.localRotation;
            s.vel = _f.Velocity;
            var a = _f.animator;
            if (a && a.runtimeAnimatorController)
            {
                if (_paramsFor != a.runtimeAnimatorController) { _params = PilotAnim.Params(a); _paramsFor = a.runtimeAnimatorController; }
                s.speed = _params.Contains(PilotAnim.Speed) ? a.GetFloat(PilotAnim.Speed) : s.vel.magnitude;
                s.upper = a.layerCount > 1 ? a.GetLayerWeight(1) : 0f;
                if (_params.Contains(PilotAnim.Combat) && a.GetBool(PilotAnim.Combat)) s.flags |= PilotState.FlagCombat;
                if (_params.Contains(PilotAnim.Aim) && a.GetBool(PilotAnim.Aim)) s.flags |= PilotState.FlagAim;
                s.slash = _params.Contains(PilotAnim.SlashSpeed) ? a.GetFloat(PilotAnim.SlashSpeed) : 1f;
            }
            else s.speed = s.vel.magnitude;

            if (_k)
            {
                s.attack = _k.CurrentAttack;
                if ((_k.trail && _k.trail.emitting) || (_k.fpTrail && _k.fpTrail.emitting)) s.flags |= PilotState.FlagTrail;
            }
            if (_o)
            {
                bool charging = (_o.railgun && _o.railgun.Charging) || (_o.fpRailgun && _o.fpRailgun.Charging);
                if (charging && !_wasCharge) _shotSeq++;
                _wasCharge = charging;
                var fc = FlightCamera.Main;
                s.aim = fc && fc.target == t ? fc.AimPoint(_o.range, t, out _, out _) : t.position + t.forward * 100f;
            }
            bool dash = _f.Dashing;
            if (dash && !_wasDash) _dashSeq++;
            _wasDash = dash;
            if (dash) s.flags |= PilotState.FlagDash;
            if (s_cruising != null && s_cruising(_f)) s.flags |= PilotState.FlagCruise;
            s.thrust = _f.jetpack && s_jetTarget != null ? (float)s_jetTarget.GetValue(_f.jetpack) : (dash ? 1f : _f.idleThrust);
            s.hp = e ? e.PlayerHP01 : 1f;
            if (e && !e.PlayerAlive) s.flags |= PilotState.FlagDown;
            s.dashSeq = _dashSeq; s.shotSeq = _shotSeq;
            return s;
        }
    }

    /// The partner's pilot on this PC: its ZeroGFlight is switched off and the body follows the partner's packets
    /// (played 100 ms behind, between the two packets around that moment): root, lean, animator, booster, blade trail,
    /// railgun shots at the partner's aim point (no damage here: hits only count on the PC that made them), and a hit
    /// flash when the partner's HP drops. Cutscenes (CineActor) own the body while they run.
    [DefaultExecutionOrder(-10)]
    public sealed class CoopPuppet : MonoBehaviour
    {
        public ZeroGFlight flight;
        public float delay = 0.1f;

        Transform _lean;
        KestrelController _k;
        OrbitFrameController _o;
        HashSet<int> _params;
        RuntimeAnimatorController _paramsFor;
        readonly List<(float t, PilotState s)> _buf = new List<(float, PilotState)>(16);
        PilotState _cur;
        bool _has, _first = true;
        int _lastAttack = -1, _lastShot, _lastDash;
        bool _wasDash;
        float _lastHp = 1f;
        HitFlash _flash;

        /// the partner's latest (interpolated) state
        public PilotState State => _cur;
        public bool HasState => _has;
        public float LastPacketAge => _buf.Count > 0 ? Time.unscaledTime - _buf[_buf.Count - 1].t : 999f;

        public void Init(ZeroGFlight f)
        {
            flight = f;
            _lean = f.transform.Find("LeanPivot");
            _k = f as KestrelController;
            _o = f as OrbitFrameController;
            if (_o)
            {
                // the railgun stays (its charge, beam and recoil look), the controller's shot handler goes: no damage,
                // no hit-stop, no camera kick from the partner's shots on this PC; sparks where the partner's beam lands
                var mi = typeof(OrbitFrameController).GetMethod("OnShot", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var r in new[] { _o.railgun, _o.fpRailgun })
                {
                    if (!r) continue;
                    if (mi != null) r.Shot -= (System.Action<Vector3, Vector3>)System.Delegate.CreateDelegate(typeof(System.Action<Vector3, Vector3>), _o, mi);
                    r.aimProvider = () => _cur.aim;
                    r.Shot += (muzzle, end) => { if (ImpactFX.Main) ImpactFX.Main.Play(end, (muzzle - end).normalized, 1f, 36); };
                }
            }
            flight.ResetMotion();
            flight.enabled = false;
        }

        public void Push(PilotState s)
        {
            _buf.Add((Time.unscaledTime, s));
            if (_buf.Count > 24) _buf.RemoveRange(0, _buf.Count - 24);
        }

        void Update()
        {
            if (!flight) return;
            // the arrival cutscene / CineActor plays both pilots on both PCs
            if (HuskEncounter.CutsceneRunning()) { _first = true; return; }
            if (flight.enabled) { flight.ResetMotion(); flight.enabled = false; }   // CineActor.End() turns it back on
            if (_buf.Count == 0) return;

            float rt = Time.unscaledTime - delay;
            int i = _buf.Count - 1;
            while (i > 0 && _buf[i - 1].t > rt) i--;
            PilotState s;
            if (i == 0 || _buf.Count == 1) s = _buf[0].s;
            else
            {
                var a = _buf[i - 1]; var b = _buf[i];
                float span = Mathf.Max(1e-3f, b.t - a.t);
                s = PilotState.Blend(a.s, b.s, Mathf.Clamp((rt - a.t) / span, 0f, 1.5f));
            }
            // drop what is behind the play head (keep one before it)
            while (_buf.Count > 2 && _buf[1].t < rt) _buf.RemoveAt(0);
            Apply(s);
        }

        void Apply(in PilotState s)
        {
            var t = flight.transform;
            t.SetPositionAndRotation(s.pos, s.rot);
            if (_lean) _lean.localRotation = s.lean;
            var a = flight.animator;
            if (a && a.runtimeAnimatorController)
            {
                if (_paramsFor != a.runtimeAnimatorController) { _params = PilotAnim.Params(a); _paramsFor = a.runtimeAnimatorController; }
                if (_params.Contains(PilotAnim.Speed)) a.SetFloat(PilotAnim.Speed, s.speed);
                if (a.layerCount > 1) a.SetLayerWeight(1, s.upper);
                if (_params.Contains(PilotAnim.Combat)) a.SetBool(PilotAnim.Combat, s.Has(PilotState.FlagCombat));
                if (_params.Contains(PilotAnim.Aim)) a.SetBool(PilotAnim.Aim, s.Has(PilotState.FlagAim));
                if (_params.Contains(PilotAnim.SlashSpeed)) a.SetFloat(PilotAnim.SlashSpeed, s.slash);
                // a new slash: the same trigger the controller pulls
                if (_k && s.attack >= 0 && s.attack != _lastAttack && !_first && _params.Contains(PilotAnim.Attack))
                {
                    if (_params.Contains(PilotAnim.AttackType)) a.SetInteger(PilotAnim.AttackType, s.attack);
                    a.SetTrigger(PilotAnim.Attack);
                    if (flight.jetpack) flight.jetpack.Puff(s.attack == 1 ? 1f : 0.7f);
                }
            }
            _lastAttack = s.attack;
            if (_k && _k.trail) _k.trail.emitting = s.Has(PilotState.FlagTrail);

            var jet = flight.jetpack;
            bool dash = s.Has(PilotState.FlagDash);
            if (jet)
            {
                if (!_first && s.dashSeq != _lastDash) jet.Ignite();
                else if (_wasDash && !dash && !s.Has(PilotState.FlagCruise)) jet.Cutoff();
                jet.SetThrust(s.thrust);
            }
            _lastDash = s.dashSeq; _wasDash = dash;

            if (_o && !_first && s.shotSeq != _lastShot && _o.railgun && !_o.railgun.Charging) _o.railgun.Fire();
            _lastShot = s.shotSeq;

            // the partner got hit: their suit flashes here too
            if (!_first && s.hp < _lastHp - 0.004f)
            {
                if (!_flash)
                {
                    _flash = t.GetComponent<HitFlash>();
                    if (!_flash) { _flash = t.gameObject.AddComponent<HitFlash>(); _flash.renderers = t.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
                }
                _flash.AddHit(t.position + Vector3.up * (flight.bodyCenter + 0.15f), new Color(7f, 0.6f, 2.4f), 0.9f, 0.55f);
            }
            _lastHp = s.hp;
            _cur = s; _has = true; _first = false;
        }
    }
}
