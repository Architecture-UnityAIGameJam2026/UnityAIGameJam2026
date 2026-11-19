using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>
    /// 게임 HUD 배치 (오버워치식). 레이아웃 JSON · 프리팹은 그대로 두고 런타임에 그룹 위치만 옮긴다
    /// (프리팹을 다시 굽으면 Arena 씬의 오버라이드 — 지도 RawImage 등 — 가 깨지기 때문).
    ///   왼쪽 위: 동그라미 미니맵 + 목표
    ///   왼쪽 아래: 체력 바, 그 위에 교환 · 지원 슬롯과 메뉴, 체력 오른쪽에 교환 요청 · 위기 · 각성 알림
    ///   오른쪽 아래: [Q 스킬][E 스킬][무기 · 탄약] 한 줄 (스킬 칸은 HudSkillPanel)
    /// 그룹 경로("Vitals/Health bar")는 바꾸지 않으므로 OnYourMarkHud · 스왑 글리치 · 고장 연출이 그대로 동작한다.
    /// HudPerspective(3D 기울기)는 Start에서 이 배치를 기준으로 모서리를 나눈다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class HudArrangement : MonoBehaviour
    {
        [Serializable]
        public struct Place
        {
            public string group;
            [Tooltip("1920×1080 기준 좌상단 (x, 아래로 y)")]
            public Vector2 position;
            public Place(string g, float x, float y) { group = g; position = new Vector2(x, y); }
        }

        [SerializeField] OnYourMarkHud hud;
        [SerializeField] Place[] places =
        {
            new Place("Spatial", 48f, 40f),
            new Place("Objective", 48f, 302f),
            new Place("Vitals", 48f, 964f),
            new Place("ToggleChannels", 48f, 872f),
            new Place("ToggleSupport", 128f, 872f),
            new Place("QuickShield", 208f, 872f),
            new Place("QuickScan", 288f, 872f),
            new Place("Toast", 368f, 888f),
            new Place("Channels", 48f, 704f),
            new Place("SupportWheel", 48f, 704f),
            new Place("Channel0", 68f, 772f),
            new Place("Support0", 68f, 772f),
            new Place("Channel1", 164f, 772f),
            new Place("Support1", 164f, 772f),
            new Place("Channel2", 260f, 772f),
            new Place("Support2", 260f, 772f),
            new Place("CloseTray", 300f, 708f),
            new Place("Exchange", 500f, 970f),
            new Place("CancelExchange", 728f, 980f),
            new Place("Critical", 500f, 978f),
            new Place("Awakened", 500f, 974f),
        };

        [Header("동그라미 미니맵")]
        [SerializeField] bool circularMap = true;
        [SerializeField] string mapGroup = "Spatial";
        [Tooltip("모듈 안 원 중심 (좌상단 기준, 아래로 y)")]
        [SerializeField] Vector2 mapCenter = new Vector2(120f, 125f);
        [SerializeField] float mapRadius = 116f;
        [Tooltip("원으로 바꾸면서 끄는 사각 테두리 · 유리")]
        [SerializeField] string[] mapHide = { "Map glass", "Map contour", "Map inner edge", "Map footer" };
        [Tooltip("원 밖에 두는 장식 (이름 앞부분). 나머지 지도 내용은 원으로 잘린다")]
        [SerializeField] string[] mapChrome = { "Ownership icon", "Connection", "North arrow" };

        bool _done;

        void Reset() => hud = GetComponent<OnYourMarkHud>();

        void Awake()
        {
            if (!hud) hud = GetComponent<OnYourMarkHud>();
            Apply();
        }

        /// <summary>한 번만 적용한다 (런타임 전용, 저장되지 않음).</summary>
        public void Apply()
        {
            if (_done || !Application.isPlaying || !hud) return;
            _done = true;
            var root = hud.transform;
            for (int i = 0; i < places.Length; i++)
            {
                if (root.Find(places[i].group) is RectTransform rt)
                    rt.anchoredPosition = new Vector2(places[i].position.x, -places[i].position.y);
            }
            if (circularMap && root.Find(mapGroup) is RectTransform map) MakeCircle(map);
        }

        void MakeCircle(RectTransform map)
        {
            var graphics = map.GetComponentsInChildren<Graphic>(true);
            var palette = JsonUtility.FromJson<HudLayout>(hud.layoutAsset.text)?.colors;
            Color ink = Parse(palette?.ink, new Color32(0x14, 0x13, 0x22, 255));
            Color primary = Parse(palette?.primary, new Color32(0xB7, 0xA6, 0xF6, 255));
            ink.a = 0.55f; primary.a = 0.8f;

            foreach (var g in graphics)
            {
                string n = g.gameObject.name;
                if (Array.IndexOf(mapHide, n) >= 0) { g.gameObject.SetActive(false); continue; }
                if (IsChrome(n)) continue;
                var clip = g.GetComponent<HudCircleClip>();
                if (!clip) clip = g.gameObject.AddComponent<HudCircleClip>();
                clip.hideFlags = HideFlags.DontSave;
                clip.module = map;
                clip.center = new Vector2(mapCenter.x, -mapCenter.y);
                clip.radius = mapRadius - 1f;
                g.SetVerticesDirty();
            }

            var size = map.rect.size;
            var disc = Geometry("Map disc", map, size, "polygon", Circle(mapCenter, mapRadius, 72, false), 1f, ink);
            disc.transform.SetAsFirstSibling();
            var rim = Geometry("Map rim", map, size, "line", Circle(mapCenter, mapRadius, 96, true), 1.2f, primary);
            rim.transform.SetSiblingIndex(1);
        }

        bool IsChrome(string n)
        {
            for (int i = 0; i < mapChrome.Length; i++) if (n.StartsWith(mapChrome[i], StringComparison.Ordinal)) return true;
            return false;
        }

        HudGeometry Geometry(string name, RectTransform parent, Vector2 size, string shape, float[] points, float stroke, Color c)
        {
            var rt = OnYourMarkHud.Rect(name, parent, 0f, 0f, size.x, size.y);
            rt.gameObject.hideFlags = HideFlags.DontSave;
            rt.gameObject.layer = parent.gameObject.layer;
            var g = rt.gameObject.AddComponent<HudGeometry>();
            g.shape = shape; g.points = points; g.thickness = stroke;
            g.color = c; g.raycastTarget = false;
            g.material = hud.hologramMaterial;
            return g;
        }

        /// <summary>HudGeometry 점 배열 (좌상단 기준, 아래로 y).</summary>
        static float[] Circle(Vector2 c, float r, int n, bool closed)
        {
            int count = closed ? n + 1 : n;
            var p = new float[count * 2];
            for (int i = 0; i < count; i++)
            {
                float a = (i % n) / (float)n * Mathf.PI * 2f;
                p[i * 2] = c.x + Mathf.Cos(a) * r;
                p[i * 2 + 1] = c.y + Mathf.Sin(a) * r;
            }
            return p;
        }

        static Color Parse(string html, Color fallback) =>
            !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var c) ? c : fallback;
    }
}
