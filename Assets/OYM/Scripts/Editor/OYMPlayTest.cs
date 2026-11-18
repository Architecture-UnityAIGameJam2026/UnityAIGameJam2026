// OnYourMark - zero-g character play test: KESTREL + ORBIT FRAME floating among drifting blocks and practice targets.
//   Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMPlayTest.BuildAll -quit
//       (prefabs + scene, then GripCheck: weapon-in-hand stills of the animated poses -> Screenshots/grip/)
//   Unity.exe -projectPath <proj> -executeMethod OYM.EditorTools.OYMPlayTest.OpenAndPlay            (play it yourself)
//   Unity.exe -projectPath <proj> -executeMethod OYM.EditorTools.OYMPlayTest.OpenAndPlay -oymDemo    (scripted demo,
//       records Screenshots/play/demo_*/ + GIFs, then quits; -oymDemoTakes kestrel_fp,orbit_fp for a subset)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    public static class OYMPlayTest
    {
        public const string ScenePath = OYMSetup.Root + "/Scenes/CharacterPlayTest.unity";
        const string Dir = OYMSetup.Root + "/Characters";

        [MenuItem("OYM/Characters/Build Play Test (Kestrel + Orbit Frame)")]
        public static void BuildAll()
        {
            try
            {
                OYMKestrel.Build();
                OYMOrbitFrame.Build();
                BuildScene();
                GripCheck();
                Debug.Log("[OYM-PT] BuildAll finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-PT] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static Material LitMat(string name, Color c, float metal, float smooth, bool emissive = false)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (emissive)
            {
                m.SetFloat("_UseEmissiveIntensity", 0f); m.SetColor("_EmissiveColorLDR", Color.black);
                m.SetColor("_EmissiveColor", Color.black); m.SetFloat("_EmissiveExposureWeight", 0f);   // flashes are screen-relative HDR
            }
            HDShaderUtils.ResetMaterialKeywords(m); EditorUtility.SetDirty(m);
            return m;
        }

        static OYM.ImpactFX BuildImpact()
        {
            var go = new GameObject("ImpactFX");
            var fade = OYMKestrel.Grad(new[] { (0f, Color.white), (0.3f, new Color(0.8f, 0.9f, 1f)), (1f, new Color(0.35f, 0.55f, 1f)) },
                                       new[] { (0f, 1f), (0.35f, 0.8f), (1f, 0f) });
            var flashMat = OYMKestrel.FxMat("FX_ImpactFlash", "fx_flare", new Color(0.85f, 0.93f, 1f), 30f);
            var coreMat = OYMKestrel.FxMat("FX_ImpactCore", "fx_glow", Color.white, 55f);
            var sparkMat = OYMKestrel.FxMat("FX_ImpactSpark", "fx_glow", new Color(1f, 0.86f, 0.62f), 16f);
            var rot = Quaternion.identity;
            var flash = OYMKestrel.Ps("Flash", go.transform, rot, flashMat, false, true, new Vector2(0.1f, 0.13f), Vector2.zero, new Vector2(1.1f, 1.3f), fade,
                                      new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1.1f), new Keyframe(1, 1.25f)));
            var core = OYMKestrel.Ps("Core", go.transform, rot, coreMat, false, true, new Vector2(0.07f, 0.09f), Vector2.zero, new Vector2(0.4f, 0.45f), fade,
                                     new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.3f)));
            var sparks = OYMKestrel.Ps("Sparks", go.transform, rot, sparkMat, true, true, new Vector2(0.15f, 0.42f), new Vector2(6f, 22f), new Vector2(0.012f, 0.03f), fade,
                                       new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.3f)));
            var sh = sparks.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 62f; sh.radius = 0.04f;
            sparks.GetComponent<ParticleSystemRenderer>().lengthScale = 2.4f;
            var light = OYMKestrel.FxLight("ImpactLight", go.transform, new Color(0.85f, 0.92f, 1f), 7f, 0.1f);
            light.transform.localPosition = Vector3.forward * 0.25f;
            var fx = go.AddComponent<OYM.ImpactFX>();
            fx.flash = flash; fx.core = core; fx.sparks = sparks; fx.flashLight = light;
            return fx;
        }

        static Vector3 EyeOffset(GameObject inst)
        {
            var eye = inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "EyeGlowSocket" && !t.GetComponentInParent<OYM.FirstPersonRig>(true));
            Vector3 l = inst.transform.InverseTransformPoint(eye.position);
            return new Vector3(0f, l.y, l.z + 0.03f);
        }

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gv = new GameObject("Global Volume (dark map)");
            var vol = gv.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = OYMKestrel.TestVolume(Dir + "/PlayTest_Volume.asset");
            OYMKestrel.DirLight("Key (planetshine)", 1500f, new Color(0.80f, 0.86f, 1.0f), new Vector3(35, 150, 0));
            OYMKestrel.DirLight("Rim (star)", 4000f, new Color(0.65f, 0.85f, 1.0f), new Vector3(20, -20, 0), false);

            var deck = LitMat("PlayTest_Deck", new Color(0.05f, 0.052f, 0.058f), 0.15f, 0.28f);
            var block = LitMat("PlayTest_Block", new Color(0.075f, 0.078f, 0.085f), 0.55f, 0.42f);
            var tgtMat = LitMat("PlayTest_Target", new Color(0.16f, 0.15f, 0.14f), 0.7f, 0.5f, true);

            // a station hull far below (depth cue + shadows), drifting blocks all around: there is no floor to stand on
            var hull = GameObject.CreatePrimitive(PrimitiveType.Plane);
            hull.name = "Hull"; hull.transform.position = new Vector3(0f, -6f, 0f); hull.transform.localScale = new Vector3(14, 1, 14);
            hull.GetComponent<MeshRenderer>().sharedMaterial = deck;
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            for (int i = 0; i < 34; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Block_" + i;
                float a = R() * Mathf.PI * 2f, r = 7f + R() * 26f;
                c.transform.position = new Vector3(Mathf.Cos(a) * r, -3f + R() * 16f, Mathf.Sin(a) * r);
                c.transform.rotation = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
                float s = 0.6f + R() * R() * 3.4f;
                c.transform.localScale = new Vector3(s * (0.5f + R()), s * (0.5f + R()), s * (0.5f + R()));
                c.GetComponent<MeshRenderer>().sharedMaterial = block;
                if (R() < 0.6f) { var sp = c.AddComponent<OYM.Spin>(); sp.axis = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f); sp.degreesPerSecond = 2f + R() * 6f; }
            }
            var targets = new OYM.ZeroGTarget[5];
            for (int i = 0; i < targets.Length; i++)
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                t.name = "Target_" + i; t.transform.localScale = Vector3.one * (0.75f + 0.1f * i);
                t.transform.position = new Vector3(-2f + i, 2f + (i % 2), 6f + 2.5f * i);
                t.GetComponent<MeshRenderer>().sharedMaterial = tgtMat;
                var rb = t.AddComponent<Rigidbody>(); rb.useGravity = false; rb.mass = 3f; rb.linearDamping = 0.12f; rb.angularDamping = 0.15f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                targets[i] = t.AddComponent<OYM.ZeroGTarget>();
            }
            BuildImpact();

            var kestrel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OYMKestrel.PrefabPath));
            kestrel.transform.SetPositionAndRotation(new Vector3(-1.3f, 1.5f, 0f), Quaternion.identity);
            var orbit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OYMOrbitFrame.PrefabPath));
            orbit.transform.SetPositionAndRotation(new Vector3(1.4f, 1.5f, 0f), Quaternion.identity);

            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 80000f; cam.fieldOfView = 55f;
            camGo.AddComponent<HDAdditionalCameraData>().antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(-0.8f, 3.2f, -3.6f);
            camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var fc = camGo.AddComponent<OYM.FlightCamera>(); fc.target = kestrel.transform;

            var kc = kestrel.GetComponent<OYM.KestrelController>(); kc.cameraTransform = camGo.transform;
            var oc = orbit.GetComponent<OYM.OrbitFrameController>(); oc.cameraTransform = camGo.transform;
            var dir = new GameObject("PlayTest").AddComponent<OYM.PlayTestDirector>();
            dir.controllers = new OYM.ZeroGFlight[] { kc, oc };
            dir.names = new[] { "KESTREL", "ORBIT FRAME" };
            dir.flightCamera = fc;
            dir.pivotOffsets = new[] { new Vector3(0f, 1.45f, 0f), new Vector3(0f, 1.6f, 0f) };
            dir.eyeOffsets = new[] { EyeOffset(kestrel), EyeOffset(orbit) };
            dir.targets = targets;
            fc.eyeOffset = dir.eyeOffsets[0];
            Debug.Log($"[OYM-PT] eye offsets KESTREL {dir.eyeOffsets[0]}, ORBIT FRAME {dir.eyeOffsets[1]}");

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[OYM-PT] play test scene saved " + ScenePath);
        }

        [MenuItem("OYM/Characters/Open Play Test")]
        public static void OpenAndPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var gameView = Type.GetType("UnityEditor.GameView,UnityEditor");        // play maximized: no Scene view rendering
            if (gameView != null) { var gv = EditorWindow.GetWindow(gameView); gv.maximized = true; gv.Focus(); }
            EditorApplication.EnterPlaymode();
        }

        // ------------------------------------------------------------------------------------------------ grip check
        /// Edit-mode stills of the animated poses through the Unity humanoid retarget: is the weapon in the fist, does the
        /// RG-77 point where the aim clip says? Logs the barrel / blade direction and renders the right hand up close.
        [MenuItem("OYM/Characters/Grip Check (stills)")]
        public static void GripCheck()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gv = new GameObject("vol"); var vol = gv.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = OYMKestrel.TestVolume(Dir + "/GripCheck_Volume.asset");
            OYMKestrel.DirLight("key", 9000f, new Color(1f, 0.97f, 0.94f), new Vector3(30, 140, 0));
            OYMKestrel.DirLight("fill", 5000f, new Color(0.8f, 0.88f, 1f), new Vector3(15, -40, 0), false);
            OYMKestrel.DirLight("under", 2500f, new Color(0.8f, 0.85f, 1f), new Vector3(-50, 60, 0), false);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.02f;
            camGo.AddComponent<HDAdditionalCameraData>().antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/grip"));
            Directory.CreateDirectory(outDir);
            int W = 900, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var jobs = new (string prefab, string animDir, string weapon, string[] shots)[]
            {
                (OYMKestrel.PrefabPath, OYMKestrel.AnimDir, "Kestrel_Sword", new[] { "#0:1.2", "#1:1.2", "CombatIdle:0.4", "Slash_Left:1.50", "Slash_Charged:1.40" }),
                (OYMOrbitFrame.PrefabPath, OYMOrbitFrame.AnimDir, "OrbitFrame_Railgun", new[] { "#1:1.2", "AimIdle:0.5" }),
            };
            foreach (var job in jobs) foreach (var shotOne in job.shots)       // fresh instance per shot: edit mode skins once per frame
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(job.prefab));
                go.transform.position = Vector3.zero;
                var anim = go.GetComponentsInChildren<Animator>(true).First(a => !a.GetComponentInParent<OYM.FirstPersonRig>(true));
                var all = anim.GetComponentsInChildren<Transform>(true);
                var weapon = all.First(t => t.name == job.weapon);
                var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;            // edit mode: never skip the pose update
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
                var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { job.animDir })
                    .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                    .Where(c => !c.name.StartsWith("__preview__")).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
                var graph = PlayableGraph.Create("grip"); var output = AnimationPlayableOutput.Create(graph, "o", anim);
                foreach (var shot in new[] { shotOne })
                {
                    string cn = shot.Split(':')[0]; float t = float.Parse(shot.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
                    bool composite = cn.StartsWith("#");
                    AnimationClipPlayable cp = default;
                    if (composite)
                    {
                        // the real controller: hover base layer + UpperBody (weight from the shot), like in play
                        float w = float.Parse(cn.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                        cn = w > 0.5f ? "Animator_Upper" : "Animator_Hover";
                        anim.Rebind();
                        foreach (var prm in anim.parameters)
                            if (prm.type == AnimatorControllerParameterType.Bool && (prm.name == "Aim" || prm.name == "Combat")) anim.SetBool(prm.name, true);
                        anim.Update(0f);
                        for (float tt = 0f; tt < t; tt += 0.05f) { anim.SetLayerWeight(1, w); anim.Update(0.05f); }
                    }
                    else
                    {
                        if (!clips.TryGetValue(cn, out var clip)) { Debug.LogWarning("[OYM-PT] grip check: no clip " + cn); continue; }
                        cp = AnimationClipPlayable.Create(graph, clip); output.SetSourcePlayable(cp);
                        cp.SetTime(t); cp.SetTime(t); graph.Evaluate(0f);
                    }
                    string msg;
                    var muz = all.FirstOrDefault(x => x.name == "MuzzleSocket"); var rs = all.FirstOrDefault(x => x.name == "RailStartSocket");
                    var tip = all.FirstOrDefault(x => x.name == "BladeTipSocket");
                    Vector3 axis = muz && rs ? (muz.position - rs.position).normalized : (tip.position - weapon.position).normalized;
                    msg = $"axis {axis.ToString("F2")} (yaw {Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg:F0}, pitch {Mathf.Asin(axis.y) * Mathf.Rad2Deg:F0} deg)";
                    Debug.Log($"[OYM-PT] grip {go.name} {cn}@{t:F2}: weapon {msg}, hand->grip {Vector3.Distance(hand.position, weapon.parent.position) * 100f:F1} cm");
                    string baseName = $"{go.name}_{cn}_{t:F2}".Replace('.', '_');
                    var views = new (string tag, Vector3 eye, Vector3 look, float fov)[]
                    {
                        ("full", new Vector3(2.0f, 1.5f, 2.4f), new Vector3(0f, 1.1f, 0.2f), 40f),
                        ("hand", hand.position + new Vector3(0.35f, 0.15f, 0.30f), hand.position, 34f),
                        ("hand2", hand.position + new Vector3(-0.05f, 0.25f, 0.42f), hand.position, 34f),
                    };
                    if (composite)
                        views = new (string tag, Vector3 eye, Vector3 look, float fov)[]
                        {
                            ("full", new Vector3(2.0f, 1.5f, 2.4f), new Vector3(0f, 1.0f, 0.2f), 40f),
                            ("side", new Vector3(-3.4f, 1.1f, 0.25f), new Vector3(0f, 0.95f, 0.25f), 38f),
                            ("side2", new Vector3(3.4f, 1.1f, 0.25f), new Vector3(0f, 0.95f, 0.25f), 38f),
                        };
                    for (int v = 0; v < views.Length; v++)
                    {
                        var vw = views[v];
                        cam.fieldOfView = vw.fov; cam.transform.SetPositionAndRotation(vw.eye, Quaternion.LookRotation(vw.look - vw.eye));
                        for (int i = 0; i < 6; i++) { if (!composite) graph.Evaluate(0f); cam.Render(); }      // few renders: batch Camera.Render memory grows per call
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{baseName}_{vw.tag}.png"), tex.EncodeToPNG());
                    }
                    if (cp.IsValid()) cp.Destroy();
                }
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(go);
            }
            cam.targetTexture = null; rt.Release();
            Debug.Log("[OYM-PT] grip check -> " + outDir);
        }
    }
}
