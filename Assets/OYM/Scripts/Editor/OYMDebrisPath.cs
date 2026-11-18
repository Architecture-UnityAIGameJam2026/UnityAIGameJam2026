// OnYourMark - floating debris path for the showcase (user feedback 11-18: "no flat floor; stand on a fragment, let
// fragments link up into a path, and have rocks drift around").
//
// * Stones: the flattest real fragments of the simulated wreck (Generated/Wreck/*.oymmesh), turned with PCA so their
//   thinnest axis points up. The biggest one is the spawn fragment (top at y = 0, the zero-g characters float on it),
//   then ~16 stepping stones meander towards the wreck, rising slowly, tilted a few degrees and bobbing gently.
// * Props (Meshy session, Assets/OYM/Props/<name>/*.prefab), used only when their prefabs exist:
//   truss beams bridge the gaps between stones, cover / small debris sit on the stones, prop asteroids and rocks drift.
// * Drifting: the arena's own small/medium asteroids near the spawn and a swarm of small rocks circle the path
//   (OYM.Drift: slow orbit + bob; OYM.Spin for tumbling).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OYM.EditorTools
{
    public static class OYMDebrisPath
    {
        public class Stone { public Vector3 top; public float size; public Transform t; }
        class Frag { public WreckEntry w; public Mesh mesh, col; public Vector3 mu; public Quaternion align; public float size, thick, flat; }

        static string Rel(string rel) => OYMSetup.Gen + "/" + (rel.StartsWith("wreck/") ? "Wreck/" + rel.Substring(6) : rel.StartsWith("rocks/") ? "Rocks/" + rel.Substring(6) : rel);

        static readonly Dictionary<string, GameObject> s_props = new Dictionary<string, GameObject>();
        /// prop prefab from the Meshy props import (null until that session has made the prefabs)
        public static GameObject Prop(string name)
        {
            if (s_props.TryGetValue(name, out var p)) return p;
            p = null;
            if (AssetDatabase.IsValidFolder(OYMSetup.Root + "/Props"))
                foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { OYMSetup.Root + "/Props" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == name) { p = AssetDatabase.LoadAssetAtPath<GameObject>(path); break; }
                }
            s_props[name] = p;
            return p;
        }

        static GameObject PutProp(string name, Transform parent, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            var pf = Prop(name);
            if (pf == null) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale *= scale;
            return g;
        }

        // PCA of the fragment's vertices: `align` turns the thinnest axis up and the longest axis forward
        static Frag Analyse(WreckEntry w)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Rel(w.mesh));
            if (mesh == null) return null;
            var v = mesh.vertices;
            if (v.Length < 60) return null;
            int step = Mathf.Max(1, v.Length / 4000);
            Vector3 mu = Vector3.zero; int n = 0;
            for (int i = 0; i < v.Length; i += step) { mu += v[i]; n++; }
            mu /= n;
            var C = new double[3, 3];
            for (int i = 0; i < v.Length; i += step)
            {
                var d = v[i] - mu;
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) C[a, b] += d[a] * d[b] / n;
            }
            Jacobi(C, out var e, out var V);
            int[] o = { 0, 1, 2 };
            System.Array.Sort(o, (x, y) => e[y].CompareTo(e[x]));
            Vector3 Ev(int k) => new Vector3((float)V[0, k], (float)V[1, k], (float)V[2, k]).normalized;
            float l2 = Mathf.Sqrt((float)e[o[1]]), l3 = Mathf.Sqrt((float)e[o[2]]);
            return new Frag
            {
                w = w, mesh = mesh, mu = mu,
                col = string.IsNullOrEmpty(w.collider) ? null : AssetDatabase.LoadAssetAtPath<Mesh>(Rel(w.collider)),
                align = Quaternion.Inverse(Quaternion.LookRotation(Ev(o[0]), Ev(o[2]))),
                size = Mathf.Sqrt((float)e[o[0]]) * 3.4f, thick = l3 * 3.4f, flat = l3 / Mathf.Max(l2, 1e-3f),
            };
        }

        // symmetric 3x3 eigen-decomposition (cyclic Jacobi); columns of V are the eigenvectors
        static void Jacobi(double[,] A, out double[] e, out double[,] V)
        {
            V = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            var a = (double[,])A.Clone();
            for (int sweep = 0; sweep < 50; sweep++)
            {
                int p = 0, q = 1; double max = System.Math.Abs(a[0, 1]);
                if (System.Math.Abs(a[0, 2]) > max) { p = 0; q = 2; max = System.Math.Abs(a[0, 2]); }
                if (System.Math.Abs(a[1, 2]) > max) { p = 1; q = 2; max = System.Math.Abs(a[1, 2]); }
                if (max < 1e-12) break;
                double th = 0.5 * System.Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                double cs = System.Math.Cos(th), sn = System.Math.Sin(th);
                for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = cs * akp - sn * akq; a[k, q] = sn * akp + cs * akq; }
                for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = cs * apk - sn * aqk; a[q, k] = sn * apk + cs * aqk; }
                for (int k = 0; k < 3; k++) { double vkp = V[k, p], vkq = V[k, q]; V[k, p] = cs * vkp - sn * vkq; V[k, q] = sn * vkp + cs * vkq; }
            }
            e = new[] { a[0, 0], a[1, 1], a[2, 2] };
        }

        // Path stones are seen from a metre or two away, where the arena hull tiling (24 m / 9 m) repeats visibly
        // (user feedback 11-18). Their own copies tile PathTileScale x larger; the distant wreck keeps the originals.
        public const float PathTileScale = 8f;
        static readonly Dictionary<string, Material> s_pathMats = new Dictionary<string, Material>();
        static Material PathMat(string src)
        {
            if (s_pathMats.TryGetValue(src, out var m) && m != null) return m;
            var orig = AssetDatabase.LoadAssetAtPath<Material>($"{OYMSetup.MatDir}/{src}.mat");
            string path = $"{OYMSetup.MatDir}/Path_{src}.mat";
            AssetDatabase.DeleteAsset(path);
            m = new Material(orig) { name = "Path_" + src };
            m.SetFloat("_TexWorldScale", orig.GetFloat("_TexWorldScale") / PathTileScale);
            AssetDatabase.CreateAsset(m, path);
            s_pathMats[src] = m;
            return m;
        }

        static GameObject PlaceFrag(Frag f, Transform parent, string name, Vector3 topCentre, Quaternion extra)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rot = extra * f.align;
            go.transform.SetPositionAndRotation(topCentre - rot * f.mu - Vector3.up * (f.thick * 0.5f), rot);
            go.AddComponent<MeshFilter>().sharedMesh = f.mesh;
            var ext = PathMat("Hull_Exterior");
            var inn = PathMat("Hull_Interior");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { ext, inn, inn };          // torn edges cooled: no glow underfoot
            if (f.col != null) go.AddComponent<MeshCollider>().sharedMesh = f.col;
            go.layer = Mathf.Max(0, LayerMask.NameToLayer(OYMSetup.ObstacleLayer));
            return go;
        }

        public static List<Stone> BuildPath(Manifest M, Vector3 wreckCentre)
        {
            var frags = new List<Frag>();
            foreach (var w in M.wreck)
                if (w.radius >= 3f && w.radius <= 30f) { var f = Analyse(w); if (f != null && f.flat < 0.45f) frags.Add(f); }
            frags.Sort((a, b) => a.flat.CompareTo(b.flat));                                  // flattest first
            var used = new HashSet<Frag>();
            Frag Pick(float minSize, float maxSize)
            {
                foreach (var cand in frags) if (!used.Contains(cand) && cand.size >= minSize && cand.size <= maxSize) { used.Add(cand); return cand; }
                foreach (var cand in frags) if (!used.Contains(cand)) { used.Add(cand); return cand; }
                return null;
            }
            var rnd = new System.Random(23);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var root = new GameObject("Debris Path (spawn fragment + stepping stones)").transform;
            var stones = new List<Stone>();

            // spawn fragment: big and flat, top at y = 0 under the floating characters
            var plat = Pick(16f, 45f);
            var pgo = PlaceFrag(plat, root, "SpawnFragment", Vector3.zero, Quaternion.Euler(0f, R(0f, 360f), 0f));
            stones.Add(new Stone { top = Vector3.zero, size = plat.size, t = pgo.transform });
            Debug.Log($"[OYM-SC] spawn fragment {plat.w.name}: {plat.size:F1} m across, {plat.thick:F1} m thick, flatness {plat.flat:F2}");

            // stepping stones meandering towards the wreck
            Vector3 fwd = new Vector3(wreckCentre.x, 0f, wreckCentre.z).normalized, right = Vector3.Cross(Vector3.up, fwd);
            float dist = plat.size * 0.5f + 5f;
            for (int i = 0; i < 16; i++)
            {
                var f = Pick(7f, 22f);
                if (f == null) break;
                dist += f.size * 0.5f + R(3f, 7f);
                var top = fwd * dist + right * (Mathf.Sin(i * 0.75f) * 10f + R(-3f, 3f)) + Vector3.up * (i * 1.4f + R(-1f, 1.5f));
                var go = PlaceFrag(f, root, $"PathStone_{i:00}", top, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)));
                var dr = go.AddComponent<OYM.Drift>();
                dr.center = go.transform.position; dr.degreesPerSecond = 0f; dr.bobAmplitude = R(0.15f, 0.45f); dr.bobPeriod = R(8f, 14f);
                stones.Add(new Stone { top = top, size = f.size, t = go.transform });
                dist += f.size * 0.5f;
            }

            // props: truss beams bridging the gaps, cover and loose debris on the stones (only if the prefabs exist)
            string[] cover = { "cover_barricade_low", "cover_crate_stack", "cover_hull_slab", "cover_container", "cover_barricade_high", "cover_bulkhead" };
            string[] loose = { "debris_panel", "debris_tank", "debris_pipe_elbow", "debris_junction_box", "debris_cable_coil", "debris_bracket", "debris_panel_shard", "debris_vent_grate" };
            int props = 0;
            for (int i = 1; i < stones.Count; i++)
            {
                var a = stones[i - 1]; var b = stones[i];
                Vector3 mid = (a.top + b.top) * 0.5f - Vector3.up * 0.6f;
                Vector3 span = b.top - a.top;
                if (i % 2 == 1 && PutProp(span.magnitude > 9f ? "wreck_truss_long" : "wreck_truss_short", root, mid,
                        // truss props run along their local X (10 m / 6 m wide): turn X onto the gap, roll a little about it
                        Quaternion.LookRotation(span.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(R(-15f, 15f), 0f, 0f)) != null) props++;
                Vector3 off = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward * (b.size * R(0.15f, 0.3f));
                if (i % 3 == 0 && PutProp(cover[(i / 3) % cover.Length], b.t, b.top + off, Quaternion.Euler(0f, R(0f, 360f), 0f)) != null) props++;
                for (int k = 0; k < 2; k++)
                {
                    Vector3 o2 = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward * (b.size * R(0.1f, 0.4f));
                    if (PutProp(loose[rnd.Next(loose.Length)], b.t, b.top + o2 + Vector3.up * 0.3f, Random.rotationUniform) != null) props++;
                }
            }
            // a few loose pieces around the spawn fragment
            for (int k = 0; k < 6; k++)
            {
                Vector3 o2 = Quaternion.Euler(0f, k * 60f + R(-20f, 20f), 0f) * Vector3.forward * (plat.size * R(0.25f, 0.45f));
                if (PutProp(loose[rnd.Next(loose.Length)], pgo.transform, o2 + Vector3.up * 0.3f, Random.rotationUniform) != null) props++;
            }
            Debug.Log($"[OYM-SC] debris path: {stones.Count - 1} stones, {dist:F0} m long, {props} props" + (Prop("wreck_truss_long") == null ? " (prop prefabs not imported yet)" : ""));
            return stones;
        }

        public static void AddDrifters(Manifest M, List<Stone> stones)
        {
            var rnd = new System.Random(41);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            Vector3 mid = stones[stones.Count / 2].top;
            Vector3 Axis() => (Vector3.up + new Vector3(R(-0.35f, 0.35f), 0f, R(-0.35f, 0.35f))).normalized;
            void MakeDrift(GameObject obj, float speed, float bob)
            {
                var dr = obj.GetComponent<OYM.Drift>();
                if (dr == null) dr = obj.AddComponent<OYM.Drift>();
                float d = Vector3.Distance(obj.transform.position, mid);
                dr.center = mid; dr.axis = Axis();
                dr.degreesPerSecond = speed / Mathf.Max(d, 10f) * Mathf.Rad2Deg * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                dr.bobAmplitude = bob; dr.bobPeriod = R(12f, 40f);
            }
            bool NearPath(Vector3 q, float clear)
            {
                foreach (var st in stones) if (Vector3.Distance(q, st.top) < st.size * 0.5f + clear) return true;
                return false;
            }
            int drifting = 0;

            // the arena's own small / medium asteroids near the spawn start circling the path slowly
            var asteroids = GameObject.Find("Asteroids");
            if (asteroids != null)
                foreach (Transform r in asteroids.transform)
                {
                    float rad = r.localScale.x, d = Vector3.Distance(r.position, mid);
                    if (rad > 60f || d > 1800f) continue;
                    MakeDrift(r.gameObject, R(3f, 8f), rad * 0.15f);
                    drifting++;
                }

            // prop asteroids (24 m) and rocks drifting past the path
            var root = new GameObject("Drifting Rocks (near path)").transform;
            string[] bigRocks = { "ast_ice", "ast_metal", "ast_husk", "ast_ice", "ast_metal", "ast_cover_drill" };
            for (int i = 0; i < bigRocks.Length; i++)
            {
                Vector3 p; int guard = 0;
                do { p = mid + new Vector3(R(-220f, 220f), R(-60f, 80f), R(-200f, 260f)); } while ((NearPath(p, 30f) || p.magnitude < 60f) && ++guard < 50);
                var g = PutProp(bigRocks[i], root, p, Random.rotationUniform);
                if (g == null) continue;
                var sp = g.AddComponent<OYM.Spin>(); sp.axis = Random.onUnitSphere; sp.degreesPerSecond = R(0.5f, 2f);
                MakeDrift(g, R(2f, 5f), R(1f, 4f));
                drifting++;
            }

            // a swarm of small rocks: prop rocks when available, else the simulation's rock variants
            var lods = new List<Mesh[]>();
            foreach (var rv in M.rock_variants)
            {
                var arr = new Mesh[rv.lods.Length];
                for (int l = 0; l < arr.Length; l++) arr[l] = AssetDatabase.LoadAssetAtPath<Mesh>(Rel(rv.lods[l]));
                lods.Add(arr);
            }
            var matA = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Rock_A.mat");
            var matB = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Rock_B.mat");
            float[] lodH = { 0.12f, 0.03f, 0.0015f };
            string[] smallProps = { "rock_intact", "rock_cracked", "rock_split", "debris_panel_shard", "debris_panel", "debris_bracket" };
            for (int i = 0; i < 64; i++)
            {
                float rad = Mathf.Lerp(0.5f, 6f, Mathf.Pow(R(0f, 1f), 2.2f));                     // many pebbles, a few boulders
                Vector3 p; int guard = 0;
                do { p = mid + new Vector3(R(-150f, 150f), R(-30f, 40f), R(-170f, 190f)); } while (NearPath(p, 4f + rad) && ++guard < 50);
                GameObject go = null;
                string pn = smallProps[rnd.Next(smallProps.Length)];
                var pf = Prop(pn);
                if (pf != null)
                {
                    float s = pn.StartsWith("rock_") ? rad / 0.6f : R(1f, 2.5f);                    // prop rocks are ~1.2 m
                    go = PutProp(pn, root, p, Random.rotationUniform, s);
                }
                else
                {
                    go = new GameObject($"DriftRock_{i:00}");
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(p, Random.rotationUniform);
                    go.transform.localScale = Vector3.one * rad;
                    var meshes = lods[rnd.Next(lods.Count)];
                    var mat = rnd.NextDouble() < 0.5 ? matA : matB;
                    var ls = new LOD[meshes.Length];
                    for (int l = 0; l < meshes.Length; l++)
                    {
                        var c = new GameObject("LOD" + l); c.transform.SetParent(go.transform, false);
                        c.AddComponent<MeshFilter>().sharedMesh = meshes[l];
                        var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                        ls[l] = new LOD(lodH[l], new Renderer[] { mr });
                    }
                    var lg = go.AddComponent<LODGroup>(); lg.SetLODs(ls); lg.RecalculateBounds();
                }
                var spin = go.AddComponent<OYM.Spin>(); spin.axis = Random.onUnitSphere; spin.degreesPerSecond = R(4f, 25f) / Mathf.Sqrt(rad);
                MakeDrift(go, R(1.5f, 5f), R(0.3f, 2f));
                drifting++;
            }
            Debug.Log($"[OYM-SC] drifting rocks/asteroids: {drifting}");
        }
    }
}
