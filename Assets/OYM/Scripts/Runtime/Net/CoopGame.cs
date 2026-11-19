using OYM.Cine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OYM.Net
{
    /// The game scene with two people (installs itself on sceneLoaded when a CoopSession deployed, like the other
    /// game-scene modules: no scene edits). Each PC flies its own pilot (the one the room gave it, slot 0 of the
    /// PlayTestDirector, Tab locked); the other pilot is a CoopPuppet fed ~30 times a second. Both screens wait at a
    /// black curtain until both scenes are in, then the arrival cutscene starts on both at once (its lines run on their
    /// own; only the host may hold to skip). The host's HUSK go for either pilot, the hits on the guest's pilot are
    /// sent to the guest (PilotHit), the rest of the fight is CoopWorld. The partner's HP feeds the crossed visor
    /// (CoopHud). A dropped connection: the host flies on alone, the guest goes back to the title.
    [DefaultExecutionOrder(-30)]
    public sealed class CoopGame : MonoBehaviour
    {
        public static CoopGame Current { get; private set; }

        CoopSession _s;
        PlayTestDirector _d;
        HuskEncounter _e;
        ZeroGFlight _local, _remote;
        PilotSampler _sampler;
        CoopPuppet _puppet;
        CoopWorld _world;
        CoopCurtain _curtain;
        bool _localReady, _remoteReady, _cineStarted, _lost, _waiting = true;
        float _sendT, _readyT, _waitStart, _lostAt = -1f;

        public bool IsHost => _s && _s.IsHost;
        /// both scenes are in (the host sends the world from then on)
        public bool PartnerInGame => _remoteReady && !_lost;
        public bool Lost => _lost;
        public ZeroGFlight LocalFlight => _local;
        public ZeroGFlight RemoteFlight => _remote;
        public CoopPuppet Puppet => _puppet;
        public CoopWorld World => _world;
        /// the partner's HP 0..1 (their packets) and whether they are down
        public float PartnerHP01 => _puppet && _puppet.HasState ? Mathf.Clamp01(_puppet.State.hp) : 1f;
        public bool PartnerDown => _puppet && _puppet.HasState && _puppet.State.Has(PilotState.FlagDown);

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (s.name == GameSession.TitleScene)
            {
                // back on the title: the room is over
                if (CoopSession.Active && CoopSession.InGame) CoopSession.Active.End("back on the title");
                return;
            }
            if (s.name != ShowcaseScene.Name || !CoopSession.InGame) return;
            PlayTestDirector d = null;
            foreach (var root in s.GetRootGameObjects()) { d = root.GetComponentInChildren<PlayTestDirector>(true); if (d) break; }
            if (!d || d.controllers == null || d.controllers.Length < 2) { Debug.LogWarning("[OYM-COOP] game scene has no two pilots"); return; }
            var go = new GameObject("Co-op (two pilots)");
            SceneManager.MoveGameObjectToScene(go, s);
            var g = go.AddComponent<CoopGame>();
            g._d = d;
            g._s = CoopSession.Active;
            g._curtain = go.AddComponent<CoopCurtain>();
            g._curtain.Show("WAITING FOR YOUR PARTNER", "파트너 연결 대기 중");      // black from the first frame
        }

        void Awake() { Current = this; }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_s) { _s.Message -= OnMessage; _s.Lost -= OnLost; }
            ArrivalCutscene.SkipStarted -= OnSkipStarted;
        }

        void Start()
        {
            _e = HuskEncounter.Current;
            // the pilots: the one the room gave this PC (GameSession put it in slot 0) and the partner's
            string mine = ShowcaseSession.SelectedPilot;
            for (int i = 0; i < _d.controllers.Length; i++)
            {
                var c = _d.controllers[i];
                if (!c) continue;
                string n = _d.names != null && i < _d.names.Length ? _d.names[i] : c.name;
                if (!_local && n == mine) _local = c;
            }
            if (!_local) _local = _d.controllers[0];
            foreach (var c in _d.controllers) if (c && c != _local) { _remote = c; break; }
            _sampler = new PilotSampler(_local);
            _puppet = _remote.gameObject.AddComponent<CoopPuppet>();
            _puppet.Init(_remote);
            if (_e)
            {
                _e.Remote = new HuskEncounter.RemotePilot { t = _remote.transform, bodyCenter = _remote.bodyCenter, damage = SendPilotHit };
            }
            _world = gameObject.AddComponent<CoopWorld>();
            _world.Init(this, _s, _e);
            _s.Message += OnMessage;
            _s.Lost += OnLost;
            ArrivalCutscene.SkipStarted += OnSkipStarted;

            // nobody moves and the level clock does not run while the other PC is still loading
            _d.enabled = false;
            if (_d.flightCamera) _d.flightCamera.scripted = true;
            _waitStart = Time.unscaledTime;
            Debug.Log($"[OYM-COOP] game scene: this PC flies {Name(_local)}, the partner {Name(_remote)} ({(IsHost ? "host" : "guest")})");
            if (!_s.Connected) OnLost("the connection dropped while loading");
        }

        static string Name(ZeroGFlight f) => f is KestrelController ? "KESTREL" : f is OrbitFrameController ? "ORBIT FRAME" : (f ? f.name : "?");

        // ------------------------------------------------------------------------------------------------ messages
        void OnMessage(CoopMsg.R r)
        {
            switch (r.Type)
            {
                case CoopMsg.Ready:
                    if (!_remoteReady)
                    {
                        _remoteReady = true;
                        Debug.Log("[OYM-COOP] partner is in the scene");
                        if (IsHost) _world.SendRoster();
                    }
                    break;
                case CoopMsg.StartCine:
                    if (!IsHost) StartTogether();
                    break;
                case CoopMsg.SkipCine:
                    if (!IsHost && ArrivalCutscene.Current) ArrivalCutscene.Current.RequestSkip();
                    break;
                case CoopMsg.Pilot:
                    _puppet.Push(PilotState.Read(r));
                    break;
                case CoopMsg.PilotHit:
                {
                    float amount = r.F(); Vector3 from = r.V3(); float knock = r.F();
                    if (!IsHost && _e) _e.DamagePlayer(amount, from, knock);
                    break;
                }
                default:
                    _world.Receive(r);
                    break;
            }
        }

        void SendPilotHit(float amount, Vector3 from, float knock)
        {
            if (_s && _s.Connected) _s.Send(new CoopMsg.W(CoopMsg.PilotHit).F(amount).V3(from).F(knock).Done());
        }

        void OnSkipStarted()
        {
            if (IsHost && _s && _s.Connected) _s.Send(new[] { CoopMsg.SkipCine });
        }

        // ------------------------------------------------------------------------------------------------ the start
        void Update()
        {
            float now = Time.unscaledTime;
            if (_waiting)
            {
                // say "in" until the partner answers (a Ready sent while it was still loading may have been early)
                if (!_localReady && Time.frameCount > 3) { _localReady = true; _readyT = 0f; }
                if (_localReady && now >= _readyT && _s && _s.Connected) { _readyT = now + 0.5f; _s.Send(new[] { CoopMsg.Ready }); }
                if (IsHost && _remoteReady) { _s.Send(new[] { CoopMsg.Ready }); _s.Send(new[] { CoopMsg.StartCine }); StartTogether(); }
                else if (now - _waitStart > 45f) { Debug.LogWarning("[OYM-COOP] partner never came in: starting alone"); StartTogether(); }
                else if (_lost) StartTogether();
            }

            // this PC's pilot on the wire, 30 Hz (also during cutscenes: the HP keeps going)
            if (_s && _s.Connected && now >= _sendT && _sampler != null && !HuskEncounter.CutsceneRunning())
            {
                _sendT = now + 1f / 30f;
                _s.Send(_sampler.Sample(_e).Write());
            }

            if (_e && _e.Remote != null && _puppet && _puppet.HasState)
            {
                _e.Remote.velocity = _puppet.State.vel;
                _e.Remote.alive = !_puppet.State.Has(PilotState.FlagDown) && !_lost;
            }

            if (_lostAt > 0f && now - _lostAt > 3.5f && !IsHost)
            {
                _lostAt = -1f;
                ShowcaseSession.LoadScene(GameSession.TitleScene);
            }
        }

        void StartTogether()
        {
            if (!_waiting) return;
            _waiting = false;
            _curtain.Hide(0.15f);
            var cine = ArrivalCutscene.Current;
            if (cine && !cine.Running)
            {
                _cineStarted = true;
                cine.Begin();                                         // turns the director off until it hands back
            }
            else
            {
                _d.enabled = true;
                if (_d.flightCamera) _d.flightCamera.scripted = false;
            }
            Debug.Log("[OYM-COOP] both in: go" + (_cineStarted ? " (arrival cutscene)" : ""));
        }

        // ------------------------------------------------------------------------------------------------ dropped
        void OnLost(string why)
        {
            if (_lost) return;
            _lost = true;
            if (_e) { _e.Remote = null; }
            if (_world) _world.PartnerGone();
            if (IsHost)
            {
                _curtain.Toast("PARTNER DISCONNECTED  ·  FLYING ON ALONE", "파트너 연결이 끊겼습니다. 혼자 계속합니다", 4.5f);
                if (_remote) _remote.gameObject.SetActive(false);
            }
            else
            {
                _curtain.Show("HOST DISCONNECTED", "호스트 연결이 끊겼습니다. 타이틀로 돌아갑니다");
                _lostAt = Time.unscaledTime;
            }
        }
    }

    /// Full-screen black with two lines (waiting for the partner, a dropped host) and a short toast; UI Toolkit, above
    /// the cine overlay.
    public sealed class CoopCurtain : MonoBehaviour
    {
        UIDocument _doc;
        VisualElement _black, _toastBox;
        Label _en, _ko, _toastEn, _toastKo;
        float _alpha, _target, _speed = 4f, _toastUntil = -1f, _toastA, _dots;
        string _baseEn;

        void Awake()
        {
            _doc = OYM.Signal.SignalUI.Document(transform, "co-op curtain", 60f);
            var root = _doc.rootVisualElement;
            _black = OYM.Signal.SignalUI.Fill(new VisualElement());
            _black.style.backgroundColor = Color.black;
            _black.style.alignItems = Align.Center; _black.style.justifyContent = Justify.Center;
            _en = OYM.Signal.SignalUI.Text(30f, new Color(0.92f, 0.95f, 0.97f), 10, true);
            _ko = OYM.Signal.SignalUI.Text(22f, new Color(1f, 1f, 1f, 0.6f), 2);
            _ko.style.marginTop = 14;
            _black.Add(_en); _black.Add(_ko);
            root.Add(_black);

            _toastBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _toastBox.style.position = Position.Absolute; _toastBox.style.top = Length.Percent(12);
            _toastBox.style.left = 0; _toastBox.style.right = 0; _toastBox.style.alignItems = Align.Center; _toastBox.style.opacity = 0f;
            _toastEn = OYM.Signal.SignalUI.Text(24f, new Color(1f, 0.55f, 0.45f), 8, true);
            _toastKo = OYM.Signal.SignalUI.Text(20f, new Color(1f, 1f, 1f, 0.75f), 1);
            _toastKo.style.marginTop = 8;
            _toastBox.Add(_toastEn); _toastBox.Add(_toastKo);
            root.Add(_toastBox);
            _black.style.opacity = 0f;
        }

        public void Show(string en, string ko)
        {
            if (_en == null) Awake();
            _baseEn = en; _en.text = en; _ko.text = ko ?? "";
            _alpha = _target = 1f; _speed = 99f;
            _black.style.opacity = 1f;
        }

        public void Hide(float seconds) { _target = 0f; _speed = 1f / Mathf.Max(0.01f, seconds); }

        public void Toast(string en, string ko, float seconds)
        {
            _toastEn.text = en; _toastKo.text = ko ?? "";
            _toastUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _alpha = Mathf.MoveTowards(_alpha, _target, dt * _speed);
            _black.style.opacity = _alpha;
            if (_alpha > 0f && _baseEn != null && _baseEn.StartsWith("WAITING"))
            {
                _dots += dt * 2f;
                _en.text = _baseEn + new string('.', 1 + (int)_dots % 3);
            }
            bool on = Time.unscaledTime < _toastUntil;
            _toastA = Mathf.MoveTowards(_toastA, on ? 1f : 0f, dt / (on ? 0.15f : 0.5f));
            _toastBox.style.opacity = _toastA;
        }
    }
}
