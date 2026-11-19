using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Cine
{
    /// Game-start cutscene of the debris field: the two pilots streak in from deep space behind the spawn, flip over and
    /// burn their boosters to brake (AC6-style ignition flash), slam onto the wreck slab where they spawn (the "snap":
    /// fast arrival, a dip, hard hold) and the booster wash throws a ring of smoke that rolls out over the deck
    /// ("퐈아아아"). The camera circles them in the smoke, they talk (Resources/OYM_Cine/arrival_dialogue.json, over the
    /// shoulder shots), and the camera blends back into the gameplay camera behind the pilot picked on the title.
    ///
    /// Installs itself: when Showcase_DebrisField (ShowcaseScene.Name) loads with a PlayTestDirector, an
    /// ArrivalCutscene object is added — no scene edits, so scene rebuilds keep it. It plays on its own when the game is
    /// entered from the title (GAME START); a scene opened straight in the editor waits for F8; runs with any other
    /// -oym* automation flag (other recorders) and batch mode get nothing. ArrivalCutscene.PlayOnGameStart = false turns
    /// the automatic start off. Hold Space / Enter / Esc to skip, click / Space for the next line, F8 (re)plays it.
    /// -oymCineDemo records a take to Screenshots/cine/arrival/ and quits the editor.
    [DefaultExecutionOrder(-40)]
    public class ArrivalCutscene : MonoBehaviour
    {
        public static bool PlayOnGameStart = true;
        public static ArrivalCutscene Current { get; private set; }
        /// raised when control goes back to the player (also after a skip)
        public static event System.Action Finished;
        /// co-op (OYM.Net): the lines run on their own on both screens (no click-to-advance) and only the host may hold
        /// to skip; SkipStarted tells the host's session, RequestSkip() skips the guest's copy
        public static bool DialogueInput = true, SkipInput = true;
        public static event System.Action SkipStarted;
        public bool Running => _running;

        public PlayTestDirector director;
        public string dialogue = "arrival_dialogue";
        public bool record;
        [Tooltip("start on its own (entered from the title / -oymCineDemo); otherwise F8 plays it")]
        public bool autoPlay = true;

        [Header("Timing (s)")]
        public float flipTime = 3.55f;
        public float burnTime = 0.95f;
        public float stagger = 0.32f;
        public float dialogueAt = 9.4f;
        public float skipHold = 0.7f;

        [Header("Approach (m, from each landing spot)")]
        public float startBack = 135f, startUp = 58f, startSide = 20f;
        public float flipBack = 8f, flipUp = 7.5f;
        public float slamSpeed = 6f;

        sealed class Pilot
        {
            public string name;
            public ZeroGFlight flight;
            public CineActor actor;
            public Vector3 land, ground, normal;
            public float yaw, side, flip, touch;
            public Color tint;
            public bool ignited, flipped, landed, settled;
        }

        readonly List<Pilot> _p = new List<Pilot>();
        FlightCamera _fc;
        CineCamera _cam;
        CineOverlay _ui;
        LandingSmoke _smoke;
        AudioSource _voice;
        Transform _mid;
        Vector3 _C, _F, _R, _U = Vector3.up, _ahead;
        float _t, _hold;
        bool _running, _skipping, _ready;
        Coroutine _run;

        static readonly Color Kestrel = new Color(0.35f, 0.85f, 1f), Orbit = new Color(1f, 0.68f, 0.22f);

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; Finished = null; PlayOnGameStart = true; DialogueInput = true; SkipInput = true; SkipStarted = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// any other OYM automation flag (-oymDemo, -oymEventDemo, -oymTitleDemo, other sessions' recorders...)
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && a != "-oymCineDemo") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            bool demo = Flag("-oymCineDemo");
            if (Application.isBatchMode || s.name != ShowcaseScene.Name) return;
            // plays when the game is entered from the title (GAME START); a scene opened straight in the editor and
            // other recorders' runs stay untouched (F8 still plays it there)
            bool fromTitle = PlayOnGameStart && !string.IsNullOrEmpty(ShowcaseSession.SelectedPilot) && !OtherAutomation();
            if (!demo && !fromTitle && OtherAutomation()) return;
            PlayTestDirector d = null;
            foreach (var root in s.GetRootGameObjects()) { d = root.GetComponentInChildren<PlayTestDirector>(true); if (d) break; }
            if (!d || d.controllers == null || d.controllers.Length == 0) return;
            var go = new GameObject("ArrivalCutscene");
            SceneManager.MoveGameObjectToScene(go, s);
            var c = go.AddComponent<ArrivalCutscene>();
            c.director = d; c.record = demo; c.autoPlay = demo || fromTitle;
            if (c.autoPlay) { c._ui.Fade(1f, 0f); c._ui.Letterbox(true, 0f); }    // black from the very first frame
        }

        void Awake()
        {
            Current = this;
            _ui = CineOverlay.Create(transform);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Start()
        {
            if (!autoPlay) return;                 // F8 plays it
            Setup();
            if (record) StartCoroutine(Record());
            Begin();
        }

        // ------------------------------------------------------------------------------------------------ setup
        void Setup()
        {
            if (_ready) return;
            _fc = director.flightCamera ? director.flightCamera : FlightCamera.Main;
            var cam = _fc ? _fc.Cam : Camera.main;
            if (!cam && _fc) cam = _fc.GetComponent<Camera>();
            _cam = CineCamera.Ensure(cam);
            _smoke = LandingSmoke.Ensure();
            _smoke.transform.SetParent(transform, false);
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false; _voice.spatialBlend = 0f; _voice.volume = 1f;

            var ctl = director.controllers;
            for (int i = 0; i < ctl.Length; i++)
            {
                var f = ctl[i];
                if (!f) continue;
                string n = director.names != null && i < director.names.Length ? director.names[i] : f.name;
                var p = new Pilot { name = n, flight = f, land = f.transform.position, yaw = f.transform.eulerAngles.y };
                p.tint = n.Contains("KESTREL") ? Kestrel : n.Contains("ORBIT") ? Orbit : Color.white;
                p.actor = CineActor.For(f, n);
                p.actor.accent = p.tint;
                _p.Add(p);
            }
            // land KESTREL first (lighter), ORBIT FRAME second with the bigger burst
            _p.Sort((a, b) => (a.name.Contains("KESTREL") ? 0 : 1).CompareTo(b.name.Contains("KESTREL") ? 0 : 1));

            _C = Vector3.zero;
            foreach (var p in _p) _C += p.land;
            _C /= Mathf.Max(1, _p.Count);
            Vector3 fwd = cam ? cam.transform.forward : _p[0].flight.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            _F = fwd.normalized; _R = Vector3.Cross(_U, _F);
            _ahead = _C + _F * 40f + _U * 3f;

            for (int i = 0; i < _p.Count; i++)
            {
                var p = _p[i];
                float s = Vector3.Dot(p.land - _C, _R);
                p.side = Mathf.Abs(s) > 0.05f ? Mathf.Sign(s) : (i == 0 ? -1f : 1f);
                p.flip = flipTime + stagger * i;
                p.touch = p.flip + burnTime;
                Deck(p.land + _U * 2.5f, out p.ground, out p.normal);
            }
            _mid = new GameObject("cine pair mid").transform;
            _mid.SetParent(transform, false);
            _ready = true;
        }

        bool IsPilot(Transform t)
        {
            foreach (var p in _p) if (t.IsChildOf(p.flight.transform)) return true;
            return false;
        }

        /// Deck point + normal below `from` (pilots ignored); falls back to 2.5 m down / world up.
        bool Deck(Vector3 from, out Vector3 point, out Vector3 normal)
        {
            point = from - _U * 2.5f; normal = _U;
            var hits = Physics.RaycastAll(from, -_U, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            foreach (var h in hits)
            {
                if (IsPilot(h.transform) || h.distance >= best) continue;
                best = h.distance; point = h.point; normal = h.normal; ok = true;
            }
            return ok;
        }

        /// A point `h` m above the deck at the horizontal position of p (camera placement).
        Vector3 OnDeck(Vector3 p, float h)
        {
            Deck(new Vector3(p.x, _C.y + 3f, p.z), out var g, out _);
            return new Vector3(p.x, Mathf.Max(g.y, _C.y - 3f) + h, p.z);
        }

        // ------------------------------------------------------------------------------------------------ run
        public void Begin()
        {
            if (_running) return;
            Setup();
            _running = true; _skipping = false; _t = 0f; _hold = 0f;
            if (director) director.enabled = false;            // no play-test HUD text, no Tab / F9 during the scene
            if (_fc) _fc.scripted = true;
            foreach (var p in _p)
            {
                p.ignited = p.flipped = p.landed = p.settled = false;
                p.actor.Begin();
                p.actor.Play("DashLoop|Dash|Hover", 0f);
                if (p.actor.jetpack) p.actor.jetpack.SetThrust(0.7f);
            }
            Pose(0f);
            _run = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            _ui.Fade(1f, 0f);
            _ui.Letterbox(true, 0f);
            yield return null;
            _ui.Fade(0f, 1.3f);
            var A = _p[0];
            var B = _p.Count > 1 ? _p[1] : _p[0];
            Note("start");

            // S1 deck, looking back and up at two booster stars closing in
            _cam.Play(CineShot.Fixed(OnDeck(_C + _F * 6f - _R * 2.2f, 0.75f), _mid, 34f).Look(new Vector3(0f, 1f, 0f)).Lag(0f, 2.5f).Handheld(0.15f).Named("deck_lookup"));
            while (_t < 2.25f) { yield return Tick(); }

            // S2 chase ORBIT FRAME as they dive at the slab
            _cam.Play(CineShot.Follow(B.flight.transform, new Vector3(-1.7f * B.side, 1.35f, -5.2f), FollowSpace.Velocity, 68f)
                .Look(new Vector3(0f, 0.4f, 9f)).Lag(0f, 0f).Handheld(0.45f).Blur(0.85f).Named("chase"));
            _cam.Shake(0.18f);
            Note("chase");
            while (_t < flipTime - 0.05f) { yield return Tick(); }

            // S3 low on the deck: flip, burn, slam, smoke rolls at the lens
            Vector3 l0 = OnDeck(_C + _R * 6.8f + _F * 3.8f, 0.45f), l1 = OnDeck(_C + _R * 5.7f + _F * 3.0f, 0.55f);
            _cam.Play(CineShot.Rail(l0, l1, 4f, _mid, 46f).Look(new Vector3(0f, 1.5f, 0f)).Zoom(38f, 4f).Lag(0f, 4f).Handheld(0.3f).Named("landing_low"));
            Note("landing");
            while (_t < 7.0f) { yield return Tick(); }

            // S4 slow orbit in the smoke, they straighten up and turn to each other
            float baseYaw = _mid.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 sun = SunDir(); sun.y = 0f;
            float best = 150f, bestK = -9f;
            foreach (float a in new[] { 150f, 210f })
            {
                float r = baseYaw + a * Mathf.Deg2Rad;
                var camDir = -new Vector3(-Mathf.Sin(r), 0f, -Mathf.Cos(r));        // camera looks back at the subject
                float k = sun.sqrMagnitude > 1e-4f ? Vector3.Dot(camDir, sun.normalized) : 0f;
                if (k > bestK) { bestK = k; best = a; }
            }
            _cam.Play(CineShot.Orbit(_mid, 5.6f, 1.2f, best, best > 180f ? -8f : 8f, 38f).Look(new Vector3(0f, 1.45f, 0f)).Lag(2f, 4f).Dof(0.45f).Handheld(0.2f).Named("smoke_orbit"), 0f);
            Note("orbit");
            while (_t < 7.8f) { yield return Tick(); }
            if (_p.Count > 1) { A.actor.LookAt(B.actor.Head, 1f); B.actor.LookAt(A.actor.Head, 0.8f); }
            while (_t < dialogueAt) { yield return Tick(); }

            // dialogue
            if (_p.Count > 1)
            {
                var script = DialogueScript.Load(dialogue);
                var dlg = new CineDialogue
                {
                    cam = _cam, overlay = _ui, voice = _voice, aheadPoint = _ahead,
                    actor = n => Find(n)?.actor,
                    accent = n => Find(n)?.tint ?? Color.white,
                    abort = () => _skipping,
                    allowInput = !record && DialogueInput,
                };
                Note("dialogue");
                var it = dlg.Play(script);
                while (it.MoveNext()) { yield return Tick(); }
            }
            Note("outro");
            yield return Outro();
        }

        Pilot Find(string n)
        {
            foreach (var p in _p) if (p.name == n) return p;
            foreach (var p in _p) if (!string.IsNullOrEmpty(n) && p.name.Contains(n)) return p;
            return null;
        }

        /// One frame of the scripted part (pilots, beats), then yield.
        object Tick()
        {
            _t += Time.deltaTime;
            Pose(_t);
            return null;
        }

        IEnumerator Outro()
        {
            foreach (var p in _p) p.actor.LookAt(_ahead, 0.8f);
            float w = 0f;
            while (w < 0.5f) { w += Time.deltaTime; yield return Tick(); }
            HandBack(1.8f);
            _ui.Letterbox(false, 1.1f);
            w = 0f;
            while (w < 2.2f) { w += GameFeel.RealDt; yield return null; }
            _running = false;
            Note("done");
        }

        /// Pilots back to the controllers, camera blends into the gameplay camera (behind the active pilot).
        void HandBack(float blend)
        {
            foreach (var p in _p)
            {
                p.actor.SetBody(p.land, p.yaw, Vector2.zero);
                p.actor.End();
                if (p.actor.jetpack) p.actor.jetpack.SetThrust(p.flight.idleThrust);
            }
            if (_fc)
            {
                _fc.SetLook(Mathf.Atan2(_F.x, _F.z) * Mathf.Rad2Deg, 7f);
                _fc.SnapToTarget();
                _fc.scripted = record;                       // recordings: keep the mouse out of it
            }
            if (director) director.enabled = true;
            _cam.Release(blend);
            _ui.ClearLine();
            Finished?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------ the pilots
        static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float s)
        {
            float s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * m1;
        }

        static float Smooth(float a, float b, float x) { float k = Mathf.Clamp01((x - a) / (b - a)); return k * k * (3f - 2f * k); }

        /// Root position of pilot p at time t (and its velocity by finite difference).
        Vector3 PathAt(Pilot p, float t)
        {
            Vector3 P0 = p.land - _F * startBack + _U * startUp + _R * (p.side * startSide);
            Vector3 P1 = p.land - _F * flipBack + _U * flipUp + _R * (p.side * 1f);
            Vector3 P2 = p.land;
            float T1 = p.flip, T2 = p.touch - p.flip;
            Vector3 d1 = (P2 - P1).normalized;
            Vector3 v1 = Vector3.Lerp((P1 - P0).normalized, d1, 0.55f).normalized * (2.05f * (P2 - P1).magnitude / T2);
            if (t <= T1)
            {
                Vector3 v0 = (P1 - P0) / T1 * 1.3f;
                return Hermite(P0, v0 * T1, P1, v1 * T1, Mathf.Max(0f, t) / T1);
            }
            if (t <= p.touch)
                return Hermite(P1, v1 * T2, P2, -_U * slamSpeed * T2, (t - T1) / T2);
            // the snap: a dip on impact, a small rebound, hold
            float u = t - p.touch;
            float dip = -0.34f * Mathf.Sin(15f * u) * Mathf.Exp(-6.5f * u);
            return P2 + _U * dip;
        }

        void Pose(float t)
        {
            Vector3 mid = Vector3.zero;
            foreach (var p in _p)
            {
                Vector3 pos = PathAt(p, t);
                Vector3 vel = (PathAt(p, t + 0.02f) - PathAt(p, t - 0.02f)) / 0.04f;
                if (t > p.touch) vel = Vector3.zero;

                // yaw along the flight, onto the spawn heading through the burn
                Vector3 hv = vel; hv.y = 0f;
                float flyYaw = hv.sqrMagnitude > 1f ? Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg : p.yaw;
                float yaw = Mathf.LerpAngle(flyYaw, p.yaw, Smooth(p.flip - 0.2f, p.touch, t));

                // pitch: superman dive -> flip to feet-first with a back lean (overshoot) -> landing settle (the snap)
                float below = vel.sqrMagnitude > 1f ? Mathf.Asin(Mathf.Clamp(-vel.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
                float dive = 70f + 0.4f * below;
                float pitch, roll;
                if (t < p.flip - 0.12f) { pitch = dive; roll = -p.side * 12f * Smooth(0f, 1.5f, t); }
                else if (t < p.touch)
                {
                    float k = Mathf.Clamp01((t - (p.flip - 0.12f)) / 0.42f);
                    float back = 1f + 2.4f * Mathf.Pow(k - 1f, 3f) + 1.4f * Mathf.Pow(k - 1f, 2f);   // ease-out-back
                    pitch = Mathf.LerpUnclamped(dive, -18f, back);
                    roll = -p.side * 12f * (1f - k);
                }
                else
                {
                    float u = t - p.touch;
                    pitch = -18f * Mathf.Exp(-6f * u) * Mathf.Cos(10f * u) + 9f * Mathf.Sin(15f * u) * Mathf.Exp(-6.5f * u);
                    roll = 0f;
                }
                p.actor.SetBody(pos, yaw, new Vector2(pitch, roll));
                mid += pos;

                Beats(p, t, vel);
            }
            mid /= Mathf.Max(1, _p.Count);
            _mid.SetPositionAndRotation(mid, Quaternion.LookRotation(_F, _U));
        }

        void Beats(Pilot p, float t, Vector3 vel)
        {
            var jet = p.actor.jetpack;
            if (!p.ignited && t >= 0.15f + 0.2f * (p == _p[0] ? 0 : 1))
            {
                p.ignited = true;
                if (jet) { jet.Ignite(); jet.SetThrust(0.75f); }
            }
            if (!p.flipped && t >= p.flip - 0.08f)
            {
                p.flipped = true;
                p.actor.Play("DashStop|Hover", 0.12f);
                if (jet) { jet.Ignite(); jet.SetThrust(1f); }              // retro burn: the AC6 flash
                _cam.Flash(0.35f);
                _cam.Shake(0.12f);
            }
            if (!p.landed && t >= p.touch)
            {
                p.landed = true;
                bool heavy = p.name.Contains("ORBIT");
                float s = heavy ? 1.25f : 0.85f;
                if (jet) jet.Cutoff();
                _smoke.Burst(p.ground, p.normal, s, p.tint, _F * 18f - _U * slamSpeed, p.name.GetHashCode());
                _cam.Shake(heavy ? 0.55f : 0.35f);
                _cam.Punch(heavy ? -60f : -35f);
                _cam.Flash(heavy ? 0.6f : 0.4f);
                if (heavy) GameFeel.HitStop(0.07f, 0.05f);
                Note("touchdown " + p.name);
            }
            if (p.landed && !p.settled && t >= p.touch + 0.55f)
            {
                p.settled = true;
                p.actor.Play("Hover|Idle", 0.7f);
                if (jet) jet.SetThrust(p.flight.idleThrust);
            }
        }

        static Vector3 SunDir()
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled && (!sun || l.intensity > sun.intensity)) sun = l;
            return sun ? -sun.transform.forward : Vector3.zero;
        }

        // ------------------------------------------------------------------------------------------------ skip / replay
        void Update()
        {
            bool hold = false, replay = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null) { hold = k.spaceKey.isPressed || k.enterKey.isPressed || k.escapeKey.isPressed; replay = k.f8Key.wasPressedThisFrame; }
#else
            hold = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Escape);
            replay = Input.GetKeyDown(KeyCode.F8);
#endif
            if (_running && !_skipping && !record && SkipInput)
            {
                _hold = hold ? _hold + GameFeel.RealDt : 0f;
                _ui.SetSkip(_hold > 0.15f ? Mathf.InverseLerp(0.15f, skipHold, _hold) : 0f);
                if (_hold >= skipHold) StartCoroutine(Skip());
            }
            else if (_ui) _ui.SetSkip(0f);
            if (!_running && replay) Replay();
        }

        /// Skip now (co-op: the host skipped).
        public void RequestSkip() { if (_running && !_skipping) StartCoroutine(Skip()); }

        IEnumerator Skip()
        {
            _skipping = true;
            SkipStarted?.Invoke();
            _ui.SetSkip(0f);
            _ui.Fade(1f, 0.2f);
            float w = 0f;
            while (w < 0.22f) { w += GameFeel.RealDt; yield return null; }
            if (_run != null) StopCoroutine(_run);
            _voice.Stop();
            foreach (var p in _p) p.landed = p.flipped = p.ignited = p.settled = true;
            HandBack(0f);
            _ui.Letterbox(false, 0f);
            _ui.Fade(0f, 0.45f);
            _running = false;
            _skipping = false;
            Note("skipped");
        }

        /// Fly them in again (F8 in play mode).
        public void Replay()
        {
            if (_running) return;
            Setup();
            foreach (var p in _p) { p.flight.ResetMotion(); }
            Begin();
        }

        // ------------------------------------------------------------------------------------------------ recording (-oymCineDemo)
        readonly List<string> _notes = new List<string>();
        int _frame;

        void Note(string what)
        {
            string s = $"t={_t:F2} frame={_frame} {what}";
            _notes.Add(s);
            Debug.Log("[OYM-CINE] " + s);
        }

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/cine/arrival"));

        IEnumerator Record()
        {
            const int W = 1280, H = 720, Fps = 25;
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "frame_*.jpg")) File.Delete(f);
            Time.captureFramerate = Fps;
            var cam = _cam.Cam;
            var camRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); camRT.Create();
            var uiRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); uiRT.Create();
            cam.targetTexture = camRT;
            _ui.SetTarget(uiRT);
            var a = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var b = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var o = new Texture2D(W, H, TextureFormat.RGB24, false);
            var outPx = new Color32[W * H];
            float tail = 0f;
            int max = Fps * 70, lastFc = -1;
            Application.runInBackground = true;                   // an unfocused editor must keep playing
            while (_frame < max)
            {
                yield return new WaitForEndOfFrame();
                // the editor can repaint the game view without a player-loop step: one picture per game frame only
                if (Time.frameCount == lastFc) continue;
                lastFc = Time.frameCount;
                var prev = RenderTexture.active;
                RenderTexture.active = camRT; a.ReadPixels(new Rect(0, 0, W, H), 0, 0); a.Apply(false);
                RenderTexture.active = uiRT; b.ReadPixels(new Rect(0, 0, W, H), 0, 0); b.Apply(false);
                RenderTexture.active = prev;
                var ca = a.GetPixels32(); var cb = b.GetPixels32();
                for (int i = 0; i < outPx.Length; i++)
                {
                    // the panel draws premultiplied: out = ui + cam * (1 - a)
                    int ia = 255 - cb[i].a;
                    outPx[i] = new Color32((byte)Mathf.Min(255, cb[i].r + ca[i].r * ia / 255), (byte)Mathf.Min(255, cb[i].g + ca[i].g * ia / 255), (byte)Mathf.Min(255, cb[i].b + ca[i].b * ia / 255), 255);
                }
                o.SetPixels32(outPx); o.Apply(false);
                File.WriteAllBytes(Path.Combine(OutDir, $"frame_{_frame:0000}.jpg"), o.EncodeToJPG(92));
                _frame++;
                if (!_running) { tail += 1f / Fps; if (tail > 1.6f) break; }
            }
            File.WriteAllLines(Path.Combine(OutDir, "beats.txt"), _notes);
            cam.targetTexture = null;
            Time.captureFramerate = 0;
            Debug.Log($"[OYM-CINE] recorded {_frame} frames -> {OutDir}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }
    }
}
