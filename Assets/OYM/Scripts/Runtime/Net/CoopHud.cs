using System.Reflection;
using OnYourMark.UI;
using OYM.Cine;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OYM.Net
{
    /// The visor HUD of the game scene is the team's SenseLink HUD (garam layout) with the HUD glitch from the
    /// "HUD 고장·파손" work - HudSwapGlitch for the crossed link, HudBreak for big hits, HudArrangement / HudPerspective /
    /// HudSkillPanel for the Overwatch-style layout - fed from this game instead of the team's networked Player
    /// (their HudPlayerBridge):
    ///   boot      the arrival cutscene hands over (its dialogue told why): the HUD comes up on this pilot's own vitals
    ///   cross     a beat later HudSwapGlitch's cross link tears every module over to the partner (OnYourMarkHud
    ///             ownership: not returned) and the notice says so: "서로 HUD가 꼬였다 / 상대방의 상태를 브리핑하며 헤쳐
    ///             나가야 합니다" (the user's words)
    ///   restore   the link relays on the route (OYM.Field.FieldKit) give modules back one step at a time (Restore):
    ///             they swap back toward self with the same glitch and the notice says what came back
    ///   hits      HudBreak.Hit(lost / max HP) on this pilot's hits (its light / medium / heavy glitch)
    ///   data      own HP from HuskEncounter, the partner's from their packets (CoopGame; single-player: the other
    ///             pilot, unhurt); reserves: dash charges for KESTREL, rounds for ORBIT FRAME
    /// The HUD stays off under cutscene letterboxes (they would cover its bottom modules), level cutaways and the boss
    /// entrance. The team's exchange / support slots and its reticle / ally marker stay off (no such systems here: the
    /// marks are MarkOverlay's); the minimap sits a little lower, clear of the play-test text. No cutscene: up and
    /// crossed shortly after the game starts. Co-op: the first hit on this pilot after the cross says where its own
    /// numbers went.
    [DefaultExecutionOrder(-5)]                        // before OnYourMarkHud.Update draws the values
    public sealed class CoopHud : MonoBehaviour
    {
        public static CoopHud Current { get; private set; }

        public const string NoticeTitle = "서로 HUD가 꼬였다";
        public const string NoticeBody = "상대방의 상태를 브리핑하며 헤쳐 나가야 합니다";

        static readonly string[] OffGroups =
        {
            "Reticle", "AllyMarker", "Channels", "SupportWheel",   // 하단 단축 슬롯 4개(ToggleChannels · ToggleSupport · QuickShield · QuickScan)는 보인다
            "Channel0", "Channel1", "Channel2", "Support0", "Support1", "Support2", "CloseTray", "Exchange", "CancelExchange",
            "Awakened", "Toast"
        };

        PlayTestDirector _d;
        GameObject _go;
        Canvas _canvas;
        OnYourMarkHud _hud;
        HudSwapGlitch _swap;
        HudSkillPanel _skills;
        AudioSource _sfx;
        bool _booted, _crossed, _cineSeen, _waitHandBack, _hintShown, _hooked, _noBreakFlash;
        static FieldInfo s_breakFlash;
        float _crossAt = -1f, _playT, _hintUntil = -1f, _hintA, _noticeT = -1f;
        UIDocument _doc;
        Label _hint, _nTitle, _nBody;
        VisualElement _notice, _nRule;
        static MethodInfo s_refresh;
        static FieldInfo s_started;
        /// modules a link relay gave back to this pilot (0 vitals, 1 minimap, 2 reserves, 3 skills)
        readonly bool[] _back = new bool[4];

        public bool Crossed => _crossed;
        public bool IsBack(int module) => module >= 0 && module < _back.Length && _back[module];

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        /// another session's recorder drives the scene (switch flags like -oymNoRadio and our -coop* are not automation)
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymPerfHud" && a != "-oymCineDemo" && a != "-oymHudDemo") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != ShowcaseScene.Name || OtherAutomation()) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymNoHud") >= 0) return;
            PlayTestDirector d = null;
            foreach (var root in s.GetRootGameObjects()) { d = root.GetComponentInChildren<PlayTestDirector>(true); if (d) break; }
            if (!d || d.controllers == null || d.controllers.Length == 0) return;
            var go = new GameObject("Visor HUD (SenseLink, crossed link)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<CoopHud>()._d = d;
        }

        void Awake()
        {
            Current = this;
            var prefab = Resources.Load<GameObject>("SenseLinkHUD");
            if (!prefab) { var r = Resources.Load<CoopHudPrefab>("OYM_CoopHudPrefab"); if (r) prefab = r.hud; }   // team layout: garam/UI/Prefabs
            if (!prefab) { Debug.LogWarning("[OYM-HUD] Resources/SenseLinkHUD missing (Assets/garam/UI not imported): no visor HUD"); enabled = false; return; }
            _go = Instantiate(prefab);
            _go.name = "SenseLink HUD (visor)";
            SceneManager.MoveGameObjectToScene(_go, gameObject.scene);            // goes with the game scene
            _hud = _go.GetComponent<OnYourMarkHud>();
            _hud.demoInput = false;                // the preview keys (1/2/3, Q, E, Tab, F1-F10) belong to the game here
            _canvas = _go.GetComponent<Canvas>();
            _swap = _go.GetComponent<HudSwapGlitch>();
            _skills = _go.GetComponent<HudSkillPanel>();
            foreach (var n in OffGroups) { var t = _go.transform.Find(n); if (t) t.gameObject.SetActive(false); }
            LowerMap(_go.GetComponent<HudArrangement>());                          // before its Start places the groups
            // own data first, without a swap event (the cross link comes later)
            for (int i = 0; i < _hud.returned.Length; i++) _hud.returned[i] = true;
            Refresh();
            if (_canvas) _canvas.enabled = false;
            // the cross link's sound slot is empty in the prefab: a short digital tear
            _sfx = _go.AddComponent<AudioSource>();
            _sfx.playOnAwake = false; _sfx.spatialBlend = 0f; _sfx.volume = 0.5f;
            SetField(_swap, "sfxSource", _sfx);
            SetField(_swap, "swapClip", MakeGlitchClip());
            BuildOverlay();
            // test aid: -coopNoBreakFlash turns HudBreak's full-screen HDR flash off (the module glitches stay)
            _noBreakFlash = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopNoBreakFlash") >= 0;
            CineDialogue.LineStarted += OnLine;
            ArrivalCutscene.Finished += OnCineFinished;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            CineDialogue.LineStarted -= OnLine;
            ArrivalCutscene.Finished -= OnCineFinished;
            var e = HuskEncounter.Current;
            if (e && _hooked) e.PlayerDamaged -= OnPlayerDamaged;
            if (_go) Destroy(_go);
        }

        static void SetField(object o, string name, object value)
        {
            if (o == null) return;
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, value);
        }

        /// the round minimap and the objective under it move down past the play-test text in the top left corner
        static void LowerMap(HudArrangement arrangement)
        {
            if (!arrangement) return;
            var f = typeof(HudArrangement).GetField("places", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(f?.GetValue(arrangement) is HudArrangement.Place[] places)) return;
            for (int i = 0; i < places.Length; i++)
                if (places[i].group == "Spatial" || places[i].group == "Objective") places[i].position.y += 84f;
            f.SetValue(arrangement, places);
        }

        /// the ownership icons (self / partner) follow `returned` (OnYourMarkHud refreshes them on its own changes only)
        void Refresh()
        {
            if (s_refresh == null) s_refresh = typeof(OnYourMarkHud).GetMethod("RefreshOwnership", BindingFlags.Instance | BindingFlags.NonPublic);
            s_refresh?.Invoke(_hud, null);
        }

        // ------------------------------------------------------------------------------------------------ who is who
        ZeroGFlight Me
        {
            get
            {
                var fc = FlightCamera.Main;
                var f = fc && fc.target ? fc.target.GetComponent<ZeroGFlight>() : null;
                if (f) return f;
                var g = CoopGame.Current;
                if (g && g.LocalFlight) return g.LocalFlight;
                return _d && _d.controllers.Length > 0 ? _d.controllers[0] : null;
            }
        }

        ZeroGFlight Partner
        {
            get
            {
                var g = CoopGame.Current;
                if (g && g.RemoteFlight) return g.RemoteFlight;
                var me = Me;
                if (_d && _d.controllers != null) foreach (var c in _d.controllers) if (c && c != me) return c;
                return null;
            }
        }

        static bool Coop => CoopGame.Current && !CoopGame.Current.Lost;
        static string Callsign(ZeroGFlight f) => f ? SignalInstall.NameOf(f) : "PARTNER";
        static Camera Cam() { var fc = FlightCamera.Main; return fc && fc.Cam ? fc.Cam : Camera.main; }

        // ------------------------------------------------------------------------------------------------ the beats
        void OnLine(int index, DialogueLine line) { _cineSeen = true; }

        /// the arrival cutscene handed over (also after a skip): up and crossed once the game view is back
        void OnCineFinished()
        {
            _cineSeen = true;
            if (!_crossed) _waitHandBack = true;
        }

        void Boot()
        {
            if (_booted || !_hud) return;
            _booted = true;
            if (s_started == null) s_started = typeof(OnYourMarkHud).GetField("started", BindingFlags.Instance | BindingFlags.NonPublic);
            s_started?.SetValue(_hud, Time.unscaledTime);                         // its own intro fade, from now
            if (_canvas) _canvas.worldCamera = Cam();
        }

        void Cross()
        {
            if (_crossed || !_hud) return;
            if (!_booted) Boot();
            _crossed = true;
            // the team's cross link (snapshots the old readouts, tears them out module by module) - then the data flips
            if (_swap) _swap.PlayCrossLink(false);
            for (int i = 0; i < _hud.returned.Length; i++) _hud.returned[i] = i < _back.Length && _back[i];
            Refresh();
            if (_skills) _skills.SetTelemetry(default, _back[3]);
            ShowNotice(NoticeTitle, NoticeBody);
            Debug.Log($"[OYM-HUD] link crossed: this visor shows {Callsign(Partner)}'s vitals (SenseLink cross link) - \"{NoticeTitle} / {NoticeBody}\"");
        }

        /// A link relay on the route (OYM.Field.FieldKit) fixed part of the crossed link: these modules (0 vitals,
        /// 1 minimap, 2 reserves, 3 skills) show this pilot's own data again - the swap glitch plays them back toward
        /// self (OnYourMarkHud.ChannelSwapping; the skill panel's OwnerSwapping) - and the notice says what came back.
        /// Before the cross it only marks them (the cross leaves them on this pilot).
        public void Restore(int[] modules, string title, string body)
        {
            if (!_hud || modules == null) return;
            bool any = false;
            foreach (int m in modules)
                if (m >= 0 && m < _back.Length && !_back[m]) { _back[m] = true; any = true; }
            if (!any) return;
            if (_crossed)
            {
                _hud.SetGameState(_back[0], _back[1], _back[2], -1);
                Refresh();
                if (_skills) _skills.SetTelemetry(default, _back[3]);
            }
            if (!string.IsNullOrEmpty(title)) ShowNotice(title, body);
            Debug.Log($"[OYM-HUD] link relay: modules {string.Join(",", modules)} back on this pilot (vitals {_back[0]}, map {_back[1]}, reserves {_back[2]}, skills {_back[3]}) - \"{title} / {body}\"");
        }

        void ShowNotice(string title, string body)
        {
            if (_nTitle == null) return;
            _nTitle.text = title ?? "";
            _nBody.text = body ?? "";
            _noticeT = 0f;
        }

        void OnPlayerDamaged(float amount, Vector3 from)
        {
            var e = HuskEncounter.Current;
            float max = e && e.config ? Mathf.Max(1f, e.config.playerHP) : 100f;
            if (_booted) HudBreak.Hit(amount / max);                               // the warm glitch: light / medium / heavy
            if (!_crossed || _hintShown || !Coop || _back[0]) return;
            _hintShown = true;
            _hint.text = $"내 체력은 {Callsign(Partner)} 화면에 떠요  ·  무전으로 물어보세요";
            _hintUntil = Time.unscaledTime + 4.5f;
        }

        // ------------------------------------------------------------------------------------------------ loop
        void Update()
        {
            if (!_hud) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f), now = Time.unscaledTime;
            var e = HuskEncounter.Current;
            if (e && !_hooked) { e.PlayerDamaged += OnPlayerDamaged; _hooked = true; }
            bool view = SignalInstall.PlayerView(e);                               // false under any letterbox / cutaway

            if (_waitHandBack && view)
            {
                _waitHandBack = false;
                Boot();
                if (!_crossed && _crossAt < 0f) _crossAt = now + 1.3f;             // own vitals for a beat, then the cross
            }
            // no cutscene at all (scene opened straight, -oymNoCine): up in the game, then the cross
            if (!_cineSeen && view && !_crossed) _playT += dt;
            if (!_cineSeen && !_booted && _playT > 0.6f) Boot();
            if (!_cineSeen && !_crossed && _crossAt < 0f && _playT > 2.0f) _crossAt = now;
            if (_crossAt > 0f && now >= _crossAt && view) { _crossAt = -1f; Cross(); }

            if (_canvas)
            {
                _canvas.enabled = _booted && view;
                if (!_canvas.worldCamera) _canvas.worldCamera = Cam();
            }
            Feed(e);
            Overlay(now, dt, view);
            if (_noBreakFlash) KillBreakFlash();
        }

        void KillBreakFlash()
        {
            var hb = HudBreak.Local;
            if (!hb) return;
            if (s_breakFlash == null) s_breakFlash = typeof(HudBreak).GetField("_screenFlash", BindingFlags.Instance | BindingFlags.NonPublic);
            if (s_breakFlash?.GetValue(hb) is UnityEngine.UI.Image img && img) { Destroy(img.gameObject); s_breakFlash.SetValue(hb, null); Debug.Log("[OYM-HUD] test: HudBreak screen flash off"); }
        }

        void Feed(HuskEncounter e)
        {
            var me = Me;
            var mate = Partner;
            _hud.localHealth = e ? Mathf.Clamp01(e.PlayerHP01) : 1f;
            Reserves(me, me && me.Dashing, out int ammo, out int cap, out bool dash);
            _hud.localAmmo = ammo; _hud.localAmmoCapacity = cap; _hud.localResourceIsDash = dash;
            var g = CoopGame.Current;
            bool partnerDashing = g && g.Puppet && g.Puppet.HasState && g.Puppet.State.Has(PilotState.FlagDash);
            Reserves(mate, partnerDashing, out int pa, out int pc, out bool pd);
            float php = Coop ? (g.PartnerDown ? 0f : g.PartnerHP01) : 1f;
            _hud.SetPartnerTelemetry(php, pa, pc, pd);
            if (_skills) _skills.SetTelemetry(default, !_crossed || _back[3]);  // no Q / E skills here: dim tiles, crossed with the rest
            PartnerDash(me, mate, g);
        }

        // 총잡이(ORBIT FRAME) 화면에만: 소모품 칸 오른쪽에 칼잡이(KESTREL)의 Shift 돌진 충전 상태를 항상 띄운다 (HUD 교차와 무관).
        // 싱글: 같은 씬의 KESTREL에서 남은 초를 읽는다. 코옵: 상대 패킷의 FlagStrikeReady (준비 / 충전 중).
        UnityEngine.UI.Text _dashText;
        static readonly Color DashReadyInk = new Color32(0xF3, 0xEE, 0xDD, 255), DashChargingInk = new Color32(0xFF, 0x6B, 0x5E, 255);

        void PartnerDash(ZeroGFlight me, ZeroGFlight mate, CoopGame g)
        {
            bool remote = Coop && g.Puppet && g.Puppet.HasState;
            var kestrel = mate as KestrelController;
            bool show = me && !(me is KestrelController) && kestrel;
            if (!_dashText)
            {
                if (!show) return;
                var reserves = _go.transform.Find("Reserves");
                if (!reserves) return;
                var rt = OnYourMarkHud.Rect("Partner dash", reserves, 236f, 6f, 110f, 70f);
                rt.gameObject.layer = reserves.gameObject.layer;
                _dashText = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
                _dashText.font = _hud.monoFont ? _hud.monoFont : _hud.displayFont ? _hud.displayFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _dashText.fontSize = 16;
                _dashText.lineSpacing = 1.1f;
                _dashText.alignment = TextAnchor.UpperLeft;
                _dashText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _dashText.verticalOverflow = VerticalWrapMode.Overflow;
                _dashText.raycastTarget = false;
            }
            _dashText.enabled = show;
            if (!show) return;
            bool ready;
            string state;
            if (remote)
            {
                ready = g.Puppet.State.Has(PilotState.FlagStrikeReady);
                state = ready ? "READY" : "CHARGING";
            }
            else
            {
                float left = kestrel.StrikeArmed ? kestrel.StrikeCooldownLeft : 0f;
                ready = left <= 0f;
                state = ready ? "READY" : $"{left:0.0}s";
            }
            string text = "KESTREL DASH\n" + state;
            if (_dashText.text != text) _dashText.text = text;
            _dashText.color = ready ? DashReadyInk : DashChargingInk;
        }

        /// reserves module: KESTREL = dash charges, ORBIT FRAME = railgun rounds (no ammo system in this build: full)
        static void Reserves(ZeroGFlight f, bool dashing, out int ammo, out int cap, out bool isDash)
        {
            isDash = f is KestrelController;
            cap = isDash ? 3 : 8;
            ammo = isDash && dashing ? cap - 1 : cap;
        }

        // ------------------------------------------------------------------------------------------------ notice + hint
        static readonly Color Ink = new Color32(0xF3, 0xEE, 0xDD, 255), Lavender = new Color32(0xB7, 0xA6, 0xF6, 255),
                              Lime = new Color32(0xD5, 0xEF, 0x86, 255);

        void BuildOverlay()
        {
            _doc = SignalUI.Document(transform, "visor notice", 4f);
            var root = _doc.rootVisualElement;

            // the crossed-link notice (the user's words), high in the frame in the SenseLink colours
            _notice = new VisualElement { pickingMode = PickingMode.Ignore };
            _notice.style.position = Position.Absolute; _notice.style.left = 0; _notice.style.right = 0; _notice.style.top = Length.Percent(21);
            _notice.style.alignItems = Align.Center; _notice.style.opacity = 0f;
            _nTitle = SignalUI.Text(50f, Ink, 4, true);
            _nTitle.text = NoticeTitle;
            _nTitle.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 16f, color = new Color(Lavender.r, Lavender.g, Lavender.b, 0.95f) };
            _nRule = new VisualElement { pickingMode = PickingMode.Ignore };
            _nRule.style.width = 560; _nRule.style.height = 2; _nRule.style.marginTop = 12; _nRule.style.marginBottom = 12;
            _nRule.style.backgroundColor = Lime;
            _nBody = SignalUI.Text(27f, Ink, 1, true);
            _nBody.text = NoticeBody;
            _notice.Add(_nTitle); _notice.Add(_nRule); _notice.Add(_nBody);
            root.Add(_notice);

            _hint = SignalUI.Text(24f, Ink, 1, true);
            _hint.style.position = Position.Absolute; _hint.style.left = 0; _hint.style.right = 0; _hint.style.top = Length.Percent(36);
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter; _hint.style.opacity = 0f;
            root.Add(_hint);
        }

        void Overlay(float now, float dt, bool view)
        {
            // notice: comes in with the cross link, holds ~4.5 s, fades
            if (_noticeT >= 0f)
            {
                _noticeT += dt;
                float a = _noticeT < 0.2f ? _noticeT / 0.2f : _noticeT > 4.4f ? 1f - (_noticeT - 4.4f) / 0.6f : 1f;
                _notice.style.opacity = view ? Mathf.Clamp01(a) : 0f;
                _nRule.style.width = 560f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_noticeT / 0.35f));
                if (_noticeT > 5f) { _noticeT = -1f; _notice.style.opacity = 0f; }
            }
            bool hintOn = now < _hintUntil && view;
            _hintA = Mathf.MoveTowards(_hintA, hintOn ? 1f : 0f, dt / (hintOn ? 0.2f : 0.5f));
            _hint.style.opacity = _hintA;
        }

        /// a short digital tear: gated noise bursts over a falling square blip, a little bit-crushed
        static AudioClip MakeGlitchClip()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.62f);
            var data = new float[n];
            var rnd = new System.Random(4242);
            float gate = 1f; int gateLeft = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                if (gateLeft-- <= 0) { gate = rnd.NextDouble() < 0.62 ? 1f : 0f; gateLeft = rnd.Next(rate / 90, rate / 28); }
                float env = Mathf.Exp(-t * 4.2f);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0) * gate;
                float f = Mathf.Lerp(1900f, 240f, Mathf.Clamp01(t / 0.45f));
                float sq = Mathf.Sin(2f * Mathf.PI * f * t) > 0f ? 1f : -1f;
                float v = (noise * 0.55f + sq * 0.28f * (t < 0.45f ? 1f : 0f)) * env;
                data[i] = Mathf.Round(v * 12f) / 12f * 0.8f;
            }
            var clip = AudioClip.Create("visor link glitch", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
