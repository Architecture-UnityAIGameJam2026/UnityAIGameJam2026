using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OYM
{
    /// One pilot on the main title. It floats between poses on damped springs:
    ///   Home   - side by side with the other pilot, body and head turned away from it (each looks its own way)
    ///   Front  - boosted toward the camera (booster ignition flash), arrives with the snap (fast, small overshoot, hard
    ///            hold), turns its face to the lens and strikes its hero pose: KESTREL plays a cut into a held blade pose
    ///            (slash arc on), ORBIT FRAME charges and fires the RG-77 once; its hero light comes up
    ///   Back   - pushed into the depth while the other pilot has the focus (slow, floaty), looks away again
    ///   Launch - DEPLOY: full boost up and past the camera
    /// Animation is its own PlayableGraph (no Animator controller on the title): a looping idle clip, and a "front" clip
    /// played from frontFrom to frontHold and frozen there. The body leans into the motion around a pivot ~1 m up, and
    /// neck + head are turned toward the look direction after the animation (rest-pose forward axis -> FromToRotation).
    [DefaultExecutionOrder(-10)]
    public class TitleCharacter : MonoBehaviour
    {
        public enum Slot { Home, Front, Back, Launch }

        [System.Serializable]
        public struct Pose
        {
            public Vector3 pos;
            [Tooltip("body yaw in degrees (0 = +Z)")] public float yaw;
            [Tooltip("head look relative to the body: yaw (+ = to its right), pitch (+ = down)")] public Vector2 look;
            public Pose(Vector3 p, float y, Vector2 l) { pos = p; yaw = y; look = l; }
        }

        public string displayName = "KESTREL";
        public Animator animator;
        public JetpackFX jetpack;
        [Tooltip("spot light on the front pose, faded in while this pilot has the focus")]
        public Light heroLight;
        public float heroIntensity = 40000f;

        [Header("Poses (world)")]
        public Pose home;
        public Pose front;
        public Pose back;
        public Vector3 launchVelocity = new Vector3(0f, 9f, -16f);

        [Header("Animation")]
        public AnimationClip idleClip;
        [Tooltip("hero move when this pilot comes to the front (null = stay on the idle clip)")]
        public AnimationClip frontClip;
        public float frontFrom = 1.0f, frontHold = 1.6f, frontSpeed = 1f;
        [Tooltip("slash arc on between these clip times (KESTREL)")]
        public BladeTrail trail;
        public Vector2 trailWindow = new Vector2(-1f, -1f);
        [Tooltip("hold pose: turn the right arm (and the weapon in its hand) by this many degrees in the screen plane, "
               + "counter-clockwise as the camera sees it (KESTREL: blade lowered toward the bottom-left corner, user sketch 11-18)")]
        public float frontArmRoll = 0f;
        [Tooltip("fired once when this pilot comes to the front (ORBIT FRAME)")]
        public RailgunFX railgun;
        public float fireDelay = 0.12f;

        [Header("Look")]
        [Tooltip("the front pose looks at this (the camera)")]
        public Transform lookTarget;
        public float maxHeadYaw = 75f, maxHeadPitch = 30f, lookSharpness = 6f;
        [Range(0f, 1f)] public float lookWeight = 0.92f;

        [Header("Motion")]
        public float pivotHeight = 1.0f;
        public float frontFreq = 2.3f, frontDamping = 0.48f;     // the snap: fast arrival, small overshoot
        public float backFreq = 0.8f, backDamping = 0.9f;        // pushed back: slow drift
        public float homeFreq = 1.1f, homeDamping = 0.8f;
        public float launchAccel = 38f;
        public float leanPerSpeed = 7f, maxLean = 34f, leanFreq = 2.0f, leanDamping = 0.5f;
        public float bobAmp = 0.035f, bobFreq = 0.21f, swayDeg = 1.8f;
        public float boostTime = 0.4f;

        // MARK: calm-pace
        [Header("Pace")]
        [Tooltip("time scale of the title motion: idle clip, bob / sway, the slot springs, lean, head turns, the boost burn. "
               + "1 = the original snappy timing; 0.6 = calm idle (user 11-19: \"성급하게 빠른거같아, 천천히 아이들하게\")")]
        [Range(0.3f, 1f)] public float pace = 0.6f;
        [Tooltip("speed of the hero move clip (KESTREL's cut into the held pose), multiplies frontSpeed")]
        [Range(0.3f, 1f)] public float strikePace = 0.8f;
        [Tooltip("ORBIT FRAME: title-motion seconds it settles at the front before the RG-77 fires (added to fireDelay)")]
        public float settleBeforeFire = 0.75f;

        [Header("Choreography")]
        [Tooltip("world velocity kick (m/s) when it boosts to the front: the path bows out instead of a straight line "
               + "(both focus shots put the pilot on the right third, so +X bows toward the frame edge)")]
        public Vector3 swoopKick = new Vector3(2.2f, 1.1f, 0f);
        [Tooltip("pushed back: retro kick (m/s) and a spin away (deg/s)")]
        public float retroKick = 2.6f, retroSpin = 240f;
        [Tooltip("hero light flash on the strike (x intensity)")]
        public float strikeFlash = 2.5f;

        /// slot changes (TitleFX: boost / retreat / launch presentation)
        public event System.Action<TitleCharacter, Slot> SlotChanged;
        /// the hero move lands: KESTREL's blade locks into the hold pose, ORBIT FRAME's RG-77 fires
        public event System.Action<TitleCharacter> Struck;

        public Slot CurrentSlot => _slot;
        public bool Hovered { get; set; }
        public Vector3 HeadPosition => _head ? _head.position : transform.position + Vector3.up * 1.7f;
        /// 0..1, how much this pilot has the focus (UI / lights)
        public float FocusWeight => _hero;

        Slot _slot = Slot.Home;
        Vector3 _p, _v;
        float _yaw, _yawV;
        Vector2 _lean, _leanV, _look;
        float _hero, _thrustT, _seed, _time, _frontT, _frontW, _fireT = -1f, _flash;
        bool _struck, _shotHooked;
        Transform _head, _neck, _armR;
        Vector3 _headAxis, _neckAxis;
        bool _ready;
        PlayableGraph _graph;
        AnimationMixerPlayable _mix;
        AnimationClipPlayable _idle, _front;

        void Awake() { Init(); }
        void OnDestroy() { DisposeGraph(); }

        void Init()
        {
            if (_ready) return;
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!jetpack) jetpack = GetComponentInChildren<JetpackFX>();
            if (animator && animator.isHuman)
            {
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                _armR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                // the saved scene holds the rest pose: the face points along the root's forward
                if (_head) _headAxis = Quaternion.Inverse(_head.rotation) * transform.forward;
                if (_neck) _neckAxis = Quaternion.Inverse(_neck.rotation) * transform.forward;
            }
            _seed = (displayName.GetHashCode() & 1023) * 0.0173f;
            _ready = true;
            Snap(_slot);
        }

        public Pose PoseOf(Slot s) => s == Slot.Front ? front : s == Slot.Back ? back : home;
        bool InFront => _slot == Slot.Front || _slot == Slot.Launch;

        public void SetSlot(Slot s)
        {
            Init();
            if (s == _slot) return;
            var prev = _slot;
            _slot = s;
            if (s == Slot.Front)
            {
                Boost(boostTime);
                if (prev != Slot.Launch) { _frontT = frontFrom; _frontW = 0f; }
                if (railgun) _fireT = fireDelay + settleBeforeFire;
                _struck = false;
                // swoop: bow the path outward and up, the spring pulls it back in -> a curved boost with an overshoot
                _v += swoopKick;
            }
            else if (s == Slot.Launch) { Boost(1.5f); _v += launchVelocity * 0.35f; }
            else if (prev == Slot.Front)
            {
                if (jetpack) jetpack.Puff(0.6f);                                       // retro burst: kicked back
                Vector3 away = PoseOf(s).pos - front.pos;
                if (away.sqrMagnitude > 1e-4f) _v += away.normalized * retroKick + Vector3.up * 0.5f;
                _yawV += Mathf.Sign(front.pos.x == 0f ? 1f : front.pos.x) * retroSpin;
            }
            SlotChanged?.Invoke(this, s);
        }

        /// Jump straight to the resting state of a slot (scene start, edit-mode captures).
        public void Snap(Slot s)
        {
            if (!_ready) { _slot = s; Init(); return; }
            _slot = s;
            var p = PoseOf(s);
            _p = p.pos; _v = Vector3.zero; _yaw = p.yaw; _yawV = 0f;
            _lean = _leanV = Vector2.zero;
            _look = p.look;
            _hero = InFront ? 1f : 0f;
            _frontT = InFront ? frontHold : frontFrom;
            _frontW = InFront && frontClip ? 1f : 0f;
            _thrustT = 0f; _fireT = -1f;
            if (trail) trail.emitting = false;
            ApplyTransform();
            ApplyLight();
            if (jetpack && !Application.isPlaying) jetpack.Tick(0f);
        }

        void Strike()
        {
            _struck = true;
            _flash = 1f;
            if (jetpack) jetpack.Puff(0.8f);                                           // counter-thrust holds the pose
            Struck?.Invoke(this);
        }

        void OnEnable()
        {
            if (railgun && !_shotHooked) { railgun.Shot += OnShot; _shotHooked = true; }
        }

        void OnDisable()
        {
            if (railgun && _shotHooked) { railgun.Shot -= OnShot; _shotHooked = false; }
        }

        void OnShot(Vector3 muzzle, Vector3 end) { if (_slot == Slot.Front && !_struck) Strike(); }

        void Boost(float seconds)
        {
            if (!jetpack) return;
            jetpack.Ignite();
            _thrustT = seconds;
        }

        // ------------------------------------------------------------------------------------------------ animation graph
        void BuildGraph()
        {
            if (_graph.IsValid() || !animator || !idleClip) return;
            animator.runtimeAnimatorController = null;                      // the graph is the only animation source
            _graph = PlayableGraph.Create(name + " (title)");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "pose", animator);
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            _idle = AnimationClipPlayable.Create(_graph, idleClip);
            _idle.SetTime(_seed * 3f);
            _graph.Connect(_idle, 0, _mix, 0);
            if (frontClip)
            {
                _front = AnimationClipPlayable.Create(_graph, frontClip);
                _front.SetSpeed(0);
                _front.SetApplyFootIK(false);
                _graph.Connect(_front, 0, _mix, 1);
            }
            output.SetSourcePlayable(_mix);
        }

        public void DisposeGraph() { if (_graph.IsValid()) _graph.Destroy(); }

        /// Advance the idle clip by dt and write the pose (dt = 0: just re-evaluate, edit-mode captures).
        public void EvaluatePose(float dt)
        {
            Init();
            BuildGraph();
            if (!_graph.IsValid()) return;
            _mix.SetInputWeight(0, 1f - _frontW);
            if (_front.IsValid()) { _mix.SetInputWeight(1, _frontW); _front.SetTime(_frontT); }
            _graph.Evaluate(dt);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Step(dt);
            EvaluatePose(dt * pace);
        }

        public void Step(float dt)
        {
            Init();
            float rdt = dt;                                                   // real time: flashes stay quick
            dt *= pace;                                                       // title motion time (calm pace)
            _time += dt;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.01f));
            float h = dt / n;
            for (int i = 0; i < n; i++) Integrate(h);

            // front move: play from frontFrom, freeze on frontHold (the snap is in the clip's own timing)
            if (InFront) _frontT = Mathf.Min(_frontT + rdt * frontSpeed * strikePace, frontHold);
            if (_slot == Slot.Front && frontClip && !_struck && _frontT >= frontHold - 1e-4f) Strike();
            float wT = InFront && frontClip ? 1f : 0f;
            _frontW = Mathf.MoveTowards(_frontW, wT, dt / (wT > _frontW ? 0.1f : 0.4f));
            if (trail) trail.emitting = InFront && _frontW > 0.5f && _frontT >= trailWindow.x && _frontT <= trailWindow.y;
            if (_fireT >= 0f) { _fireT -= dt; if (_fireT < 0f && railgun && _slot == Slot.Front) railgun.Fire(); }

            _flash *= Mathf.Exp(-rdt / 0.2f);
            float heroT = InFront ? 1f : Hovered && _slot == Slot.Home ? 0.25f : 0f;
            _hero = Mathf.MoveTowards(_hero, heroT, dt / (heroT > _hero ? 0.3f : 0.6f));

            if (jetpack && _thrustT > 0f)
            {
                _thrustT -= dt;
                jetpack.SetThrust(_slot == Slot.Launch ? 1f : 0.8f);
                if (_thrustT <= 0f) jetpack.Cutoff();
            }
            ApplyTransform();
            ApplyLight();
        }

        void Integrate(float dt)
        {
            if (_slot == Slot.Launch)
            {
                _v += launchVelocity.normalized * launchAccel * dt;
                _p += _v * dt;
            }
            else
            {
                var target = PoseOf(_slot);
                float f = _slot == Slot.Front ? frontFreq : _slot == Slot.Back ? backFreq : homeFreq;
                float z = _slot == Slot.Front ? frontDamping : _slot == Slot.Back ? backDamping : homeDamping;
                float w = 2f * Mathf.PI * f;
                _v += (w * w * (target.pos - _p) - 2f * z * w * _v) * dt;
                _p += _v * dt;
                float wy = w * 1.15f;
                _yawV += (wy * wy * Mathf.DeltaAngle(_yaw, target.yaw) - 2f * z * wy * _yawV) * dt;
                _yaw += _yawV * dt;
            }
            // lean into the motion: + pitch = nose down (moving forward), roll banks into sideways motion
            Vector3 lv = Quaternion.Euler(0f, -_yaw, 0f) * _v;
            Vector2 lt = new Vector2(Mathf.Clamp(lv.z * leanPerSpeed - lv.y * 1.2f, -maxLean, maxLean), Mathf.Clamp(-lv.x * leanPerSpeed, -maxLean, maxLean));
            float lw = 2f * Mathf.PI * leanFreq;
            _leanV += (lw * lw * (lt - _lean) - 2f * leanDamping * lw * _leanV) * dt;
            _lean += _leanV * dt;
        }

        void ApplyTransform()
        {
            float t = Application.isPlaying ? _time : 0f;
            float bob = bobAmp * Mathf.Sin(2f * Mathf.PI * bobFreq * t + _seed * 6f);
            Vector2 tilt = _lean + new Vector2(swayDeg * 0.6f * Mathf.Sin(2f * Mathf.PI * 0.17f * t + 1f + _seed), swayDeg * Mathf.Sin(2f * Mathf.PI * 0.21f * t + _seed * 2f));
            float ang = tilt.magnitude;
            Quaternion leanQ = ang > 1e-3f ? Quaternion.AngleAxis(ang, new Vector3(tilt.x, 0f, tilt.y) / ang) : Quaternion.identity;
            Quaternion rot = Quaternion.Euler(0f, _yaw, 0f) * leanQ;
            Vector3 pivot = _p + Vector3.up * (pivotHeight + bob);
            transform.SetPositionAndRotation(pivot - rot * (Vector3.up * pivotHeight), rot);
        }

        void ApplyLight()
        {
            if (!heroLight) return;
            float w = _hero * _hero * (3f - 2f * _hero);
            heroLight.intensity = heroIntensity * w * (1f + strikeFlash * _flash);
            heroLight.enabled = w > 0.002f;
        }

        // ------------------------------------------------------------------------------------------------ head look
        void LateUpdate() { ApplyArm(); ApplyLook(Time.deltaTime * pace); }

        /// After the animation: lower the weapon arm of the hold pose in the screen plane (rotates about the shoulder).
        public void ApplyArm()
        {
            if (!_armR || frontArmRoll == 0f || !frontClip || _frontW <= 0f || !lookTarget) return;
            _armR.rotation = Quaternion.AngleAxis(frontArmRoll * _frontW, lookTarget.forward) * _armR.rotation;
        }

        /// After the animation: turn neck + head toward the slot's look direction (front: into the camera).
        /// dt <= 0 = jump there (captures).
        public void ApplyLook(float dt)
        {
            if (!_head || _headAxis == Vector3.zero) return;
            Vector2 want = PoseOf(_slot).look;
            if (InFront && lookTarget)
            {
                Vector3 d = lookTarget.position - _head.position;
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, _yaw, 0f)) * d;
                want = new Vector2(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg);
            }
            want.x = Mathf.Clamp(want.x, -maxHeadYaw, maxHeadYaw);
            want.y = Mathf.Clamp(want.y, -maxHeadPitch, maxHeadPitch);
            _look = dt <= 0f ? want : Vector2.Lerp(_look, want, 1f - Mathf.Exp(-lookSharpness * dt));

            Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * (Quaternion.Euler(_look.y, _look.x, 0f) * Vector3.forward);
            if (_neck && _neckAxis != Vector3.zero)
            {
                var dn = Quaternion.FromToRotation(_neck.rotation * _neckAxis, dir);
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, dn, 0.4f * lookWeight) * _neck.rotation;
            }
            var dh = Quaternion.FromToRotation(_head.rotation * _headAxis, dir);
            _head.rotation = Quaternion.Slerp(Quaternion.identity, dh, lookWeight) * _head.rotation;
        }
    }
}
