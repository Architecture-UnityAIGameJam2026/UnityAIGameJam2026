using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Where the CITADEL's red sensor lights are, per part (baked by OYM.EditorTools.OYMJevMark.BakeWeakPoints from the
    /// parts' emissive maps sampled through their mesh UVs). Loaded from Resources/OYM_CitadelWeakPoints.
    public class CitadelWeakPointMap : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("transform name under the boss root, e.g. citadel_head, citadel_claw_L1")] public string part;
            [Tooltip("glow-weighted centre of the red lights, part space")] public Vector3 center;
            [Tooltip("mean facing of the glowing faces, part space")] public Vector3 normal = Vector3.forward;
            [Tooltip("spread of the glow round the centre, part space")] public float radius = 1f;
            [Tooltip("summed glow (area x brightness): ordering only")] public float strength;
            [Tooltip("glow clusters, part space, brightest first: the hit point and the blasts go there")] public Vector3[] spots = new Vector3[0];
        }

        public List<Entry> entries = new List<Entry>();
        public string source;
    }
}
