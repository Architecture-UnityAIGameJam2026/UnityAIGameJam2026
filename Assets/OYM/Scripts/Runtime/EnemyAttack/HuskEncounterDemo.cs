using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace OYM
{
    /// Play-mode verification of the whole level flow (only with -oymHuskDemo, added by HuskEncounter): ORBIT FRAME on
    /// autopilot flies up the debris path, watches the nearest threat, shoots some rushing WICKs and the PRISMs caught
    /// charging, lets others reach it (latch + blast, a lance), dashes out of a latch and away from incoming darts. Keeps
    /// its hands off during cutaways and the CITADEL entrance. The game camera is recorded on a fixed 24 fps capture
    /// clock to Screenshots/husk_ingame/frame_NNNN.jpg (+ events.txt) until the boss phase has run a while, then the
    /// editor quits.
    public class HuskEncounterDemo : MonoBehaviour
    {
        public float maxSeconds = 230f;
        [Tooltip("seconds of boss phase (after the entrance) to keep recording")] public float bossSeconds = 30f;
        public Vector2Int size = new Vector2Int(960, 540);
        const int Fps = 24;

        HuskEncounter _e;
        HuskLevel _lv;
        FlightCamera _fc;
        ZeroGFlight _pilot;
        RenderTexture _rt;
        Texture2D _tex;
        readonly List<string> _log = new List<string>();
        float _t, _yaw, _pitch, _fireCool, _dodgeCool, _bossLiveAt = -1f;
        bool _lookInit;
        readonly HashSet<HuskEnemy> _spared = new HashSet<HuskEnemy>();
        readonly HashSet<HuskEnemy> _seen = new HashSet<HuskEnemy>();
        readonly HashSet<HuskEnemy> _charging = new HashSet<HuskEnemy>();
        int _rushOrder, _latches, _prismCharges, _frame;
        HuskEnemy _latchedBy;

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/husk_ingame"));

        IEnumerator Start()
        {
            _e = GetComponent<HuskEncounter>();
            yield return null;                                         // PlayTestDirector.Start ran: pick ORBIT FRAME
            _lv = _e.Level;
            var d = _e.director;
            int idx = d.names != null ? System.Array.IndexOf(d.names, "ORBIT FRAME") : -1;
            if (idx > 0)
            {
                var act = typeof(PlayTestDirector).GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Instance);
                act?.Invoke(d, new object[] { idx });
            }
            _fc = d.flightCamera ? d.flightCamera : FlightCamera.Main;
            _pilot = _fc && _fc.target ? _fc.target.GetComponent<ZeroGFlight>() : null;
            if (!_fc || !_pilot) { Debug.LogError("[OYM-HUSK-DEMO] no pilot / camera"); Quit(1); yield break; }
            _pilot.scripted = true;
            _fc.scripted = true;
            _e.EnemyGone += (en, byPlayer) => Note($"{(en ? en.name : "?")} gone ({(byPlayer ? "shot down" : "self-destruct")})");
            _e.PlayerDamaged += (a, from) => Note($"player hit {a:F0} -> HP {_e.PlayerHP01 * 100f:F0}%");
            if (_lv) _lv.BeatStarted += b => Note($"== beat {b} (progress {_lv.Progress:F0} m)");
            if (_lv) _lv.Radio += (who, line) => Note($"radio {who}|{line}");
            Time.captureFramerate = Fps;
            Directory.CreateDirectory(OutDir);
            foreach (var f in Directory.GetFiles(OutDir, "frame_*.*")) File.Delete(f);
            _rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _rt.Create();
            _tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            var cam = _fc.Cam ? _fc.Cam : Camera.main;
            cam.targetTexture = _rt;
            int frame = 0;
            var black = new Color32[size.x * size.y];
            for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            while (_t < maxSeconds && !(_bossLiveAt > 0f && _t - _bossLiveAt > bossSeconds))
            {
                yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active; RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                RenderTexture.active = prev;
                // the letterbox bars are screen UI (not in the camera image): draw them into the recording
                float bars = 0f;
                if (_e.boss && _e.boss.Playing) bars = Mathf.Max(bars, 0.11f * BossEntrance.Letterbox(_e.boss.Clock));
                int bh = Mathf.RoundToInt(size.y * bars);
                if (bh > 0) { _tex.SetPixels32(0, 0, size.x, bh, black); _tex.SetPixels32(0, size.y - bh, size.x, bh, black); }
                _tex.Apply(false);
                File.WriteAllBytes(Path.Combine(OutDir, $"frame_{frame:0000}.jpg"), _tex.EncodeToJPG(90));
                frame++;
                _frame = frame;
            }
            cam.targetTexture = null;
            Time.captureFramerate = 0;
            Note($"done: {frame} frames, kills {_e.Kills}, HP {_e.PlayerHP01 * 100f:F0}%");
            File.WriteAllLines(Path.Combine(OutDir, "events.txt"), _log);
            Quit(0);
        }

        void Note(string s)
        {
            string line = $"{_t,6:F2}s f{_frame:0000}  {s}";
            _log.Add(line);
            Debug.Log("[OYM-HUSK-DEMO] " + line);
        }

        void Update()
        {
            if (!_pilot || !_fc) return;
            _t += Time.deltaTime;
            _fireCool -= Time.deltaTime; _dodgeCool -= Time.deltaTime;
            if (_lv && _lv.beat == HuskLevel.Beat.Boss && _e.boss && _e.boss.Done && _bossLiveAt < 0f) { _bossLiveAt = _t; Note("boss entrance done"); }
            Bookkeeping();
            // hands off during the arrival and boss entrance
            if (!_e.Gate) { _pilot.scriptedInput = default; return; }
            if (!_pilot.scripted) _pilot.scripted = true;
            if (!_fc.scripted) _fc.scripted = true;

            Vector3 chest = _e.PlayerChest;
            HuskEnemy look = PickTarget(chest);
            Vector3 camPos = _fc.transform.position;
            Vector3 lookAt = look ? look.transform.position + (look.kind == HuskEnemy.Kind.Prism ? look.transform.up * 0.6f : Vector3.zero)
                                  : chest + PathDir(chest) * 30f + Vector3.up * 2f;
            Aim(lookAt - camPos, look && look.Latched ? 10f : 4.5f);

            var input = new FlightInput();
            Vector3 move = Vector3.zero;
            float threat = look ? Vector3.Distance(look.transform.position, chest) : 999f;
            if (!look || threat > 28f) move = PathDir(chest) * 0.75f;                         // advance up the path
            else move = Vector3.Cross(Vector3.up, (look.transform.position - chest).normalized) * (Mathf.Sin(_t * 0.6f) * 0.45f);

            if (look)
            {
                bool aligned = Vector3.Angle(_fc.Forward, look.transform.position - camPos) < 2.5f;
                if (look.kind == HuskEnemy.Kind.Wick && look.Rushing && !_spared.Contains(look) && threat < 26f && aligned && _fireCool <= 0f)
                { input.fire = true; _fireCool = 0.9f; Note($"shoot {look.name} at {threat:F0} m"); }
                if (look.kind == HuskEnemy.Kind.Prism && look.Charging && aligned && _fireCool <= 0f && _prismCharges >= 2)
                { input.fire = true; _fireCool = 0.9f; Note($"shoot charging {look.name}"); }
                if (look.Latched && _latches >= 2 && look == _latchedBy)
                { move = (chest - look.transform.position).normalized; input.dash = true; }
            }
            // darts: one sideways dash when the first of a salvo gets close
            var darts = _e.Darts;
            if (darts != null && _dodgeCool <= 0f)
                foreach (var dt in darts)
                {
                    if (!dt || !dt.Busy || dt.Clock > dt.FlightTime) continue;
                    Vector3 rel = chest - dt.transform.position;
                    if (rel.magnitude < 28f && Vector3.Dot(rel.normalized, dt.transform.forward) > 0.6f)
                    {
                        move = Vector3.Cross(dt.transform.forward, Vector3.up).normalized; input.dash = true;
                        _dodgeCool = 3f; Note("dash away from darts"); break;
                    }
                }
            input.move = Quaternion.Inverse(_fc.transform.rotation) * move;
            input.move.y = Mathf.Clamp(move.y, -1f, 1f);
            _pilot.scriptedInput = input;
        }

        void Bookkeeping()
        {
            foreach (var en in _e.Enemies)
            {
                if (!en || !en.Alive) continue;
                if (en.kind == HuskEnemy.Kind.Wick && en.Rushing && !_seen.Contains(en))
                {
                    _seen.Add(en);
                    if (_rushOrder % 2 == 0) _spared.Add(en);                 // every second WICK gets through
                    _rushOrder++;
                }
                if (en.Latched && _latchedBy != en) { _latchedBy = en; _latches++; Note($"latched by {en.name} (#{_latches})"); }
                if (en.kind == HuskEnemy.Kind.Prism)
                {
                    bool was = _charging.Contains(en);
                    if (en.Charging && !was) { _charging.Add(en); _prismCharges++; Note($"{en.name} charges (#{_prismCharges})"); }
                    else if (!en.Charging && was) _charging.Remove(en);
                }
            }
        }

        HuskEnemy PickTarget(Vector3 chest)
        {
            HuskEnemy look = null; float best = float.MaxValue;
            foreach (var en in _e.Enemies)
            {
                if (!en || !en.Alive || en.Dormant) continue;
                float d = Vector3.Distance(en.transform.position, chest);
                if (d > 90f) continue;
                float score = d;
                if (en.Latched) score -= 1000f;
                else if (en.kind == HuskEnemy.Kind.Prism && en.Charging) score -= 60f;
                else if (en.kind == HuskEnemy.Kind.Wick && en.Rushing && d < 35f) score -= 30f;
                if (score < best) { best = score; look = en; }
            }
            return look;
        }

        /// toward the next path stone ahead (a little above it), else straight along the path
        Vector3 PathDir(Vector3 chest)
        {
            if (!_lv || _lv.Stones.Count == 0) return Vector3.forward;
            float prog = _lv.Progress;
            foreach (var s in _lv.Stones)
            {
                if (!s) continue;
                float along = Vector3.Dot(s.position - chest, _lv.PathForward);
                if (along < 6f) continue;
                Vector3 to = s.position + Vector3.up * 3f - chest;
                return to.normalized;
            }
            return _lv.PathForward;
        }

        void Aim(Vector3 dir, float rate)
        {
            dir.Normalize();
            float wy = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, wp = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (!_lookInit) { _yaw = wy; _pitch = wp; _lookInit = true; }
            float k = 1f - Mathf.Exp(-Time.deltaTime * rate);
            _yaw += Mathf.DeltaAngle(_yaw, wy) * k;
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(wp, -60f, 60f), k);
            _fc.SetLook(_yaw, _pitch);
        }

        static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
