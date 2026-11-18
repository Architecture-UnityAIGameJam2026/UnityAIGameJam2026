using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// First-person sword swoosh: a crescent of screen-relative HDR light that sweeps across the view with each cut
    /// (reveal in ~70 ms, bright leading edge, thin tapered ends, then fades). Rides on the camera; the hit-stop freezes it.
    /// Play(0) = Slash_Left (wide horizontal, right -> left), Play(1) = Slash_Charged (big diagonal from the top right),
    /// Play(2) = Slash_Thrust (short rising diagonal).
    [DefaultExecutionOrder(260)]
    public class SlashArc : MonoBehaviour
    {
        public Material material;
        public Color color = new Color(0.78f, 0.95f, 1f);
        public float distance = 0.95f, width = 0.13f, reveal = 0.075f, fade = 0.2f;
        const int Seg = 48;

        struct Shape { public Vector2 center; public float radius, from, to, roll, pitch; }
        static readonly Shape[] Shapes =
        {
            new Shape { center = new Vector2(0.02f, -0.62f), radius = 0.78f, from = 18f, to = 162f, roll = 8f, pitch = -18f },
            new Shape { center = new Vector2(-0.05f, -0.30f), radius = 0.85f, from = 75f, to = 235f, roll = -32f, pitch = -10f },
            new Shape { center = new Vector2(0.10f, -0.70f), radius = 0.62f, from = 120f, to = 62f, roll = 40f, pitch = -24f },
        };

        GameObject _go;
        Mesh _mesh;
        float _t = -1f;
        Shape _s;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _tri = new List<int>();

        public void Play(int type)
        {
            _s = Shapes[Mathf.Clamp(type, 0, Shapes.Length - 1)];
            _t = 0f;
            if (!_go)
            {
                _go = new GameObject("FP_SlashArc") { hideFlags = HideFlags.DontSave };
                _mesh = new Mesh { name = "SlashArc" }; _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            _go.SetActive(true);
        }

        void OnDisable() { if (_go) _go.SetActive(false); _t = -1f; }
        void OnDestroy() { if (_go) Destroy(_go); }

        void LateUpdate()
        {
            if (_t < 0f || !_go) return;
            var cam = FlightCamera.Main;
            if (!cam) return;
            _t += Time.deltaTime;
            float prog = Mathf.Clamp01(_t / reveal);
            float alpha = _t < reveal ? 1f : 1f - (_t - reveal) / fade;
            if (alpha <= 0f) { _go.SetActive(false); _t = -1f; return; }
            Transform ct = cam.transform;
            _go.transform.SetPositionAndRotation(ct.position + ct.forward * distance,
                                                 ct.rotation * Quaternion.Euler(_s.pitch, 0f, _s.roll));
            float grow = 1f + 0.08f * Mathf.Clamp01(_t / (reveal + fade));
            _v.Clear(); _c.Clear(); _uv.Clear(); _tri.Clear();
            int n = Mathf.Max(2, Mathf.CeilToInt(Seg * prog));
            for (int i = 0; i <= n; i++)
            {
                float f = i / (float)Seg;
                if (f > prog) f = prog;
                float a = Mathf.Lerp(_s.from, _s.to, f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(f / Mathf.Max(prog, 1e-3f)))), 0.6f);   // sin(pi) < 0 -> NaN
                float head = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(prog - 0.75f, prog, f));   // bright leading edge, tail fades
                float r = _s.radius * grow, w = width * taper;
                Vector3 c = new Vector3(_s.center.x, _s.center.y, 0f);
                _v.Add(c + dir * (r - w * 0.5f)); _v.Add(c + dir * (r + w * 0.5f));
                var col = new Color(color.r, color.g, color.b, alpha * head);
                _c.Add(col); _c.Add(col);
                float u = 1f - head;
                _uv.Add(new Vector2(u, 0f)); _uv.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i + 3 < _v.Count; i += 2)
            {
                _tri.Add(i); _tri.Add(i + 2); _tri.Add(i + 1);
                _tri.Add(i + 1); _tri.Add(i + 2); _tri.Add(i + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_tri, 0);
            _mesh.RecalculateBounds();
        }
    }
}
