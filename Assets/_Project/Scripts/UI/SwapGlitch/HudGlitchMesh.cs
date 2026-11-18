using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>
    /// HUD 모듈 하나(또는 그 고스트)가 공유하는 글리치 값. HudSwapGlitch가 매 프레임 채우고,
    /// 같은 모듈의 모든 Graphic(HudGlitchMesh)이 이 값으로 메쉬를 흔든다.
    /// </summary>
    public sealed class GlitchParams
    {
        public bool active;
        public float tear;          // px: 띠(band)별 가로 찢김 최대치
        public float split;         // px: RGB 분리(빨강/청록 사본) 간격
        public float slide;         // px: 모듈 전체 가로 이동 (+ = 바깥쪽)
        public float stagger;       // 0..1: 띠마다 이동이 엇갈리는 정도 (인터레이스 도착)
        public float alpha = 1f;
        public float dropout;       // 0..1: 띠가 깜빡 사라질 확률
        public float flash;         // 0..1: flashColor 쪽으로 섞는 양
        public Color32 flashColor = new Color32(255, 255, 255, 255);
        public float bandHeight = 6f;
        public int seed, step;      // step이 바뀔 때마다 찢김 패턴이 새로 뽑힌다 (~30 Hz)
    }

    /// <summary>
    /// UGUI 메쉬 이펙트: 삼각형을 모듈 공간의 가로 띠로 나눠 밀고, RGB 분리 사본을 뒤에 깐다.
    /// Params가 비활성이면 아무것도 하지 않으므로 평소 비용은 0이다 (메쉬가 다시 만들어질 때 if 한 번).
    /// 리스트는 ListPool에서 빌려 쓰고 돌려줘 프레임당 할당이 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudGlitchMesh : BaseMeshEffect
    {
        /// <summary>HudSwapGlitch가 연결한다 (직렬화하지 않음).</summary>
        public GlitchParams Params;
        /// <summary>그래픽 로컬 → 모듈 로컬 위치 차이. HUD 트리는 회전·비균등 스케일이 없어 이동만 보면 된다.</summary>
        public Vector2 ToModule;

        static readonly Color32 SplitWarm = new Color32(255, 72, 98, 255);
        static readonly Color32 SplitCool = new Color32(72, 226, 255, 255);
        const float SplitAlpha = 0.55f;

        public override void ModifyMesh(VertexHelper vh)
        {
            var p = Params;
            if (p == null || !p.active || !IsActive()) return;

            var src = ListPool<UIVertex>.Get();
            var dst = ListPool<UIVertex>.Get();
            vh.GetUIVertexStream(src);
            int n = src.Count - src.Count % 3;
            if (dst.Capacity < n * 3) dst.Capacity = n * 3;

            if (p.split > 0.25f)
            {
                Emit(src, n, dst, p, -p.split, SplitWarm, true);
                Emit(src, n, dst, p, p.split, SplitCool, true);
            }
            Emit(src, n, dst, p, 0f, default, false);

            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
            ListPool<UIVertex>.Release(src);
            ListPool<UIVertex>.Release(dst);
        }

        void Emit(System.Collections.Generic.List<UIVertex> src, int n, System.Collections.Generic.List<UIVertex> dst,
                  GlitchParams p, float splitOffset, Color32 tint, bool isSplit)
        {
            float inv = 1f / Mathf.Max(1f, p.bandHeight);
            for (int i = 0; i < n; i += 3)
            {
                UIVertex a = src[i], b = src[i + 1], c = src[i + 2];
                // 삼각형 단위로 띠를 고른다: 글자·선분이 통째로 밀려 '슬라이스'처럼 읽힌다
                float cy = (a.position.y + b.position.y + c.position.y) * (1f / 3f) + ToModule.y;
                int band = Mathf.FloorToInt(-cy * inv);

                float alpha = p.alpha;
                if (p.dropout > 0f && Hash(p.seed + 53, p.step, band) < p.dropout) alpha = 0f;
                if (alpha <= 0.003f) continue;

                float h = Hash(p.seed, p.step, band) * 2f - 1f;
                float tear = Hash(p.seed + 17, p.step, band) < 0.42f ? h * p.tear : h * p.tear * 0.12f;
                float lag = 1f + p.stagger * (Hash(p.seed + 31, 0, band) * 2f - 1f);
                float dx = tear + p.slide * lag + splitOffset;

                Shift(ref a, dx, alpha, p, tint, isSplit);
                Shift(ref b, dx, alpha, p, tint, isSplit);
                Shift(ref c, dx, alpha, p, tint, isSplit);
                dst.Add(a); dst.Add(b); dst.Add(c);
            }
        }

        static void Shift(ref UIVertex v, float dx, float alpha, GlitchParams p, Color32 tint, bool isSplit)
        {
            v.position.x += dx;
            var col = v.color;
            // 밝은 선·숫자만 번쩍이고 갈라진다: 큰 어두운 면(지도 유리, 패널 바탕)이 색 블록으로 덮이지 않게
            float lum = (0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b) * (1f / 255f);
            float lit = Mathf.Clamp01((lum - 0.15f) / 0.45f);
            lit = lit * lit * (3f - 2f * lit);
            if (isSplit)
            {
                col = new Color32(tint.r, tint.g, tint.b, (byte)(col.a * alpha * SplitAlpha * (0.12f + 0.88f * lit)));
            }
            else
            {
                if (p.flash > 0f && lit > 0f)
                {
                    float f = Mathf.Clamp01(p.flash) * lit;
                    col.r = (byte)(col.r + (p.flashColor.r - col.r) * f);
                    col.g = (byte)(col.g + (p.flashColor.g - col.g) * f);
                    col.b = (byte)(col.b + (p.flashColor.b - col.b) * f);
                }
                col.a = (byte)(col.a * Mathf.Clamp01(alpha));
            }
            v.color = col;
        }

        /// <summary>정수 해시 → [0,1). 같은 (seed, step, band)는 같은 값이라 프레임 사이 패턴이 튀지 않는다.</summary>
        public static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }
    }
}
