using UnityEngine;

namespace OYM
{
    /// Continuous energy beam (PRISM's red-magenta lance, or a player beam weapon). While `firing`, a LineRenderer
    /// with OYM/FX/Beam runs from `origin` along its +Z to the first collider (or maxLength); where it lands, a
    /// looping laser-burn impact (molten core, spark spray, heat haze, flickering light) follows the hit point and the
    /// target's HitFlash keeps a hot spot pulsing. Ramps up / down with a short envelope; the width breathes.
    public class BeamFX : MonoBehaviour
    {
        public Transform origin;
        public LineRenderer line;
        public HitFXInstance impactPrefab;
        public Transform muzzleGlow;
        public bool firing;

        public float maxLength = 150f;
        public float width = 0.32f;
        public LayerMask mask = ~0;
        public float rampUp = 0.06f, rampDown = 0.14f;

        [Header("Target reaction")]
        [ColorUsage(false, true)] public Color hitColor = new Color(6f, 0.5f, 1.8f);
        public float hitSize = 0.55f;

        float _env, _t;
        HitFXInstance _impact;

        public float Envelope => _env;

        void Update() { Step(Time.deltaTime); }

        public void Step(float dt)
        {
            if (!origin || !line) return;
            _t += dt;
            float target = firing ? 1f : 0f;
            float rate = 1f / Mathf.Max(1e-3f, firing ? rampUp : rampDown);
            _env = Mathf.MoveTowards(_env, target, rate * dt);

            Vector3 o = origin.position, dir = origin.forward;
            Vector3 end = o + dir * maxLength, n = -dir;
            Collider hitCol = null;
            if (_env > 0f && Physics.Raycast(o, dir, out var hit, maxLength, mask, QueryTriggerInteraction.Ignore))
            { end = hit.point; n = hit.normal; hitCol = hit.collider; }

            bool on = _env > 0.001f;
            line.enabled = on;
            if (muzzleGlow) muzzleGlow.gameObject.SetActive(on);
            if (on)
            {
                line.positionCount = 2;
                line.SetPosition(0, o);
                line.SetPosition(1, end);
                float breathe = 1f + 0.07f * Mathf.Sin(_t * 47f) + 0.05f * Mathf.Sin(_t * 113f);
                float ease = _env * _env * (3f - 2f * _env);
                line.widthMultiplier = width * (0.25f + 0.75f * ease) * breathe;
                var c = new Color(1f, 1f, 1f, ease);
                line.startColor = c; line.endColor = c;
            }

            if (on && hitCol && impactPrefab)
            {
                if (!_impact || !_impact.Playing)
                {
                    _impact = Application.isPlaying ? HitFX.Spawn(impactPrefab) : Instantiate(impactPrefab);
                    _impact.transform.SetPositionAndRotation(end, Quaternion.LookRotation(n));
                    _impact.Play();
                }
                _impact.transform.SetPositionAndRotation(end, Quaternion.LookRotation(n));
                _impact.SetSustain(_env);
                var hf = hitCol.GetComponentInParent<HitFlash>();
                if (hf) hf.HoldHit(end, hitColor * _env, hitSize);
            }
            else if (_impact) ReleaseImpact();
            if (!Application.isPlaying && _impact) _impact.Tick(dt);
        }

        void ReleaseImpact()
        {
            if (Application.isPlaying) _impact.Stop();
            else DestroyImmediate(_impact.gameObject);
            _impact = null;
        }

        void OnDisable()
        {
            if (_impact) ReleaseImpact();
            if (line) line.enabled = false;
        }
    }
}
