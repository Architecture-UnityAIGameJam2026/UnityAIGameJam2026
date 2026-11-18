// OnYourMark - environment props + pickups (game-ready Meshy assets from meshy/props): HDRP materials, LOD groups,
// colliders, a prop test scene and look-dev captures.
//
//   1) python Tools/prep_props.py      (copies meshy/props/game -> Assets/OYM/Props, converts textures)
//   2) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMProps.BuildAndCapture -quit
//
// Assets come in metres with the front on +Z (cover props pivot on their base, everything else on its centre).
// Prefabs: Assets/OYM/Props/Prefabs/<Category>/<name>.prefab
//   obstacles (cover, asteroids, wreckage): layer OYMSetup.ObstacleLayer (team "Environment"), non-convex MeshCollider on the LOD1 mesh
//   small floating things (pickups, debris, drift rocks, folded shield): convex MeshCollider on the LOD2 mesh
//   (pickups stay on Default; no Rigidbody/trigger - pickup and physics behaviour belong to gameplay code)
//   rock_split_pieces.prefab: the split rock's fragments as separate convex pieces (for a break-apart swap)
// Test scene: Scenes/DebrisField_PropTest.unity (DebrisField.unity is untouched): a cover cluster raycast onto the
// cover asteroid near the spawn + floating debris/pickups, and per-category line-ups -> Screenshots/props/*.png
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    public static class OYMProps
    {
        public const string Dir = OYMSetup.Root + "/Props";
        public const string TexDir = Dir + "/Textures";
        public const string PrefabDir = Dir + "/Prefabs";
        public const string TestScene = OYMSetup.Root + "/Scenes/DebrisField_PropTest.unity";

        // LOD screen-relative heights (LOD0 -> LOD1, LOD1 -> LOD2, LOD2 -> culled); landmarks are never culled
        static readonly float[] LodHeights = { 0.25f, 0.08f, 0.004f };
        static readonly float[] LandmarkLodHeights = { 0.15f, 0.04f, 0f };
        static readonly HashSet<string> Landmarks = new HashSet<string> { "ast_giant", "wreck_ship_bow" };
        static readonly HashSet<string> SmallFloating = new HashSet<string> { "cover_shield_folded" };

        [Serializable] public class Item { public string name, category, origin; public int tex; public bool emissive, pieces; public float emissive_strength; }
        [Serializable] class Prep { public Item[] items; }

        static List<Item> ReadPrep()
        {
            string p = Dir + "/props_prep.json";
            if (!File.Exists(p)) throw new Exception("missing " + p + " (run Tools/prep_props.py)");
            return JsonUtility.FromJson<Prep>(File.ReadAllText(p)).items.ToList();
        }

        static bool IsSmall(Item it) => it.category == "Pickups" || it.name.StartsWith("debris_") || it.name.StartsWith("rock_") || SmallFloating.Contains(it.name);

        [MenuItem("OYM/Props/Build + Capture Prop Test")]
        public static void BuildAndCapture()
        {
            try
            {
                var prefabs = BuildPrefabs();
                BuildTestScene(prefabs);
                Capture(null);
                Debug.Log("[OYM-PR] BuildAndCapture finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-PR] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // scene + captures only (prefabs already built)
        public static void SceneAndCapture()
        {
            try { BuildTestScene(null); Capture(null); Debug.Log("[OYM-PR] SceneAndCapture finished OK"); }
            catch (Exception e) { Debug.LogError("[OYM-PR] failed: " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        // ------------------------------------------------------------------------------------------------
        [MenuItem("OYM/Props/1. Import + Prefabs")]
        public static Dictionary<string, GameObject> BuildPrefabs()
        {
            var items = ReadPrep();
            int obstacle = Mathf.Max(0, LayerMask.NameToLayer(OYMSetup.ObstacleLayer));
            var result = new Dictionary<string, GameObject>();
            foreach (var it in items)
            {
                string fbx = $"{Dir}/{it.name}/{it.name}.fbx";
                if (!ConfigureModel(fbx)) { Debug.LogWarning("[OYM-PR] missing " + fbx); continue; }
                var mat = Mat(it);
                string folder = $"{PrefabDir}/{it.category}";
                Directory.CreateDirectory(folder);

                var root = new GameObject(it.name);
                var inst = Instantiate(fbx, root.transform, "Model");
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                SetupLods(inst, Landmarks.Contains(it.name) ? LandmarkLodHeights : LodHeights);
                bool small = IsSmall(it);
                var colMesh = inst.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith(small ? "_LOD2" : "_LOD1"));
                if (colMesh != null)
                {
                    var mc = colMesh.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = colMesh.sharedMesh;
                    mc.convex = small;
                }
                if (it.category != "Pickups") SetLayer(root, obstacle);

                var b = Bounds(inst, true);
                var p = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{it.name}.prefab");
                UnityEngine.Object.DestroyImmediate(root);
                result[it.name] = p;
                Debug.Log($"[OYM-PR] {it.name}: {it.category}, size {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} m, centre {b.center}, " +
                          $"LOD0 tris {Tris(p, "_LOD0")}, collider {(small ? "convex LOD2" : "mesh LOD1")}" + (it.emissive ? $", emissive x{it.emissive_strength}" : ""));

                if (it.pieces)
                {
                    string pf = $"{Dir}/{it.name}/{it.name}_pieces.fbx";
                    if (!ConfigureModel(pf)) continue;
                    var proot = new GameObject(it.name + "_pieces");
                    var pinst = Instantiate(pf, proot.transform, "Pieces");
                    foreach (var r in pinst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        var mc = r.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                        mc.convex = true;
                    }
                    SetLayer(proot, obstacle);
                    int nPieces = pinst.GetComponentsInChildren<MeshRenderer>(true).Length;
                    var pp = PrefabUtility.SaveAsPrefabAsset(proot, $"{folder}/{it.name}_pieces.prefab");
                    UnityEngine.Object.DestroyImmediate(proot);
                    result[it.name + "_pieces"] = pp;
                    Debug.Log($"[OYM-PR] {it.name}_pieces: {nPieces} convex pieces");
                }
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static bool ConfigureModel(string fbx)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) return false;
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
            return true;
        }

        static GameObject Instantiate(string fbx, Transform parent, string name)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = name;
            inst.transform.SetParent(parent, false);
            return inst;
        }

        static void SetLayer(GameObject g, int layer)
        {
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void SetupLods(GameObject inst, float[] heights)
        {
            var arr = new Renderer[3];
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var m = Regex.Match(r.name, @"_LOD(\d)$");
                if (m.Success && int.Parse(m.Groups[1].Value) < 3) arr[int.Parse(m.Groups[1].Value)] = r;
            }
            var lods = new List<LOD>();
            for (int k = 0; k < 3; k++) if (arr[k]) lods.Add(new LOD(heights[k], new[] { arr[k] }));
            var lg = inst.GetComponent<LODGroup>() ?? inst.AddComponent<LODGroup>();
            lg.SetLODs(lods.ToArray());
            lg.RecalculateBounds();
        }

        static int Tris(GameObject g, string suffix)
        {
            int t = 0;
            foreach (var f in g.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh && f.name.EndsWith(suffix))
                    for (int s = 0; s < f.sharedMesh.subMeshCount; s++) t += (int)(f.sharedMesh.GetIndexCount(s) / 3);
            return t;
        }

        // bounds in the parent's space from mesh bounds x transform matrices (Renderer.bounds is stale right after
        // instantiation). lod0Only: ignore LOD1/LOD2.
        static Bounds Bounds(GameObject g, bool lod0Only)
        {
            var toRoot = g.transform.parent != null ? g.transform.parent.worldToLocalMatrix : Matrix4x4.identity;
            bool any = false; var b = new Bounds();
            foreach (var f in g.GetComponentsInChildren<MeshFilter>(true))
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

        static void Tex(string path, bool normal, bool srgb, int size)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            if (ti.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) && ti.sRGBTexture == srgb && ti.maxTextureSize == size) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = size;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material Mat(Item it)
        {
            string set = it.name;
            string P(string kind) => $"{TexDir}/{set}_{kind}.png";
            int size = Mathf.Clamp(it.tex, 256, 4096);
            Tex(P("albedo"), false, true, size); Tex(P("normal"), true, false, size); Tex(P("mask"), false, false, size);
            bool glow = it.emissive && File.Exists(P("emissive"));
            if (glow) Tex(P("emissive"), false, true, size);

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
                // same rule as the HUSK enemies: emissive EV100 = camera EV100 + log2(strength) + bias
                float ev = OYMSetup.CameraEV100 + Mathf.Log(Mathf.Max(it.emissive_strength, 1f), 2f) + OYMEnemies.EmissiveEvBias;
                m.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("emissive")));
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, ev, EmissiveIntensityUnit.EV100);
                m.SetFloat("_EmissiveExposureWeight", 1f);
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
        public class Shot { public string name; public Vector3 pos, target; public float fov = 40f; public string only; }   // only: render just this object (+ sky/lights)
        static readonly List<Shot> s_shots = new List<Shot>();

        [MenuItem("OYM/Props/2. Build Test Scene")]
        public static void BuildTestScene() => BuildTestScene(null);

        static void BuildTestScene(Dictionary<string, GameObject> prefabs)
        {
            var items = ReadPrep();
            if (prefabs == null)
            {
                prefabs = new Dictionary<string, GameObject>();
                foreach (var it in items)
                    foreach (var n in new[] { it.name, it.name + "_pieces" })
                    {
                        var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{it.category}/{n}.prefab");
                        if (p) prefabs[n] = p;
                    }
            }
            var scene = EditorSceneManager.OpenScene(OYMSetup.ScenePath);
            var M = OYMSetup.LoadManifest();
            Vector3 sun = new Vector3(M.sun_dir[0], M.sun_dir[1], M.sun_dir[2]).normalized;
            Vector3 wc = new Vector3(M.wreck_center[0], M.wreck_center[1], M.wreck_center[2]);
            var root = new GameObject("Props (test)").transform;
            s_shots.Clear();
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble() * 2f - 1f;

            GameObject Put(string n, Vector3 pos, Quaternion rot, Transform parent, string label = null)
            {
                if (!prefabs.TryGetValue(n, out var p)) { Debug.LogWarning("[OYM-PR] no prefab " + n); return null; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
                g.name = label ?? n;
                g.transform.SetPositionAndRotation(pos, rot);
                return g;
            }

            // --- in context: the cover asteroid off to the side of the spawn->wreck line, cover raycast onto its top,
            //     pickups and small debris drifting around it, a HUSK-claimed rock and wreckage further out
            var ctx = new GameObject("Context").transform; ctx.SetParent(root, false);
            Vector3 toWreck = wc.normalized;
            Vector3 dir = Quaternion.AngleAxis(-28f, Vector3.up) * toWreck;
            Vector3 rockPos = dir * 42f + Vector3.down * 6f;
            var cluster = new GameObject("CoverCluster").transform; cluster.SetParent(ctx, false);
            var coverRock = Put("ast_cover_drill", rockPos, Quaternion.LookRotation(-new Vector3(dir.x, 0, dir.z).normalized), cluster, "CoverAsteroid");
            Physics.SyncTransforms();
            var placed = new List<string>();
            // flat-spot search: grid of downward rays over the rock; a spot fits when the ray at the centre and at the
            // four footprint corners all land on the rock within 0.35 m of each other (rejects the drill rig, ledges
            // and the rim), keeps clear of cover already placed; higher spots win ties
            float? Down(Vector3 p, out Vector3 normal)
            {
                normal = Vector3.up;
                var hs = Physics.RaycastAll(new Vector3(p.x, rockPos.y + 40f, p.z), Vector3.down, 80f)
                                .Where(h => h.collider.transform.IsChildOf(coverRock.transform)).OrderBy(h => h.distance).ToArray();
                if (hs.Length == 0) return null;
                normal = hs[0].normal;
                return hs[0].point.y;
            }
            var used = new List<(Vector3 p, float r)>();
            void OnRock(string n)
            {
                if (coverRock == null || !prefabs.TryGetValue(n, out var pf)) return;
                var fb = Bounds(pf, true);
                float hx = fb.size.x / 2f, hz = fb.size.z / 2f, r = Mathf.Max(hx, hz);
                (Vector3 pos, Vector3 up, float yaw, float score)? best = null;
                for (float gx = -7.5f; gx <= 7.5f; gx += 0.75f)
                    for (float gz = -7.5f; gz <= 7.5f; gz += 0.75f)
                    {
                        Vector3 c = rockPos + new Vector3(gx, 0f, gz);
                        if (used.Any(u => Vector2.Distance(new Vector2(u.p.x, u.p.z), new Vector2(c.x, c.z)) < u.r + r + 0.8f)) continue;
                        var y0 = Down(c, out var n0);
                        if (y0 == null || n0.y < 0.8f) continue;
                        for (float yaw = 0f; yaw < 180f; yaw += 30f)
                        {
                            var q = Quaternion.AngleAxis(yaw, Vector3.up);
                            float dev = 0f; bool ok = true; Vector3 nsum = n0;
                            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                            {
                                var y = Down(c + q * new Vector3(sx * hx, 0f, sz * hz), out var nn);
                                if (y == null) { ok = false; break; }
                                dev = Mathf.Max(dev, Mathf.Abs(y.Value - y0.Value)); nsum += nn;
                            }
                            if (!ok || dev > 0.35f) continue;
                            float score = dev - (y0.Value - rockPos.y) * 0.05f;
                            if (best == null || score < best.Value.score)
                                best = (new Vector3(c.x, y0.Value, c.z), Vector3.Slerp(Vector3.up, nsum.normalized, 0.5f).normalized, yaw, score);
                        }
                    }
                if (best == null) { Debug.Log($"[OYM-PR] cover {n}: no flat spot on the asteroid"); return; }
                var bv = best.Value;
                var fwd = Vector3.ProjectOnPlane(Quaternion.AngleAxis(bv.yaw, Vector3.up) * Vector3.forward, bv.up).normalized;
                if (Vector3.Dot(fwd, -dir) < 0f) fwd = -fwd;                                     // cover faces the spawn side
                var g = Put(n, bv.pos - bv.up * 0.08f, Quaternion.LookRotation(fwd, bv.up), cluster);   // magnetic feet sink in a little
                if (g == null) return;
                used.Add((bv.pos, r));
                placed.Add(n);
                Debug.Log($"[OYM-PR] cover {n}: spot {bv.pos - rockPos} (local), yaw {bv.yaw}, flatness {bv.score:F2}");
            }
            OnRock("cover_crate_stack");
            OnRock("cover_barricade_high");
            OnRock("cover_barricade_low");
            OnRock("cover_shield_deployed");
            Debug.Log($"[OYM-PR] cover on the asteroid: {string.Join(", ", placed)}");

            Vector3 cl = rockPos + Vector3.up * 8f;
            string[] floaters = { "pk_medkit_closed", "pk_ammo_closed", "pk_shield_cell", "pk_booster_fuel_cell", "debris_panel", "debris_tank",
                                  "debris_cable_coil", "debris_beacon_cage", "rock_intact", "rock_cracked", "debris_pipe_elbow", "debris_vent_grate" };
            for (int i = 0; i < floaters.Length; i++)
            {
                float a = i * 360f / floaters.Length;
                var pos = cl + Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * (9f + R() * 3f) + Vector3.up * R() * 3f - dir * 6f;
                Put(floaters[i], pos, Quaternion.Euler(R() * 40f, R() * 180f, R() * 40f), ctx);
            }
            Put("cover_container", rockPos - dir * 14f + Vector3.Cross(Vector3.up, dir) * 10f + Vector3.up * 3f, Quaternion.Euler(8f, 70f, -5f), ctx);
            Put("ast_husk", dir * 120f + Vector3.Cross(Vector3.up, dir) * -45f + Vector3.up * 15f, Quaternion.Euler(10f, 140f, 0f), ctx);
            Put("wreck_hab_module", toWreck * 60f + Vector3.Cross(Vector3.up, toWreck) * 30f + Vector3.up * 12f, Quaternion.Euler(5f, 60f, 12f), ctx);
            Put("wreck_truss_long", toWreck * 48f + Vector3.Cross(Vector3.up, toWreck) * 14f + Vector3.up * 2f, Quaternion.Euler(20f, 30f, 35f), ctx);
            Put("wreck_solar_wing", dir * 90f + Vector3.up * 30f, Quaternion.Euler(30f, -20f, 15f), ctx);

            Vector3 side = Vector3.Cross(Vector3.up, sun).normalized;
            s_shots.Add(new Shot { name = "p01_context_from_spawn", pos = Vector3.zero, target = rockPos + Vector3.up * 4f, fov = 65f });
            Vector3 clusterCam = (Vector3.up * 0.35f - dir + Vector3.Cross(Vector3.up, dir) * 0.45f + sun * 0.3f).normalized;
            s_shots.Add(new Shot { name = "p02_cover_on_asteroid", pos = rockPos + clusterCam * 26f, target = rockPos + Vector3.up * 4f, fov = 50f });
            s_shots.Add(new Shot { name = "p03_cover_cluster_isolated", pos = rockPos + clusterCam * 26f, target = rockPos + Vector3.up * 4f, fov = 50f, only = "CoverCluster" });
            s_shots.Add(new Shot { name = "p04_cover_cluster_other_side", pos = rockPos + Vector3.Reflect(clusterCam, Vector3.Cross(Vector3.up, dir)) * 26f, target = rockPos + Vector3.up * 4f, fov = 50f, only = "CoverCluster" });

            // --- line-ups per category, far behind the spawn; renders isolate each group
            Vector3 camDir = (sun + side * 0.6f + Vector3.up * 0.25f).normalized;           // sun side, 3/4 from above
            Vector3 flat = new Vector3(camDir.x, 0f, camDir.z).normalized;
            Vector3 along = Vector3.Cross(Vector3.up, flat).normalized;
            var groups = new (string label, string[] names, bool glow)[]
            {
                ("Pickups", items.Where(i => i.category == "Pickups").Select(i => i.name).ToArray(), true),
                ("Cover", items.Where(i => i.category == "Cover").Select(i => i.name).ToArray(), false),
                ("Debris", items.Where(i => i.name.StartsWith("debris_")).Select(i => i.name).ToArray(), false),
                ("Rocks", new[] { "rock_intact", "rock_cracked", "rock_split", "rock_split_pieces" }, false),
                ("Wreckage", new[] { "wreck_truss_corner", "wreck_hull_rib", "wreck_hull_panel", "wreck_truss_short", "wreck_hull_large", "wreck_truss_long", "wreck_hab_module", "wreck_solar_wing" }, false),
                ("Asteroids", new[] { "ast_cover_drill", "ast_metal", "ast_ice", "ast_husk" }, true),
                ("Landmarks", new[] { "wreck_ship_bow", "ast_giant" }, false),
            };
            Vector3 groupBase = new Vector3(0f, 0f, -3000f);
            foreach (var (label, names, glow) in groups)
            {
                var gRoot = new GameObject("PropGroup_" + label).transform; gRoot.SetParent(root, false);
                var sizes = names.Where(prefabs.ContainsKey).Select(n => (n, b: Bounds(prefabs[n], true))).ToList();
                if (sizes.Count == 0) continue;
                float gap = sizes.Average(s => Mathf.Max(s.b.size.x, s.b.size.z)) * 0.35f;
                float length = sizes.Sum(s => Mathf.Max(s.b.size.x, s.b.size.z)) + gap * (sizes.Count - 1);
                float height = sizes.Max(s => s.b.size.y);
                float t = -length / 2f;
                var rot = Quaternion.LookRotation(Quaternion.Euler(0f, 30f, 0f) * flat);
                foreach (var (n, b) in sizes)
                {
                    float w = Mathf.Max(b.size.x, b.size.z);
                    Vector3 slot = groupBase + along * (t + w / 2f);
                    Put(n, slot - rot * b.center, rot, gRoot);
                    t += w + gap;
                }
                float hfov = 2f * Mathf.Atan(Mathf.Tan(20f * Mathf.Deg2Rad) * 16f / 9f);
                float dist = Mathf.Max(length / 2f / Mathf.Tan(hfov / 2f) * 1.12f, height / 2f / Mathf.Tan(20f * Mathf.Deg2Rad) * 1.4f) + height * 0.5f;
                s_shots.Add(new Shot { name = "l_" + label, pos = groupBase + camDir * dist, target = groupBase, fov = 40f, only = gRoot.name });
                if (glow)
                {
                    Vector3 dark = (-sun + side * 0.35f + Vector3.up * 0.2f).normalized;
                    s_shots.Add(new Shot { name = "g_" + label, pos = groupBase + dark * dist, target = groupBase, fov = 40f, only = gRoot.name });
                }
                groupBase += Vector3.right * Mathf.Max(length * 2f + 200f, 400f);
            }

            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log($"[OYM-PR] test scene saved -> {TestScene} ({s_shots.Count} shots)");
        }

        // ------------------------------------------------------------------------------------------------
        [MenuItem("OYM/Props/3. Capture")]
        public static void Capture() => Capture(null);

        // shots whose name starts with one of `prefixes` (null = all)
        public static void Capture(string[] prefixes)
        {
            if (s_shots.Count == 0) BuildTestScene(null);
            else EditorSceneManager.OpenScene(TestScene);
            string outDir = Path.Combine(OYMCapture.OutDir, "props");
            Directory.CreateDirectory(outDir);
            var cam = Camera.main;
            foreach (var dust in cam.GetComponentsInChildren<ParticleSystem>()) dust.gameObject.SetActive(false);   // keep close-ups clean
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            bool first = true;
            const int IsolateLayer = 30;   // isolation by culling mask (same as OYMEnemies.Capture)
            int defaultMask = cam.cullingMask;
            var fogs = UnityEngine.Object.FindObjectsByType<LocalVolumetricFog>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in s_shots)
            {
                if (prefixes != null && !Array.Exists(prefixes, x => s.name.StartsWith(x, StringComparison.Ordinal))) continue;
                Transform only = s.only == null ? null : GameObject.Find(s.only)?.transform;
                var restore = new List<(GameObject go, int layer)>();
                if (only != null) foreach (var t in only.GetComponentsInChildren<Transform>(true)) { restore.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = IsolateLayer; }
                cam.cullingMask = only == null ? defaultMask : 1 << IsolateLayer;
                foreach (var f in fogs) f.enabled = only == null;
                cam.transform.SetPositionAndRotation(s.pos, Quaternion.LookRotation((s.target - s.pos).normalized, Vector3.up));
                cam.fieldOfView = s.fov;
                float d = Vector3.Distance(s.pos, s.target);
                cam.nearClipPlane = Mathf.Clamp(d * 0.01f, 0.02f, 0.3f);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, d * 4f);
                for (int i = 0; i < (first ? 120 : 40); i++) cam.Render();
                first = false;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, s.name + ".png"), tex.EncodeToPNG());
                foreach (var (go, layer) in restore) go.layer = layer;
                Debug.Log("[OYM-PR] captured " + s.name);
            }
            cam.targetTexture = null;
            cam.cullingMask = defaultMask;
            rt.Release();
        }
    }
}
