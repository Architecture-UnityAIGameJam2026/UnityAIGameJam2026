using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// KESTREL (sword) on the zero-g flight base (ZeroGFlight: WASD / Space / Ctrl fly).
    /// LMB = slash (cycles Left / Charged / Thrust; click again during a cut to chain), Q = combat stance.
    /// A slash: short wind-up (the clip's anticipation runs fast), a jet-assisted lunge along the aim, the cut with a
    /// white-cyan arc, then a hard stop on the end pose (the clip's snap) with hit-stop, camera shake and impact sparks
    /// on anything in reach. The legs stay in the hover pose (UpperBody layer: slashes / combat stance only).
    /// Shift = swift strike (Genji): the pilot shoots strikeDistance along the aim in a straight line, slashing all the
    /// way (cut after cut, Charged / Left drawn at once, one long blade ribbon down the line), through the HUSK on it,
    /// cutting everything within strikeRadius of the path, then stops dead on the end pose (hit-stop when it cut
    /// something). strikeCooldown s to recharge; a kill (by the strike or a slash) gives it back at once, and a press
    /// during the strike then chains the next one. While it recharges Shift is the plain dash (dashWhileCooling).
    /// Shift held with no enemies around carries on into the cruise boost.
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

        [Header("Swift strike (Shift)")]
        [Tooltip("Shift = a long dash cut along the aim through everything on the line (off = the plain dash)")]
        public bool swiftStrike = true;
        [Tooltip("metres covered, in this many seconds")]
        public float strikeDistance = 20f, strikeTime = 0.25f;
        [Tooltip("seconds to recharge after a strike lands")]
        public float strikeCooldown = 3f;
        [Tooltip("a kill (strike or slash) gives the strike back at once (Genji)")]
        public bool strikeResetOnKill = true;
        [Tooltip("a target the sword hit that dies within this many seconds counts as a kill")]
        public float killWindow = 0.6f;
        [Tooltip("while the strike recharges, Shift is the plain dash (keeps the dodge and the cruise); off = Shift does nothing")]
        public bool dashWhileCooling = true;
        [Tooltip("cut radius around the path (m), and the shove on what it cuts")]
        public float strikeRadius = 2.2f, strikeImpulse = 12f;
        [Tooltip("a new cut starts while at least this much of the strike is left (s): a long strike slashes all the way")]
        public float strikeRecut = 0.08f;
        [Tooltip("a Shift press during the strike chains the next one if a kill gives it back by the landing (+ this s)")]
        public float strikeBuffer = 0.25f;
        [Tooltip("clip speed of the draw (wind-up skipped in a frame or two) and where it hands over to the cut (s before it)")]
        public float strikeDraw = 14f, strikeLead = 0.05f;
        [Tooltip("clip speed of the cuts while the strike flies (faster = more cuts down the line)")]
        public float strikeCutSpeed = 1.8f;
        [Tooltip("blade ribbon during the strike: life (s, long enough to show the whole line), outer / inner edge in blade lengths")]
        public float strikeTrailLife = 0.6f, strikeTrailReach = 1.25f, strikeTrailInner = 0.3f;
        [Tooltip("scripted input (cutaways, recordings) keeps the plain dash unless this is on")]
        public bool strikeWhenScripted;

        static readonly int CombatId = Animator.StringToHash("Combat");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int AttackTypeId = Animator.StringToHash("AttackType");
        static readonly int SlashSpeedId = Animator.StringToHash("SlashSpeed");
        static readonly int[] SlashStates = { Animator.StringToHash("UpperBody.Slash_Left"), Animator.StringToHash("UpperBody.Slash_Charged"), Animator.StringToHash("UpperBody.Slash_Thrust") };
        static readonly Collider[] s_cut = new Collider[64];

        int _attack = -1, _next;
        float _clipT, _lastAttack = -99f;
        bool _lunged, _impacted, _queued, _combat;
        Vector3 _lungeDir;

        struct TrailShape { public bool saved; public float life, reach, inner; }
        readonly HashSet<ZeroGTarget> _cutSet = new HashSet<ZeroGTarget>();
        readonly List<Collider> _killWatch = new List<Collider>();
        bool _striking, _strikeRibbon;
        int _strikeClip = 1, _strikeHits, _strikeCuts, _strikeFrame = -1, _kills;
        float _strikeSpeed, _strikeLeft, _strikeReadyAt, _strikeBufferUntil = -1f, _killWatchUntil = -1f, _trailRestoreAt = -1f;
        Vector3 _strikeDir, _sweepFrom;
        TrailShape _tShape, _fpShape;

        public bool Attacking => _attack >= 0;
        public int CurrentAttack => _attack;
        /// Shift's dash cut is in the air.
        public bool Striking => _striking;
        /// Targets cut by the current (or last) strike, and how many cuts it swung.
        public int StrikeHits => _strikeHits;
        public int StrikeCuts => _strikeCuts;
        /// Kills that gave the strike back (since the start).
        public int StrikeResets => _kills;
        /// Shift = swift strike when it is charged (live input, or a scripted run that opted in).
        public bool StrikeArmed => swiftStrike && (!scripted || strikeWhenScripted);
        public bool StrikeReady => Time.time >= _strikeReadyAt;
        public float StrikeCooldownLeft => Mathf.Max(0f, _strikeReadyAt - Time.time);
        Vector3 Chest => transform.position + Vector3.up * 1.25f;

        /// Clip-time offset (normalized) the builder puts on the transition into slash `i`.
        public float StartOffset(int i) => Mathf.Clamp01(Mathf.Max(0f, slashStrike[i] - windup) / slashLength[i]);

        protected override void Tick(float dt, Quaternion aim)
        {
            if (input.alt) _combat = !_combat;
            if (StrikeArmed) StrikeChain(aim);
            if (_striking) StrikeTick(dt);
            KillWatch();
            if (input.fire)
            {
                if (_attack < 0 || _clipT >= slashHoldEnd[_attack] - 0.02f) StartAttack(aim);
                else _queued = true;
            }

            if (_attack >= 0)
            {
                // a long strike keeps slashing: the next cut as soon as one lands, while enough of the line is left
                if (_striking && _clipT >= slashImpact[_attack] + 0.02f && _strikeLeft / Mathf.Max(1f, _strikeSpeed) > strikeRecut) StrikeCut();
                int a = _attack;
                float drawTo = slashStrike[a] - strikeLead, speed;
                if (_striking && _clipT < drawTo) speed = Mathf.Clamp((drawTo - _clipT) / Mathf.Max(dt, 1e-4f), 1f, strikeDraw);   // the draw: land on the cut
                else if (_striking) speed = strikeCutSpeed;                                     // cuts on the run
                else speed = _clipT < slashStrike[a] - 0.04f ? windupSpeed : _clipT < slashHoldEnd[a] ? 1f : recoverSpeed;
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
                if (_striking && _clipT >= slashStrike[a] - 0.06f) _strikeRibbon = true;    // from the first cut on, one ribbon down the whole line
                bool arc = _striking ? _strikeRibbon : _clipT >= slashStrike[a] - 0.06f && _clipT <= slashImpact[a] + 0.05f;
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
            if (_trailRestoreAt > 0f && !_striking && Time.time >= _trailRestoreAt)
            {
                _trailRestoreAt = -1f;
                RestoreTrail(trail, ref _tShape, true); RestoreTrail(fpTrail, ref _fpShape, true);
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
            Vector3 chest = Chest;
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
                Watch(col);                                               // a slash kill gives the strike back too
                hit = true;
            }
            float big = a == 1 ? 1.5f : 1f;
            if (hit) GameFeel.HitStop(0.075f * big, 0.03f);
            else GameFeel.HitStop(0.03f, 0.25f);
            if (flightCam) { flightCam.Shake((hit ? 0.42f : 0.18f) * big); flightCam.Kick(hit ? 60f : 25f, sweep.x * 40f); }
            if (fpRig && firstPersonActive) fpRig.Kick(hit ? 0.5f : 0.25f, Quaternion.Inverse(transform.rotation) * sweep);
        }

        // ------------------------------------------------------------------------------------------------ swift strike
        /// The strike starts from the dash (ZeroGFlight.StartDash -> OnDash): the flight base owns the brake and the
        /// cruise hand-off; this side sets the line, the speed, the cuts and the recharge.
        void BeginStrike()
        {
            _strikeDir = AimRotation() * Vector3.forward;                   // where the crosshair points, not the WASD dodge
            _strikeSpeed = strikeDistance / Mathf.Max(0.05f, strikeTime);
            _strikeLeft = strikeDistance;
            dashDir = _strikeDir; dashLeft = strikeTime + 1f;               // the distance ends it (StrikeTick), this is a backstop
            dashReady = Time.time + strikeTime;                             // the plain dash may follow the landing
            _strikeReadyAt = Time.time + strikeTime + Mathf.Max(0f, strikeCooldown);
            velocity = _strikeDir * _strikeSpeed;
            _striking = true; passThrough = true; _strikeFrame = Time.frameCount;
            _strikeHits = 0; _strikeCuts = 0; _strikeRibbon = false; _cutSet.Clear(); _strikeBufferUntil = -1f;
            _sweepFrom = Chest;
            _queued = false;
            StrikeCut();
            upperTarget = 1f; upperWeight = Mathf.Max(upperWeight, 0.85f);
            Vector3 f = _strikeDir; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);

            if (jetpack) jetpack.Puff(1f);
            leanVel.x += 200f;
            if (flightCam) flightCam.FovKick(firstPersonActive ? 120f : 70f);    // on top of the dash's own punch
            StrikeTrail(trail, ref _tShape); StrikeTrail(fpTrail, ref _fpShape);
            _trailRestoreAt = -1f;
        }

        /// One cut of the strike: Charged / Left alternate (always a fresh state), drawn at once from script.
        void StrikeCut()
        {
            int a = _strikeClip == _attack ? (_strikeClip == 1 ? 0 : 1) : _strikeClip;
            _strikeClip = a == 1 ? 0 : 1;
            _attack = a; _lunged = true; _impacted = true;                  // no lunge / end-of-cut stop: the strike flies and cuts itself
            _clipT = StartOffset(a) * slashLength[a];                        // where the controller's own transition starts the clip
            _lastAttack = Time.time;
            _strikeCuts++;
            SetInt(AttackTypeId, a); SetFloat(SlashSpeedId, strikeDraw);
            CrossSlash(animator, a, _clipT); CrossSlash(FpAnim, a, _clipT);
            if (fpArc && firstPersonActive) fpArc.Play(a);
        }

        /// Into the slash state straight from script (no trigger: whatever the layer is doing, the cut starts now).
        static void CrossSlash(Animator an, int a, float clipTime)
        {
            if (!an || !an.runtimeAnimatorController || an.layerCount < 2 || !an.HasState(1, SlashStates[a])) return;
            an.ResetTrigger(AttackId);
            an.CrossFadeInFixedTime(SlashStates[a], 0.04f, 1, clipTime);
        }

        /// Runs before the move each frame of the strike: cut along what was flown last frame, hold the line. The last
        /// frame flies only what is left (the strike is strikeDistance at any frame rate); the flight base cuts the burn
        /// (or hands over to the cruise) on the next one.
        void StrikeTick(float dt)
        {
            Vector3 c = Chest;
            Cut(_sweepFrom, c);
            _sweepFrom = c;
            if (Dashing)
            {
                float step = Mathf.Min(_strikeSpeed * dt, _strikeLeft);
                _strikeLeft -= step;
                dashDir = _strikeDir;                                       // a straight line: WASD does not bend it
                velocity = _strikeDir * (dt > 1e-6f ? step / dt : _strikeSpeed);
                if (_strikeLeft <= 1e-3f) dashLeft = Mathf.Min(dashLeft, 1e-5f);
                if (flightCam && flightCam.target == transform) flightCam.SetSpeedFx(1f);   // tighter follow, wider view at speed
            }
            else EndStrike();
        }

        /// The flight base already cut the burn (hard stop, brake kick) or handed over to the cruise.
        void EndStrike()
        {
            _striking = false; passThrough = false;
            RestoreTrail(trail, ref _tShape, false); RestoreTrail(fpTrail, ref _fpShape, false);
            _trailRestoreAt = Time.time + strikeTrailLife;                   // the streak fades at its own length first
            if (cruising || _strikeHits == 0) return;
            float k = Mathf.Min(_strikeHits, 4);
            GameFeel.HitStop(0.05f + 0.012f * k, 0.03f);                    // the snap on the end pose
            if (flightCam) { flightCam.Shake(0.22f + 0.06f * k); flightCam.Kick(45f, 0f); }
        }

        /// A press during the strike is kept until it lands (+ strikeBuffer): if a kill gave the strike back by then, the
        /// next one goes at once (Genji's chain). Otherwise it lapses - no stray plain dash after a strike.
        void StrikeChain(Quaternion aim)
        {
            if (input.dash && _striking && _strikeFrame != Time.frameCount)
                _strikeBufferUntil = Time.time + strikeBuffer + _strikeLeft / Mathf.Max(1f, _strikeSpeed);
            if (_strikeBufferUntil < 0f || _striking || Dashing || cruising) return;
            if (Time.time > _strikeBufferUntil) { _strikeBufferUntil = -1f; return; }
            if (!StrikeReady || Time.time < dashReady) return;
            _strikeBufferUntil = -1f;
            StartDash(aim * Vector3.forward);                                // -> OnDash -> BeginStrike
        }

        /// Everything with a ZeroGTarget within strikeRadius of the segment a-b gets cut, once per strike.
        void Cut(Vector3 a, Vector3 b)
        {
            int n = Physics.OverlapCapsuleNonAlloc(a, b, strikeRadius, s_cut, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_cut[i];
                if (!col || col.transform.IsChildOf(transform)) continue;
                var tgt = col.GetComponentInParent<ZeroGTarget>();
                if (!tgt || !_cutSet.Add(tgt)) continue;
                Vector3 onPath = ClosestOnSegment(a, b, col.bounds.center);
                Vector3 p = col.ClosestPoint(onPath);
                Vector3 away = p - onPath; away -= _strikeDir * Vector3.Dot(away, _strikeDir);
                away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.right;
                tgt.Hit(p, (_strikeDir * 0.75f + away * 0.6f).normalized * strikeImpulse, slashTint);
                Vector3 n2 = onPath - p;
                if (ImpactFX.Main) ImpactFX.Main.Play(p, n2.sqrMagnitude > 1e-4f ? n2.normalized : -_strikeDir, 1.2f, 40);
                if (flightCam && flightCam.target == transform) flightCam.Shake(0.1f);
                _strikeHits++;
                Watch(col);
            }
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
            return l2 < 1e-8f ? a : a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
        }

        void Watch(Collider c)
        {
            if (!_killWatch.Contains(c)) _killWatch.Add(c);
            _killWatchUntil = Time.time + killWindow;
        }

        /// A target the sword hit that dies right after (its collider goes off, or it is gone: HUSK Kill, a destroyed
        /// pod) gives the strike back - ready when the current one lands.
        void KillWatch()
        {
            if (_killWatchUntil < 0f) return;
            if (Time.time > _killWatchUntil) { _killWatchUntil = -1f; _killWatch.Clear(); return; }
            if (!strikeResetOnKill) return;
            foreach (var c in _killWatch)
            {
                if (c && c.enabled && c.gameObject.activeInHierarchy) continue;
                float at = Time.time + (_striking ? _strikeLeft / Mathf.Max(1f, _strikeSpeed) : 0f);
                if (_strikeReadyAt > at) { _strikeReadyAt = at; _kills++; }
                _killWatchUntil = -1f; _killWatch.Clear();
                return;
            }
        }

        void StrikeTrail(BladeTrail t, ref TrailShape s)
        {
            if (!t) return;
            if (!s.saved) { s.saved = true; s.life = t.life; s.reach = t.reach; s.inner = t.inner; }
            t.life = Mathf.Max(s.life, strikeTrailLife); t.reach = strikeTrailReach; t.inner = strikeTrailInner;
        }

        static void RestoreTrail(BladeTrail t, ref TrailShape s, bool life)
        {
            if (!t || !s.saved) return;
            t.reach = s.reach; t.inner = s.inner;
            if (life) { t.life = s.life; s.saved = false; }
        }

        /// The strike flies through what it cuts (HUSK, practice targets, boss pods); rocks and hulls still stop it.
        protected override bool PassThrough(Collider c) => c.GetComponentInParent<ZeroGTarget>();

        // ------------------------------------------------------------------------------------------------ flight hooks
        protected override bool FaceCamera() => _attack >= 0 || _combat;

        protected override bool CanDash()
        {
            if (StrikeArmed && StrikeReady) return true;                    // the strike cuts into anything
            if (StrikeArmed && !dashWhileCooling) return false;             // recharging: Shift waits
            return _attack < 0 || _impacted;                                // the plain dash
        }

        protected override void OnDash()
        {
            if (StrikeArmed && StrikeReady) { BeginStrike(); return; }
            if (_attack >= 0 && _impacted) { _attack = -1; SetFloat(SlashSpeedId, 1f); }   // dash cancels the recovery
        }

        /// Out of a strike the cruise picks up at its full speed (not from the plain dash's).
        protected override void OnCruise(bool on)
        {
            if (!on || !_striking) return;
            cruiseNow = Mathf.Clamp(_strikeSpeed, dashSpeed, cruiseSpeed);
            velocity = cruiseDir * cruiseNow;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_striking) dashLeft = 0f;                                   // no half strike waiting for the re-enable
            _striking = false; passThrough = false; _strikeBufferUntil = -1f;
            RestoreTrail(trail, ref _tShape, true); RestoreTrail(fpTrail, ref _fpShape, true);
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
