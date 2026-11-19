// Arena fog + brightness (11-19, user: "포그 느낌이 전체적으로 있고 (멀리 있는 건 형태만 해서 그 최적화하는), 맵이 어두워서
// 살짝 밝게"). Writes the look into OYM_SpaceVolume: DepthFocusFade fog (luminous dust, far debris -> soft shapes) and
// fog culling (OYM.FogCulling: lowest LOD in deep fog, not drawn once lost in it), lifted ambient + midtones, and a
// shadow distance that ends where the fog has taken over. Also renders before/after stills, a variant set for tuning,
// and counts what the G-buffer draws before/after the fog optimisation (wreck LODs + fog culling).
//   batch: Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMFogLook.BatchTune    (no -quit:
//          Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMFogLook.BatchApply   the job exits)
//   menu:  OYM > Look > Apply Fog Look,  OYM > Look > Capture Fog Compare
// Stills go to Screenshots/fog/<look>/<shot>.png, counts to Screenshots/fog/stats.json. Captures use a temporary
// in-memory override volume, so the profile asset is only written by Apply.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace OYM.EditorTools
{
    public static class OYMFogLook
    {
        const string Showcase = OYMSetup.Root + "/Scenes/Showcase_DebrisField.unity";
        const string Title = OYMSetup.Root + "/Scenes/MainTitle.unity";
        static string OutDir => Path.Combine(OYMCapture.OutDir, "fog");
        const int W = 1600, H = 900;
        const int TimedBursts = 6;                     // timed bursts per shot (looks 0 and 1) for ms/frame

        [Serializable]
        public struct Look
        {
            public string name;
            // depth fade (far focus)
            public float desaturation, darkenEV, haze;
            // fog
            public float fog, fogStart, fogDistance, fogMaxOpacity, fogSkyOpacity, fogBackdropOpacity;
            public Color fogColor;
            public float nebulaTint, nebulaBlur, nebulaGain, sunScatter, sunAnisotropy, silhouette;
            public bool culling;
            public float cullSilhouetteOpacity, cullPixels;
            // brightness
            public float ambient, midtoneLift, exposureComp;
            public float shadowDistance;
        }

        /// The look before the fog pass (OYM_SpaceVolume as of 11-19 03:00).
        public static readonly Look Before = new Look
        {
            name = "before",
            desaturation = 0.75f, darkenEV = 1.5f, haze = 0.4f,
            fog = 0f, fogStart = 25f, fogDistance = 1100f, fogMaxOpacity = 0.88f, fogSkyOpacity = 0.3f, fogBackdropOpacity = 0.18f,
            fogColor = new Color(0.36f, 0.42f, 0.48f), nebulaTint = 0.5f, nebulaBlur = 5f, nebulaGain = 2.5f,
            sunScatter = 0.03f, sunAnisotropy = 0.65f, silhouette = 0.45f,
            culling = false, cullSilhouetteOpacity = 0.5f, cullPixels = 1.5f,
            ambient = 5f, midtoneLift = 0f, exposureComp = 0f, shadowDistance = 2600f,
        };

        /// The chosen look (written by Apply, and by OYMDepthFade when it builds a profile).
        public static readonly Look Final = Variant("final", fog: 1f, fogDistance: 1100f, sky: 0.3f, color: new Color(0.36f, 0.42f, 0.48f),
            ambient: 7f, lift: 0.08f);

        static Look Variant(string name, float fog, float fogDistance, float sky, Color color, float ambient, float lift,
            float silhouette = 0.45f, float sun = 0.03f, float maxOpacity = 0.88f, float darken = 0.25f, float comp = 0f)
        {
            var l = Before;
            l.name = name;
            l.desaturation = 0.6f; l.darkenEV = darken; l.haze = 0.15f;
            l.fog = fog; l.fogDistance = fogDistance; l.fogSkyOpacity = sky; l.fogColor = color;
            l.fogMaxOpacity = maxOpacity; l.silhouette = silhouette; l.sunScatter = sun;
            l.culling = true;
            l.ambient = ambient; l.midtoneLift = lift; l.exposureComp = comp;
            l.shadowDistance = 1200f;
            return l;
        }

        /// Looks rendered by BatchTune (after "before"), to pick Final from.
        static Look[] TuneSet() => new[]
        {
            Variant("v1_light", 1f, 1500f, 0.2f, new Color(0.30f, 0.36f, 0.42f), 6.5f, 0.05f),
            Variant("v2_mid", 1f, 1100f, 0.3f, new Color(0.36f, 0.42f, 0.48f), 7f, 0.08f),
            Variant("v3_dense", 1f, 800f, 0.4f, new Color(0.42f, 0.48f, 0.54f), 8f, 0.1f, maxOpacity: 0.92f),
            Variant("v4_mid_pale", 1f, 1100f, 0.3f, new Color(0.36f, 0.42f, 0.48f), 7f, 0.08f, silhouette: 0f),
            Variant("v5_bright_nofog", 0f, 1100f, 0.3f, new Color(0.36f, 0.42f, 0.48f), 7f, 0.08f),
        };

        // ------------------------------------------------------------------------------------- profile writing ----

        static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet(out T c)) return c;
            c = p.Add<T>(false);
            c.name = typeof(T).Name;
            if (AssetDatabase.Contains(p)) AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        /// Writes a look into a profile (overrides only the parameters the look owns).
        public static void Write(VolumeProfile p, Look l)
        {
            var f = GetOrAdd<DepthFocusFade>(p);
            f.active = true;
            f.intensity.Override(1f);
            f.desaturation.Override(l.desaturation);
            f.darkenEV.Override(l.darkenEV);
            f.haze.Override(l.haze);
            f.fog.Override(l.fog);
            f.fogStart.Override(l.fogStart);
            f.fogDistance.Override(l.fogDistance);
            f.fogMaxOpacity.Override(l.fogMaxOpacity);
            f.fogSkyOpacity.Override(l.fogSkyOpacity);
            f.fogBackdropOpacity.Override(l.fogBackdropOpacity);
            f.fogColor.Override(l.fogColor);
            f.fogNebulaTint.Override(l.nebulaTint);
            f.fogNebulaBlur.Override(l.nebulaBlur);
            f.fogNebulaGain.Override(l.nebulaGain);
            f.fogSunScatter.Override(l.sunScatter);
            f.fogSunAnisotropy.Override(l.sunAnisotropy);
            f.fogSilhouette.Override(l.silhouette);
            f.fogCulling.Override(l.culling);
            f.cullSilhouetteOpacity.Override(l.cullSilhouetteOpacity);
            f.cullPixels.Override(l.cullPixels);

            var ilc = GetOrAdd<IndirectLightingController>(p);
            ilc.active = true;
            ilc.indirectDiffuseLightingMultiplier.Override(l.ambient);

            var lgg = GetOrAdd<LiftGammaGain>(p);
            lgg.active = true;
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, l.midtoneLift));

            var ex = GetOrAdd<Exposure>(p);
            ex.compensation.Override(l.exposureComp);

            var sh = GetOrAdd<HDShadowSettings>(p);
            sh.maxShadowDistance.Override(l.shadowDistance);

            if (AssetDatabase.Contains(p))
            {
                foreach (var c in p.components) EditorUtility.SetDirty(c);
                EditorUtility.SetDirty(p);
            }
        }

        [MenuItem("OYM/Look/Apply Fog Look")]
        public static void Apply() => WriteAsset(Final);

        /// A/B: puts the pre-fog look back (fog and culling off, old far darkening, ambient 5, shadows 2600 m).
        [MenuItem("OYM/Look/Revert Fog Look (before)")]
        public static void Revert() => WriteAsset(Before);

        static void WriteAsset(Look look)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(OYMDepthFade.ProfilePath);
            if (profile == null) { Debug.LogError("[OYM-FOG] no profile at " + OYMDepthFade.ProfilePath); return; }
            OYMDepthFade.AddTo(profile);
            Write(profile, look);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OYM-FOG] '{look.name}' look written to {OYMDepthFade.ProfilePath}");
        }

        // Team repo: no auto-apply on the first editor load (the art project has one). OYM_SpaceVolume.asset is committed
        // with the Final look already written, so every checkout gets it, and a teammate's later tweak to the profile is
        // never overwritten by someone else's fresh Library.

        // ------------------------------------------------------------------------------------------ batch ----
        // The captures run as jobs stepped from EditorApplication.update, a few camera renders per editor tick. A
        // synchronous loop of hundreds of cam.Render() calls inside one editor call keeps every frame's render
        // resources alive (no frame boundary) and drove another session's batch into page-file thrash (11-19, ~27 s
        // per frame). Batch runs must be started WITHOUT -quit: the job calls EditorApplication.Exit when done.

        public static void BatchTune() => Run(TuneJob());
        public static void BatchApply() => Run(ApplyJob());

        [MenuItem("OYM/Look/Capture Fog Compare")]
        public static void CaptureCompare() => Run(CaptureShowcase(new List<Look> { Before, Final }, "final"));

        static IEnumerator TuneJob()
        {
            var looks = new List<Look> { Before };
            looks.AddRange(TuneSet());
            return CaptureShowcase(looks, "tune");
        }

        static IEnumerator ApplyJob()
        {
            Apply();
            var a = CaptureShowcase(new List<Look> { Before, Final }, "final");
            while (a.MoveNext()) yield return null;
            var b = CaptureTitle(new List<Look> { Before, Final });
            while (b.MoveNext()) yield return null;
        }

        static IEnumerator s_Job;
        static bool s_ExitWhenDone;

        static void Run(IEnumerator job)
        {
            if (s_Job != null) { Debug.LogWarning("[OYM-FOG] a capture is already running"); return; }
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OYM-FOG] stop Play mode first"); return; }
            s_Job = job;
            s_ExitWhenDone = Application.isBatchMode;
            EditorApplication.update += Tick;
            Debug.Log("[OYM-FOG] job started");
        }

        static void Tick()
        {
            bool done;
            Exception err = null;
            try { done = !s_Job.MoveNext(); }
            catch (Exception e) { err = e; done = true; }
            if (!done) return;
            EditorApplication.update -= Tick;
            s_Job = null;
            if (err != null) Debug.LogError("[OYM-FOG] failed: " + err);
            else Debug.Log("[OYM-FOG] batch finished OK");
            if (s_ExitWhenDone) EditorApplication.Exit(err != null ? 1 : 0);
        }

        // ---------------------------------------------------------------------------------------- capture ----

        const int RendersPerTick = 4;

        sealed class Rig : IDisposable
        {
            public Camera cam;
            public RenderTexture rt;
            public Texture2D tex;
            public Volume volume;
            public List<PlayableGraph> graphs;
            public bool warm;
            public double lastMs;
            Texture2D m_Sync;

            public Rig(Camera c)
            {
                cam = c;
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var go = new GameObject("OYM FogLook (temp volume)") { hideFlags = HideFlags.DontSave, layer = 0 };
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 1000f;
                graphs = PoseIdle();
            }

            public void Use(Look l)
            {
                var p = ScriptableObject.CreateInstance<VolumeProfile>();
                p.hideFlags = HideFlags.DontSave;
                Write(p, l);
                if (volume.sharedProfile != null) Object.DestroyImmediate(volume.sharedProfile);
                volume.sharedProfile = p;
            }

            /// renders n frames, RendersPerTick per editor tick
            public IEnumerator Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    foreach (var g in graphs) g.Evaluate(0f);
                    cam.Render();
                    if ((i + 1) % RendersPerTick == 0) yield return null;
                }
            }

            /// warm-up frames (TAA / SSAO / volumetric history settle), then the still
            public IEnumerator Shoot(string path)
            {
                var f = Frames(warm ? 12 : 48);
                while (f.MoveNext()) yield return null;
                warm = true;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }

            /// waits for the GPU (a 1-pixel readback of the camera target)
            public void Sync()
            {
                if (m_Sync == null) m_Sync = new Texture2D(1, 1, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                m_Sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); m_Sync.Apply();
                RenderTexture.active = null;
            }

            /// milliseconds per frame (CPU + GPU, synchronised): bursts of RendersPerTick renders, one burst per tick
            public IEnumerator TimeFrames(int bursts)
            {
                double total = 0; int n = 0;
                for (int b = 0; b < bursts; b++)
                {
                    Sync();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < RendersPerTick; i++) cam.Render();
                    Sync();
                    total += sw.Elapsed.TotalMilliseconds; n += RendersPerTick;
                    yield return null;
                }
                lastMs = total / Math.Max(1, n);
            }

            public DepthFocusFade Settings()
            {
                var hd = HDCamera.GetOrCreate(cam);
                var s = hd != null && hd.volumeStack != null ? hd.volumeStack.GetComponent<DepthFocusFade>() : null;
                return s != null ? s : (volume.sharedProfile != null && volume.sharedProfile.TryGet(out DepthFocusFade d) ? d : null);
            }

            public void Dispose()
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(tex);
                if (m_Sync != null) Object.DestroyImmediate(m_Sync);
                if (volume != null)
                {
                    if (volume.sharedProfile != null) Object.DestroyImmediate(volume.sharedProfile);
                    Object.DestroyImmediate(volume.gameObject);
                }
                foreach (var g in graphs) g.Destroy();
            }
        }

        struct Shot { public string name; public Vector3 eye, look; public float fov; }

        static List<Shot> ShowcaseShots(Camera cam)
        {
            var start = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            Vector3 ctr = Vector3.zero; int n = 0;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { ctr += a.transform.position; n++; }
            ctr = n > 0 ? ctr / n + Vector3.up * 1.0f : new Vector3(0f, 1.2f, 0f);
            var wreck = new Vector3(0f, 47f, 949f);
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && (sun == null || l.intensity > sun.intensity)) sun = l;
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            // look 22 deg beside the sun so the disc and its flare do not fill the frame
            Vector3 nearSun = Quaternion.AngleAxis(22f, Vector3.up) * toSun;
            return new List<Shot>
            {
                new Shot { name = "s1_follow", eye = start.position, look = start.position + start.rotation * Vector3.forward * 100f, fov = start.fieldOfView },
                new Shot { name = "s2_wide_arena", eye = ctr + new Vector3(7f, 3f, -9f), look = ctr + new Vector3(0f, 0f, 18f), fov = 55f },
                new Shot { name = "s3_debris_path", eye = ctr + new Vector3(-16f, 11f, -22f), look = ctr + new Vector3(4f, 0f, 75f), fov = 55f },
                new Shot { name = "s4_toward_wreck", eye = ctr + new Vector3(-3f, 2f, -6f), look = wreck, fov = 60f },
                new Shot { name = "s5_near_sun", eye = ctr + new Vector3(0f, 2f, 0f) - nearSun * 6f, look = ctr + nearSun * 1000f, fov = 60f },
                new Shot { name = "s6_planet", eye = ctr + new Vector3(2f, 2f, -4f), look = ctr + OYMSetup.PlanetDir * 1000f, fov = 60f },
            };
        }

        static void Pose(Camera cam, Shot s)
        {
            cam.fieldOfView = s.fov;
            cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(s.look - s.eye, Vector3.up));
        }

        /// Showcase stills for each look. The first look is rendered as authored (no wreck LODs, no fog culling); the
        /// others with the fog optimisation on (wreck LODs added in memory, FogCulling evaluated per shot). Writes
        /// the drawn-geometry counts and synchronised ms/frame (batch editor, render target W x H) of the first and
        /// second look to stats_<tag>.json.
        static IEnumerator CaptureShowcase(List<Look> looks, string tag)
        {
            var scene = EditorSceneManager.OpenScene(Showcase);
            var cam = Camera.main;
            var shots = ShowcaseShots(cam);
            var stats = new StringBuilder();
            stats.Append("{\n  \"tag\": \"").Append(tag).Append("\",\n  \"shots\": [\n");
            var before = new Dictionary<string, (int r, long t, double ms)>();
            FogCulling cull = null;
            var rig = new Rig(cam);
            try
            {
                for (int li = 0; li < looks.Count; li++)
                {
                    var look = looks[li];
                    if (li == 1)
                    {
                        // the fog optimisation, as play mode sets it up: wreck LODs, then the culling scan
                        int added = WreckLods.Apply(scene);
                        var go = new GameObject(FogCulling.ObjectName) { hideFlags = HideFlags.DontSave };
                        cull = go.AddComponent<FogCulling>();
                        cull.Rescan();
                        Debug.Log($"[OYM-FOG] wreck LODs on {added} fragments, fog culling tracks {cull.Tracked} objects");
                    }
                    rig.Use(look);
                    foreach (var s in shots)
                    {
                        Pose(cam, s);
                        if (cull != null)
                        {
                            cam.Render();                               // let the volume stack pick up the look
                            yield return null;
                            if (look.culling) cull.EvaluateNow(cam, rig.Settings()); else cull.RestoreAll();
                        }
                        var shoot = rig.Shoot(Path.Combine(OutDir, tag, look.name, s.name + ".png"));
                        while (shoot.MoveNext()) yield return null;
                        var d = Drawn(cam, cull);
                        double ms = 0.0;
                        if (li <= 1)
                        {
                            var t = rig.TimeFrames(TimedBursts);
                            while (t.MoveNext()) yield return null;
                            ms = rig.lastMs;
                        }
                        if (li == 0) before[s.name] = (d.r, d.t, ms);
                        else if (li == 1)
                        {
                            var b = before[s.name];
                            stats.Append($"    {{ \"shot\": \"{s.name}\", \"before_renderers\": {b.r}, \"before_tris\": {b.t}, \"before_ms\": {b.ms:F2}, " +
                                $"\"after_renderers\": {d.r}, \"after_tris\": {d.t}, \"after_ms\": {ms:F2}, " +
                                $"\"silhouette\": {cull.Count(FogCulling.State.Silhouette)}, \"faded_out\": {cull.Count(FogCulling.State.FadedOut)} }},\n");
                            Debug.Log($"[OYM-FOG] {s.name}: drawn {b.r} -> {d.r} renderers, {b.t:N0} -> {d.t:N0} tris, {b.ms:F1} -> {ms:F1} ms/frame " +
                                $"(silhouette {cull.Count(FogCulling.State.Silhouette)}, faded out {cull.Count(FogCulling.State.FadedOut)} of {cull.Tracked})");
                        }
                        Debug.Log($"[OYM-FOG] captured {tag}/{look.name}/{s.name}");
                    }
                }
            }
            finally
            {
                rig.Dispose();
                if (cull != null) Object.DestroyImmediate(cull.gameObject);   // restores every renderer
            }
            stats.Append("    {}\n  ]\n}\n");
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, $"stats_{tag}.json"), stats.ToString());
        }

        static IEnumerator CaptureTitle(List<Look> looks)
        {
            EditorSceneManager.OpenScene(Title);
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[OYM-FOG] title has no main camera"); yield break; }
            var rig = new Rig(cam);
            try
            {
                foreach (var look in looks)
                {
                    rig.Use(look);
                    var shoot = rig.Shoot(Path.Combine(OutDir, "title", look.name + ".png"));
                    while (shoot.MoveNext()) yield return null;
                    Debug.Log("[OYM-FOG] captured title/" + look.name);
                }
            }
            finally { rig.Dispose(); }
        }

        // ------------------------------------------------------------------------ drawn geometry estimate ----

        static readonly Dictionary<Mesh, long> s_Tris = new Dictionary<Mesh, long>();

        static long Tris(Mesh m)
        {
            if (m == null) return 0;
            if (s_Tris.TryGetValue(m, out var t)) return t;
            t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += (long)m.GetIndexCount(i) / 3;
            s_Tris[m] = t;
            return t;
        }

        /// Opaque mesh renderers the camera's G-buffer pass would draw: in the frustum, enabled, not faded out by
        /// FogCulling, and (in LOD groups) the LOD Unity would pick for this view or the one FogCulling holds.
        static (int r, long t) Drawn(Camera cam, FogCulling cull)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 eye = cam.transform.position;
            var skip = new HashSet<Renderer>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                if (lods.Length == 0) continue;
                Vector3 ls = g.transform.lossyScale;
                float size = g.size * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                float dist = Mathf.Max(0.01f, Vector3.Distance(eye, g.transform.TransformPoint(g.localReferencePoint)));
                float rel = size / (2f * dist * tanHalf) * QualitySettings.lodBias;
                int sel = -1;
                for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { sel = i; break; }
                Renderer first = lods.SelectMany(l => l.renderers).FirstOrDefault(r => r != null);
                if (cull != null && first != null && cull.StateOf(first) == FogCulling.State.Silhouette) sel = lods.Length - 1;
                for (int i = 0; i < lods.Length; i++)
                    if (i != sel) foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            int n = 0; long tris = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || skip.Contains(r)) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                n++; tris += Tris(mf.sharedMesh);
            }
            return (n, tris);
        }

        static List<PlayableGraph> PoseIdle()
        {
            var graphs = new List<PlayableGraph>();
            foreach (var anim in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                var ctl = anim.runtimeAnimatorController;
                if (ctl == null) continue;
                var clip = ctl.animationClips.FirstOrDefault(c => c.name == "CombatIdle") ?? ctl.animationClips.FirstOrDefault(c => c.name.Contains("Idle")) ?? ctl.animationClips.FirstOrDefault();
                if (clip == null) continue;
                var g = PlayableGraph.Create("foglook-" + anim.name);
                var cp = AnimationClipPlayable.Create(g, clip);
                cp.SetApplyFootIK(true);
                AnimationPlayableOutput.Create(g, "out", anim).SetSourcePlayable(cp);
                cp.SetTime(0.6f); cp.SetTime(0.6f);
                g.Evaluate(0f);
                graphs.Add(g);
            }
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                smr.forceMatrixRecalculationPerRender = true;      // batch stills: skin every render, not once per editor frame
            return graphs;
        }
    }
}
