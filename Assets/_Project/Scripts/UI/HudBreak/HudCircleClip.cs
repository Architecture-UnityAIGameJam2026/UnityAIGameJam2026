using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>
    /// 그래픽의 삼각형을 모듈 공간의 원(다각형 근사) 안으로 잘라 낸다. 동그라미 미니맵용.
    /// 스텐실 마스크를 쓰지 않아 머티리얼이 그대로고, 글리치로 옆으로 밀린 띠도 잘리지 않는다
    /// (이 이펙트가 글리치 이펙트보다 먼저 붙어 있어 '제자리 모양'만 자른다). 복제(고스트)되면 module 참조도 복제본 루트로 따라간다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudCircleClip : BaseMeshEffect
    {
        public RectTransform module;
        [Tooltip("모듈 공간 중심 (좌상단 원점, 아래가 -y)")]
        public Vector2 center;
        public float radius = 100f;
        [Range(16, 128)] public int sides = 64;

        static System.Collections.Generic.List<UIVertex> s_a = new System.Collections.Generic.List<UIVertex>(16), s_b = new System.Collections.Generic.List<UIVertex>(16);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !module || radius <= 0f) return;
            Vector2 off = module.InverseTransformPoint(transform.TransformPoint(Vector3.zero));
            var src = ListPool<UIVertex>.Get();
            var dst = ListPool<UIVertex>.Get();
            vh.GetUIVertexStream(src);
            int n = src.Count - src.Count % 3;
            float inner = radius * Mathf.Cos(Mathf.PI / sides);   // 다각형에 내접하는 원: 이 안이면 자를 필요가 없다
            float in2 = inner * inner;
            for (int i = 0; i < n; i += 3)
            {
                UIVertex a = src[i], b = src[i + 1], c = src[i + 2];
                Vector2 pa = (Vector2)a.position + off - center, pb = (Vector2)b.position + off - center, pc = (Vector2)c.position + off - center;
                if (pa.sqrMagnitude <= in2 && pb.sqrMagnitude <= in2 && pc.sqrMagnitude <= in2) { dst.Add(a); dst.Add(b); dst.Add(c); continue; }
                Vector2 mn = Vector2.Min(pa, Vector2.Min(pb, pc)), mx = Vector2.Max(pa, Vector2.Max(pb, pc));
                if (mn.x > radius || mx.x < -radius || mn.y > radius || mx.y < -radius) continue;
                s_a.Clear(); s_a.Add(a); s_a.Add(b); s_a.Add(c);
                for (int k = 0; k < sides && s_a.Count >= 3; k++)
                {
                    float ang = (k + 0.5f) / sides * Mathf.PI * 2f;
                    var nrm = new Vector2(-Mathf.Cos(ang), -Mathf.Sin(ang));   // 안쪽을 향한 법선: dot(n, p - center) >= -inner
                    Clip(s_a, s_b, nrm, Vector2.Dot(nrm, center) - inner, off);
                    (s_a, s_b) = (s_b, s_a);
                }
                for (int k = 1; k + 1 < s_a.Count; k++) { dst.Add(s_a[0]); dst.Add(s_a[k]); dst.Add(s_a[k + 1]); }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
            ListPool<UIVertex>.Release(src);
            ListPool<UIVertex>.Release(dst);
        }

        /// <summary>Sutherland–Hodgman: dot(n, pos + off) >= c 쪽만 남긴다. 정점 속성은 보간한다.</summary>
        static void Clip(System.Collections.Generic.List<UIVertex> src, System.Collections.Generic.List<UIVertex> dst, Vector2 n, float c, Vector2 off)
        {
            dst.Clear();
            int cnt = src.Count;
            for (int i = 0; i < cnt; i++)
            {
                UIVertex A = src[i], B = src[(i + 1) % cnt];
                float da = Vector2.Dot(n, (Vector2)A.position + off) - c;
                float db = Vector2.Dot(n, (Vector2)B.position + off) - c;
                if (da >= 0f) dst.Add(A);
                if ((da >= 0f) != (db >= 0f)) dst.Add(Lerp(A, B, da / (da - db)));
            }
        }

        static UIVertex Lerp(UIVertex a, UIVertex b, float t) => new UIVertex
        {
            position = Vector3.LerpUnclamped(a.position, b.position, t),
            normal = Vector3.LerpUnclamped(a.normal, b.normal, t),
            tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t),
            color = Color32.LerpUnclamped(a.color, b.color, t),
            uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t),
            uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t),
            uv2 = Vector4.LerpUnclamped(a.uv2, b.uv2, t),
            uv3 = Vector4.LerpUnclamped(a.uv3, b.uv3, t),
        };
    }
}
