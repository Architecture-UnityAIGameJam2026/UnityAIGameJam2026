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
    }

    /// Shared zero-g flight for the play-test characters. Nobody walks: the body floats, the booster pack pushes it.
    ///   WASD = move along the camera aim (3D), Space / Ctrl = up / down, Shift = dash (ignition flash, then a hard cut-off).
    ///   Velocity with acceleration and drag (drifts when you let go), the body yaws to the camera, and a damped spring leans
    ///   it (pitch / roll from the local velocity, big lean while dashing) on a pivot ~1 m up inserted above the "Model".
    ///   The dash ends with a brake kick: the body flares back, overshoots and settles (the snap).
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
        Renderer[] _body;
        ShadowCastingMode[] _bodyShadow;
        Transform _thighL, _thighR;
        Vector2 _leg, _legVel;
        float _seed;
        bool _ready;

        public Vector3 Velocity => velocity;
        public bool Dashing => dashLeft > 0f;
        public bool FirstPerson => firstPersonActive;

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
                i.alt = k.qKey.wasPressedThisFrame;
            }
            if (m != null && live && !consumed)
            {
                i.fire = m.leftButton.wasPressedThisFrame; i.fireHeld = m.leftButton.isPressed; i.aim = m.rightButton.isPressed;
            }
#else
            i.move = new Vector3(Input.GetAxisRaw("Horizontal"), (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0), Input.GetAxisRaw("Vertical"));
            i.dash = Input.GetKeyDown(KeyCode.LeftShift); i.alt = Input.GetKeyDown(KeyCode.Q);
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

            if (input.dash && dashLeft <= 0f && Time.time >= dashReady && CanDash())
                StartDash(wish.sqrMagnitude > 0.01f ? wish.normalized : aim * Vector3.forward);

            if (dashLeft > 0f)
            {
                dashLeft -= dt;
                if (wish.sqrMagnitude > 0.01f) dashDir = Vector3.RotateTowards(dashDir, wish.normalized, 1.2f * dt, 0f);
                velocity = dashDir * dashSpeed;
                if (jetpack) jetpack.SetThrust(1f);
                if (dashLeft <= 0f) EndDash();
            }
            else
            {
                velocity += wish * (acceleration * dt);
                velocity *= Mathf.Exp(-(wish.sqrMagnitude > 0.01f ? inputDrag : idleDrag) * dt);
                if (jetpack) jetpack.SetThrust(Mathf.Clamp01(idleThrust + wish.magnitude * 0.55f + ExtraThrust()));
            }

            Tick(dt, aim);
            Move(velocity * dt);
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
        public void ResetMotion() { velocity = Vector3.zero; dashLeft = 0f; snapLeft = 0f; lean = leanVel = _leg = _legVel = Vector2.zero; }

        public Quaternion AimRotation()
        {
            if (flightCam) return flightCam.Aim;
            if (cameraTransform) return cameraTransform.rotation;
            return transform.rotation;
        }

        protected void StartDash(Vector3 dir)
        {
            dashDir = dir.normalized; dashLeft = dashTime; dashReady = Time.time + dashTime + dashCooldown;
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

        // ------------------------------------------------------------------------------------------------ motion
        void Move(Vector3 delta)
        {
            Vector3 c = transform.position + Vector3.up * bodyCenter;
            Vector3 top = Vector3.up * 0.6f;                                   // capsule up to the head: the FP eye never pokes into blocks
            for (int it = 0; it < 3 && delta.sqrMagnitude > 1e-10f; it++)
            {
                float len = delta.magnitude; Vector3 dir = delta / len;
                if (Physics.CapsuleCast(c - Vector3.up * 0.45f, c + top, bodyRadius, dir, out var h, len + 0.03f, ~0, QueryTriggerInteraction.Ignore) && !h.transform.IsChildOf(transform))
                {
                    float travel = Mathf.Max(0f, h.distance - 0.03f);
                    transform.position += dir * travel; c += dir * travel;
                    if (h.rigidbody && !h.rigidbody.isKinematic)
                        h.rigidbody.AddForceAtPosition(dir * Mathf.Min(velocity.magnitude, 8f) * 0.6f, h.point, ForceMode.VelocityChange);
                    if (Vector3.Dot(velocity, h.normal) < 0f) velocity = Vector3.ProjectOnPlane(velocity, h.normal) * 0.8f;
                    delta = Vector3.ProjectOnPlane(delta - dir * travel, h.normal);
                }
                else { transform.position += delta; break; }
            }
        }

        void Face(float dt, Quaternion aim, Vector3 wish)
        {
            Vector3 f = aim * Vector3.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            bool busy = firstPersonActive || FaceCamera() || wish.sqrMagnitude > 0.01f || Dashing;
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
