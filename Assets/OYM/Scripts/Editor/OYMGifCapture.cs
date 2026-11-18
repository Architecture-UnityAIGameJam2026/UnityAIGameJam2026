// OnYourMark - scripted "in-game test" frames for KESTREL and ORBIT FRAME, recorded in batch mode (edit mode, no player loop).
//
//   Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMGifCapture.RebuildAndRecordAll -quit
//     (RecordKestrel / RecordOrbitFrame record one character from the existing prefab + test scene without rebuilding)
//
// The controller logic of KestrelController / OrbitFrameController is replicated here with inputs coming from a timeline
// (Space = dash, LMB = slash / fire, RMB = aim), because Update() does not run in edit mode. The Animator, the FX
// (JetpackFX.Tick / RailgunFX.Tick) and every ParticleSystem are stepped by hand in 12.5 ms ticks (like a ~80 fps game loop),
// a smoothed third-person camera follows the root, and one PNG is rendered per 1/20 s into Screenshots/gif/frames_<name>/.
// Screenshots/gif/make_gif.py assembles the GIFs + contact sheets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OYM.EditorTools
{
    public static class OYMGifCapture
    {
        const int OutW = 800, OutH = 450;   // GIF size
        const int SS = 2;                   // rendered at 2x, make_gif.py downsamples (clean thin FX / stars)
        const int W = OutW * SS, H = OutH * SS;
        const float FrameDt = 0.05f;      // 20 fps GIF
        const int Sub = 4;                // sim ticks per GIF frame (dt = 12.5 ms)

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CombatId = Animator.StringToHash("Combat");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int AttackTypeId = Animator.StringToHash("AttackType");
        static readonly int DashId = Animator.StringToHash("Dash");
        static readonly int AimId = Animator.StringToHash("Aim");

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/gif"));

        // ------------------------------------------------------------------------------------------------ entry points
        [MenuItem("OYM/Characters/GIF: Rebuild + record both")]
        public static void RebuildAndRecordAll()
        {
            Guard(() => { Rebuild(); RecordKestrelInner(); RecordOrbitFrameInner(); });
        }

        /// KESTREL prefab already current: rebuild its test scene + the ORBIT FRAME prefab / scene, then record both.
        public static void RebuildOrbitAndRecordAll()
        {
            Guard(() =>
            {
                OYMKestrel.BuildTestScene();
                ShadowProbe();
                RecordKestrelInner();
                OYMOrbitFrame.Build(); OYMOrbitFrame.BuildTestScene();
                RecordOrbitFrameInner();
            });
        }

        [MenuItem("OYM/Characters/GIF: Rebuild only (prefabs + scenes)")]
        public static void RebuildOnly() { Guard(Rebuild); }

        [MenuItem("OYM/Characters/GIF: Record KESTREL")]
        public static void RecordKestrel() { Guard(RecordKestrelInner); }

        [MenuItem("OYM/Characters/GIF: Record ORBIT FRAME")]
        public static void RecordOrbitFrame() { Guard(RecordOrbitFrameInner); }

        static void Guard(Action a)
        {
            var sw = Stopwatch.StartNew();
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            try { a(); Debug.Log($"[OYM-G] finished OK in {sw.Elapsed.TotalSeconds:F0} s"); }
            catch (Exception e)
            {
                Debug.LogError("[OYM-G] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// Before/after numbers for the batch-render slowness: the old test scenes had two shadow-casting directional lights
        /// (HDRP logs "Cascade Shadow atlasing has failed" every frame); the builders now shadow only the key light.
        static void ShadowProbe()
        {
            EditorSceneManager.OpenScene(OYMKestrel.TestScene);
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var anim = UnityEngine.Object.FindFirstObjectByType<KestrelController>().GetComponentInChildren<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.Rebind(); anim.Update(0f);
            var dirs = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional).ToArray();
            int warns = 0;
            Application.LogCallback cb = (m, st, t) => { if (t == LogType.Warning) warns++; };
            Application.logMessageReceived += cb;
            // the old stills got slower and slower (1, 1, 3, 5, 7, 9 min each), i.e. render cost accumulated with the frame count: sample many renders
            string Run(string label, int n, double budgetMs)
            {
                warns = 0; var ms = new List<double>(); var sw = new Stopwatch(); var total = Stopwatch.StartNew();
                for (int i = 0; i < n && total.Elapsed.TotalMilliseconds < budgetMs; i++) { sw.Restart(); anim.Update(0.05f); cam.Render(); ms.Add(sw.Elapsed.TotalMilliseconds); }
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); RenderTexture.active = null;
                var pick = new[] { 1, 2, 5, 10, 20, 30, 40, 50, 60, 80, 100 }.Where(k => k <= ms.Count).Select(k => $"#{k}={ms[k - 1]:F0}");
                return $"[OYM-G] probe 1600x900 {label}: {ms.Count} renders in {total.Elapsed.TotalSeconds:F1} s, ms [{string.Join(", ", pick)}], {warns} warnings";
            }
            var shadowed = dirs.Select(l => l.shadows).ToArray();
            Debug.Log($"[OYM-G] probe: {dirs.Length} directional lights, shadow modes [{string.Join(", ", shadowed)}]");
            Debug.Log(Run("AFTER  (rebuilt scene: only the key light casts shadows)", 100, 30000));
            foreach (var l in dirs) l.shadows = LightShadows.Soft;
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Debug.Log(Run("BEFORE (old scenes: both directional lights cast shadows)", 100, 60000));
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            for (int i = 0; i < dirs.Length; i++) dirs[i].shadows = shadowed[i];
            Application.logMessageReceived -= cb;
            cam.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Rebuild()
        {
            var sw = Stopwatch.StartNew();
            OYMKestrel.Build(); OYMKestrel.BuildTestScene();
            Debug.Log($"[OYM-G] KESTREL prefab + scene rebuilt ({sw.Elapsed.TotalSeconds:F0} s)"); sw.Restart();
            OYMOrbitFrame.Build(); OYMOrbitFrame.BuildTestScene();
            Debug.Log($"[OYM-G] ORBIT FRAME prefab + scene rebuilt ({sw.Elapsed.TotalSeconds:F0} s)");
        }

        // ------------------------------------------------------------------------------------------------ rig / camera
        sealed class Rig
        {
            public string name, dir;
            public Transform root;
            public Animator anim;
            public JetpackFX jet;
            public RailgunFX rail;
            public Camera cam;
            public RenderTexture rt, rtOut;   // rt: OutW*SS x OutH*SS camera target; rtOut: box-downsampled copy that gets saved
            public Texture2D tex;
            public ParticleSystem[] ps;
            public float t;
            public int frame;
        }

        struct CamKey
        {
            public float t, az, el, dist, fov, smooth, follow;
            public Vector3 off;
            /// az: degrees around the character (0 = in front of a +Z facing character, 90 = its right side, 180 = behind);
            /// off: pivot offset from the root; smooth: seconds the orbit / zoom take to settle; follow: pivot lag.
            public static CamKey K(float t, float az, float el, float dist, float fov, float offY, float offZ = 0f, float smooth = 0.35f, float follow = 0.16f)
                => new CamKey { t = t, az = az, el = el, dist = dist, fov = fov, off = new Vector3(0f, offY, offZ), smooth = smooth, follow = follow };
        }

        sealed class CamRig
        {
            float az, el, dist, fov, vaz, vel, vdist, vfov;
            Vector3 pivot, vpivot;

            public void Step(CamKey k, Vector3 rootPos, float dt, bool snap)
            {
                Vector3 target = rootPos + k.off;
                if (snap) { az = k.az; el = k.el; dist = k.dist; fov = k.fov; pivot = target; vaz = vel = vdist = vfov = 0f; vpivot = Vector3.zero; return; }
                az = Mathf.SmoothDampAngle(az, k.az, ref vaz, k.smooth, Mathf.Infinity, dt);
                el = Mathf.SmoothDamp(el, k.el, ref vel, k.smooth, Mathf.Infinity, dt);
                dist = Mathf.SmoothDamp(dist, k.dist, ref vdist, k.smooth, Mathf.Infinity, dt);
                fov = Mathf.SmoothDamp(fov, k.fov, ref vfov, k.smooth, Mathf.Infinity, dt);
                pivot = Vector3.SmoothDamp(pivot, target, ref vpivot, k.follow, Mathf.Infinity, dt);
            }

            public void Apply(Camera cam)
            {
                float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
                Vector3 pos = pivot + new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e)) * dist;
                pos.y = Mathf.Max(pos.y, 0.12f);
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pivot - pos, Vector3.up));
                cam.fieldOfView = fov;
            }
        }

        static CamKey KeyAt(CamKey[] keys, float t)
        {
            var k = keys[0];
            foreach (var c in keys) if (c.t <= t) k = c;
            return k;
        }

        static bool Edge(float t, float dt, float at) => t - dt < at && t >= at;

        static Rig Open(string scenePath, string name, Type controller)
        {
            EditorSceneManager.OpenScene(scenePath);
            var ctl = (Component)UnityEngine.Object.FindFirstObjectByType(controller);
            if (ctl == null) throw new Exception("no " + controller.Name + " in " + scenePath);
            var r = new Rig { name = name, root = ctl.transform };
            r.anim = ctl.GetComponentInChildren<Animator>();
            r.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;      // edit mode: never cull the pose update
            r.anim.applyRootMotion = false;
            r.anim.Rebind(); r.anim.Update(0f);
            r.jet = ctl.GetComponent<JetpackFX>();
            r.rail = ctl.GetComponent<RailgunFX>();
            r.ps = ctl.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var p in r.ps) { p.Simulate(0f, false, true, false); p.Clear(false); }
            foreach (var l in ctl.GetComponentsInChildren<Light>(true)) if (l.name != "EyeGlowLight") l.enabled = false;
            var lr = ctl.GetComponentInChildren<LineRenderer>(true); if (lr) lr.enabled = false;
            r.cam = Camera.main;
            r.rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); r.rt.Create();
            r.rtOut = new RenderTexture(OutW, OutH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear }; r.rtOut.Create();
            r.cam.targetTexture = r.rt;
            r.tex = new Texture2D(OutW, OutH, TextureFormat.RGB24, false);
            r.dir = Path.Combine(OutDir, "frames_" + name);
            Directory.CreateDirectory(r.dir);
            foreach (var f in Directory.GetFiles(r.dir, "f_*.png")) File.Delete(f);
            foreach (var c in r.anim.runtimeAnimatorController.animationClips)
                Debug.Log($"[OYM-G] {name} clip {c.name}: {c.length:F2} s{(c.isLooping ? " loop" : "")}");
            return r;
        }

        static void Close(Rig r)
        {
            r.cam.targetTexture = null; r.rt.Release(); r.rtOut.Release();
            UnityEngine.Object.DestroyImmediate(r.rt); UnityEngine.Object.DestroyImmediate(r.rtOut); UnityEngine.Object.DestroyImmediate(r.tex);
        }

        /// One sim tick after the controller logic: Animator -> FX (recoil lands on the fresh pose) -> particles.
        static void Advance(Rig r, float dt)
        {
            r.anim.Update(dt);
            if (r.jet) r.jet.Tick(dt);
            if (r.rail) r.rail.Tick(dt);
            foreach (var p in r.ps) p.Simulate(dt, false, false, false);
        }

        static void Shoot(Rig r)
        {
            r.cam.Render();
            Graphics.Blit(r.rt, r.rtOut);                           // bilinear 2x down = 2x2 box filter (supersampling)
            RenderTexture.active = r.rtOut;
            r.tex.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(r.dir, $"f_{r.frame:0000}.png"), r.tex.EncodeToPNG());
            r.frame++;
        }

        static void RecordLoop(Rig r, float duration, Action<float, float> tick, CamKey[] keys)
        {
            int frames = Mathf.RoundToInt(duration / FrameDt);
            var cam = new CamRig();
            cam.Step(keys[0], r.root.position, FrameDt, true); cam.Apply(r.cam);
            var wsw = Stopwatch.StartNew();
            for (int i = 0; i < 4; i++) r.cam.Render();                  // warm-up: sky / TAA history on the opening pose
            Debug.Log($"[OYM-G] {r.name}: warm-up renders {wsw.Elapsed.TotalMilliseconds / 4:F0} ms each");
            double renderMs = 0, simMs = 0; var sw = new Stopwatch();
            for (int f = 0; f < frames; f++)
            {
                sw.Restart();
                for (int s = 0; s < Sub; s++) { r.t += FrameDt / Sub; tick(r.t, FrameDt / Sub); }
                cam.Step(KeyAt(keys, r.t), r.root.position, FrameDt, false); cam.Apply(r.cam);
                simMs += sw.Elapsed.TotalMilliseconds; sw.Restart();
                Shoot(r);
                renderMs += sw.Elapsed.TotalMilliseconds;
                if (f % 20 == 19) Debug.Log($"[OYM-G] {r.name}: frame {f + 1}/{frames} t={r.t:F2}s  avg render+save {renderMs / (f + 1):F0} ms, sim {simMs / (f + 1):F1} ms");
            }
            Debug.Log($"[OYM-G] {r.name}: {frames} frames -> {r.dir}");
        }

        // ------------------------------------------------------------------------------------------------ KESTREL
        sealed class KState { public float speed, dashLeft, dashReady; public Vector3 dashDir; public int nextAttack; public bool combat; }

        /// KestrelController.Update with timeline inputs (no walking in this script; camera forward = +Z).
        static void KestrelStep(Rig r, KestrelController c, KState s, float t, float dt, bool dash, bool attack, bool cancel)
        {
            var a = r.anim; var tr = r.root;
            bool attacking = a.GetCurrentAnimatorStateInfo(0).IsTag("Attack")
                             || (a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).IsTag("Attack"));
            if (attack && (!attacking || cancel))     // cancel: chain over the recovery tail (the controller itself ignores LMB until the clip is 90% done)
            {
                a.SetInteger(AttackTypeId, s.nextAttack); a.SetTrigger(AttackId);
                s.nextAttack = (s.nextAttack + 1) % 3; s.combat = true; attacking = true;
            }
            bool stopping = a.GetCurrentAnimatorStateInfo(0).IsTag("DashStop");
            if (dash && s.dashLeft <= 0f && t >= s.dashReady && !attacking)
            {
                s.dashDir = tr.forward; s.dashLeft = c.dashTime; s.dashReady = t + c.dashTime + c.dashCooldown;
                tr.rotation = Quaternion.LookRotation(s.dashDir, Vector3.up);
                if (r.jet) r.jet.Ignite();
            }
            if (s.dashLeft > 0f)
            {
                s.dashLeft -= dt;
                if (r.jet) { if (s.dashLeft > 0f) r.jet.SetThrust(1f); else r.jet.Cutoff(); }
                s.speed = c.dashSpeed;
                tr.position += s.dashDir * (s.speed * dt);
                a.SetBool(DashId, s.dashLeft > 0f); a.SetFloat(SpeedId, 0f);
                return;
            }
            a.SetBool(DashId, false);
            if (stopping || attacking)
            {
                s.speed = Mathf.MoveTowards(s.speed, 0f, c.dashBrake * dt);
                tr.position += tr.forward * (s.speed * dt);
                a.SetFloat(SpeedId, 0f); a.SetBool(CombatId, s.combat);
                return;
            }
            s.speed = Mathf.MoveTowards(s.speed, 0f, c.acceleration * dt);
            tr.position += tr.forward * (s.speed * dt);
            a.SetFloat(SpeedId, s.speed); a.SetBool(CombatId, s.combat);
        }

        /// take a = cinematic orbit (front for the face + slashes), take b = plain third-person follow camera (behind-right).
        static void RecordKestrelInner()
        {
            RecordKestrelTake("a");
            RecordKestrelTake("b");
        }

        static void RecordKestrelTake(string take)
        {
            var r = Open(OYMKestrel.TestScene, "kestrel_" + take, typeof(KestrelController));
            var c = r.root.GetComponent<KestrelController>();
            var s = new KState();
            // timeline (s): face look -> idle -> Space dash -> dash stop (snap) -> LMB slash (Left) -> LMB slash (Charged, cancels the Left recovery)
            const float tDash = 2.2f, tSlash1 = 3.15f, tSlash2 = 5.3f, duration = 7.8f;
            var keys = take == "a"
                ? new[]
                {
                    CamKey.K(0.00f,  24f,  3f, 1.25f, 28f, 1.57f, 0f, 0.25f),
                    CamKey.K(0.08f,  24f,  3f, 0.95f, 28f, 1.57f, 0f, 1.00f),                 // slow push-in on the mask
                    CamKey.K(0.95f, 148f, 10f, 3.30f, 40f, 1.12f, 0f, 0.45f),                 // pull back + orbit to the rear-right
                    CamKey.K(tDash, 152f, 9f, 4.10f, 47f, 1.10f, 0.4f, 0.28f, 0.18f),         // dash: pull back, wider lens
                    CamKey.K(tDash + 0.5f, 40f, 6f, 3.70f, 40f, 1.05f, 0f, 0.55f, 0.20f),     // swing round to the front for the stop + slashes
                    CamKey.K(tSlash2 - 0.1f, 64f, 5f, 3.30f, 38f, 1.10f, 0.1f, 0.70f, 0.20f),
                }
                : new[]
                {
                    CamKey.K(0.00f,  24f,  3f, 1.25f, 28f, 1.57f, 0f, 0.25f),
                    CamKey.K(0.08f,  24f,  3f, 0.95f, 28f, 1.57f, 0f, 1.00f),
                    CamKey.K(0.95f, 158f, 10f, 3.40f, 40f, 1.12f, 0f, 0.45f),                 // pull back to the third-person view and stay behind-right
                    CamKey.K(tDash, 160f, 9f, 4.30f, 48f, 1.10f, 0.6f, 0.30f, 0.20f),
                    CamKey.K(tDash + 0.6f, 154f, 8f, 3.60f, 40f, 1.10f, 0.3f, 0.60f, 0.20f),
                };
            r.t = 0f;
            RecordLoop(r, duration, (t, dt) =>
            {
                KestrelStep(r, c, s, t, dt, Edge(t, dt, tDash), Edge(t, dt, tSlash1) || Edge(t, dt, tSlash2), Edge(t, dt, tSlash2));
                Advance(r, dt);
            }, keys);
            Close(r);
        }

        // ------------------------------------------------------------------------------------------------ ORBIT FRAME
        sealed class OState { public float speed, dashLeft, dashReady; public Vector3 dashDir; }

        /// OrbitFrameController.Update with timeline inputs (aiming along +Z).
        static void OrbitStep(Rig r, OrbitFrameController c, OState s, float t, float dt, bool aimHeld, bool fire, bool dash)
        {
            var a = r.anim; var tr = r.root;
            bool aim = aimHeld;
            if (fire) { aim = true; if (r.rail) r.rail.Fire(); }
            if (r.rail && r.rail.Charging) aim = true;
            if (dash && s.dashLeft <= 0f && t >= s.dashReady)
            {
                s.dashDir = tr.forward; s.dashLeft = c.dashTime; s.dashReady = t + c.dashTime + c.dashCooldown;
                tr.rotation = Quaternion.LookRotation(s.dashDir, Vector3.up);
                if (r.jet) r.jet.Ignite();
            }
            if (s.dashLeft > 0f)
            {
                s.dashLeft -= dt;
                if (r.jet) { if (s.dashLeft > 0f) r.jet.SetThrust(1f); else r.jet.Cutoff(); }
                s.speed = c.dashSpeed;
                tr.position += s.dashDir * (s.speed * dt);
                a.SetBool(DashId, s.dashLeft > 0f); a.SetBool(AimId, false); a.SetFloat(SpeedId, 0f);
                return;
            }
            a.SetBool(DashId, false);
            if (a.GetCurrentAnimatorStateInfo(0).IsTag("DashStop"))
            {
                s.speed = Mathf.MoveTowards(s.speed, 0f, c.dashBrake * dt);
                tr.position += tr.forward * (s.speed * dt);
                return;
            }
            if (aim) tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.LookRotation(Vector3.forward, Vector3.up), c.turnSpeed * 1.5f * dt);
            s.speed = Mathf.MoveTowards(s.speed, 0f, c.acceleration * dt);
            tr.position += tr.forward * (s.speed * dt);
            a.SetFloat(SpeedId, s.speed); a.SetBool(AimId, aim);
        }

        /// take a = cinematic (side view for the shots, behind for the dash, front for the stop), take b = third-person follow camera (behind-right).
        static void RecordOrbitFrameInner()
        {
            RecordOrbitFrameTake("a");
            RecordOrbitFrameTake("b");
        }

        static void RecordOrbitFrameTake(string take)
        {
            var r = Open(OYMOrbitFrame.TestScene, "orbitframe_" + take, typeof(OrbitFrameController));
            var c = r.root.GetComponent<OrbitFrameController>();
            var s = new OState();
            // timeline (s): idle -> RMB aim -> LMB fire x2 (charge 0.32 s, then the shot) -> release aim -> Space dash -> dash stop
            const float tAim = 0.8f, tFire1 = 1.6f, tFire2 = 3.1f, tAimEnd = 4.3f, tDash = 4.5f, duration = 7.4f;
            var keys = take == "a"
                ? new[]
                {
                    CamKey.K(0.00f,  30f,  4f, 3.50f, 38f, 1.20f, 0.2f, 0.30f),
                    CamKey.K(tAim,   96f,  6f, 3.55f, 40f, 1.28f, 0.85f, 0.65f),               // swing to the right side: the beam crosses the frame
                    CamKey.K(tAimEnd, 150f, 8f, 4.00f, 46f, 1.15f, 0.3f, 0.45f, 0.18f),        // behind the shoulder for the dash
                    CamKey.K(tDash + 0.55f, 36f, 6f, 3.80f, 40f, 1.05f, 0f, 0.60f, 0.20f),     // swing to the front for the stop
                }
                : new[]
                {
                    CamKey.K(0.00f,  30f,  4f, 3.50f, 38f, 1.20f, 0.2f, 0.30f),
                    CamKey.K(tAim,  142f,  7f, 3.40f, 42f, 1.30f, 0.7f, 0.65f),               // over the right shoulder: muzzle flash + beam go away from the camera
                    CamKey.K(tDash, 152f,  8f, 4.20f, 48f, 1.15f, 0.4f, 0.30f, 0.20f),
                    CamKey.K(tDash + 0.6f, 150f, 8f, 3.70f, 42f, 1.10f, 0.2f, 0.60f, 0.20f),
                };
            r.t = 0f;
            RecordLoop(r, duration, (t, dt) =>
            {
                bool aimHeld = t >= tAim && t < tAimEnd;
                OrbitStep(r, c, s, t, dt, aimHeld, Edge(t, dt, tFire1) || Edge(t, dt, tFire2), Edge(t, dt, tDash));
                Advance(r, dt);
            }, keys);
            Close(r);
        }
    }
}
