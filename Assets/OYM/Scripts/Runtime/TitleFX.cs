using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// Main-title presentation on top of TitleDirector's camera (applied in LateUpdate, after the director has placed the
    /// camera for the frame, so nothing accumulates) and of the pilots' TitleCharacter events:
    ///   opening - the camera pushes in from deep space while the fade clears
    ///   boost   - a pilot boosts to the front: whip toward it (yaw / roll punch), FOV opens and snaps back, motion
    ///             blur, bloom, chromatic aberration and a lens-distortion pulse (AC6-style HDR ignition)
    ///   strike  - KESTREL's blade locks into its hold / ORBIT FRAME's RG-77 fires: hit-stop freeze frame, impact
    ///             shake, FOV punch-in, bloom flash (the "snap")
    ///   retreat - the pilot pushed back: a small counter-roll
    ///   launch  - DEPLOY: the big one (FOV warp, blur, bloom, distortion) before the fade
    /// Post effects live on a private global volume (priority 70) whose values are re-written every frame; camera
    /// offsets are damped springs driven by impulses and timed in real seconds (they keep moving through the hit-stop).
    [DefaultExecutionOrder(1000)]
    public class TitleFX : MonoBehaviour
    {
        public TitleDirector director;
        public Camera cam;

        [Header("Opening")]
        public float openingDistance = 3.2f, openingRise = 0.6f, openingTime = 2.2f;

        [Header("Boost")]
        public float boostYaw = 3.2f, boostRoll = 4.5f, boostPitch = 1.2f, boostFov = 6f;
        public float boostBlur = 0.85f, boostBloom = 0.55f, boostChroma = 0.45f, boostLens = 0.16f;

        [Header("Strike")]
        public float strikeStop = 0.08f, strikeStopScale = 0.03f;
        public float strikeShake = 0.028f, strikeShakeDeg = 1.0f, strikeFov = -3.5f;
        public float strikeBloom = 0.75f, strikeChroma = 0.65f;

        [Header("Launch")]
        public float launchFov = 16f, launchLens = 0.35f, launchBloom = 0.9f;

        // MARK: calm-pace
        [Header("Pace")]
        [Tooltip("scales the boost / push-back camera whip, FOV, blur and lens pulses (strike and launch unchanged). "
               + "1 = the original AC6 whip; user 11-19 asked for a calmer title")]
        [Range(0f, 1f)] public float boostPace = 0.6f;

        [Header("Springs")]
        public float freq = 3.0f, damping = 0.42f;

        Volume _vol;
        VolumeProfile _profile;
        Bloom _bloom; ChromaticAberration _chroma; LensDistortion _lens; MotionBlur _blur;
        float _baseBloom = 0.25f, _baseChroma = 0.06f, _baseBlur = 0.2f;
        float _pBloom, _pChroma, _pLens, _pBlur, _shake, _open = 1f, _seed;
        Vector3 _rot, _rotV;       // pitch, yaw, roll offset (deg)
        float _fov, _fovV;
        TitleCharacter[] _hooked;

        void Start()
        {
            if (!director) director = FindFirstObjectByType<TitleDirector>();
            if (!cam) cam = director && director.cam ? director.cam : Camera.main;
            ReadBase();
            BuildVolume();
            _seed = Random.value * 100f;
            if (director && director.pilots != null)
            {
                _hooked = director.pilots;
                foreach (var p in _hooked) if (p) { p.SlotChanged += OnSlot; p.Struck += OnStrike; }
            }
        }

        void OnDestroy()
        {
            if (_hooked != null) foreach (var p in _hooked) if (p) { p.SlotChanged -= OnSlot; p.Struck -= OnStrike; }
            if (_profile) Destroy(_profile);
        }

        // the look these pulses sit on: the strongest global volume that sets each effect (the space look)
        void ReadBase()
        {
            float pb = -1e9f, pc = -1e9f, pm = -1e9f;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || !v.sharedProfile) continue;
                var pr = v.sharedProfile;
                if (pr.TryGet(out Bloom b) && b.intensity.overrideState && v.priority > pb) { pb = v.priority; _baseBloom = b.intensity.value; }
                if (pr.TryGet(out ChromaticAberration c) && c.intensity.overrideState && v.priority > pc) { pc = v.priority; _baseChroma = c.intensity.value; }
                if (pr.TryGet(out MotionBlur m) && m.intensity.overrideState && v.priority > pm) { pm = v.priority; _baseBlur = m.intensity.value; }
            }
        }

        void BuildVolume()
        {
            var go = new GameObject("Title FX Volume");
            go.transform.SetParent(transform, false);
            _vol = go.AddComponent<Volume>();
            _vol.isGlobal = true; _vol.priority = 70f; _vol.weight = 1f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "TitleFX (runtime)";
            _bloom = _profile.Add<Bloom>(false); _bloom.intensity.overrideState = true;
            _chroma = _profile.Add<ChromaticAberration>(false); _chroma.intensity.overrideState = true;
            _lens = _profile.Add<LensDistortion>(false); _lens.intensity.overrideState = true;
            _blur = _profile.Add<MotionBlur>(false); _blur.intensity.overrideState = true;
            _vol.sharedProfile = _profile;
            ApplyVolume();
        }

        // ------------------------------------------------------------------------------------------------ events
        float SideOf(TitleCharacter p)
        {
            if (!cam) return 1f;
            float x = Vector3.Dot(p.front.pos - cam.transform.position, cam.transform.right);
            return x >= 0f ? 1f : -1f;
        }

        void OnSlot(TitleCharacter p, TitleCharacter.Slot s)
        {
            float w = 2f * Mathf.PI * freq;                                   // impulses in deg/s scaled by the spring
            if (s == TitleCharacter.Slot.Front)
            {
                float side = SideOf(p), k = boostPace;
                _rotV += new Vector3(-boostPitch, side * boostYaw, -side * boostRoll) * (w * k);
                _fovV += boostFov * w * k;
                _pBlur = k; _pLens = k;
                _pBloom = Mathf.Max(_pBloom, k); _pChroma = Mathf.Max(_pChroma, k);
            }
            else if (s == TitleCharacter.Slot.Back)
            {
                _rotV += new Vector3(0.4f, 0f, SideOf(p) * 1.5f) * (w * boostPace);
            }
            else if (s == TitleCharacter.Slot.Launch)
            {
                _fovV += launchFov * w;
                _rotV += new Vector3(-3f, 0f, 2f) * w;
                _pBlur = 1.4f; _pLens = launchLens / Mathf.Max(boostLens, 1e-3f);
                _pBloom = launchBloom / Mathf.Max(boostBloom, 1e-3f); _pChroma = 1.2f; _shake = 0.7f;
            }
        }

        void OnStrike(TitleCharacter p)
        {
            GameFeel.HitStop(strikeStop, strikeStopScale);
            float w = 2f * Mathf.PI * freq;
            _shake = 1f;
            _fovV += strikeFov * w;
            _rotV += new Vector3(0.8f, 0f, -SideOf(p) * 1.2f) * w;
            _pBloom = Mathf.Max(_pBloom, strikeBloom / Mathf.Max(boostBloom, 1e-3f));
            _pChroma = Mathf.Max(_pChroma, strikeChroma / Mathf.Max(boostChroma, 1e-3f));
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);
            if (dt <= 0f || !cam) return;

            // springs (substepped: stiff + light damping = punchy overshoot)
            float w = 2f * Mathf.PI * freq, z = damping;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.005f)); float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                _rotV += (-w * w * _rot - 2f * z * w * _rotV) * h; _rot += _rotV * h;
                _fovV += (-w * w * _fov - 2f * z * w * _fovV) * h; _fov += _fovV * h;
            }
            _pBloom *= Mathf.Exp(-dt / 0.22f);
            _pChroma *= Mathf.Exp(-dt / 0.18f);
            _pLens *= Mathf.Exp(-dt / 0.16f);
            _pBlur *= Mathf.Exp(-dt / 0.25f);
            _shake *= Mathf.Exp(-dt / 0.13f);
            _open = Mathf.MoveTowards(_open, 0f, dt / Mathf.Max(openingTime, 0.01f));

            // camera: opening push-in, shake, rotation punch, FOV
            var t = cam.transform;
            float o = _open * _open * _open;                                   // ease-out cubic on the way in
            Vector3 local = new Vector3(0f, openingRise * o, -openingDistance * o);
            float s2 = _shake * _shake, tt = Time.unscaledTime * 38f;
            Vector3 shakeP = new Vector3(Noise(tt, 0f), Noise(tt, 1f), 0f) * (strikeShake * s2);
            Vector3 shakeR = new Vector3(Noise(tt, 2f), Noise(tt, 3f), Noise(tt, 4f) * 1.5f) * (strikeShakeDeg * s2);
            t.position += t.rotation * (local + shakeP);
            t.rotation = t.rotation * Quaternion.Euler(_rot + shakeR);
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + _fov + 6f * o, 10f, 100f);

            ApplyVolume();
        }

        float Noise(float t, float k) => Mathf.PerlinNoise(t + _seed, k * 7.31f) * 2f - 1f;

        void ApplyVolume()
        {
            if (!_profile) return;
            _bloom.intensity.value = Mathf.Clamp01(_baseBloom + boostBloom * _pBloom);
            _chroma.intensity.value = Mathf.Clamp01(_baseChroma + boostChroma * _pChroma);
            _lens.intensity.value = Mathf.Clamp(boostLens * _pLens, -1f, 1f);
            _blur.intensity.value = Mathf.Max(0f, _baseBlur + (1f - _baseBlur) * boostBlur * Mathf.Min(_pBlur, 1.4f));
        }
    }
}
