using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OnYourMark.UI;
using OYM.Cine;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Net
{
    /// Test aid: with -minimapTest <folder> the game scene checks the visor's live minimap on its own and quits the
    /// editor: skips the arrival cutscene, waits for the crossed visor, holds the HUSK (F10), puts five sleeping test
    /// HUSK at known spots round the pilot (ahead, right + above, behind-left + below, left, far off) and turns the
    /// view three ways - each mark must sit where the camera's own axes put it (a few px) - then wakes them facing
    /// the pilot, lets them loose for a moment, breaks the HUD (heavy hit glitch) and calls the CITADEL. JPGs (full
    /// frame + the top-left corner at full size) and minimap_log.txt (PASS / FAIL + [OYM-MAP] / [OYM-HUD] lines and any
    /// error) land in the folder. Nothing happens without the flag.
    public sealed class VisorMinimapTest : MonoBehaviour
    {
        const string RanKey = "OYM.VisorMinimapTest.ran";

        /// Only the first play of an editor started with -minimapTest (OYMShowcase.OpenAndPlay), and never a play that
        /// came through the title: someone pressing Play in that window afterwards gets the normal game.
        public static bool Asked
        {
            get
            {
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-minimapTest") < 0) return false;
                if (!string.IsNullOrEmpty(GameSession.SelectedPilot)) return false;
#if UNITY_EDITOR
                if (UnityEditor.SessionState.GetBool(RanKey, false)) return false;
#endif
                return true;
            }
        }

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
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool(RanKey, true);        // one run per editor session
#endif
            Application.runInBackground = true;                    // an unfocused editor must keep playing
            _dir = Path.GetFullPath(Arg("-minimapTest") ?? "minimaptest");
            Directory.CreateDirectory(_dir);
            _log = new StreamWriter(Path.Combine(_dir, "minimap_log.txt"), false) { AutoFlush = true };
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
            if (type == LogType.Error || type == LogType.Exception || msg.StartsWith("[OYM-MAP]") || msg.StartsWith("[OYM-HUD]"))
                _log.WriteLine($"{Time.realtimeSinceStartup,8:F2} {type,-9} {msg}" + (type == LogType.Exception ? "\n" + stack : ""));
            if (type == LogType.Exception) _fails++;
        }

        void Note(string s) { _log?.WriteLine($"{Time.realtimeSinceStartup,8:F2} TEST      {s}"); }
        void Check(bool ok, string what) { if (!ok) _fails++; Note((ok ? "PASS " : "FAIL ") + what); }

        static VisorMinimap Map()
        {
            foreach (var m in FindObjectsByType<VisorMinimap>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (m.Live) return m;
            return null;
        }

        IEnumerator Start()
        {
            Note("minimap test: " + SceneManager.GetActiveScene().name);
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 25f)
            {
                var c = ArrivalCutscene.Current;
                if (c && c.Running) { yield return new WaitForSecondsRealtime(2f); c.RequestSkip(); Note("arrival cutscene skipped"); break; }
                yield return null;
            }
            t0 = Time.unscaledTime;
            while (!(CoopHud.Current && CoopHud.Current.Crossed) && Time.unscaledTime - t0 < 45f) yield return null;
            Check(CoopHud.Current && CoopHud.Current.Crossed, "visor up and crossed");
            var map = Map();
            Check(map, "live minimap under the Spatial module");
            var hud = map ? map.GetComponentInParent<OnYourMarkHud>() : null;
            var street = hud ? hud.transform.Find(VisorMinimap.ModuleName + "/Street west") : null;
            var disc = hud ? hud.transform.Find(VisorMinimap.ModuleName + "/Map disc") : null;
            Check(street && !street.gameObject.activeSelf, "garam mock streets off");
            Check(disc && disc.gameObject.activeInHierarchy, $"round map disc kept (HudArrangement), circle r {(map ? map.circleRadius : 0f):F0} px");
            var e = HuskEncounter.Current;
            var fc = FlightCamera.Main;
            if (!map || !e || !fc) { yield return Quit(); yield break; }
            yield return WaitView(30f);
            yield return new WaitForSeconds(1.2f);
            yield return WaitView(15f);
            Check(map.Showing && map.Owner == "self", $"single-player map centred on this pilot (owner '{map.Owner}', {map.Count} marks)");
            yield return Shot("start");

            // five sleeping test HUSK at known spots round the pilot, relative to the current view
            e.hostileEnabled = false;
            e.Engaged = true;
            Pin();
            yield return WaitView(15f);
            float yaw0 = fc.Yaw;
            Vector3 chest = Chest();
            Quaternion q = Quaternion.Euler(0f, yaw0, 0f);
            var spots = new[]
            {
                ("ahead 30 m", new Vector3(0f, 0f, 30f)),
                ("right 45 m, 18 m up", new Vector3(45f, 18f, 0f)),
                ("behind-left 40 m, 16 m down", new Vector3(-28f, -16f, -28f)),
                ("left 22 m", new Vector3(-22f, 0f, 0f)),
                ("far ahead-right 220 m", new Vector3(150f, 0f, 160f)),
            };
            var dummies = new List<HuskEnemy>();
            var worlds = new List<Vector3>();
            foreach (var (name, local) in spots)
            {
                Vector3 p = chest + q * local;
                Vector3 toPilot = Vector3.ProjectOnPlane(chest - p, Vector3.up);
                var en = e.Spawn(HuskEnemy.Kind.Wick, p, Quaternion.LookRotation(toPilot.sqrMagnitude > 0.01f ? toPilot.normalized : Vector3.forward, Vector3.up), -1f);
                en.name = "minimap test " + name;
                dummies.Add(en); worlds.Add(p);
            }
            Note($"spawned {dummies.Count} test HUSK round the pilot (view yaw {yaw0:F0})");

            float[] turns = { 0f, 90f, -135f };
            foreach (float turn in turns)
            {
                fc.SetLook(yaw0 + turn, 0f);
                Pin();
                yield return null; yield return null; yield return null;
                yield return WaitView(10f);
                int ok = 0;
                for (int i = 0; i < worlds.Count; i++) ok += Where(map, fc, worlds[i], spots[i].Item1, turn) ? 1 : 0;
                Check(ok == worlds.Count, $"view turned {turn:+0;-0;0} deg: {ok}/{worlds.Count} test marks where the camera puts them");
                yield return Shot($"turn{(int)turn}");
                if (turn == 0f)
                {
                    Note("diag live map: " + map.Diagnose());
                    var rim = hud.transform.Find(VisorMinimap.ModuleName + "/Map rim")?.GetComponent<UnityEngine.UI.Graphic>();
                    if (rim)
                    {
                        var rr = rim.canvasRenderer;
                        Note($"diag map rim: canvas '{(rim.canvas ? rim.canvas.name : "-")}' cull {rr.cull} alpha {rr.GetInheritedAlpha():F2} mats {rr.materialCount} depth {rr.absoluteDepth} centre world {rim.rectTransform.TransformPoint(new Vector3(map.circleCenter.x, -map.circleCenter.y, 0f)).ToString("F3")}");
                    }
                    var cr = map.canvasRenderer;
                    Check(cr && !cr.cull && cr.materialCount > 0 && map.LastVerts > 0, $"live map drawn: {map.LastVerts} vertices on the visor canvas");
                }
            }
            fc.SetLook(yaw0, 0f);
            yield return null; yield return null;
            Check(Elev(map, fc, worlds[1]) > 0 && Elev(map, fc, worlds[2]) < 0 && Elev(map, fc, worlds[0]) == 0,
                  $"height carets: above {Elev(map, fc, worlds[1])}, below {Elev(map, fc, worlds[2])}, level {Elev(map, fc, worlds[0])}");

            // awake, facing the pilot: bright triangles pointing at the centre
            foreach (var en in dummies) if (en) en.WakeIn(0f);
            yield return new WaitForSeconds(0.6f);
            Pin();
            yield return WaitView(10f);
            int inward = 0, near = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!dummies[i]) continue;
                int b = Nearest(map, Expected(map, fc, dummies[i].transform.position), out float d);
                if (b < 0 || d > 3f) continue;
                near++;
                Vector2 off = map.Offset(b);
                if (off.sqrMagnitude > 1f && Vector2.Dot(map.Direction(b), -off.normalized) > 0.8f) inward++;
            }
            Check(near == 4 && inward == 4, $"awake test HUSK facing the pilot: {near}/4 on the map, {inward}/4 pointing at the centre");
            yield return Shot("awake");

            // loose for a moment: they come for the pilot
            e.hostileEnabled = true;
            yield return new WaitForSeconds(1.6f);
            yield return WaitView(8f);
            yield return Shot("loose");
            e.hostileEnabled = false;

            // the heavy hit glitch tears the map with the rest of the visor
            HudBreak.Hit(0.32f);
            yield return new WaitForSecondsRealtime(0.09f);
            yield return Shot("break_glitch");
            yield return new WaitForSecondsRealtime(1.6f);
            foreach (var en in dummies) if (en && en.Alive) en.TakeDamage(9999f, Vector3.up, 0.5f);
            yield return new WaitForSeconds(1f);

            // the CITADEL: called like the level does (F11's shortcut), mark while its fight is on
            var lv = e.Level;
            var start = typeof(HuskLevel).GetMethod("StartBoss", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lv && start != null && e.boss)
            {
                start.Invoke(lv, null);
                Note("CITADEL called");
                t0 = Time.unscaledTime;
                while (!e.boss.Done && Time.unscaledTime - t0 < 40f) yield return null;
                t0 = Time.unscaledTime;
                while (!map.BossShown && Time.unscaledTime - t0 < 20f) yield return null;
                yield return new WaitForSeconds(1.5f);
                yield return WaitView(20f);
                yield return new WaitForSeconds(0.5f);
                int boss = -1;
                for (int i = 0; i < map.Count; i++) if (map.IsBoss(i)) boss = i;
                Check(map.BossShown && boss >= 0, $"CITADEL mark during its fight (at {(boss >= 0 ? map.Offset(boss).ToString("F0") : "-")})");
                yield return Shot("boss");
                fc.SetLook(fc.Yaw + 120f, 0f);
                yield return null; yield return null;
                yield return Shot("boss_turned");
            }
            else Note("no level / boss here: CITADEL check skipped");

            Note(_fails == 0 ? "RESULT OK" : $"RESULT {_fails} FAIL(S)");
            yield return Quit();
        }

        // ------------------------------------------------------------------------------------------------ checks
        /// where the mark of a HUSK at `world` must be, from the camera's own axes (not the map's yaw)
        static Vector2 Expected(VisorMinimap map, FlightCamera fc, Vector3 world)
        {
            var ct = fc.Cam ? fc.Cam.transform : fc.transform;
            Vector3 r = Vector3.ProjectOnPlane(ct.right, Vector3.up).normalized, f = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
            Vector3 d = world - Chest();
            Vector2 m = new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f)) * map.PixelsPerMeter;
            float rim = map.RimRadius;
            return m.sqrMagnitude > rim * rim ? m.normalized * rim : m;
        }

        static int Nearest(VisorMinimap map, Vector2 at, out float dist)
        {
            int best = -1; dist = float.MaxValue;
            for (int i = 0; i < map.Count; i++)
            {
                if (map.IsBoss(i)) continue;
                float d = Vector2.Distance(map.Offset(i), at);
                if (d < dist) { dist = d; best = i; }
            }
            return best;
        }

        bool Where(VisorMinimap map, FlightCamera fc, Vector3 world, string name, float turn)
        {
            Vector2 exp = Expected(map, fc, world);
            int i = Nearest(map, exp, out float d);
            bool ok = i >= 0 && d <= 3f;
            Note($"   {name,-30} expect ({exp.x,6:F1},{exp.y,6:F1})  mark {(i >= 0 ? $"({map.Offset(i).x,6:F1},{map.Offset(i).y,6:F1})" : "none")}  off {d:F1} px {(ok ? "" : "<-- WRONG")}");
            return ok;
        }

        static int Elev(VisorMinimap map, FlightCamera fc, Vector3 world)
        {
            int i = Nearest(map, Expected(map, fc, world), out float d);
            return i >= 0 && d <= 3f ? map.Elevation(i) : -99;
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static ZeroGFlight Pilot() { var fc = FlightCamera.Main; return fc && fc.target ? fc.target.GetComponent<ZeroGFlight>() : null; }

        static Vector3 Chest() { var f = Pilot(); return f ? f.transform.position + Vector3.up * (f.bodyCenter + 0.15f) : Vector3.zero; }

        /// the pilot holds still (no input, no drift)
        static void Pin()
        {
            var f = Pilot();
            if (!f) return;
            f.scripted = true; f.scriptedInput = default;
            if (s_vel == null) s_vel = typeof(ZeroGFlight).GetField("velocity", BindingFlags.NonPublic | BindingFlags.Instance);
            s_vel?.SetValue(f, Vector3.zero);
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
            // the top-left corner at full size (the minimap + the objective under it)
            int cw = Mathf.RoundToInt(tex.width * 0.2f), ch = Mathf.RoundToInt(tex.height * 0.4f);
            var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            crop.SetPixels(tex.GetPixels(0, tex.height - ch, cw, ch));
            crop.Apply(false);
            File.WriteAllBytes(Path.Combine(_dir, $"{_shot:00}_{name}_map.png"), crop.EncodeToPNG());
            Destroy(crop);
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
