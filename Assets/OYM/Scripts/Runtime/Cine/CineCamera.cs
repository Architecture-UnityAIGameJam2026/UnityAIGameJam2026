using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.Cine
{
    /// How a shot places the camera.
    public enum ShotMode
    {
        /// world position (offset), aims at the subject (or lookPoint)
        Fixed,
        /// rides with the subject: offset in the follow space (World / Yaw / Full / Velocity)
        Follow,
        /// circles the subject: offset = (radius, height, start angle deg), orbitSpeed deg/s, angle 0 = subject's back
        Orbit,
        /// dialogue: behind `other`'s shoulder looking at `target`. offset = (side m, up m, back m), side + = right shoulder
        OverShoulder,
        /// both subjects in frame from the side. offset = (side sign, up m, distance as a multiple of their gap)
        TwoShot,
        /// dolly from offset to offsetB (world) over duration, aims at the subject
        Rail,
    }

    public enum FollowSpace { World, Yaw, Full, Velocity }

    /// One camera setup. Build it with the static helpers (Fixed / Follow / Orbit / OverShoulder / TwoShot / Rail) and
    /// the fluent modifiers, or fill the fields in the Inspector.
    [System.Serializable]
    public class CineShot
    {
        public string name = "shot";
        public ShotMode mode = ShotMode.Follow;
        [Tooltip("Follow / Orbit / Fixed / Rail: the subject. OverShoulder: who is looked at (speaker). TwoShot: first subject.")]
        public Transform target;
        [Tooltip("OverShoulder: whose shoulder the camera is behind (listener). TwoShot: second subject.")]
        public Transform other;
        [Tooltip("see ShotMode")]
        public Vector3 offset = new Vector3(0f, 1.6f, -4f);
        [Tooltip("Rail: world end position")]
        public Vector3 offsetB;
        [Tooltip("aim point on the subject, in the subject's yaw space")]
        public Vector3 lookOffset = new Vector3(0f, 1.45f, 0f);
        [Tooltip("no subject: world point to aim at")]
        public Vector3 lookPoint;
        public FollowSpace space = FollowSpace.Yaw;
        public float fov = 50f;
        [Tooltip("FOV reached at the end of `duration` (<= 0: no zoom)")]
        public float fovEnd;
        [Tooltip("Rail travel / zoom time (s)")]
        public float duration = 3f;
        [Tooltip("Orbit: deg/s")]
        public float orbitSpeed;
        [Tooltip("camera roll (deg)")]
        public float dutch;
        [Tooltip("lag of the camera position / aim: exponential sharpness 1/s (0 = rigid)")]
        public float posSharpness = 6f, lookSharpness = 9f;
        [Tooltip("handheld wobble (deg)")]
        public float handheld = 0.2f;
        [Tooltip("depth of field focused on the aim point, 0 = off .. 1 = full")]
        [Range(0f, 1f)] public float dof;
        [Tooltip("motion blur 0..1 (< 0 = leave the scene's)")]
        public float motionBlur = -1f;

        // -------------------------------------------------------------------------------------------- builders
        public static CineShot Fixed(Vector3 pos, Transform subject, float fov = 45f) =>
            new CineShot { name = "fixed", mode = ShotMode.Fixed, offset = pos, target = subject, fov = fov, posSharpness = 0f };
        public static CineShot FixedAt(Vector3 pos, Vector3 look, float fov = 45f) =>
            new CineShot { name = "fixed", mode = ShotMode.Fixed, offset = pos, lookPoint = look, fov = fov, posSharpness = 0f, lookSharpness = 0f };
        public static CineShot Follow(Transform subject, Vector3 offset, FollowSpace space = FollowSpace.Yaw, float fov = 55f) =>
            new CineShot { name = "follow", mode = ShotMode.Follow, target = subject, offset = offset, space = space, fov = fov };
        public static CineShot Orbit(Transform subject, float radius, float height, float startDeg, float degPerSec, float fov = 40f) =>
            new CineShot { name = "orbit", mode = ShotMode.Orbit, target = subject, offset = new Vector3(radius, height, startDeg), orbitSpeed = degPerSec, fov = fov };
        public static CineShot OverShoulder(Transform listener, Transform speaker, float side = 0.55f, float up = 0.22f, float back = 1.25f, float fov = 34f) =>
            new CineShot { name = "ots", mode = ShotMode.OverShoulder, other = listener, target = speaker, offset = new Vector3(side, up, back), fov = fov, dof = 1f };
        public static CineShot TwoShot(Transform a, Transform b, float side = 1f, float up = 0.1f, float distance = 1.6f, float fov = 38f) =>
            new CineShot { name = "two", mode = ShotMode.TwoShot, target = a, other = b, offset = new Vector3(side, up, distance), fov = fov, dof = 0.5f };
        public static CineShot Rail(Vector3 from, Vector3 to, float seconds, Transform subject, float fov = 45f) =>
            new CineShot { name = "rail", mode = ShotMode.Rail, offset = from, offsetB = to, duration = seconds, target = subject, fov = fov, posSharpness = 0f };

        public CineShot Named(string n) { name = n; return this; }
        public CineShot Look(Vector3 subjectOffset) { lookOffset = subjectOffset; return this; }
        public CineShot Zoom(float toFov, float seconds) { fovEnd = toFov; duration = seconds; return this; }
        public CineShot Lag(float pos, float look) { posSharpness = pos; lookSharpness = look; return this; }
        public CineShot Dutch(float deg) { dutch = deg; return this; }
        public CineShot Handheld(float deg) { handheld = deg; return this; }
        public CineShot Dof(float w) { dof = w; return this; }
        public CineShot Blur(float w) { motionBlur = w; return this; }
        public CineShot Space(FollowSpace s) { space = s; return this; }
    }

    /// Cutscene camera ("camera follow module"). Sits on the game camera and, while a shot is playing, overrides the pose
    /// written by the gameplay camera (FlightCamera runs at order 100, this at 150) — so the gameplay camera keeps
    /// tracking underneath and Release() can blend back into it seamlessly.
    ///   Play(shot, blend)  cut (blend 0) or eased blend into a shot; shots keep evaluating while they blend out
    ///   Release(blend)     blend back to whatever the gameplay camera does this frame, then step aside
    ///   Shake / Punch / Flash  real-time trauma shake, FOV punch (spring), bloom + exposure flash
    /// Post (private global volume, priority 150): depth of field on the shot's aim point, motion blur, a light vignette.
    [DefaultExecutionOrder(150)]
    public class CineCamera : MonoBehaviour
    {
        public static CineCamera Main { get; private set; }

        [Header("Feel")]
        public float shakeAngle = 2.6f, shakeOffset = 0.05f, shakeFreq = 19f, traumaDecay = 1.5f;
        public float punchFreq = 3.2f, punchDamping = 0.45f;
        public float flashDecay = 0.16f, flashExposure = 1.6f, flashBloom = 0.9f;
        [Header("Post")]
        public float vignette = 0.32f;

        sealed class Live
        {
            public CineShot shot;
            public float t;
            public bool init;
            public Vector3 pos, look, vel, lastTarget;
            public Quaternion rot;
            public float fov, roll, dof, blur;
            public bool frozen;           // a fixed pose (the camera where it was when the cutscene took over)
            public bool gameplay;         // follows the gameplay camera's pose of this frame
        }

        Live _from, _to;
        float _blendT, _blendLen;
        bool _active, _releasing;
        Camera _cam;
        Vector3 _gpPos; Quaternion _gpRot = Quaternion.identity; float _gpFov = 55f;
        float _trauma, _flash, _punch, _punchV, _seed, _postW;
        Volume _vol; Bloom _bloom; ColorAdjustments _color; DepthOfField _dofC; MotionBlur _mb; Vignette _vig;

        public bool Active => _active;
        public bool Blending => _active && _blendT < _blendLen;
        public CineShot Current => _to?.shot;
        public float ShotTime => _to != null ? _to.t : 0f;
        public Camera Cam => _cam;

        public static CineCamera Ensure(Camera cam)
        {
            if (!cam) cam = Camera.main;
            if (!cam) return null;
            var c = cam.GetComponent<CineCamera>();
            return c ? c : cam.gameObject.AddComponent<CineCamera>();
        }

        void Awake() { _cam = GetComponent<Camera>(); _seed = Random.value * 100f; Main = this; }
        void OnEnable() { Main = this; }
        void OnDestroy() { if (_vol) Destroy(_vol.gameObject); }

        // ------------------------------------------------------------------------------------------------ API
        public void Play(CineShot shot, float blend = 0f)
        {
            if (shot == null) return;
            var next = new Live { shot = shot };
            if (blend <= 0f) _from = null;
            else if (!_active) _from = Frozen(transform.position, transform.rotation, _cam ? _cam.fieldOfView : 55f);
            // the outgoing shot keeps moving while it blends out (unless a blend is already running: freeze that mix)
            else _from = _from == null && _to != null && !_to.gameplay ? _to : Snapshot();
            _to = next;
            _blendT = 0f; _blendLen = Mathf.Max(0f, blend);
            _active = true; _releasing = false;
            Step(next, 0f, true);
        }

        /// Hand the camera back to the gameplay camera with an eased blend (0 = cut).
        public void Release(float blend = 1f)
        {
            if (!_active) return;
            _from = Snapshot();
            _to = new Live { gameplay = true };
            _blendT = 0f; _blendLen = Mathf.Max(0f, blend);
            _releasing = true;
            if (blend <= 0f) _active = false;
        }

        public void Shake(float trauma) { _trauma = Mathf.Clamp01(_trauma + trauma); }
        /// FOV punch: deg/s into the spring (negative = punch in).
        public void Punch(float fovVel) { _punchV += fovVel; }
        public void Flash(float amount) { _flash = Mathf.Max(_flash, amount); }

        Live Frozen(Vector3 p, Quaternion r, float fov) => new Live { frozen = true, init = true, pos = p, rot = r, fov = fov, look = p + r * Vector3.forward * 10f };

        /// The blended output of this frame as a fixed pose (start of the next blend).
        Live Snapshot() => Frozen(transform.position, transform.rotation, _cam ? _cam.fieldOfView : 55f);

        // ------------------------------------------------------------------------------------------------ shot math
        static Vector3 Head(Transform t, Vector3 local) => t.position + Quaternion.Euler(0f, t.eulerAngles.y, 0f) * local;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// Raw (undamped) rig pose of a shot at its time t.
        static void Rig(Live l, out Vector3 pos, out Vector3 look)
        {
            var s = l.shot;
            Transform T = s.target;
            Vector3 subject = T ? Head(T, s.lookOffset) : s.lookPoint;
            switch (s.mode)
            {
                default:
                case ShotMode.Fixed:
                    pos = s.offset; look = subject; break;
                case ShotMode.Rail:
                {
                    float k = s.duration > 0f ? Mathf.Clamp01(l.t / s.duration) : 1f;
                    pos = Vector3.Lerp(s.offset, s.offsetB, k * k * (3f - 2f * k));
                    look = subject; break;
                }
                case ShotMode.Follow:
                {
                    Quaternion basis = Quaternion.identity;
                    if (T)
                    {
                        if (s.space == FollowSpace.Yaw) basis = Quaternion.Euler(0f, T.eulerAngles.y, 0f);
                        else if (s.space == FollowSpace.Full) basis = T.rotation;
                        else if (s.space == FollowSpace.Velocity && l.vel.sqrMagnitude > 0.25f) basis = Quaternion.LookRotation(l.vel.normalized, Vector3.up);
                        else if (s.space == FollowSpace.Velocity) basis = Quaternion.Euler(0f, T.eulerAngles.y, 0f);
                    }
                    Vector3 root = T ? T.position : s.lookPoint;
                    pos = root + basis * s.offset;
                    look = T ? root + basis * s.lookOffset : subject;
                    break;
                }
                case ShotMode.Orbit:
                {
                    Vector3 c = T ? T.position : s.lookPoint;
                    float a = (s.offset.z + s.orbitSpeed * l.t) * Mathf.Deg2Rad;
                    float baseYaw = T ? T.eulerAngles.y * Mathf.Deg2Rad : 0f;
                    // angle 0 = behind the subject (its -forward)
                    Vector3 dir = new Vector3(-Mathf.Sin(baseYaw + a), 0f, -Mathf.Cos(baseYaw + a));
                    pos = c + dir * s.offset.x + Vector3.up * s.offset.y;
                    look = subject; break;
                }
                case ShotMode.OverShoulder:
                {
                    Transform A = s.other ? s.other : T, B = T ? T : s.other;
                    Vector3 a = Head(A, s.lookOffset), b = B ? Head(B, s.lookOffset) : s.lookPoint;
                    Vector3 f = Flat(b - a), r = Vector3.Cross(Vector3.up, f);
                    pos = a + r * s.offset.x + Vector3.up * s.offset.y - f * s.offset.z;
                    look = b; break;
                }
                case ShotMode.TwoShot:
                {
                    Transform A = T, B = s.other ? s.other : T;
                    Vector3 a = A ? Head(A, s.lookOffset) : s.lookPoint, b = B ? Head(B, s.lookOffset) : s.lookPoint;
                    Vector3 mid = (a + b) * 0.5f, axis = Flat(b - a);
                    Vector3 side = Vector3.Cross(Vector3.up, axis) * (s.offset.x >= 0f ? 1f : -1f);
                    float d = Mathf.Max(2.2f, Vector3.Distance(a, b) * Mathf.Abs(s.offset.z));
                    pos = mid + side * d + Vector3.up * s.offset.y;
                    look = mid; break;
                }
            }
        }

        static float Damp(float sharp, float dt) => sharp <= 0f ? 1f : 1f - Mathf.Exp(-sharp * dt);

        void Step(Live l, float dt, bool snap)
        {
            if (l.frozen) return;
            if (l.gameplay) { l.pos = _gpPos; l.rot = _gpRot; l.fov = _gpFov; l.dof = 0f; l.blur = -1f; l.init = true; return; }
            var s = l.shot;
            l.t += dt;
            if (s.target)
            {
                Vector3 p = s.target.position;
                if (l.init && dt > 1e-5f) l.vel = Vector3.Lerp(l.vel, (p - l.lastTarget) / dt, Damp(10f, dt));
                l.lastTarget = p;
            }
            Rig(l, out var rp, out var rl);
            if (!l.init || snap) { l.pos = rp; l.look = rl; l.init = true; }
            else
            {
                l.pos = Vector3.Lerp(l.pos, rp, Damp(s.posSharpness, dt));
                l.look = Vector3.Lerp(l.look, rl, Damp(s.lookSharpness, dt));
            }
            float k = s.fovEnd > 0f && s.duration > 0f ? Mathf.Clamp01(l.t / s.duration) : 0f;
            l.fov = s.fovEnd > 0f ? Mathf.Lerp(s.fov, s.fovEnd, k * k * (3f - 2f * k)) : s.fov;
            l.roll = s.dutch;
            l.dof = s.dof; l.blur = s.motionBlur;
            Vector3 d = l.look - l.pos;
            l.rot = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d, Vector3.up) * Quaternion.Euler(0f, 0f, l.roll) : l.rot;
            if (s.handheld > 0f)
            {
                float t = Time.time * 0.55f;
                l.rot *= Quaternion.Euler((Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f * s.handheld,
                                          (Mathf.PerlinNoise(_seed + 3f, t * 0.8f) - 0.5f) * 2f * s.handheld, 0f);
            }
        }

        // ------------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            // whatever the gameplay camera wrote this frame (it runs before us)
            _gpPos = transform.position; _gpRot = transform.rotation; if (_cam) _gpFov = _cam.fieldOfView;
            float rdt = Mathf.Min(GameFeel.RealDt, 0.1f);
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            _trauma = Mathf.Max(0f, _trauma - traumaDecay * rdt);
            _flash *= Mathf.Exp(-rdt / Mathf.Max(1e-3f, flashDecay));
            Spring.Step(ref _punch, ref _punchV, 0f, punchFreq, punchDamping, rdt);

            if (!_active) { Post(rdt, 0f, 0f, -1f, Vector3.zero); return; }

            if (_to != null) Step(_to, dt, false);
            if (_from != null) Step(_from, dt, false);
            _blendT += dt;
            float w = _blendLen <= 0f ? 1f : Mathf.Clamp01(_blendT / _blendLen);
            w = w * w * (3f - 2f * w);

            Vector3 pos; Quaternion rot; float fov, dof, blur; Vector3 look;
            if (_from == null || w >= 1f) { pos = _to.pos; rot = _to.rot; fov = _to.fov; dof = _to.dof; blur = _to.blur; look = _to.look; if (w >= 1f) _from = null; }
            else
            {
                pos = Vector3.Lerp(_from.pos, _to.pos, w);
                rot = Quaternion.Slerp(_from.rot, _to.rot, w);
                fov = Mathf.Lerp(_from.fov, _to.fov, w);
                dof = Mathf.Lerp(_from.dof, _to.dof, w);
                blur = Mathf.Max(_from.blur, _to.blur) < 0f ? -1f : Mathf.Lerp(Mathf.Max(0f, _from.blur), Mathf.Max(0f, _to.blur), w);
                look = Vector3.Lerp(_from.look, _to.look, w);
            }

            // shake (real time: keeps rattling through a hit-stop)
            float s = _trauma * _trauma;
            if (s > 1e-5f)
            {
                float t = Time.unscaledTime * shakeFreq;
                float N(float o) => Mathf.PerlinNoise(_seed + o, t) * 2f - 1f;
                rot *= Quaternion.Euler(N(0f) * shakeAngle * s, N(10f) * shakeAngle * s, N(20f) * shakeAngle * 0.5f * s);
                pos += rot * new Vector3(N(30f), N(40f), 0f) * shakeOffset * s;
            }
            transform.SetPositionAndRotation(pos, rot);
            if (_cam) _cam.fieldOfView = Mathf.Clamp(fov + _punch, 10f, 120f);

            bool done = _releasing && w >= 1f;
            Post(rdt, done ? 0f : 1f - (_releasing ? w : 0f), dof * (_releasing ? 1f - w : 1f), blur, look);
            if (done) { _active = false; _releasing = false; _from = _to = null; }
        }

        // ------------------------------------------------------------------------------------------------ post
        void EnsureVolume()
        {
            if (_vol) return;
            var go = new GameObject("CineCamera post") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _vol = go.AddComponent<Volume>();
            _vol.isGlobal = true; _vol.priority = 150f; _vol.weight = 0f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.hideFlags = HideFlags.DontSave;
            _bloom = p.Add<Bloom>(true); _bloom.intensity.Override(0f); _bloom.scatter.Override(0.75f);
            _bloom.active = false;
            _color = p.Add<ColorAdjustments>(true); _color.postExposure.Override(0f);
            _dofC = p.Add<DepthOfField>(true); _dofC.focusMode.Override(DepthOfFieldMode.Manual); _dofC.active = false;
            _mb = p.Add<MotionBlur>(true); _mb.intensity.Override(0f); _mb.active = false;
            _vig = p.Add<Vignette>(true); _vig.intensity.Override(0f); _vig.smoothness.Override(0.55f); _vig.color.Override(Color.black);
            _vol.sharedProfile = p;
        }

        void Post(float rdt, float on, float dof, float blur, Vector3 look)
        {
            _postW = Mathf.MoveTowards(_postW, on, rdt / 0.4f);
            if (_postW <= 0.001f && _flash <= 0.001f) { if (_vol) _vol.weight = 0f; return; }
            EnsureVolume();
            _vol.weight = 1f;
            _vig.intensity.value = vignette * _postW;
            _color.postExposure.value = flashExposure * _flash;
            _bloom.active = _flash > 0.01f;
            _bloom.intensity.value = flashBloom * _flash;
            _mb.active = blur >= 0f && _postW > 0.01f;
            if (_mb.active) _mb.intensity.value = blur * _postW;
            _dofC.active = dof > 0.01f;
            if (_dofC.active)
            {
                float f = Mathf.Max(0.5f, Vector3.Distance(transform.position, look));
                // near: everything closer than ~half the focus distance (the shoulder in an OTS) goes soft
                _dofC.nearFocusStart.value = f * 0.12f;
                _dofC.nearFocusEnd.value = f * Mathf.Lerp(0.12f, 0.6f, dof);
                _dofC.farFocusStart.value = f * Mathf.Lerp(30f, 1.35f, dof);
                _dofC.farFocusEnd.value = f * Mathf.Lerp(60f, 4f, dof);
            }
        }
    }
}
