using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// "Shape only" optimisation that rides on the arena fog (DepthFocusFade.fog): far debris is reduced to a soft
    /// shape by the fog, so its detail does not need drawing. Every environment object (renderers on the Obstacle /
    /// Environment layer; LOD groups as one object) is kept in one of three states:
    ///   normal       automatic LOD selection
    ///   silhouette   held at its lowest LOD                      fog opacity >= DepthFocusFade.cullSilhouetteOpacity
    ///   faded out    not drawn into the frame (still casts its   on-screen radius x (1 - fog opacity) < cullPixels
    ///                shadow, so nothing pops on near surfaces)
    /// The opacity is the same curve the fog shader draws (DepthFocusFade.FogOpacity), so an object only fades out
    /// once it is a faint speck. Objects are re-evaluated a slice per frame (each about every 6 frames) against the main
    /// camera, with hysteresis so nothing flickers at a threshold.
    ///
    /// Installs itself in play mode on every scene load (no scene edits). -oymNoFogCull = off, F7 = toggle (A/B).
    /// Editor tools can drive it in edit mode through Rescan / EvaluateNow / RestoreAll.
    [DefaultExecutionOrder(900)]
    public sealed class FogCulling : MonoBehaviour
    {
        public const string ObjectName = "OYM Fog Culling";
        const int SliceDivisor = 6, MinSlice = 96;
        const float UncullFactor = 1.3f, SilhouetteHysteresis = 0.04f;

        public enum State : byte { Normal, Silhouette, FadedOut }

        sealed class Item
        {
            public Transform tr;
            public Vector3 localCenter;
            public float localRadius;
            public LODGroup lod;
            public int lowest;
            public float cullHeight;              // LODGroup culls itself below this screen-relative height (0 = never)
            public Renderer[] renderers;
            public ShadowCastingMode[] shadowModes;
            public State state;
        }

        readonly List<Item> m_Items = new List<Item>();
        readonly Dictionary<Renderer, Item> m_ByRenderer = new Dictionary<Renderer, Item>();
        int m_Cursor;
        bool m_Active = true, m_RescanPending;
        DepthFocusFade m_Override;                // edit-mode tools pass the settings directly

        public int Tracked => m_Items.Count;
        public int Count(State s) { int n = 0; foreach (var it in m_Items) if (it.state == s) n++; return n; }

        /// State of the object a renderer belongs to (Normal for renderers this does not track).
        public State StateOf(Renderer r) => r != null && m_ByRenderer.TryGetValue(r, out var it) ? it.state : State.Normal;

        /// Lowest LOD index of a tracked LOD group's object (-1 when the renderer is not in a tracked LOD group).
        public int LowestLodOf(Renderer r) => r != null && m_ByRenderer.TryGetValue(r, out var it) && it.lod != null ? it.lowest : -1;

        // ------------------------------------------------------------------------------------------- install ----
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymNoFogCull") >= 0) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Install();

        static void Install()
        {
            var existing = FindAnyObjectByType<FogCulling>();
            if (existing != null) { existing.m_RescanPending = true; return; }   // additive load: pick up the new scene
            var go = new GameObject(ObjectName);        // lives in the active scene: gone with it, rescanned with the next
            go.AddComponent<FogCulling>();
        }

        // Start runs a frame after the load, after every sceneLoaded hook (WreckLods adds its LOD groups there)
        void Start() => Rescan();

        void OnDisable() => RestoreAll();

        // ------------------------------------------------------------------------------------------- scanning ----
        public static int EnvironmentMask()
        {
            int mask = 0;
            foreach (var n in new[] { "Obstacle", "Environment" })
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0) mask |= 1 << l;
            }
            return mask;
        }

        /// Collects the environment objects of every loaded scene.
        public void Rescan()
        {
            RestoreAll();
            m_Items.Clear();
            m_ByRenderer.Clear();
            m_Cursor = 0;
            int mask = EnvironmentMask();
            if (mask == 0) return;
            var inGroup = new HashSet<Renderer>();
            var groups = new List<LODGroup>();
            var renderers = new List<MeshRenderer>();
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, groups);
                    foreach (var g in groups)
                    {
                        if ((mask & (1 << g.gameObject.layer)) == 0) continue;
                        var lods = g.GetLODs();
                        if (lods.Length == 0) continue;
                        var all = new List<Renderer>();
                        foreach (var l in lods) foreach (var r in l.renderers) if (r != null && !all.Contains(r)) all.Add(r);
                        if (all.Count == 0 || !AllOpaque(all)) continue;
                        foreach (var r in all) inGroup.Add(r);
                        int lowest = lods.Length - 1;
                        m_Items.Add(NewItem(g.transform, g.localReferencePoint, g.size * 0.5f, all, g, lowest,
                            lods[lowest].screenRelativeTransitionHeight));
                    }
                    root.GetComponentsInChildren(true, renderers);
                    foreach (var r in renderers)
                    {
                        if (inGroup.Contains(r) || (mask & (1 << r.gameObject.layer)) == 0) continue;
                        if (!AllOpaque(new List<Renderer> { r })) continue;
                        var b = r.localBounds;
                        m_Items.Add(NewItem(r.transform, b.center, b.extents.magnitude, new List<Renderer> { r }, null, 0, 0f));
                    }
                }
            }
            foreach (var it in m_Items) foreach (var r in it.renderers) m_ByRenderer[r] = it;
        }

        static Item NewItem(Transform tr, Vector3 center, float radius, List<Renderer> rs, LODGroup lod, int lowest, float cullHeight)
        {
            var modes = new ShadowCastingMode[rs.Count];
            for (int i = 0; i < rs.Count; i++) modes[i] = rs[i].shadowCastingMode;
            return new Item { tr = tr, localCenter = center, localRadius = radius, lod = lod, lowest = lowest,
                cullHeight = cullHeight, renderers = rs.ToArray(), shadowModes = modes, state = State.Normal };
        }

        static bool AllOpaque(List<Renderer> rs)
        {
            foreach (var r in rs)
            {
                if (r is SkinnedMeshRenderer || r is ParticleSystemRenderer) return false;
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.renderQueue > (int)RenderQueue.GeometryLast) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------- update ----
        void Update()
        {
            if (!Application.isPlaying) return;
            if (m_RescanPending) { m_RescanPending = false; Rescan(); }
            if (ToggleKey())
            {
                m_Active = !m_Active;
                if (!m_Active) RestoreAll();
                Debug.Log("[OYM-FOG] fog culling " + (m_Active ? "ON" : "OFF"));
            }
            if (!m_Active || m_Items.Count == 0) return;
            var cam = Camera.main;
            var s = Settings(cam);
            if (cam == null || s == null) return;
            if (!s.fogCulling.value || s.FogOpacity(1e6f) <= 0f) { RestoreAll(); return; }
            int n = Mathf.Min(m_Items.Count, Mathf.Max(MinSlice, m_Items.Count / SliceDivisor));
            var view = View.From(cam);
            for (int i = 0; i < n; i++)
            {
                if (m_Cursor >= m_Items.Count) m_Cursor = 0;
                Evaluate(m_Items[m_Cursor++], s, view);
            }
        }

        static bool ToggleKey()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k.f7Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F7);
#endif
        }

        DepthFocusFade Settings(Camera cam)
        {
            if (m_Override != null) return m_Override;
            // HDCamera.volumeStack creates the stack lazily, which throws while the VolumeManager is down (play-mode
            // exit, pipeline rebuilds)
            if (cam == null || !VolumeManager.instance.isInitialized) return null;
            try
            {
                var hd = HDCamera.GetOrCreate(cam);
                return hd != null && hd.volumeStack != null ? hd.volumeStack.GetComponent<DepthFocusFade>() : null;
            }
            catch (System.InvalidOperationException) { return null; }
        }

        /// Edit-mode tools: evaluates every object once for this camera and these settings.
        public void EvaluateNow(Camera cam, DepthFocusFade settings)
        {
            if (cam == null || settings == null) return;
            var view = View.From(cam);
            foreach (var it in m_Items) Evaluate(it, settings, view);
        }

        struct View
        {
            public Vector3 pos;
            public float pxPerRadian;      // on-screen pixels per unit of (radius / distance)
            public float heightPerUnit;    // LOD screen-relative height per unit of (size / distance)
            public static View From(Camera cam)
            {
                float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                return new View
                {
                    pos = cam.transform.position,
                    pxPerRadian = cam.pixelHeight * 0.5f / tanHalf,
                    heightPerUnit = QualitySettings.lodBias / (2f * tanHalf),
                };
            }
        }

        static void Evaluate(Item it, DepthFocusFade s, in View v)
        {
            if (it.tr == null) return;
            Vector3 c = it.tr.TransformPoint(it.localCenter);
            Vector3 ls = it.tr.lossyScale;
            float radius = it.localRadius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            float dist = Vector3.Distance(v.pos, c);
            State next = State.Normal;
            if (dist < s.skyLayerDistance.value)                                  // the planet backdrop keeps its look
            {
                float near = Mathf.Max(0f, dist - radius);                        // closest surface: conservative
                float fog = s.FogOpacity(near);
                float eff = radius / Mathf.Max(dist, 0.01f) * v.pxPerRadian * (1f - fog);
                float cullPx = s.cullPixels.value * (it.state == State.FadedOut ? UncullFactor : 1f);
                float silFrom = s.cullSilhouetteOpacity.value - (it.state == State.Silhouette ? SilhouetteHysteresis : 0f);
                if (eff < cullPx)
                    next = State.FadedOut;
                else if (it.lod != null && it.lowest > 0 && fog >= silFrom
                         && 2f * radius / Mathf.Max(dist, 0.01f) * v.heightPerUnit >= it.cullHeight)   // not self-culled
                    next = State.Silhouette;
            }
            if (next != it.state) Apply(it, next);
        }

        static void Apply(Item it, State next)
        {
            if (it.lod != null) it.lod.ForceLOD(next == State.Silhouette ? it.lowest : -1);
            bool fade = next == State.FadedOut;
            for (int i = 0; i < it.renderers.Length; i++)
            {
                var r = it.renderers[i];
                if (r == null) continue;
                var keep = it.shadowModes[i];
                if (fade)
                {
                    // out of the frame, but a shadow it throws on near surfaces must not pop
                    if (keep == ShadowCastingMode.Off) r.forceRenderingOff = true;
                    else r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                else
                {
                    r.forceRenderingOff = false;
                    r.shadowCastingMode = keep;
                }
            }
            it.state = next;
        }

        /// Puts every object back as authored.
        public void RestoreAll()
        {
            foreach (var it in m_Items)
                if (it.state != State.Normal) Apply(it, State.Normal);
        }

        /// Edit-mode tools: use these settings instead of the camera's volume stack.
        public void UseSettings(DepthFocusFade s) => m_Override = s;
    }
}
