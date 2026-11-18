using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Booster pack FX (KESTREL twin cyan nozzles). Ignite(): a blinding blue-white flash that floods the character
    /// with light + radial sparks, then the jet plumes take over; SetThrust() drives the plumes; Cutoff() kills them hard.
    /// Particle sprites are screen-relative HDR (OYM/FX/AdditiveHDR) so they bloom the same in any map; the lights are
    /// physical and get scaled by the scene's fixed exposure so the flash floods the body equally in dark and bright maps.
    public class JetpackFX : MonoBehaviour
    {
        public ParticleSystem[] plumes;
        public ParticleSystem[] cores;
        public ParticleSystem flash, flashCore, sparks;
        public Light[] nozzleLights;
        public Light flashLight;

        [Header("Look")]
        public float plumeRate = 260f;
        public float coreRate = 70f;
        [Tooltip("Lumens at the reference exposure; scaled by 2^(EV - referenceEV)")]
        public float nozzleLumen = 2500f;
        public float flashLumen = 26000f;
        public float referenceEV = 9f;
        [Tooltip("Read the fixed exposure of the global volume at start (else use exposureEV)")]
        public bool readSceneExposure = true;
        public float exposureEV = 9f;

        [Header("Timing")]
        public float spinUp = 25f;
        public float spinDown = 14f;
        public float flashDecay = 0.09f;

        float _target, _thrust, _flash, _simTime;

        void Start()
        {
            if (!readSceneExposure) return;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.sharedProfile && v.sharedProfile.TryGet<Exposure>(out var ex) && ex.mode.value == ExposureMode.Fixed)
                {
                    exposureEV = ex.fixedExposure.value;
                    break;
                }
        }

        public void Ignite()
        {
            _flash = 1f; _thrust = 1f; _target = 1f;
            if (flash) flash.Emit(1);
            if (flashCore) flashCore.Emit(1);
            if (sparks) sparks.Emit(48);
        }

        public void SetThrust(float t) { _target = Mathf.Clamp01(t); }

        /// First person: the pack's sprites would sweep over the camera, so only its light (on the arms) stays.
        public void SetVisible(bool visible)
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r) r.enabled = visible;
            }
        }

        /// Small burst (slash lunge): a fraction of the ignition flash + sparks, thrust spikes then falls back.
        public void Puff(float strength)
        {
            _flash = Mathf.Max(_flash, 0.35f * strength); _thrust = Mathf.Max(_thrust, 0.9f * strength);
            if (flashCore) flashCore.Emit(1);
            if (sparks) sparks.Emit(Mathf.RoundToInt(16 * strength));
        }

        public void Cutoff()
        {
            _target = 0f;
            _thrust = Mathf.Min(_thrust, 0.3f);
            if (sparks) sparks.Emit(10);
        }

        void Update() { Apply(Time.deltaTime, Time.time); }

        /// Manual step for edit-mode / batch captures (Update() does not run there).
        public void Tick(float dt) { _simTime += dt; Apply(dt, _simTime); }

        void Apply(float dt, float time)
        {
            _thrust = Mathf.MoveTowards(_thrust, _target, (_target > _thrust ? spinUp : spinDown) * dt);
            _flash *= Mathf.Exp(-dt / flashDecay);
            float k = Mathf.Pow(2f, exposureEV - referenceEV);
            float flicker = 0.82f + 0.18f * Mathf.PerlinNoise(time * 31f, 0.37f);
            if (plumes != null) foreach (var p in plumes) { if (!p) continue; var em = p.emission; em.rateOverTimeMultiplier = plumeRate * _thrust; }
            if (cores != null) foreach (var p in cores) { if (!p) continue; var em = p.emission; em.rateOverTimeMultiplier = _thrust > 0.02f ? coreRate : 0f; }
            if (nozzleLights != null) foreach (var l in nozzleLights) { if (!l) continue; l.intensity = nozzleLumen * k * _thrust * flicker; l.enabled = _thrust > 0.01f; }
            if (flashLight) { flashLight.intensity = flashLumen * k * _flash; flashLight.enabled = _flash > 0.005f; }
        }

        /// Editor capture helper: deterministic state `t` seconds after Ignite() while thrusting.
        public void PreviewAt(float t, float ev)
        {
            exposureEV = ev;
            foreach (var p in new[] { flash, flashCore, sparks })
                if (p) { p.Clear(true); p.Simulate(0f, true, true); }
            if (flash) flash.Emit(1);
            if (flashCore) flashCore.Emit(1);
            if (sparks) sparks.Emit(48);
            foreach (var p in new[] { flash, flashCore, sparks })
                if (p && t > 0f) p.Simulate(t, true, false);
            _thrust = 1f; _target = 1f; _flash = Mathf.Exp(-t / flashDecay);
            Apply(0f, 0f);
            if (plumes != null) foreach (var p in plumes) if (p) p.Simulate(Mathf.Max(t, 0.25f), true, true);
            if (cores != null) foreach (var p in cores) if (p) p.Simulate(0.2f, true, true);
        }
    }
}
