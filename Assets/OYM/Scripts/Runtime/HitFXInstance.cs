using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// One impact effect (root of an FX_Hit_* prefab, +Z = surface normal). Play() restarts every particle system
    /// (bursts at t = 0, some delayed) and fires a light flash that decays exponentially (lumens at the reference
    /// exposure, scaled to the scene's fixed exposure like RailgunFX so it floods equally in any map).
    /// Looping effects (laser burn) run until Stop(); SetSustain(0..1) scales their emission + light.
    public class HitFXInstance : MonoBehaviour
    {
        public ParticleSystem[] systems;
        public Light flashLight;
        [Tooltip("Peak lumens at the reference exposure")] public float flashLumen = 20000f;
        public float flashDecay = 0.08f;
        [Tooltip("Looping effects: steady lumens at full sustain (flickers)")] public float sustainLumen;
        public float referenceEV = 9f;
        [Tooltip("Seconds until it returns to the pool")] public float duration = 2f;
        public bool loop;

        [HideInInspector] public HitFXInstance prefabKey;

        float _t, _flash, _sustain = 1f, _flickerSeed;
        bool _playing, _stopping;
        float[] _baseRates;

        static float s_ev = 9f;
        static int s_evFrame = -1000;

        public bool Playing => _playing;

        void Awake() { CacheRates(); }

        void CacheRates()
        {
            if (systems == null) systems = GetComponentsInChildren<ParticleSystem>(true);
            _baseRates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++) if (systems[i]) _baseRates[i] = systems[i].emission.rateOverTimeMultiplier;
        }

        public static float SceneEV()
        {
            if (Time.frameCount - s_evFrame < 120 && Application.isPlaying) return s_ev;
            s_evFrame = Time.frameCount;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.sharedProfile && v.sharedProfile.TryGet<Exposure>(out var ex) && ex.mode.value == ExposureMode.Fixed)
                { s_ev = ex.fixedExposure.value; break; }
            return s_ev;
        }

        public void Play()
        {
            if (_baseRates == null || _baseRates.Length != (systems?.Length ?? 0)) CacheRates();
            _t = 0f; _flash = 1f; _playing = true; _stopping = false; _sustain = 1f;
            _flickerSeed = Random.value * 100f;
            for (int i = 0; i < systems.Length; i++)
            {
                var p = systems[i]; if (!p) continue;
                p.Clear(false);
                var em = p.emission; em.rateOverTimeMultiplier = _baseRates[i];
                p.Play(false);
            }
            UpdateLight(SceneEV());
        }

        /// Looping effects: stop emitting, let live particles finish, then return to the pool.
        public void Stop()
        {
            if (!_playing || _stopping) return;
            _stopping = true; _t = 0f;
            foreach (var p in systems) if (p) p.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        public void SetSustain(float k)
        {
            _sustain = Mathf.Clamp01(k);
            for (int i = 0; i < systems.Length; i++)
            {
                var p = systems[i]; if (!p || !p.main.loop) continue;
                var em = p.emission; em.rateOverTimeMultiplier = _baseRates[i] * _sustain;
            }
        }

        void Update() { if (_playing) Step(Time.deltaTime, false); }

        /// Edit-mode / batch capture: advance particles by hand (Update does not run there).
        public void Tick(float dt)
        {
            if (!_playing) return;
            foreach (var p in systems) if (p) p.Simulate(dt, false, false, false);
            Step(dt, true);
        }

        /// Edit-mode capture helper: the state `t` seconds after Play().
        public void PreviewAt(float t)
        {
            Play();
            foreach (var p in systems) if (p) p.Simulate(Mathf.Max(0f, t), false, true, false);
            _t = t; _flash = Mathf.Exp(-t / Mathf.Max(1e-3f, flashDecay));
            UpdateLight(SceneEV());
        }

        void Step(float dt, bool manual)
        {
            _t += dt;
            _flash *= Mathf.Exp(-dt / Mathf.Max(1e-3f, flashDecay));
            UpdateLight(SceneEV());
            bool done = loop ? (_stopping && _t > duration) : _t > duration;
            if (done)
            {
                _playing = false;
                if (flashLight) flashLight.enabled = false;
                if (!manual) HitFX.Release(this);
            }
        }

        void UpdateLight(float ev)
        {
            if (!flashLight) return;
            float k = Mathf.Pow(2f, ev - referenceEV) * transform.lossyScale.x * transform.lossyScale.x;
            float sustain = 0f;
            if (loop && !_stopping)
            {
                float f = Mathf.PerlinNoise(_flickerSeed, (Application.isPlaying ? Time.time : _t) * 18f);
                sustain = sustainLumen * _sustain * (0.65f + 0.7f * f);
            }
            float lm = flashLumen * _flash + sustain;
            flashLight.intensity = lm * k;
            flashLight.enabled = lm > flashLumen * 0.004f + 1f;
        }
    }
}
