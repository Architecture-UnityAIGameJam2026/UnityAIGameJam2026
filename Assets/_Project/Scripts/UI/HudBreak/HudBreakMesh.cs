using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>살아 있는 모듈(원본 그래픽)에 거는 피해 글리치 · 재부팅 값. HudBreak가 매 프레임 채운다.</summary>
    public sealed class LiveBreak
    {
        public bool active, hidden;
        public Vector2 offset;          // px: 충격 흔들림 · 떨림
        public float tear;              // px: 띠별 가로 찢김
        public float split;             // px: 따뜻한 색 분리 간격 (교환 글리치의 적 · 청 분리와 구분)
        public float splitHeat;         // 0..1: 분리 사본 색 (흰색 → 주황 → 빨강으로 식는다)
        public float dropout;           // 0..1: 띠가 꺼질 확률 (신호 손실)
        public float alpha = 1f;
        public float flash;             // 0..1: flashColor 쪽으로 (밝은 선만)
        public Color32 flashColor = new Color32(255, 255, 255, 255);
        /// <summary>모듈 공간 y. 재부팅 스캔: 이보다 아래(더 음수)인 삼각형은 아직 켜지지 않았다.</summary>
        public float revealY = float.NegativeInfinity;
        public float scanGlow;          // 0..1: 스캔 선 바로 위를 밝힌다
        public float bandHeight = 5f;
        public int seed, step;

        /// <summary>흔들림 · 숨김 값을 평소로 (active는 그대로).</summary>
        public void Reset()
        {
            hidden = false; offset = Vector2.zero; tear = 0f; split = 0f; splitHeat = 0f; dropout = 0f; alpha = 1f; flash = 0f;
            revealY = float.NegativeInfinity; scanGlow = 0f;
        }
    }

    /// <summary>
    /// 모듈 고스트(뜨거운 HDR 머티리얼을 쓰는 복제본)의 잔상 값. 맞는 순간 밝은 선이 하얗게 타고(HDR),
    /// 찢긴 채 옆으로 밀린 잔상이 흑체색(흰색 → 주황 → 빨강)으로 식으며 사라진다.
    /// </summary>
    public sealed class GhostBreak
    {
        public bool active;
        public Vector2 offset;
        public float tear, alpha = 1f;
        public float heat;              // 0..1 흑체색
        public float flash;             // 0..1 흰색 쪽으로
        public Color flashColor = new Color(1f, 0.93f, 0.82f);
        public float bandHeight = 6f, dropout = 0.3f;
        public bool gammaVertex = true;
        public int seed, step;
    }

    /// <summary>
    /// UGUI 메쉬 이펙트 (HudGlitchMesh와 같은 방식). 두 가지로 쓴다.
    /// - Live: 원본 모듈 그래픽을 흔들고, 띠를 찢고, 따뜻한 색으로 분리하고, 띠를 끄고, 숨기고, 위에서부터 스캔하며 다시 켠다.
    /// - Ghost: 고스트 그래픽의 밝은 선만 HDR 흰색 → 흑체색 잔상으로 그린다 (띠별로 찢기고 듬성듬성 끊긴다).
    /// 둘 다 꺼져 있으면 아무것도 하지 않으므로 평소 비용은 0이다. 리스트는 ListPool에서 빌려 프레임당 할당이 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudBreakMesh : BaseMeshEffect
    {
        [System.NonSerialized] public LiveBreak Live;
        [System.NonSerialized] public GhostBreak Ghost;
        /// <summary>그래픽 로컬 → 모듈 로컬 이동 (HUD 트리는 모듈 안에서 회전 · 비균등 스케일이 없다).</summary>
        [System.NonSerialized] public Vector2 ToModule;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var g = Ghost;
            if (g != null) { if (g.active) Afterimage(vh, g); else vh.Clear(); return; }
            var p = Live;
            if (p != null && p.active) Disturb(vh, p);
        }

        // ------------------------------------------------------------------------------------------- live

        void Disturb(VertexHelper vh, LiveBreak p)
        {
            if (p.hidden || p.alpha <= 0.003f) { vh.Clear(); return; }
            var src = ListPool<UIVertex>.Get();
            var dst = ListPool<UIVertex>.Get();
            vh.GetUIVertexStream(src);
            int n = src.Count - src.Count % 3;
            bool split = p.split > 0.25f;
            if (dst.Capacity < n * 3) dst.Capacity = n * 3;
            float inv = 1f / Mathf.Max(1f, p.bandHeight);
            bool scanning = !float.IsNegativeInfinity(p.revealY);
            Color32 warm = default, deep = default;
            if (split)
            {
                // 분리 사본: 한쪽은 흰색 → 주황, 반대쪽은 한 단계 식은 빨강 (평소 밝기 머티리얼이라 HDR은 아니다)
                warm = Blackbody32(Mathf.Lerp(0.55f, 1f, p.splitHeat));
                deep = Blackbody32(Mathf.Lerp(0.3f, 0.62f, p.splitHeat));
            }

            for (int pass = split ? 0 : 2; pass < 3; pass++)
            {
                float so = pass == 0 ? -p.split : pass == 1 ? p.split : 0f;
                for (int i = 0; i < n; i += 3)
                {
                    UIVertex a = src[i], b = src[i + 1], c = src[i + 2];
                    float cy = (a.position.y + b.position.y + c.position.y) * (1f / 3f) + ToModule.y;
                    if (scanning && cy < p.revealY) continue;   // 아직 켜지지 않은 아래쪽
                    // 어두운 면(패널 유리 · 지도판)은 찢지 않고 통째로만 흔든다: 선 · 숫자만 글리치하고 판은 버틴다
                    bool fill = Lit(a.color) < 0.08f;
                    if (fill && pass < 2) continue;
                    int band = Mathf.FloorToInt(-cy * inv);
                    if (!fill && p.dropout > 0f && HudGlitchMesh.Hash(p.seed + 53, p.step, band) < p.dropout) continue;

                    float h = HudGlitchMesh.Hash(p.seed, p.step, band) * 2f - 1f;
                    float tear = fill ? 0f : HudGlitchMesh.Hash(p.seed + 17, p.step, band) < 0.4f ? h * p.tear : h * p.tear * 0.1f;
                    float dx = p.offset.x + tear + so;
                    if (pass < 2)
                    {
                        var tint = pass == 0 ? warm : deep;
                        Tint(ref a, dx, p.offset.y, tint, p.alpha); Tint(ref b, dx, p.offset.y, tint, p.alpha); Tint(ref c, dx, p.offset.y, tint, p.alpha);
                    }
                    else
                    {
                        float scan = scanning ? p.scanGlow * Mathf.Clamp01(1f - (cy - p.revealY) / 9f) : 0f;
                        float flash = Mathf.Clamp01(p.flash + scan);
                        Shift(ref a, dx, p.offset.y, flash, p); Shift(ref b, dx, p.offset.y, flash, p); Shift(ref c, dx, p.offset.y, flash, p);
                    }
                    dst.Add(a); dst.Add(b); dst.Add(c);
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
            ListPool<UIVertex>.Release(src);
            ListPool<UIVertex>.Release(dst);
        }

        static void Shift(ref UIVertex v, float dx, float dy, float flash, LiveBreak p)
        {
            v.position.x += dx;
            v.position.y += dy;
            var col = v.color;
            if (flash > 0f)
            {
                float f = flash * Lit(col);
                col.r = (byte)(col.r + (p.flashColor.r - col.r) * f);
                col.g = (byte)(col.g + (p.flashColor.g - col.g) * f);
                col.b = (byte)(col.b + (p.flashColor.b - col.b) * f);
            }
            col.a = (byte)(col.a * Mathf.Clamp01(p.alpha));
            v.color = col;
        }

        static void Tint(ref UIVertex v, float dx, float dy, Color32 tint, float alpha)
        {
            v.position.x += dx;
            v.position.y += dy;
            var col = v.color;
            // 밝은 선 · 숫자만 갈라진다: 큰 어두운 면(지도 유리)이 색 블록으로 덮이지 않게
            v.color = new Color32(tint.r, tint.g, tint.b, (byte)(col.a * Mathf.Clamp01(alpha) * 0.55f * (0.1f + 0.9f * Lit(col))));
        }

        /// <summary>밝은 선 · 숫자는 1, 어두운 면(패널 유리)은 0에 가깝다.</summary>
        static float Lit(Color32 c)
        {
            float lum = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * (1f / 255f);
            float lit = Mathf.Clamp01((lum - 0.15f) / 0.45f);
            return lit * lit * (3f - 2f * lit);
        }

        // ------------------------------------------------------------------------------------------- ghost (HDR afterimage)

        void Afterimage(VertexHelper vh, GhostBreak g)
        {
            if (g.alpha <= 0.003f) { vh.Clear(); return; }
            var src = ListPool<UIVertex>.Get();
            var dst = ListPool<UIVertex>.Get();
            vh.GetUIVertexStream(src);
            int n = src.Count - src.Count % 3;
            if (dst.Capacity < n) dst.Capacity = n;
            float inv = 1f / Mathf.Max(1f, g.bandHeight);
            // 흰색(섬광) → 흑체색(식는 잔상). 고스트 머티리얼 발광 배수만큼 HDR로 빛난다
            Color lin = Color.Lerp(Blackbody(g.heat) * (0.3f + 0.7f * g.heat), g.flashColor, Mathf.Clamp01(g.flash));
            for (int i = 0; i < n; i += 3)
            {
                UIVertex a = src[i], b = src[i + 1], c = src[i + 2];
                float lit = Lit(a.color);
                if (lit < 0.08f) continue;   // 어두운 면은 잔상을 남기지 않는다 (밝은 선 · 숫자만 탄다)
                float cy = (a.position.y + b.position.y + c.position.y) * (1f / 3f) + ToModule.y;
                int band = Mathf.FloorToInt(-cy * inv);
                if (g.dropout > 0f && HudGlitchMesh.Hash(g.seed + 29, g.step, band) < g.dropout * (1f - g.flash)) continue;
                float dx = g.offset.x + (HudGlitchMesh.Hash(g.seed + 7, g.step, band) * 2f - 1f) * g.tear;
                var col = Encode(lin, a.color.a * (1f / 255f) * g.alpha * lit, g.gammaVertex);
                a.position.x += dx; b.position.x += dx; c.position.x += dx;
                a.position.y += g.offset.y; b.position.y += g.offset.y; c.position.y += g.offset.y;
                a.color = col; b.color = col; c.color = col;
                dst.Add(a); dst.Add(b); dst.Add(c);
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
            ListPool<UIVertex>.Release(src);
            ListPool<UIVertex>.Release(dst);
        }

        /// <summary>선형 색 → 정점 색. 선형 프로젝트에서 캔버스가 sRGB→선형으로 바꾸면(gamma) 감마로 넣는다.</summary>
        public static Color32 Encode(Color lin, float a, bool gamma)
        {
            float r = Mathf.Clamp01(lin.r), g = Mathf.Clamp01(lin.g), b = Mathf.Clamp01(lin.b);
            if (gamma) { r = Mathf.LinearToGammaSpace(r); g = Mathf.LinearToGammaSpace(g); b = Mathf.LinearToGammaSpace(b); }
            return new Color32((byte)(r * 255f + 0.5f), (byte)(g * 255f + 0.5f), (byte)(b * 255f + 0.5f), (byte)(Mathf.Clamp01(a) * 255f + 0.5f));
        }

        static Color32 Blackbody32(float h)
        {
            var c = Blackbody(h);
            return new Color32((byte)(Mathf.LinearToGammaSpace(c.r) * 255f), (byte)(Mathf.LinearToGammaSpace(c.g) * 255f), (byte)(Mathf.LinearToGammaSpace(c.b) * 255f), 255);
        }

        /// <summary>흑체 비슷한 색 (단위 밝기, 선형): 0 검정 → 짙은 빨강 → 주황 → 노랑 → 1 흰색. OYM.ShatterFX.Blackbody와 같은 곡선.</summary>
        public static Color Blackbody(float h)
        {
            h = Mathf.Clamp01(h);
            if (h < 0.25f) return Color.Lerp(new Color(0f, 0f, 0f), new Color(0.55f, 0.03f, 0f), h / 0.25f);
            if (h < 0.5f) return Color.Lerp(new Color(0.55f, 0.03f, 0f), new Color(1f, 0.22f, 0.02f), (h - 0.25f) / 0.25f);
            if (h < 0.75f) return Color.Lerp(new Color(1f, 0.22f, 0.02f), new Color(1f, 0.58f, 0.16f), (h - 0.5f) / 0.25f);
            return Color.Lerp(new Color(1f, 0.58f, 0.16f), new Color(1f, 0.93f, 0.8f), (h - 0.75f) / 0.25f);
        }
    }
}
