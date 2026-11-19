using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Net
{
    /// Test aid: with -coopShots <folder> the co-op session saves a JPG of the screen every `every` seconds (title,
    /// curtain, cutscene, game) plus one per noteworthy moment, and writes the game's [OYM-COOP] / [OYM-HUD] log lines
    /// next to them, so a two-instance run can be checked afterwards. Nothing happens without the flag.
    public sealed class CoopShots : MonoBehaviour
    {
        string _dir;
        int _n;
        float _every = 1.5f, _next, _quitAt = -1f, _bossAfter = -1f, _bossAt = -1f;
        bool _wantFp;
        StreamWriter _log;

        public static void InstallIfAsked(GameObject host)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-coopShots");
            if (i < 0 || i + 1 >= args.Length || host.GetComponent<CoopShots>()) return;
            var s = host.AddComponent<CoopShots>();
            s._dir = Path.GetFullPath(args[i + 1]);
            int j = System.Array.IndexOf(args, "-coopShotEvery");
            if (j >= 0 && j + 1 < args.Length && float.TryParse(args[j + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float e)) s._every = Mathf.Max(0.3f, e);
            // -coopQuitAfter <s>: a test run ends itself (the editor exits) this long after the room opened
            int q = System.Array.IndexOf(args, "-coopQuitAfter");
            if (q >= 0 && q + 1 < args.Length && float.TryParse(args[q + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float qa)) s._quitAt = Time.unscaledTime + qa;
            // -coopCallBoss <s>: the host calls the CITADEL this long after the game scene came in (F11's shortcut)
            // -coopFirstPerson: fly in first person (V) once the game scene is in
            s._wantFp = System.Array.IndexOf(args, "-coopFirstPerson") >= 0;
            int b = System.Array.IndexOf(args, "-coopCallBoss");
            if (b >= 0 && b + 1 < args.Length && float.TryParse(args[b + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ba)) s._bossAfter = ba;
        }

        void OnEnable() { SceneManager.sceneLoaded += OnScene; }
        void OnDisable() { SceneManager.sceneLoaded -= OnScene; }
        void OnScene(Scene sc, LoadSceneMode m) { if (sc.name == ShowcaseScene.Name && _bossAfter >= 0f) _bossAt = Time.unscaledTime + _bossAfter; }

        static System.Reflection.MethodInfo s_startBoss;

        void CallBoss()
        {
            var e = HuskEncounter.Current;
            var lv = e ? e.Level : null;
            if (!CoopSession.Active || !CoopSession.Active.IsHost || !lv) { _bossAt = -1f; return; }
            if (!e.Gate || lv.InCutaway || lv.beat == HuskLevel.Beat.Boss || lv.beat == HuskLevel.Beat.Victory) { _bossAt = Time.unscaledTime + 1f; return; }
            e.Engaged = true;
            if (s_startBoss == null) s_startBoss = typeof(HuskLevel).GetMethod("StartBoss", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            s_startBoss?.Invoke(lv, null);
            _bossAt = -1f;
            Debug.Log("[OYM-COOP] test: CITADEL called (-coopCallBoss)");
        }

        void Update()
        {
            if (_bossAt > 0f && Time.unscaledTime >= _bossAt) CallBoss();
            if (_wantFp && FlightCamera.Main && SceneManager.GetActiveScene().name == ShowcaseScene.Name && !FlightCamera.Main.firstPerson)
            {
                FlightCamera.Main.firstPerson = true;
                Debug.Log("[OYM-COOP] test: first person (-coopFirstPerson)");
                _wantFp = false;
            }
            if (_quitAt < 0f || Time.unscaledTime < _quitAt) return;
            _quitAt = -1f;
            Debug.Log("[OYM-COOP] test run over (-coopQuitAfter): quitting");
            _log?.Flush();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        void Start()
        {
            Directory.CreateDirectory(_dir);
            _log = new StreamWriter(Path.Combine(_dir, "coop_log.txt"), true) { AutoFlush = true };
            _log.WriteLine($"---- {System.DateTime.Now:HH:mm:ss} co-op shots, every {_every:F1} s");
            Application.logMessageReceived += OnLog;
            StartCoroutine(Loop());
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            _log?.Dispose(); _log = null;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (_log == null) return;
            if (type == LogType.Error || type == LogType.Exception || msg.StartsWith("[OYM-COOP]") || msg.StartsWith("[OYM-HUD]") || msg.StartsWith("[OYM-HUSK]") || msg.StartsWith("[OYM-CINE]"))
                _log.WriteLine($"{Time.realtimeSinceStartup,8:F2} {SceneManager.GetActiveScene().name,-22} {type,-9} {msg}" + (type == LogType.Exception ? "\n" + stack : ""));
        }

        IEnumerator Loop()
        {
            var end = new WaitForEndOfFrame();
            while (_n < 600)
            {
                yield return end;
                if (Time.unscaledTime < _next) continue;
                _next = Time.unscaledTime + _every;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (!tex) continue;
                var scaled = tex;
                if (tex.width > 1280)
                {
                    // half size keeps a long run small
                    var rt = RenderTexture.GetTemporary(tex.width / 2, tex.height / 2, 0);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    scaled = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    scaled.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); scaled.Apply(false);
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    Destroy(tex);
                }
                File.WriteAllBytes(Path.Combine(_dir, $"shot_{_n:0000}_{SceneManager.GetActiveScene().name}.jpg"), scaled.EncodeToJPG(82));
                Destroy(scaled);
                _n++;
            }
        }
    }
}
