using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Enemy death: the body breaks into its baked pieces and blows apart.
    ///
    ///   EnemyShatter.Detonate(enemyRoot, shotDir);                 // kill: crack + fireball + pieces
    ///   EnemyShatter.Detonate(wick, dir, 1f, withExplosion: false); // pieces only (the caller has its own blast)
    ///
    /// Timeline (local, no network state; a few ms per kill, everything pooled):
    ///   0     .. 0.075 s  CRACK  - the body holds still, swells a hair, goes white-hot (fresnel flash) and its own
    ///                              pink sensor lights surge and stutter (overload)
    ///   0.075 s           BURST  - pieces kick out (radial + along the shot), fast pop then drag ("쪼"), spin;
    ///                              HitFX explosion front + back (fireballs, smoke, sparks, light, shake);
    ///                              sensor lights die out with a flicker
    ///   0.2 .. 0.6 s      POPS   - 1-2 of the biggest pieces cook off (small explosions)
    ///   ..                COOL   - break edges and the inside of the shell glow blackbody white -> orange -> red -> off;
    ///                              the biggest pieces drag ember streaks while hot
    ///   end               pieces shrink away (space: drift; with gravity: bounce and settle on the floor first)
    public static class EnemyShatter
    {
        static ShatterLibrary _lib;
        public static ShatterLibrary Library
        {
            get { if (!_lib) _lib = Resources.Load<ShatterLibrary>("OYM_ShatterLibrary"); return _lib; }
            set => _lib = value;
        }

        /// Gravity used by Detonate(): zero in space maps; set Physics.gravity for grounded arenas.
        public static Vector3 DefaultGravity = Vector3.zero;

        internal static readonly List<ShatterFX> Active = new List<ShatterFX>();
        static readonly List<MeshFilter> s_filters = new List<MeshFilter>();
        static readonly List<ShatterFX.Src> s_srcs = new List<ShatterFX.Src>();

        /// Breaks `enemyRoot` (any of its MeshFilters with baked shards) and hides its renderers.
        /// dir = direction of the killing shot (world). Returns false when nothing under the root has shards.
        public static bool Detonate(GameObject enemyRoot, Vector3 dir, float power = 1f, bool withExplosion = true)
            => Play(enemyRoot, dir, power, withExplosion, DefaultGravity, true) != null;

        public static ShatterFX Play(GameObject root, Vector3 dir, float power, bool withExplosion, Vector3 gravity, bool hideSource)
        {
            var lib = Library;
            if (!root || !lib) return null;
            s_filters.Clear(); s_srcs.Clear();
            root.GetComponentsInChildren(false, s_filters);
            foreach (var f in s_filters)
            {
                if (!f || !f.sharedMesh) continue;
                var r = f.GetComponent<MeshRenderer>();
                if (!r || !r.enabled) continue;
                if (lib.TryGet(f.sharedMesh, out var shards, out int src)) s_srcs.Add(new ShatterFX.Src { shards = shards, source = src, renderer = r });
            }
            if (s_srcs.Count == 0) return null;

            if (dir.sqrMagnitude < 1e-6f) dir = root.transform.forward;
            var fx = ShatterFX.Get();
            fx.Begin(s_srcs, lib, dir.normalized, Mathf.Max(0.1f, power), withExplosion, gravity, root.transform);
            if (hideSource) Hide(root);
            s_srcs.Clear();
            return fx;
        }

        /// Hides every renderer under root. The renderers come back by themselves when the root is deactivated
        /// (pooled enemies) or with Restore(root).
        public static void Hide(GameObject root)
        {
            var mark = root.GetComponent<ShatterHidden>();
            if (!mark) mark = root.AddComponent<ShatterHidden>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && !(r is ParticleSystemRenderer)) { r.enabled = false; mark.hidden.Add(r); }
        }

        public static void Restore(GameObject root)
        {
            if (root && root.TryGetComponent<ShatterHidden>(out var mark)) mark.RestoreNow();
        }

        /// Edit-mode / batch capture: advance every live shatter (and the HitFX it spawned) by hand.
        public static void TickAll(float dt)
        {
            for (int i = Active.Count - 1; i >= 0; i--) if (i < Active.Count && Active[i]) Active[i].Tick(dt);
        }

        /// Ends every live shatter now (scene reset / capture cleanup).
        public static void StopAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--) if (i < Active.Count && Active[i]) Active[i].End();
            Active.Clear();
        }
    }
}
