// OnYourMark - HUSK enemies (game-ready Meshy assets) in the debris field: HDRP materials with HDR emission,
// LOD groups, test scene and look-dev captures.
//
//   1) python Tools/prep_enemies.py      (copies meshy/enemies/game -> Assets/OYM/Enemies, converts textures)
//   2) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMEnemies.BuildAndCapture -quit
//
// Assets come in metres with the front on +Z, so no scale/orientation fix is applied (sizes are only logged).
// Builds Assets/OYM/Enemies/Prefabs/<model>.prefab, then a copy of the debris field with the enemies placed
// (Scenes/DebrisField_EnemyTest.unity; DebrisField.unity is untouched) and renders Screenshots/enemies/*.png.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    public static class OYMEnemies
    {
        public const string Dir = OYMSetup.Root + "/Enemies";
        public const string TexDir = Dir + "/Textures";
        public const string PrefabDir = Dir + "/Prefabs";
        public const string TestScene = OYMSetup.Root + "/Scenes/DebrisField_EnemyTest.unity";

        public static readonly string[] MODELS = { "wick_body", "wick_open", "prism_body", "prism_charge", "citadel_dart", "citadel_boss" };
        static readonly string[] BOSS_SETS = { "citadel_launcher_damaged", "citadel_launcher", "citadel_body", "citadel_claw", "citadel_engine", "citadel_head", "citadel_plate" };

        // HDR glow: emissive EV100 = camera EV100 + log2(strength) + bias. The meshy session authored the strength
        // (KHR_materials_emissive_strength: x4 for WICK, x8 for the rest = +2 / +3 stops over the camera exposure).
        // +2: at the authored +2/+3 stops the sensors read as red paint on the shadow side; +2 more lets them clear
        // the bloom and read as light (judged on the g_* shadow-side captures).
        public const float EmissiveEvBias = 2f;
        // user feedback 11-18 ("enemy eyes should glow more"): sensors / eyes get another +1.5 EV. wick_open keeps the base
        // level because its whole open interior is emissive and would turn into a magenta lamp.
        public const float EyeExtraEv = 1.5f;
        // LOD screen-relative heights (LOD0 -> LOD1, LOD1 -> LOD2, LOD2 -> culled)
        static readonly float[] LodHeights = { 0.25f, 0.08f, 0.002f };

        [MenuItem("OYM/Enemies/Build + Capture Enemy Test")]
        public static void BuildAndCapture()
        {
            try
            {
                var prefabs = BuildPrefabs();
                BuildTestScene(prefabs);
                Capture();
                Debug.Log("[OYM-EN] BuildAndCapture finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-EN] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------------------------------------
        class SetInfo { public bool emissive; public float strength; }

        static Dictionary<string, SetInfo> ReadPrep()
        {
            var d = new Dictionary<string, SetInfo>();
            string p = Dir + "/enemies_prep.json";
            if (!File.Exists(p)) return d;
            string txt = File.ReadAllText(p);
            foreach (Match m in Regex.Matches(txt, "\"(\\w+)\":\\s*\\{\\s*\"emissive\":\\s*(true|false),\\s*\"emissive_strength\":\\s*([0-9.eE+-]+)"))
                d[m.Groups[1].Value] = new SetInfo { emissive = m.Groups[2].Value == "true", strength = float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) };
            return d;
        }

        static string SetFor(string model, string rendererName)
        {
            if (model != "citadel_boss") return model;
            var n = Regex.Replace(rendererName, @"_LOD\d$", "");
            if (n.Contains("_damaged")) return "citadel_launcher_damaged";
            foreach (var s in BOSS_SETS) if (n.StartsWith(s, StringComparison.Ordinal)) return s;
            return "citadel_body";
        }

        [MenuItem("OYM/Enemies/1. Import + Prefabs")]
        public static Dictionary<string, GameObject> BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            var prep = ReadPrep();
            var mats = new Dictionary<string, Material>();
            Material MatFor(string set)
            {
                if (!mats.TryGetValue(set, out var m))
                {
                    prep.TryGetValue(set, out var info);
                    mats[set] = m = Mat(set, info);
                }
                return m;
            }

            var result = new Dictionary<string, GameObject>();
            foreach (var model in MODELS)
            {
                string fbx = $"{Dir}/{model}/{model}.fbx";
                var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
                if (mi == null) { Debug.LogWarning("[OYM-EN] missing " + fbx + " (run Tools/prep_enemies.py)"); continue; }
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importTangents != ModelImporterTangents.CalculateMikk)
                {
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;   // HDRP materials are built below
                    mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                    mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
                    mi.importNormals = ModelImporterNormals.Import;
                    mi.importTangents = ModelImporterTangents.CalculateMikk;
                    mi.meshCompression = ModelImporterMeshCompression.Off;
                    mi.indexFormat = ModelImporterIndexFormat.Auto;
                    mi.isReadable = false;
                    mi.SaveAndReimport();
                }

                var root = new GameObject(model);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);

                var usedSets = new HashSet<string>();
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var set = SetFor(model, r.name);
                    usedSets.Add(set);
                    var mm = r.sharedMaterials;
                    for (int k = 0; k < mm.Length; k++) mm[k] = MatFor(set);
                    r.sharedMaterials = mm;
                }
                int groups = SetupLods(inst);
                // destructible boss: damaged launchers sit on the intact ones, swapped in on destruction
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (Regex.IsMatch(t.name, @"^citadel_launcher_[LR]_damaged$")) t.gameObject.SetActive(false);

                var b = Bounds(inst, true);
                var p = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{model}.prefab");
                UnityEngine.Object.DestroyImmediate(root);
                result[model] = p;
                Debug.Log($"[OYM-EN] {model}: size {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} m, centre {b.center}, LOD groups {groups}, " +
                          $"LOD0 tris {Tris(p, 0)}, sets [{string.Join(", ", usedSets)}]");
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static int SetupLods(GameObject inst)
        {
            var groups = new Dictionary<Transform, Renderer[]>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var m = Regex.Match(r.name, @"_LOD(\d)$");
                if (!m.Success) continue;
                int k = int.Parse(m.Groups[1].Value);
                if (k > 2) continue;
                var parent = r.transform.parent != null ? r.transform.parent : r.transform;
                if (!groups.TryGetValue(parent, out var arr)) groups[parent] = arr = new Renderer[3];
                arr[k] = r;
            }
            foreach (var kv in groups)
            {
                var lods = new List<LOD>();
                for (int k = 0; k < 3; k++) if (kv.Value[k]) lods.Add(new LOD(LodHeights[k], new[] { kv.Value[k] }));
                var lg = kv.Key.gameObject.GetComponent<LODGroup>();
                if (lg == null) lg = kv.Key.gameObject.AddComponent<LODGroup>();
                lg.SetLODs(lods.ToArray());
                lg.RecalculateBounds();
            }
            return groups.Count;
        }

        static int Tris(GameObject g, int lod)
        {
            int t = 0;
            foreach (var f in g.GetComponentsInChildren<MeshFilter>(false))
                if (f.sharedMesh && f.name.EndsWith("_LOD" + lod))
                    for (int s = 0; s < f.sharedMesh.subMeshCount; s++) t += (int)(f.sharedMesh.GetIndexCount(s) / 3);
            return t;
        }

        // bounds in the parent's space from mesh bounds x transform matrices (Renderer.bounds is stale right after
        // instantiation). lod0Only: ignore LOD1/LOD2 and inactive (damaged) parts.
        static Bounds Bounds(GameObject g, bool lod0Only)
        {
            var toRoot = g.transform.parent != null ? g.transform.parent.worldToLocalMatrix : Matrix4x4.identity;
            bool any = false; var b = new Bounds();
            foreach (var f in g.GetComponentsInChildren<MeshFilter>(!lod0Only))
            {
                if (f.sharedMesh == null || (lod0Only && Regex.IsMatch(f.name, @"_LOD[12]$"))) continue;
                var m = toRoot * f.transform.localToWorldMatrix;
                var mb = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static void Tex(string path, bool normal, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            if (ti.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) && ti.sRGBTexture == srgb && ti.maxTextureSize == 2048) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material Mat(string set, SetInfo info)
        {
            string P(string kind) => $"{TexDir}/{set}_{kind}.png";
            Tex(P("albedo"), false, true); Tex(P("normal"), true, false); Tex(P("mask"), false, false);
            bool glow = info != null && info.emissive && File.Exists(P("emissive"));
            if (glow) Tex(P("emissive"), false, true);

            Directory.CreateDirectory(Dir + "/Materials");
            string path = $"{Dir}/Materials/{set}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")) { name = set }; AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("albedo")));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("normal")));
            m.SetFloat("_NormalScale", 1f);
            m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("mask")));
            m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
            m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f);
            m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
            if (glow)
            {
                float ev = OYMSetup.CameraEV100 + Mathf.Log(Mathf.Max(info.strength, 1f), 2f) + EmissiveEvBias + (set == "wick_open" ? 0f : EyeExtraEv);
                m.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("emissive")));
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, ev, EmissiveIntensityUnit.EV100);
                m.SetFloat("_EmissiveExposureWeight", 1f);
                Debug.Log($"[OYM-EN] material {set}: emission x{info.strength} -> EV100 {ev:F1}");
            }
            else
            {
                m.SetTexture("_EmissiveColorMap", null);
                HDMaterial.SetUseEmissiveIntensity(m, false);
                HDMaterial.SetEmissiveColor(m, Color.black);
            }
            m.enableInstancing = true;
            HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------------------------------------
        // Test scene: in-context placements + an inspection row (each model alone: sun-lit 3/4 view, and for
        // glowing models a shadow-side view where the HDR emission has to carry the read)
        public class Shot { public string name; public Vector3 pos, target; public float fov = 50f; public string only; }   // only: render just this object (+ sky/lights)
        static readonly List<Shot> s_shots = new List<Shot>();

        [MenuItem("OYM/Enemies/2. Build Test Scene")]
        public static void BuildTestScene() => BuildTestScene(null);

        static void BuildTestScene(Dictionary<string, GameObject> prefabs)
        {
            if (prefabs == null)
            {
                prefabs = new Dictionary<string, GameObject>();
                foreach (var n in MODELS) { var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{n}.prefab"); if (p) prefabs[n] = p; }
            }
            var scene = EditorSceneManager.OpenScene(OYMSetup.ScenePath);
            var M = OYMSetup.LoadManifest();
            Vector3 sun = new Vector3(M.sun_dir[0], M.sun_dir[1], M.sun_dir[2]).normalized;
            Vector3 wc = new Vector3(M.wreck_center[0], M.wreck_center[1], M.wreck_center[2]);
            var root = new GameObject("HUSK Enemies (test)").transform;
            s_shots.Clear();

            GameObject Put(string n, Vector3 pos, Quaternion rot, string label = null)
            {
                if (!prefabs.TryGetValue(n, out var p)) return null;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(p, root);
                g.name = label ?? n;
                g.transform.SetPositionAndRotation(pos, rot);
                return g;
            }

            // --- in context: a WICK pack between spawn and the wreck, PRISMs further out, the CITADEL beyond the wreck
            var toWreck = wc.normalized;
            var rnd = new System.Random(7);
            float R() => (float)rnd.NextDouble() * 2f - 1f;
            for (int i = 0; i < 6; i++)
            {
                var pos = toWreck * (14f + i * 4f) + new Vector3(R() * 6f, R() * 3f, 0f);
                Put(i == 5 ? "wick_open" : "wick_body", pos, Quaternion.LookRotation(-toWreck + new Vector3(R(), R(), 0) * 0.2f), $"WICK_{i}");
            }
            Put("prism_body", toWreck * 70f + new Vector3(16f, 6f, 0f), Quaternion.LookRotation(-toWreck), "PRISM_0");
            Put("prism_charge", toWreck * 95f + new Vector3(-22f, -4f, 0f), Quaternion.LookRotation(-toWreck), "PRISM_1");
            var citadelPos = wc + new Vector3(650f, 160f, 900f);
            Put("citadel_boss", citadelPos, Quaternion.LookRotation(Vector3.left), "CITADEL");          // nose -X, +Z flank to the sun side
            for (int i = 0; i < 12; i++)
                Put("citadel_dart", citadelPos + new Vector3(-180f - i * 9f, 60f + R() * 25f, 40f + R() * 60f), Quaternion.LookRotation(new Vector3(-1f, R() * 0.1f, R() * 0.2f)), $"Dart_{i}");

            Vector3 side = Vector3.Cross(Vector3.up, sun).normalized;
            Vector3 pack = toWreck * 24f;
            s_shots.Add(new Shot { name = "e01_spawn_context", pos = Vector3.zero, target = toWreck * 40f, fov = 70f });
            s_shots.Add(new Shot { name = "e02_pack_sunside", pos = pack + (sun * 0.8f - side * 0.5f).normalized * 16f, target = pack, fov = 60f });
            s_shots.Add(new Shot { name = "e03_citadel_broadside_far", pos = citadelPos + new Vector3(0.35f, 0.3f, 1f).normalized * 1300f, target = citadelPos, fov = 50f });
            s_shots.Add(new Shot { name = "e04_citadel_broadside_near", pos = citadelPos + new Vector3(0.45f, 0.22f, 1f).normalized * 520f, target = citadelPos + new Vector3(-20f, 10f, 0), fov = 50f });
            s_shots.Add(new Shot { name = "e05_citadel_head_on", pos = citadelPos + new Vector3(-1f, 0.12f, 0.25f).normalized * 420f, target = citadelPos, fov = 50f });

            // --- inspection row behind the spawn; renders isolate the target (rocks / wreck switched off)
            var row = new GameObject("Inspection row").transform; row.SetParent(root, false);
            Vector3 basePos = new Vector3(0f, 0f, -900f);
            Vector3 camDir = (sun + side * 0.6f + Vector3.up * 0.15f).normalized;          // sun side, 3/4
            Vector3 flat = new Vector3(camDir.x, 0f, camDir.z).normalized;
            float x = 0f;
            void Inspect(string model, string label, float yaw, Action<GameObject> tweak = null, bool glowShot = false, float distMul = 1.9f)
            {
                if (!prefabs.TryGetValue(model, out var pf)) return;
                var b = Bounds(pf, true);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                Vector3 pos = basePos + Vector3.right * x;
                x += Mathf.Max(size * 3f, 40f);
                var g = Put(model, pos, Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * flat), label);
                g.transform.SetParent(row, true);
                tweak?.Invoke(g);
                Vector3 c = g.transform.TransformPoint(b.center);
                float dist = size * distMul + 0.6f;
                s_shots.Add(new Shot { name = "i_" + label, pos = c + camDir * dist, target = c, fov = 40f, only = label });
                if (glowShot)
                {
                    Vector3 dark = (-sun + side * 0.35f + Vector3.up * 0.1f).normalized;
                    s_shots.Add(new Shot { name = "g_" + label, pos = c + dark * dist, target = c, fov = 40f, only = label });
                }
            }
            Inspect("wick_body", "wick_body", 35f, glowShot: true);
            Inspect("wick_open", "wick_open", 35f, glowShot: true);
            Inspect("prism_body", "prism_body", 35f);
            Inspect("prism_charge", "prism_charge", 35f, glowShot: true);
            Inspect("citadel_dart", "citadel_dart", 35f, glowShot: true);
            Inspect("citadel_boss", "citadel_boss", 75f, glowShot: true, distMul: 1.35f);
            Inspect("citadel_boss", "citadel_boss_launcherL_destroyed", 75f, g =>
            {
                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "citadel_launcher_L") t.gameObject.SetActive(false);
                    if (t.name == "citadel_launcher_L_damaged") t.gameObject.SetActive(true);
                }
            }, glowShot: false, distMul: 1.35f);

            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log($"[OYM-EN] test scene saved -> {TestScene} ({s_shots.Count} shots)");
        }

        // ------------------------------------------------------------------------------------------------
        [MenuItem("OYM/Enemies/3. Capture")]
        public static void Capture() => Capture(null);

        // shots whose name starts with one of `prefixes` (null = all)
        public static void Capture(string[] prefixes)
        {
            if (s_shots.Count == 0) BuildTestScene(null);
            else EditorSceneManager.OpenScene(TestScene);
            string outDir = Path.Combine(OYMCapture.OutDir, "enemies");
            Directory.CreateDirectory(outDir);
            var cam = Camera.main;
            foreach (var dust in cam.GetComponentsInChildren<ParticleSystem>()) dust.gameObject.SetActive(false);   // keep close-ups clean
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            bool first = true;
            // isolation by camera culling mask: inspected objects are moved to an otherwise unused layer. (Toggling
            // thousands of Renderer.enabled per shot made the batch process grow to ~19 GB and thrash.)
            const int IsolateLayer = 30;
            int defaultMask = cam.cullingMask;
            var fogs = UnityEngine.Object.FindObjectsByType<LocalVolumetricFog>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in s_shots)
            {
                if (prefixes != null && !Array.Exists(prefixes, x => s.name.StartsWith(x, StringComparison.Ordinal))) continue;
                // isolate: render only the target's layer (lights, sky and volumes stay as they are)
                Transform only = s.only == null ? null : GameObject.Find(s.only)?.transform;
                var restore = new List<(GameObject go, int layer)>();
                if (only != null) foreach (var t in only.GetComponentsInChildren<Transform>(true)) { restore.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = IsolateLayer; }
                cam.cullingMask = only == null ? defaultMask : 1 << IsolateLayer;
                foreach (var f in fogs) f.enabled = only == null;
                cam.transform.SetPositionAndRotation(s.pos, Quaternion.LookRotation((s.target - s.pos).normalized, Vector3.up));
                cam.fieldOfView = s.fov;
                cam.nearClipPlane = Mathf.Clamp(Vector3.Distance(s.pos, s.target) * 0.01f, 0.02f, 0.3f);
                for (int i = 0; i < (first ? 120 : 40); i++) cam.Render();
                first = false;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, s.name + ".png"), tex.EncodeToPNG());
                foreach (var (go, layer) in restore) go.layer = layer;          // next isolated shot must not see this one
                Debug.Log("[OYM-EN] captured " + s.name);
            }
            cam.targetTexture = null;
            cam.cullingMask = defaultMask;
            rt.Release();
        }
    }
}
