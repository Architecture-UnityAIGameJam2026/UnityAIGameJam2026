using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Signal
{
    /// Play-mode smoke test of the signal layer (only with -oymSignalDemo; OYMJevMark.PlaySignalDemo opens the game
    /// scene and enters play mode): installs the live HUSK the way HuskEncounter does (it stands down under other
    /// flags), then partner mark -> lock on an awake HUSK -> the CITADEL now -> lock a red light -> break all of them
    /// one by one -> finale / victory. Screenshots + events.txt to Screenshots/jevmark, then the editor quits.
    public class SignalDemo : MonoBehaviour
    {
        HuskEncounter _e;
        FlightCamera _fc;
        readonly List<string> _log = new List<string>();
        int _shot;
        float _t0;

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/jevmark"));

        public static void InstallHusk(Scene s)
        {
            if (HuskEncounter.Current) return;
            PlayTestDirector d = null; BossEntrance be = null;
            foreach (var root in s.GetRootGameObjects())
            {
                if (!d) d = root.GetComponentInChildren<PlayTestDirector>(true);
                if (!be) be = root.GetComponentInChildren<BossEntrance>(true);
            }
            var cfg = Resources.Load<HuskEncounterConfig>("OYM_HuskEncounter");
            if (!d || !cfg) { Debug.LogWarning("[OYM-SIGNAL-DEMO] no director / HUSK config"); return; }
            if (be) be.playOnStart = false;
            var go = new GameObject("HUSK Encounter");
            SceneManager.MoveGameObjectToScene(go, s);
            var e = go.AddComponent<HuskEncounter>();
            e.config = cfg; e.director = d; e.boss = be;
        }

        void Note(string s)
        {
            string line = $"{Time.realtimeSinceStartup - _t0,7:F1}s  {s}";
            _log.Add(line);
            Debug.Log("[OYM-SIGNAL-DEMO] " + s);
        }

        void Shot(string name)
        {
            Directory.CreateDirectory(OutDir);
            string p = Path.Combine(OutDir, $"{++_shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(p);
            Note("screenshot " + Path.GetFileName(p));
        }

        void Aim(Vector3 p)
        {
            if (!_fc) return;
            Vector3 d = p - _fc.transform.position;
            _fc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        IEnumerator Start()
        {
            _t0 = Time.realtimeSinceStartup;
            RadioMediator.Spoke += l => Note($"RADIO {(l.Jev ? "JEV" : "PARTNER")} {l.id}: {l.english} / {l.korean}");
            LockOn.Signal += tag => Note("lock signal: " + tag);
            CitadelWeakPoints.Broke += (p, left) => Note($"weak point {p.part} broken, {left} left");
            float wait = 0f;
            while ((!(_e = HuskEncounter.Current) || !_e.Level || !(_fc = FlightCamera.Main)) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            if (!_e || !_fc) { Note("FAIL: no HUSK encounter / camera"); Quit(1); yield break; }
            // no cursor lock here: this editor runs behind the user's windows (the demo aims with SetLook, locks with ForceLock)
            Note($"installed: radio {(RadioMediator.Current ? "yes" : "NO")}, lock-on {(LockOn.Current ? "yes" : "NO")}, marks {(FindAnyObjectByType<MarkOverlay>() ? "yes" : "NO")}, Jev key {JevLink.KeySource}");
            yield return new WaitForSecondsRealtime(4f);

            // partner mark: fly 45 m up the path, look back at the partner
            var me = _e.Player;
            ZeroGFlight mate = null;
            foreach (var c in _e.director.controllers) if (c && c.transform != me) mate = c;
            if (me && mate)
            {
                me.position += _e.Level.PathForward * 45f + Vector3.up * 3f;
                yield return new WaitForSecondsRealtime(0.5f);
                Aim(mate.transform.position + Vector3.up * 1.5f);
                yield return new WaitForSecondsRealtime(0.6f);
                Shot("partner_mark");
                Aim(mate.transform.position + Vector3.up * 1.5f + Quaternion.Euler(0f, 90f, 0f) * (mate.transform.position - me.position));
                yield return new WaitForSecondsRealtime(0.4f);
                Shot("partner_edge_arrow");
            }

            // lock-on: the nearest awake HUSK
            float t = 0f;
            HuskEnemy target = null;
            while (t < 40f)
            {
                float best = 1e9f;
                foreach (var en in _e.Enemies)
                    if (en && en.Alive && en.IsAwake) { float d = Vector3.Distance(en.transform.position, _e.PlayerChest); if (d < best && d < 200f) { best = d; target = en; } }
                if (target) break;
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (target)
            {
                for (int i = 0; i < 8; i++) { Aim(LockPoint(target)); yield return null; }
                Note($"soft target: {(LockOn.Current && LockOn.Current.HasSoft ? LockOn.Current.Soft.kind.ToString() : "none")}");
                bool ok = LockOn.Current && LockOn.Current.ForceLock(target);
                yield return new WaitForSecondsRealtime(0.35f);
                Note($"hard lock on {target.name}: {ok}, {LockOn.Current.Describe()}");
                Shot("lock_husk");
                yield return new WaitForSecondsRealtime(2.5f);
            }
            else Note("no awake HUSK within 200 m to lock");

            // the CITADEL now
            var lv = _e.Level;
            typeof(HuskLevel).GetMethod("StartBoss", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(lv, null);
            Note("boss called");
            t = 0f;
            while (!CitadelWeakPoints.Current && t < 40f) { t += Time.unscaledDeltaTime; yield return null; }
            var wp = CitadelWeakPoints.Current;
            if (!wp || wp.Total == 0) { Note("FAIL: no weak points"); Quit(1); yield break; }
            foreach (var p in wp.Points) Note($"weak point {p.part}: {Vector3.Distance(p.AimPoint, _e.PlayerChest):F0} m, hit radius {p.Radius:F1} m, hp {p.maxHp}");
            _e.hostileEnabled = false;                                    // keep the swarm off for the stills
            yield return new WaitForSecondsRealtime(1.5f);

            var order = new List<CitadelWeakPoint>(wp.Points);
            for (int i = 0; i < order.Count; i++)
            {
                var p = order[i];
                if (!p || p.Broken) continue;
                for (int k = 0; k < 10; k++) { Aim(p.AimPoint); yield return null; }
                bool ok = LockOn.Current && LockOn.Current.ForceLock(p);
                yield return new WaitForSecondsRealtime(0.4f);
                if (i == 0) Shot("lock_weak_point");
                // two hits (flare), then the break
                p.Hit(p.maxHp * 0.4f, p.AimPoint, -_fc.transform.forward);
                yield return new WaitForSecondsRealtime(0.5f);
                p.Hit(p.maxHp * 0.4f, p.AimPoint, -_fc.transform.forward);
                yield return new WaitForSecondsRealtime(0.5f);
                Note($"lock {p.part}: {ok}; hp before break {p.Hp:F0}");
                p.Hit(p.maxHp, p.AimPoint, -_fc.transform.forward);
                yield return new WaitForSecondsRealtime(0.25f);
                Shot("break_" + p.part.Replace("citadel_", ""));
                yield return new WaitForSecondsRealtime(i == order.Count - 1 ? 1.2f : 3.5f);
            }
            Shot("finale");
            yield return new WaitForSecondsRealtime(3f);
            Shot("victory_cut");
            yield return new WaitForSecondsRealtime(6f);
            Note($"beat {_e.Level.beat}, weak points left {wp.Left}");
            Quit(0);
        }

        static Vector3 LockPoint(HuskEnemy en) => en.kind == HuskEnemy.Kind.Prism && en.prism ? en.prism.LensWorld : en.transform.position;

        void Quit(int code)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllLines(Path.Combine(OutDir, "events.txt"), _log);
            Debug.Log($"[OYM-SIGNAL-DEMO] done ({code})");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
