using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Particle cost control. Installs itself when the game starts (no scene or prefab edits), like WreckLods.
    ///
    /// SpaceDust (the camera dust of the title and every flight scene, OYMSetup.AddSpaceDust) keeps ~1,440 mesh
    /// particles alive, each drawn with the asteroid mesh rock_00_lod2 (2,492 triangles): ~3.6 M triangles. HDRP has
    /// no particle GPU instancing, so Unity re-bakes all of them into one dynamic vertex buffer every frame
    /// (~1.8 M vertices, on the job threads + upload) and the GPU draws them in the depth prepass, the G-buffer and the
    /// object motion-vector pass. The specks are 0.09-0.78 m across and almost always a few pixels, so they get an
    /// 80-triangle copy of the same rock (ParticleBudgetMesh.cs: an icosphere shrink-wrapped onto rock_00_lod2 by
    /// pipeline/particles/make_dust_mesh.py, same silhouette and size, so startSize keeps its meaning) and
    /// camera-only motion vectors (the specks stand still in the world; their motion is the camera's).
    ///
    /// Cameras created later (co-op, cutscenes) are checked on their first render. -oymNoParticleBudget (or the
    /// environment variable OYM_NO_PARTICLE_BUDGET=1) turns all of this off for A/B benchmarks.
    public static partial class ParticleBudget
    {
        public const string DustName = "SpaceDust";
        /// rock-mesh particles above this many triangles get the low-poly dust rock
        const int HeavyTris = 600;

        static Mesh s_DustMesh;
        static readonly HashSet<int> s_Cams = new HashSet<int>();
        static readonly List<(ParticleSystemRenderer r, Mesh mesh, MotionVectorGenerationMode mv)> s_Patched =
            new List<(ParticleSystemRenderer, Mesh, MotionVectorGenerationMode)>();
        static bool s_Reverted;
        static readonly List<ParticleSystemRenderer> s_Tmp = new List<ParticleSystemRenderer>();
        static bool s_On;

        /// how many dust systems got the low-poly rock since the game started (logs / bench checks)
        public static int PatchedSystems { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            s_Cams.Clear();
            s_Patched.Clear();
            s_On = false;
            s_Reverted = false;
            PatchedSystems = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // off for A/B benchmarks: command line, or OYM_NO_PARTICLE_BUDGET=1 (set by an editor bench between play sessions)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymNoParticleBudget") >= 0 ||
                System.Environment.GetEnvironmentVariable("OYM_NO_PARTICLE_BUDGET") == "1")
            {
                Debug.Log("[ParticleBudget] off (A/B baseline)");
                return;
            }
            s_On = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.beginContextRendering += OnBeginContext;
            ApplyLoaded();
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            s_Cams.Clear(); // camera ids can be reused by the new scene's cameras
            ApplyLoaded();
        }

        static void ApplyLoaded()
        {
            s_Patched.RemoveAll(p => p.r == null);   // systems of unloaded scenes
            int n = 0;
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (PatchDust(r)) n++;
            if (n > 0) Debug.Log($"[ParticleBudget] {SceneManager.GetActiveScene().name}: low-poly dust on {n} system(s)");
        }

        // cameras spawned after the scene load (co-op rigs, cutscene cameras) carry their own dust
        static void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (!s_On) return;
            for (int i = 0; i < cams.Count; i++)
            {
                var c = cams[i];
                if (c == null || c.cameraType != CameraType.Game || !s_Cams.Add(c.GetInstanceID())) continue;
                c.GetComponentsInChildren(true, s_Tmp);
                foreach (var r in s_Tmp) PatchDust(r);
                s_Tmp.Clear();
            }
        }

        static int TriCount(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return (int)n;
        }

        /// Swaps a dust system's heavy rock mesh for the low-poly one (true if it changed something).
        public static bool PatchDust(ParticleSystemRenderer r)
        {
            if (r == null || r.renderMode != ParticleSystemRenderMode.Mesh) return false;
            var m = r.mesh;
            if (m == null || m == s_DustMesh) return false;
            foreach (var p in s_Patched) if (p.r == r) return false;   // reverted on purpose (SetActive(false))
            bool dust = r.name == DustName || m.name.StartsWith("rock_", System.StringComparison.Ordinal);
            if (!dust) return false;
            int tris = TriCount(m);
            if (tris <= HeavyTris) return false;
            s_Patched.Add((r, m, r.motionVectorGenerationMode));
            if (!s_Reverted)
            {
                r.mesh = DustMesh();
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }
            PatchedSystems++;
            Debug.Log($"[ParticleBudget] {r.name}: mesh {m.name} {tris} -> {TriCount(s_DustMesh)} tris, camera motion vectors");
            return true;
        }

        /// Debug / in-session A/B benchmarks: false puts the original rock meshes and motion-vector modes back on
        /// every patched dust system, true applies the low-poly rock again.
        public static void SetActive(bool on)
        {
            s_Reverted = !on;
            for (int i = s_Patched.Count - 1; i >= 0; i--)
            {
                var p = s_Patched[i];
                if (p.r == null) { s_Patched.RemoveAt(i); continue; }
                p.r.mesh = on ? DustMesh() : p.mesh;
                p.r.motionVectorGenerationMode = on ? MotionVectorGenerationMode.Camera : p.mv;
            }
        }

        public static bool Active => s_On && !s_Reverted;

        /// The 80-triangle dust rock (built once, kept CPU-readable: the particle renderer bakes it on the CPU).
        /// Smooth normals like the Surface-Nets source; spherical UVs only so the tangents are well defined (the rock
        /// material maps its textures triplanar in world space and ignores UVs).
        public static Mesh DustMesh()
        {
            if (s_DustMesh != null) return s_DustMesh;
            int nv = DustRockVerts.Length / 3;
            var v = new Vector3[nv];
            var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                var p = new Vector3(DustRockVerts[3 * i], DustRockVerts[3 * i + 1], DustRockVerts[3 * i + 2]);
                v[i] = p;
                var d = p.normalized;
                uv[i] = new Vector2(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f);
            }
            var t = new int[DustRockTris.Length];
            for (int i = 0; i < t.Length; i++) t[i] = DustRockTris[i];
            var m = new Mesh { name = "rock_dust_lp" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0, true);
            m.RecalculateNormals();
            m.RecalculateTangents();
            s_DustMesh = m;
            return m;
        }
    }
}
