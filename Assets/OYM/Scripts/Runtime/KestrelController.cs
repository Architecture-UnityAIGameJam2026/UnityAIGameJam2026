using UnityEngine;

namespace OYM
{
    /// KESTREL (sword) on the zero-g flight base (ZeroGFlight: WASD / Space / Ctrl fly, Shift dash).
    /// LMB = slash (cycles Left / Charged / Thrust; click again during a cut to chain), Q = combat stance.
    /// A slash: short wind-up (the clip's anticipation runs fast), a jet-assisted lunge along the aim, the cut with a
    /// white-cyan arc, then a hard stop on the end pose (the clip's snap) with hit-stop, camera shake and impact sparks
    /// on anything in reach. The legs stay in the hover pose (UpperBody layer: slashes / combat stance only).
    public class KestrelController : ZeroGFlight
    {
        [Header("Sword")]
        public BladeTrail trail;
        public BladeTrail fpTrail;
        [Tooltip("first-person swoosh across the view (camera space)")]
        public SlashArc fpArc;
        [Tooltip("per slash (Left, Charged, Thrust), seconds in the clip: start of the cut, end of the cut, end of the hold, length (anims.py EVENTS)")]
        public float[] slashStrike = { 1.375f, 1.208f, 1.542f };
        public float[] slashImpact = { 1.458f, 1.333f, 1.625f };
        public float[] slashHoldEnd = { 1.875f, 1.75f, 2.042f };
        public float[] slashLength = { 3.5f, 2.5f, 3.333f };
        [Tooltip("seconds of anticipation kept before the cut (the clip starts there), and how fast it plays")]
        public const float DefaultWindup = 0.62f;
        public float windup = DefaultWindup, windupSpeed = 1.7f, recoverSpeed = 1.25f;
        public float lungeSpeed = 8.5f, lungeLead = 0.1f, hitRadius = 1.4f, hitReach = 1.35f, hitImpulse = 9f;
        public float combatLinger = 3f;
        public Color slashTint = new Color(0.72f, 0.93f, 1f);

        static readonly int CombatId = Animator.StringToHash("Combat");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int AttackTypeId = Animator.StringToHash("AttackType");
        static readonly int SlashSpeedId = Animator.StringToHash("SlashSpeed");

        int _attack = -1, _next;
        float _clipT, _lastAttack = -99f;
        bool _lunged, _impacted, _queued, _combat;
        Vector3 _lungeDir;

        public bool Attacking => _attack >= 0;
        public int CurrentAttack => _attack;

        /// Clip-time offset (normalized) the builder puts on the transition into slash `i`.
        public float StartOffset(int i) => Mathf.Clamp01(Mathf.Max(0f, slashStrike[i] - windup) / slashLength[i]);

        protected override void Tick(float dt, Quaternion aim)
        {
            if (input.alt) _combat = !_combat;
            if (input.fire)
            {
                if (_attack < 0 || _clipT >= slashHoldEnd[_attack] - 0.02f) StartAttack(aim);
                else _queued = true;
            }

            if (_attack >= 0)
            {
                int a = _attack;
                float speed = _clipT < slashStrike[a] - 0.04f ? windupSpeed : _clipT < slashHoldEnd[a] ? 1f : recoverSpeed;
                SetFloat(SlashSpeedId, speed);
                _clipT += dt * speed;

                // jet-assisted lunge into the cut
                if (!_lunged && _clipT >= slashStrike[a] - lungeLead)
                {
                    _lunged = true;
                    _lungeDir = aim * Vector3.forward;
                    velocity = Vector3.Lerp(velocity, _lungeDir * lungeSpeed * (a == 1 ? 1.25f : 1f), 0.9f);
                    if (jetpack) jetpack.Puff(a == 1 ? 1f : 0.7f);
                    leanVel.x += 300f;
                    if (flightCam) flightCam.FovKick(firstPersonActive ? 90f : 50f);
                    if (fpArc && firstPersonActive) fpArc.Play(a);
                }
                bool arc = _clipT >= slashStrike[a] - 0.06f && _clipT <= slashImpact[a] + 0.05f;
                if (trail) trail.emitting = arc && !firstPersonActive;
                if (fpTrail) fpTrail.emitting = arc && firstPersonActive;

                if (!_impacted && _clipT >= slashImpact[a])
                {
                    _impacted = true;
                    velocity *= 0.12f;                                    // hard stop on the end of the cut
                    leanVel.x -= 260f;
                    Impact(a, aim);
                }
                if (_queued && _clipT >= slashHoldEnd[a] - 0.02f) { _queued = false; StartAttack(aim); }
                else if (_clipT >= slashLength[a] * 0.88f) { _attack = -1; SetFloat(SlashSpeedId, 1f); }
            }
            else
            {
                if (trail) trail.emitting = false;
                if (fpTrail) fpTrail.emitting = false;
            }

            if (fpRig)
            {
                // the viewmodel arm belongs to the clip during a cut, to the framing IK otherwise (snaps back in)
                float target = _attack >= 0 && _clipT > slashStrike[_attack] - 0.3f && _clipT < slashHoldEnd[_attack] + 0.1f ? 0f : 1f;
                fpRig.ikWeight = Mathf.MoveTowards(fpRig.ikWeight, target, dt / (target > fpRig.ikWeight ? 0.22f : 0.1f));
            }
            bool stance = _combat || _attack >= 0 || Time.time - _lastAttack < combatLinger;
            upperTarget = stance ? 1f : 0f;
            SetBool(CombatId, stance);
        }

        void StartAttack(Quaternion aim)
        {
            int a = _next; _next = (_next + 1) % 3;
            _attack = a; _queued = false; _lunged = false; _impacted = false;
            _clipT = StartOffset(a) * slashLength[a];
            _lastAttack = Time.time;
            SetInt(AttackTypeId, a); Trigger(AttackId); SetFloat(SlashSpeedId, windupSpeed);
            upperTarget = 1f; upperWeight = Mathf.Max(upperWeight, 0.6f);
            Vector3 f = aim * Vector3.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        void Impact(int a, Quaternion aim)
        {
            Vector3 chest = transform.position + Vector3.up * 1.25f;
            Vector3 fwd = aim * Vector3.forward;
            Vector3 c = chest + fwd * hitReach;
            Vector3 sweep = (firstPersonActive ? fpTrail : trail) is BladeTrail bt && bt ? bt.TipDelta : Vector3.zero;
            sweep = sweep.sqrMagnitude > 1e-6f ? sweep.normalized : transform.right;
            bool hit = false;
            foreach (var col in Physics.OverlapSphere(c, hitRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var tgt = col.GetComponentInParent<ZeroGTarget>();
                if (!tgt) continue;
                Vector3 p = col.ClosestPoint(chest);
                tgt.Hit(p, (fwd * 0.7f + sweep * 0.6f).normalized * hitImpulse * (a == 1 ? 1.5f : 1f), slashTint);
                if (ImpactFX.Main) ImpactFX.Main.Play(p, (chest - p).normalized, a == 1 ? 1.4f : 1f, 46);
                hit = true;
            }
            float big = a == 1 ? 1.5f : 1f;
            if (hit) GameFeel.HitStop(0.075f * big, 0.03f);
            else GameFeel.HitStop(0.03f, 0.25f);
            if (flightCam) { flightCam.Shake((hit ? 0.42f : 0.18f) * big); flightCam.Kick(hit ? 60f : 25f, sweep.x * 40f); }
            if (fpRig && firstPersonActive) fpRig.Kick(hit ? 0.5f : 0.25f, Quaternion.Inverse(transform.rotation) * sweep);
        }

        protected override bool FaceCamera() => _attack >= 0 || _combat;
        protected override bool CanDash() => _attack < 0 || _impacted;

        protected override void OnDash()
        {
            if (_attack >= 0 && _impacted) { _attack = -1; SetFloat(SlashSpeedId, 1f); }   // dash cancels the recovery
        }

        protected override void SyncViewmodel()
        {
            var f = fpRig ? fpRig.animator : null;
            if (!f) return;
            f.SetBool(CombatId, true);
            f.SetFloat(SlashSpeedId, 1f);
        }

        protected override void OnViewChanged(bool fp)
        {
            if (trail) trail.emitting = false;
            if (fpTrail) fpTrail.emitting = false;
        }
    }
}
