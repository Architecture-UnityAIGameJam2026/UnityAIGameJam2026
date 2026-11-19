using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Camera-side hit feedback. Put it on the main camera.
    ///   * shake: trauma model (amount^2, Perlin rotation + a little translation). In play mode the offset is put on
    ///     the camera only while it renders (RenderPipelineManager begin/end camera rendering) and taken off again, so
    ///     camera rigs that move the camera incrementally (FreeFlyCamera) never inherit it;
    ///   * hit-stop: a few dozen ms of near-frozen time on hits close to the camera (the "snap");
    ///   * flash -> wobble: big blasts near the camera used to white the screen out (post exposure + bloom); since 11-19
    ///     ("적이 터지는거 완전 흰색되는거 그거 하지말고 울렁울렁으로") the same requests make the picture wobble instead:
    ///     lens distortion swinging in and out, fading over ~0.5 s. No brightness change at all;
    ///   * damage: PlayerHit() pulses a faction-coloured vignette + chromatic aberration.
    [DefaultExecutionOrder(1000)]
    public class HitScreenFX : MonoBehaviour
    {
        public static HitScreenFX Instance { get; private set; }

        [Header("Shake")]
        public float maxAngle = 2.2f;
        public float maxOffset = 0.06f;
        public float frequency = 22f;
        [Tooltip("Trauma lost per second")] public float recovery = 1.6f;

        [Header("Hit-stop")]
        public float hitStopTimeScale = 0.03f;

        [Header("Wobble (was the white-out) / damage")]
        [Tooltip("Lens distortion swing at a full (1) flash request: the picture bulges in and out (울렁울렁)")]
        public float wobbleIntensity = 0.3f;
        [Tooltip("In-out swings per second")] public float wobbleFrequency = 4f;
        [Tooltip("The swing fades with this time constant (s)")] public float wobbleDecay = 0.45f;
        public Color damageTint = new Color(1f, 0.08f, 0.32f);
        public float damageDecay = 0.35f;

        float _trauma, _flash, _wobEnv, _wobT, _damage, _stopUntil = -1f, _seed;
        Volume _flashVol, _dmgVol;
        LensDistortion _lens;
        Camera _cam;
        Vector3 _offPos, _savedPos;
        Quaternion _offRot = Quaternion.identity, _savedRot;
        bool _offsetOn;

        void OnEnable()
        {
            Instance = this;
            _seed = Random.value * 100f;
            _cam = GetComponent<Camera>();
            if (!Application.isPlaying) return;
            EnsureVolumes();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestorePose();
            if (Instance == this) Instance = null;
            if (_stopUntil > 0f) { Time.timeScale = 1f; _stopUntil = -1f; }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != _cam || _offsetOn || (_offPos == Vector3.zero && _offRot == Quaternion.identity)) return;
            _savedPos = transform.position; _savedRot = transform.rotation;
            transform.SetPositionAndRotation(_savedPos + _savedRot * _offPos, _savedRot * _offRot);
            _offsetOn = true;
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == _cam) RestorePose();
        }

        void RestorePose()
        {
            if (!_offsetOn) return;
            transform.SetPositionAndRotation(_savedPos, _savedRot);
            _offsetOn = false;
        }

        /// Creates the two override volumes (play mode does this on enable; edit-mode captures call it by hand).
        public void EnsureVolumes()
        {
            if (_flashVol) return;
            _flashVol = MakeVolume("HitScreenFX wobble", p =>
            {
                _lens = p.Add<LensDistortion>(true);
                _lens.intensity.Override(0f);
                _lens.scale.Override(1f);
            });
            _dmgVol = MakeVolume("HitScreenFX damage", p =>
            {
                var v = p.Add<Vignette>(true); v.color.Override(damageTint); v.intensity.Override(0.5f); v.smoothness.Override(0.6f);
                var c = p.Add<ChromaticAberration>(true); c.intensity.Override(1f);
            });
        }

        Volume MakeVolume(string n, System.Action<VolumeProfile> fill)
        {
            var go = new GameObject(n) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true; v.priority = 200f; v.weight = 0f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.hideFlags = HideFlags.DontSave;
            fill(p);
            v.sharedProfile = p;
            return v;
        }

        /// World-space impact: shake scaled by distance to the camera, optional hit-stop and flash (= wobble) when close.
        public static void Impulse(Vector3 at, float amount, float range, float hitStop, float flash = 0f)
        {
            var i = Instance;
            if (!i || amount <= 0f) return;
            float d = Vector3.Distance(i.transform.position, at);
            float k = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, range));
            if (k <= 0f) return;
            i._trauma = Mathf.Min(1f, i._trauma + amount * k);
            i.AddFlash(flash * k);
            if (hitStop > 0f && k > 0.5f) i.HitStop(hitStop * k);
        }

        public void AddTrauma(float amount) { _trauma = Mathf.Min(1f, _trauma + amount); }
        /// A big-blast "flash" request (0..1). Wobbles the picture since 11-19, no white-out.
        public void AddFlash(float amount) { _flash = Mathf.Max(_flash, amount); }
        public void AddDamage(float amount) { _damage = Mathf.Clamp01(_damage + amount); }

        /// The player took damage.
        public static void PlayerHit(float amount)
        {
            var i = Instance; if (!i) return;
            i._damage = Mathf.Clamp01(i._damage + amount);
            i._trauma = Mathf.Min(1f, i._trauma + amount * 0.5f);
        }

        public void HitStop(float seconds)
        {
            if (!Application.isPlaying) return;
            _stopUntil = Mathf.Max(_stopUntil, Time.unscaledTime + seconds);
            Time.timeScale = hitStopTimeScale;
        }

        void LateUpdate()
        {
            if (_stopUntil > 0f && Time.unscaledTime >= _stopUntil) { Time.timeScale = 1f; _stopUntil = -1f; }
            Step(Time.unscaledDeltaTime, Time.unscaledTime);
        }

        /// Advance shake / wobble / damage. Play mode calls it from LateUpdate; edit-mode captures call it by hand after
        /// posing the camera (there the offset is written straight onto the transform).
        public void Step(float dt, float time)
        {
            _trauma = Mathf.Max(0f, _trauma - recovery * dt);
            _damage *= Mathf.Exp(-dt / Mathf.Max(1e-3f, damageDecay));
            if (_dmgVol) _dmgVol.weight = _damage;

            // wobble: the request fades (wobbleDecay), the envelope follows it with a ~35 ms attack so a new kick never
            // pops, and the swing keeps one running phase while anything is left (a fresh one starts at 0 = no jump)
            _flash *= Mathf.Exp(-dt / Mathf.Max(1e-3f, wobbleDecay));
            _wobEnv = Mathf.Lerp(_wobEnv, _flash, 1f - Mathf.Exp(-dt / 0.035f));
            if (_flash < 1e-3f && _wobEnv < 1e-3f) { _flash = 0f; _wobEnv = 0f; _wobT = 0f; }
            else _wobT += dt;
            float w = wobbleIntensity * Mathf.Clamp01(_wobEnv) * Mathf.Sin(_wobT * wobbleFrequency * 2f * Mathf.PI);
            if (_lens)
            {
                _lens.intensity.value = Mathf.Clamp(w, -1f, 1f);
                // positive (pincushion) samples past the screen edges: zoom in a hair while it lasts
                _lens.scale.value = 1f + Mathf.Max(0f, w) * 0.25f;
            }
            if (_flashVol) _flashVol.weight = _wobEnv > 1e-3f ? 1f : 0f;

            float s = _trauma * _trauma;
            if (s < 1e-5f) { _offPos = Vector3.zero; _offRot = Quaternion.identity; return; }
            float t = time * frequency;
            float N(float o) => Mathf.PerlinNoise(_seed + o, t) * 2f - 1f;
            _offRot = Quaternion.Euler(N(0f) * maxAngle * s, N(10f) * maxAngle * s, N(20f) * maxAngle * 0.5f * s);
            _offPos = new Vector3(N(30f), N(40f), 0f) * maxOffset * s;     // camera-local
            if (Application.isPlaying) return;
            transform.SetPositionAndRotation(transform.position + transform.rotation * _offPos, transform.rotation * _offRot);
        }
    }
}
