using UnityEngine;

namespace OYM.Web
{
    /// The emission colour property for code that tints materials at runtime: HDRP Lit's _EmissiveColor, or URP Lit's
    /// _EmissionColor in the web build. Abs scales an absolute HDRP emissive value (nits) into URP display range.
    public static class Emissive
    {
        public static int Id => WebLightAdapter.IsUrp ? EmissionId : EmissiveIdHd;
        public static float Abs => WebLightAdapter.IsUrp ? WebLightAdapter.EmissionScale : 1f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveIdHd = Shader.PropertyToID("_EmissiveColor");
    }
}
