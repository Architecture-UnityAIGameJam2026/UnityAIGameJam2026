// OnYourMark - lighting / capture-path diagnostic (batch-mode friendly, not part of the scene build).
//
//   Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMDebug.LightTest -quit -logFile <log>
//
// Builds a throw-away scene (never saved) with a test sun, lit spheres/rocks with the project materials and
// HDRP/Unlit reference patches of known linear value, then renders a series of variants into
// Screenshots/dbg/<variant>.png plus dbg/probes.json (screen-space positions for automated pixel checks).
// The unlit patches go through exposure de-exposure, post FX and the PNG readback unchanged except for
// tonemapping, so a neutral grey patch that comes out tinted means the capture path itself is wrong.
// The volume profile is cloned in memory; nothing on disk is modified.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    public static class OYMDebug
    {
        static string s_firstOnly;   // set by the FirstFrame* entry points: only render the first-frames test

        // First frames of a fresh process with one effect disabled (one process per variant, since the transient
        // only shows up on the very first frames after Unity starts).
        public static void FirstFrameNoSSGI() { s_firstOnly = "noSSGI"; LightTest(); }
        public static void FirstFrameNoVolumetrics() { s_firstOnly = "noVolFog"; LightTest(); }

        [MenuItem("OYM/Debug/Lighting Diagnostic")]
        public static void LightTest()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var src = AssetDatabase.LoadAssetAtPath<VolumeProfile>(OYMSetup.SetDir + "/OYM_SpaceVolume.asset");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            foreach (var c in src.components) profile.components.Add(Object.Instantiate(c));
            var gv = new GameObject("GV").AddComponent<Volume>(); gv.isGlobal = true; gv.sharedProfile = profile; gv.priority = 10;

            Vector3 fwd = new Vector3(-0.5f, -0.4f, 0.7f).normalized;      // light travels away from camera, from upper-right
            var sunGo = new GameObject("Sun");
            var hdSun = sunGo.AddHDLight(LightType.Directional);
            var sun = sunGo.GetComponent<Light>();
            sun.lightUnit = LightUnit.Lux; sun.intensity = OYMSetup.SunLux;
            sun.useColorTemperature = true; sun.colorTemperature = 5500f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.LookRotation(fwd);
            Debug.Log($"[OYM-DBG] sun fwd={sunGo.transform.forward} finalColor={sun.color * Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature)}");

            var hdDef = new Material(Shader.Find("HDRP/Lit"));
            hdDef.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f)); hdDef.SetFloat("_Smoothness", 0.3f);
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Rock_A.mat");
            var hullMat = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Hull_Exterior.mat");
            var rockMesh = AssetDatabase.LoadAssetAtPath<Mesh>(OYMSetup.Gen + "/Rocks/rock_00_lod0.oymmesh");
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            var camGo = new GameObject("Cam"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();

            var probes = new List<string>();
            void Obj(string n, Mesh m, Material mat, Vector3 p, float s, string kind)
            {
                var g = new GameObject(n); g.transform.position = p; g.transform.localScale = Vector3.one * s;
                g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
                probes.Add($"{{\"name\":\"{n}\",\"kind\":\"{kind}\",\"pos\":[{p.x},{p.y},{p.z}],\"size\":{s}}}");
            }
            Obj("sph_grey80", sphere, hdDef, new Vector3(-9, 4, 30), 5f, "sphere");
            Obj("sph_rock", sphere, rockMat, new Vector3(-3, 4, 30), 5f, "sphere");
            Obj("sph_hull", sphere, hullMat, new Vector3(3, 4, 30), 5f, "sphere");
            Obj("rock_grey80", rockMesh, hdDef, new Vector3(-9, -3, 30), 5f, "rock");
            Obj("rock_rock", rockMesh, rockMat, new Vector3(-3, -3, 30), 5f, "rock");
            Obj("rock_hull", rockMesh, hullMat, new Vector3(3, -3, 30), 5f, "rock");
            // unlit reference patches (linear values written straight into the colour buffer)
            var patches = new (string n, Color c)[]
            {
                ("u_grey05", new Color(0.05f, 0.05f, 0.05f)), ("u_grey18", new Color(0.18f, 0.18f, 0.18f)),
                ("u_grey50", new Color(0.5f, 0.5f, 0.5f)), ("u_grey100", Color.white),
                ("u_red", new Color(0.5f, 0f, 0f)), ("u_green", new Color(0f, 0.5f, 0f)), ("u_blue", new Color(0f, 0f, 0.5f)),
            };
            for (int i = 0; i < patches.Length; i++)
            {
                var um = new Material(Shader.Find("HDRP/Unlit"));
                um.SetColor("_UnlitColor", patches[i].c);
                HDShaderUtils.ResetMaterialKeywords(um);
                Obj(patches[i].n, quad, um, new Vector3(-10.5f + i * 3.5f, -10f, 30), 2.6f, "unlit");
            }

            int W = 1280, H = 720;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create();
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            string outDir = Path.Combine(OYMCapture.OutDir, "dbg");
            Directory.CreateDirectory(outDir);
            // every variant starts on a brand-new camera so no temporal history (TAA/SSGI/SSR/volumetrics) carries over
            void FreshCamera()
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(cam.gameObject);
                camGo = new GameObject("Cam"); camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>(); camGo.AddComponent<HDAdditionalCameraData>();
                cam.fieldOfView = 60f; cam.transform.position = Vector3.zero;
                cam.targetTexture = rt;
            }
            void Shot(string n, int frames)
            {
                for (int i = 0; i < frames; i++) cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, n + ".png"), tex.EncodeToPNG());
                Debug.Log("[OYM-DBG] shot " + n);
            }
            T Get<T>() where T : VolumeComponent { profile.TryGet<T>(out var c); return c; }
            var fog = Get<Fog>(); var ssgi = Get<GlobalIllumination>(); var ssr = Get<ScreenSpaceReflection>();
            float baseH = fog.baseHeight.value, maxH = fog.maximumHeight.value;
            void FogAsIs() { fog.enabled.Override(true); fog.baseHeight.Override(baseH); fog.maximumHeight.Override(maxH); }
            void FogParked() { fog.enabled.Override(true); fog.baseHeight.Override(-100000f); fog.maximumHeight.Override(-99000f); }

            // A) first frames of the process with the fixed fog: must already be warm white (the green flash seen
            //    before only ever appeared on the very first frames with the 60 km fog layer)
            if (s_firstOnly != null)
            {
                FogParked();
                if (s_firstOnly == "noSSGI") ssgi.enable.Override(false);
                if (s_firstOnly == "noVolFog") fog.enableVolumetricFog.Override(false);
                FreshCamera();
                for (int f = 1; f <= 40; f++) { cam.Render(); if (f == 1 || f == 3 || f == 10 || f == 40) Shot($"z_first_{s_firstOnly}_f{f}", 0); }
                s_firstOnly = null;
                return;
            }
            FogParked(); FreshCamera(); Shot("a0_parked_first_f3", 3); Shot("a0_parked_first_f40", 37);
            // B) height-fog test: the old profile (fog layer base at +60 km) vs. fog off vs. layer parked 100 km below
            FogAsIs(); FreshCamera(); Shot("b0_fog_old_f3", 3); Shot("b0_fog_old_f120", 117);
            fog.enabled.Override(false); FreshCamera(); Shot("b1_fog_off_f60", 60);
            FogParked(); ssgi.enable.Override(false); FreshCamera(); Shot("b2_parked_noSSGI_f60", 60);
            ssgi.enable.Override(true);
            // D) direction check: side light from screen-right (L = +x) -> terminator must be vertical, right half lit
            sunGo.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0.15f));
            FreshCamera(); Shot("d0_sidelight_right_f40", 40);
            sunGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -1f, 0.15f));
            FreshCamera(); Shot("d1_toplight_f40", 40);
            sunGo.transform.rotation = Quaternion.LookRotation(fwd);
            // C) capture-path check: grading off -> unlit grey patches must read back neutral
            foreach (var c in profile.components)
                if (c is ColorAdjustments || c is WhiteBalance || c is ChromaticAberration || c is FilmGrain || c is Vignette || c is ScreenSpaceLensFlare || c is Bloom)
                    c.active = false;
            FreshCamera(); Shot("c0_parked_nograde_f60", 60);
            gv.enabled = false; FreshCamera(); Shot("c1_novolume_f60", 60);

            // probe sheet: screen-space centre + radius (pixels, origin top-left) for the python checker
            var sb = new StringBuilder("{\"W\":" + W + ",\"H\":" + H + ",\"L\":[" + (-fwd.x) + "," + (-fwd.y) + "," + (-fwd.z) + "],\"objects\":[");
            for (int i = 0; i < probes.Count; i++)
            {
                var j = probes[i];
                var go = GameObject.Find(j.Substring(9, j.IndexOf('"', 9) - 9));
                var b = go.GetComponent<Renderer>().bounds;
                var sc = cam.WorldToScreenPoint(b.center);
                var se = cam.WorldToScreenPoint(b.center + cam.transform.right * b.extents.x);
                float sr = Mathf.Abs(se.x - sc.x), sy = H - sc.y;
                sb.Append(j.TrimEnd('}')).Append(",\"sx\":" + sc.x.ToString("F1") + ",\"sy\":" + sy.ToString("F1") + ",\"sr\":" + sr.ToString("F1") + "}");
                if (i < probes.Count - 1) sb.Append(',');
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(outDir, "probes.json"), sb.ToString());
            Debug.Log("[OYM-DBG] gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
            Debug.Log("[OYM-DBG] done");
        }
    }
}
