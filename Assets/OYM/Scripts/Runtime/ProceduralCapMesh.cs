using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM
{
    /// <summary>
    /// Spherical cap around +Y (radius r, out to capDeg from the pole), built on load instead of being stored in the
    /// scene. The BossIntro planet/atmosphere caps are ~1.4M vertices; serialized into the scene they made it 128 MB
    /// (over GitHub's 100 MB file limit). Rings bunch up towards the rim so the limb stays round at 1080p.
    /// Meshes are shared per parameter set, reference counted (freed when the last user is disabled) and never
    /// saved (HideFlags.DontSave), so the scene file only keeps these few numbers.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter))]
    public sealed class ProceduralCapMesh : MonoBehaviour
    {
        public string meshName = "cap";
        public float radius = 1f;
        [Range(0.1f, 180f)] public float capDeg = 20f;
        [Min(1)] public int rings = 64;
        [Min(3)] public int segments = 256;

        readonly struct Key : System.IEquatable<Key>
        {
            readonly float r, deg; readonly int rings, segs;
            public Key(float r, float deg, int rings, int segs) { this.r = r; this.deg = deg; this.rings = rings; this.segs = segs; }
            public bool Equals(Key o) => r == o.r && deg == o.deg && rings == o.rings && segs == o.segs;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => System.HashCode.Combine(r, deg, rings, segs);
        }

        sealed class Entry { public Mesh mesh; public int refs; }
        static readonly Dictionary<Key, Entry> Shared = new();

        Key _held;
        bool _holding;

        void OnEnable() => Acquire();
        void OnDisable() => Release();

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Release();
            Acquire();
        }
#endif

        void Acquire()
        {
            if (_holding) return;
            rings = Mathf.Max(1, rings); segments = Mathf.Max(3, segments);
            _held = new Key(radius, capDeg, rings, segments);
            if (!Shared.TryGetValue(_held, out var e) || !e.mesh)
            {
                e = new Entry { mesh = Build(meshName, radius, capDeg, rings, segments) };
                e.mesh.hideFlags = HideFlags.DontSave;
                Shared[_held] = e;
            }
            e.refs++;
            _holding = true;
            GetComponent<MeshFilter>().sharedMesh = e.mesh;
        }

        void Release()
        {
            if (!_holding) return;
            _holding = false;
            if (!Shared.TryGetValue(_held, out var e)) return;
            if (--e.refs > 0) return;
            Shared.Remove(_held);
            if (!e.mesh) return;
            if (Application.isPlaying) Destroy(e.mesh); else DestroyImmediate(e.mesh);
        }

        /// <summary>Same layout and winding as the old OYMBossIntro.CapMesh (and OYMSetup.SphereMesh).</summary>
        public static Mesh Build(string name, float r, float capDeg, int rings, int segs)
        {
            var v = new Vector3[(rings + 1) * (segs + 1)];
            var nrm = new Vector3[v.Length];
            float tmax = capDeg * Mathf.Deg2Rad;
            for (int i = 0; i <= rings; i++)
            {
                float u = (float)i / rings, th = tmax * (1f - (1f - u) * (1f - u));
                float st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int j = 0; j <= segs; j++)
                {
                    float ph = 2f * Mathf.PI * j / segs;
                    var d = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));
                    v[i * (segs + 1) + j] = d * r; nrm[i * (segs + 1) + j] = d;
                }
            }
            var tri = new int[rings * segs * 6];
            int k = 0;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = i * (segs + 1) + j, c = a + segs + 1;
                    tri[k++] = a; tri[k++] = a + 1; tri[k++] = c;
                    tri[k++] = a + 1; tri[k++] = c + 1; tri[k++] = c;
                }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.vertices = v; m.normals = nrm; m.triangles = tri;
            m.RecalculateBounds();
            m.UploadMeshData(true);   // static geometry: drop the CPU copy (~40 MB for the planet cap)
            return m;
        }
    }
}
