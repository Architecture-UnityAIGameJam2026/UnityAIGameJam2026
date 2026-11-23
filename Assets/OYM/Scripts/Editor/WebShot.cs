using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM.WebTools
{
    /// Quick look at the web (URP) picture without a WebGL build: plays a scene in the editor and saves screenshots.
    /// Unity -batchmode -projectPath . -buildTarget WebGL -force-glcore -webShot <outDir> -webShotScene <scene path>
    ///       [-webShotTimes 6,15,30] -executeMethod OYM.WebTools.WebShot.Run
    /// (-force-glcore: OpenGL depth conventions, as in WebGL). Exits by itself after the last shot.
    [InitializeOnLoad]
    public static class WebShot
    {
        const string KeyStarted = "OYM_WebShot_Started", KeyT0 = "OYM_WebShot_T0", KeyNext = "OYM_WebShot_Next";

        static WebShot()
        {
            if (Arg("-webShot") == null) return;
            EditorApplication.update += Tick;
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static float[] Times()
        {
            var s = Arg("-webShotTimes") ?? "6,15,30";
            return Array.ConvertAll(s.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void Run()
        {
            if (SessionState.GetBool(KeyStarted, false)) return;
            SessionState.SetBool(KeyStarted, true);
            var scene = Arg("-webShotScene") ?? "Assets/OYM/Scenes/MainTitle.unity";
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Debug.Log("[WEBSHOT] play " + scene);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            float now = (float)EditorApplication.timeSinceStartup;
            float t0 = SessionState.GetFloat(KeyT0, -1f);
            if (t0 < 0f) { SessionState.SetFloat(KeyT0, now); return; }
            var times = Times();
            int next = SessionState.GetInt(KeyNext, 0);
            if (next >= times.Length) return;
            if (now - t0 < times[next]) return;
            Shoot(next, now - t0);
            SessionState.SetInt(KeyNext, next + 1);
            if (next + 1 >= times.Length)
            {
                Debug.Log("[WEBSHOT] done");
                EditorApplication.Exit(0);
            }
        }

        static void Shoot(int i, float t)
        {
            var dir = Arg("-webShot");
            Directory.CreateDirectory(dir);
            Camera cam = null; float depth = float.NegativeInfinity;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled && c.targetTexture == null && c.cameraType == CameraType.Game && c.depth >= depth) { depth = c.depth; cam = c; }
            if (!cam) { Debug.LogWarning("[WEBSHOT] no camera"); return; }
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "webshot" };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var path = Path.Combine(dir, $"{i:00}_{scene}_{t:0}s.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release();
            Debug.Log($"[WEBSHOT] {path} camera {cam.name}");
        }
    }
}
