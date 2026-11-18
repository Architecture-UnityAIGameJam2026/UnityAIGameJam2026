// Renders look-dev screenshots of the debris field through HDRP (batch-mode friendly).
// Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMCapture.CaptureAll -quit
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OYM.EditorTools
{
    public static class OYMCapture
    {
        public static string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots"));

        [MenuItem("OYM/3. Capture Look-dev Shots")]
        public static void CaptureAll()
        {
            try
            {
                EditorSceneManager.OpenScene(OYMSetup.ScenePath);
                var M = OYMSetup.LoadManifest();
                Vector3 wc = new Vector3(M.wreck_center[0], M.wreck_center[1], M.wreck_center[2]);
                Vector3 sun = new Vector3(M.sun_dir[0], M.sun_dir[1], M.sun_dir[2]).normalized;
                Vector3 core = new Vector3(M.lights[0].pos[0], M.lights[0].pos[1], M.lights[0].pos[2]);
                Vector3 planet = OYMSetup.PlanetDir.normalized * OYMSetup.PlanetDistance;
                var shots = new (string name, Vector3 pos, Vector3 target)[]
                {
                    ("01_spawn_wreck", Vector3.zero, wc),
                    ("02_core_closeup", core + new Vector3(-300, 90, -340), core),
                    ("03_toward_sun", new Vector3(-150, -40, 250), new Vector3(-150, -40, 250) + sun * 1000f),
                    ("04_planet", new Vector3(0, 0, 100), planet),
                    ("05_asteroids", new Vector3(350, 60, 150), new Vector3(-900, -100, 1300)),
                    ("06_wide", new Vector3(-1700, 650, -1000), wc),
                    ("07_flythrough", wc + new Vector3(420, 40, -520), wc + new Vector3(-200, 0, 300)),
                };
                Directory.CreateDirectory(OutDir);
                var cam = Camera.main;
                int W = 1920, H = 1080;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                // warm-up: SSGI adds a green cast on the first ~40 frames of a fresh Unity process (uninitialised
                // history); burn those frames before the first real shot
                cam.transform.SetPositionAndRotation(shots[0].pos, Quaternion.LookRotation((shots[0].target - shots[0].pos).normalized, Vector3.up));
                for (int i = 0; i < 90; i++) cam.Render();
                foreach (var s in shots)
                {
                    cam.transform.position = s.pos;
                    cam.transform.rotation = Quaternion.LookRotation((s.target - s.pos).normalized, Vector3.up);
                    for (int i = 0; i < 40; i++) cam.Render();   // let TAA / SSGI / volumetric history converge
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(OutDir, s.name + ".png"), tex.EncodeToPNG());
                    Debug.Log("[OYM] captured " + s.name);
                }
                cam.targetTexture = null;
                rt.Release();
                Debug.Log("[OYM] Capture finished -> " + OutDir);
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM] Capture failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
