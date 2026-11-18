using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Sword slash arc: a ribbon between the blade base and tip, sampled every frame while `emitting` (Catmull-Rom
    /// subdivided so a fast, snapped cut still reads as a clean arc at 25-30 fps), fading out over `life` seconds.
    /// Screen-relative HDR (OYM/FX/AdditiveHDR): white-hot along the edge, tinted toward the base, blooms in any map.
    [DefaultExecutionOrder(300)]
    public class BladeTrail : MonoBehaviour
    {
        public Transform tipSocket, baseSocket;
        public Material material;
        public float life = 0.13f;
        public int subdiv = 5;
        public Color color = new Color(0.72f, 0.93f, 1f);
        public bool emitting;

        struct S { public Vector3 a, b; public float t; }
        readonly List<S> _s = new List<S>();
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _tri = new List<int>();
        Mesh _mesh;
        GameObject _go;
        Vector3 _lastTip;

        /// World-space tip movement of the last frame (slash direction for hit impulses).
        public Vector3 TipDelta { get; private set; }

        void OnEnable()
        {
            if (!_go)
            {
                _go = new GameObject(name + "_Trail");
                _go.hideFlags = HideFlags.DontSave;
                _mesh = new Mesh { name = "BladeTrail" }; _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                _go.layer = gameObject.layer;
            }
            _go.SetActive(true);
            _s.Clear();
            if (tipSocket) _lastTip = tipSocket.position;
        }

        void OnDisable() { if (_go) _go.SetActive(false); }
        void OnDestroy() { if (_go) Destroy(_go); }

        void LateUpdate()
        {
            if (!tipSocket || !baseSocket) return;
            float now = Time.time;
            TipDelta = tipSocket.position - _lastTip; _lastTip = tipSocket.position;
            if (emitting) _s.Add(new S { a = baseSocket.position, b = tipSocket.position, t = now });
            while (_s.Count > 0 && now - _s[0].t > life) _s.RemoveAt(0);
            Build(now);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        void Build(float now)
        {
            _v.Clear(); _c.Clear(); _uv.Clear(); _tri.Clear();
            int n = _s.Count;
            if (n >= 2)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    S s0 = _s[Mathf.Max(i - 1, 0)], s1 = _s[i], s2 = _s[i + 1], s3 = _s[Mathf.Min(i + 2, n - 1)];
                    int steps = i == n - 2 ? subdiv + 1 : subdiv;
                    for (int k = 0; k < steps; k++)
                    {
                        float f = k / (float)subdiv;
                        Vector3 a = CR(s0.a, s1.a, s2.a, s3.a, f), b = CR(s0.b, s1.b, s2.b, s3.b, f);
                        float age = Mathf.Clamp01((now - Mathf.Lerp(s1.t, s2.t, f)) / life);
                        float alpha = (1f - age) * (1f - age);
                        var col = new Color(color.r, color.g, color.b, alpha);
                        _v.Add(a); _v.Add(b); _c.Add(col); _c.Add(col);
                        _uv.Add(new Vector2(age, 0f)); _uv.Add(new Vector2(age, 1f));
                    }
                }
                for (int i = 0; i + 3 < _v.Count; i += 2)
                {
                    _tri.Add(i); _tri.Add(i + 1); _tri.Add(i + 2);
                    _tri.Add(i + 2); _tri.Add(i + 1); _tri.Add(i + 3);
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_tri, 0);
            _mesh.RecalculateBounds();
        }
    }
}
