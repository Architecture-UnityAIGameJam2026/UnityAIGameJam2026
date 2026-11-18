// OnYourMark - KESTREL (sword class) playable character: Meshy-rigged body + transplanted hood/mask head + black
// steel sword in the right hand + glowing eyes.
//
//   1) python -I pipeline/kestrel/prep_unity.py   (Kestrel.fbx, retimed clip FBXs, baked textures -> Assets/OYM/Characters/Kestrel)
//   2) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMKestrel.BuildAndCapture -quit
//
// Builds Characters/Kestrel/Kestrel.prefab (Humanoid, AC_Kestrel animator, sword child of RightHand, cyan eye light on
// the Head bone), a dark test scene Scenes/KestrelTest.unity (WASD / Shift run / Space dash / LMB slash / Q stance),
// and Screenshots/kestrel/*.png.
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
    public static class OYMKestrel
    {
        public const string Dir = OYMSetup.Root + "/Characters/Kestrel";
        public const string ModelPath = Dir + "/Kestrel.fbx";
        public const string AnimDir = Dir + "/Animations";
        public const string TexDir = Dir + "/Textures";
        public const string PrefabPath = Dir + "/Kestrel.prefab";
        public const string ControllerPath = Dir + "/AC_Kestrel.controller";
        public const string VolumePath = Dir + "/Kestrel_TestVolume.asset";
        public const string TestScene = OYMSetup.Root + "/Scenes/KestrelTest.unity";
        public const string FxDir = OYMSetup.Root + "/FX";            // shared FX sprites + materials

        // Meshy biped rig: Hips -> Spine02 -> Spine01 -> Spine -> neck -> Head (no finger bones)
        static readonly (string human, string bone)[] HumanMap =
        {
            ("Hips", "Hips"), ("Spine", "Spine02"), ("Chest", "Spine01"), ("UpperChest", "Spine"), ("Neck", "neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftArm"), ("LeftLowerArm", "LeftForeArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightArm"), ("RightLowerArm", "RightForeArm"), ("RightHand", "RightHand"),
            ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"), ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToeBase"),
            ("RightUpperLeg", "RightUpLeg"), ("RightLowerLeg", "RightLeg"), ("RightFoot", "RightFoot"), ("RightToes", "RightToeBase"),
        };

        // one clip per FBX (pipeline/kestrel/anims.py retimes the Meshy actions: slashes + dash stop end with a snap)
        static readonly Dictionary<string, bool> Clips = new Dictionary<string, bool>   // clip -> loop
        {
            { "Idle", true }, { "Hover", true }, { "CombatIdle", true }, { "Walk", true }, { "Run", true }, { "DashLoop", true },
            { "DashStop", false }, { "Slash_Left", false }, { "Slash_Charged", false }, { "Slash_Thrust", false },
        };

        // eye glow: screen-relative HDR (exposure weight 0) so the eyes read and bloom in dark and bright maps alike
        const float EyeGlow = 9f;
        static readonly Color EyeLightColor = new Color(0.35f, 0.88f, 1f);
        const float EyeLightLumen = 60f, EyeLightRange = 0.5f;

        [MenuItem("OYM/Characters/Kestrel: Build + Capture")]
        public static void BuildAndCapture()
        {
            try
            {
                Build();
                BuildTestScene();
                Capture();
                Debug.Log("[OYM-K] BuildAndCapture finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-K] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("OYM/Characters/Kestrel: Build Prefab")]
        public static void Build()
        {
            var mats = Materials();
            var avatar = ImportModel(mats);
            var clips = ImportAnimations();
            var ev = OYMFirstPerson.ReadEvents(AnimDir + "/Kestrel_events.json");
            var ctrl = BuildController(clips, ev);
            BuildPrefab(avatar, ctrl, ev);
        }

        static readonly string[] Slashes = { "Slash_Left", "Slash_Charged", "Slash_Thrust" };
        static float Ev(Dictionary<string, Dictionary<string, float>> ev, string clip, string key, float fallback)
            => ev.TryGetValue(clip, out var e) && e.TryGetValue(key, out var v) ? v : fallback;

        // ------------------------------------------------------------------------------------------------ textures / materials
        static Texture2D Tex(string name, bool normal, bool srgb, int max)
        {
            string path = $"{TexDir}/{name}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new Exception("texture missing " + path + " (run pipeline/kestrel/prep_unity.py)");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != max)
            {
                ti.textureType = type; ti.sRGBTexture = srgb; ti.maxTextureSize = max;
                ti.anisoLevel = 8; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material LitMat(string name)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Lit");
            return m;
        }

        static Material PbrMat(string part, int max)
        {
            var m = LitMat("M_Kestrel_" + part);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", Tex($"Kestrel_{part}_albedo", false, true, max));
            m.SetTexture("_NormalMap", Tex($"Kestrel_{part}_normal", true, false, max));
            m.SetFloat("_NormalScale", 1f);
            m.SetTexture("_MaskMap", Tex($"Kestrel_{part}_mask", false, false, max));
            m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
            m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f);
            m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
            HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> Materials()
        {
            var d = new Dictionary<string, Material>
            {
                ["M_Kestrel_Body"] = PbrMat("Body", 2048),
                ["M_Kestrel_Head"] = PbrMat("Head", 2048),
                ["M_Kestrel_Sword"] = PbrMat("Sword", 2048),
            };
            // silver mask: smooth bright metal, no normal map (keeps the smoothed face clean), cyan eye-slit glow
            var mask = LitMat("M_Kestrel_Mask");
            mask.SetColor("_BaseColor", new Color(0.88f, 0.88f, 0.90f));
            mask.SetTexture("_BaseColorMap", null); mask.SetTexture("_NormalMap", null); mask.SetTexture("_MaskMap", null);
            mask.SetFloat("_Metallic", 0.92f);
            mask.SetFloat("_Smoothness", 0.52f);
            mask.SetTexture("_EmissiveColorMap", Tex("Kestrel_Mask_eyeglow", false, true, 2048));
            mask.SetFloat("_UseEmissiveIntensity", 0f);
            mask.SetColor("_EmissiveColorLDR", Color.white);
            mask.SetColor("_EmissiveColor", Color.white * EyeGlow);
            mask.SetFloat("_EmissiveExposureWeight", 0f);
            HDShaderUtils.ResetMaterialKeywords(mask);
            EditorUtility.SetDirty(mask);
            d["M_Kestrel_Mask"] = mask;
            AssetDatabase.SaveAssets();
            return d;
        }

        // ------------------------------------------------------------------------------------------------ model / avatar
        internal static HumanDescription HumanFor(GameObject model)
        {
            var bones = model.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(bones.Select(t => t.name));
            var human = new List<HumanBone>();
            foreach (var (h, b) in HumanMap)
            {
                if (!names.Contains(b)) throw new Exception($"bone '{b}' missing in {model.name}");
                human.Add(new HumanBone { humanName = h, boneName = b, limit = new HumanLimit { useDefaultValues = true } });
            }
            var skel = bones.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            return new HumanDescription
            {
                human = human.ToArray(), skeleton = skel,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
        }

        internal static void SetupHumanoid(string path, bool importAnim, Action<ModelImporter> extra)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new Exception("missing " + path + " (run pipeline/kestrel/prep_unity.py)");
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
            mi.importAnimation = importAnim;
            mi.animationType = ModelImporterAnimationType.Generic;   // first pass: read the plain hierarchy
            extra?.Invoke(mi);
            mi.SaveAndReimport();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.humanDescription = HumanFor(go);
            mi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (av == null || !av.isValid || !av.isHuman) throw new Exception("humanoid avatar failed for " + path);
        }


        /// Animation-only FBX: humanoid through the model's avatar (same skeleton, same A-pose reference). Building an avatar
        /// from the clip file itself would take its first frame as the reference pose and cancel the pose out.
        internal static void SetupClipAvatar(string path, Avatar modelAvatar)
        {
            // modelAvatar's asset path -> the model GameObject (rest pose) to borrow the reference skeleton from
            var rest = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(modelAvatar));
            var restBones = rest.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new Exception("missing " + path);
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
            mi.importAnimation = true; mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Generic;      // first pass: read the clip file's own hierarchy
            mi.SaveAndReimport();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var hd = HumanFor(go);
            int borrowed = 0;
            var sk = hd.skeleton;
            for (int i = 0; i < sk.Length; i++)
            {
                if (sk[i].name == go.name) continue;                    // the file's root keeps its own transform
                if (restBones.TryGetValue(sk[i].name, out var rb))
                {
                    sk[i].position = rb.localPosition; sk[i].rotation = rb.localRotation; sk[i].scale = rb.localScale; borrowed++;
                }
            }
            hd.skeleton = sk;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.humanDescription = hd;
            mi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (av == null || !av.isValid || !av.isHuman) throw new Exception("clip avatar failed for " + path);
            Debug.Log($"[OYM] clip avatar {Path.GetFileName(path)}: rest pose from the model for {borrowed}/{sk.Length} transforms");
        }

        static Avatar ImportModel(Dictionary<string, Material> mats)
        {
            SetupHumanoid(ModelPath, false, mi =>
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
            Debug.Log("[OYM-K] avatar OK: " + avatar.name);
            return avatar;
        }

        static Dictionary<string, AnimationClip> ImportAnimations()
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (var path in Directory.GetFiles(AnimDir, "*.fbx").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
            {
                string clipName = Path.GetFileNameWithoutExtension(path).Replace("Kestrel_", "");
                if (!Clips.TryGetValue(clipName, out bool loop)) { Debug.LogWarning("[OYM-K] unknown clip file " + path); continue; }
                SetupClipAvatar(path, AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First());
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                var takes = mi.defaultClipAnimations;
                if (takes.Length == 0) throw new Exception("no take in " + path);
                var c = takes.OrderByDescending(t => t.lastFrame - t.firstFrame).First();
                c.name = clipName; c.loopTime = loop; c.loopPose = false;
                // in place: root XZ / yaw are extracted as root motion and dropped (Animator.applyRootMotion = false);
                // vertical body motion stays in the pose
                c.lockRootRotation = false; c.keepOriginalOrientation = false;   // stance / swing yaw extracted + dropped: facing stays on the camera (mashing dash + slash used to turn the body)
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;      // hip offsets stay in the pose
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview__"));
                result[clipName] = clip;
                Debug.Log($"[OYM-K] clip {clipName}: {clip.length:F2}s{(loop ? " loop" : "")} (take '{c.takeName}')");
            }
            foreach (var need in Clips.Keys)
                if (!result.ContainsKey(need)) throw new Exception("missing clip " + need);
            return result;
        }

        // ------------------------------------------------------------------------------------------------ animator
        /// Zero-g: the base layer is only the hover pose (legs bent, no walking); the UpperBody layer (torso, head, arms)
        /// holds the combat stance and the slashes. The controller drives the UpperBody weight (0 = relaxed hover arms).
        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, Dictionary<string, Dictionary<string, float>> ev)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Combat", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("AttackType", AnimatorControllerParameterType.Int);
            ac.AddParameter("Dash", AnimatorControllerParameterType.Bool);
            ac.AddParameter(new AnimatorControllerParameter { name = "SlashSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var sm = ac.layers[0].stateMachine;
            var hover = sm.AddState("Hover", new Vector3(300, 0, 0)); hover.motion = clips["Hover"];
            sm.defaultState = hover;

            ac.AddLayer("UpperBody");
            var layers = ac.layers;
            layers[1].avatarMask = OYMFirstPerson.UpperBodyMask(Dir + "/Kestrel_UpperBody.mask");
            layers[1].defaultWeight = 0f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;
            var us = ac.layers[1].stateMachine;
            var combat = us.AddState("CombatIdle", new Vector3(300, 120, 0)); combat.motion = clips["CombatIdle"];
            us.defaultState = combat;
            for (int i = 0; i < Slashes.Length; i++)
            {
                var st = us.AddState(Slashes[i], new Vector3(600, 40 + 80 * i, 0));
                st.motion = clips[Slashes[i]]; st.tag = "Attack";
                st.speedParameterActive = true; st.speedParameter = "SlashSpeed";
                float len = clips[Slashes[i]].length, strike = Ev(ev, Slashes[i], "strike", len * 0.4f);
                var t = us.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.If, 0, "Attack"); t.AddCondition(AnimatorConditionMode.Equals, i, "AttackType");
                t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = false;
                t.offset = Mathf.Clamp01(Mathf.Max(0f, strike - OYM.KestrelController.DefaultWindup) / len);   // skip the slow lead-in
                var back = st.AddTransition(combat); back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;
                Debug.Log($"[OYM-K] {Slashes[i]}: starts at {t.offset * len:F2}s, cut {strike:F2}s, len {len:F2}s");
            }
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        // ------------------------------------------------------------------------------------------------ prefab
        static GameObject BuildPrefab(Avatar avatar, AnimatorController ctrl, Dictionary<string, Dictionary<string, float>> ev)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("Kestrel");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);

            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // face +Z (the controller and the capture rig assume it)
            Vector3 right = (anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            if (Vector3.Dot(fwd, Vector3.forward) < 0f) { inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); fwd = -fwd; }
            Debug.Log("[OYM-K] model forward " + fwd);

            var all = inst.GetComponentsInChildren<Transform>(true);
            var sword = all.FirstOrDefault(t => t.name == "Kestrel_Sword");
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (sword == null || hand == null || !sword.IsChildOf(hand)) throw new Exception("sword is not under the RightHand bone");
            Debug.Log($"[OYM-K] sword hierarchy: {hand.name} -> {sword.parent.name} -> {sword.name} (mesh pivot {sword.localPosition.magnitude * 100f:F1} cm off the grip socket)");
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = false;
                smr.localBounds = new Bounds(smr.localBounds.center, smr.localBounds.size * 1.6f);   // swings reach well outside the bind pose
            }

            // eye glow light: tiny cyan point light just in front of the eye slits -> spills onto the mask / hood lining
            var socket = all.FirstOrDefault(t => t.name == "EyeGlowSocket");
            if (socket == null) throw new Exception("EyeGlowSocket missing");
            var lgo = new GameObject("EyeGlowLight");
            lgo.transform.SetParent(socket, false);
            var hdl = lgo.AddHDLight(LightType.Point);
            var l = lgo.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = EyeLightLumen; l.range = EyeLightRange;
            l.color = EyeLightColor; l.useColorTemperature = false; l.shadows = LightShadows.None;
            hdl.affectsVolumetric = false;
            var so = new SerializedObject(hdl);
            var rad = so.FindProperty("m_ShapeRadius");
            if (rad != null) { rad.floatValue = 0.012f; so.ApplyModifiedPropertiesWithoutUndo(); }
            var efd = so.FindProperty("m_FadeDistance");
            if (efd != null) { efd.floatValue = 25f; so.ApplyModifiedPropertiesWithoutUndo(); }    // eye light: close range only

            SetupLods(inst, sword.GetComponent<Renderer>());
            var ctl = root.AddComponent<OYM.KestrelController>();
            ctl.animator = anim;
            ctl.jetpack = BuildJetpack(root, all, fwd);
            ctl.slashStrike = Slashes.Select((c, i) => Ev(ev, c, "strike", ctl.slashStrike[i])).ToArray();
            ctl.slashImpact = Slashes.Select((c, i) => Ev(ev, c, "impact", ctl.slashImpact[i])).ToArray();
            ctl.slashHoldEnd = Slashes.Select((c, i) => Ev(ev, c, "hold_end", ctl.slashHoldEnd[i])).ToArray();
            ctl.slashLength = Slashes.Select(c => ctrl.animationClips.First(x => x.name == c).length).ToArray();

            // slash arc on the sword (third person) + the first-person viewmodel with its own arc
            OYMFirstPerson.EnsureTrailSprite(FxDir);
            var trailMat = FxMat("FX_BladeTrail", "fx_trail", new Color(0.62f, 0.9f, 1f), 7f);
            OYM.BladeTrail Trail(Transform[] ts, Transform swordT)
            {
                var bt = swordT.gameObject.AddComponent<OYM.BladeTrail>();
                bt.tipSocket = ts.First(t => t.name == "BladeTipSocket"); bt.baseSocket = ts.First(t => t.name == "BladeBaseSocket");
                bt.material = trailMat; return bt;
            }
            ctl.trail = Trail(all, sword);
            var spec = new OYMFirstPerson.ViewmodelSpec
            {
                modelPath = ModelPath, weaponName = "Kestrel_Sword", avatar = avatar, controller = ctrl,
                weaponMat = null,                                           // the sword's source textures are 2K: the 3P material is already full res
                armsMeshPath = Dir + "/Kestrel_FPArms.asset", leftOnWeapon = false,
                axisFrom = null, axisTip = "BladeTipSocket", upRef = "RightHand",
                gripPos = new Vector3(0.22f, -0.27f, 0.40f), aimDir = new Vector3(-0.45f, 0.45f, 0.77f), upDir = new Vector3(0.88f, 0.22f, 0.38f),
                bodyOffset = new Vector3(0f, -0.04f, -0.02f),
            };
            ctl.fpRig = OYMFirstPerson.BuildViewmodel(root, spec, out var fpAll, out _);
            ctl.fpTrail = Trail(fpAll, fpAll.First(t => t.name == "Kestrel_Sword"));
            ctl.fpTrail.life = 0.2f;                                        // first person: the arc lingers a little longer
            ctl.fpArc = ctl.fpRig.gameObject.AddComponent<OYM.SlashArc>();
            ctl.fpArc.material = trailMat;

            // report: height and materials
            var b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                Debug.Log($"[OYM-K] renderer {r.name}: [{string.Join(", ", r.sharedMaterials.Select(m => m ? m.name : "null"))}]");
            Debug.Log($"[OYM-K] prefab bounds {b.size} (height {b.max.y - b.min.y:F2} m), sword under {sword.parent.name}");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }


        /// LOD0/1/2 skinned meshes (named *_LOD0..2 by make_lods.py) + the weapon in every level; the importer's own
        /// LODGroup (if any) is replaced so the transitions are ours.
        internal static void SetupLods(GameObject model, Renderer weapon)
        {
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.name.Contains("_LOD")).OrderBy(r => r.name).ToArray();
            if (smrs.Length < 2) { Debug.LogWarning("[OYM] no LOD meshes found under " + model.name); return; }
            foreach (var old in model.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(old);
            var lg = model.AddComponent<LODGroup>();
            float[] h = { 0.30f, 0.10f, 0.008f };
            var lods = new LOD[smrs.Length];
            for (int i = 0; i < smrs.Length; i++)
            {
                var rs = weapon ? new Renderer[] { smrs[i], weapon } : new Renderer[] { smrs[i] };
                lods[i] = new LOD(h[Mathf.Min(i, h.Length - 1)], rs);
                if (i == smrs.Length - 1) lods[i].screenRelativeTransitionHeight = h[h.Length - 1];
            }
            lg.SetLODs(lods);
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
            Debug.Log($"[OYM] LODGroup: {string.Join(" / ", smrs.Select(r => r.name + " " + r.sharedMesh.triangles.Length / 3 + " tris"))}");
        }

        // ------------------------------------------------------------------------------------------------ jetpack FX
        // AC6-style boost ignition: blinding blue-white flash that floods the body (HDR sprite + big light) and radial
        // sparks, then twin cyan jet plumes from the nozzles under the compact pack.
        static readonly Color JetCyan = new Color(0.45f, 0.85f, 1f);

        /// Colours + material names of one booster look (KESTREL cyan, ORBIT FRAME amber).
        internal class JetPalette
        {
            public string prefix = "FX_";
            public Color plume = new Color(0.55f, 0.88f, 1f), core = new Color(0.85f, 0.97f, 1f), flash = new Color(0.7f, 0.9f, 1f), spark = new Color(0.7f, 0.92f, 1f);
            public Color light = new Color(0.45f, 0.85f, 1f), flashLight = new Color(0.75f, 0.9f, 1f);
            public Color mid = new Color(0.6f, 0.9f, 1f), tail = new Color(0.15f, 0.35f, 1f), fadeMid = new Color(0.65f, 0.9f, 1f), fadeTail = new Color(0.25f, 0.5f, 1f);
            public float flashSize = 1.7f, plumeSpeed = 11f, plumeLen = 1f;
        }
        internal static readonly JetPalette KestrelJet = new JetPalette();

        internal static Material FxMat(string name, string sprite, Color tint, float intensity)
        {
            Directory.CreateDirectory(FxDir);
            var sh = Shader.Find("OYM/FX/AdditiveHDR");
            if (sh == null) throw new Exception("shader OYM/FX/AdditiveHDR missing (Assets/OYM/Shaders/AdditiveHDR.shader)");
            string tp = $"{FxDir}/{sprite}.png";
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti == null) throw new Exception("FX sprite missing " + tp);
            // uncompressed: the sprites carry their gradient in alpha only; BC3 alpha (8 levels per 4x4 block) x HDR intensity 30+ shows as rings in the big flash halo
            if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
            string path = $"{FxDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            m.SetColor("_Tint", tint); m.SetFloat("_Intensity", intensity);
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Gradient Grad((float t, Color c)[] cols, (float t, float a)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(cols.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), alphas.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        internal static ParticleSystem Ps(string name, Transform parent, Quaternion worldRot, Material mat, bool stretched, bool world,
                                 Vector2 life, Vector2 speed, Vector2 size, Gradient col, AnimationCurve sizeCurve)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = worldRot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true; main.loop = true; main.duration = 1f; main.maxParticles = 600;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = Color.white;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(col);
            var so = ps.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretched) { r.velocityScale = 0.035f; r.lengthScale = 1.6f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = 3f;
            return ps;
        }

        internal static Light FxLight(string name, Transform parent, Color c, float range, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = range; l.color = c; l.useColorTemperature = false;
            l.shadows = LightShadows.None; l.enabled = false;
            hd.affectsVolumetric = false;
            var fso = new SerializedObject(hd);
            var fd = fso.FindProperty("m_FadeDistance");
            if (fd != null) { fd.floatValue = 60f; fso.ApplyModifiedPropertiesWithoutUndo(); }   // no cost far away
            var so = new SerializedObject(hd);
            var rad = so.FindProperty("m_ShapeRadius");
            if (rad != null) { rad.floatValue = radius; so.ApplyModifiedPropertiesWithoutUndo(); }
            return l;
        }

        static OYM.JetpackFX BuildJetpack(GameObject root, Transform[] all, Vector3 fwd)
            => BuildJetpack(root, all, fwd, KestrelJet, (-fwd * 0.5f - Vector3.up * 0.87f).normalized);

        internal static OYM.JetpackFX BuildJetpack(GameObject root, Transform[] all, Vector3 fwd, JetPalette pal, Vector3 nozzleDir)
        {
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n) ?? throw new Exception(n + " missing");
            var rot = Quaternion.LookRotation(nozzleDir, fwd);
            var plumeMat = FxMat(pal.prefix + "JetPlume", "fx_glow", pal.plume, 7f);
            var coreMat = FxMat(pal.prefix + "JetCore", "fx_glow", pal.core, 16f);
            var flashMat = FxMat(pal.prefix + "IgniteFlash", "fx_flare", pal.flash, 34f);
            var flashCoreMat = FxMat(pal.prefix + "IgniteCore", "fx_glow", Color.white, 60f);
            var sparkMat = FxMat(pal.prefix + "IgniteSpark", "fx_glow", pal.spark, 14f);

            var plumeCol = Grad(new[] { (0f, Color.white), (0.25f, pal.mid), (1f, pal.tail) },
                                new[] { (0f, 1f), (0.6f, 0.65f), (1f, 0f) });
            var fade = Grad(new[] { (0f, Color.white), (0.35f, pal.fadeMid), (1f, pal.fadeTail) },
                            new[] { (0f, 1f), (0.3f, 0.8f), (1f, 0f) });

            var fx = root.AddComponent<OYM.JetpackFX>();
            var plumes = new List<ParticleSystem>(); var cores = new List<ParticleSystem>(); var lights = new List<Light>();
            foreach (var sock in new[] { Find("ThrusterSocket_L"), Find("ThrusterSocket_R") })
            {
                var plume = Ps("JetPlume", sock, rot, plumeMat, true, true, new Vector2(0.07f, 0.12f) * pal.plumeLen, new Vector2(pal.plumeSpeed - 2f, pal.plumeSpeed + 2f),
                               new Vector2(0.06f, 0.11f), plumeCol, new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(0.35f, 1.6f), new Keyframe(1, 0.5f)));
                var sh = plume.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 0.012f;
                plumes.Add(plume);
                var core = Ps("JetCore", sock, rot, coreMat, false, false, new Vector2(0.05f, 0.06f), new Vector2(0f, 0f),
                              new Vector2(0.10f, 0.14f), fade, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.7f)));
                cores.Add(core);
                var l = FxLight("NozzleLight", sock, pal.light, 2.5f, 0.03f);
                l.transform.position = sock.position + nozzleDir * 0.08f;
                lights.Add(l);
            }
            var pack = Find("JetpackSocket");
            var flash = Ps("IgniteFlash", pack, rot, flashMat, false, false, new Vector2(0.2f, 0.24f), Vector2.zero, new Vector2(pal.flashSize - 0.2f, pal.flashSize + 0.2f), fade,
                           new AnimationCurve(new Keyframe(0, 0.55f), new Keyframe(0.25f, 1.1f), new Keyframe(1, 1.25f)));
            var flashCore = Ps("IgniteCore", pack, rot, flashCoreMat, false, false, new Vector2(0.11f, 0.13f), Vector2.zero, new Vector2(0.45f, 0.6f), fade,
                               new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.4f)));
            var sparks = Ps("IgniteSparks", pack, rot, sparkMat, true, true, new Vector2(0.12f, 0.35f), new Vector2(10f, 24f), new Vector2(0.012f, 0.03f), fade,
                            new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.3f)));
            var ssh = sparks.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Cone; ssh.angle = 55f; ssh.radius = 0.06f;
            sparks.GetComponent<ParticleSystemRenderer>().lengthScale = 2.2f;
            var flashLight = FxLight("IgniteLight", pack, pal.flashLight, 6f, 0.15f);
            flashLight.transform.position = pack.position + nozzleDir * 0.25f;

            fx.plumes = plumes.ToArray(); fx.cores = cores.ToArray(); fx.nozzleLights = lights.ToArray();
            fx.flash = flash; fx.flashCore = flashCore; fx.sparks = sparks; fx.flashLight = flashLight;
            return fx;
        }

        // ------------------------------------------------------------------------------------------------ test scene
        static VolumeProfile TestVolume() => TestVolume(VolumePath);

        internal static VolumeProfile TestVolume(string volumePath)
        {
            AssetDatabase.DeleteAsset(volumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, volumePath);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, profile); return c;
            }
            const float ev = 9f;                        // a dark map: ~16x less light than the sunlit debris field
            var ve = Add<VisualEnvironment>();
            ve.skyType.Override((int)SkyType.HDRI); ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var hs = Add<HDRISky>();
            hs.hdriSky.Override(AssetDatabase.LoadAssetAtPath<Cubemap>(OYMSetup.SkyPath));
            hs.skyIntensityMode.Override(SkyIntensityMode.Exposure); hs.exposure.Override(ev - 0.6f);
            var ex = Add<Exposure>(); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(ev);
            var bloom = Add<Bloom>(); bloom.intensity.Override(0.3f); bloom.scatter.Override(0.72f); bloom.threshold.Override(0f); bloom.quality.Override(2);
            var tm = Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var vig = Add<Vignette>(); vig.intensity.Override(0.22f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        internal static void DirLight(string name, float lux, Color c, Vector3 euler, bool shadows = true)
        {
            var go = new GameObject(name);
            go.AddHDLight(LightType.Directional);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lux; l.intensity = lux; l.color = c; l.useColorTemperature = false; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            go.transform.rotation = Quaternion.Euler(euler);
        }

        [MenuItem("OYM/Characters/Kestrel: Build Test Scene")]
        public static void BuildTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gv = new GameObject("Global Volume (dark test)");
            var vol = gv.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = TestVolume();

            DirLight("Key (planetshine)", 1500f, new Color(0.80f, 0.86f, 1.0f), new Vector3(35, 150, 0));
            DirLight("Rim (star)", 4000f, new Color(0.65f, 0.85f, 1.0f), new Vector3(20, -20, 0), false);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Deck"; floor.transform.localScale = new Vector3(6, 1, 6);
            var fm = LitMat("M_Kestrel_TestDeck");
            fm.SetColor("_BaseColor", new Color(0.05f, 0.052f, 0.058f)); fm.SetFloat("_Metallic", 0.15f); fm.SetFloat("_Smoothness", 0.28f);
            HDShaderUtils.ResetMaterialKeywords(fm);
            floor.GetComponent<MeshRenderer>().sharedMaterial = fm;

            var kestrel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            kestrel.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 80000f; cam.fieldOfView = 40f;
            var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.6f, -3.4f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1.2f, 0) - camGo.transform.position);
            var orbit = camGo.AddComponent<OYM.OrbitFollowCamera>(); orbit.target = kestrel.transform;
            kestrel.GetComponent<OYM.KestrelController>().cameraTransform = camGo.transform;

            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log("[OYM-K] test scene saved " + TestScene);
        }

        // ------------------------------------------------------------------------------------------------ capture
        [MenuItem("OYM/Characters/Kestrel: Capture")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(TestScene);
            var root = UnityEngine.Object.FindFirstObjectByType<OYM.KestrelController>().gameObject;
            var anim = root.GetComponentInChildren<Animator>();
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimDir })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                .Where(c => !c.name.StartsWith("__preview__")).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            var cam = Camera.main;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/kestrel"));
            Directory.CreateDirectory(outDir);
            int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var jet = root.GetComponent<OYM.JetpackFX>();
            var shots = new (string name, string clip, float t, Vector3 eye, Vector3 look, float fov)[]
            {
                ("10_dash_ignite", "DashLoop", 0.10f, new Vector3(-2.6f, 1.6f, -2.4f), new Vector3(0, 1.1f, 0), 42f),
                ("11_dash_thrust", "DashLoop", 0.30f, new Vector3(-2.8f, 1.3f, -1.6f), new Vector3(0, 1.0f, 0), 40f),
                ("01_idle_front", "Idle", 1.0f, new Vector3(1.2f, 1.3f, 3.2f), new Vector3(0, 0.95f, 0), 38f),
                ("02_head_glow", "Idle", 1.0f, new Vector3(0.35f, 1.62f, 0.85f), new Vector3(0, 1.57f, 0), 30f),
                ("03_combat_idle", "CombatIdle", 0.6f, new Vector3(-1.8f, 1.4f, 2.6f), new Vector3(0, 0.95f, 0), 40f),
                ("04_slash_left_hold", "Slash_Left", 1.55f, new Vector3(2.6f, 1.5f, 2.2f), new Vector3(0, 1.0f, 0), 42f),
                ("05_slash_charged_hold", "Slash_Charged", 1.35f, new Vector3(-2.4f, 1.3f, 2.4f), new Vector3(0, 1.0f, 0), 42f),
                ("06_run", "Run", 0.3f, new Vector3(3.2f, 1.2f, 1.2f), new Vector3(0, 0.95f, 0), 38f),
                ("08_dash_stop_hold", "DashStop", 0.42f, new Vector3(2.8f, 1.3f, 2.0f), new Vector3(0, 0.95f, 0), 40f),
                ("09_slash_thrust_hold", "Slash_Thrust", 1.70f, new Vector3(2.2f, 1.4f, 2.6f), new Vector3(0, 1.0f, 0), 42f),
                ("07_back", "Idle", 1.0f, new Vector3(-1.0f, 1.5f, -3.0f), new Vector3(0, 1.0f, 0), 38f),
            };
            var graph = PlayableGraph.Create("kestrel-capture");
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            bool warm = false;
            foreach (var s in shots)
            {
                if (!clips.TryGetValue(s.clip, out var clip)) { Debug.LogWarning("[OYM-K] no clip " + s.clip); continue; }
                var cp = AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(true);
                output.SetSourcePlayable(cp);
                cp.SetTime(s.t); cp.SetTime(s.t);
                graph.Evaluate(0f);
                float fxT = s.name.StartsWith("10_") ? 0.035f : s.name.StartsWith("11_") ? 0.45f : -1f;
                if (jet != null)
                {
                    if (fxT >= 0f) jet.PreviewAt(fxT, 9f);
                    else
                    {
                        foreach (var p in jet.GetComponentsInChildren<ParticleSystem>()) p.Clear(true);
                        foreach (var l in jet.GetComponentsInChildren<Light>()) l.enabled = false;
                    }
                }
                cam.fieldOfView = s.fov;
                cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(s.look - s.eye, Vector3.up));
                for (int i = 0; i < (warm ? 40 : 120); i++) { graph.Evaluate(0f); cam.Render(); }
                warm = true;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, s.name + ".png"), tex.EncodeToPNG());
                Debug.Log("[OYM-K] captured " + s.name);
                cp.Destroy();
            }
            graph.Destroy();
            cam.targetTexture = null; rt.Release();
            Debug.Log("[OYM-K] capture -> " + outDir);
        }
    }
}
