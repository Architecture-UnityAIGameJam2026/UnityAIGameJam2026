using System.Collections;
using System.IO;
using System.Reflection;
using OYM.Cine;
using OYM.Net;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Field
{
    /// Test aid: with -fieldTest <folder> the game scene checks the field kit on its own and quits the editor:
    /// skips the arrival cutscene, waits for the crossed visor, holds the HUSK (F10), hurts the pilot and flies it
    /// through a heal pack, passes a pack at full HP, then calibrates every link relay in order; JPGs of each moment
    /// and field_log.txt (results + the [OYM-FIELD] / [OYM-HUD] lines and any error) land in the folder. Nothing
    /// happens without the flag.
    public sealed class FieldKitTest : MonoBehaviour
    {
        public static bool Asked => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fieldTest") >= 0;

        string _dir;
        StreamWriter _log;
        int _shot, _fails;
        static FieldInfo s_vel;

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        void Awake()
        {
            Application.runInBackground = true;                    // an unfocused editor must keep playing
            _dir = Path.GetFullPath(Arg("-fieldTest") ?? "fieldtest");
            Directory.CreateDirectory(_dir);
            _log = new StreamWriter(Path.Combine(_dir, "field_log.txt"), false) { AutoFlush = true };
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            _log?.Dispose(); _log = null;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (_log == null) return;
            if (type == LogType.Error || type == LogType.Exception || msg.StartsWith("[OYM-FIELD]") || msg.StartsWith("[OYM-HUD]"))
                _log.WriteLine($"{Time.realtimeSinceStartup,8:F2} {type,-9} {msg}" + (type == LogType.Exception ? "\n" + stack : ""));
            if (type == LogType.Exception) _fails++;
        }

        void Note(string s) { _log?.WriteLine($"{Time.realtimeSinceStartup,8:F2} TEST      {s}"); }
        void Check(bool ok, string what) { if (!ok) _fails++; Note((ok ? "PASS " : "FAIL ") + what); }

        IEnumerator Start()
        {
            Note("field kit test: " + SceneManager.GetActiveScene().name);
            // the arrival cutscene: skip it once it runs (the visor crosses 1.3 s after it hands back)
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 25f)
            {
                var c = ArrivalCutscene.Current;
                if (c && c.Running) { yield return new WaitForSecondsRealtime(2f); c.RequestSkip(); Note("arrival cutscene skipped"); break; }
                yield return null;
            }
            t0 = Time.unscaledTime;
            while (!(CoopHud.Current && CoopHud.Current.Crossed) && Time.unscaledTime - t0 < 45f) yield return null;
            Check(CoopHud.Current && CoopHud.Current.Crossed, "visor crossed after the hand-back");
            var kit = FieldKit.Current;
            var e = HuskEncounter.Current;
            Check(kit && kit.Relays.Count == 3 && kit.Packs.Count == 8, $"placed {(kit ? kit.Relays.Count : 0)} relays, {(kit ? kit.Packs.Count : 0)} packs");
            Check(e && e.config && e.config.playerHP >= 150f, $"pilot max HP {(e && e.config ? e.config.playerHP : 0f)}");
            if (!kit || !e) { yield return Quit(); yield break; }
            e.hostileEnabled = false;                              // the HUSK hold: this is about the kit
            yield return WaitView(20f);
            yield return Shot("crossed");

            // heal: hurt the pilot, put it next to pack 0, then into it
            e.Engaged = true;
            Pin(true);
            var pack = kit.Packs[0];
            Vector3 side = Vector3.ProjectOnPlane(Camera.main ? Camera.main.transform.right : Vector3.right, Vector3.up).normalized;
            PutChest(pack.Center - side * 5f + Vector3.up * 0.4f);
            yield return WaitView(10f);
            e.DamagePlayer(80f, pack.Center + Vector3.up * 3f, 0f);
            float hurt = e.PlayerHP01;
            yield return new WaitForSeconds(0.7f);
            Look(pack.Center);
            yield return Shot("pack_before");
            PutChest(pack.Center);
            yield return new WaitForSeconds(0.25f);
            float healed = e.PlayerHP01;
            Check(!pack.Available && healed > hurt + 0.3f, $"heal pack 0: HP {hurt * 100f:F0}% -> {healed * 100f:F0}% (pack gone {!pack.Available})");
            PutChest(pack.Center - side * 4f);
            Look(pack.Center);
            yield return new WaitForSeconds(0.25f);
            yield return Shot("pack_taken");

            // at full HP a pack stays
            e.Heal(999f);
            var full = kit.Packs[1];
            PutChest(full.Center - side * 4f);
            yield return new WaitForSeconds(0.2f);
            PutChest(full.Center);
            yield return new WaitForSeconds(0.4f);
            Check(full.Available, "heal pack 1 stays at full HP");
            PutChest(full.Center - side * 4f);
            Look(full.Center);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("pack_full_hp");

            // relays in route order: approach (outside reach), calibrating, done
            var hud = CoopHud.Current;
            for (int i = 0; i < kit.Relays.Count; i++)
            {
                var r = kit.Relays[i];
                Vector3 away = Vector3.ProjectOnPlane(r.transform.forward, Vector3.up).normalized;
                if (away.sqrMagnitude < 0.1f) away = -side;
                PutChest(r.lamp + away * (kit.config.relayRadius + 4f) + Vector3.up * 0.5f);
                Look(r.lamp);
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"relay{i}_idle");
                PutChest(r.lamp + away * 3.2f - Vector3.up * 0.4f);
                Look(r.transform.position);
                yield return new WaitForSeconds(kit.config.relayHold * 0.55f);
                Check(r.Progress > 0.2f && !r.Done, $"relay {i} calibrating ({r.Progress:P0})");
                yield return Shot($"relay{i}_calibrating");
                float tw = Time.time;
                while (!r.Done && Time.time - tw < kit.config.relayHold + 2f) yield return null;
                bool back = true;
                foreach (int m in r.step.modules) back &= hud && hud.IsBack(m);
                Check(r.Done && back, $"relay {i} done, modules {string.Join("+", r.step.modules)} back on the visor");
                yield return new WaitForSeconds(0.45f);
                yield return Shot($"relay{i}_done");
                PutChest(r.lamp + away * (kit.config.relayRadius + 4f) + Vector3.up * 0.5f);
                Look(r.lamp);
                yield return new WaitForSeconds(1.2f);
                yield return Shot($"relay{i}_after");
            }
            Check(hud && hud.IsBack(0) && hud.IsBack(1) && hud.IsBack(2) && hud.IsBack(3), "every visor module back after the last relay");

            // pack 0 comes back after the respawn time
            float wait = pack.BackIn + 0.6f;
            Note($"waiting {wait:F1} s for pack 0");
            yield return new WaitForSeconds(wait);
            Check(pack.Available, "heal pack 0 back after the respawn time");
            PutChest(pack.Center - side * 4.5f);
            Look(pack.Center);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("pack_back");
            Note(_fails == 0 ? "RESULT OK" : $"RESULT {_fails} FAIL(S)");
            yield return Quit();
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static ZeroGFlight Pilot() { var fc = FlightCamera.Main; return fc && fc.target ? fc.target.GetComponent<ZeroGFlight>() : null; }

        /// the pilot holds still (no input; level cutaways restore their own flags, so this is set again before moves)
        static void Pin(bool on) { var f = Pilot(); if (f) { f.scripted = on; f.scriptedInput = default; } }

        static void PutChest(Vector3 chest)
        {
            var f = Pilot();
            if (!f) return;
            f.scripted = true; f.scriptedInput = default;
            f.transform.position = chest - Vector3.up * (f.bodyCenter + 0.15f);
            if (s_vel == null) s_vel = typeof(ZeroGFlight).GetField("velocity", BindingFlags.NonPublic | BindingFlags.Instance);
            s_vel?.SetValue(f, Vector3.zero);
        }

        /// turn the pilot (and so the follow camera) toward a point
        static void Look(Vector3 p)
        {
            var f = Pilot();
            if (!f) return;
            Vector3 d = Vector3.ProjectOnPlane(p - f.transform.position, Vector3.up);
            if (d.sqrMagnitude > 0.01f) f.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        IEnumerator WaitView(float max)
        {
            float t0 = Time.unscaledTime;
            while (!SignalInstall.PlayerView(HuskEncounter.Current) && Time.unscaledTime - t0 < max) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (!tex) yield break;
            var outTex = tex;
            if (tex.width > 1600)
            {
                var rt = RenderTexture.GetTemporary(tex.width / 2, tex.height / 2, 0);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                outTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                outTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); outTex.Apply(false);
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                Destroy(tex);
            }
            File.WriteAllBytes(Path.Combine(_dir, $"{_shot++:00}_{name}.jpg"), outTex.EncodeToJPG(85));
            Destroy(outTex);
            Note("shot " + name);
        }

        IEnumerator Quit()
        {
            yield return new WaitForSecondsRealtime(1f);
            _log?.Flush();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_fails == 0 ? 0 : 1);
#else
            Application.Quit(_fails == 0 ? 0 : 1);
#endif
        }
    }
}
