using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Gives the wreck fragments LODs when a scene loads (no scene edits).
    ///
    /// The 799 hull fragments had no LODs: 8.2 M triangles drawn in the G-buffer, motion-vector and every shadow
    /// pass (the biggest GPU cost left in the title and the debris field). For each MeshFilter whose mesh has an
    /// entry in Resources/OYM_WreckLods, this adds a LODGroup with two child renderers (25 % and 6 % triangles,
    /// same materials and render settings). Colliders stay on the original object.
    public static class WreckLods
    {
        // screen-relative heights: LOD0 above, LOD1 above, LOD2 for everything smaller (never culled here;
        // the GPU Resident Drawer's small-mesh culling still drops sub-pixel pieces)
        public const float Lod0Height = 0.3f, Lod1Height = 0.08f;

        static Dictionary<string, WreckLodTable.Entry> s_Map;
        static bool s_Loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Map = null; s_Loaded = false; SceneManager.sceneLoaded -= OnSceneLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymNoWreckLods") >= 0) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) Apply(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply(s);

        static bool LoadTable()
        {
            if (s_Loaded) return s_Map != null;
            s_Loaded = true;
            var table = Resources.Load<WreckLodTable>("OYM_WreckLods");
            if (table == null || table.entries == null) return false;
            s_Map = new Dictionary<string, WreckLodTable.Entry>(table.entries.Length);
            foreach (var e in table.entries)
                if (!string.IsNullOrEmpty(e.name) && e.lod1 != null && e.lod2 != null) s_Map[e.name] = e;
            return true;
        }

        static void Apply(Scene scene)
        {
            if (!scene.isLoaded || !LoadTable()) return;
            int added = 0;
            var filters = new List<MeshFilter>();
            foreach (var root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, filters);
                foreach (var mf in filters)
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null || !s_Map.TryGetValue(mesh.name, out var e)) continue;
                    if (!mf.TryGetComponent(out MeshRenderer mr) || mf.GetComponentInParent<LODGroup>(true) != null) continue;
                    var r1 = MakeLod(mf.gameObject, mr, e.lod1, "_LOD1");
                    var r2 = MakeLod(mf.gameObject, mr, e.lod2, "_LOD2");
                    var lg = mf.gameObject.AddComponent<LODGroup>();
                    lg.fadeMode = LODFadeMode.None;
                    lg.SetLODs(new[]
                    {
                        new LOD(Lod0Height, new Renderer[] { mr }),
                        new LOD(Lod1Height, new Renderer[] { r1 }),
                        new LOD(0f, new Renderer[] { r2 }),
                    });
                    lg.RecalculateBounds();
                    added++;
                }
            }
            if (added > 0) Debug.Log($"[WreckLods] {scene.name}: LODs on {added} wreck fragments");
        }

        static MeshRenderer MakeLod(GameObject owner, MeshRenderer src, Mesh mesh, string suffix)
        {
            var go = new GameObject(owner.name + suffix) { layer = owner.layer };
            go.transform.SetParent(owner.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = src.sharedMaterials;
            r.shadowCastingMode = src.shadowCastingMode;
            r.receiveShadows = src.receiveShadows;
            r.motionVectorGenerationMode = src.motionVectorGenerationMode;
            r.renderingLayerMask = src.renderingLayerMask;
            r.lightProbeUsage = src.lightProbeUsage;
            r.reflectionProbeUsage = src.reflectionProbeUsage;
            r.staticShadowCaster = src.staticShadowCaster;
            r.allowOcclusionWhenDynamic = src.allowOcclusionWhenDynamic;
            r.enabled = src.enabled;
            return r;
        }
    }
}
