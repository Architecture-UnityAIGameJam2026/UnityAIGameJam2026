using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Zero-g play-test camera: third person over the shoulder or first person at the eyes (V toggles).
    /// Mouse look while the cursor is locked (click the game view to lock, Esc to release), wheel = distance.
    /// Game feel: trauma shake, recoil kick and FOV punch, all damped springs in real time (unaffected by hit-stop).
    [DefaultExecutionOrder(100)]
    public class FlightCamera : MonoBehaviour
    {
        public static FlightCamera Main { get; private set; }

        public Transform target;
        [Header("Third person")]
        public Vector3 pivotOffset = new Vector3(0f, 1.45f, 0f);
        public Vector3 shoulder = new Vector3(0.55f, 0.15f, 0f);
        public float distance = 3.6f, minDistance = 1.2f, maxDistance = 9f;
        public float followSharpness = 12f;
        public float fov = 55f;
        [Header("First person")]
        [Tooltip("Eye position in the character's yaw space (root origin)")]
        public Vector3 eyeOffset = new Vector3(0f, 1.62f, 0.10f);
        public float fpFov = 68f;
        public bool firstPerson;
        [Header("Input")]
        public float sensitivity = 0.11f, zoomSpeed = 0.002f;
        [Tooltip("Look is set from script (SetLook) instead of the mouse")]
        public bool scripted;
        [Header("Feel")]
        public float shakeAngle = 3.2f, shakeFreq = 22f, traumaDecay = 1.8f;
        public float kickFreq = 7f, kickDamping = 0.55f, fovFreq = 4.5f, fovDamping = 0.6f;

        float _yaw, _pitch, _trauma, _seed;
        Vector2 _kick, _kickV;
        float _fovK, _fovKV, _roll, _aimZoom;
        Vector3 _pivot;
        Quaternion _base = Quaternion.identity;
        Camera _cam;
        int _consumedFrame = -1;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        /// Look rotation without shake / kick (aim direction).
        public Quaternion Aim => Quaternion.Euler(_pitch, _yaw, 0f);
        public Vector3 Forward => Aim * Vector3.forward;
        public Vector3 Right => Aim * Vector3.right;
        public Camera Cam => _cam;
        /// True on the frame the click was used to lock the cursor (controllers ignore that click).
        public bool ClickConsumed => _consumedFrame == Time.frameCount;
        public bool InputLive => scripted || Cursor.lockState == CursorLockMode.Locked;

        void Awake()
        {
            Main = this; _cam = GetComponent<Camera>(); _seed = Random.value * 100f;
            Vector3 e = transform.eulerAngles; _yaw = e.y; _pitch = e.x > 180f ? e.x - 360f : e.x;
            if (lightweight) Lighten();
        }

        [Tooltip("turn off the expensive screen-space / volumetric passes on this camera (play-test performance)")]
        public bool lightweight = true;

        void Lighten()
        {
            var hd = GetComponent<HDAdditionalCameraData>();
            if (!hd) return;
            hd.customRenderingSettings = true;
            var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
            var fs = hd.renderingPathCustomFrameSettings;
            foreach (var f in new[] { FrameSettingsField.SSR, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.ReprojectionForVolumetrics,
                                      FrameSettingsField.ContactShadows, FrameSettingsField.Transmission, FrameSettingsField.SubsurfaceScattering })
            {
                mask.mask[(uint)f] = true; fs.SetEnabled(f, false);
            }
            hd.renderingPathCustomFrameSettingsOverrideMask = mask;
            hd.renderingPathCustomFrameSettings = fs;
        }

        void OnEnable() { Main = this; }

        void Start() { if (target) _pivot = target.position + pivotOffset; }

        public void SetLook(float yaw, float pitch) { _yaw = yaw; _pitch = pitch; }
        public void AddLook(float dyaw, float dpitch) { _yaw += dyaw; _pitch += dpitch; }
        public void Shake(float trauma) { _trauma = Mathf.Clamp01(_trauma + trauma); }
        /// Recoil: angular velocity (deg/s) into the kick spring; positive pitch kicks the view up.
        public void Kick(float pitchVel, float yawVel = 0f) { _kickV += new Vector2(-pitchVel, yawVel); }
        public void FovKick(float degPerSec) { _fovKV += degPerSec; }
        public void SetRoll(float deg) { _roll = deg; }
        public void SetAimZoom(float z) { _aimZoom = Mathf.Clamp01(z); }
        public void SnapToTarget() { if (target) _pivot = target.position + pivotOffset; }

        /// Point under the crosshair (or `maxDist` along the aim if nothing is hit). Colliders under `ignore` are skipped.
        public Vector3 AimPoint(float maxDist, Transform ignore, out RaycastHit hit, out bool didHit)
        {
            var ray = new Ray(transform.position, Forward);
            didHit = false; hit = default;
            var hits = Physics.RaycastAll(ray, maxDist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (ignore && h.transform.IsChildOf(ignore)) continue;
                if (firstPerson == false && h.distance < 0.5f) continue;
                if (h.distance < best) { best = h.distance; hit = h; didHit = true; }
            }
            return didHit ? hit.point : ray.origin + ray.direction * maxDist;
        }

        void ReadInput()
        {
            Vector2 look = Vector2.zero; float scroll = 0f; bool toggle = false, esc = false, click = false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; var k = Keyboard.current;
            if (m != null) { look = m.delta.ReadValue(); scroll = m.scroll.ReadValue().y; click = m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame; }
            if (k != null) { toggle = k.vKey.wasPressedThisFrame; esc = k.escapeKey.wasPressedThisFrame; }
#else
            look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f; scroll = Input.mouseScrollDelta.y * 120f;
            toggle = Input.GetKeyDown(KeyCode.V); esc = Input.GetKeyDown(KeyCode.Escape); click = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
#endif
            if (toggle) firstPerson = !firstPerson;
            if (esc) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (click && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; _consumedFrame = Time.frameCount;
            }
            if (scripted) return;
            if (Cursor.lockState == CursorLockMode.Locked) { _yaw += look.x * sensitivity; _pitch -= look.y * sensitivity; }
            if (!firstPerson) distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        void LateUpdate()
        {
            ReadInput();
            if (!target) return;
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);
            _pitch = Mathf.Clamp(_pitch, firstPerson ? -85f : -70f, firstPerson ? 85f : 80f);

            // springs: recoil kick and FOV punch (snap out, settle back)
            Spring.Step(ref _kick, ref _kickV, Vector2.zero, kickFreq, kickDamping, dt);
            Spring.Step(ref _fovK, ref _fovKV, 0f, fovFreq, fovDamping, dt);
            _trauma = Mathf.Max(0f, _trauma - traumaDecay * dt);

            _base = Quaternion.Euler(_pitch, _yaw, 0f);
            float s = _trauma * _trauma, t = Time.unscaledTime * shakeFreq;
            var shake = new Vector3((Mathf.PerlinNoise(_seed, t) - 0.5f), (Mathf.PerlinNoise(_seed + 7f, t) - 0.5f), (Mathf.PerlinNoise(_seed + 13f, t) - 0.5f)) * (2f * shakeAngle * s);
            Quaternion rot = Quaternion.Euler(_pitch + _kick.x + shake.x, _yaw + _kick.y + shake.y, _roll + shake.z * 0.6f);

            Vector3 pos;
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            if (firstPerson)
            {
                pos = target.position + yawRot * eyeOffset;
                _pivot = target.position + pivotOffset;
            }
            else
            {
                _pivot = Vector3.Lerp(_pivot, target.position + pivotOffset, 1f - Mathf.Exp(-followSharpness * dt));
                Vector3 shoulderW = _base * shoulder;
                Vector3 back = -(rot * Vector3.forward);
                float d = distance;
                if (Physics.SphereCast(_pivot + shoulderW, 0.2f, back, out var h, distance, ~0, QueryTriggerInteraction.Ignore) && !h.transform.IsChildOf(target))
                    d = Mathf.Max(0.3f, h.distance - 0.05f);
                pos = _pivot + shoulderW + back * d;
            }
            transform.SetPositionAndRotation(pos, rot);
            if (_cam)
            {
                float baseFov = firstPerson ? fpFov : fov;
                _cam.fieldOfView = Mathf.Clamp(baseFov * (1f - 0.28f * _aimZoom) + _fovK, 20f, 110f);
                _cam.nearClipPlane = firstPerson ? 0.02f : 0.06f;
            }
        }
    }
}
