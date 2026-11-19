using UnityEngine;

namespace OYM
{
    /// Slow zero-g drift for rocks / debris: the object circles `center` around `axis` at `degreesPerSecond`
    /// with a gentle bob along the axis. Pair with Spin for tumbling. Moved by DebrisMotion (one job for all of
    /// them); a non-kinematic Rigidbody still uses MovePosition.
    [DisallowMultipleComponent]
    public class Drift : MonoBehaviour
    {
        public Vector3 center;
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 1f;
        public float bobAmplitude = 0.5f;
        public float bobPeriod = 12f;

        Vector3 _r0;
        float _phase;
        Rigidbody _rb;
        bool _started;

        internal int slot = -1;
        internal bool physics;
        internal Vector3 R0 => _r0;
        internal float Phase => _phase;

        void Start()
        {
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            _r0 = transform.position - center;
            _phase = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.11f, 6.2831853f);
            _rb = GetComponent<Rigidbody>();
            _started = true;
            DebrisMotion.Register(this, _rb);
        }

        void OnEnable() { if (_started) DebrisMotion.Register(this, _rb); }
        void OnDisable() => DebrisMotion.Unregister(this);

        /// fixed-step path, only for non-kinematic bodies (called by DebrisMotion.FixedUpdate)
        internal void PhysicsStep()
        {
            float t = Time.time;
            _rb.MovePosition(center + Quaternion.AngleAxis(degreesPerSecond * t, axis) * _r0
                             + axis * (Mathf.Sin(t * 6.2831853f / Mathf.Max(bobPeriod, 0.1f) + _phase) * bobAmplitude));
        }
    }
}
