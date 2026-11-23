using System.Collections;
using System.Reflection;
using OYM.Flow;
using OYM.Net;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Shell
{
    /// The way out of the game scene (user 11-19: "esc 누르면 돌아가기 / 보스잡으면 클리어뜨고 메인화면으로"):
    ///   ESC     a first press shows "ESC 한 번 더 — 메인 화면으로" (the camera lets the cursor go, as before); a second
    ///           press within 2.5 s fades out and loads the main title. Not while the arrival cutscene runs (holding ESC
    ///           skips it there). During the CLEAR, one press goes straight on. Co-op: the session ends, so the partner's
    ///           PC hears a disconnect (a host flies on alone, a guest goes back to its title).
    ///   CLEAR   the CITADEL beaten (HuskLevel's victory; on a co-op guest the host's): after its sinking shot a big
    ///           "CLEAR" with the play time and kills, then a fade and the main title. No white flash (FX taste).
    /// Installs itself with the game scene, and adds the visor HUD's pilot colours (HudPilotColors). Under another
    /// session's test flags the ESC exit is off and the clear stays in the scene (FlowTest sets ReturnInTests).
    public sealed class GameShell : MonoBehaviour
    {
        public static GameShell Current { get; private set; }
        /// test aid: the clear goes back to the title although a test flag is set (FlowTest checks that path)
        public static bool ReturnInTests;

        const float EscWindow = 2.5f, ClearAfterVictory = 7.5f, ClearHold = 5.5f, FadeTime = 0.6f;
        static readonly Color Ink = new Color32(0xF3, 0xEE, 0xDD, 255), Dim = new Color32(0xB9, 0xB4, 0xA6, 255);

        UIDocument _doc;
        VisualElement _shade, _clear, _rule;
        Label _escLine, _clearTitle, _clearSub, _clearStats;
        float _escAt = -9f, _escA, _victoryAt = -1f, _clearAt = -1f, _controlAt = -1f, _fade;
        bool _leaving, _clearShown, _testNoted;

        public bool ClearShown => _clearShown;
        public bool Leaving => _leaving;

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; ReturnInTests = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// another session's recorder / test drives the scene (switch flags -oymNo... are not automation)
        public static bool Automation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymPerfHud") return true;
                if (a == "-coopShots" || a == "-coopQuitAfter" || a == "-coopCallBoss" || a == "-flowTest" || a == "-fieldTest" ||
                    a == "-minimapTest" || a == "-flashCheck") return true;
            }
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != LevelFlow.SceneName) return;
            var go = new GameObject("Game Shell (ESC, clear, HUD colours)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<GameShell>();
            if (!Flag("-oymNoHudColors")) go.AddComponent<HudPilotColors>();
        }

        void Awake() { Current = this; BuildUI(); }
        void OnDestroy() { if (Current == this) Current = null; }

        // ------------------------------------------------------------------------------------------------ loop
        void Update()
        {
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var e = HuskEncounter.Current;
            if (_controlAt < 0f && e && e.Gate) _controlAt = Time.time;                 // play time: from the first control
            if (!_leaving) Esc(now);
            if (_victoryAt < 0f && Victory(e))
            {
                _victoryAt = now;
                Debug.Log($"[OYM-SHELL] CITADEL beaten: CLEAR in {ClearAfterVictory:F1} s, then the main title");
            }
            if (_victoryAt >= 0f && !_clearShown && now - _victoryAt >= ClearAfterVictory) ShowClear(e, now);
            if (_clearShown && !_leaving && now - _clearAt >= ClearHold)
            {
                if (!Automation() || ReturnInTests) Leave("clear");
                else if (!_testNoted) { _testNoted = true; Debug.Log("[OYM-SHELL] test run: the clear stays in the scene"); }
            }
            Animate(now, dt);
        }

        static bool EscPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        void Esc(float now)
        {
            if (!EscPressed() || Automation() || HuskEncounter.CutsceneRunning()) return;
            if (_clearShown) { Leave("clear (ESC)"); return; }
            if (now - _escAt <= EscWindow) { Leave("ESC"); return; }
            _escAt = now;
            Debug.Log("[OYM-SHELL] ESC: once more within 2.5 s goes back to the main title");
        }

        static bool Victory(HuskEncounter e)
        {
            var lv = e ? e.Level : null;
            if (lv) return lv.beat == HuskLevel.Beat.Victory;
            var g = CoopGame.Current;                                                     // co-op guest: the host's beat
            return g && g.World && g.World.Beat == (int)HuskLevel.Beat.Victory;
        }

        void ShowClear(HuskEncounter e, float now)
        {
            _clearShown = true; _clearAt = now;
            float play = _controlAt >= 0f ? Time.time - _controlAt : Time.timeSinceLevelLoad;
            int kills = e ? e.Kills : 0;
            int m = Mathf.FloorToInt(play / 60f), s = Mathf.FloorToInt(play % 60f);
            _clearStats.text = $"플레이 시간 {m}:{s:00}" + (kills > 0 ? $"   ·   처치 {kills}" : "");
            var fc = FlightCamera.Main;
            var me = fc && fc.target ? fc.target.GetComponent<ZeroGFlight>() : null;
            Color pc = HudPilotColors.ColorOf(me);
            _rule.style.backgroundColor = pc;
            _clearTitle.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 22f, color = new Color(pc.r, pc.g, pc.b, 0.9f) };
            Debug.Log($"[OYM-SHELL] CLEAR: play time {m}:{s:00}, {kills} kills");
        }

        /// fade to black, end a co-op session, load the main title
        public void Leave(string reason)
        {
            if (_leaving) return;
            _leaving = true;
            StartCoroutine(LeaveRoutine(reason));
        }

        IEnumerator LeaveRoutine(string reason)
        {
            Debug.Log($"[OYM-SHELL] back to the main title ({reason})");
            float t = 0f;
            while (t < FadeTime) { t += Time.unscaledDeltaTime; _fade = Mathf.Clamp01(t / FadeTime); yield return null; }
            Time.timeScale = 1f;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            var session = CoopSession.Active;
            var game = CoopGame.Current;
            // team repo: the co-op host takes both PCs back to the title together (CoopGame.ReturnTeamToTitle)
            if (session && game && game.IsHost && s_returnTeam != null) { s_returnTeam.Invoke(game, null); yield break; }
            if (session) session.End("left the game (" + reason + ")");
            if (s_loadScene != null) s_loadScene.Invoke(null, new object[] { GameSession.TitleScene });   // local: no build settings needed in the editor
            else if (s_returnToTitle != null) s_returnToTitle.Invoke(null, null);                          // team repo
            else SceneManager.LoadSceneAsync(GameSession.TitleScene);
        }

        // the local project and the team repo differ here: use what exists
        static readonly MethodInfo s_returnTeam = typeof(CoopGame).GetMethod("ReturnTeamToTitle", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
        static readonly MethodInfo s_loadScene = typeof(GameSession).GetMethod("LoadScene", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        static readonly MethodInfo s_returnToTitle = typeof(GameSession).GetMethod("ReturnToTitle", BindingFlags.Public | BindingFlags.Static, null, System.Type.EmptyTypes, null);

        // ------------------------------------------------------------------------------------------------ UI
        void BuildUI()
        {
            _doc = SignalUI.Document(transform, "game shell", 20f);
            var root = _doc.rootVisualElement;

            _shade = SignalUI.Fill(new VisualElement());
            _shade.style.backgroundColor = Color.black;
            _shade.style.opacity = 0f;
            root.Add(_shade);

            _escLine = SignalUI.Text(22f, Ink, 2, true);
            _escLine.text = "ESC 한 번 더  —  메인 화면으로";
            _escLine.style.position = Position.Absolute; _escLine.style.left = 0; _escLine.style.right = 0; _escLine.style.bottom = Length.Percent(13);
            _escLine.style.unityTextAlign = TextAnchor.MiddleCenter; _escLine.style.opacity = 0f;
            root.Add(_escLine);

            _clear = new VisualElement { pickingMode = PickingMode.Ignore };
            _clear.style.position = Position.Absolute; _clear.style.left = 0; _clear.style.right = 0; _clear.style.top = Length.Percent(33);
            _clear.style.alignItems = Align.Center; _clear.style.opacity = 0f;
            _clearTitle = SignalUI.Text(112f, Ink, 22, true);
            _clearTitle.text = "CLEAR";
            _rule = new VisualElement { pickingMode = PickingMode.Ignore };
            _rule.style.height = 2; _rule.style.width = 0; _rule.style.marginTop = 10; _rule.style.marginBottom = 16;
            _clearSub = SignalUI.Text(30f, Ink, 4, true);
            _clearSub.text = "CITADEL 격파  ·  임무 완료";
            _clearStats = SignalUI.Text(22f, Dim, 2, false);
            _clearStats.style.marginTop = 10;
            _clear.Add(_clearTitle); _clear.Add(_rule); _clear.Add(_clearSub); _clear.Add(_clearStats);
            root.Add(_clear);
        }

        void Animate(float now, float dt)
        {
            bool escOn = now - _escAt <= EscWindow && !_leaving && !_clearShown;
            _escA = Mathf.MoveTowards(_escA, escOn ? 1f : 0f, dt / (escOn ? 0.12f : 0.3f));
            _escLine.style.opacity = _escA;

            float shade = 0f;
            if (_clearShown)
            {
                float t = now - _clearAt;
                shade = 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f));
                _clear.style.opacity = Mathf.Clamp01(t / 0.15f);
                float snap = HuskFX.Snap(Mathf.Clamp01(t / 0.35f), 0.08f);               // the "쪼": in fast, a small overshoot, hard stop
                _clearTitle.style.scale = new Scale(Vector2.one * Mathf.Lerp(1.25f, 1f, snap));
                _rule.style.width = 560f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.2f) / 0.5f));
                float sub = Mathf.Clamp01((t - 0.45f) / 0.3f);
                _clearSub.style.opacity = sub;
                _clearStats.style.opacity = Mathf.Clamp01((t - 0.7f) / 0.3f);
            }
            _shade.style.opacity = Mathf.Max(shade, _fade);
        }
    }
}
