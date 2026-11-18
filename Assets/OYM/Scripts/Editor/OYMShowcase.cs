// OnYourMark - showcase: debris field + HUSK enemies + the playable characters (KESTREL / ORBIT FRAME) in one scene.
//
//   batch:       Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMShowcase.BuildAndCapture -quit
//   interactive: Unity.exe -projectPath <proj> -executeMethod OYM.EditorTools.OYMShowcase.OpenAndPlay
//                (or menu OYM/Showcase/Open + Play)  -> Tab switches character, mouse orbits the follow camera
//
// Built from other pipelines' scenes so it never goes stale and has no compile-time dependency on them:
//   * OYMEnemies.BuildTestScene()  -> DebrisField + enemies (inspection row removed)
//   * Scenes/CharacterPlayTest.unity (character session, OYMPlayTest) merged in: characters, follow camera and the
//     PlayTest director are kept; its dark-test volume, lights, deck and pillars are dropped (the debris field's
//     sun / planetshine / space volume light them instead).
// The characters hover in zero-g over a real wreck fragment; a floating debris path leads on (OYMDebrisPath).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace OYM.EditorTools
{
    public static class OYMShowcase
    {
        public const string ScenePath = OYMSetup.Root + "/Scenes/Showcase_DebrisField.unity";
        const string CharScene = OYMSetup.Root + "/Scenes/CharacterPlayTest.unity";

        [MenuItem("OYM/Showcase/Build (debris field + enemies + characters)")]
        public static void Build()
        {
            if (!File.Exists(CharScene)) throw new FileNotFoundException("character play test scene missing (OYM/Characters/Build Play Test)", CharScene);
            OYMEnemies.BuildTestScene();                                       // opens DebrisField, places enemies, saves the test scene
            var dst = SceneManager.GetActiveScene();
            var row = GameObject.Find("HUSK Enemies (test)/Inspection row");
            if (row) Object.DestroyImmediate(row);

            var oldCam = dst.GetRootGameObjects().First(g => g.name == "Main Camera");
            var src = EditorSceneManager.OpenScene(CharScene, OpenSceneMode.Additive);
            foreach (var go in src.GetRootGameObjects())
            {
                string n = go.name;
                bool keep = go.GetComponentInChildren<Animator>(true) != null || go.GetComponent<Camera>() != null || n == "PlayTest";
                if (!keep) Object.DestroyImmediate(go);                        // dark-test volume, key/rim lights, deck, pillars
            }
            SceneManager.MergeScenes(src, dst);
            var newCam = dst.GetRootGameObjects().First(g => g.name == "Main Camera" && g != oldCam);
            var dust = oldCam.transform.Find("SpaceDust");
            if (dust) dust.SetParent(newCam.transform, false);                 // keep the drifting space dust around the player
            newCam.GetComponent<Camera>().farClipPlane = 80000f;
            Object.DestroyImmediate(oldCam);

            // no flat floor (user feedback 11-18): the characters stand on a real wreck fragment, more fragments link
            // up into a floating path towards the wreck, and rocks / asteroids drift around it
            var M = OYMSetup.LoadManifest();
            var wc = new Vector3(M.wreck_center[0], M.wreck_center[1], M.wreck_center[2]);
            var stones = OYMDebrisPath.BuildPath(M, wc);
            OYMDebrisPath.AddDrifters(M, stones);

            EditorSceneManager.SaveScene(dst, ScenePath);
            Debug.Log("[OYM-SC] showcase scene saved -> " + ScenePath + "  roots: " + string.Join(", ", dst.GetRootGameObjects().Select(g => g.name)));
        }

        [MenuItem("OYM/Showcase/Open + Play")]
        public static void OpenAndPlay()
        {
            if (!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildAndCapture()
        {
            try
            {
                Build();
                Capture();
                Debug.Log("[OYM-SC] BuildAndCapture finished OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-SC] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // enemy materials/prefabs refresh + showcase + enemy glow re-shots, in one Unity session
        public static void RefreshAll()
        {
            try
            {
                OYMEnemies.BuildPrefabs();                                       // re-applies the enemy emission levels
                Build();
                Capture();
                Debug.Log("[OYM-SC] RefreshAll finished OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-SC] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // stills in edit mode: every character is posed on an idle clip of its own controller
        [MenuItem("OYM/Showcase/Capture Stills")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var cam = Camera.main;
            var graphs = new List<PlayableGraph>();
            foreach (var anim in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                var ctl = anim.runtimeAnimatorController;
                if (ctl == null) continue;
                var clip = ctl.animationClips.FirstOrDefault(c => c.name == "CombatIdle") ?? ctl.animationClips.FirstOrDefault(c => c.name.Contains("Idle")) ?? ctl.animationClips.FirstOrDefault();
                if (clip == null) continue;
                var g = PlayableGraph.Create("showcase-" + anim.name);
                var cp = AnimationClipPlayable.Create(g, clip);
                cp.SetApplyFootIK(true);
                AnimationPlayableOutput.Create(g, "out", anim).SetSourcePlayable(cp);
                cp.SetTime(0.6f); cp.SetTime(0.6f);
                g.Evaluate(0f);
                graphs.Add(g);
                Debug.Log($"[OYM-SC] posed {anim.transform.root.name} with '{clip.name}'");
            }
            var start = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            // frame on where the characters actually are (they float in zero-g, y ~1.5 m above the deck)
            Vector3 ctr = Vector3.zero; int n = 0;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { ctr += a.transform.position; n++; }
            ctr = n > 0 ? ctr / n + Vector3.up * 1.0f : new Vector3(0f, 1.2f, 0f);
            var shots = new (string name, Vector3 eye, Vector3 look, float fov)[]
            {
                ("s01_follow_cam", start.position, start.position + start.rotation * Vector3.forward, start.fieldOfView),
                ("s02_front_sunlit", ctr + new Vector3(2.6f, 0.5f, 4.6f), ctr, 40f),
                ("s03_over_shoulder_pack", ctr + new Vector3(-2.2f, 0.6f, -2.6f), ctr + new Vector3(2f, 0f, 22f), 55f),
                ("s04_wide_arena", ctr + new Vector3(7f, 3f, -9f), ctr + new Vector3(0f, 0f, 18f), 55f),
                ("s05_debris_path", ctr + new Vector3(-16f, 11f, -22f), ctr + new Vector3(4f, 0f, 75f), 55f),
                ("s06_wick_eyes", new Vector3(0.6f, 1.6f, 9f), new Vector3(0f, 1.5f, 24f), 42f),
            };
            string outDir = Path.Combine(OYMCapture.OutDir, "showcase");
            Directory.CreateDirectory(outDir);
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            bool warm = false;
            foreach (var s in shots)
            {
                cam.fieldOfView = s.fov;
                cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(s.look - s.eye, Vector3.up));
                for (int i = 0; i < (warm ? 40 : 120); i++) { foreach (var g in graphs) g.Evaluate(0f); cam.Render(); }
                warm = true;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, s.name + ".png"), tex.EncodeToPNG());
                Debug.Log("[OYM-SC] captured " + s.name);
            }
            cam.targetTexture = null; rt.Release();
            foreach (var g in graphs) g.Destroy();
        }
    }
}
