// OnYourMark - scripted play-mode run of the collision event for recordings (only with the command-line flag -oymEventDemo):
//   Unity.exe -projectPath <proj> -executeMethod OYM.EditorTools.OYMCollisionEvent.OpenAndPlay -oymEventDemo
// Take "cinematic": the player waits at the viewing spot and the camera follows the beats (impact -> HUSK -> path).
// Take "lane": an autopilot flies the dash lane and slides / dashes into the open slot of the next wave.
// Frames go to Screenshots/event_collision/play_<take>/frame_0000.png (25 fps capture clock, 1280x720), then the editor quits.
using System.Collections;
using System.IO;
using UnityEngine;

namespace OYM
{
    public class CollisionEventDemo : MonoBehaviour
    {
        public CollisionEvent ev;
        public int fps = 25;
        public Vector2Int size = new Vector2Int(1280, 720);

        bool _rec;
        int _frames;
        RenderTexture _rt;
        Texture2D _tex;
        string _log = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymEventDemo") < 0) return;
            var ev = FindFirstObjectByType<CollisionEvent>();
            if (!ev) return;
            new GameObject("CollisionEventDemo (scripted)").AddComponent<CollisionEventDemo>().ev = ev;
        }

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/event_collision"));

        IEnumerator Start()
        {
            for (int i = 0; i < 20; i++) yield return null;                     // PlayTestDirector picks the active character first
            var fc = FlightCamera.Main;
            var pl = fc ? fc.target : null;
            var flight = pl ? pl.GetComponent<ZeroGFlight>() : null;
            if (!fc || !flight) { Debug.LogError("[OYM-EV-DEMO] no player / flight camera"); Quit(1); yield break; }
            fc.scripted = true; flight.scripted = true; flight.scriptedInput = default;
            Time.captureFramerate = fps;
            var T = ev.transform;

            // ---- take 1: the event from the viewing spot
            ev.Replay(pl);
            for (int i = 0; i < 2; i++) yield return null;
            StartCoroutine(Record("play_cinematic"));
            Vector3 eye = ev.viewPoint, C = ev.collisionPoint, pathMid = new Vector3(20f, 3f, 50f), formation = new Vector3(-12f, 10f, 55f);
            float end = ev.pathReadyTime + 1.6f;
            int lastHalf = -1;
            while (ev.Clock < end)
            {
                float t = ev.Clock;
                // aim: collision -> HUSK formation -> the path as it forms
                Vector3 look = C;
                if (t > ev.enemiesFrom + 0.8f) look = Vector3.Lerp(C, formation, Mathf.SmoothStep(0f, 1f, (t - ev.enemiesFrom - 0.8f) / 1.6f));
                if (t > ev.pathFrom - 0.4f) look = Vector3.Lerp(formation, pathMid, Mathf.SmoothStep(0f, 1f, (t - ev.pathFrom + 0.4f) / 1.4f));
                Aim(fc, T.TransformDirection(look - eye));
                int half = Mathf.FloorToInt(t * 2f);
                if (half != lastHalf) { lastHalf = half; Note($"t={t:F1} frame={_frames}"); }
                yield return null;
            }
            _rec = false;
            yield return null; yield return null;

            // ---- take 2: the dash lane with an autopilot
            ev.JumpToLane(pl);
            for (int i = 0; i < 3; i++) yield return null;
            StartCoroutine(Record("play_lane"));
            float laneT = 0f;
            Vector3 S = ev.laneStart, dir = (ev.laneEnd - ev.laneStart).normalized;
            Vector3 U = Vector3.Cross(Vector3.up, dir).normalized, V = Vector3.Cross(dir, U);
            string lastPhase = "";
            while (laneT < 26f)
            {
                Aim(fc, T.TransformDirection(dir + Vector3.down * 0.05f));
                Vector3 body = T.InverseTransformPoint(pl.position + Vector3.up * ev.bodyCenter);
                Vector3 rel = body - S;
                float along = Vector3.Dot(rel, dir);
                Vector2 lat = new Vector2(Vector3.Dot(rel, U), Vector3.Dot(rel, V));
                var input = new FlightInput { move = new Vector3(0f, 0f, 1f) };
                if (ev.NextWave(along, lat, out float dz, out var gap))
                {
                    Vector2 err = gap - lat;
                    float eta = dz / (ev.laneSpeed + 6f);
                    Vector2 m = Vector2.ClampMagnitude(err * 0.9f, 1f);
                    input.move = new Vector3(m.x, m.y, err.magnitude > 1.2f && eta < 1.2f ? 0.25f : 1f);
                    if (err.magnitude > 1.7f && eta < 0.8f && !flight.Dashing)
                    {
                        input.dash = true;
                        input.move = new Vector3(err.x, err.y, 0.15f).normalized;
                    }
                }
                flight.scriptedInput = input;
                string ph = ev.LaneStatus;
                if (ph != lastPhase) { lastPhase = ph; Note($"lane {laneT:F1}s: {ph}"); }
                if (ev.LaneCleared && laneT > 1f) { for (int i = 0; i < 40; i++) yield return null; break; }
                laneT += Time.deltaTime / Mathf.Max(Time.timeScale, 1e-3f);
                yield return null;
            }
            _rec = false;
            yield return null; yield return null;
            Note($"lane take: {ev.TotalHits} hits");

            // ---- take 3: no dodging, straight down the middle -> hits, LANE FAILED, back to the gate
            ev.JumpToLane(pl);
            for (int i = 0; i < 3; i++) yield return null;
            StartCoroutine(Record("play_lane_fail"));
            int hits0 = ev.TotalHits, lastHits = -1;
            bool failed = false, reset = false;
            float ft = 0f;
            while (ft < 18f)
            {
                Aim(fc, T.TransformDirection(dir + Vector3.down * 0.05f));
                flight.scriptedInput = new FlightInput { move = new Vector3(0f, 0f, 1f) };
                if (ev.TotalHits != lastHits) { lastHits = ev.TotalHits; Note($"fail take {ft:F1}s: hits {ev.TotalHits - hits0}, hull {ev.LaneStatus}"); }
                if (ev.LaneFailing && !failed) { failed = true; Note($"fail take {ft:F1}s: {ev.LaneStatus}"); }
                if (failed && !ev.LaneFailing && !reset)
                {
                    reset = true;
                    Vector3 b = T.InverseTransformPoint(pl.position + Vector3.up * ev.bodyCenter);
                    Note($"fail take {ft:F1}s: reset to the gate, {Vector3.Distance(b, ev.laneStart):F1} m from it, status {ev.LaneStatus}");
                }
                if (reset && ft > 0f) { for (int i = 0; i < 30; i++) { flight.scriptedInput = default; yield return null; } break; }
                ft += Time.deltaTime / Mathf.Max(Time.timeScale, 1e-3f);
                yield return null;
            }
            _rec = false;
            yield return null; yield return null;
            Note($"fail take: failed={failed} reset={reset}");
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(OutDir, "play_log.txt"), _log);
            Debug.Log("[OYM-EV-DEMO] finished\n" + _log);
            Quit(0);
        }

        void Note(string s) { _log += s + "\n"; Debug.Log("[OYM-EV-DEMO] " + s); }

        static void Aim(FlightCamera fc, Vector3 worldDir)
        {
            worldDir.Normalize();
            float yaw = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(worldDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            fc.SetLook(yaw, pitch);
        }

        IEnumerator Record(string take)
        {
            _rec = true; _frames = 0;
            string dir = Path.Combine(OutDir, take);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "frame_*.png")) File.Delete(f);
            var cam = FlightCamera.Main.Cam;
            if (!_rt) { _rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _rt.Create(); }
            if (!_tex) _tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            cam.targetTexture = _rt;
            while (_rec)
            {
                yield return new WaitForEndOfFrame();
                if (!_rec) break;
                var prev = RenderTexture.active; RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); _tex.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"frame_{_frames:0000}.png"), _tex.EncodeToPNG());
                _frames++;
            }
            cam.targetTexture = null;
            Note($"{take}: {_frames} frames -> {dir}");
        }

        static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
