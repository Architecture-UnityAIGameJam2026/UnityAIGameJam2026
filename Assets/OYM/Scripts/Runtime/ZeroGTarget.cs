using UnityEngine;

namespace OYM
{
    /// Floating practice target (zero-g rigidbody): hits push and spin it, and its emissive flashes.
    [RequireComponent(typeof(Rigidbody))]
    public class ZeroGTarget : MonoBehaviour
    {
        public Renderer[] renderers;
        public Color flashColor = new Color(1f, 0.6f, 0.25f);
        public float flashIntensity = 6f, flashDecay = 0.12f;
        [Tooltip("flash scales from minNearScale at the camera up to full strength nearFadeDistance (+0.5 m) away")]
        public float nearFadeDistance = 4.5f, minNearScale = 0.12f;
        Rigidbody _rb;
        MaterialPropertyBlock _mpb;
        float _flash;
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        public void Hit(Vector3 point, Vector3 impulse, Color tint)
        {
            _rb.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            _flash = 1f; flashColor = tint;
        }

        public void Hold(Vector3 pos)
        {
            _rb.position = pos; transform.position = pos;
            _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Random.insideUnitSphere * 0.3f;
        }

        void Update()
        {
            if (_flash <= 0f) return;
            _flash *= Mathf.Exp(-Time.deltaTime / flashDecay);
            if (_flash < 0.002f) _flash = 0f;
            Apply(_flash);
        }

        void Apply(float f)
        {
            // up close (first-person melee) the target fills the view: dim its flash so it glows instead of whiting out
            var cam = FlightCamera.Main;
            float near = cam ? Mathf.Clamp((Vector3.Distance(cam.transform.position, transform.position) - 0.5f) / nearFadeDistance, minNearScale, 1f) : 1f;
            foreach (var r in renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissiveId, flashColor * (flashIntensity * f * near));
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
