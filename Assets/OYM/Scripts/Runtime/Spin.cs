using UnityEngine;

namespace OYM
{
    /// Torque-free tumbling for zero-g debris / asteroids (constant world-space angular velocity).
    /// Moved by DebrisMotion (one job for all of them); a non-kinematic Rigidbody still turns with MoveRotation.
    [DisallowMultipleComponent]
    public class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 2f;
        Rigidbody _rb;

        internal int slot = -1;
        internal bool physics;
        internal float radius; // bounding radius, measured once by DebrisMotion

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
        }

        void OnEnable() => DebrisMotion.Register(this, _rb);
        void OnDisable() => DebrisMotion.Unregister(this);

        /// fixed-step path, only for non-kinematic bodies (called by DebrisMotion.FixedUpdate)
        internal void PhysicsStep()
        {
            var dq = Quaternion.AngleAxis(degreesPerSecond * Time.fixedDeltaTime, axis);
            _rb.MoveRotation(dq * _rb.rotation);
        }
    }
}
