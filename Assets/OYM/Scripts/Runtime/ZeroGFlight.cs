using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// One frame of player intent (keyboard / mouse, or a scripted demo).
    [System.Serializable]
    public struct FlightInput
    {
        /// x = right, y = up, z = forward (each -1..1), relative to the camera aim (y is world up).
        public Vector3 move;
        public bool dash, fire, fireHeld, aim, alt;
        /// Shift held: with no enemies around the dash carries into the cruise boost.
        public bool boost;
    }

    /// Shared zero-g flight for the play-test characters. Nobody walks: the body floats, the booster pack pushes it.
    ///   WASD = move along the camera aim (3D), Space / Ctrl = up / down, Shift = dash (ignition flash, then a hard cut-off).
    ///   Velocity with acceleration and drag (drifts when you let go), the body yaws to the camera, and a damped spring leans
    ///   it (pitch / roll from the local velocity, big lean while dashing) on a pivot ~1 m up inserted above the "Model".
    ///   The dash ends with a brake kick: the body flares back, overshoots and settles (the snap).
    ///   Cruise boost: hold Shift with no enemies around and the dash does not cut off - the burn catches (second puff) and
    ///   carries on, ramping up to cruiseSpeed; the heading follows the aim / WASD with weight and the body banks into the
    ///   turns; the camera opens up (FlightCamera.SetSpeedFx). Release = retro brake with the snap. Contact (EnemiesNear:
    ///   an awake enemy close, a dart in the air, the boss fight), a weapon or a head-on bump ends it.
    /// First person (FlightCamera.firstPerson) hides the body from the camera (shadows stay) and shows the viewmodel rig.
    public abstract class ZeroGFlight : MonoBehaviour
    {
        public Animator animator;
        public Transform cameraTransform;
        public JetpackFX jetpack;
        public FirstPersonRig fpRig;

        [Header("Flight")]
        public float acceleration = 16f;
        [Tooltip("1/s while thrusting; top speed = acceleration / inputDrag")]
        public float inputDrag = 2.6f;
        [Tooltip("1/s with no input: the suit bleeds the drift off slowly")]
        public float idleDrag = 1.0f;
        public float turnSpeed = 420f;
        public float bodyCenter = 1.0f, bodyRadius = 0.42f;

        [Header("Dash")]
        public float dashSpeed = 16f;
        public float dashTime = 0.24f;
        public float dashCooldown = 0.4f;
        [Tooltip("speed kept when the dash cuts off")]
        public float dashEndSpeed = 2.2f;
        [Tooltip("legacy (OYMGifCapture)")]
        public float dashBrake = 30f;
        [Tooltip("booster always ticking over while flying (soft plume + nozzle glow)")]
        public float idleThrust = 0.16f;

        [Header("Cruise boost (hold Shift, no enemies around)")]
        [Tooltip("hold Shift through the dash with no enemies around: the burn carries into a sustained cruise")]
        public bool cruiseEnabled = true;
        public float cruiseSpeed = 34f;
        [Tooltip("m/s^2 from the dash speed up to the cruise speed")]
        public float cruiseAccel = 20f;
        [Tooltip("rad/s the cruise heading follows the aim / WASD (weight in the turns)")]
        public float cruiseTurn = 2.4f;
        public float cruiseLean = 66f;
        [Tooltip("bank into the turns: deg of roll per deg/s of heading change, and its limit")]
        public float cruiseBank = 0.12f, cruiseMaxBank = 34f;
        [Tooltip("release: retro brake rate (1/s) for this long (s), then the normal drift")]
        public float cruiseBrake = 15f, cruiseBrakeTime = 0.2f;
        [Tooltip("a static obstacle hit this head-on (dot of the heading into the surface) ends the cruise; shallower hits slide along")]
        public float cruiseBumpDot = 0.55f;
        [Tooltip("awake enemies this close (m) count as 'enemies around'")]
        public float threatRadius = 180f;
        [Tooltip("seconds 'enemies around' holds after the last threat (no flicker at the edge)")]
        public float threatLinger = 1.2f;

        /// "Enemies around this point (within radius)?" Installed by FlightThreat (live HUSK); null = never (test maps).
        public static System.Func<Vector3, float, bool> EnemiesNear;

        [Header("Lean spring")]
        public float pivotHeight = 1.0f;
        public float leanPerSpeed = 3.4f, rollPerSpeed = 3.8f, maxLean = 32f, dashLean = 62f;
        public float leanFreq = 2.2f, leanDamping = 0.55f;
        [Tooltip("after the dash: faster, springier settle (fast arrival, small overshoot, hard hold)")]
        public float snapFreq = 5.2f, snapDamping = 0.38f, snapTime = 0.45f, brakeKick = 520f;
        [Tooltip("hard limits so stacked dash / brake / attack kicks can't run away")]
        public float leanLimit = 70f, leanSpeedLimit = 900f;

        [Header("Hover")]
        public float bobAmp = 0.025f, bobFreq = 0.3f, swayDeg = 1.8f;

        [Header("Leg trail (secondary motion, no walking)")]
        [Tooltip("thigh swing per degree of body lean: lean forward -> legs stream back, brake flare -> legs swing forward")]
        public float legFollow = 0.38f, legMax = 20f, legFreq = 2.6f, legDamping = 0.32f;

        [Header("Scripted input (demo)")]
        public bool scripted;
        public FlightInput scriptedInput;

        protected Vector3 velocity;
        protected Transform leanPivot, model;
        protected FlightCamera flightCam;
        protected FlightInput input;
        protected float dashLeft, dashReady, snapLeft;
        protected Vector3 dashDir;
        protected bool firstPersonActive;
        protected float upperWeight, upperTarget;
        protected Vector2 lean, leanVel;                 // x = pitch (+ nose down), y = roll (deg / deg per s)
        protected bool cruising;
        protected float cruiseNow, brakeLeft;
        protected Vector3 cruiseDir;
        /// While true, Move() flies through the colliders PassThrough() accepts (KESTREL's swift strike cuts through the
        /// HUSK on its line instead of stopping on them). Normal flight never sets it: its sweep is unchanged.
        protected bool passThrough;
        static readonly RaycastHit[] s_hits = new RaycastHit[24];
        Renderer[] _body;
        ShadowCastingMode[] _bodyShadow;
        Transform _thighL, _thighR;
        Vector2 _leg, _legVel;
        float _seed;
        bool _ready;
        float _threatUntil = -1f, _yawRate;
        bool _bumped, _bumpStatic;
        Vector3 _bumpN, _bumpP;

        public Vector3 Velocity => velocity;
        public bool Dashing => dashLeft > 0f;
        public bool FirstPerson => firstPersonActive;
        public bool Cruising => cruising;
        /// 0..1 of the cruise speed while cruising.
        public float Cruise01 => cruising ? Mathf.Clamp01(cruiseNow / Mathf.Max(1f, cruiseSpeed)) : 0f;
        bool OwnsCamera => flightCam && flightCam.target == transform;

        protected virtual void Awake() { Setup(); }

        void Setup()
        {
            if (_ready) return;
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!jetpack) jetpack = GetComponentInChildren<JetpackFX>();
            if (!fpRig) fpRig = GetComponentInChildren<FirstPersonRig>(true);
            if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
            _seed = Random.value * 10f;
            if (animator)
            {
                model = animator.transform;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // FP shows the body as shadows only; FX sockets ride on the bones
                leanPivot = new GameObject("LeanPivot").transform;
                leanPivot.SetParent(transform, false);
                leanPivot.localPosition = Vector3.up * pivotHeight;
                model.SetParent(leanPivot, true);
                var list = new System.Collections.Generic.List<Renderer>();
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
                _body = list.ToArray();
                _bodyShadow = new ShadowCastingMode[_body.Length];
                for (int i = 0; i < _body.Length; i++) _bodyShadow[i] = _body[i].shadowCastingMode;
                if (animator.isHuman)
                {
                    _thighL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    _thighR = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                }
            }
            if (fpRig) { fpRig.flight = this; fpRig.gameObject.SetActive(false); }
            _ready = true;
        }

        protected virtual void OnDisable()
        {
            if (jetpack) jetpack.SetThrust(0f);
            if (firstPersonActive) SetFirstPerson(false);
            cruising = false; cruiseNow = 0f; brakeLeft = 0f; _yawRate = 0f;
        }

        // ------------------------------------------------------------------------------------------------ input
        protected virtual FlightInput ReadInput()
        {
            var i = new FlightInput();
            bool live = !flightCam || flightCam.InputLive;
            bool consumed = flightCam && flightCam.ClickConsumed;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            if (k != null)
            {
                if (k.wKey.isPressed) i.move.z += 1; if (k.sKey.isPressed) i.move.z -= 1;
                if (k.dKey.isPressed) i.move.x += 1; if (k.aKey.isPressed) i.move.x -= 1;
                if (k.spaceKey.isPressed) i.move.y += 1; if (k.leftCtrlKey.isPressed || k.cKey.isPressed) i.move.y -= 1;
                i.dash = k.leftShiftKey.wasPressedThisFrame;
                i.boost = k.leftShiftKey.isPressed;
                i.alt = k.qKey.wasPressedThisFrame;
            }
            if (m != null && live && !consumed)
            {
                i.fire = m.leftButton.wasPressedThisFrame; i.fireHeld = m.leftButton.isPressed; i.aim = m.rightButton.isPressed;
            }
#else
            i.move = new Vector3(Input.GetAxisRaw("Horizontal"), (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0), Input.GetAxisRaw("Vertical"));
            i.dash = Input.GetKeyDown(KeyCode.LeftShift); i.boost = Input.GetKey(KeyCode.LeftShift); i.alt = Input.GetKeyDown(KeyCode.Q);
            if (live && !consumed) { i.fire = Input.GetMouseButtonDown(0); i.fireHeld = Input.GetMouseButton(0); i.aim = Input.GetMouseButton(1); }
#endif
            return i;
        }

        // ------------------------------------------------------------------------------------------------ main loop
        protected virtual void Update()
        {
            Setup();
            if (!animator) return;
            float dt = Time.deltaTime;
            if (!flightCam || !flightCam.isActiveAndEnabled) flightCam = FlightCamera.Main;
            if (scripted)
            {
                input = scriptedInput;
                scriptedInput.dash = scriptedInput.fire = scriptedInput.alt = false;     // one-shot presses
            }
            else input = ReadInput();

            bool fp = flightCam && flightCam.firstPerson && flightCam.target == transform;
            if (fp != firstPersonActive) SetFirstPerson(fp);

            Quaternion aim = AimRotation();
            Vector3 wish = aim * Vector3.forward * input.move.z + aim * Vector3.right * input.move.x + Vector3.up * input.move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= ThrustScale();

            if (input.dash && dashLeft <= 0f && !cruising && Time.time >= dashReady && CanDash())
                StartDash(wish.sqrMagnitude > 0.01f ? wish.normalized : aim * Vector3.forward);

            if (dashLeft > 0f)
            {
                dashLeft -= dt;
                if (wish.sqrMagnitude > 0.01f) dashDir = Vector3.RotateTowards(dashDir, wish.normalized, 1.2f * dt, 0f);
                velocity = dashDir * dashSpeed;
                if (jetpack) jetpack.SetThrust(1f);
                if (dashLeft <= 0f)
                {
                    if (input.boost && CanCruise()) StartCruise();          // Shift still held, nobody around: the burn carries on
                    else EndDash();
                }
            }
            else if (cruising) Cruise(dt, aim, wish);
            else
            {
                velocity += wish * (acceleration * dt);
                velocity *= Mathf.Exp(-(wish.sqrMagnitude > 0.01f ? inputDrag : idleDrag) * dt);
                if (brakeLeft > 0f) { brakeLeft -= dt; velocity *= Mathf.Exp(-cruiseBrake * dt); }     // retro brake after a cruise
                if (jetpack) jetpack.SetThrust(Mathf.Clamp01(idleThrust + wish.magnitude * 0.55f + ExtraThrust()));
            }

            Tick(dt, aim);
            _bumped = false;
            Move(velocity * dt);
            if (cruising && _bumped) CruiseBump();
            if (cruising && OwnsCamera) flightCam.SetSpeedFx(Cruise01);
            Face(dt, aim, wish);
            Lean(dt);
            upperWeight = Mathf.MoveTowards(upperWeight, upperTarget, dt / 0.15f);
            animator.SetLayerWeight(1, upperWeight);
            SetFloat(SpeedId, velocity.magnitude);
        }

        /// Character-specific per-frame logic (attacks); runs after the flight velocity update, before the move.
        protected abstract void Tick(float dt, Quaternion aim);
        protected virtual bool CanDash() => true;
        protected virtual float ThrustScale() => 1f;
        protected virtual float ExtraThrust() => 0f;
        protected virtual bool FaceCamera() => true;

        /// Stop all motion (demo / respawn).
        public void ResetMotion()
        {
            velocity = Vector3.zero; dashLeft = 0f; snapLeft = 0f; lean = leanVel = _leg = _legVel = Vector2.zero;
            cruising = false; cruiseNow = 0f; brakeLeft = 0f; _yawRate = 0f;
        }

        public Quaternion AimRotation()
        {
            if (flightCam) return flightCam.Aim;
            if (cameraTransform) return cameraTransform.rotation;
            return transform.rotation;
        }

        protected void StartDash(Vector3 dir)
        {
            dashDir = dir.normalized; dashLeft = dashTime; dashReady = Time.time + dashTime + dashCooldown;
            brakeLeft = 0f;
            velocity = dashDir * dashSpeed;
            if (jetpack) jetpack.Ignite();
            if (flightCam) { flightCam.Shake(0.22f); flightCam.FovKick(firstPersonActive ? 140f : 90f); }
            Vector3 l = Quaternion.Inverse(transform.rotation) * dashDir;
            leanVel += new Vector2(l.z, -l.x) * 380f;                         // throw the body into the dash
            OnDash();
        }

        protected virtual void OnDash() { }

        void EndDash()
        {
            velocity = dashDir * dashEndSpeed;
            if (jetpack) jetpack.Cutoff();
            snapLeft = snapTime;
            Vector3 l = Quaternion.Inverse(transform.rotation) * dashDir;
            leanVel -= new Vector2(l.z, -l.x) * brakeKick;                   // brake: flare back against the dash
            if (flightCam) { flightCam.Shake(0.1f); flightCam.FovKick(-70f); }
            if (fpRig) fpRig.Kick(0.6f, l);
        }

        // ------------------------------------------------------------------------------------------------ cruise boost
        /// Awake enemies within threatRadius, a dart in the air or the boss fight (holds threatLinger s after the last one).
        public bool EnemiesAround()
        {
            if (EnemiesNear != null && EnemiesNear(transform.position, threatRadius)) _threatUntil = Time.time + threatLinger;
            return Time.time < _threatUntil;
        }

        protected virtual bool CanCruise() => cruiseEnabled && !EnemiesAround();
        /// (true = the cruise starts, false = it ends) for character-specific reactions.
        protected virtual void OnCruise(bool on) { }

        void StartCruise()
        {
            cruising = true;
            cruiseDir = dashDir.sqrMagnitude > 1e-4f ? dashDir.normalized : transform.forward;
            cruiseNow = dashSpeed; _yawRate = 0f;
            velocity = cruiseDir * cruiseNow;
            if (jetpack) { jetpack.SetThrust(1f); jetpack.Puff(0.8f); }           // second stage: the burn catches and holds
            if (OwnsCamera) { flightCam.Shake(0.1f); flightCam.FovKick(firstPersonActive ? 110f : 70f); }
            Vector3 l = Quaternion.Inverse(transform.rotation) * cruiseDir;
            leanVel += new Vector2(l.z, -l.x) * 180f;                           // dig in as the burn takes hold
            OnCruise(true);
        }

        void Cruise(float dt, Quaternion aim, Vector3 wish)
        {
            if (!input.boost || input.fire || input.aim) { EndCruise(0); return; }
            if (!CanCruise()) { EndCruise(1); return; }                        // contact: drop out of the cruise
            Vector3 want = wish.sqrMagnitude > 0.01f ? wish.normalized : aim * Vector3.forward;
            Vector3 prev = cruiseDir;
            cruiseDir = Vector3.RotateTowards(cruiseDir, want, cruiseTurn * dt, 0f).normalized;
            Vector3 a = Vector3.ProjectOnPlane(prev, Vector3.up), b = Vector3.ProjectOnPlane(cruiseDir, Vector3.up);
            float yaw = dt > 1e-5f && a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(a, b, Vector3.up) / dt : 0f;
            _yawRate = Mathf.Lerp(_yawRate, yaw, 1f - Mathf.Exp(-8f * dt));
            cruiseNow = Mathf.MoveTowards(cruiseNow, cruiseSpeed, cruiseAccel * dt);
            velocity = cruiseDir * cruiseNow;
            if (jetpack) jetpack.SetThrust(1f);
        }

        /// why: 0 = released / weapon, 1 = contact, 2 = crashed into something
        void EndCruise(int why)
        {
            if (!cruising) return;
            float k = Cruise01;
            cruising = false;
            brakeLeft = cruiseBrakeTime;
            dashReady = Time.time + (why == 1 ? 0.1f : dashCooldown * 0.5f);   // contact: the next dash is right there
            if (jetpack) jetpack.Cutoff();
            snapLeft = snapTime;
            Vector3 l = Quaternion.Inverse(transform.rotation) * cruiseDir;
            leanVel -= new Vector2(l.z, -l.x) * brakeKick * (0.7f + 0.6f * k);  // retro brake: flare back, overshoot, settle
            if (OwnsCamera) { flightCam.Shake(why == 2 ? 0.45f : why == 1 ? 0.28f : 0.12f); flightCam.FovKick(-(30f + 80f * k)); }
            if (fpRig && firstPersonActive) fpRig.Kick(0.4f + 0.5f * k, l);
            cruiseNow = 0f; _yawRate = 0f;
            OnCruise(false);
        }

        /// Move() hit something while cruising: head-on into a static obstacle = crash out, otherwise slide along it.
        /// Loose debris was swatted aside in Move(): the heading holds.
        void CruiseBump()
        {
            if (!_bumpStatic) return;
            float into = -Vector3.Dot(cruiseDir, _bumpN);
            if (into >= cruiseBumpDot)
            {
                float k = Cruise01;
                if (ImpactFX.Main) ImpactFX.Main.Play(_bumpP, _bumpN, 0.7f + 0.6f * k, Mathf.RoundToInt(20 + 26 * k));
                GameFeel.HitStop(0.04f, 0.1f);
                EndCruise(2);
                velocity = Vector3.ProjectOnPlane(velocity, _bumpN) * 0.35f + _bumpN * (1.5f + 3f * k);   // glance off
            }
            else
            {
                Vector3 along = Vector3.ProjectOnPlane(cruiseDir, _bumpN);
                if (along.sqrMagnitude > 1e-4f) cruiseDir = along.normalized;
            }
        }

        // ------------------------------------------------------------------------------------------------ motion
        void Move(Vector3 delta)
        {
            Vector3 c = transform.position + Vector3.up * bodyCenter;
            Vector3 top = Vector3.up * 0.6f;                                   // capsule up to the head: the FP eye never pokes into blocks
            for (int it = 0; it < 3 && delta.sqrMagnitude > 1e-10f; it++)
            {
                float len = delta.magnitude; Vector3 dir = delta / len;
                if (Cast(c - Vector3.up * 0.45f, c + top, dir, len + 0.03f, out var h))
                {
                    float travel = Mathf.Max(0f, h.distance - 0.03f);
                    transform.position += dir * travel; c += dir * travel;
                    bool loose = h.rigidbody && !h.rigidbody.isKinematic;
                    if (loose)                                                 // cruising: loose debris gets swatted out of the way
                        h.rigidbody.AddForceAtPosition(dir * (cruising ? velocity.magnitude * 1.15f : Mathf.Min(velocity.magnitude, 8f) * 0.6f), h.point, ForceMode.VelocityChange);
                    if (!_bumped || !loose) { _bumpN = h.normal; _bumpP = h.point; _bumpStatic = !loose; }
                    _bumped = true;
                    if (Vector3.Dot(velocity, h.normal) < 0f) velocity = Vector3.ProjectOnPlane(velocity, h.normal) * 0.8f;
                    delta = Vector3.ProjectOnPlane(delta - dir * travel, h.normal);
                }
                else { transform.position += delta; break; }
            }
        }

        /// The body's sweep. Normal flight: the first thing in the way (as always). passThrough: the nearest hit that is
        /// not ours and not something PassThrough() lets the body fly through.
        bool Cast(Vector3 p0, Vector3 p1, Vector3 dir, float len, out RaycastHit hit)
        {
            if (!passThrough)
                return Physics.CapsuleCast(p0, p1, bodyRadius, dir, out hit, len, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(transform);
            hit = default;
            float best = float.MaxValue;
            int n = Physics.CapsuleCastNonAlloc(p0, p1, bodyRadius, dir, s_hits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue;      // overlapping at the start: CapsuleCast skips those too
                if (h.distance >= best || h.transform.IsChildOf(transform) || PassThrough(h.collider)) continue;
                best = h.distance; hit = h;
            }
            return best < float.MaxValue;
        }

        /// While passThrough is on: may the body fly through this collider?
        protected virtual bool PassThrough(Collider c) => false;

        void Face(float dt, Quaternion aim, Vector3 wish)
        {
            Vector3 f = aim * Vector3.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            if (cruising && !firstPersonActive)
            {
                // cruising: the body flies along its heading and carves the turns, unless the heading points back at the camera
                Vector3 h = cruiseDir; h.y = 0f;
                if (h.sqrMagnitude > 0.04f && Vector3.Dot(h.normalized, f.normalized) > -0.17f) f = h;
            }
            bool busy = firstPersonActive || FaceCamera() || wish.sqrMagnitude > 0.01f || Dashing || cruising;
            float rate = firstPersonActive ? 4000f : busy ? turnSpeed : turnSpeed * 0.3f;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(f.normalized, Vector3.up), rate * dt);
        }

        void Lean(float dt)
        {
            if (!leanPivot) return;
            Vector3 lv = Quaternion.Inverse(transform.rotation) * velocity;
            Vector2 target;
            if (Dashing)
            {
                Vector3 l = Quaternion.Inverse(transform.rotation) * dashDir;
                target = new Vector2(l.z * dashLean - l.y * 18f, -l.x * dashLean * 0.8f);
            }
            else if (cruising)
            {
                // flat out along the heading, banked into the turn (right turn = head to the right)
                Vector3 l = Quaternion.Inverse(transform.rotation) * cruiseDir;
                float bank = Mathf.Clamp(-_yawRate * cruiseBank, -cruiseMaxBank, cruiseMaxBank);
                target = new Vector2(l.z * cruiseLean - l.y * 18f, -l.x * cruiseLean * 0.8f + bank);
            }
            else target = new Vector2(Mathf.Clamp(lv.z * leanPerSpeed - lv.y * 1.5f, -maxLean, maxLean), Mathf.Clamp(-lv.x * rollPerSpeed, -maxLean, maxLean));
            target += LeanOffset();
            bool snap = snapLeft > 0f; snapLeft -= dt;
            Spring.Step(ref lean, ref leanVel, target, snap ? snapFreq : leanFreq, snap ? snapDamping : leanDamping, dt);
            leanVel = Vector2.ClampMagnitude(leanVel, leanSpeedLimit);
            lean = Vector2.ClampMagnitude(lean, leanLimit);
            float t = Time.time;
            float bob = bobAmp * Mathf.Sin(2f * Mathf.PI * bobFreq * t + _seed);
            float swayR = swayDeg * Mathf.Sin(2f * Mathf.PI * 0.21f * t + _seed * 2f), swayP = swayDeg * 0.6f * Mathf.Sin(2f * Mathf.PI * 0.17f * t + 1f + _seed);
            leanPivot.localPosition = Vector3.up * (pivotHeight + bob);
            // one tilt about a horizontal axis: pitch and roll never compose into a yaw twist
            Vector2 tilt = new Vector2(lean.x + swayP, lean.y + swayR);
            float ang = tilt.magnitude;
            leanPivot.localRotation = ang > 1e-3f ? Quaternion.AngleAxis(ang, new Vector3(tilt.x, 0f, tilt.y) / ang) : Quaternion.identity;
        }

        protected virtual Vector2 LeanOffset() => Vector2.zero;

        /// After the Animator: swing the thighs at the hips behind the body lean (lags and overshoots it).
        protected virtual void LateUpdate()
        {
            if (!_thighL || !_thighR || !leanPivot) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Spring.Step(ref _leg, ref _legVel, lean, legFreq, legDamping, dt);
            float pitch = Mathf.Clamp(_leg.x * legFollow, -legMax, legMax);
            float roll = Mathf.Clamp(_leg.y * legFollow * 0.6f, -legMax * 0.6f, legMax * 0.6f);
            Vector3 right = leanPivot.right, fwd = leanPivot.forward;
            Quaternion q = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(-roll, fwd);
            _thighL.rotation = q * _thighL.rotation;
            _thighR.rotation = q * _thighR.rotation;
        }

        // ------------------------------------------------------------------------------------------------ first person
        void SetFirstPerson(bool on)
        {
            firstPersonActive = on;
            if (_body != null)
                for (int i = 0; i < _body.Length; i++) if (_body[i]) _body[i].shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : _bodyShadow[i];
            if (jetpack) jetpack.SetVisible(!on);
            if (fpRig)
            {
                fpRig.gameObject.SetActive(on);
                if (on) { fpRig.flight = this; SyncViewmodel(); }
            }
            OnViewChanged(on);
        }

        protected virtual void OnViewChanged(bool fp) { }
        /// Push the current animator state to the viewmodel when it is switched on.
        protected virtual void SyncViewmodel() { }

        // ------------------------------------------------------------------------------------------------ animator (body + viewmodel)
        protected static readonly int SpeedId = Animator.StringToHash("Speed");
        protected Animator FpAnim => firstPersonActive && fpRig ? fpRig.animator : null;
        protected void SetFloat(int id, float v) { animator.SetFloat(id, v); var f = FpAnim; if (f) f.SetFloat(id, v); }
        protected void SetBool(int id, bool v) { animator.SetBool(id, v); var f = FpAnim; if (f) f.SetBool(id, v); }
        protected void SetInt(int id, int v) { animator.SetInteger(id, v); var f = FpAnim; if (f) f.SetInteger(id, v); }
        protected void Trigger(int id) { animator.SetTrigger(id); var f = FpAnim; if (f) f.SetTrigger(id); }
    }
}
