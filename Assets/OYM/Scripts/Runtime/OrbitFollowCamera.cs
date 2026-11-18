using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Third-person orbit camera for character tests: follows `target`, RMB-drag (or locked cursor) to orbit, wheel to zoom.
    public class OrbitFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 pivotOffset = new Vector3(0f, 1.45f, 0f);
        public float distance = 3.2f, minDistance = 1.0f, maxDistance = 9f;
        public float sensitivity = 0.15f, zoomSpeed = 0.002f, followSharpness = 12f;
        float _yaw, _pitch;
        Vector3 _pivot;

        void Start()
        {
            if (!target) return;
            _pivot = target.position + pivotOffset;
            Vector3 d = transform.position - _pivot;
            if (d.sqrMagnitude > 1e-4f)
            {
                distance = d.magnitude;
                _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 180f;
                _pitch = Mathf.Asin(Mathf.Clamp(-d.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        void LateUpdate()
        {
            if (!target) return;
            Vector2 look = Vector2.zero; float scroll = 0f; bool held = false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m != null) { held = m.rightButton.isPressed || Cursor.lockState == CursorLockMode.Locked; look = m.delta.ReadValue(); scroll = m.scroll.ReadValue().y; }
#else
            held = Input.GetMouseButton(1); look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f; scroll = Input.mouseScrollDelta.y * 120f;
#endif
            if (held) { _yaw += look.x * sensitivity; _pitch = Mathf.Clamp(_pitch - look.y * sensitivity, -30f, 70f); }
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
            _pivot = Vector3.Lerp(_pivot, target.position + pivotOffset, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_pivot - rot * Vector3.forward * distance, rot);
        }
    }
}
