// OnYourMark - builds the "MASS CONVERGENCE" event-zone test scene (design/EVENT_ASTEROID_COLLISION.md) and captures stills.
//
//   batch:       Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMCollisionEvent.BuildAndCapture -quit
//   interactive: menu OYM/Event/Open + Play (Asteroid Collision)   (F5 replay, F6 jump to the dash lane)
//
// The scene is a copy of the showcase (debris field + characters + floating debris path; DebrisField.unity and the
// showcase scene are not touched). The event rig sits on the dark side of the spawn (world -Z, sun behind-left), its
// own +Z being the event forward. Runtime: OYM.CollisionEvent (everything is a pure function of the event clock).
// Props are found through OYMDebrisPath.Prop (same lookup as the showcase), enemies from Enemies/Prefabs.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Ev = OYM.CollisionEvent;
using Object = UnityEngine.Object;

namespace OYM.EditorTools
{
    public static class OYMCollisionEvent
    {
        public const string ScenePath = OYMSetup.Root + "/Scenes/Event_AsteroidCollision.unity";
        const string FxDir = OYMSetup.Root + "/FX/Event";
        const string EnemyDir = OYMSetup.Root + "/Enemies/Prefabs";
        static string ShotDir => Path.Combine(OYMCapture.OutDir, "event_collision");
        /// event forward = world -Z: clean dark sky with the nebula, sun behind-left, the wreck and debris path behind
        static readonly Quaternion RigRot = Quaternion.Euler(0f, 180f, 0f);

        static System.Random _rnd = new System.Random(1118);
        static float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);
        static float Sign() => _rnd.NextDouble() < 0.5 ? -1f : 1f;
        static Quaternion RandRot() => Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
        static Vector3 RandDir() { Vector3 v; do v = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)); while (v.sqrMagnitude > 1f || v.sqrMagnitude < 1e-3f); return v.normalized; }

        // ------------------------------------------------------------------ build
        [MenuItem("OYM/Event/Build Asteroid Collision Event")]
        public static void Build()
        {
            string src = File.Exists(OYMShowcase.ScenePath) ? OYMShowcase.ScenePath : OYMSetup.ScenePath;
            if (!File.Exists(src)) throw new FileNotFoundException("base scene missing", src);
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(src, ScenePath)) throw new System.Exception("could not copy " + src + " -> " + ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[OYM-EV] base scene " + src);
            _rnd = new System.Random(1118);

            var test = GameObject.Find("HUSK Enemies (test)");
            if (test) test.SetActive(false);                                    // the showcase's enemy row sits right at the spawn

            var root = new GameObject("Event: MASS CONVERGENCE (asteroid collision prototype)");
            root.transform.SetPositionAndRotation(Vector3.zero, RigRot);
            var ev = root.AddComponent<Ev>();
            ev.glowMat = FxMat("FX_Event_Glow", "fx_glow");
            ev.beamMat = FxMat("FX_Event_Beam", "fx_beam");
            ev.flashLight = FlashLight(root.transform);

            Asteroids(ev, Group(root, "Asteroids (colliding)"), out var halfA, out var halfB);
            Fragments(ev, Group(root, "Fragments (burst)"), halfA, halfB);
            Enemies(ev, Group(root, "HUSK (emerging)"));
            WreckPath(ev, Group(root, "Wreck path (locks in)"));
            LaneDebris(ev, Group(root, "Dash lane debris"));
            ev.laneAutoTime = ev.pathReadyTime + 1f;

            ClearZone(root.transform);
            FaceEvent();
            EditorUtility.SetDirty(ev);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[OYM-EV] saved {ScenePath}: {ev.pieces.Count} pieces, impact {ev.impactTime:F1}s, path ready {ev.pathReadyTime:F2}s, lane {Vector3.Distance(ev.laneStart, ev.laneEnd):F0} m");
        }

        [MenuItem("OYM/Event/Open + Play (Asteroid Collision)")]
        public static void OpenAndPlay()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
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
                Debug.Log("[OYM-EV] BuildAndCapture finished OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-EV] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void CaptureOnly()
        {
            try { Capture(); Debug.Log("[OYM-EV] CaptureOnly finished OK"); }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-EV] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ helpers
        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root.transform, false);
            return g;
        }

        static Material FxMat(string name, string sprite)
        {
            if (!AssetDatabase.IsValidFolder(FxDir)) AssetDatabase.CreateFolder(OYMSetup.Root + "/FX", "Event");
            var sh = Shader.Find("OYM/FX/AdditiveHDR");
            if (sh == null) throw new System.Exception("shader OYM/FX/AdditiveHDR missing");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{OYMSetup.Root}/FX/{sprite}.png");
            if (tex == null) throw new System.Exception("FX sprite missing " + sprite);
            string path = $"{FxDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_Intensity", 1f);                                       // HDR intensity rides in the float vertex colours
            EditorUtility.SetDirty(m);
            return m;
        }

        static Light FlashLight(Transform root)
        {
            var go = new GameObject("FX flash light (impact)");
            go.transform.SetParent(root, false);
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = 2500f; l.color = Color.white; l.useColorTemperature = false;
            l.shadows = LightShadows.None; l.enabled = false;
            hd.affectsVolumetric = false;                                       // 7e9 lm in the space fog would white out the whole frame
            return l;
        }

        static GameObject Prefab(string name)
        {
            var p = OYMDebrisPath.Prop(name);
            if (p == null) p = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyDir}/{name}.prefab");
            if (p == null) throw new System.Exception("prefab not found: " + name);
            return p;
        }

        static GameObject Put(string name, Transform parent, float scale, bool keepColliders = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(name), parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = go.transform.localScale * scale;
            if (!keepColliders) foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return go;
        }

        /// bounds of all meshes in `go`'s own local space (go's scale not applied)
        static Bounds LocalBounds(GameObject go)
        {
            var w2l = go.transform.worldToLocalMatrix;
            bool any = false; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var m = w2l * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static MeshFilter Lod0(GameObject go) => go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();

        /// the asteroid's flat side (Meshy built it from one view): the axis direction with the most triangle area facing it
        static Vector3 FlatSide(GameObject go, out string report)
        {
            var mf = Lod0(go);
            var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var v = mf.sharedMesh.vertices; var tri = mf.sharedMesh.triangles;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var area = new float[6];
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                float ar = n.magnitude;
                if (ar < 1e-9f) continue;
                n /= ar;
                for (int k = 0; k < 6; k++) if (Vector3.Dot(n, dirs[k]) > 0.97f) area[k] += ar * 0.5f;
            }
            int best = 0;
            for (int k = 1; k < 6; k++) if (area[k] > area[best]) best = k;
            report = string.Join(" ", new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" }.Select((s, k) => $"{s}:{area[k]:F0}"));
            return dirs[best];
        }

        static float Along(Bounds b, Vector3 axis) => Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))));

        static Ev.Piece Add(Ev ev, Transform t, Ev.Kind kind)
        {
            var p = new Ev.Piece { t = t, kind = kind };
            ev.pieces.Add(p);
            return p;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float s = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * s);
        }

        /// keep burst fragments out of the places the player needs: the dash lane, the path, the enemy line
        static bool Blocked(Ev ev, Vector3 p, float r)
        {
            if (SegDist(p, ev.laneStart - (ev.laneEnd - ev.laneStart).normalized * 10f, ev.laneEnd + (ev.laneEnd - ev.laneStart).normalized * 25f) < ev.laneRadius + r + 10f) return true;
            if (p.z < 105f) return true;
            return false;
        }

        // ------------------------------------------------------------------ the pieces
        static void Asteroids(Ev ev, Transform g, out float halfA, out float halfB)
        {
            Vector3 C = ev.collisionPoint;
            (string name, float scale, float side, Vector3 start, Vector3 bow, float spin)[] rocks =
            {
                ("ast_metal", 2.3f, -1f, new Vector3(-150f, 30f, 192f), new Vector3(0f, 7f, 0f), 6f),
                ("ast_ice",   2.1f,  1f, new Vector3( 150f, 12f, 160f), new Vector3(0f, -5f, 4f), -5f),
            };
            var half = new float[2];
            for (int i = 0; i < 2; i++)
            {
                var r = rocks[i];
                var go = Put(r.name, g, r.scale);
                go.name = $"{r.name} (collides from the {(r.side < 0 ? "left" : "right")})";
                Vector3 flat; string rep;
                try { flat = FlatSide(go, out rep); }
                catch (System.Exception e) { flat = Vector3.down; rep = "detection failed (" + e.Message + "), assuming -Y"; }
                var b = LocalBounds(go);
                half[i] = Along(b, flat) * r.scale;
                Vector3 toOther = new Vector3(-r.side, 0f, 0f);                // the flat side faces the other rock
                var p = Add(ev, go.transform, Ev.Kind.Asteroid);
                p.r0 = Quaternion.AngleAxis(R(0f, 360f), toOther) * Quaternion.FromToRotation(flat, toOther);
                p.spinAxis = toOther; p.spin = r.spin; p.phase = 0f;
                p.p0 = r.start; p.ctrl = r.bow;
                p.t0 = ev.approachFrom; p.dur = ev.impactTime - ev.approachFrom;
                p.radius = b.extents.magnitude * r.scale;
                Debug.Log($"[OYM-EV] {r.name}: flat side {flat} ({rep}), half-depth {half[i]:F1} m, size {b.size * r.scale}");
            }
            halfA = half[0]; halfB = half[1];
            // contact: the two flat faces meet just past the collision point (the flash hides the overlap)
            ev.pieces[ev.pieces.Count - 2].p1 = C + Vector3.left * (halfA * 0.8f);
            ev.pieces[ev.pieces.Count - 1].p1 = C + Vector3.right * (halfB * 0.8f);
            foreach (var p in ev.pieces) { p.t.localPosition = p.p0; p.t.localRotation = p.r0; }     // parked at the screen edges
        }

        static void Fragments(Ev ev, Transform g, float halfA, float halfB)
        {
            Vector3 C = ev.collisionPoint;
            int n = 0;
            // big chunks: the split-rock pieces, blown up to 11 - 15 m (their thin axis thickened 2.5x)
            for (int set = 0; set < 2; set++)
            {
                float side = set == 0 ? -1f : 1f;
                var shell = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("rock_split_pieces"), g);
                PrefabUtility.UnpackPrefabInstance(shell, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var spin0 = RandRot();
                var holder = shell.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pieces") ?? shell.transform;
                var pieces = holder.Cast<Transform>().ToList();
                if (pieces.Count == 0) pieces = shell.GetComponentsInChildren<MeshFilter>(true).Select(f => f.transform).ToList();
                foreach (var pc in pieces)
                {
                    Vector3 off = g.InverseTransformVector(pc.position - shell.transform.position);
                    pc.SetParent(g, true);
                    float s = R(20f, 24f);
                    var b = LocalBounds(pc.gameObject);
                    Vector3 sz = b.size; int thin = sz.x < sz.y ? (sz.x < sz.z ? 0 : 2) : (sz.y < sz.z ? 1 : 2);
                    var sc = Vector3.one * s; sc[thin] *= 2.5f;
                    pc.localScale = sc;
                    foreach (var c in pc.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    var p = Add(ev, pc, Ev.Kind.Fragment);
                    p.r0 = spin0 * pc.localRotation;
                    p.p0 = C + new Vector3(side * (set == 0 ? halfA : halfB) * 0.45f, 0f, 0f) + spin0 * off * s;
                    p.radius = b.extents.magnitude * s;
                    Vector3 dir = default, p1 = default;
                    for (int k = 0; k < 40; k++)
                    {
                        float az = R(0f, Mathf.PI * 2f);
                        dir = new Vector3(side * R(0.25f, 0.6f), Mathf.Cos(az), Mathf.Sin(az)).normalized;
                        p1 = p.p0 + dir * R(35f, 75f);
                        if (!Blocked(ev, p1, p.radius)) break;
                    }
                    p.p1 = p1; p.dur = R(0.9f, 1.5f); p.overshoot = R(0.05f, 0.09f);
                    p.spinAxis = RandDir(); p.spinBurst = R(60f, 160f) * Sign(); p.spin = R(2f, 6f) * Sign();
                    p.drift = dir * R(0.3f, 1.0f); p.glow = R(2.5f, 4f);
                    pc.localPosition = p.p0; pc.localRotation = p.r0;
                    pc.gameObject.SetActive(false);
                    n++;
                }
                Object.DestroyImmediate(shell);
            }
            // medium and small rocks
            string[] rocks = { "rock_intact", "rock_cracked", "rock_split" };
            for (int i = 0; i < 58; i++)
            {
                bool med = i < 22;
                float s = med ? R(4f, 9f) : R(1.2f, 3f);
                var go = Put(rocks[_rnd.Next(rocks.Length)], g, s);
                var p = Add(ev, go.transform, Ev.Kind.Fragment);
                p.radius = LocalBounds(go).extents.magnitude * s;
                p.p0 = C + new Vector3(R(-12f, 12f), R(-9f, 9f), R(-9f, 9f));
                Vector3 dir = default, p1 = default;
                for (int k = 0; k < 40; k++)
                {
                    float az = R(0f, Mathf.PI * 2f);
                    dir = new Vector3(R(-0.5f, 0.5f), Mathf.Cos(az), Mathf.Sin(az)).normalized;
                    p1 = p.p0 + dir * (med ? R(30f, 110f) : R(40f, 150f));
                    if (!Blocked(ev, p1, p.radius)) break;
                }
                p.p1 = p1; p.dur = med ? R(0.6f, 1.3f) : R(0.5f, 1.1f); p.overshoot = R(0.04f, 0.1f);
                p.r0 = RandRot(); p.spinAxis = RandDir(); p.spinBurst = R(90f, 400f) * Sign(); p.spin = R(4f, 30f) * Sign();
                p.drift = dir * R(0.5f, 2.5f); p.glow = med && i % 2 == 0 ? R(1f, 2f) : 0f;
                go.transform.localPosition = p.p0; go.transform.localRotation = p.r0;
                go.SetActive(false);
                n++;
            }
            Debug.Log($"[OYM-EV] fragments: {n}");
        }

        static void Enemies(Ev ev, Transform g)
        {
            Vector3 C = ev.collisionPoint;
            for (int i = 0; i < 10; i++)
            {
                string name = i % 3 == 2 ? "wick_open" : "wick_body";
                var go = Put(name, g, 1f);
                go.name = $"WICK_{i:00} ({name})";
                var p = Add(ev, go.transform, Ev.Kind.Enemy);
                p.p1 = new Vector3(Mathf.Lerp(-30f, 10f, i / 9f) + R(-2f, 2f), 5f + 7f * Mathf.Sin(i * 1.3f) + R(-1.5f, 1.5f), R(34f, 48f));
                p.p0 = C + new Vector3(R(-30f, 15f), R(-12f, 12f), R(18f, 32f));
                p.ctrl = Vector3.Lerp(p.p0, p.p1, 0.45f) + new Vector3(R(-30f, 30f), R(6f, 22f), R(-10f, 10f));
                p.t0 = ev.enemiesFrom + i * 0.16f + R(0f, 0.05f);
                p.dur = R(1.0f, 1.4f); p.overshoot = R(0.1f, 0.14f);
                p.bob = R(0.25f, 0.5f); p.phase = R(0f, 6.28f); p.radius = 0.5f;
                go.transform.localPosition = p.p0;
                go.SetActive(false);
            }
            {
                var go = Put("prism_body", g, 1f);
                go.name = "PRISM_00 (rises behind the big chunk)";
                var p = Add(ev, go.transform, Ev.Kind.Enemy);
                p.p0 = C + new Vector3(14f, -32f, 26f);
                p.ctrl = C + new Vector3(24f, 8f, -10f);
                p.p1 = new Vector3(-6f, 12f, 58f);
                p.t0 = ev.enemiesFrom + 0.9f; p.dur = 2.0f; p.overshoot = 0.06f;
                p.bob = 0.4f; p.phase = 1.3f; p.radius = 1.75f;
                go.transform.localPosition = p.p0;
                go.SetActive(false);
            }
        }

        static void WreckPath(Ev ev, Transform g)
        {
            Vector3 C = ev.collisionPoint;
            Vector3 dir = (ev.laneEnd - ev.laneStart).normalized;
            Vector3 a = new Vector3(4f, -0.5f, 26f), c = new Vector3(24f, 1f, 44f), b = ev.laneStart - dir * 9f + Vector3.down * 1.5f;
            // arc-length samples of the path curve
            var pts = new List<Vector3>(); var len = new List<float>();
            for (int i = 0; i <= 200; i++)
            {
                float s = i / 200f, u = 1f - s;
                pts.Add(u * u * a + 2f * u * s * c + s * s * b);
                len.Add(i == 0 ? 0f : len[i - 1] + Vector3.Distance(pts[i], pts[i - 1]));
            }
            float total = len[len.Count - 1];
            (Vector3 p, Vector3 tan) At(float l)
            {
                int i = Mathf.Clamp(len.FindIndex(x => x >= l), 1, pts.Count - 1);
                return (pts[i], (pts[i] - pts[i - 1]).normalized);
            }
            (string name, float scale)[] kit =
            {
                ("wreck_hull_large", 1.1f), ("wreck_hull_panel", 1.8f), ("wreck_hull_large", 1.0f), ("wreck_truss_long", 0.9f),
                ("wreck_hull_panel", 1.8f), ("wreck_hull_large", 1.1f), ("wreck_hull_rib", 2.4f), ("wreck_hull_panel", 1.8f),
                ("wreck_hull_large", 1.0f), ("wreck_solar_wing", 0.55f),
            };
            float ready = 0f;
            for (int i = 0; i < kit.Length; i++)
            {
                bool gate = i == kit.Length - 1;
                var (pos, tan) = gate ? (ev.laneStart - dir * 4f + Vector3.down * 1.6f, dir) : At((i + 0.5f) / (kit.Length - 1) * total);
                var go = Put(kit[i].name, g, kit[i].scale, keepColliders: true);
                go.name = $"PathPiece_{i:00} ({kit[i].name})" + (gate ? " - lane gate platform" : "");
                var bnd = LocalBounds(go);
                Vector3 sz = bnd.size;
                int thin = sz.x < sz.y ? (sz.x < sz.z ? 0 : 2) : (sz.y < sz.z ? 1 : 2);
                int lng = sz.x > sz.y ? (sz.x > sz.z ? 0 : 2) : (sz.y > sz.z ? 1 : 2);
                if (lng == thin) lng = (thin + 1) % 3;
                Vector3 thinV = Vector3.zero, longV = Vector3.zero; thinV[thin] = 1f; longV[lng] = 1f;
                Vector3 flatTan = Vector3.ProjectOnPlane(tan, Vector3.up).normalized;
                if (flatTan.sqrMagnitude < 1e-4f) flatTan = Vector3.forward;
                var slot = Quaternion.LookRotation(flatTan, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(longV, thinV));
                var p = Add(ev, go.transform, Ev.Kind.PathPiece);
                p.r1 = Quaternion.AngleAxis(R(-6f, 6f), flatTan) * Quaternion.AngleAxis(R(-4f, 4f), Vector3.Cross(Vector3.up, flatTan)) * slot;
                p.p1 = pos - p.r1 * (bnd.center * kit[i].scale);                 // centre the slab on the path line
                p.radius = sz[thin] * 0.5f * kit[i].scale + 0.35f;                // guide light just above the deck
                p.p0 = C + new Vector3(R(-40f, 40f), R(-15f, 15f), R(-25f, 10f));
                p.ctrl = Vector3.Lerp(p.p0, p.p1, 0.5f) + new Vector3(R(-20f, 20f), R(10f, 30f), R(-10f, 10f));
                p.r0 = RandRot();
                p.t0 = ev.pathFrom + i * 0.28f; p.dur = R(1.5f, 1.9f); p.overshoot = 0.07f;
                p.bob = 0.15f; p.phase = R(0f, 6.28f);
                ready = Mathf.Max(ready, p.t0 + p.dur);
                go.transform.localPosition = p.p0; go.transform.localRotation = p.r0;
                go.SetActive(false);
            }
            ev.pathReadyTime = ready;
            Debug.Log($"[OYM-EV] wreck path: {kit.Length} pieces over {total:F0} m, ready at {ready:F2}s");
        }

        static void LaneDebris(Ev ev, Transform g)
        {
            const int Center = 6;
            Vector2 Slot(int k)
            {
                if (k == Center) return Vector2.zero;
                float a = (90f - 60f * k) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.4f;
            }
            // waves: occupied slots (0 = top, clockwise; 6 = centre), big = a 2.6 m rock through the centre
            (int[] slots, bool big)[] waves =
            {
                (new int[0], true),
                (new[] { 4, 5, 0, Center }, false),
                (new[] { 1, 2, 3, Center }, false),
                (new[] { 0, 1, 2, 3, 4, 5 }, false),
                (new[] { 5, 0, 1, Center }, false),
                (new[] { 2, 3, 4, Center }, false),
                (new[] { 0, 3 }, true),
                (new[] { 0, 1, 2, 3, 4, 5 }, false),
                (new[] { 4, 5, 0, Center }, false),
                (new[] { 1, 4 }, true),
            };
            ev.waveCount = waves.Length;
            string[] junk = { "debris_panel", "debris_tank", "debris_pipe_elbow", "debris_junction_box", "debris_cable_coil", "debris_bracket", "debris_vent_grate", "wreck_hull_rib", "rock_cracked" };
            int n = 0;
            for (int w = 0; w < waves.Length; w++)
            {
                var list = waves[w].slots.Select(k => (slot: Slot(k), r: R(0.9f, 1.3f), name: junk[_rnd.Next(junk.Length)])).ToList();
                if (waves[w].big) list.Add((Vector2.zero, 2.6f, "rock_cracked"));
                foreach (var it in list)
                {
                    var probe = Put(it.name, g, 1f);
                    var nb = LocalBounds(probe).size;
                    float nominal = Mathf.Max(nb.x, nb.y, nb.z);
                    float s = 2f * it.r / Mathf.Max(nominal, 0.05f);
                    probe.transform.localScale *= s;
                    probe.name = $"LaneDebris_w{w}_{n:00} ({it.name})";
                    var p = Add(ev, probe.transform, Ev.Kind.LaneDebris);
                    p.t0 = w * ev.wavePeriod;
                    p.slot = it.slot + new Vector2(R(-0.3f, 0.3f), R(-0.3f, 0.3f));
                    p.radius = it.r;
                    p.r0 = RandRot(); p.spinAxis = RandDir(); p.spin = R(40f, 160f) * Sign();
                    probe.transform.localPosition = ev.laneEnd + Vector3.up * 2000f;
                    probe.SetActive(false);
                    n++;
                }
            }
            Debug.Log($"[OYM-EV] dash lane: {waves.Length} waves, {n} debris");
        }

        /// arena rocks / drifting props inside the event volume would block the show: hide them in this copy only
        static void ClearZone(Transform rig)
        {
            int hidden = 0;
            foreach (var go in SceneManagerRoots())
            {
                if (go.GetComponentInChildren<Ev>(true) || go.GetComponentInChildren<Animator>(true) || go.GetComponent<Camera>() || go.name.StartsWith("Debris Path")) continue;
                if (go.GetComponent<Light>() || go.name.StartsWith("Global Volume") || go.name.StartsWith("GiantPlanet")) continue;
                foreach (Transform child in go.transform)
                {
                    var rs = child.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    var c = rig.InverseTransformPoint(b.center);
                    float rad = b.extents.magnitude * 0.6f;                     // sphere vs the event's core box
                    var q = new Vector3(Mathf.Clamp(c.x, -200f, 200f), Mathf.Clamp(c.y, -60f, 120f), Mathf.Clamp(c.z, 8f, 300f));
                    if ((q - c).magnitude < rad && child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(false); hidden++;
                        Debug.Log($"[OYM-EV]   hid {go.name}/{child.name} (r {rad:F0} m at {c})");
                    }
                }
            }
            Debug.Log($"[OYM-EV] cleared {hidden} arena objects from the event volume");
        }

        static IEnumerable<GameObject> SceneManagerRoots() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        /// the characters, camera and spawn marker turn to the event (the showcase faces the wreck, +Z)
        static void FaceEvent()
        {
            foreach (var go in SceneManagerRoots())
            {
                bool character = go.GetComponentInChildren<Animator>(true) != null && go.GetComponentInChildren<OYM.ZeroGFlight>(true) != null;
                if (character || go.GetComponent<Camera>() || go.name == "PlayerSpawn")
                {
                    go.transform.SetPositionAndRotation(RigRot * go.transform.position, RigRot * go.transform.rotation);
                    Debug.Log($"[OYM-EV] turned {go.name} to face the event");
                }
            }
        }

        // ------------------------------------------------------------------ capture
        struct Shot { public string name; public float t; public Vector3 eye, look; public float fov; }

        [MenuItem("OYM/Event/Capture Stills (Asteroid Collision)")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var ev = Object.FindFirstObjectByType<Ev>(FindObjectsInactive.Include);
            if (!ev) throw new System.Exception("no CollisionEvent in " + ScenePath);
            ev.Prepare();
            var cam = Camera.main;
            if (!cam) throw new System.Exception("no main camera");
            var graphs = PoseCharacters();
            var T = ev.transform;
            float I = ev.impactTime;
            Vector3 C = ev.collisionPoint, S = ev.laneStart, dir = (ev.laneEnd - ev.laneStart).normalized;
            Vector3 U = Vector3.Cross(Vector3.up, dir).normalized, V = Vector3.Cross(dir, U);
            float laneT = ev.laneAutoTime + 4.2f;
            var shots = new List<Shot>
            {
                new Shot { name = "e01_zone_trigger", t = -1f, eye = new Vector3(1.4f, 3.4f, -5.5f), look = new Vector3(0f, 14f, 120f), fov = 60f },
                new Shot { name = "e02_approach", t = 2.4f, eye = new Vector3(1f, 3f, 4f), look = C + new Vector3(0f, -6f, 0f), fov = 60f },
                new Shot { name = "e03_contact", t = I - 0.06f, eye = new Vector3(1f, 3f, 4f), look = C, fov = 50f },
                new Shot { name = "e04_impact_flash", t = I + 0.03f, eye = new Vector3(1f, 3f, 4f), look = C, fov = 50f },
                new Shot { name = "e04b_flash_decay", t = I + 0.12f, eye = new Vector3(1f, 3f, 4f), look = C, fov = 55f },
                new Shot { name = "e05_burst", t = I + 0.3f, eye = new Vector3(1f, 3f, 4f), look = C, fov = 60f },
                new Shot { name = "e06_snap_hold", t = I + 1.7f, eye = new Vector3(1f, 3f, 4f), look = C, fov = 60f },
                new Shot { name = "e07_husk_emerge", t = ev.enemiesFrom + 0.75f, eye = new Vector3(-3f, 5f, 16f), look = new Vector3(-5f, 14f, 110f), fov = 55f },
                new Shot { name = "e08_husk_formation", t = ev.enemiesFrom + 3.4f, eye = new Vector3(4f, 3f, 12f), look = new Vector3(-8f, 8f, 42f), fov = 58f },
                new Shot { name = "e09_path_forming", t = ev.pathFrom + 1.6f, eye = new Vector3(-6f, 7f, 10f), look = new Vector3(22f, 3f, 52f), fov = 60f },
                new Shot { name = "e10_path_lane_ready", t = ev.pathReadyTime + 0.9f, eye = new Vector3(-45f, 32f, 10f), look = new Vector3(28f, 6f, 80f), fov = 55f },
                new Shot { name = "e11_dash_lane", t = laneT, eye = S + dir * 6f + V * 1.0f - U * 0.6f, look = S + dir * 90f, fov = 62f },
                new Shot { name = "e12_overview", t = laneT, eye = new Vector3(-85f, 45f, 15f), look = new Vector3(20f, 8f, 110f), fov = 60f },
            };
            Directory.CreateDirectory(ShotDir);
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            float Lane(float t) => t >= ev.laneAutoTime ? t - ev.laneAutoTime : -1f;
            void Frame(Shot s, float t)
            {
                cam.fieldOfView = s.fov;
                Vector3 e = T.TransformPoint(s.eye), l = T.TransformPoint(s.look);
                cam.transform.SetPositionAndRotation(e, Quaternion.LookRotation(l - e, Vector3.up));
                ev.Evaluate(t, Lane(t), cam);
                foreach (var g in graphs) g.Evaluate(0f);
                cam.Render();
            }
            // warm-up: shaders, SSGI / TAA / volumetric history
            for (int i = 0; i < 600; i++)
            {
                Frame(shots[0], -1f);
                if (i >= 90 && !ShaderUtil.anythingCompiling) break;
                if (ShaderUtil.anythingCompiling) System.Threading.Thread.Sleep(50);
            }
            foreach (var s in shots)
            {
                for (int i = 12; i >= 1; i--) Frame(s, s.t - i / 30f);              // the real preceding frames: TAA / motion blur history
                Frame(s, s.t);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(ShotDir, s.name + ".png"), tex.EncodeToPNG());
                Debug.Log($"[OYM-EV] captured {s.name} (t={s.t:F2})");
            }
            cam.targetTexture = null; rt.Release();
            foreach (var g in graphs) g.Destroy();
            ev.Evaluate(-1f, -1f, cam);
            ev.Teardown();
            Debug.Log("[OYM-EV] stills -> " + ShotDir);
        }

        /// edit-mode stills: every character posed on an idle clip of its own controller (as the showcase does)
        static List<PlayableGraph> PoseCharacters()
        {
            var graphs = new List<PlayableGraph>();
            foreach (var anim in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                var ctl = anim.runtimeAnimatorController;
                if (ctl == null) continue;
                var clip = ctl.animationClips.FirstOrDefault(c => c.name == "CombatIdle") ?? ctl.animationClips.FirstOrDefault(c => c.name.Contains("Idle")) ?? ctl.animationClips.FirstOrDefault();
                if (clip == null) continue;
                var g = PlayableGraph.Create("event-" + anim.name);
                var cp = AnimationClipPlayable.Create(g, clip);
                cp.SetApplyFootIK(true);
                AnimationPlayableOutput.Create(g, "out", anim).SetSourcePlayable(cp);
                cp.SetTime(0.6f); cp.SetTime(0.6f);
                g.Evaluate(0f);
                graphs.Add(g);
            }
            return graphs;
        }
    }
}
