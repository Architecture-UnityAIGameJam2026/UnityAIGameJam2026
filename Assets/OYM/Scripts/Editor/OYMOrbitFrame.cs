// OnYourMark - ORBIT FRAME (golden-monkey railgun soldier) playable character: Meshy-rigged body + transplanted helmet
// bust + RG-77 railgun in both hands + amber eye glow + amber booster FX + railgun charge / shot FX.
//
//   1) python -I pipeline/orbit_frame/prep_unity.py   (OrbitFrame.fbx, clip FBXs, baked textures -> Assets/OYM/Characters/OrbitFrame)
//   2) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMOrbitFrame.BuildAndCapture -quit
//
// Shares the humanoid / FX / test-volume helpers with OYMKestrel. Test scene Scenes/OrbitFrameTest.unity
// (WASD / Shift run / Space dash / RMB aim / LMB fire), captures in Screenshots/orbitframe/*.png.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    public static class OYMOrbitFrame
    {
        public const string Dir = OYMSetup.Root + "/Characters/OrbitFrame";
        public const string ModelPath = Dir + "/OrbitFrame.fbx";
        public const string AnimDir = Dir + "/Animations";
        public const string TexDir = Dir + "/Textures";
        public const string PrefabPath = Dir + "/OrbitFrame.prefab";
        public const string ControllerPath = Dir + "/AC_OrbitFrame.controller";
        public const string VolumePath = Dir + "/OrbitFrame_TestVolume.asset";
        public const string TestScene = OYMSetup.Root + "/Scenes/OrbitFrameTest.unity";

        static readonly Dictionary<string, bool> Clips = new Dictionary<string, bool>
        {
            { "Idle", true }, { "Walk", true }, { "Run", true }, { "WalkShoot", true }, { "AimIdle", true },
            { "DashLoop", true }, { "DashStop", false },
        };

        const float EyeGlow = 6f;
        static readonly Color Amber = new Color(1f, 0.66f, 0.25f);
        static readonly Color RailBlue = new Color(0.45f, 0.72f, 1f);

        internal static readonly OYMKestrel.JetPalette OrbitJet = new OYMKestrel.JetPalette
        {
            prefix = "FX_OF_",
            plume = new Color(1f, 0.74f, 0.4f), core = new Color(1f, 0.93f, 0.78f), flash = new Color(1f, 0.86f, 0.62f), spark = new Color(1f, 0.8f, 0.5f),
            light = new Color(1f, 0.72f, 0.42f), flashLight = new Color(1f, 0.85f, 0.66f),
            mid = new Color(1f, 0.78f, 0.45f), tail = new Color(1f, 0.38f, 0.12f), fadeMid = new Color(1f, 0.82f, 0.55f), fadeTail = new Color(1f, 0.45f, 0.2f),
            flashSize = 2.1f, plumeSpeed = 14f, plumeLen = 1.3f,
        };

        [MenuItem("OYM/Characters/Orbit Frame: Build + Capture")]
        public static void BuildAndCapture()
        {
            try
            {
                Build();
                BuildTestScene();
                Capture();
                Debug.Log("[OYM-OF] BuildAndCapture finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-OF] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("OYM/Characters/Orbit Frame: Build Prefab")]
        public static void Build()
        {
            var mats = Materials();
            var avatar = ImportModel(mats);
            var clips = ImportAnimations();
            var ctrl = BuildController(clips);
            BuildPrefab(avatar, ctrl);
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Texture2D Tex(string name, bool normal, bool srgb, int max)
        {
            string path = $"{TexDir}/{name}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new Exception("texture missing " + path + " (run pipeline/orbit_frame/prep_unity.py)");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != max)
            {
                ti.textureType = type; ti.sRGBTexture = srgb; ti.maxTextureSize = max;
                ti.anisoLevel = 8; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material PbrMat(string part, int max)
        {
            string path = $"{Dir}/M_OrbitFrame_{part}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", Tex($"OrbitFrame_{part}_albedo", false, true, max));
            m.SetTexture("_NormalMap", Tex($"OrbitFrame_{part}_normal", true, false, max));
            m.SetFloat("_NormalScale", 1f);
            m.SetTexture("_MaskMap", Tex($"OrbitFrame_{part}_mask", false, false, max));
            m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
            m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f);
            m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
            return m;
        }

        static Dictionary<string, Material> Materials()
        {
            var body = PbrMat("Body", 2048);
            var gun = PbrMat("Railgun", 2048);
            var head = PbrMat("Head", 2048);
            // amber eye slits: screen-relative HDR so they read and bloom in dark and bright maps
            head.SetTexture("_EmissiveColorMap", Tex("OrbitFrame_Head_eyeglow", false, true, 2048));
            head.SetFloat("_UseEmissiveIntensity", 0f);
            head.SetColor("_EmissiveColorLDR", Color.white);
            head.SetColor("_EmissiveColor", Color.white * EyeGlow);
            head.SetFloat("_EmissiveExposureWeight", 0f);
            foreach (var m in new[] { body, gun, head }) { HDShaderUtils.ResetMaterialKeywords(m); EditorUtility.SetDirty(m); }
            AssetDatabase.SaveAssets();
            return new Dictionary<string, Material> { ["M_OrbitFrame_Body"] = body, ["M_OrbitFrame_Head"] = head, ["M_OrbitFrame_Railgun"] = gun };
        }

        // ------------------------------------------------------------------------------------------------ model / clips
        static Avatar ImportModel(Dictionary<string, Material> mats)
        {
            OYMKestrel.SetupHumanoid(ModelPath, false, mi =>
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.indexFormat = ModelImporterIndexFormat.UInt32;
                mi.isReadable = true;                                    // the first-person arms mesh is cut from LOD0
            });
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();
            Debug.Log("[OYM-OF] avatar OK: " + avatar.name);
            return avatar;
        }

        static Dictionary<string, AnimationClip> ImportAnimations()
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (var path in Directory.GetFiles(AnimDir, "*.fbx").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
            {
                string clipName = Path.GetFileNameWithoutExtension(path).Replace("OrbitFrame_", "");
                if (!Clips.TryGetValue(clipName, out bool loop)) { Debug.LogWarning("[OYM-OF] unknown clip file " + path); continue; }
                OYMKestrel.SetupClipAvatar(path, AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First());
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                var takes = mi.defaultClipAnimations;
                if (takes.Length == 0) throw new Exception("no take in " + path);
                var c = takes.OrderByDescending(t => t.lastFrame - t.firstFrame).First();
                c.name = clipName; c.loopTime = loop; c.loopPose = false;
                c.lockRootRotation = true; c.keepOriginalOrientation = true; c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;   // exact authored pose: the bladed rifle stance keeps its yaw, the barrel stays on the aim
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview__"));
                result[clipName] = clip;
                Debug.Log($"[OYM-OF] clip {clipName}: {clip.length:F2}s{(loop ? " loop" : "")}");
            }
            foreach (var need in Clips.Keys) if (!result.ContainsKey(need)) throw new Exception("missing clip " + need);
            return result;
        }

        /// Zero-g: the base layer is only the hover pose (Idle with the hover baked in, no walking); the UpperBody layer
        /// (torso, head, arms) keeps the RG-77 shouldered (AimIdle: level barrel, support hand on FrontGripSocket).
        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Aim", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Dash", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            var hover = sm.AddState("Hover", new Vector3(300, 0, 0)); hover.motion = clips["AimIdle"];   // full-body aim stance (hover "7" legs baked): same body frame as the UpperBody layer -> barrel stays on the aim
            sm.defaultState = hover;
            ac.AddLayer("UpperBody");
            var layers = ac.layers;
            layers[1].avatarMask = OYMFirstPerson.UpperBodyMask(Dir + "/OrbitFrame_UpperBody.mask");
            layers[1].defaultWeight = 1f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;
            var us = ac.layers[1].stateMachine;
            var aim = us.AddState("AimIdle", new Vector3(300, 120, 0)); aim.motion = clips["AimIdle"];
            us.defaultState = aim;
            EditorUtility.SetDirty(ac); AssetDatabase.SaveAssets();
            return ac;
        }

        // ------------------------------------------------------------------------------------------------ prefab + FX
        static OYM.RailgunFX BuildRailgun(GameObject root, Transform[] all, Animator anim, Vector3 fwd)
        {
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n) ?? throw new Exception(n + " missing");
            var muzzle = Find("MuzzleSocket"); var rail = Find("RailStartSocket");
            Vector3 dir = (muzzle.position - rail.position).normalized;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            var fade = OYMKestrel.Grad(new[] { (0f, Color.white), (0.3f, new Color(0.7f, 0.85f, 1f)), (1f, new Color(0.3f, 0.5f, 1f)) },
                                       new[] { (0f, 1f), (0.35f, 0.8f), (1f, 0f) });
            var arcMat = OYMKestrel.FxMat("FX_RailArc", "fx_glow", RailBlue, 12f);
            var flashMat = OYMKestrel.FxMat("FX_RailFlash", "fx_flare", new Color(0.75f, 0.88f, 1f), 40f);
            var coreMat = OYMKestrel.FxMat("FX_RailCore", "fx_glow", Color.white, 70f);
            var sparkMat = OYMKestrel.FxMat("FX_RailSpark", "fx_glow", new Color(0.7f, 0.85f, 1f), 16f);
            var beamMat = OYMKestrel.FxMat("FX_RailBeam", "fx_beam", new Color(0.7f, 0.86f, 1f), 18f);

            var mid = new GameObject("RailGap");
            mid.transform.SetParent(rail, false);
            mid.transform.position = (rail.position + muzzle.position) * 0.5f; mid.transform.rotation = rot;
            var arcs = OYMKestrel.Ps("ChargeArcs", mid.transform, rot, arcMat, false, false, new Vector2(0.04f, 0.09f), Vector2.zero,
                                     new Vector2(0.03f, 0.07f), fade, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.4f)));
            var ash = arcs.shape; ash.enabled = true; ash.shapeType = ParticleSystemShapeType.Box; ash.scale = new Vector3(0.012f, 0.012f, 0.01f);
            var flash = OYMKestrel.Ps("MuzzleFlash", muzzle, rot, flashMat, false, false, new Vector2(0.11f, 0.13f), Vector2.zero,
                                      new Vector2(1.1f, 1.4f), fade, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1.1f), new Keyframe(1, 1.2f)));
            var core = OYMKestrel.Ps("MuzzleCore", muzzle, rot, coreMat, false, false, new Vector2(0.07f, 0.08f), Vector2.zero,
                                     new Vector2(0.35f, 0.45f), fade, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.3f)));
            var sparks = OYMKestrel.Ps("MuzzleSparks", muzzle, rot, sparkMat, true, true, new Vector2(0.08f, 0.22f), new Vector2(14f, 34f),
                                       new Vector2(0.01f, 0.025f), fade, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.3f)));
            var ssh = sparks.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Cone; ssh.angle = 24f; ssh.radius = 0.03f;

            var beamGo = new GameObject("RailBeam");
            beamGo.transform.SetParent(root.transform, false);
            var lr = beamGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.sharedMaterial = beamMat; lr.widthMultiplier = 0.07f;
            lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
            lr.colorGradient = OYMKestrel.Grad(new[] { (0f, Color.white), (1f, new Color(0.5f, 0.7f, 1f)) }, new[] { (0f, 1f), (1f, 0.25f) });
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.enabled = false;

            var ml = OYMKestrel.FxLight("MuzzleLight", muzzle, new Color(0.75f, 0.88f, 1f), 9f, 0.1f);
            var rl = OYMKestrel.FxLight("RailLight", mid.transform, RailBlue, 2.5f, 0.05f);

            var fx = root.AddComponent<OYM.RailgunFX>();
            fx.muzzle = muzzle; fx.railStart = rail; fx.chargeArcs = arcs; fx.muzzleFlash = flash; fx.muzzleCore = core; fx.sparks = sparks;
            fx.beam = lr; fx.muzzleLight = ml; fx.railLight = rl;
            fx.recoilBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            return fx;
        }

        static void BuildPrefab(Avatar avatar, AnimatorController ctrl)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("OrbitFrame");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Model"; inst.transform.SetParent(root.transform, false);
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            Vector3 right = (anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            if (Vector3.Dot(fwd, Vector3.forward) < 0f) { inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); fwd = -fwd; }
            Debug.Log("[OYM-OF] model forward " + fwd);

            var all = inst.GetComponentsInChildren<Transform>(true);
            var gun = all.FirstOrDefault(t => t.name == "OrbitFrame_Railgun");
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (gun == null || hand == null || !gun.IsChildOf(hand)) throw new Exception("railgun is not under the RightHand bone");
            Debug.Log($"[OYM-OF] railgun hierarchy: {hand.name} -> {gun.parent.name} -> {gun.name} (mesh pivot {gun.localPosition.magnitude * 100f:F1} cm off the grip socket)");
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.localBounds = new Bounds(smr.localBounds.center, smr.localBounds.size * 1.6f);

            // amber eye light, spills onto the mask and the helmet's inner rim
            var eyeSock = all.FirstOrDefault(t => t.name == "EyeGlowSocket") ?? throw new Exception("EyeGlowSocket missing");
            var eye = OYMKestrel.FxLight("EyeGlowLight", eyeSock, Amber, 0.6f, 0.012f);
            eye.intensity = 50f; eye.enabled = true;

            OYMKestrel.SetupLods(inst, gun.GetComponent<Renderer>());
            var ctl = root.AddComponent<OYM.OrbitFrameController>();
            ctl.animator = anim;
            ctl.jetpack = OYMKestrel.BuildJetpack(root, all, fwd, OrbitJet, (-fwd * 0.15f - Vector3.up * 0.99f).normalized);
            ctl.railgun = BuildRailgun(root, all, anim, fwd);

            // first-person viewmodel: arms-only copy + the RG-77 at full (4K) texture resolution with its own charge / shot FX
            var fpMat = OYMFirstPerson.FullResWeaponMat(gun.GetComponent<Renderer>().sharedMaterial, TexDir, Dir + "/M_OrbitFrame_Railgun_FP.mat");
            var spec = new OYMFirstPerson.ViewmodelSpec
            {
                modelPath = ModelPath, weaponName = "OrbitFrame_Railgun", avatar = avatar, controller = ctrl, weaponMat = fpMat,
                armsMeshPath = Dir + "/OrbitFrame_FPArms.asset", leftOnWeapon = true,
                axisFrom = "RailStartSocket", axisTip = "MuzzleSocket", upRef = "RailStartSocket",
                gripPos = new Vector3(0.20f, -0.29f, 0.27f), aimDir = new Vector3(-0.035f, 0.03f, 1f), upDir = new Vector3(0.1f, 1f, 0f),
                bodyOffset = new Vector3(0f, -0.03f, -0.04f),
            };
            ctl.fpRig = OYMFirstPerson.BuildViewmodel(root, spec, out var fpAll, out var fpAnim);
            ctl.fpRailgun = BuildRailgun(ctl.fpRig.gameObject, fpAll, fpAnim, fwd);
            ctl.fpRailgun.recoilBone = null;                              // the viewmodel kicks through FirstPersonRig.Kick

            var b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) if (r is SkinnedMeshRenderer || r is MeshRenderer) b.Encapsulate(r.bounds);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                Debug.Log($"[OYM-OF] renderer {r.name}: [{string.Join(", ", r.sharedMaterials.Select(m => m ? m.name : "null"))}]");
            Debug.Log($"[OYM-OF] prefab height {b.max.y - b.min.y:F2} m, gun under {gun.parent.name}");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------------------------------------ test scene + capture
        [MenuItem("OYM/Characters/Orbit Frame: Build Test Scene")]
        public static void BuildTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gv = new GameObject("Global Volume (dark test)");
            var vol = gv.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = OYMKestrel.TestVolume(VolumePath);
            OYMKestrel.DirLight("Key (planetshine)", 1500f, new Color(0.80f, 0.86f, 1.0f), new Vector3(35, 150, 0));
            OYMKestrel.DirLight("Rim (star)", 4000f, new Color(0.65f, 0.85f, 1.0f), new Vector3(20, -20, 0), false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Deck"; floor.transform.localScale = new Vector3(6, 1, 6);
            floor.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(OYMKestrel.Dir + "/M_Kestrel_TestDeck.mat");
            var of = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            of.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 80000f; cam.fieldOfView = 40f;
            camGo.AddComponent<HDAdditionalCameraData>().antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0.6f, 1.75f, -3.4f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1.3f, 0) - camGo.transform.position);
            var orbit = camGo.AddComponent<OYM.OrbitFollowCamera>(); orbit.target = of.transform; orbit.pivotOffset = new Vector3(0.35f, 1.6f, 0f);
            of.GetComponent<OYM.OrbitFrameController>().cameraTransform = camGo.transform;
            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log("[OYM-OF] test scene saved " + TestScene);
        }

        [MenuItem("OYM/Characters/Orbit Frame: Capture")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(TestScene);
            var root = UnityEngine.Object.FindFirstObjectByType<OYM.OrbitFrameController>().gameObject;
            var anim = root.GetComponentInChildren<Animator>();
            var jet = root.GetComponent<OYM.JetpackFX>(); var rail = root.GetComponent<OYM.RailgunFX>();
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimDir })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                .Where(c => !c.name.StartsWith("__preview__")).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            var cam = Camera.main;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/orbitframe"));
            Directory.CreateDirectory(outDir);
            int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            // fx: "" none, "jet:<t>" booster t s after ignition, "rail:<t>" railgun (t<0 charge fraction, t>=0 s after the shot)
            var shots = new (string name, string clip, float t, Vector3 eye, Vector3 look, float fov, string fx)[]
            {
                ("01_idle_front", "Idle", 1.0f, new Vector3(1.3f, 1.4f, 3.4f), new Vector3(0, 1.0f, 0), 38f, ""),
                ("02_head_eyes", "AimIdle", 0.3f, new Vector3(0.45f, 1.78f, 1.0f), new Vector3(0, 1.68f, 0), 30f, ""),
                ("03_aim", "AimIdle", 0.3f, new Vector3(2.2f, 1.6f, 2.0f), new Vector3(0, 1.3f, 0.3f), 40f, ""),
                ("04_rail_charge", "AimIdle", 0.3f, new Vector3(2.0f, 1.7f, 1.2f), new Vector3(0, 1.5f, 0.6f), 38f, "rail:-0.8"),
                ("05_rail_fire", "AimIdle", 0.3f, new Vector3(2.6f, 1.8f, -0.6f), new Vector3(0, 1.5f, 2.0f), 46f, "rail:0.02"),
                ("06_dash_ignite", "DashLoop", 0.10f, new Vector3(-2.8f, 1.7f, -2.6f), new Vector3(0, 1.2f, 0), 42f, "jet:0.035"),
                ("07_dash_thrust", "DashLoop", 0.30f, new Vector3(-3.0f, 1.4f, -1.8f), new Vector3(0, 1.1f, 0), 40f, "jet:0.45"),
                ("08_dash_stop_hold", "DashStop", 0.42f, new Vector3(2.9f, 1.4f, 2.1f), new Vector3(0, 1.0f, 0), 40f, ""),
                ("09_back", "Idle", 1.0f, new Vector3(-1.2f, 1.6f, -3.2f), new Vector3(0, 1.1f, 0), 38f, ""),
            };
            var graph = PlayableGraph.Create("orbitframe-capture");
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            bool warm = false;
            foreach (var s in shots)
            {
                if (!clips.TryGetValue(s.clip, out var clip)) { Debug.LogWarning("[OYM-OF] no clip " + s.clip); continue; }
                var cp = AnimationClipPlayable.Create(graph, clip); cp.SetApplyFootIK(true);
                output.SetSourcePlayable(cp); cp.SetTime(s.t); cp.SetTime(s.t); graph.Evaluate(0f);
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) p.Clear(true);
                foreach (var l in root.GetComponentsInChildren<Light>()) if (l.name != "EyeGlowLight") l.enabled = false;
                var lr = root.GetComponentInChildren<LineRenderer>(); if (lr) lr.enabled = false;
                if (s.fx.StartsWith("jet:") && jet) jet.PreviewAt(float.Parse(s.fx.Substring(4), System.Globalization.CultureInfo.InvariantCulture), 9f);
                if (s.fx.StartsWith("rail:") && rail) rail.PreviewAt(float.Parse(s.fx.Substring(5), System.Globalization.CultureInfo.InvariantCulture), 9f);
                cam.fieldOfView = s.fov;
                cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(s.look - s.eye, Vector3.up));
                for (int i = 0; i < (warm ? 30 : 90); i++) { graph.Evaluate(0f); cam.Render(); }
                warm = true;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, s.name + ".png"), tex.EncodeToPNG());
                Debug.Log("[OYM-OF] captured " + s.name);
                cp.Destroy();
            }
            graph.Destroy();
            cam.targetTexture = null; rt.Release();
            Debug.Log("[OYM-OF] capture -> " + outDir);
        }
    }
}
