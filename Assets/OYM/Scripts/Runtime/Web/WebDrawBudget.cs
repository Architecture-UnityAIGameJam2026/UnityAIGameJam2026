using UnityEngine;

namespace OYM.Web
{
    /// Web build (URP) only: draw-call budget for the environment objects FogCulling tracks.
    ///
    /// Under HDRP the GPU Resident Drawer draws the ~2,400 debris / wreck renderers in a handful of instanced batches
    /// and its small-mesh culling drops sub-pixel pieces. WebGL 2 has neither, so every renderer is its own draw call
    /// in every pass, and the wreck LODs' lowest level (never culled, see WreckLods) keeps even specks on screen.
    /// FogCulling calls in here while it scans a scene (play mode, URP only), so the authored scenes and the HDRP
    /// build stay untouched:
    ///   - LOD groups get a cull height: below <see cref="LodCullHeight"/> of the screen the object is not drawn
    ///   - fog culling drops objects at <see cref="CullPixelsScale"/> x the volume's cullPixels
    public static class WebDrawBudget
    {
        /// screen-relative height under which an environment LOD group culls itself (~6 px at 1080p)
        public const float LodCullHeight = 0.006f;
        /// fog culling's cullPixels multiplier (1.5 px authored -> ~4 px)
        public const float CullPixelsScale = 2.5f;

        static int s_Checked = -1;
        static bool s_Active;

        /// URP in a player / play mode (edit-mode tools must not change the scene's objects)
        public static bool Active
        {
            get
            {
                int f = Time.frameCount;
                if (s_Checked != f) { s_Checked = f; s_Active = Application.isPlaying && WebLightAdapter.IsUrp; }
                return s_Active;
            }
        }

        /// Gives the group a cull height (its lowest LOD stops drawing below LodCullHeight). Returns the LODs in use.
        public static LOD[] TuneLods(LODGroup g, LOD[] lods)
        {
            if (!Active || lods.Length == 0) return lods;
            int last = lods.Length - 1;
            float prev = last > 0 ? lods[last - 1].screenRelativeTransitionHeight : 1f;
            float h = Mathf.Min(LodCullHeight, prev * 0.5f);   // keep the heights strictly decreasing
            if (lods[last].screenRelativeTransitionHeight >= h) return lods;
            lods[last].screenRelativeTransitionHeight = h;
            g.SetLODs(lods);
            return lods;
        }

        public static float CullPixels(float authored) => Active ? authored * CullPixelsScale : authored;
    }
}
