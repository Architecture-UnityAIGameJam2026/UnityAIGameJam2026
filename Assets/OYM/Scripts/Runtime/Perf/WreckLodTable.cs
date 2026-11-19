using UnityEngine;

namespace OYM
{
    /// Pre-baked LOD meshes for the wreck fragments (Resources/OYM_WreckLods), keyed by the LOD0 mesh name.
    /// Made by perf_tools/wreck_lods (Blender collapse decimation of Generated/Wreck/frag_NNNN.oymmesh to 25 % / 6 %
    /// into Generated/WreckLod, then make_table.py). Applied at runtime by WreckLods.
    public sealed class WreckLodTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string name;
            public Mesh lod1;
            public Mesh lod2;
        }

        public Entry[] entries;
    }
}
