// OnYourMark - shared editor helpers for the zero-g play-test characters: first-person viewmodel rig (arms-only skinned
// mesh + full-res weapon + FirstPersonRig), the upper-body avatar mask, the sword-trail sprite and the slash timing file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM.EditorTools
{
    public static class OYMFirstPerson
    {
        public static readonly string[] ArmBones = { "RightArm", "RightForeArm", "RightHand", "LeftArm", "LeftForeArm", "LeftHand" };

        /// Humanoid mask for the attack / aim layer: torso, head, arms (legs, root and IK stay with the hover base layer).
        public static AvatarMask UpperBodyMask(string path)
        {
            AssetDatabase.DeleteAsset(path);
            var m = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                                      AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                m.SetHumanoidBodyPartActive(p, true);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// Copy of the skinned mesh keeping only triangles whose three vertices are weighted >= minWeight to the kept bones.
        public static Mesh ArmsMesh(SkinnedMeshRenderer smr, string[] keep, string path, float minWeight = 0.5f)
        {
            var src = smr.sharedMesh;
            var bones = smr.bones;
            var keepIdx = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++) if (bones[i] && keep.Contains(bones[i].name)) keepIdx.Add(i);
            var bw = src.boneWeights;
            var ws = new float[src.vertexCount];
            for (int v = 0; v < bw.Length; v++)
            {
                var b = bw[v]; float s = 0f;
                if (keepIdx.Contains(b.boneIndex0)) s += b.weight0;
                if (keepIdx.Contains(b.boneIndex1)) s += b.weight1;
                if (keepIdx.Contains(b.boneIndex2)) s += b.weight2;
                if (keepIdx.Contains(b.boneIndex3)) s += b.weight3;
                ws[v] = s;
            }
            var m = UnityEngine.Object.Instantiate(src);
            m.name = Path.GetFileNameWithoutExtension(path);
            int kept = 0, total = 0;
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                var tris = src.GetTriangles(sub);
                var outT = new List<int>(tris.Length / 4);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    total++;
                    if (ws[tris[t]] >= minWeight && ws[tris[t + 1]] >= minWeight && ws[tris[t + 2]] >= minWeight)
                    { outT.Add(tris[t]); outT.Add(tris[t + 1]); outT.Add(tris[t + 2]); kept++; }
                }
                m.SetTriangles(outT, sub, false);
            }
            m.RecalculateBounds();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            Debug.Log($"[OYM-FP] arms mesh {path}: {kept}/{total} triangles ({keepIdx.Count} bones kept)");
            return m;
        }

        /// The weapon material at full source resolution (albedo + normal copied to <TexDir>/FP/*_fp.png, imported at 4096).
        public static Material FullResWeaponMat(Material src, string texDir, string dstMatPath)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(dstMatPath);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, dstMatPath); }
            m.CopyPropertiesFromMaterial(src);
            Directory.CreateDirectory(texDir + "/FP");
            foreach (var prop in new[] { "_BaseColorMap", "_NormalMap" })
            {
                var t = src.GetTexture(prop) as Texture2D;
                if (!t) continue;
                string sp = AssetDatabase.GetAssetPath(t);
                string dp = $"{texDir}/FP/{Path.GetFileNameWithoutExtension(sp)}_fp.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(dp) == null) AssetDatabase.CopyAsset(sp, dp);
                var ti = (TextureImporter)AssetImporter.GetAtPath(dp);
                bool normal = prop == "_NormalMap";
                if (ti.maxTextureSize != 4096 || ti.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default))
                {
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = !normal; ti.maxTextureSize = 4096; ti.anisoLevel = 16;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
                var ft = AssetDatabase.LoadAssetAtPath<Texture2D>(dp);
                m.SetTexture(prop, ft);
                Debug.Log($"[OYM-FP] {Path.GetFileName(dp)}: {ft.width}x{ft.height}");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        public class ViewmodelSpec
        {
            public string modelPath, weaponName;
            public Avatar avatar;
            public RuntimeAnimatorController controller;
            public Material weaponMat;
            public string armsMeshPath;
            public bool leftOnWeapon;
            public string axisFrom, axisTip, upRef;           // socket names (upRef "RightHand" = the hand bone)
            public Vector3 gripPos, aimDir, upDir, bodyOffset;
        }

        /// FPView (child of the character root, inactive): a second copy of the model with the arms-only mesh and the
        /// weapon, driven by the same controller, plus FirstPersonRig. Returns the rig; `all` = transforms of the copy.
        public static OYM.FirstPersonRig BuildViewmodel(GameObject root, ViewmodelSpec s, out Transform[] all, out Animator anim)
        {
            var view = new GameObject("FPView");
            view.transform.SetParent(root.transform, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(s.modelPath));
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "FPModel";
            inst.transform.SetParent(view.transform, false);
            anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.avatar = s.avatar; anim.runtimeAnimatorController = s.controller;
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Vector3 right = (anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
            if (Vector3.Dot(Vector3.Cross(right, Vector3.up), Vector3.forward) < 0f) inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lg);
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var lod0 = smrs.FirstOrDefault(r => r.name.Contains("_LOD0")) ?? smrs.OrderByDescending(r => r.sharedMesh.vertexCount).First();
            foreach (var r in smrs) if (r != lod0) UnityEngine.Object.DestroyImmediate(r.gameObject);
            lod0.sharedMesh = ArmsMesh(lod0, ArmBones, s.armsMeshPath);
            lod0.shadowCastingMode = ShadowCastingMode.Off; lod0.updateWhenOffscreen = true;
            var ts = inst.GetComponentsInChildren<Transform>(true);
            all = ts;
            Transform Find(string n) => ts.FirstOrDefault(t => t.name == n) ?? throw new Exception($"{n} missing in the viewmodel copy");
            var weapon = Find(s.weaponName);
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (s.weaponMat) r.sharedMaterial = s.weaponMat;
            }

            var rig = view.AddComponent<OYM.FirstPersonRig>();
            rig.animator = anim;
            rig.rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); rig.rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); rig.rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            rig.lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); rig.lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); rig.lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            rig.grip = Find("GripSocket_R");
            rig.axisFrom = string.IsNullOrEmpty(s.axisFrom) ? null : Find(s.axisFrom);
            rig.axisTip = Find(s.axisTip);
            rig.upRef = s.upRef == "RightHand" ? rig.rHand : Find(s.upRef);
            rig.eyeLocal = view.transform.InverseTransformPoint(Find("EyeGlowSocket").position);
            rig.leftOnWeapon = s.leftOnWeapon;
            rig.gripPos = s.gripPos; rig.aimDir = s.aimDir; rig.upDir = s.upDir; rig.bodyOffset = s.bodyOffset;
            // the eye light / sockets of the copy are not needed in first person
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(l.gameObject);
            Debug.Log($"[OYM-FP] viewmodel for {root.name}: eye {rig.eyeLocal}, grip under {rig.grip.parent.name}");
            view.SetActive(false);
            return rig;
        }

        /// White sprite for the sword arc ribbon: u = age (fades), v = across the blade (base 0 -> tip 1, hot edge near the tip).
        public static void EnsureTrailSprite(string fxDir)
        {
            string p = fxDir + "/fx_trail.png";
            if (File.Exists(p)) return;
            Directory.CreateDirectory(fxDir);
            int W = 64, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f), v = y / (H - 1f);
                    float across = Mathf.Pow(Mathf.SmoothStep(0.05f, 0.95f, v), 1.7f) + 0.9f * Mathf.Exp(-Mathf.Pow((v - 0.92f) / 0.045f, 2f));
                    float along = Mathf.Pow(1f - u, 0.7f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(across * along)));
                }
            File.WriteAllBytes(p, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p);
        }

        /// Slash timing written by pipeline/kestrel/anims.py: {clip: {strike, impact, hold_end, length}} (seconds).
        public static Dictionary<string, Dictionary<string, float>> ReadEvents(string path)
        {
            var d = new Dictionary<string, Dictionary<string, float>>();
            if (!File.Exists(path)) { Debug.LogWarning("[OYM-FP] no events file " + path); return d; }
            string s = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(s, "\"(\\w+)\"\\s*:\\s*\\{([^}]*)\\}"))
            {
                var e = new Dictionary<string, float>();
                foreach (Match kv in Regex.Matches(m.Groups[2].Value, "\"(\\w+)\"\\s*:\\s*([-0-9.eE]+)"))
                    e[kv.Groups[1].Value] = float.Parse(kv.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                d[m.Groups[1].Value] = e;
            }
            return d;
        }
    }
}
