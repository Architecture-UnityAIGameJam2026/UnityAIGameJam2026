using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Zero-g preview camera: WASD move, Space/Ctrl up/down, Q/E roll, Shift boost, RMB (or locked cursor) look.
    public class FreeFlyCamera : MonoBehaviour
    {
        public float speed = 40f;
        public float boost = 6f;
        public float lookSensitivity = 0.12f;
        public float rollSpeed = 60f;
        public float damping = 6f;
        Vector3 _vel;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 move = Vector3.zero;
            Vector2 look = Vector2.zero;
            float roll = 0f;
            bool fast = false, lookHeld = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            if (k != null)
            {
                if (k.wKey.isPressed) move.z += 1; if (k.sKey.isPressed) move.z -= 1;
                if (k.dKey.isPressed) move.x += 1; if (k.aKey.isPressed) move.x -= 1;
                if (k.spaceKey.isPressed) move.y += 1; if (k.leftCtrlKey.isPressed) move.y -= 1;
                if (k.qKey.isPressed) roll += 1; if (k.eKey.isPressed) roll -= 1;
                fast = k.leftShiftKey.isPressed;
            }
            if (m != null) { lookHeld = m.rightButton.isPressed || Cursor.lockState == CursorLockMode.Locked; look = m.delta.ReadValue(); }
#else
            move = new Vector3(Input.GetAxisRaw("Horizontal"), (Input.GetKey(KeyCode.Space) ? 1 : 0) - (Input.GetKey(KeyCode.LeftControl) ? 1 : 0), Input.GetAxisRaw("Vertical"));
            roll = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);
            fast = Input.GetKey(KeyCode.LeftShift);
            lookHeld = Input.GetMouseButton(1);
            look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
            if (lookHeld)
            {
                transform.rotation *= Quaternion.Euler(-look.y * lookSensitivity, look.x * lookSensitivity, 0f);
            }
            transform.rotation *= Quaternion.Euler(0f, 0f, roll * rollSpeed * dt);
            Vector3 target = transform.TransformDirection(move) * speed * (fast ? boost : 1f);
            _vel = Vector3.Lerp(_vel, target, 1f - Mathf.Exp(-damping * dt));
            transform.position += _vel * dt;
        }
    }
}
