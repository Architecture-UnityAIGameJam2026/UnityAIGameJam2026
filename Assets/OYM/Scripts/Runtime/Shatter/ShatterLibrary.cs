using System;
using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Resources/OYM_ShatterLibrary: every baked ShatterShards, looked up by source mesh.
    [CreateAssetMenu(menuName = "OYM/Shatter Library", fileName = "OYM_ShatterLibrary")]
    public class ShatterLibrary : ScriptableObject
    {
        public ShatterShards[] shards = Array.Empty<ShatterShards>();
        [Tooltip("Inside of the shell: HDRP/Lit charred metal, emission driven per piece (screen-relative)")]
        public Material interiorMaterial;
        [Tooltip("OYM/FX/ShatterHeat: break-edge glow")]
        public Material heatMaterial;
        [Tooltip("Ember streak behind the biggest pieces (OYM/FX/AdditiveHDR)")]
        public Material trailMaterial;

        Dictionary<Mesh, (ShatterShards shards, int source)> _byMesh;

        public bool TryGet(Mesh mesh, out ShatterShards s, out int source)
        {
            s = null; source = -1;
            if (!mesh) return false;
            if (_byMesh == null)
            {
                _byMesh = new Dictionary<Mesh, (ShatterShards, int)>();
                foreach (var sh in shards)
                {
                    if (!sh) continue;
                    for (int i = 0; i < sh.sources.Length; i++)
                        if (sh.sources[i] != null && sh.sources[i].mesh && !_byMesh.ContainsKey(sh.sources[i].mesh))
                            _byMesh.Add(sh.sources[i].mesh, (sh, i));
                }
            }
            if (!_byMesh.TryGetValue(mesh, out var hit)) return false;
            s = hit.shards; source = hit.source;
            return true;
        }

        void OnValidate() => _byMesh = null;
        void OnEnable() => _byMesh = null;
    }
}
