using UnityEngine;

namespace OYM
{
    /// Torque-free tumbling for zero-g debris / asteroids (constant world-space angular velocity).
    [DisallowMultipleComponent]
    public class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 2f;
        Rigidbody _rb;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
        }

        void FixedUpdate()
        {
            var dq = Quaternion.AngleAxis(degreesPerSecond * Time.fixedDeltaTime, axis);
            if (_rb != null) _rb.MoveRotation(dq * _rb.rotation);
            else transform.rotation = dq * transform.rotation;
        }
    }
}
