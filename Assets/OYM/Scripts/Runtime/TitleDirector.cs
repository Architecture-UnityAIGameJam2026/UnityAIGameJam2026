using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Main title. Opens on both pilots floating back to back; then the remembered pilot boosts to the camera (front
    /// right of the frame) and the menu comes in around it: logo top left, menu bottom left, pilot cards bottom right,
    /// Discord top right, co-op friend code on the right (TitleUI). A / D, Tab, a pilot card or a click on the pilot in
    /// the back swaps pilots (the other one is pushed back into the depth of field); W / S + ENTER drive the menu;
    /// GAME START opens the mode menu. SINGLE deploys into the OYM showcase (arrival cutscene, then the Showcase_DebrisField
    /// level); a matched friend pair (and the -bot launch flag) deploys into the co-op Arena. The camera drifts a little
    /// with the mouse. Music comes from BgmPlayer's scene routes.
    [DefaultExecutionOrder(-20)]
    public class TitleDirector : MonoBehaviour
    {
        [System.Serializable]
        public struct Shot
        {
            public Vector3 pos, look;
            public float fov;
            public Shot(Vector3 p, Vector3 l, float f) { pos = p; look = l; fov = f; }
        }

        public Camera cam;
        [Tooltip("0 = KESTREL, 1 = ORBIT FRAME")]
        public TitleCharacter[] pilots;
        public TitleUI ui;
        public Volume dofVolume;
        [Tooltip("opening shot: both pilots back to back")]
        public Shot neutralShot;
        [Tooltip("per pilot: that pilot in front, right third of the frame")]
        public Shot[] focusShots;

        [Header("Intro")]
        [Tooltip("seconds after the scene starts: the remembered pilot boosts to the front")]
        public float pilotIntro = 0.9f;
        [Tooltip("seconds after the scene starts: logo, menu and cards come in")]
        public float uiIntro = 1.3f;

        [Header("Camera")]
        public float camFreq = 0.75f, camDamping = 0.95f;
        public float handheld = 0.025f;
        [Tooltip("camera shift with the mouse position (m, right / up at the screen edge)")]
        public Vector2 mouseParallax = new Vector2(0.07f, 0.04f);
        [Tooltip("neutral: background starts to soften this far out (m)")]
        public float neutralFarStart = 60f, neutralFarEnd = 6000f;
        [Tooltip("focus: blur ramps from (subject + a) to (subject + b)")]
        public Vector2 focusFarRange = new Vector2(0.9f, 5.5f);

        [Header("Game")]
        public float deployDelay = 1.25f;

        public int Focus => _focus;
        public int Hover => _hover;
        public bool Deploying => _deploying;
        /// scripted demo: set the hover from outside and ignore the real mouse / keyboard
        [System.NonSerialized] public bool scripted;
        [System.NonSerialized] public int scriptedHover = -1;

        int _focus = -1, _hover = -1;
        bool _deploying, _introDone;
        Vector3 _camP, _camV, _lookP, _lookV;
        float _fov, _fovV, _farS, _farE, _farSV, _farEV, _kick;
        Vector2 _mouseN, _par;
        DepthOfField _dof;
        float _seed;

        void Start()
        {
            if (!cam) cam = Camera.main;
            if (dofVolume && dofVolume.profile && dofVolume.profile.TryGet(out DepthOfField d)) _dof = d;   // .profile = runtime copy
            foreach (var p in pilots) if (p) { p.lookTarget = cam ? cam.transform : null; p.Snap(TitleCharacter.Slot.Home); }
            SnapCamera(-1);
            _seed = Random.value * 50f;
            if (ui)
            {
                ui.Bind(this);
                ui.DeployClicked += Deploy;
                ui.HostClicked += () => OpenRoom(OnYourMark.TitleMatch.Mode.Host);
                ui.JoinClicked += code => OpenRoom(OnYourMark.TitleMatch.Mode.Join, code);
                ui.PilotClicked += SetFocus;
                ui.FadeIn(1.6f);
            }
            StartCoroutine(Intro());
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymTitleDemo") >= 0)
                new GameObject("TitleDemo").AddComponent<TitleDemo>();
        }

        IEnumerator Intro()
        {
            float t0 = Time.time;
            while (Time.time - t0 < pilotIntro) yield return null;
            if (_focus < 0 && !_deploying && pilots.Length > 0) SetFocus(Mathf.Clamp(GameSettings.TitlePilot, 0, pilots.Length - 1));
            while (Time.time - t0 < uiIntro) yield return null;
            if (ui && !_deploying) ui.PlayIntro();
            _introDone = true;
            if (!GameSession.LaunchArgsUsed)
            {
                GameSession.LaunchArgsUsed = true;
                string join = OnYourMark.LaunchArgs.Get("-join");
                if (join != null || OnYourMark.LaunchArgs.Has("-host") || OnYourMark.LaunchArgs.Has("-bot"))
                {
                    if (int.TryParse(OnYourMark.LaunchArgs.Get("-role"), out int role)) SetFocus(Mathf.Clamp(role, 0, 1));
                    OpenRoom(join != null ? OnYourMark.TitleMatch.Mode.Join : OnYourMark.LaunchArgs.Has("-bot") ?
                        OnYourMark.TitleMatch.Mode.Solo : OnYourMark.TitleMatch.Mode.Host, join == null ? null : OnYourMark.RoomCode.FromLaunchArgument(join));
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ input
        void Update()
        {
            float dt = Time.deltaTime;
            if (!scripted && !_deploying && Application.isFocused) HandleInput();
            if (scripted) _hover = scriptedHover;
            for (int i = 0; i < pilots.Length; i++) if (pilots[i]) pilots[i].Hovered = i == _hover && !_deploying;
            UpdateCamera(dt);
            UpdateDof(dt);
            if (ui) ui.Tick(cam, pilots, _focus, _deploying ? -1 : _hover, _deploying);
        }

        void HandleInput()
        {
            Vector2 mouse = default;
            bool click = false, left = false, right = false, up = false, down = false, tab = false, enter = false, esc = false, friend = false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; var k = Keyboard.current;
            if (m != null) { mouse = m.position.ReadValue(); click = m.leftButton.wasPressedThisFrame; }
            if (k != null)
            {
                friend = k.fKey.wasPressedThisFrame;
                left = k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame;
                right = k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame;
                up = k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame;
                down = k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame;
                tab = k.tabKey.wasPressedThisFrame;
                enter = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame;
                esc = k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame;

            }
#else
            friend = Input.GetKeyDown(KeyCode.F);
            mouse = Input.mousePosition; click = Input.GetMouseButtonDown(0);
            left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
            up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            tab = Input.GetKeyDown(KeyCode.Tab);
            enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
            esc = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace);

#endif
            if (Screen.width > 0 && Screen.height > 0)
                _mouseN = new Vector2(Mathf.Clamp(mouse.x / Screen.width * 2f - 1f, -1f, 1f), Mathf.Clamp(mouse.y / Screen.height * 2f - 1f, -1f, 1f));
            if (!_introDone) { _hover = -1; return; }

            bool overUi = ui && ui.IsOverInteractive(mouse);
            bool canPick = !ui || ui.AllowsPilotPick;
            _hover = overUi || !canPick ? -1 : PickPilot(mouse);
            if (click && _hover >= 0 && _hover != _focus) SetFocus(_hover);
            if (!ui) { if (left) Cycle(-1); if (right || tab) Cycle(1); if (enter) Deploy(); return; }

            if (ui.CurrentMode == TitleUI.Mode.Friend || ui.CurrentMode == TitleUI.Mode.SelectMode)
            {
                if (ui.IsTyping) return;
                if (KeyboardEscape()) ui.Cancel();
                else if (friend) ui.ToggleFriend();
                return;
            }
            if (ui.IsTyping) return;                                             // the friend-code field has the keys
            if (up) ui.Navigate(0, -1);
            if (down) ui.Navigate(0, 1);
            if (left && !ui.Navigate(-1, 0)) Cycle(-1);
            if (right && !ui.Navigate(1, 0)) Cycle(1);
            if (tab && ui.CurrentMode == TitleUI.Mode.Main) Cycle(1);

            if (friend) ui.ToggleFriend();
            if (enter) ui.Submit();
            if (esc) ui.Cancel();
        }

        static bool KeyboardEscape()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        int PickPilot(Vector2 screen)
        {
            if (!cam) return -1;
            var r = new Rect(0, 0, Screen.width, Screen.height);
            if (!r.Contains(screen)) return -1;
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(screen), 60f, ~0, QueryTriggerInteraction.Collide);
            int best = -1; float bestD = float.MaxValue;
            foreach (var h in hits)
            {
                var p = h.collider ? h.collider.GetComponentInParent<TitleCharacter>() : null;
                if (!p) continue;
                int i = System.Array.IndexOf(pilots, p);
                if (i >= 0 && h.distance < bestD) { bestD = h.distance; best = i; }
            }
            return best;
        }

        void Cycle(int d)
        {
            if (_focus < 0 || pilots.Length == 0) return;
            SetFocus(((_focus + d) % pilots.Length + pilots.Length) % pilots.Length);
        }

        // ------------------------------------------------------------------------------------------------ actions
        /// -1 = both pilots (opening shot), 0 / 1 = that pilot in front, the other pushed back. Remembered for next time.
        public void SetFocus(int i)
        {
            if (_deploying || i == _focus || i >= pilots.Length) return;
            _focus = i;
            if (i >= 0 && OnYourMark.TitleMatch.Active) OnYourMark.TitleMatch.Active.PreferPilot(i);
            for (int j = 0; j < pilots.Length; j++)
                if (pilots[j]) pilots[j].SetSlot(i < 0 ? TitleCharacter.Slot.Home : j == i ? TitleCharacter.Slot.Front : TitleCharacter.Slot.Back);
            if (i >= 0) { _kick = 1f; GameSettings.TitlePilot = i; }               // small push-in with the ignition
            if (ui) ui.ShowFocus(i);
        }

        /// SINGLE: straight into the OYM showcase (arrival cutscene, then the level). Friend rooms keep TitleMatch -> Arena.
        public void Deploy()
        {
            if (_deploying || _focus < 0) return;
            StartCoroutine(DeployShowcase());
        }

        IEnumerator DeployShowcase()
        {
            // a friend room that is still open or closing goes first: TitleMatch sends a title that is already deploying
            // back to the title scene when its room finishes closing
            if (OnYourMark.TitleMatch.Active)
            {
                OnYourMark.TitleMatch.Active.Close();
                while (OnYourMark.TitleMatch.Active) yield return null;
                if (ui && ui.CurrentMode != TitleUI.Mode.SelectMode) yield break;
            }
            if (_deploying || _focus < 0) yield break;
            string pilot = pilots[_focus] ? pilots[_focus].displayName : null;
            AnimateDeployment();
            yield return new WaitForSecondsRealtime(deployDelay);
            LoadShowcase(pilot);
        }

        /// OYM.ShowcaseSession (feature/arrival-cutscene) carries the pick into the showcase: that pilot takes slot 0 and
        /// the arrival cutscene plays. Looked up by name, so the title also builds and runs before that module is merged
        /// (the level then starts with its default pilot and without the cutscene).
        static void LoadShowcase(string pilot)
        {
            const System.Reflection.BindingFlags open = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            var session = typeof(TitleDirector).Assembly.GetType("OYM.ShowcaseSession");
            session?.GetField("SelectedPilot", open)?.SetValue(null, pilot);
            var load = session?.GetMethod("LoadScene", open, null, new[] { typeof(string) }, null);
            Debug.Log($"[OYM-Title] deploy {pilot} -> {ShowcaseScene.Name}" + (load == null ? " (no ShowcaseSession: no arrival cutscene)" : ""));
            if (load != null) load.Invoke(null, new object[] { ShowcaseScene.Name });
            else UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(ShowcaseScene.Name, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        void OpenRoom(OnYourMark.TitleMatch.Mode mode, string code = null)
        {
            if (_deploying || _focus < 0) return;
            OnYourMark.TitleMatch.Begin(this, mode, _focus, code);
        }

        public void AnimateDeployment()
        {
            if (_deploying) return;
            _deploying = true;
            var p = pilots[_focus];
            GameSettings.Save();
            p.SetSlot(TitleCharacter.Slot.Launch);
            _kick = 2.2f;
            if (ui) { ui.ShowDeploy(p.displayName); ui.FadeOut(deployDelay * .9f); }
            BgmPlayer.FadeOut(deployDelay + .3f);
        }

        // ------------------------------------------------------------------------------------------------ camera
        Shot Target(int focus) => focus >= 0 && focusShots != null && focus < focusShots.Length ? focusShots[focus] : neutralShot;

        void SnapCamera(int focus)
        {
            var s = Target(focus);
            _camP = s.pos; _lookP = s.look; _fov = s.fov; _camV = _lookV = Vector3.zero; _fovV = 0f;
            _par = Vector2.zero;
            DofTarget(focus, out _farS, out _farE); _farSV = _farEV = 0f;
            ApplyCamera(0f);
            ApplyDof();
        }

        void UpdateCamera(float dt)
        {
            if (dt <= 0f) return;
            var s = Target(_focus);
            float w = 2f * Mathf.PI * camFreq, z = camDamping;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.01f)); float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                _camV += (w * w * (s.pos - _camP) - 2f * z * w * _camV) * h; _camP += _camV * h;
                _lookV += (w * w * (s.look - _lookP) - 2f * z * w * _lookV) * h; _lookP += _lookV * h;
                _fovV += (w * w * (s.fov - _fov) - 2f * z * w * _fovV) * h; _fov += _fovV * h;
            }
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 2.2f);
            Vector2 parT = scripted || _deploying || !_introDone ? Vector2.zero : _mouseN;
            _par = Vector2.Lerp(_par, parT, 1f - Mathf.Exp(-2.2f * dt));
            ApplyCamera(Time.time);
        }

        void ApplyCamera(float t)
        {
            if (!cam) return;
            Vector3 hand = new Vector3(Mathf.PerlinNoise(t * 0.23f, _seed) - 0.5f, Mathf.PerlinNoise(_seed, t * 0.19f) - 0.5f, 0f) * (2f * handheld);
            Vector3 par = new Vector3(_par.x * mouseParallax.x, _par.y * mouseParallax.y, 0f);
            Vector3 fwd = (_lookP - _camP).normalized;
            Quaternion rot = Quaternion.LookRotation(_lookP - _camP, Vector3.up);
            float kick = _kick * _kick * (3f - 2f * Mathf.Min(_kick, 1f)) * 0.5f;
            Vector3 basePos = _camP + rot * par;                                // parallax re-aims at the look point
            cam.transform.position = basePos + rot * hand + fwd * (0.12f * kick);
            cam.transform.rotation = Quaternion.LookRotation(_lookP - basePos, Vector3.up);
            cam.fieldOfView = _fov - 1.2f * kick;
        }

        void DofTarget(int focus, out float s, out float e)
        {
            if (focus < 0 || !pilots[focus] || !cam) { s = neutralFarStart; e = neutralFarEnd; return; }
            float d = Vector3.Distance(Target(focus).pos, pilots[focus].front.pos + Vector3.up * (pilots[focus].pivotHeight + 0.3f));
            s = d + focusFarRange.x; e = d + focusFarRange.y;
        }

        void UpdateDof(float dt)
        {
            if (dt <= 0f) return;
            DofTarget(_focus, out float ts, out float te);
            // log-space spring: the neutral range is kilometres, the focus range metres
            float w = 2f * Mathf.PI * 0.9f, z = 1f;
            float ls = Mathf.Log(_farS), le = Mathf.Log(_farE);
            _farSV += (w * w * (Mathf.Log(ts) - ls) - 2f * z * w * _farSV) * dt; ls += _farSV * dt;
            _farEV += (w * w * (Mathf.Log(te) - le) - 2f * z * w * _farEV) * dt; le += _farEV * dt;
            _farS = Mathf.Exp(ls); _farE = Mathf.Max(Mathf.Exp(le), _farS + 0.5f);
            ApplyDof();
        }

        void ApplyDof()
        {
            if (_dof == null) return;
            _dof.farFocusStart.Override(_farS);
            _dof.farFocusEnd.Override(_farE);
        }

        // ------------------------------------------------------------------------------------------------ edit-mode stills
        /// Put pilots, camera and depth of field in the resting state of `focus` (OYMMainTitle stills).
        public void PoseInstant(int focus, DepthOfField dof)
        {
            _focus = focus; _dof = dof;
            for (int j = 0; j < pilots.Length; j++)
                if (pilots[j]) pilots[j].Snap(focus < 0 ? TitleCharacter.Slot.Home : j == focus ? TitleCharacter.Slot.Front : TitleCharacter.Slot.Back);
            _kick = 0f;
            SnapCamera(focus);
        }
    }
}
