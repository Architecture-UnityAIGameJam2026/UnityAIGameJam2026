using UnityEngine;

namespace OYM
{
    /// Slow zero-g drift for rocks / debris: the object circles `center` around `axis` at `degreesPerSecond`
    /// with a gentle bob along the axis. Pair with Spin for tumbling. Uses Rigidbody.MovePosition when present.
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

        void Start()
        {
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            _r0 = transform.position - center;
            _phase = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.11f, 6.2831853f);
            _rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            float t = Time.time;
            Vector3 p = center + Quaternion.AngleAxis(degreesPerSecond * t, axis) * _r0
                        + axis * (Mathf.Sin(t * 6.2831853f / Mathf.Max(bobPeriod, 0.1f) + _phase) * bobAmplitude);
            if (_rb != null) _rb.MovePosition(p);
            else transform.position = p;
        }
    }
}
