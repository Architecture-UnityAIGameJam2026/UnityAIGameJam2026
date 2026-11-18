using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>Mouse look for the local first-person player.</summary>
    public class FirstPersonLook : MonoBehaviour
    {
        [SerializeField] InputActionReference look;
        [SerializeField] Transform pitchTarget;
        [SerializeField] float sensitivity = 0.12f;
        [SerializeField] float minPitch = -75f;
        [SerializeField] float maxPitch = 75f;

        float pitch;

        void OnEnable()
        {
            if (look != null) look.action.Enable();
            if (pitchTarget != null) pitch = NormalizePitch(pitchTarget.localEulerAngles.x);
        }

        void Update()
        {
            if (!CursorLock.IsLocked || look == null || pitchTarget == null) return;

            Vector2 delta = look.action.ReadValue<Vector2>() * (sensitivity * OYM.GameSettings.Sensitivity);
            transform.Rotate(Vector3.up, delta.x, Space.World);

            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
            pitchTarget.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        static float NormalizePitch(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
