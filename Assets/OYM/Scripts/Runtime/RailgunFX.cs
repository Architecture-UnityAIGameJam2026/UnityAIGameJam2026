using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// RG-77 shot. Fire() starts the charge: blue arcs light up the rail gap from the rear to the muzzle ("coils light
    /// in sequence"); when full the shot goes: blinding HDR muzzle flash + core, a straight beam that thins out, sparks,
    /// a big light flash, and a snappy recoil kick on the upper chest (damped spring: hard hit, small overshoot, settle).
    public class RailgunFX : MonoBehaviour
    {
        public Transform muzzle, railStart;
        public ParticleSystem chargeArcs, muzzleFlash, muzzleCore, sparks;
        public LineRenderer beam;
        public Light muzzleLight, railLight;
        public Transform recoilBone;

        [Header("Timing")]
        public float chargeTime = 0.32f;
        public float beamTime = 0.18f;
        public float beamLength = 90f;
        public float beamWidth = 0.07f;
        public float flashDecay = 0.07f;

        [Header("Recoil (the snap)")]
        public float recoilDegrees = 11f;
        public float recoilDamping = 13f;
        public float recoilFrequency = 24f;

        [Header("Light (lumens at the reference exposure)")]
        public float flashLumen = 30000f;
        public float railLumen = 1800f;
        public float referenceEV = 9f;
        public bool readSceneExposure = true;
        public float exposureEV = 9f;

        [Header("Aim")]
        [Tooltip("where the beam ends (crosshair hit); null = straight along the rails")]
        public System.Func<Vector3> aimProvider;
        /// (muzzle, beam end) when the shot leaves the gun
        public event System.Action<Vector3, Vector3> Shot;

        float _charge = -1f, _beamT = -1f, _flash, _shotT = 99f;

        public bool Charging => _charge >= 0f;
        public float Charge01 => _charge >= 0f ? Mathf.Clamp01(_charge / chargeTime) : 0f;

        void Start()
        {
            if (beam) beam.enabled = false;
            if (!readSceneExposure) return;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.sharedProfile && v.sharedProfile.TryGet<Exposure>(out var ex) && ex.mode.value == ExposureMode.Fixed)
                { exposureEV = ex.fixedExposure.value; break; }
        }

        public void Fire() { if (_charge < 0f) _charge = 0f; }

        void Update() { Step(Time.deltaTime); }

        void Step(float dt)
        {
            float k = Mathf.Pow(2f, exposureEV - referenceEV);
            float c = 0f;
            if (_charge >= 0f)
            {
                _charge += dt;
                c = Mathf.Clamp01(_charge / chargeTime);
                if (c >= 1f) { Shoot(); _charge = -1f; c = 0f; }
            }
            SetCharge(c, k);
            if (_beamT >= 0f)
            {
                _beamT += dt;
                float t = _beamT / beamTime;
                if (beam)
                {
                    if (muzzle) beam.SetPosition(0, muzzle.position);
                    beam.widthMultiplier = beamWidth * Mathf.Clamp01(1f - t) * (1f + 0.6f * Mathf.Exp(-_beamT / 0.03f));
                    beam.enabled = t < 1f;
                }
                if (t >= 1f) _beamT = -1f;
            }
            _flash *= Mathf.Exp(-dt / flashDecay);
            if (muzzleLight) { muzzleLight.intensity = flashLumen * k * _flash; muzzleLight.enabled = _flash > 0.005f; }
            _shotT += dt;
        }

        void SetCharge(float c, float k)
        {
            if (chargeArcs && muzzle && railStart)
            {
                var em = chargeArcs.emission; em.rateOverTimeMultiplier = c > 0f ? 260f : 0f;
                float len = Vector3.Distance(railStart.position, muzzle.position);
                var sh = chargeArcs.shape;                        // fill the rail gap from the rear toward the muzzle
                sh.scale = new Vector3(0.012f, 0.012f, Mathf.Max(0.01f, len * c));
                sh.position = new Vector3(0f, 0f, -len * 0.5f + len * c * 0.5f);
            }
            if (railLight) { railLight.intensity = railLumen * k * c * c; railLight.enabled = c > 0.01f; }
        }

        void Shoot()
        {
            if (muzzleFlash) muzzleFlash.Emit(1);
            if (muzzleCore) muzzleCore.Emit(1);
            if (sparks) sparks.Emit(36);
            Vector3 end = Vector3.zero;
            if (muzzle && railStart)
            {
                Vector3 dir = (muzzle.position - railStart.position).normalized;
                end = aimProvider != null ? aimProvider() : muzzle.position + dir * beamLength;
                if (muzzleFlash) muzzleFlash.transform.rotation = Quaternion.LookRotation(end - muzzle.position);
                if (beam)
                {
                    beam.SetPosition(0, muzzle.position);
                    beam.SetPosition(1, end);
                    beam.enabled = true; _beamT = 0f;
                }
            }
            _flash = 1f; _shotT = 0f;
            if (muzzle) Shot?.Invoke(muzzle.position, end);
        }

        void LateUpdate() { ApplyRecoil(); }

        void ApplyRecoil()
        {
            if (!recoilBone || _shotT > 1.2f) return;
            float a = recoilDegrees * Mathf.Exp(-_shotT * recoilDamping) * Mathf.Cos(_shotT * recoilFrequency);
            recoilBone.rotation = Quaternion.AngleAxis(-a, transform.right) * recoilBone.rotation;
        }

        /// Manual step for edit-mode / batch captures (Update / LateUpdate do not run there): advance charge + shot state,
        /// then kick the chest. Call after the Animator update so the kick lands on top of the fresh pose.
        public void Tick(float dt) { Step(dt); ApplyRecoil(); }

        /// Editor capture helper: state `t` seconds after the shot left the muzzle (t < 0: mid-charge at -t of chargeTime).
        public void PreviewAt(float t, float ev)
        {
            exposureEV = ev;
            foreach (var p in new[] { chargeArcs, muzzleFlash, muzzleCore, sparks }) if (p) { p.Clear(true); p.Simulate(0f, true, true); }
            float k = Mathf.Pow(2f, exposureEV - referenceEV);
            if (t < 0f)
            {
                SetCharge(Mathf.Clamp01(-t), k);
                if (chargeArcs) chargeArcs.Simulate(0.12f, true, true);
                if (beam) beam.enabled = false;
                if (muzzleLight) muzzleLight.enabled = false;
                return;
            }
            SetCharge(0f, k);
            Shoot();
            foreach (var p in new[] { muzzleFlash, muzzleCore, sparks }) if (p && t > 0f) p.Simulate(t, true, false);
            _beamT = 0f; Step(t); _shotT = t;
        }
    }
}
