using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Zero-g character play test: Tab switches the controlled character, V first / third person (FlightCamera),
    /// F9 starts / stops a recording (fixed-step frames + GIF via pipeline/tools/frames_to_gif.py).
    /// Started with the command-line flag -oymDemo the scene plays a scripted demo instead (both characters, third and
    /// first person), records every take to Screenshots/play/demo_<take>/ (+ .gif) and quits the editor;
    /// -oymDemoTakes a,b limits it to some takes (kestrel_tp, kestrel_fp, orbit_tp, orbit_fp).
    [DefaultExecutionOrder(-50)]
    public class PlayTestDirector : MonoBehaviour
    {
        public ZeroGFlight[] controllers;
        public string[] names;
        /// co-op (OYM.Net): each player flies their own pilot, Tab does not swap
        public static bool SwitchLocked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSwitchLock() { SwitchLocked = false; }
        public FlightCamera flightCamera;
        public Vector3[] pivotOffsets;
        public Vector3[] eyeOffsets;
        public ZeroGTarget[] targets;

        [Header("Recording")]
        public int recordFps = 25;
        public int maxSeconds = 20;
        public int gifWidth = 720;
        public Vector2Int recordSize = new Vector2Int(1280, 720);

        int _active;
        bool _recording, _demo;
        string _dir, _status = "";
        int _frames;
        readonly List<System.Diagnostics.Process> _gifs = new List<System.Diagnostics.Process>();
        RenderTexture _rt;
        Texture2D _tex;
        Vector3[] _spawn;
        Quaternion[] _spawnRot;

        void Start()
        {
            _spawn = new Vector3[controllers.Length]; _spawnRot = new Quaternion[controllers.Length];
            for (int i = 0; i < controllers.Length; i++) { _spawn[i] = controllers[i].transform.position; _spawnRot[i] = controllers[i].transform.rotation; }
            Activate(0);
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-oymDemo") >= 0)
            {
                string only = null;
                int k = System.Array.IndexOf(args, "-oymDemoTakes");
                if (k >= 0 && k + 1 < args.Length) only = args[k + 1];
                StartCoroutine(Demo(only));
            }
        }

        void Activate(int i)
        {
            if (controllers == null || controllers.Length == 0) return;
            _active = (i + controllers.Length) % controllers.Length;
            for (int j = 0; j < controllers.Length; j++)
            {
                var c = controllers[j]; if (!c) continue;
                c.scripted = j != _active || _demo;
                c.scriptedInput = default;
            }
            if (flightCamera)
            {
                flightCamera.target = controllers[_active].transform;
                if (pivotOffsets != null && _active < pivotOffsets.Length) flightCamera.pivotOffset = pivotOffsets[_active];
                if (eyeOffsets != null && _active < eyeOffsets.Length) flightCamera.eyeOffset = eyeOffsets[_active];
                flightCamera.SnapToTarget();
            }
        }

        // F1 perf overlay
        bool _perf = true;
        float _fpsT, _fpsAcc, _fps, _worst, _worstAcc; int _fpsN;
        Unity.Profiling.ProfilerRecorder _tris, _batches;

        void OnEnable()
        {
            _tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            _batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
        }

        void OnDisable() { _tris.Dispose(); _batches.Dispose(); }

        void Perf()
        {
            float dt = Time.unscaledDeltaTime;
            _fpsAcc += dt; _fpsN++; _worstAcc = Mathf.Max(_worstAcc, dt); _fpsT += dt;
            if (_fpsT >= 0.5f) { _fps = _fpsN / _fpsAcc; _worst = _worstAcc * 1000f; _fpsT = _fpsAcc = _worstAcc = 0f; _fpsN = 0; }
        }

        void Update()
        {
            Perf();
            if (_demo) return;
            bool tab = false, rec = false, f1 = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null) { tab = k.tabKey.wasPressedThisFrame; rec = k.f9Key.wasPressedThisFrame; f1 = k.f1Key.wasPressedThisFrame; }
#else
            tab = Input.GetKeyDown(KeyCode.Tab); rec = Input.GetKeyDown(KeyCode.F9); f1 = Input.GetKeyDown(KeyCode.F1);
#endif
            if (f1) _perf = !_perf;
            if (tab && !_recording && !SwitchLocked) Activate(_active + 1);
            if (rec) { if (_recording) _recording = false; else StartCoroutine(Record(Stamp() + "_" + Name(_active), false)); }
        }

        string Name(int i) => names != null && i < names.Length ? names[i].Replace(' ', '_') : "char";
        static string Stamp() => System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        static string PlayDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/play"));

        // ------------------------------------------------------------------------------------------------ recording
        /// Records until _recording is cleared (or maxSeconds). offscreen: render the camera into a fixed-size target.
        IEnumerator Record(string take, bool offscreen)
        {
            _recording = true; _frames = 0;
            _dir = Path.Combine(PlayDir, take);
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, "frame_*.png")) File.Delete(f);
            Time.captureFramerate = recordFps;
            var cam = flightCamera ? flightCamera.Cam : Camera.main;
            if (offscreen)
            {
                if (!_rt) { _rt = new RenderTexture(recordSize.x, recordSize.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _rt.Create(); }
                if (!_tex) _tex = new Texture2D(recordSize.x, recordSize.y, TextureFormat.RGB24, false);
                cam.targetTexture = _rt;
            }
            int max = recordFps * maxSeconds;
            while (_recording && _frames < max)
            {
                yield return new WaitForEndOfFrame();
                if (!_recording) break;
                byte[] png;
                if (offscreen)
                {
                    var prev = RenderTexture.active; RenderTexture.active = _rt;
                    _tex.ReadPixels(new Rect(0, 0, recordSize.x, recordSize.y), 0, 0); _tex.Apply(false);
                    RenderTexture.active = prev; png = _tex.EncodeToPNG();
                }
                else { var t = ScreenCapture.CaptureScreenshotAsTexture(); png = t.EncodeToPNG(); Destroy(t); }
                File.WriteAllBytes(Path.Combine(_dir, $"frame_{_frames:0000}.png"), png);
                _frames++;
            }
            _recording = false;
            Time.captureFramerate = 0;
            if (offscreen) cam.targetTexture = null;
            _status = $"{_frames} frames -> {_dir}";
#if UNITY_EDITOR
            string tool = Path.GetFullPath(Path.Combine(Application.dataPath, "../../pipeline/tools/frames_to_gif.py"));
            if (File.Exists(tool) && _frames > 1)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("python", $"-I \"{tool}\" \"{_dir}\" \"{_dir}.gif\" {recordFps} {gifWidth}")
                { UseShellExecute = false, CreateNoWindow = true };
                try { _gifs.Add(System.Diagnostics.Process.Start(psi)); _status = $"GIF: {_dir}.gif ({_frames} frames)"; }
                catch (System.Exception e) { _status = "GIF tool failed: " + e.Message; }
            }
#endif
            Debug.Log("[PlayTest] " + _status);
        }

        // ------------------------------------------------------------------------------------------------ scripted demo
        sealed class Step { public float t; public System.Action a; }

        IEnumerator Demo(string only)
        {
            _demo = true;
            yield return null;
            var takes = new List<(string name, int who, bool fp, List<Step> steps, float len)>
            {
                ("kestrel_tp", 0, false, KestrelSteps(false), 9.2f),
                ("kestrel_fp", 0, true, KestrelSteps(true), 8.4f),
                ("orbit_tp", 1, false, OrbitSteps(false), 9.0f),
                ("orbit_fp", 1, true, OrbitSteps(true), 8.0f),
            };
            foreach (var tk in takes)
            {
                if (!string.IsNullOrEmpty(only) && !only.Contains(tk.name)) continue;
                Debug.Log("[PlayTest] demo take " + tk.name);
                _aimIdx = -1;
                ResetScene();
                Activate(tk.who);
                var c = controllers[tk.who];
                c.scripted = true;
                flightCamera.scripted = true;
                flightCamera.firstPerson = tk.fp;
                flightCamera.SetLook(0f, tk.fp ? 4f : 10f);
                flightCamera.SnapToTarget();
                PlaceTargets(c.transform.position, tk.who, tk.fp);
                for (int i = 0; i < 12; i++) yield return null;                      // settle (camera, TAA)
                StartCoroutine(Record("demo_" + tk.name, true));
                float t = 0f; int si = 0;
                while (t < tk.len)
                {
                    while (si < tk.steps.Count && tk.steps[si].t <= t) { tk.steps[si].a(); si++; }
                    yield return null;
                    t += Time.deltaTime / Mathf.Max(Time.timeScale, 1e-3f);           // demo clock in real (capture) time
                }
                _recording = false;
                yield return null; yield return null;
                c.scriptedInput = default;
                flightCamera.firstPerson = false;
            }
            foreach (var p in _gifs) { try { p.WaitForExit(60000); } catch { } }
            Debug.Log("[PlayTest] demo finished");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }

        void ResetScene()
        {
            Time.timeScale = 1f;
            for (int i = 0; i < controllers.Length; i++)
            {
                var c = controllers[i];
                c.transform.SetPositionAndRotation(_spawn[i], _spawnRot[i]);
                c.ResetMotion();
                c.scriptedInput = default;
            }
        }

        void PlaceTargets(Vector3 from, int who, bool fp)
        {
            if (targets == null) return;
            Vector3[] near = { new Vector3(0.15f, 1.15f, 2.5f), new Vector3(-1.2f, 2.2f, 7f), new Vector3(1.6f, 0.4f, 9f), new Vector3(-2.5f, 1.5f, 12f), new Vector3(2.8f, 2.8f, 14f) };
            Vector3[] far = { new Vector3(0.0f, 1.5f, 13f), new Vector3(-2.2f, 2.6f, 16f), new Vector3(2.4f, 0.9f, 18f), new Vector3(-0.6f, 4.0f, 22f), new Vector3(3.5f, 3.5f, 25f) };
            var set = who == 0 ? near : far;
            for (int i = 0; i < targets.Length; i++) if (targets[i]) targets[i].Hold(from + set[i % set.Length]);
        }

        FlightInput In { get => controllers[_active].scriptedInput; set => controllers[_active].scriptedInput = value; }
        void Move(Vector3 m) { var i = In; i.move = m; In = i; }
        void Press(System.Func<FlightInput, FlightInput> f) { In = f(In); }
        static Step S(float t, System.Action a) => new Step { t = t, a = a };

        List<Step> KestrelSteps(bool fp)
        {
            var s = new List<Step>();
            if (!fp)
            {
                s.Add(S(0.9f, () => Move(new Vector3(0.6f, 0.5f, 0.6f))));          // climb out to the right: bank + lean
                s.Add(S(2.0f, () => Move(Vector3.zero)));                           // let go: drift
                s.Add(S(2.1f, () => AimAt(0, 5f)));
                s.Add(S(2.9f, () => { Move(new Vector3(0f, -0.2f, 1f)); Press(i => { i.dash = true; return i; }); }));   // dash at the targets
                s.Add(S(3.25f, () => Move(Vector3.zero)));
                s.Add(S(3.8f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(4.35f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(5.4f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(7.0f, () => { Move(new Vector3(-1f, 0.3f, 0f)); Press(i => { i.dash = true; return i; }); }));   // side dash out
                s.Add(S(7.3f, () => Move(Vector3.zero)));
            }
            else
            {
                s.Add(S(0.3f, () => AimAt(0, 4f)));
                s.Add(S(1.4f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(1.9f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(3.2f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(5.0f, () => AimAt(1, 6f)));
                s.Add(S(5.6f, () => { Move(new Vector3(0f, 0f, 1f)); Press(i => { i.dash = true; return i; }); }));
                s.Add(S(5.9f, () => Move(Vector3.zero)));
                s.Add(S(6.6f, () => Press(i => { i.fire = true; return i; })));
            }
            return s;
        }

        List<Step> OrbitSteps(bool fp)
        {
            var s = new List<Step>();
            if (!fp)
            {
                s.Add(S(0.8f, () => Move(new Vector3(-0.7f, 0.5f, 0.3f))));         // strafe left and up: roll + lean
                s.Add(S(1.9f, () => Move(Vector3.zero)));
                s.Add(S(2.2f, () => { AimAt(0, 7f); Press(i => { i.aim = true; return i; }); }));
                s.Add(S(2.8f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(4.1f, () => AimAt(1, 8f)));
                s.Add(S(4.5f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(5.7f, () => { Press(i => { i.aim = false; return i; }); Move(new Vector3(1f, 0f, 0.2f)); Press(i => { i.dash = true; return i; }); }));
                s.Add(S(6.0f, () => Move(Vector3.zero)));
                s.Add(S(6.9f, () => { Press(i => { i.aim = true; return i; }); AimAt(2, 8f); }));
                s.Add(S(7.4f, () => Press(i => { i.fire = true; return i; })));
            }
            else
            {
                s.Add(S(0.4f, () => AimAt(0, 5f)));
                s.Add(S(1.3f, () => Press(i => { i.aim = true; return i; })));
                s.Add(S(1.7f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(2.9f, () => AimAt(1, 8f)));
                s.Add(S(3.3f, () => Press(i => { i.fire = true; return i; })));
                s.Add(S(4.6f, () => { Press(i => { i.aim = false; return i; }); Move(new Vector3(1f, 0.2f, 0f)); Press(i => { i.dash = true; return i; }); }));
                s.Add(S(4.9f, () => Move(Vector3.zero)));
                s.Add(S(5.6f, () => AimAt(2, 8f)));
                s.Add(S(6.1f, () => Press(i => { i.fire = true; return i; })));
            }
            return s;
        }

        void LookTo(float yaw, float pitch, float seconds) { _aimIdx = -1; StartCoroutine(LookRoutine(yaw, pitch, seconds)); }

        int _aimIdx = -1;
        float _aimRate;
        /// Track target i with the scripted camera (exponential approach, `rate` per second) until another look command.
        void AimAt(int i, float rate) { _aimIdx = i; _aimRate = rate; }

        void LateUpdate()
        {
            if (!_demo || _aimIdx < 0 || targets == null || _aimIdx >= targets.Length || !targets[_aimIdx] || !flightCamera) return;
            Vector3 d = targets[_aimIdx].transform.position - flightCamera.transform.position;
            if (d.sqrMagnitude < 0.01f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg;
            float k = 1f - Mathf.Exp(-_aimRate * GameFeel.RealDt);
            flightCamera.SetLook(Mathf.LerpAngle(flightCamera.Yaw, yaw, k), Mathf.Lerp(flightCamera.Pitch, pitch, k));
        }

        IEnumerator LookRoutine(float yaw, float pitch, float seconds)
        {
            float y0 = flightCamera.Yaw, p0 = flightCamera.Pitch, t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime / Mathf.Max(Time.timeScale, 1e-3f);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                flightCamera.SetLook(Mathf.LerpAngle(y0, yaw, k), Mathf.Lerp(p0, pitch, k));
                yield return null;
            }
        }

        string _help, _helpFor;

        void OnGUI()
        {
            if (_recording || _demo)
            {
                if (_recording && !_demo) { GUI.color = Color.red; GUI.Label(new Rect(12, 8, 200, 24), "● REC " + (_frames / (float)recordFps).ToString("0.0") + "s"); }
                return;
            }
            string who = names != null && _active < names.Length ? names[_active] : "";
            if (!ReferenceEquals(who, _helpFor))
            {
                // built once per pilot switch, not on every OnGUI event (OnGUI runs several times a frame)
                string atk = who == "ORBIT FRAME" ? "RMB aim · LMB fire" : "LMB slash (click again to chain) · Q stance";
                _help = $"[{who}]  WASD fly · Space/Ctrl up/down · Shift dash (hold = cruise, no enemies) · {atk} · V 1st/3rd person · Tab switch · F9 record · click = mouse look, Esc = release";
                _helpFor = who;
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(12, 8, 1400, 24), _help);
            if (_status.Length > 0) GUI.Label(new Rect(12, 30, 1200, 24), _status);
            if (_perf)
                GUI.Label(new Rect(12, 52, 900, 24), $"{_fps:0} fps  ({1000f / Mathf.Max(_fps, 1f):0.0} ms avg, worst {_worst:0} ms)  ·  " +
                          $"{_tris.LastValue / 1000:0}k tris  ·  {_batches.LastValue} batches  ·  F1 hide");
            if (flightCamera && flightCamera.firstPerson)
            {
                float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;           // crosshair
                GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 6), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1, cy + 3, 2, 6), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 9, cy - 1, 6, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + 3, cy - 1, 6, 2), Texture2D.whiteTexture);
            }
        }
    }
}
