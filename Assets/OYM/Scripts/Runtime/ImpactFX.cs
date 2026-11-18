using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Shared hit flash (world-space particle systems moved to the hit point and burst): HDR flare + core + a spark
    /// spray along the surface normal + a short light flash. Used by the railgun beam and the sword cuts.
    public class ImpactFX : MonoBehaviour
    {
        public static ImpactFX Main { get; private set; }
        public ParticleSystem flash, core, sparks;
        public Light flashLight;
        public float flashLumen = 18000f, referenceEV = 9f, exposureEV = 9f, decay = 0.08f;
        float _flash;

        void Awake() { Main = this; }
        void OnEnable() { Main = this; }

        void Start()
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.sharedProfile && v.sharedProfile.TryGet<Exposure>(out var ex) && ex.mode.value == ExposureMode.Fixed)
                { exposureEV = ex.fixedExposure.value; break; }
        }

        public void Play(Vector3 pos, Vector3 normal, float scale = 1f, int sparkCount = 40)
        {
            var cam = FlightCamera.Main;                                       // right in the face (FP melee): keep it from whiting out the view
            if (cam) scale *= Mathf.Clamp(Vector3.Distance(cam.transform.position, pos) / 3.5f, 0.22f, 1f);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up));
            var ep = new ParticleSystem.EmitParams();
            if (flash) { ep.startSize = flash.main.startSize.constantMax * scale; flash.Emit(ep, 1); }
            if (core) { ep.startSize = core.main.startSize.constantMax * scale; core.Emit(ep, 1); }
            if (sparks) sparks.Emit(Mathf.RoundToInt(sparkCount * Mathf.Sqrt(scale)));
            _flash = Mathf.Max(_flash, scale);
        }

        void Update()
        {
            _flash *= Mathf.Exp(-Time.deltaTime / decay);
            if (flashLight)
            {
                flashLight.intensity = flashLumen * Mathf.Pow(2f, exposureEV - referenceEV) * _flash;
                flashLight.enabled = _flash > 0.01f;
            }
        }
    }
}
