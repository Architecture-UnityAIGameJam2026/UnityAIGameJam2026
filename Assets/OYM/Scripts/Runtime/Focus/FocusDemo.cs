using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace OYM.Signal
{
    /// Play-mode smoke test of the focus highlight (only with -oymFocusDemo; run it through OYMJevMark.PlaySignalDemo,
    /// which opens the game scene in play mode): a PRISM and a WICK in front of the pilot -> no focus / attacking
    /// without a lock (soft) / hard lock / a hit pulse / the WICK -> the CITADEL: lock each red light and each launcher
    /// pod in turn. Screenshots + events.txt to Screenshots/focus, then the editor quits.
    public class FocusDemo : MonoBehaviour
    {
        HuskEncounter _e;
        FlightCamera _fc;
        readonly List<string> _log = new List<string>();
        int _shot;
        float _t0;
        bool _quitting;

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/focus"));

        void Note(string s)
        {
            _log.Add($"{Time.realtimeSinceStartup - _t0,7:F1}s  {s}");
            Debug.Log("[OYM-FOCUS-DEMO] " + s);
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

        static Vector3 PointOf(Component c) =>
            c is HuskEnemy en ? (en.kind == HuskEnemy.Kind.Prism && en.prism ? en.prism.LensWorld : en.transform.position)
            : c is CitadelWeakPoint wp ? wp.AimPoint : c is HuskBossPart pod ? pod.Center : c.transform.position;

        IEnumerator AimFor(Component c, float seconds)
        {
            float t = 0f;
            while (t < seconds) { if (c) Aim(PointOf(c)); t += Time.unscaledDeltaTime; yield return null; }
        }

        /// the level's cutaways (the WICK reveal, the boss intro) hide the highlight on purpose: shoot outside them
        IEnumerator WaitView(float max)
        {
            float t = 0f;
            while (!SignalInstall.PlayerView(_e) && t < max) { t += Time.unscaledDeltaTime; yield return null; }
            if (t > 0.1f) Note($"(waited {t:F1} s for a cutaway to end)");
        }

        static void ReleaseLock()
        {
            var lk = LockOn.Current;
            if (lk && lk.Locked) typeof(LockOn).GetMethod("Release", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(lk, new object[] { null });
        }

        string State()
        {
            var f = FocusHighlight.Current;
            if (!f) return "NO FocusHighlight";
            var lk = LockOn.Current;
            return $"focus {(f.Focus ? f.Focus.name : "none")} ({(f.FocusHard ? "hard" : "soft")}), lit {f.Lit}, engaged {f.Engaged}, lock {(lk && lk.Locked ? lk.Describe() : "none")}";
        }

        void Update()
        {
            // watchdog: never hold the shared editor for long
            if (!_quitting && _t0 > 0f && Time.realtimeSinceStartup - _t0 > 420f) { Note("FAIL: watchdog (7 min)"); StartCoroutine(Quit(2)); }
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;                    // this editor runs behind the user's windows
            _t0 = Time.realtimeSinceStartup;
            FocusHighlight.Changed += (c, hard) => Note($"focus -> {(c ? c.name : "none")} ({(hard ? "hard" : "soft")})");
            float wait = 0f;
            while ((!(_e = HuskEncounter.Current) || !_e.Level || !(_fc = FlightCamera.Main) || !LockOn.Current || !FocusHighlight.Current) && wait < 90f)
            { wait += Time.unscaledDeltaTime; yield return null; }
            if (!_e || !_fc || !LockOn.Current || !FocusHighlight.Current) { Note($"FAIL: encounter {(bool)_e}, camera {(bool)_fc}, lock-on {(bool)LockOn.Current}, focus {(bool)FocusHighlight.Current}"); yield return Quit(1); yield break; }
            _e.hostileEnabled = false;                              // nobody shoots the pilot during the stills
            wait = 0f;
            while (!SignalInstall.PlayerView(_e) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            Note($"player view after {wait:F1} s, template {(Resources.Load<Material>("OYM_FocusHighlight") ? "Resources/OYM_FocusHighlight" : "MISSING")}, shader {(Shader.Find("OYM/FX/FocusHighlight") ? "ok" : "MISSING")}");
            var passes = UnityEngine.Rendering.HighDefinition.CustomPassVolume.GetGlobalCustomPasses(UnityEngine.Rendering.HighDefinition.CustomPassInjectionPoint.BeforePostProcess);
            int ours = 0;
            if (passes != null) foreach (var gp in passes) if (gp.instance is FocusOutlinePass) ours++;
            Note($"outline pass registered: {ours} (global passes before post: {(passes != null ? passes.Count : 0)})");
            yield return new WaitForSecondsRealtime(1.5f);

            // ---- two HUSK in front of the pilot
            Vector3 chest = _e.PlayerChest;
            Vector3 fwd = _fc.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-3f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var prism = _e.Spawn(HuskEnemy.Kind.Prism, chest + fwd * 24f - right * 3f + Vector3.up * 2.5f, Quaternion.LookRotation(-fwd), 0f);
            var wick = _e.Spawn(HuskEnemy.Kind.Wick, chest + fwd * 13f + right * 4f, Quaternion.LookRotation(-fwd), 0f);
            prism.Go(); wick.Go();
            yield return AimFor(prism, 1.2f);
            yield return WaitView(30f);
            yield return AimFor(prism, 0.9f);
            Note("1 no lock, no attack: " + State());
            Shot("no_focus");

            FocusHighlight.Engage(6f);                              // as if LMB / RMB went down
            Aim(PointOf(prism)); yield return null; Aim(PointOf(prism)); yield return null;
            Note("2 attacking, no lock: " + State());
            Shot("attack_snap");
            yield return AimFor(prism, 0.7f);
            Note("2b attacking, held: " + State());
            Shot("attack_soft");

            yield return WaitView(20f);
            bool ok = LockOn.Current.ForceLock(prism);
            Aim(PointOf(prism)); yield return null; Aim(PointOf(prism)); yield return null;
            Note($"3 hard lock {ok}: " + State());
            Shot("lock_snap");
            yield return AimFor(prism, 0.7f);
            Note("3b lock held: " + State());
            Shot("lock_hold");

            prism.TakeDamage(1f, prism.transform.position - chest, 0.6f);
            Aim(PointOf(prism)); yield return null; Aim(PointOf(prism)); yield return null;
            Note($"4 hit on the locked PRISM (hp {prism.HP:F0}): " + State());
            Shot("hit_pulse");
            yield return AimFor(prism, 0.6f);

            yield return WaitView(20f);
            ok = LockOn.Current.ForceLock(wick);
            yield return AimFor(wick, 0.9f);
            Note($"5 lock WICK {ok}: " + State());
            Shot("lock_wick");

            // ---- auto lock: the crosshair rests on a target / a flick away lets go / the middle button releases
            var lk = LockOn.Current;
            lk.ForceRelease();
            yield return new WaitForSecondsRealtime(1.4f);                // past the release hold
            yield return WaitView(20f);
            bool wasScripted = _fc.scripted;
            _fc.scripted = true;                                         // input counts as live without a captured cursor
            yield return AimFor(prism, 0.6f);
            Note($"9 crosshair rested on the PRISM 0.6 s: auto lock {lk.AutoLocked}: " + State());
            Shot("auto_lock");
            Vector3 eyeNow = _fc.transform.position;
            Vector3 away = eyeNow + Quaternion.AngleAxis(40f, Vector3.up) * (PointOf(prism) - eyeNow);
            for (int i = 0; i < 6; i++) { Aim(away); yield return null; }
            Note($"10 flicked 40 deg away: locked {lk.Locked} (expect False)");
            yield return AimFor(prism, 0.8f);
            Note($"11 back on the PRISM 0.8 s: auto lock {lk.AutoLocked} (expect True)");
            lk.ForceRelease();                                           // = the middle button
            yield return AimFor(prism, 0.8f);
            Note($"12 middle button, crosshair kept on it 0.8 s: locked {lk.Locked} (expect False)");
            Shot("auto_released");
            yield return AimFor(wick, 1.0f);
            Note($"13 onto the WICK 1 s: auto lock {lk.AutoLocked} (expect True), {lk.Describe()}");
            Shot("auto_lock_wick");
            _fc.scripted = wasScripted;
            yield return new WaitForSecondsRealtime(0.3f);               // let the still land before the boss entrance hides the marks

            // ---- the CITADEL: one part at a time
            typeof(HuskLevel).GetMethod("StartBoss", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(_e.Level, null);
            Note("boss called");
            wait = 0f;
            while (!CitadelWeakPoints.Current && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            var w = CitadelWeakPoints.Current;
            if (!w || w.Total == 0) { Note("FAIL: no CITADEL weak points"); yield return Quit(1); yield break; }
            _e.hostileEnabled = false;
            wait = 0f;
            while (!SignalInstall.PlayerView(_e) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            Note($"boss fight view after {wait:F1} s: {w.Total} red lights, {w.Pods.Count} pods");
            yield return new WaitForSecondsRealtime(1f);

            var parts = new List<Component>();
            foreach (var p in w.Points) if (p && !p.Broken) parts.Add(p);
            foreach (var p in w.Pods) if (p && !p.Broken) parts.Add(p);
            foreach (var c in parts)
            {
                yield return WaitView(20f);
                yield return AimFor(c, 0.5f);
                ok = LockOn.Current.ForceLock(c);
                yield return AimFor(c, 0.9f);
                string label = c is CitadelWeakPoint cw ? cw.part : ((HuskBossPart)c).label;
                Note($"6 CITADEL {label}: lock {ok}, {Vector3.Distance(PointOf(c), _fc.transform.position):F0} m: " + State());
                Shot("boss_" + label.Replace("citadel_", ""));
            }
            if (parts.Count > 0 && parts[0] is CitadelWeakPoint first && !first.Broken)
            {
                yield return WaitView(20f);
                yield return AimFor(first, 0.4f);
                LockOn.Current.ForceLock(first);
                yield return AimFor(first, 0.8f);
                first.Hit(1f, first.AimPoint, -_fc.transform.forward);
                yield return null; yield return null;
                Note($"7 hit on {first.part}: " + State());
                Shot("boss_hit_pulse");
            }
            // no lock, attacking with the crosshair on a launcher pod: the softer, thinner focus
            HuskBossPart pod = null;
            foreach (var p in w.Pods) if (p && !p.Broken) { pod = p; break; }
            if (pod)
            {
                ReleaseLock();
                yield return WaitView(20f);
                FocusHighlight.Engage(4f);
                yield return AimFor(pod, 0.9f);
                Note($"8 attacking the pod {pod.label} without a lock: " + State());
                Shot("boss_soft_pod");
            }
            yield return Quit(0);
        }

        IEnumerator Quit(int code)
        {
            if (_quitting) yield break;
            _quitting = true;
            yield return new WaitForSecondsRealtime(1.2f);          // the last CaptureScreenshot lands at the end of a frame
            Directory.CreateDirectory(OutDir);
            File.WriteAllLines(Path.Combine(OutDir, "events.txt"), _log);
            Debug.Log($"[OYM-FOCUS-DEMO] done ({code})");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
